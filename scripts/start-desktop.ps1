# Launches the Cursor Remote Lite desktop .exe (builds it if missing).
# UTF-8 BOM
$ErrorActionPreference = 'Stop'
$Root = Split-Path $PSScriptRoot -Parent
$Exe = Join-Path $Root 'bin\CursorRemoteLite.exe'
$Proj = Join-Path $Root 'desktop-win\CursorRemoteLite.csproj'
$Ico = Join-Path $Root 'desktop-win\app.ico'

if (-not (Test-Path $Exe)) {
  Write-Host 'Building desktop app (first time)...'
  if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet SDK not found. Install .NET 8+ from https://dotnet.microsoft.com/download'
  }
  New-Item -ItemType Directory -Force -Path (Join-Path $Root 'bin') | Out-Null
  & dotnet publish $Proj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o (Join-Path $Root 'bin')
  if (-not (Test-Path $Exe)) { throw 'Build failed: bin\CursorRemoteLite.exe missing' }
}

# Desktop shortcut (refresh target + icon)
try {
  $desk = [Environment]::GetFolderPath('Desktop')
  $lnkPath = Join-Path $desk 'Cursor Remote Lite.lnk'
  $w = New-Object -ComObject WScript.Shell
  $lnk = $w.CreateShortcut($lnkPath)
  $lnk.TargetPath = $Exe
  $lnk.WorkingDirectory = $Root
  $lnk.Description = 'Cursor Remote Lite'
  if (Test-Path $Ico) { $lnk.IconLocation = "$Ico,0" }
  $lnk.Save()
} catch {}

Start-Process -FilePath $Exe -WorkingDirectory $Root
