# One-click phone remote on Windows: ensure Cursor has a CDP port, then start the relay.
# Run outside Cursor's terminal (so it survives Cursor restarts):
#   powershell -ExecutionPolicy Bypass -File .\start-phone.ps1
#
# Optional env:
#   CR_RELAY_URL / config.json relayUrl  — ChmlFrp (or other) public URL; enables --relay
#   CDP_PORT                            — preferred debugging port when launching Cursor (default 9333)
#   PORT                                — relay listen port (default 3939)
param(
  [switch]$SkipCursor,
  [string]$RelayUrl = $env:CR_RELAY_URL
)
$ErrorActionPreference = 'Stop'
$Root = $PSScriptRoot
$DataDir = Join-Path $HOME '.cursor-remote-lite'
$StateFile = Join-Path $DataDir 'state.json'
$ConfigFile = Join-Path $DataDir 'config.json'
New-Item -ItemType Directory -Force -Path $DataDir | Out-Null

function Read-Json($path) {
  if (-not (Test-Path $path)) { return $null }
  try { return Get-Content $path -Raw | ConvertFrom-Json } catch { $null }
}

function Test-CdpPort([int]$Port) {
  try {
    $v = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/json/version" -TimeoutSec 1
    return ($v.Browser -match 'Cursor|Chrome')
  } catch { $false }
}

function Find-CursorCdpPorts {
  $ports = @(9222, 9333, 9223, 9224)
  if ($env:CDP_PORT) { $ports = @([int]$env:CDP_PORT) + $ports }
  $found = @()
  foreach ($p in ($ports | Select-Object -Unique)) {
    if (Test-CdpPort $p) { $found += $p }
  }
  $found
}

# Resolve public relay URL: arg → env → config → state
if (-not $RelayUrl) {
  $cfg = Read-Json $ConfigFile
  $st = Read-Json $StateFile
  if ($cfg.relayUrl) { $RelayUrl = [string]$cfg.relayUrl }
  elseif ($st.relayUrl) { $RelayUrl = [string]$st.relayUrl }
}

Write-Host ''
Write-Host '=== Cursor Remote Lite · 手机一键启动 ===' -ForegroundColor Cyan

$cdpPorts = Find-CursorCdpPorts
if (-not $SkipCursor) {
  if ($cdpPorts.Count -gt 0) {
    Write-Host "已检测到 Cursor 调试端口: $($cdpPorts -join ', ')"
  } else {
    # Prefer a free port so we don't collide with Edge leftovers on 9222.
    function Test-PortBusy([int]$Port) {
      try {
        $tcp = [System.Net.Sockets.TcpClient]::new()
        $tcp.Connect('127.0.0.1', $Port)
        $tcp.Close()
        $true
      } catch { $false }
    }
    $launchPort = $null
    $prefer = @()
    if ($env:CDP_PORT) { $prefer += [int]$env:CDP_PORT }
    $prefer += 9333, 9223, 9224, 9222
    foreach ($p in ($prefer | Select-Object -Unique)) {
      if (-not (Test-PortBusy $p)) { $launchPort = "$p"; break }
    }
    if (-not $launchPort) { $launchPort = '9333' }
    Write-Host "未检测到带调试端口的 Cursor，将用端口 $launchPort 重启 Cursor…"
    $env:CDP_PORT = "$launchPort"
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Root 'start-cursor.ps1')
    for ($i = 0; $i -lt 45; $i++) {
      Start-Sleep -Seconds 1
      $cdpPorts = Find-CursorCdpPorts
      if ($cdpPorts.Count -gt 0) { break }
    }
    if ($cdpPorts.Count -eq 0) {
      Write-Host '!! Cursor 调试端口仍未就绪。请确认 start-cursor.ps1 已跑完，或手动：Cursor.exe --remote-debugging-port=9333' -ForegroundColor Yellow
    } else {
      Write-Host "Cursor 已就绪: $($cdpPorts -join ', ')"
    }
  }
}

# Prefer the first live Cursor CDP for this process (server also scans multiple ports).
if ($cdpPorts.Count -gt 0) {
  $env:CDP_PORT = "$($cdpPorts[0])"
  $env:CDP_PORTS = ($cdpPorts + @(9222, 9333, 9223, 9224) | Select-Object -Unique) -join ','
}

# Stop a previous relay on the same PORT so we can bind.
$listenPort = if ($env:PORT) { [int]$env:PORT } else { 3939 }
Get-NetTCPConnection -LocalPort $listenPort -State Listen -ErrorAction SilentlyContinue |
  ForEach-Object {
    try {
      $proc = Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue
      if ($proc -and $proc.ProcessName -eq 'node') {
        Write-Host "结束旧中继 (pid $($proc.Id))…"
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
      }
    } catch {}
  }
Start-Sleep -Milliseconds 500

Set-Location $Root
if (-not (Test-Path (Join-Path $Root 'node_modules'))) {
  Write-Host '首次运行：npm install…'
  npm install
}

if ($RelayUrl) {
  $env:CR_RELAY_URL = $RelayUrl
  Write-Host "启动中继（外部隧道）：$RelayUrl"
  Write-Host '保持本窗口开启。配对：npm run pair' -ForegroundColor Green
  Write-Host ''
  node server.mjs --relay
} else {
  Write-Host '未配置 CR_RELAY_URL / relayUrl，使用 Cloudflare 快速隧道（地址会变）。'
  Write-Host '若用 ChmlFrp：先设 $env:CR_RELAY_URL = "http://或https://你的地址/" 再运行本脚本。'
  Write-Host '保持本窗口开启。配对：npm run pair' -ForegroundColor Green
  Write-Host ''
  node server.mjs --tunnel
}
