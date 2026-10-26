# Cursor Remote Lite — Windows desktop panel (no Electron download required)
# powershell -ExecutionPolicy Bypass -File .\start-desktop.ps1
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$Root = $PSScriptRoot
$DataDir = Join-Path $HOME '.cursor-remote-lite'
$ConfigFile = Join-Path $DataDir 'config.json'
$StateFile = Join-Path $DataDir 'state.json'
New-Item -ItemType Directory -Force -Path $DataDir | Out-Null

$script:RelayProc = $null
$Port = if ($env:PORT) { [int]$env:PORT } else { 3939 }

function Read-Json($path) {
  if (-not (Test-Path $path)) { return $null }
  try { return Get-Content $path -Raw -Encoding UTF8 | ConvertFrom-Json } catch { $null }
}
function Write-Json($path, $obj) {
  ($obj | ConvertTo-Json -Depth 8) | Set-Content $path -Encoding utf8
}
function Get-RelayUrl {
  $cfg = Read-Json $ConfigFile
  $st = Read-Json $StateFile
  if ($cfg.relayUrl) { return [string]$cfg.relayUrl }
  if ($st.relayUrl) { return [string]$st.relayUrl }
  ''
}
function Save-RelayUrl([string]$url) {
  $trimmed = $url.Trim()
  $cfg = Read-Json $ConfigFile
  if (-not $cfg) { $cfg = [pscustomobject]@{} }
  $st = Read-Json $StateFile
  if (-not $st) { $st = [pscustomobject]@{} }
  if (-not $trimmed) {
    $cfg.PSObject.Properties.Remove('relayUrl')
  } else {
    if (-not $trimmed.EndsWith('/')) { $trimmed += '/' }
    $cfg | Add-Member -NotePropertyName relayUrl -NotePropertyValue $trimmed -Force
    $st | Add-Member -NotePropertyName relayUrl -NotePropertyValue $trimmed -Force
    Write-Json $StateFile $st
  }
  Write-Json $ConfigFile $cfg
}
function Test-PortOpen([int]$p) {
  try {
    $c = [System.Net.Sockets.TcpClient]::new()
    $iar = $c.BeginConnect('127.0.0.1', $p, $null, $null)
    $ok = $iar.AsyncWaitHandle.WaitOne(200, $false)
    if ($ok -and $c.Connected) { $c.Close(); return $true }
    $c.Close(); return $false
  } catch { return $false }
}
function Test-Cdp([int]$p) {
  try {
    $r = Invoke-RestMethod -Uri "http://127.0.0.1:$p/json/version" -TimeoutSec 1
    return ($r.Browser -match 'Cursor|Chrome')
  } catch { return $false }
}
function Get-CdpPorts {
  @(9333, 9222, 9223, 9224) | Where-Object { Test-Cdp $_ }
}
function Stop-RelayListeners {
  Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | ForEach-Object {
    $proc = Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue
    if ($proc -and $proc.ProcessName -eq 'node') {
      Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    }
  }
}
function Stop-Relay {
  if ($script:RelayProc -and -not $script:RelayProc.HasExited) {
    try { $script:RelayProc.Kill() } catch {}
  }
  $script:RelayProc = $null
  Stop-RelayListeners
}
function Start-Relay {
  Stop-Relay
  Start-Sleep -Milliseconds 400
  $url = Get-RelayUrl
  $cdp = @(Get-CdpPorts)
  $env:CDP_PORT = if ($cdp.Count) { "$($cdp[0])" } else { '9333' }
  $env:CDP_PORTS = ((@($cdp) + 9222, 9333, 9223, 9224) | Select-Object -Unique) -join ','
  $args = @('server.mjs')
  if ($url) {
    $env:CR_RELAY_URL = $url
    $args += '--relay'
  } else {
    $args += '--tunnel'
  }
  $script:RelayProc = Start-Process -FilePath 'node' -ArgumentList $args -WorkingDirectory $Root -WindowStyle Hidden -PassThru
  for ($i = 0; $i -lt 20; $i++) {
    if (Test-PortOpen $Port) {
      if ($url) { return "已启动中继：$url" }
      return '已启动（Cloudflare 隧道）'
    }
    Start-Sleep -Milliseconds 250
  }
  return '已启动进程，正在等待端口就绪…'
}
function Ensure-Cursor {
  $ports = @(Get-CdpPorts)
  if ($ports.Count) { return "已检测到 Cursor 调试端口：$($ports -join ', ')" }
  $script = Join-Path $Root 'start-cursor.ps1'
  if (-not (Test-Path $script)) { throw '找不到 start-cursor.ps1' }
  $env:CDP_PORT = '9333'
  & powershell -NoProfile -ExecutionPolicy Bypass -File $script
  for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 1
    $ports = @(Get-CdpPorts)
    if ($ports.Count) { return 'Cursor 已带调试端口启动' }
  }
  throw 'Cursor 调试端口仍未就绪'
}
function New-PairLink {
  $out = & node (Join-Path $Root 'desktop\pair-link.mjs') 2>&1
  $text = ($out | Out-String).Trim()
  $j = $text | ConvertFrom-Json
  if ($j.error) { throw $j.error }
  return $j
}

# --- UI (Ant ERP tokens) ---
$form = New-Object Windows.Forms.Form
$form.Text = 'Cursor Remote Lite'
$form.Size = New-Object Drawing.Size(580, 720)
$form.StartPosition = 'CenterScreen'
$form.BackColor = [Drawing.Color]::FromArgb(245, 245, 245)
$form.Font = New-Object Drawing.Font('Microsoft YaHei UI', 9)

function New-Label($text, $x, $y, $w, $bold = $false) {
  $l = New-Object Windows.Forms.Label
  $l.Text = $text; $l.Location = New-Object Drawing.Point($x, $y); $l.Size = New-Object Drawing.Size($w, 22)
  if ($bold) { $l.Font = New-Object Drawing.Font($form.Font, [Drawing.FontStyle]::Bold) }
  $form.Controls.Add($l); return $l
}
function New-Hint($text, $x, $y, $w) {
  $l = New-Label $text $x $y $w
  $l.ForeColor = [Drawing.Color]::FromArgb(105, 115, 134)
  $l.Height = 36
  return $l
}
function New-Btn($text, $x, $y, $primary = $false) {
  $b = New-Object Windows.Forms.Button
  $b.Text = $text; $b.Location = New-Object Drawing.Point($x, $y); $b.Size = New-Object Drawing.Size(120, 32)
  $b.FlatStyle = 'Flat'
  if ($primary) {
    $b.BackColor = [Drawing.Color]::FromArgb(0, 122, 255)
    $b.ForeColor = [Drawing.Color]::White
    $b.FlatAppearance.BorderColor = [Drawing.Color]::FromArgb(0, 122, 255)
  } else {
    $b.BackColor = [Drawing.Color]::White
    $b.FlatAppearance.BorderColor = [Drawing.Color]::FromArgb(217, 217, 217)
  }
  $form.Controls.Add($b); return $b
}

[void](New-Label 'Cursor Remote Lite' 24 16 300 $true)
[void](New-Hint '电脑端面板 · iPhone 用 Safari 扫码添加到主屏幕（不用装 App）' 24 40 500)

$statusLbl = New-Label '状态：读取中…' 24 80 500
$urlBox = New-Object Windows.Forms.TextBox
$urlBox.Location = New-Object Drawing.Point(24, 140)
$urlBox.Size = New-Object Drawing.Size(400, 32)
$urlBox.Text = Get-RelayUrl
$form.Controls.Add($urlBox)
[void](New-Label '公网地址 relayUrl' 24 114 300)
[void](New-Hint 'http:// 也可以；配对会打开中继本身' 24 172 500)

$btnSave = New-Btn '保存' 436 138
$btnCursor = New-Btn '检查 Cursor' 24 220
$btnStop = New-Btn '停止中继' 156 220
$btnStart = New-Btn '一键启动中继' 288 220 $true

$qrBox = New-Object Windows.Forms.PictureBox
$qrBox.Location = New-Object Drawing.Point(24, 300)
$qrBox.Size = New-Object Drawing.Size(240, 240)
$qrBox.BorderStyle = 'FixedSingle'
$qrBox.SizeMode = 'Zoom'
$form.Controls.Add($qrBox)
[void](New-Label '配对 iPhone' 24 270 200 $true)

$linkBox = New-Object Windows.Forms.TextBox
$linkBox.Location = New-Object Drawing.Point(280, 300)
$linkBox.Size = New-Object Drawing.Size(260, 120)
$linkBox.Multiline = $true
$linkBox.ReadOnly = $true
$linkBox.ScrollBars = 'Vertical'
$form.Controls.Add($linkBox)

$btnPair = New-Btn '生成配对二维码' 280 440 $true
$btnCopy = New-Btn '复制链接' 410 440
$hintPair = New-Hint '扫码 → Safari → 分享 → 添加到主屏幕' 280 480 260

$toast = New-Label '' 24 640 520
$toast.ForeColor = [Drawing.Color]::FromArgb(0, 122, 255)

function Show-Toast($msg, $err = $false) {
  $toast.Text = $msg
  $toast.ForeColor = if ($err) { [Drawing.Color]::FromArgb(255, 77, 79) } else { [Drawing.Color]::FromArgb(0, 122, 255) }
}
function Update-Status {
  $listening = Test-PortOpen $Port
  $alive = $script:RelayProc -and -not $script:RelayProc.HasExited
  $cdp = @(Get-CdpPorts)
  $cdpText = if ($cdp.Count) { ($cdp | ForEach-Object { ":$_" }) -join ' ' } else { '未检测到' }
  if ($listening) { $statusLbl.Text = "状态：中继在线 · 端口 $Port · Cursor CDP $cdpText"; $statusLbl.ForeColor = [Drawing.Color]::FromArgb(19, 82, 0) }
  elseif ($alive) { $statusLbl.Text = "状态：启动中… · Cursor CDP $cdpText"; $statusLbl.ForeColor = [Drawing.Color]::FromArgb(173, 104, 0) }
  else { $statusLbl.Text = "状态：中继已停止 · Cursor CDP $cdpText"; $statusLbl.ForeColor = [Drawing.Color]::FromArgb(0, 0, 0) }
}

$btnSave.Add_Click({
  try { Save-RelayUrl $urlBox.Text; Show-Toast '已保存公网地址' } catch { Show-Toast $_.Exception.Message $true }
})
$btnStart.Add_Click({
  try { Show-Toast (Start-Relay); Update-Status } catch { Show-Toast $_.Exception.Message $true }
})
$btnStop.Add_Click({
  try { Stop-Relay; Show-Toast '已停止中继'; Update-Status } catch { Show-Toast $_.Exception.Message $true }
})
$btnCursor.Add_Click({
  try { Show-Toast (Ensure-Cursor); Update-Status } catch { Show-Toast $_.Exception.Message $true }
})
$btnPair.Add_Click({
  try {
    $j = New-PairLink
    $linkBox.Text = $j.link
    $hintPair.Text = "$($j.minutes) 分钟内有效，只能用一次"
    # QR via online chart API (no extra npm); falls back to link-only if offline
    $enc = [Uri]::EscapeDataString($j.link)
    $tmp = Join-Path $env:TEMP 'cr-pair-qr.png'
    try {
      Invoke-WebRequest -Uri "https://api.qrserver.com/v1/create-qr-code/?size=240x240&data=$enc" -OutFile $tmp -TimeoutSec 8
      $qrBox.ImageLocation = $tmp
    } catch {
      $qrBox.Image = $null
      Show-Toast '二维码图下载失败，请用右侧链接' $true
    }
    Show-Toast "配对码已生成（$($j.minutes) 分钟内有效）"
  } catch { Show-Toast $_.Exception.Message $true }
})
$btnCopy.Add_Click({
  if ($linkBox.Text) { [Windows.Forms.Clipboard]::SetText($linkBox.Text); Show-Toast '已复制配对链接' }
})

$timer = New-Object Windows.Forms.Timer
$timer.Interval = 2500
$timer.Add_Tick({ Update-Status })
$timer.Start()
Update-Status

$form.Add_FormClosing({
  # keep relay running when closing panel; only stop if user wants — leave process
  $timer.Stop()
})

[Windows.Forms.Application]::EnableVisualStyles()
[void]$form.ShowDialog()
