using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CursorRemoteLite;

static class Program
{
  [STAThread]
  static void Main()
  {
    ApplicationConfiguration.Initialize();
    Application.Run(new MainForm());
  }
}

sealed class MainForm : Form
{
  static readonly string DataDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".cursor-remote-lite");
  static readonly string ConfigPath = Path.Combine(DataDir, "config.json");
  static readonly string StatePath = Path.Combine(DataDir, "state.json");

  readonly string _repoRoot;
  readonly int _port;
  Process? _relay;
  string _lang = "zh";

  readonly Label _title = new();
  readonly Label _desc = new();
  readonly Label _status = new();
  readonly Label _urlLabel = new();
  readonly Label _urlHint = new();
  readonly TextBox _urlBox = new();
  readonly Button _btnSave = new();
  readonly Button _btnCursor = new();
  readonly Button _btnStop = new();
  readonly Button _btnStart = new();
  readonly Label _pairTitle = new();
  readonly PictureBox _qr = new();
  readonly TextBox _linkBox = new();
  readonly Button _btnPair = new();
  readonly Button _btnCopy = new();
  readonly Label _pairHint = new();
  readonly Label _toast = new();
  readonly Button _btnLang = new();
  readonly System.Windows.Forms.Timer _timer = new() { Interval = 2500 };

  public MainForm()
  {
    Directory.CreateDirectory(DataDir);
    _repoRoot = FindRepoRoot();
    _port = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var p) ? p : 3939;
    _lang = ReadLang();

    Text = "Cursor Remote Lite";
    ClientSize = new Size(560, 680);
    StartPosition = FormStartPosition.CenterScreen;
    BackColor = Color.FromArgb(245, 245, 245);
    Font = new Font("Microsoft YaHei UI", 9f);
    FormBorderStyle = FormBorderStyle.FixedSingle;
    MaximizeBox = false;

    _title.Font = new Font(Font, FontStyle.Bold);
    _title.Location = new Point(24, 16);
    _title.AutoSize = true;

    _desc.ForeColor = Color.FromArgb(105, 115, 134);
    _desc.Location = new Point(24, 42);
    _desc.Size = new Size(420, 36);

    _btnLang.Size = new Size(88, 32);
    _btnLang.Location = new Point(448, 16);
    _btnLang.FlatStyle = FlatStyle.Flat;
    _btnLang.BackColor = Color.White;
    _btnLang.FlatAppearance.BorderColor = Color.FromArgb(217, 217, 217);
    _btnLang.Click += (_, _) =>
    {
      _lang = _lang == "zh" ? "en" : "zh";
      SaveLang(_lang);
      ApplyLang();
    };

    _status.Location = new Point(24, 84);
    _status.Size = new Size(510, 22);

    _urlLabel.Location = new Point(24, 118);
    _urlLabel.AutoSize = true;

    _urlBox.Location = new Point(24, 144);
    _urlBox.Size = new Size(400, 28);
    _urlBox.Text = GetRelayUrl();

    StyleSecondary(_btnSave);
    _btnSave.Location = new Point(436, 142);
    _btnSave.Size = new Size(100, 32);
    _btnSave.Click += (_, _) =>
    {
      try { SaveRelayUrl(_urlBox.Text); Toast(T("saved")); }
      catch (Exception ex) { Toast(ex.Message, true); }
    };

    _urlHint.ForeColor = Color.FromArgb(105, 115, 134);
    _urlHint.Location = new Point(24, 178);
    _urlHint.Size = new Size(510, 36);

    StyleSecondary(_btnCursor);
    _btnCursor.Location = new Point(24, 226);
    _btnCursor.Size = new Size(120, 32);
    _btnCursor.Click += async (_, _) =>
    {
      try { Toast(await EnsureCursorAsync()); RefreshStatus(); }
      catch (Exception ex) { Toast(ex.Message, true); }
    };

    StyleSecondary(_btnStop);
    _btnStop.Location = new Point(156, 226);
    _btnStop.Size = new Size(120, 32);
    _btnStop.Click += (_, _) =>
    {
      try { StopRelay(); Toast(T("stopped")); RefreshStatus(); }
      catch (Exception ex) { Toast(ex.Message, true); }
    };

    StylePrimary(_btnStart);
    _btnStart.Location = new Point(288, 226);
    _btnStart.Size = new Size(140, 32);
    _btnStart.Click += async (_, _) =>
    {
      try { Toast(await StartRelayAsync()); RefreshStatus(); }
      catch (Exception ex) { Toast(ex.Message, true); }
    };

    _pairTitle.Font = new Font(Font, FontStyle.Bold);
    _pairTitle.Location = new Point(24, 278);
    _pairTitle.AutoSize = true;

    _qr.Location = new Point(24, 308);
    _qr.Size = new Size(240, 240);
    _qr.BorderStyle = BorderStyle.FixedSingle;
    _qr.SizeMode = PictureBoxSizeMode.Zoom;
    _qr.BackColor = Color.White;

    _linkBox.Location = new Point(280, 308);
    _linkBox.Size = new Size(256, 120);
    _linkBox.Multiline = true;
    _linkBox.ReadOnly = true;
    _linkBox.ScrollBars = ScrollBars.Vertical;

    StylePrimary(_btnPair);
    _btnPair.Location = new Point(280, 448);
    _btnPair.Size = new Size(140, 32);
    _btnPair.Click += async (_, _) =>
    {
      try
      {
        var info = await CreatePairLinkAsync();
        _linkBox.Text = info.GetProperty("link").GetString() ?? "";
        var minutes = info.GetProperty("minutes").GetInt32();
        _pairHint.Text = string.Format(T("pairHintReady"), minutes);
        var enc = Uri.EscapeDataString(_linkBox.Text);
        var tmp = Path.Combine(Path.GetTempPath(), "cr-pair-qr.png");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        var bytes = await http.GetByteArrayAsync($"https://api.qrserver.com/v1/create-qr-code/?size=240x240&data={enc}");
        await File.WriteAllBytesAsync(tmp, bytes);
        _qr.ImageLocation = tmp;
        Toast(string.Format(T("pairReady"), minutes));
      }
      catch (Exception ex) { Toast(ex.Message, true); }
    };

    StyleSecondary(_btnCopy);
    _btnCopy.Location = new Point(430, 448);
    _btnCopy.Size = new Size(106, 32);
    _btnCopy.Click += (_, _) =>
    {
      if (!string.IsNullOrWhiteSpace(_linkBox.Text))
      {
        Clipboard.SetText(_linkBox.Text);
        Toast(T("copied"));
      }
    };

    _pairHint.ForeColor = Color.FromArgb(105, 115, 134);
    _pairHint.Location = new Point(280, 490);
    _pairHint.Size = new Size(256, 48);

    _toast.Location = new Point(24, 630);
    _toast.Size = new Size(510, 22);
    _toast.ForeColor = Color.FromArgb(0, 122, 255);

    Controls.AddRange([
      _title, _desc, _btnLang, _status, _urlLabel, _urlBox, _btnSave, _urlHint,
      _btnCursor, _btnStop, _btnStart, _pairTitle, _qr, _linkBox, _btnPair, _btnCopy, _pairHint, _toast
    ]);

    _timer.Tick += (_, _) => RefreshStatus();
    _timer.Start();
    ApplyLang();
    RefreshStatus();
    FormClosing += (_, _) => _timer.Stop();
  }

  static void StylePrimary(Button b)
  {
    b.FlatStyle = FlatStyle.Flat;
    b.BackColor = Color.FromArgb(0, 122, 255);
    b.ForeColor = Color.White;
    b.FlatAppearance.BorderColor = Color.FromArgb(0, 122, 255);
  }

  static void StyleSecondary(Button b)
  {
    b.FlatStyle = FlatStyle.Flat;
    b.BackColor = Color.White;
    b.ForeColor = Color.FromArgb(0, 0, 0, 222);
    b.FlatAppearance.BorderColor = Color.FromArgb(217, 217, 217);
  }

  string T(string key) => (_lang == "en" ? En : Zh)[key];

  static readonly Dictionary<string, string> Zh = new()
  {
    ["title"] = "Cursor Remote Lite",
    ["desc"] = "电脑端面板 · iPhone 用 Safari 扫码，添加到主屏幕（不用装 App）",
    ["lang"] = "English",
    ["urlLabel"] = "公网地址（relayUrl）",
    ["urlHint"] = "http:// 也可以；配对会打开中继本身，避免混合内容拦截",
    ["save"] = "保存",
    ["checkCursor"] = "检查 Cursor",
    ["stop"] = "停止中继",
    ["start"] = "一键启动中继",
    ["pairTitle"] = "配对 iPhone",
    ["pair"] = "生成配对二维码",
    ["copy"] = "复制链接",
    ["pairHint"] = "扫码 → Safari → 分享 → 添加到主屏幕",
    ["pairHintReady"] = "{0} 分钟内有效，只能用一次",
    ["pairReady"] = "配对码已生成（{0} 分钟内有效）",
    ["saved"] = "已保存公网地址",
    ["stopped"] = "已停止中继",
    ["copied"] = "已复制配对链接",
    ["online"] = "状态：在线  端口 {0}  Cursor CDP {1}",
    ["starting"] = "状态：启动中…  Cursor CDP {0}",
    ["offline"] = "状态：已停止  Cursor CDP {0}",
    ["cdpNone"] = "未检测到",
  };

  static readonly Dictionary<string, string> En = new()
  {
    ["title"] = "Cursor Remote Lite",
    ["desc"] = "Desktop panel · iPhone: Safari scan QR, Add to Home Screen",
    ["lang"] = "中文",
    ["urlLabel"] = "Public URL (relayUrl)",
    ["urlHint"] = "http:// is OK — pairing opens the relay itself",
    ["save"] = "Save",
    ["checkCursor"] = "Check Cursor",
    ["stop"] = "Stop Relay",
    ["start"] = "Start Relay",
    ["pairTitle"] = "Pair iPhone",
    ["pair"] = "Make Pair QR",
    ["copy"] = "Copy Link",
    ["pairHint"] = "Scan → Safari → Share → Add to Home Screen",
    ["pairHintReady"] = "{0} min, one-time use",
    ["pairReady"] = "Pair code ready ({0} min)",
    ["saved"] = "Saved relayUrl",
    ["stopped"] = "Relay stopped",
    ["copied"] = "Link copied",
    ["online"] = "Status: ONLINE  port {0}  Cursor CDP {1}",
    ["starting"] = "Status: starting…  Cursor CDP {0}",
    ["offline"] = "Status: OFF  Cursor CDP {0}",
    ["cdpNone"] = "none",
  };

  void ApplyLang()
  {
    _title.Text = T("title");
    _desc.Text = T("desc");
    _btnLang.Text = T("lang");
    _urlLabel.Text = T("urlLabel");
    _urlHint.Text = T("urlHint");
    _btnSave.Text = T("save");
    _btnCursor.Text = T("checkCursor");
    _btnStop.Text = T("stop");
    _btnStart.Text = T("start");
    _pairTitle.Text = T("pairTitle");
    _btnPair.Text = T("pair");
    _btnCopy.Text = T("copy");
    if (string.IsNullOrEmpty(_linkBox.Text)) _pairHint.Text = T("pairHint");
    RefreshStatus();
  }

  void Toast(string msg, bool err = false)
  {
    _toast.Text = msg;
    _toast.ForeColor = err ? Color.FromArgb(255, 77, 79) : Color.FromArgb(0, 122, 255);
  }

  void RefreshStatus()
  {
    var listening = PortOpen(_port);
    var alive = _relay is { HasExited: false };
    var cdp = ScanCdp();
    var cdpText = cdp.Count > 0 ? string.Join(" ", cdp.Select(x => ":" + x)) : T("cdpNone");
    if (listening)
    {
      _status.Text = string.Format(T("online"), _port, cdpText);
      _status.ForeColor = Color.FromArgb(19, 82, 0);
    }
    else if (alive)
    {
      _status.Text = string.Format(T("starting"), cdpText);
      _status.ForeColor = Color.FromArgb(173, 104, 0);
    }
    else
    {
      _status.Text = string.Format(T("offline"), cdpText);
      _status.ForeColor = Color.Black;
    }
  }

  static string FindRepoRoot()
  {
    var env = Environment.GetEnvironmentVariable("CURSOR_REMOTE_ROOT");
    if (!string.IsNullOrWhiteSpace(env) && File.Exists(Path.Combine(env, "server.mjs")))
      return Path.GetFullPath(env);

    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
    {
      if (File.Exists(Path.Combine(dir.FullName, "server.mjs")))
        return dir.FullName;
    }
    // published next to repo\bin or repo\desktop-win\bin
    var fallback = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", ".."));
    return File.Exists(Path.Combine(fallback, "server.mjs")) ? fallback : AppContext.BaseDirectory;
  }

  static JsonObject ReadJson(string path)
  {
    try
    {
      if (!File.Exists(path)) return new JsonObject();
      return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
    }
    catch { return new JsonObject(); }
  }

  static void WriteJson(string path, JsonObject obj)
  {
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
  }

  string ReadLang()
  {
    var v = ReadJson(ConfigPath)["lang"]?.GetValue<string>();
    return v is "en" or "zh" ? v : "zh";
  }

  void SaveLang(string lang)
  {
    var cfg = ReadJson(ConfigPath);
    cfg["lang"] = lang;
    WriteJson(ConfigPath, cfg);
  }

  string GetRelayUrl()
  {
    var cfg = ReadJson(ConfigPath);
    var st = ReadJson(StatePath);
    return cfg["relayUrl"]?.GetValue<string>()
      ?? st["relayUrl"]?.GetValue<string>()
      ?? "";
  }

  void SaveRelayUrl(string url)
  {
    var trimmed = url.Trim();
    var cfg = ReadJson(ConfigPath);
    var st = ReadJson(StatePath);
    if (string.IsNullOrEmpty(trimmed))
    {
      cfg.Remove("relayUrl");
    }
    else
    {
      if (!trimmed.EndsWith('/')) trimmed += "/";
      cfg["relayUrl"] = trimmed;
      st["relayUrl"] = trimmed;
      WriteJson(StatePath, st);
    }
    WriteJson(ConfigPath, cfg);
  }

  static bool PortOpen(int port)
  {
    try
    {
      using var c = new TcpClient();
      var ar = c.BeginConnect("127.0.0.1", port, null, null);
      if (!ar.AsyncWaitHandle.WaitOne(200)) return false;
      c.EndConnect(ar);
      return true;
    }
    catch { return false; }
  }

  static bool ProbeCdp(int port)
  {
    try
    {
      using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(400) };
      var body = http.GetStringAsync($"http://127.0.0.1:{port}/json/version").GetAwaiter().GetResult();
      return body.Contains("Cursor") || body.Contains("Chrome");
    }
    catch { return false; }
  }

  static List<int> ScanCdp()
  {
    int[] ports = [9333, 9222, 9223, 9224];
    return ports.Where(ProbeCdp).ToList();
  }

  void KillRelayListeners()
  {
    try
    {
      Process.Start(new ProcessStartInfo
      {
        FileName = "powershell",
        Arguments = $"-NoProfile -Command \"Get-NetTCPConnection -LocalPort {_port} -State Listen -EA SilentlyContinue | ForEach-Object {{ $p=Get-Process -Id $_.OwningProcess -EA SilentlyContinue; if($p -and $p.ProcessName -eq 'node'){{ Stop-Process -Id $p.Id -Force -EA SilentlyContinue }} }}\"",
        CreateNoWindow = true,
        UseShellExecute = false,
      })?.WaitForExit(3000);
    }
    catch { }
  }

  void StopRelay()
  {
    try { _relay?.Kill(entireProcessTree: true); } catch { }
    _relay = null;
    KillRelayListeners();
  }

  async Task<string> StartRelayAsync()
  {
    StopRelay();
    await Task.Delay(400);
    var url = GetRelayUrl();
    if (string.IsNullOrWhiteSpace(_urlBox.Text) == false && _urlBox.Text.Trim() != url)
      SaveRelayUrl(_urlBox.Text);
    url = GetRelayUrl();

    var server = Path.Combine(_repoRoot, "server.mjs");
    if (!File.Exists(server))
      throw new Exception("server.mjs not found: " + _repoRoot);

    var cdp = ScanCdp();
    var psi = new ProcessStartInfo
    {
      FileName = "node",
      WorkingDirectory = _repoRoot,
      CreateNoWindow = true,
      UseShellExecute = false,
    };
    psi.ArgumentList.Add("server.mjs");
    if (!string.IsNullOrEmpty(url))
    {
      psi.ArgumentList.Add("--relay");
      psi.Environment["CR_RELAY_URL"] = url;
    }
    else psi.ArgumentList.Add("--tunnel");

    if (cdp.Count > 0)
    {
      psi.Environment["CDP_PORT"] = cdp[0].ToString();
      psi.Environment["CDP_PORTS"] = string.Join(",", cdp.Concat([9222, 9333, 9223, 9224]).Distinct());
    }

    _relay = Process.Start(psi) ?? throw new Exception("failed to start node");
    for (var i = 0; i < 20; i++)
    {
      if (PortOpen(_port))
        return string.IsNullOrEmpty(url) ? "OK (cloudflare tunnel)" : "OK: " + url;
      await Task.Delay(250);
    }
    return "Started, waiting for port…";
  }

  async Task<string> EnsureCursorAsync()
  {
    var existing = ScanCdp();
    if (existing.Count > 0) return "CDP: " + string.Join(", ", existing);

    var script = Path.Combine(_repoRoot, "start-cursor.ps1");
    if (!File.Exists(script)) throw new Exception("start-cursor.ps1 not found");

    var psi = new ProcessStartInfo
    {
      FileName = "powershell",
      Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"",
      WorkingDirectory = _repoRoot,
      CreateNoWindow = true,
      UseShellExecute = false,
    };
    psi.Environment["CDP_PORT"] = "9333";
    var p = Process.Start(psi) ?? throw new Exception("failed to start Cursor helper");
    await p.WaitForExitAsync();
    for (var i = 0; i < 30; i++)
    {
      await Task.Delay(1000);
      var ports = ScanCdp();
      if (ports.Count > 0) return "Cursor CDP ready";
    }
    throw new Exception("Cursor CDP not ready");
  }

  async Task<JsonElement> CreatePairLinkAsync()
  {
    var script = Path.Combine(_repoRoot, "desktop", "pair-link.mjs");
    if (!File.Exists(script)) throw new Exception("desktop/pair-link.mjs not found");
    var psi = new ProcessStartInfo
    {
      FileName = "node",
      Arguments = $"\"{script}\"",
      WorkingDirectory = _repoRoot,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      CreateNoWindow = true,
      UseShellExecute = false,
      StandardOutputEncoding = Encoding.UTF8,
      StandardErrorEncoding = Encoding.UTF8,
    };
    var p = Process.Start(psi) ?? throw new Exception("pair-link failed");
    var stdout = await p.StandardOutput.ReadToEndAsync();
    var stderr = await p.StandardError.ReadToEndAsync();
    await p.WaitForExitAsync();
    var text = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
    using var doc = JsonDocument.Parse(text.Trim());
    if (doc.RootElement.TryGetProperty("error", out var err))
      throw new Exception(err.GetString() ?? "pair error");
    return doc.RootElement.Clone();
  }
}
