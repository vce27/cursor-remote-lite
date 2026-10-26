# Cursor Remote Lite (English)

Full docs live in [README.md](./README.md) (Chinese primary; UI supports English via the desktop panel language toggle and the phone app language setting).

## Quick start (Windows + iPhone)

```powershell
git clone https://github.com/vce27/cursor-remote-lite.git
cd cursor-remote-lite
npm install
npm run desktop:build
npm run desktop
```

1. Paste your FRP / ChmlFrp public URL (`relayUrl`), Save, **Start Relay**.
2. **Make Pair QR** → scan with iPhone → Safari → Share → **Add to Home Screen**.
3. Use the phone PWA to chat with desktop Cursor (start Cursor with debugging port via the panel or `scripts/start-cursor.ps1`).

## Scripts

| Command | What it does |
| --- | --- |
| `npm run desktop` / `npm run desktop:build` | WinForms panel + `bin\CursorRemoteLite.exe` |
| `npm run phone` | CLI one-shot (Cursor CDP + relay) |
| `npm run public:relay` | Relay only (`CR_RELAY_URL` / config `relayUrl`) |
| `npm run pair` | Pairing QR in the terminal |
| `./scripts/start-cursor.sh` | macOS: relaunch Cursor with CDP |
| `./scripts/service.sh install` | macOS LaunchAgent |

License: MIT. Upstream: [Suchenl/cursor-remote-lite](https://github.com/Suchenl/cursor-remote-lite).
