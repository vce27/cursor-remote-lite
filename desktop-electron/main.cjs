const { app, BrowserWindow, Tray, Menu, nativeImage, ipcMain, shell } = require('electron');
const path = require('path');
const fs = require('fs');
const net = require('net');
const { spawn, execFile } = require('child_process');
const http = require('http');

const REPO = path.resolve(__dirname, '..');
const DATA = path.join(app.getPath('home'), '.cursor-remote-lite');
let win = null;
let tray = null;
let child = null;

function readJson(file) {
  try { return JSON.parse(fs.readFileSync(file, 'utf8')); } catch { return {}; }
}
function writeJson(file, obj) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, JSON.stringify(obj, null, 2) + '\n');
}
function listenPort() {
  return Number(process.env.PORT || 3939);
}
function relayUrl() {
  const cfg = readJson(path.join(DATA, 'config.json'));
  const st = readJson(path.join(DATA, 'state.json'));
  return String(cfg.relayUrl || st.relayUrl || '');
}
function portOpen(port) {
  return new Promise(resolve => {
    const s = net.connect({ host: '127.0.0.1', port, timeout: 200 }, () => { s.destroy(); resolve(true); });
    s.on('error', () => resolve(false));
    s.on('timeout', () => { s.destroy(); resolve(false); });
  });
}
function probeCdp(port) {
  return new Promise(resolve => {
    const req = http.get({ host: '127.0.0.1', port, path: '/json/version', timeout: 400 }, res => {
      let b = '';
      res.on('data', c => b += c);
      res.on('end', () => resolve(/Cursor|Chrome/.test(b)));
    });
    req.on('error', () => resolve(false));
    req.on('timeout', () => { req.destroy(); resolve(false); });
  });
}
async function scanCdp() {
  const ports = [9333, 9222, 9223, 9224];
  const hit = [];
  for (const p of ports) if (await probeCdp(p)) hit.push(p);
  return hit;
}
function killRelayListeners(port) {
  try {
    spawn('powershell', [
      '-NoProfile', '-Command',
      `Get-NetTCPConnection -LocalPort ${port} -State Listen -EA SilentlyContinue | ForEach-Object { $p=Get-Process -Id $_.OwningProcess -EA SilentlyContinue; if($p -and $p.ProcessName -eq 'node'){ Stop-Process -Id $p.Id -Force -EA SilentlyContinue } }`,
    ], { windowsHide: true, stdio: 'ignore' });
  } catch {}
}

async function getStatus() {
  const port = listenPort();
  const listening = await portOpen(port);
  const alive = Boolean(child && !child.killed);
  return {
    relay_running: alive || listening,
    relay_listening: listening,
    cdp_ports: await scanCdp(),
    relay_url: relayUrl(),
    repo_root: REPO,
    port,
  };
}

function saveRelayUrl(url) {
  const trimmed = String(url || '').trim();
  const cfgPath = path.join(DATA, 'config.json');
  const stPath = path.join(DATA, 'state.json');
  const cfg = readJson(cfgPath);
  const st = readJson(stPath);
  if (!trimmed) {
    delete cfg.relayUrl;
  } else {
    const n = trimmed.endsWith('/') ? trimmed : trimmed + '/';
    cfg.relayUrl = n;
    st.relayUrl = n;
    writeJson(stPath, st);
  }
  writeJson(cfgPath, cfg);
  return '已保存公网地址';
}

async function startRelay() {
  if (child && !child.killed) return '中继已在运行';
  const server = path.join(REPO, 'server.mjs');
  if (!fs.existsSync(server)) throw new Error('找不到 server.mjs');
  const port = listenPort();
  killRelayListeners(port);
  await new Promise(r => setTimeout(r, 400));
  const url = relayUrl();
  const cdp = await scanCdp();
  const env = { ...process.env };
  if (cdp[0]) {
    env.CDP_PORT = String(cdp[0]);
    env.CDP_PORTS = [...new Set([...cdp, 9222, 9333, 9223, 9224])].join(',');
  }
  const args = url ? ['server.mjs', '--relay'] : ['server.mjs', '--tunnel'];
  if (url) env.CR_RELAY_URL = url;
  child = spawn('node', args, { cwd: REPO, env, windowsHide: true, stdio: 'ignore' });
  child.on('exit', () => { child = null; });
  for (let i = 0; i < 20; i++) {
    if (await portOpen(port)) return url ? `已启动中继：${url}` : '已启动（Cloudflare 隧道）';
    await new Promise(r => setTimeout(r, 250));
  }
  return '已启动进程，正在等待端口就绪…';
}

function stopRelay() {
  if (child) {
    try { child.kill(); } catch {}
    child = null;
  }
  killRelayListeners(listenPort());
  return '已停止中继';
}

function ensureCursor() {
  return new Promise(async (resolve, reject) => {
    const existing = await scanCdp();
    if (existing.length) return resolve(`已检测到 Cursor 调试端口：${existing.map(p => ':' + p).join(' ')}`);
    const script = path.join(REPO, 'start-cursor.ps1');
    if (!fs.existsSync(script)) return reject(new Error('找不到 start-cursor.ps1'));
    const ps = spawn('powershell', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', script], {
      env: { ...process.env, CDP_PORT: '9333' },
      windowsHide: true,
      stdio: 'ignore',
    });
    ps.on('exit', async code => {
      if (code) return reject(new Error('start-cursor.ps1 未成功完成'));
      for (let i = 0; i < 30; i++) {
        const ports = await scanCdp();
        if (ports.length) return resolve('Cursor 已带调试端口启动');
        await new Promise(r => setTimeout(r, 1000));
      }
      reject(new Error('Cursor 调试端口仍未就绪'));
    });
  });
}

function createPairLink() {
  return new Promise((resolve, reject) => {
    execFile('node', [path.join(REPO, 'desktop', 'pair-link.mjs')], { cwd: REPO }, (err, stdout, stderr) => {
      if (err) {
        try {
          const j = JSON.parse(String(stderr || stdout || '{}'));
          if (j.error) return reject(new Error(j.error));
        } catch {}
        return reject(new Error(stderr || err.message));
      }
      try {
        resolve(JSON.parse(String(stdout).trim()));
      } catch (e) {
        reject(new Error('解析配对结果失败'));
      }
    });
  });
}

function createWindow() {
  win = new BrowserWindow({
    width: 560,
    height: 680,
    title: 'Cursor Remote Lite',
    webPreferences: {
      preload: path.join(__dirname, 'preload.cjs'),
      contextIsolation: true,
    },
  });
  win.loadFile(path.join(__dirname, 'index.html'));
  win.on('close', e => {
    if (!app.isQuitting) {
      e.preventDefault();
      win.hide();
    }
  });
}

function createTray() {
  const iconPath = path.join(REPO, 'app', 'icon-192.png');
  let img = nativeImage.createFromPath(iconPath);
  if (!img.isEmpty()) img = img.resize({ width: 16, height: 16 });
  tray = new Tray(img.isEmpty() ? nativeImage.createEmpty() : img);
  tray.setToolTip('Cursor Remote Lite');
  const menu = Menu.buildFromTemplate([
    { label: '打开面板', click: () => { win?.show(); win?.focus(); } },
    { label: '启动中继', click: async () => { try { await startRelay(); } catch {} } },
    { label: '停止中继', click: () => stopRelay() },
    { type: 'separator' },
    { label: '退出', click: () => { app.isQuitting = true; stopRelay(); app.quit(); } },
  ]);
  tray.setContextMenu(menu);
  tray.on('click', () => { win?.show(); win?.focus(); });
}

app.whenReady().then(() => {
  createWindow();
  createTray();
  ipcMain.handle('get_status', () => getStatus());
  ipcMain.handle('save_relay_url', (_e, url) => saveRelayUrl(url));
  ipcMain.handle('start_relay', () => startRelay());
  ipcMain.handle('stop_relay', () => stopRelay());
  ipcMain.handle('ensure_cursor', () => ensureCursor());
  ipcMain.handle('create_pair_link', () => createPairLink());
  ipcMain.handle('open_external', (_e, url) => shell.openExternal(url));
});

app.on('window-all-closed', e => e.preventDefault());
app.on('before-quit', () => { app.isQuitting = true; stopRelay(); });
