# Cursor Remote Lite - Windows desktop panel
# Usage: powershell -ExecutionPolicy Bypass -File .\start-desktop.ps1
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
  if ($cfg -and $cfg.relayUrl) { return [string]$cfg.relayUrl }
  if ($st -and $st.relayUrl) { return [string]$st.relayUrl }
  return ''
}
function Save-RelayUrl([string]$url) {
  $trimmed = $url.Trim()
  $cfg = Read-Json $ConfigFile
  if (-not $cfg) { $cfg = [pscustomobject]@{} }
  $st = Read-Json $StateFile
  if (-not $st) { $st = [pscustomobject]@{} }
  if (-not $trimmed) {
    if ($cfg.PSObject.Properties['relayUrl']) { $cfg.PSObject.Properties.Remove('relayUrl') }
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
    try { $c.Close() } catch {}
    return $false
  } catch { return $false }
}
function Test-Cdp([int]$p) {
  try {
    $r = Invoke-RestMethod -Uri "http://127.0.0.1:$p/json/version" -TimeoutSec 1
    return ($r.Browser -match 'Cursor|Chrome')
  } catch { return $false }
}
function Get-CdpPorts {
  return @(9333, 9222, 9223, 9224) | Where-Object { Test-Cdp $_ }
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
  $env:CDP_PORTS = ((@($cdp) + @(9222, 9333, 9223, 9224)) | Select-Object -Unique) -join ','
  $argList = @('server.mjs')
  if ($url) {
    $env:CR_RELAY_URL = $url
    $argList += '--relay'
  } else {
    $argList += '--tunnel'
  }
  $script:RelayProc = Start-Process -FilePath 'node' -ArgumentList $argList -WorkingDirectory $Root -WindowStyle Hidden -PassThru
  for ($i = 0; $i -lt 20; $i++) {
    if (Test-PortOpen $Port) {
      if ($url) { return "OK relay: $url" }
      return 'OK (cloudflare tunnel)'
    }
    Start-Sleep -Milliseconds 250
  }
  return 'Started, waiting for port...'
}
function Ensure-CursorReady {
  $ports = @(Get-CdpPorts)
  if ($ports.Count) { return ("CDP ports: " + ($ports -join ', ')) }
  $scriptPath = Join-Path $Root 'start-cursor.ps1'
  if (-not (Test-Path $scriptPath)) { throw 'start-cursor.ps1 not found' }
  $env:CDP_PORT = '9333'
  & powershell -NoProfile -ExecutionPolicy Bypass -File $scriptPath
  for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 1
    $ports = @(Get-CdpPorts)
    if ($ports.Count) { return 'Cursor CDP ready' }
  }
  throw 'Cursor CDP not ready'
}
function New-PairLink {
  $out = & node (Join-Path $Root 'desktop\pair-link.mjs') 2>&1
  $text = ($out | Out-String).Trim()
  $j = $text | ConvertFrom-Json
  if ($j.error) { throw $j.error }
  return $j
}

$form = New-Object Windows.Forms.Form
$form.Text = 'Cursor Remote Lite'
$form.Size = New-Object Drawing.Size(580, 720)
$form.StartPosition = 'CenterScreen'
$form.BackColor = [Drawing.Color]::FromArgb(245, 245, 245)
$form.Font = New-Object Drawing.Font('Microsoft YaHei UI', 9)

function New-Label($text, $x, $y, $w, $bold = $false) {
  $l = New-Object Windows.Forms.Label
  $l.Text = $text
  $l.Location = New-Object Drawing.Point($x, $y)
  $l.Size = New-Object Drawing.Size($w, 22)
  if ($bold) { $l.Font = New-Object Drawing.Font($form.Font, [Drawing.FontStyle]::Bold) }
  $form.Controls.Add($l)
  return $l
}
function New-Hint($text, $x, $y, $w) {
  $l = New-Label $text $x $y $w
  $l.ForeColor = [Drawing.Color]::FromArgb(105, 115, 134)
  $l.Height = 36
  return $l
}
function New-Btn($text, $x, $y, $w = 120, $primary = $false) {
  $b = New-Object Windows.Forms.Button
  $b.Text = $text
  $b.Location = New-Object Drawing.Point($x, $y)
  $b.Size = New-Object Drawing.Size($w, 32)
  $b.FlatStyle = 'Flat'
  if ($primary) {
    $b.BackColor = [Drawing.Color]::FromArgb(0, 122, 255)
    $b.ForeColor = [Drawing.Color]::White
    $b.FlatAppearance.BorderColor = [Drawing.Color]::FromArgb(0, 122, 255)
  } else {
    $b.BackColor = [Drawing.Color]::White
    $b.FlatAppearance.BorderColor = [Drawing.Color]::FromArgb(217, 217, 217)
  }
  $form.Controls.Add($b)
  return $b
}

[void](New-Label 'Cursor Remote Lite' 24 16 300 $true)
[void](New-Hint 'Desktop panel - iPhone: Safari scan QR, Add to Home Screen' 24 40 520)

$statusLbl = New-Label 'Status: ...' 24 80 520
[void](New-Label 'Public URL (relayUrl)' 24 114 300)
$urlBox = New-Object Windows.Forms.TextBox
$urlBox.Location = New-Object Drawing.Point(24, 140)
$urlBox.Size = New-Object Drawing.Size(400, 28)
$urlBox.Text = Get-RelayUrl
$form.Controls.Add($urlBox)
[void](New-Hint 'http:// is OK - pairing opens the relay itself' 24 172 500)

$btnSave = New-Btn 'Save' 436 138 100
$btnCursor = New-Btn 'Check Cursor' 24 220 120
$btnStop = New-Btn 'Stop Relay' 156 220 120
$btnStart = New-Btn 'Start Relay' 288 220 140 $true

[void](New-Label 'Pair iPhone' 24 270 200 $true)
$qrBox = New-Object Windows.Forms.PictureBox
$qrBox.Location = New-Object Drawing.Point(24, 300)
$qrBox.Size = New-Object Drawing.Size(240, 240)
$qrBox.BorderStyle = 'FixedSingle'
$qrBox.SizeMode = 'Zoom'
$qrBox.BackColor = [Drawing.Color]::White
$form.Controls.Add($qrBox)

$linkBox = New-Object Windows.Forms.TextBox
$linkBox.Location = New-Object Drawing.Point(280, 300)
$linkBox.Size = New-Object Drawing.Size(260, 120)
$linkBox.Multiline = $true
$linkBox.ReadOnly = $true
$linkBox.ScrollBars = 'Vertical'
$form.Controls.Add($linkBox)

$btnPair = New-Btn 'Make Pair QR' 280 440 140 $true
$btnCopy = New-Btn 'Copy Link' 430 440 110
$hintPair = New-Hint 'Scan -> Safari -> Share -> Add to Home Screen' 280 480 260

$toast = New-Label '' 24 640 520
$toast.ForeColor = [Drawing.Color]::FromArgb(0, 122, 255)

function Show-Toast($msg, $err = $false) {
  $toast.Text = $msg
  if ($err) {
    $toast.ForeColor = [Drawing.Color]::FromArgb(255, 77, 79)
  } else {
    $toast.ForeColor = [Drawing.Color]::FromArgb(0, 122, 255)
  }
}
function Update-Status {
  $listening = Test-PortOpen $Port
  $alive = $script:RelayProc -and -not $script:RelayProc.HasExited
  $cdp = @(Get-CdpPorts)
  $cdpText = if ($cdp.Count) { ($cdp | ForEach-Object { ":$_" }) -join ' ' } else { 'none' }
  if ($listening) {
    $statusLbl.Text = "Status: ONLINE  port $Port  Cursor CDP $cdpText"
    $statusLbl.ForeColor = [Drawing.Color]::FromArgb(19, 82, 0)
  } elseif ($alive) {
    $statusLbl.Text = "Status: starting...  Cursor CDP $cdpText"
    $statusLbl.ForeColor = [Drawing.Color]::FromArgb(173, 104, 0)
  } else {
    $statusLbl.Text = "Status: OFF  Cursor CDP $cdpText"
    $statusLbl.ForeColor = [Drawing.Color]::Black
  }
}

$btnSave.Add_Click({
  try { Save-RelayUrl $urlBox.Text; Show-Toast 'Saved relayUrl' } catch { Show-Toast $_.Exception.Message $true }
})
$btnStart.Add_Click({
  try { Show-Toast (Start-Relay); Update-Status } catch { Show-Toast $_.Exception.Message $true }
})
$btnStop.Add_Click({
  try { Stop-Relay; Show-Toast 'Relay stopped'; Update-Status } catch { Show-Toast $_.Exception.Message $true }
})
$btnCursor.Add_Click({
  try { Show-Toast (Ensure-CursorReady); Update-Status } catch { Show-Toast $_.Exception.Message $true }
})
$btnPair.Add_Click({
  try {
    $j = New-PairLink
    $linkBox.Text = $j.link
    $hintPair.Text = "$($j.minutes) min, one-time use"
    $enc = [Uri]::EscapeDataString($j.link)
    $tmp = Join-Path $env:TEMP 'cr-pair-qr.png'
    try {
      Invoke-WebRequest -Uri "https://api.qrserver.com/v1/create-qr-code/?size=240x240&data=$enc" -OutFile $tmp -TimeoutSec 8
      $qrBox.ImageLocation = $tmp
    } catch {
      $qrBox.Image = $null
      Show-Toast 'QR image failed; use link on the right' $true
      return
    }
    Show-Toast "Pair code ready ($($j.minutes) min)"
  } catch { Show-Toast $_.Exception.Message $true }
})
$btnCopy.Add_Click({
  if ($linkBox.Text) {
    [Windows.Forms.Clipboard]::SetText($linkBox.Text)
    Show-Toast 'Link copied'
  }
})

$timer = New-Object Windows.Forms.Timer
$timer.Interval = 2500
$timer.Add_Tick({ Update-Status })
$timer.Start()
Update-Status

$form.Add_FormClosing({ $timer.Stop() })

[Windows.Forms.Application]::EnableVisualStyles()
[void]$form.ShowDialog()