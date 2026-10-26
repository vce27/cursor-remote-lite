use serde::Serialize;
use std::fs;
use std::net::TcpStream;
use std::path::{Path, PathBuf};
use std::process::{Child, Command, Stdio};
use std::sync::Mutex;
use std::time::Duration;
use tauri::{
  menu::{Menu, MenuItem},
  tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent},
  AppHandle, Manager, State, WindowEvent,
};

struct RelayProc {
  child: Option<Child>,
}

#[derive(Serialize)]
struct AppStatus {
  relay_running: bool,
  relay_listening: bool,
  cdp_ports: Vec<u16>,
  relay_url: String,
  repo_root: String,
  port: u16,
}

#[derive(Serialize)]
struct PairInfo {
  link: String,
  minutes: u32,
  base: String,
}

fn data_dir() -> PathBuf {
  dirs_next::home_dir()
    .unwrap_or_else(|| PathBuf::from("."))
    .join(".cursor-remote-lite")
}

fn repo_root() -> PathBuf {
  if let Ok(p) = std::env::var("CURSOR_REMOTE_ROOT") {
    return PathBuf::from(p);
  }
  // desktop/src-tauri → repo root
  PathBuf::from(env!("CARGO_MANIFEST_DIR"))
    .join("../..")
    .canonicalize()
    .unwrap_or_else(|_| PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../.."))
}

fn read_json_file(path: &Path) -> serde_json::Value {
  fs::read_to_string(path)
    .ok()
    .and_then(|s| serde_json::from_str(&s).ok())
    .unwrap_or(serde_json::json!({}))
}

fn write_json_file(path: &Path, value: &serde_json::Value) -> Result<(), String> {
  if let Some(parent) = path.parent() {
    fs::create_dir_all(parent).map_err(|e| e.to_string())?;
  }
  fs::write(path, serde_json::to_string_pretty(value).unwrap_or_default() + "\n")
    .map_err(|e| e.to_string())
}

fn relay_url_from_disk() -> String {
  let cfg = read_json_file(&data_dir().join("config.json"));
  let st = read_json_file(&data_dir().join("state.json"));
  cfg
    .get("relayUrl")
    .or_else(|| st.get("relayUrl"))
    .and_then(|v| v.as_str())
    .unwrap_or("")
    .to_string()
}

fn listen_port() -> u16 {
  std::env::var("PORT")
    .ok()
    .and_then(|s| s.parse().ok())
    .unwrap_or(3939)
}

fn port_open(port: u16) -> bool {
  TcpStream::connect_timeout(
    &format!("127.0.0.1:{port}").parse().unwrap(),
    Duration::from_millis(200),
  )
  .is_ok()
}

fn probe_cdp(port: u16) -> bool {
  let url = format!("http://127.0.0.1:{port}/json/version");
  let Ok(resp) = ureq::get(&url).timeout(Duration::from_millis(400)).call() else {
    return false;
  };
  let Ok(body) = resp.into_string() else {
    return false;
  };
  body.contains("Cursor") || body.contains("Chrome")
}

fn scan_cdp_ports() -> Vec<u16> {
  let mut ports = vec![9222u16, 9333, 9223, 9224];
  if let Ok(p) = std::env::var("CDP_PORT").and_then(|s| s.parse().map_err(|_| std::env::VarError::NotPresent)) {
    ports.insert(0, p);
  }
  ports.sort_unstable();
  ports.dedup();
  ports.into_iter().filter(|&p| probe_cdp(p)).collect()
}

fn kill_listeners_on(port: u16) {
  // Best-effort: Windows — stop node listening on relay port.
  #[cfg(windows)]
  {
    let _ = Command::new("powershell")
      .args([
        "-NoProfile",
        "-Command",
        &format!(
          "Get-NetTCPConnection -LocalPort {port} -State Listen -EA SilentlyContinue | ForEach-Object {{ $p=Get-Process -Id $_.OwningProcess -EA SilentlyContinue; if($p -and $p.ProcessName -eq 'node'){{ Stop-Process -Id $p.Id -Force -EA SilentlyContinue }} }}"
        ),
      ])
      .stdout(Stdio::null())
      .stderr(Stdio::null())
      .status();
  }
}

#[tauri::command]
fn get_status(state: State<'_, Mutex<RelayProc>>) -> AppStatus {
  let mut guard = state.lock().unwrap();
  let child_alive = if let Some(child) = guard.child.as_mut() {
    match child.try_wait() {
      Ok(None) => true,
      Ok(Some(_)) => {
        guard.child = None;
        false
      }
      Err(_) => false,
    }
  } else {
    false
  };
  let port = listen_port();
  let listening = port_open(port);
  AppStatus {
    relay_running: child_alive || listening,
    relay_listening: listening,
    cdp_ports: scan_cdp_ports(),
    relay_url: relay_url_from_disk(),
    repo_root: repo_root().display().to_string(),
    port,
  }
}

#[tauri::command]
fn save_relay_url(url: String) -> Result<(), String> {
  let path = data_dir().join("config.json");
  let mut cfg = read_json_file(&path);
  let trimmed = url.trim().to_string();
  if trimmed.is_empty() {
    if let Some(obj) = cfg.as_object_mut() {
      obj.remove("relayUrl");
    }
  } else {
    let normalized = if trimmed.ends_with('/') {
      trimmed
    } else {
      format!("{trimmed}/")
    };
    cfg
      .as_object_mut()
      .ok_or_else(|| "config.json 格式错误".to_string())?
      .insert("relayUrl".into(), serde_json::Value::String(normalized.clone()));
    // Keep state in sync for pairing / appBase().
    let st_path = data_dir().join("state.json");
    let mut st = read_json_file(&st_path);
    if let Some(obj) = st.as_object_mut() {
      obj.insert("relayUrl".into(), serde_json::Value::String(normalized));
      write_json_file(&st_path, &st)?;
    }
  }
  write_json_file(&path, &cfg)
}

#[tauri::command]
fn start_relay(state: State<'_, Mutex<RelayProc>>) -> Result<String, String> {
  let mut guard = state.lock().unwrap();
  if let Some(child) = guard.child.as_mut() {
    if child.try_wait().ok().flatten().is_none() {
      return Ok("中继已在运行".into());
    }
  }

  let root = repo_root();
  let server = root.join("server.mjs");
  if !server.exists() {
    return Err(format!(
      "找不到 server.mjs（{}）。请把桌面软件放在 cursor-remote-lite 仓库旁，或设置环境变量 CURSOR_REMOTE_ROOT",
      server.display()
    ));
  }

  let port = listen_port();
  kill_listeners_on(port);
  std::thread::sleep(Duration::from_millis(400));

  let relay = relay_url_from_disk();
  let mut cmd = Command::new("node");
  cmd.current_dir(&root);
  cmd.stdout(Stdio::null());
  cmd.stderr(Stdio::null());
  #[cfg(windows)]
  {
    use std::os::windows::process::CommandExt;
    const CREATE_NO_WINDOW: u32 = 0x08000000;
    cmd.creation_flags(CREATE_NO_WINDOW);
  }

  let cdp = scan_cdp_ports();
  if let Some(p) = cdp.first() {
    cmd.env("CDP_PORT", p.to_string());
    let ports = cdp
      .iter()
      .map(|p| p.to_string())
      .collect::<Vec<_>>()
      .join(",");
    cmd.env("CDP_PORTS", format!("{ports},9222,9333,9223,9224"));
  }

  if !relay.is_empty() {
    cmd.env("CR_RELAY_URL", &relay);
    cmd.arg("server.mjs").arg("--relay");
  } else {
    cmd.arg("server.mjs").arg("--tunnel");
  }

  let child = cmd.spawn().map_err(|e| format!("启动 node 失败：{e}"))?;
  guard.child = Some(child);

  // Wait briefly for listen.
  for _ in 0..20 {
    if port_open(port) {
      return Ok(if relay.is_empty() {
        "已启动（Cloudflare 隧道）".into()
      } else {
        format!("已启动中继：{relay}")
      });
    }
    std::thread::sleep(Duration::from_millis(250));
  }
  Ok("已启动进程，正在等待端口就绪…".into())
}

#[tauri::command]
fn stop_relay(state: State<'_, Mutex<RelayProc>>) -> Result<String, String> {
  let mut guard = state.lock().unwrap();
  if let Some(mut child) = guard.child.take() {
    let _ = child.kill();
    let _ = child.wait();
  }
  kill_listeners_on(listen_port());
  Ok("已停止中继".into())
}

#[tauri::command]
fn ensure_cursor() -> Result<String, String> {
  if !scan_cdp_ports().is_empty() {
    return Ok(format!(
      "已检测到 Cursor 调试端口：{}",
      scan_cdp_ports()
        .iter()
        .map(|p| p.to_string())
        .collect::<Vec<_>>()
        .join(", ")
    ));
  }
  let root = repo_root();
  let script = root.join("start-cursor.ps1");
  if !script.exists() {
    return Err("找不到 start-cursor.ps1".into());
  }
  let status = Command::new("powershell")
    .args([
      "-NoProfile",
      "-ExecutionPolicy",
      "Bypass",
      "-File",
      script.to_str().unwrap_or(""),
    ])
    .env("CDP_PORT", "9333")
    .status()
    .map_err(|e| e.to_string())?;
  if !status.success() {
    return Err("start-cursor.ps1 未成功完成".into());
  }
  for _ in 0..30 {
    if !scan_cdp_ports().is_empty() {
      return Ok("Cursor 已带调试端口启动".into());
    }
    std::thread::sleep(Duration::from_secs(1));
  }
  Err("Cursor 调试端口仍未就绪".into())
}

#[tauri::command]
fn create_pair_link() -> Result<PairInfo, String> {
  let root = repo_root();
  let script = root.join("desktop/pair-link.mjs");
  if !script.exists() {
    return Err("找不到 desktop/pair-link.mjs".into());
  }
  let output = Command::new("node")
    .arg(&script)
    .current_dir(&root)
    .output()
    .map_err(|e| format!("运行 pair-link 失败：{e}"))?;
  if !output.status.success() {
    let err = String::from_utf8_lossy(&output.stderr);
    let out = String::from_utf8_lossy(&output.stdout);
    return Err(if !err.trim().is_empty() {
      err.trim().to_string()
    } else {
      out.trim().to_string()
    });
  }
  let text = String::from_utf8_lossy(&output.stdout);
  let v: serde_json::Value =
    serde_json::from_str(text.trim()).map_err(|e| format!("解析配对结果失败：{e} / {text}"))?;
  if let Some(e) = v.get("error").and_then(|x| x.as_str()) {
    return Err(e.into());
  }
  Ok(PairInfo {
    link: v["link"].as_str().unwrap_or("").into(),
    minutes: v["minutes"].as_u64().unwrap_or(10) as u32,
    base: v["base"].as_str().unwrap_or("").into(),
  })
}

fn show_main(app: &AppHandle) {
  if let Some(w) = app.get_webview_window("main") {
    let _ = w.show();
    let _ = w.set_focus();
  }
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
  tauri::Builder::default()
    .plugin(tauri_plugin_opener::init())
    .manage(Mutex::new(RelayProc { child: None }))
    .invoke_handler(tauri::generate_handler![
      get_status,
      save_relay_url,
      start_relay,
      stop_relay,
      ensure_cursor,
      create_pair_link
    ])
    .setup(|app| {
      let show_i = MenuItem::with_id(app, "show", "打开面板", true, None::<&str>)?;
      let start_i = MenuItem::with_id(app, "start", "启动中继", true, None::<&str>)?;
      let stop_i = MenuItem::with_id(app, "stop", "停止中继", true, None::<&str>)?;
      let quit_i = MenuItem::with_id(app, "quit", "退出", true, None::<&str>)?;
      let menu = Menu::with_items(app, &[&show_i, &start_i, &stop_i, &quit_i])?;

      let _tray = TrayIconBuilder::new()
        .menu(&menu)
        .tooltip("Cursor Remote Lite")
        .icon(app.default_window_icon().unwrap().clone())
        .on_menu_event(|app, event| match event.id.as_ref() {
          "show" => show_main(app),
          "start" => {
            let st = app.state::<Mutex<RelayProc>>();
            let _ = start_relay(st);
            show_main(app);
          }
          "stop" => {
            let st = app.state::<Mutex<RelayProc>>();
            let _ = stop_relay(st);
          }
          "quit" => {
            let st = app.state::<Mutex<RelayProc>>();
            let _ = stop_relay(st);
            app.exit(0);
          }
          _ => {}
        })
        .on_tray_icon_event(|tray, event| {
          if let TrayIconEvent::Click {
            button: MouseButton::Left,
            button_state: MouseButtonState::Up,
            ..
          } = event
          {
            show_main(tray.app_handle());
          }
        })
        .build(app)?;

      if let Some(w) = app.get_webview_window("main") {
        let app_h = app.handle().clone();
        w.on_window_event(move |e| {
          if let WindowEvent::CloseRequested { api, .. } = e {
            api.prevent_close();
            if let Some(win) = app_h.get_webview_window("main") {
              let _ = win.hide();
            }
          }
        });
      }
      Ok(())
    })
    .run(tauri::generate_context!())
    .expect("error while running tauri application");
}
