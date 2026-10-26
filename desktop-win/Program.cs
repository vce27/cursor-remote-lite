using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CursorRemoteLite;

// Colors from RustStor System Manager (--rs-* at http://10.10.3.19:8080).
static class Rs
{
  public static readonly Color Primary = Color.FromArgb(0, 122, 255);       // --rs-primary
  public static readonly Color Green = Color.FromArgb(52, 199, 89);         // --rs-green
  public static readonly Color Red = Color.FromArgb(255, 77, 79);           // --rs-red
  public static readonly Color Warning = Color.FromArgb(240, 180, 41);      // --rs-warning
  public static readonly Color Text = Color.FromArgb(224, 0, 0, 0);         // --rs-text ~.88
  public static readonly Color TextSecondary = Color.FromArgb(115, 0, 0, 0); // --rs-text-secondary ~.45
  public static readonly Color TextTertiary = Color.FromArgb(105, 115, 134); // --rs-text-tertiary
  public static readonly Color Border = Color.FromArgb(217, 217, 217);      // --rs-border
  public static readonly Color Split = Color.FromArgb(240, 240, 240);       // --rs-split
  public static readonly Color Bg = Color.FromArgb(245, 246, 248);          // --rs-bg
  public static readonly Color Surface = Color.White;                      // --rs-surface
  public static readonly Color NavActive = Color.FromArgb(230, 244, 255);   // --rs-nav-active
  public const int ControlH = 32;
  public const int Radius = 8;
}

static class Program
{
  [STAThread]
  static void Main()
  {
    ApplicationConfiguration.Initialize();
    Application.Run(new MainForm());
  }
}

sealed class RsCard : Panel
{
  public RsCard()
  {
    BackColor = Rs.Surface;
    Padding = new Padding(16);
  }

  protected override void OnPaint(PaintEventArgs e)
  {
    base.OnPaint(e);
    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
    using var path = RoundRect(ClientRectangle, Rs.Radius);
    using var pen = new Pen(Rs.Split);
    e.Graphics.DrawPath(pen, path);
  }

  static GraphicsPath RoundRect(Rectangle r, int radius)
  {
    var d = radius * 2;
    var path = new GraphicsPath();
    path.AddArc(r.X, r.Y, d, d, 180, 90);
    path.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90);
    path.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
    path.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90);
    path.CloseFigure();
    return path;
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

  readonly Panel _appBar = new();
  readonly Label _brandMark = new();
  readonly Label _title = new();
  readonly Label _desc = new();
  readonly Button _btnLang = new();
  readonly Label _status = new();
  readonly Label _phone = new();
  readonly RsCard _relayCard = new();
  readonly Label _urlLabel = new();
  readonly Label _urlHint = new();
  readonly TextBox _urlBox = new();
  readonly Button _btnSave = new();
  readonly Button _btnCursor = new();
  readonly Button _btnStop = new();
  readonly Button _btnStart = new();
  readonly RsCard _pairCard = new();
  readonly Label _pairTitle = new();
  readonly PictureBox _qr = new();
  readonly TextBox _linkBox = new();
  readonly Button _btnPair = new();
  readonly Button _btnCopy = new();
  readonly Label _pairHint = new();
  readonly Label _toast = new();
  readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };
  readonly HttpClient _http = new() { Timeout = TimeSpan.FromMilliseconds(400) };
  int _statusBusy;

  public MainForm()
  {
    Directory.CreateDirectory(DataDir);
    _repoRoot = FindRepoRoot();
    _port = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var p) ? p : 3939;
    _lang = ReadLang();

    Text = "Cursor Remote Lite";
    ClientSize = new Size(600, 720);
    StartPosition = FormStartPosition.CenterScreen;
    BackColor = Rs.Bg;
    Font = new Font("Microsoft YaHei UI", 9f);
    ForeColor = Rs.Text;
    FormBorderStyle = FormBorderStyle.FixedSingle;
    MaximizeBox = false;

    BuildAppBar();
    BuildStatus();
    BuildRelayCard();
    BuildPairCard();

    _toast.Location = new Point(20, 684);
    _toast.Size = new Size(560, 22);
    _toast.ForeColor = Rs.Primary;

    Controls.AddRange([_appBar, _status, _phone, _relayCard, _pairCard, _toast]);

    _timer.Tick += async (_, _) => await RefreshStatusAsync();
    _timer.Start();
    ApplyLang();
    _ = RefreshStatusAsync();
    FormClosing += (_, _) =>
    {
      _timer.Stop();
      StopRelay();
      _http.Dispose();
    };
  }

  void BuildAppBar()
  {
    _appBar.Location = new Point(0, 0);
    _appBar.Size = new Size(600, 56);
    _appBar.BackColor = Rs.Surface;
    _appBar.Paint += (_, e) =>
    {
      using var pen = new Pen(Rs.Split);
      e.Graphics.DrawLine(pen, 0, _appBar.Height - 1, _appBar.Width, _appBar.Height - 1);
    };

    _brandMark.Text = "CR";
    _brandMark.Font = new Font(Font.FontFamily, 9f, FontStyle.Bold);
    _brandMark.ForeColor = Color.White;
    _brandMark.BackColor = Rs.Primary;
    _brandMark.TextAlign = ContentAlignment.MiddleCenter;
    _brandMark.Location = new Point(16, 14);
    _brandMark.Size = new Size(28, 28);

    _title.Font = new Font(Font.FontFamily, 11f, FontStyle.Bold);
    _title.ForeColor = Rs.Text;
    _title.Location = new Point(52, 8);
    _title.AutoSize = true;

    _desc.ForeColor = Rs.TextTertiary;
    _desc.Location = new Point(52, 30);
    _desc.Size = new Size(420, 20);

    StyleGhost(_btnLang);
    _btnLang.Size = new Size(72, Rs.ControlH);
    _btnLang.Location = new Point(512, 12);
    _btnLang.Click += (_, _) =>
    {
      _lang = _lang == "zh" ? "en" : "zh";
      SaveLang(_lang);
      ApplyLang();
    };

    _appBar.Controls.AddRange([_brandMark, _title, _desc, _btnLang]);
  }

  void BuildStatus()
  {
    _status.Location = new Point(20, 68);
    _status.Size = new Size(560, 22);
    _phone.Location = new Point(20, 90);
    _phone.Size = new Size(560, 22);
  }

  void BuildRelayCard()
  {
    _relayCard.Location = new Point(20, 120);
    _relayCard.Size = new Size(560, 200);

    _urlLabel.Location = new Point(16, 14);
    _urlLabel.AutoSize = true;
    _urlLabel.ForeColor = Rs.Text;

    _urlBox.Location = new Point(16, 40);
    _urlBox.Size = new Size(420, Rs.ControlH);
    _urlBox.BorderStyle = BorderStyle.FixedSingle;
    _urlBox.BackColor = Rs.Surface;
    _urlBox.ForeColor = Rs.Text;
    _urlBox.Text = GetRelayUrl();

    StyleSecondary(_btnSave);
    _btnSave.Location = new Point(444, 38);
    _btnSave.Size = new Size(100, Rs.ControlH);
    _btnSave.Click += (_, _) =>
    {
      try { SaveRelayUrl(_urlBox.Text); Toast(T("saved")); }
      catch (Exception ex) { Toast(ex.Message, true); }
    };

    _urlHint.ForeColor = Rs.TextTertiary;
    _urlHint.Location = new Point(16, 76);
    _urlHint.Size = new Size(528, 36);

    StyleSecondary(_btnCursor);
    _btnCursor.Location = new Point(16, 128);
    _btnCursor.Size = new Size(120, Rs.ControlH);
    _btnCursor.Click += async (_, _) =>
    {
      try { Toast(await EnsureCursorAsync()); await RefreshStatusAsync(); }
      catch (Exception ex) { Toast(ex.Message, true); }
    };

    StyleSecondary(_btnStop);
    _btnStop.Location = new Point(148, 128);
    _btnStop.Size = new Size(120, Rs.ControlH);
    _btnStop.Click += async (_, _) =>
    {
      try { StopRelay(); Toast(T("stopped")); await RefreshStatusAsync(); }
      catch (Exception ex) { Toast(ex.Message, true); }
    };

    StylePrimary(_btnStart);
    _btnStart.Location = new Point(280, 128);
    _btnStart.Size = new Size(148, Rs.ControlH);
    _btnStart.Click += async (_, _) =>
    {
      try { Toast(await StartRelayAsync()); await RefreshStatusAsync(); }
      catch (Exception ex) { Toast(ex.Message, true); }
    };

    _relayCard.Controls.AddRange([
      _urlLabel, _urlBox, _btnSave, _urlHint, _btnCursor, _btnStop, _btnStart
    ]);
  }

  void BuildPairCard()
  {
    _pairCard.Location = new Point(20, 336);
    _pairCard.Size = new Size(560, 332);

    _pairTitle.Font = new Font(Font, FontStyle.Bold);
    _pairTitle.ForeColor = Rs.Text;
    _pairTitle.Location = new Point(16, 14);
    _pairTitle.AutoSize = true;

    _qr.Location = new Point(16, 48);
    _qr.Size = new Size(220, 220);
    _qr.BorderStyle = BorderStyle.FixedSingle;
    _qr.SizeMode = PictureBoxSizeMode.Zoom;
    _qr.BackColor = Rs.Surface;

    _linkBox.Location = new Point(252, 48);
    _linkBox.Size = new Size(292, 120);
    _linkBox.Multiline = true;
    _linkBox.ReadOnly = true;
    _linkBox.ScrollBars = ScrollBars.Vertical;
    _linkBox.BorderStyle = BorderStyle.FixedSingle;
    _linkBox.BackColor = Rs.Bg;
    _linkBox.ForeColor = Rs.Text;

    StylePrimary(_btnPair);
    _btnPair.Location = new Point(252, 184);
    _btnPair.Size = new Size(160, Rs.ControlH);
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
    _btnCopy.Location = new Point(424, 184);
    _btnCopy.Size = new Size(120, Rs.ControlH);
    _btnCopy.Click += (_, _) =>
    {
      if (!string.IsNullOrWhiteSpace(_linkBox.Text))
      {
        Clipboard.SetText(_linkBox.Text);
        Toast(T("copied"));
      }
    };

    _pairHint.ForeColor = Rs.TextTertiary;
    _pairHint.Location = new Point(252, 228);
    _pairHint.Size = new Size(292, 48);

    _pairCard.Controls.AddRange([
      _pairTitle, _qr, _linkBox, _btnPair, _btnCopy, _pairHint
    ]);
  }

  static void StylePrimary(Button b)
  {
    b.FlatStyle = FlatStyle.Flat;
    b.BackColor = Rs.Primary;
    b.ForeColor = Color.White;
    b.FlatAppearance.BorderColor = Rs.Primary;
    b.FlatAppearance.BorderSize = 0;
    b.Cursor = Cursors.Hand;
  }

  static void StyleSecondary(Button b)
  {
    b.FlatStyle = FlatStyle.Flat;
    b.BackColor = Rs.Surface;
    b.ForeColor = Rs.Text;
    b.FlatAppearance.BorderColor = Rs.Border;
    b.Cursor = Cursors.Hand;
  }

  static void StyleGhost(Button b)
  {
    b.FlatStyle = FlatStyle.Flat;
    b.BackColor = Rs.Surface;
    b.ForeColor = Rs.Primary;
    b.FlatAppearance.BorderColor = Rs.Border;
    b.Cursor = Cursors.Hand;
  }

  string T(string key) => (_lang == "en" ? En : Zh)[key];

  static readonly Dictionary<string, string> Zh = new()
  {
    ["title"] = "Cursor Remote Lite",
    ["desc"] = "电脑端面板 · iPhone Safari 扫码添加到主屏幕",
    ["lang"] = "EN",
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
    ["online"] = "中继在线 · 端口 {0} · Cursor CDP {1}",
    ["starting"] = "中继启动中… · Cursor CDP {0}",
    ["offline"] = "中继已停止 · Cursor CDP {0}",
    ["cdpNone"] = "未检测到",
    ["phoneConnected"] = "手机已连接 · {0}",
    ["phonePairedOffline"] = "已配对 {0} 台 · 当前未打开",
    ["phoneNone"] = "尚未配对手机",
    ["phoneRelayOff"] = "手机：中继未启动",
  };

  static readonly Dictionary<string, string> En = new()
  {
    ["title"] = "Cursor Remote Lite",
    ["desc"] = "Desktop panel · iPhone Safari → Add to Home Screen",
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
    ["online"] = "Relay online · port {0} · Cursor CDP {1}",
    ["starting"] = "Relay starting… · Cursor CDP {0}",
    ["offline"] = "Relay off · Cursor CDP {0}",
    ["cdpNone"] = "none",
    ["phoneConnected"] = "Phone connected · {0}",
    ["phonePairedOffline"] = "{0} paired · not open now",
    ["phoneNone"] = "No phone paired yet",
    ["phoneRelayOff"] = "Phone: relay offline",
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
    _ = RefreshStatusAsync();
  }

  void Toast(string msg, bool err = false)
  {
    _toast.Text = msg;
    _toast.ForeColor = err ? Rs.Red : Rs.Primary;
  }

  async Task RefreshStatusAsync()
  {
    if (Interlocked.Exchange(ref _statusBusy, 1) == 1) return;
    try
    {
      var port = _port;
      var alive = _relay is { HasExited: false };
      var (listening, cdp, phone) = await Task.Run(async () =>
      {
        var listen = PortOpen(port);
        var ports = await ScanCdpAsync().ConfigureAwait(false);
        string? phoneJson = null;
        if (listen)
        {
          try { phoneJson = await _http.GetStringAsync($"http://127.0.0.1:{port}/api/desktop").ConfigureAwait(false); }
          catch { }
        }
        return (listen, ports, phoneJson);
      }).ConfigureAwait(true);

      var cdpText = cdp.Count > 0 ? string.Join(" ", cdp.Select(x => ":" + x)) : T("cdpNone");
      if (listening)
      {
        _status.Text = string.Format(T("online"), _port, cdpText);
        _status.ForeColor = Rs.Green;
      }
      else if (alive)
      {
        _status.Text = string.Format(T("starting"), cdpText);
        _status.ForeColor = Rs.Warning;
      }
      else
      {
        _status.Text = string.Format(T("offline"), cdpText);
        _status.ForeColor = Rs.TextSecondary;
      }
      ApplyPhoneStatus(listening, phone);
    }
    finally { Interlocked.Exchange(ref _statusBusy, 0); }
  }

  void ApplyPhoneStatus(bool relayListening, string? json)
  {
    if (!relayListening || string.IsNullOrEmpty(json))
    {
      _phone.Text = T("phoneRelayOff");
      _phone.ForeColor = Rs.TextTertiary;
      return;
    }
    try
    {
      using var doc = JsonDocument.Parse(json);
      var root = doc.RootElement;
      var paired = root.TryGetProperty("paired", out var p) ? p.GetInt32() : 0;
      var online = root.TryGetProperty("online", out var o) ? o.GetInt32() : 0;
      if (online > 0 && root.TryGetProperty("phones", out var phones) && phones.ValueKind == JsonValueKind.Array)
      {
        var names = phones.EnumerateArray()
          .Select(x => x.TryGetProperty("name", out var n) ? n.GetString() : null)
          .Where(s => !string.IsNullOrWhiteSpace(s))
          .Distinct()
          .ToList();
        var label = names.Count > 0 ? string.Join("、", names) : online.ToString();
        _phone.Text = string.Format(T("phoneConnected"), label);
        _phone.ForeColor = Rs.Green;
      }
      else if (paired > 0)
      {
        _phone.Text = string.Format(T("phonePairedOffline"), paired);
        _phone.ForeColor = Rs.Warning;
      }
      else
      {
        _phone.Text = T("phoneNone");
        _phone.ForeColor = Rs.TextTertiary;
      }
    }
    catch
    {
      _phone.Text = T("phoneRelayOff");
      _phone.ForeColor = Rs.TextTertiary;
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
    if (string.IsNullOrEmpty(trimmed))
      cfg.Remove("relayUrl");
    else
    {
      if (!trimmed.EndsWith('/')) trimmed += "/";
      cfg["relayUrl"] = trimmed;
    }
    WriteJson(ConfigPath, cfg);
  }

  static bool PortOpen(int port)
  {
    try
    {
      using var c = new TcpClient();
      var ar = c.BeginConnect("127.0.0.1", port, null, null);
      if (!ar.AsyncWaitHandle.WaitOne(80)) return false;
      c.EndConnect(ar);
      return true;
    }
    catch { return false; }
  }

  async Task<bool> ProbeCdpAsync(int port)
  {
    if (!PortOpen(port)) return false;
    try
    {
      var body = await _http.GetStringAsync($"http://127.0.0.1:{port}/json/version").ConfigureAwait(false);
      return body.Contains("Cursor") || body.Contains("Chrome");
    }
    catch { return false; }
  }

  async Task<List<int>> ScanCdpAsync()
  {
    int[] ports = [9333, 9222, 9223, 9224];
    var hits = await Task.WhenAll(ports.Select(async p => (p, ok: await ProbeCdpAsync(p).ConfigureAwait(false))));
    return hits.Where(x => x.ok).Select(x => x.p).ToList();
  }

  void StopRelay()
  {
    try { _relay?.Kill(entireProcessTree: true); } catch { }
    _relay = null;
  }

  async Task<string> StartRelayAsync()
  {
    StopRelay();
    await Task.Delay(150);
    var url = GetRelayUrl();
    if (!string.IsNullOrWhiteSpace(_urlBox.Text) && _urlBox.Text.Trim() != url)
      SaveRelayUrl(_urlBox.Text);
    url = GetRelayUrl();

    var server = Path.Combine(_repoRoot, "server.mjs");
    if (!File.Exists(server))
      throw new Exception("server.mjs not found: " + _repoRoot);

    var cdp = await ScanCdpAsync();
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
    var existing = await ScanCdpAsync();
    if (existing.Count > 0) return "CDP: " + string.Join(", ", existing);

    var script = Path.Combine(_repoRoot, "scripts", "start-cursor.ps1");
    if (!File.Exists(script)) throw new Exception("scripts/start-cursor.ps1 not found");

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
      await Task.Delay(500);
      var ports = await ScanCdpAsync();
      if (ports.Count > 0) return "Cursor CDP ready";
    }
    throw new Exception("Cursor CDP not ready");
  }

  async Task<JsonElement> CreatePairLinkAsync()
  {
    var script = Path.Combine(_repoRoot, "pair-link.mjs");
    if (!File.Exists(script)) throw new Exception("pair-link.mjs not found");
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
