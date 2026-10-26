**中文** | [English](README.en.md)

# Cursor Remote Lite

**电脑不用带在身边，也能正常干活。** 电脑留在家里或公司开着，出门只带手机，就能接着用电脑上的 Cursor：给 Agent 布置任务、看进度、回答提问、批准命令、检查改动。本地窗口和 SSH 远程窗口都行。  
免费、开源、自托管；Windows 提供桌面面板（`.exe`），iPhone 用 Safari「添加到主屏幕」即可（也可用任意手机浏览器）。

> 灵感来自 [len5ky/CursorRemote](https://github.com/len5ky/CursorRemote)，这是一个从零实现的免费版本。本仓库 fork 强化了 **Windows 桌面面板 + ChmlFrp/frp 固定隧道** 路径。

<p align="center"><img src="docs/ui-desktop-pair.png" width="420" alt="Windows 桌面面板：中继状态、公网地址、配对二维码"></p>

<p align="center"><sub>Windows 桌面面板（RustStor 风格）：一键启动中继 · 检查 Cursor CDP · 生成 iPhone 配对二维码 · 中英文切换</sub></p>

## 界面

| Windows 桌面面板 | iPhone 配对 | 手机聊天 | 切换窗口 |
|:-:|:-:|:-:|:-:|
| <img src="docs/ui-desktop.png" width="200" alt="桌面面板"> | <img src="docs/ui-pair.png" width="160" alt="配对"> | <img src="docs/ui-chat.png" width="160" alt="聊天"> | <img src="docs/ui-windows.png" width="160" alt="切换窗口"> |
| **选模型** | **模型参数** | **运行中** | **改动的文件** |
| <img src="docs/ui-model.png" width="160" alt="选模型"> | <img src="docs/ui-params.png" width="160" alt="模型参数"> | <img src="docs/ui-running.png" width="160" alt="运行中"> | <img src="docs/ui-changes.png" width="160" alt="改动的文件"> |

## 为什么做这个

早上出门前，你在电脑上让 Cursor Agent 改 bug（本地或 SSH 远程窗口）。通勤、开会、出差时，Agent 会等你验收、点「运行」，或提问「返回 401 还是自动续期？」。你也会突然想到新需求。

**你需要的不是「远程看一眼」，而是电脑不在身边时也能正常干活**：布置任务、回答问题、批准命令、看改了哪些文件。环境必须是你自己那台电脑上的 Cursor。

### 和其他方案

| | Cursor Remote Lite | Cursor 官方 iOS | len5ky/CursorRemote | 远程桌面 |
|---|---|---|---|---|
| 价格 | 免费 MIT | 付费套餐 | $7.99/月 | 各异 |
| 电脑端 | Windows `.exe` 面板 / CLI | — | CLI | — |
| 手机 | Safari / 浏览器 PWA（安卓浏览器也可） | 仅 iPhone / iPad | 浏览器 / Telegram | 各平台 |
| 窗口 | 本地 / SSH / Agents | 主要 Agents | 本机 | 整个桌面 |
| 数据路径 | 只经你的电脑 + 自建隧道 | 云端 Agent | 局域网 / Tailscale | 厂商服务器 |
| 外网 | ChmlFrp / frp / Cloudflare | 自带 | 自备 | 自带 |

**本项目的不足**：靠读取 Cursor 界面实现，大改版可能要跟进；电脑不能睡眠；macOS 后台服务脚本可用，Windows 推荐用桌面面板常开。

## 功能摘要

- **桌面面板（Windows）**：填写 `relayUrl`、启停中继、检查 Cursor 调试端口、生成配对二维码、看手机是否在线；界面跟随 RustStor 控制台配色（`#007aff` 等）。
- **聊天模式**：气泡对话、工具卡片、确认按钮、排队消息、子 Agent、改动文件 Keep/Undo。
- **画面模式**：实时画面点击 / 滚动。
- **通知**：Agent 完成或等待确认时提醒（Web Push；iPhone 需「添加到主屏幕」）。
- **多窗口 / 模式 / 模型 / 参数**：手机上切换。
- **设备配对**：一次性二维码；可选 Authenticator 2FA。

## 原理

```
iPhone Safari PWA  ──HTTP(S)/WebSocket──>  frp / Cloudflare  ──>  本机中继 (Node :3939)  ──CDP──>  Cursor
                                                      ↑
                                            Windows 桌面面板启停
```

Cursor 需用 `--remote-debugging-port` 启动（面板「检查 Cursor」或 `scripts/start-cursor.ps1`）。

## 要求

- **Windows 10+**（推荐）或 macOS / Linux（CLI）
- Node.js 22+
- [.NET 8 SDK](https://dotnet.microsoft.com/download)（仅编译桌面面板时需要）
- 外网任选其一：
  - [ChmlFrp](https://www.chmlfrp.net/) / 其它 frp（推荐固定地址）
  - [cloudflared](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/downloads/) 快速隧道（地址会变）
- 可选：[GitHub CLI](https://cli.github.com/)（固定 GitHub Pages App 地址时）

## 快速上手（Windows + iPhone）

**1. 安装并打开桌面面板**

```powershell
git clone https://github.com/vce27/cursor-remote-lite.git
cd cursor-remote-lite
npm install
npm run desktop:build   # 生成 bin\CursorRemoteLite.exe
npm run desktop         # 打开面板，并创建桌面快捷方式
```

之后可双击桌面上的 **Cursor Remote Lite**，或直接运行 `bin\CursorRemoteLite.exe`。右上角 **EN / 中文** 切换语言。

**2. 配置隧道并启动中继**

1. 在 ChmlFrp（或其它 frp）把公网端口映射到本机 `127.0.0.1:3939`。
2. 在面板填入公网地址，例如 `http://x.x.x.x:端口/`，点「保存」。
3. 点「检查 Cursor」（必要时会按调试端口重启 Cursor）。
4. 点「一键启动中继」。状态应显示中继在线、CDP 端口。

`http://` 也可以：配对会打开中继本身，避免 HTTPS 页面访问 HTTP 中继的混合内容问题。

**3. 配对 iPhone**

1. 面板点「生成配对二维码」。
2. iPhone 相机扫码 → **用 Safari 打开**。
3. 分享 → **添加到主屏幕**。

<p align="center"><img src="docs/ui-pair.png" width="240" alt="手机未配对界面"></p>

**4. 日常使用**

打开桌面面板 → 一键启动中继 → 手机从主屏幕图标进入。  
左上角是当前 Cursor 窗口；底部发消息；「画面」看实时界面；「⋯」里开通知、切语言。

> 若手机停在「连接中… / 未连上 Cursor」：中继可能已通，但 Cursor 没开调试端口 → 面板点「检查 Cursor」。

## 通知

| 端 | 说明 |
|---|---|
| iPhone | 须「添加到主屏幕」后从图标打开；iOS 16.4+ Web Push |
| 安卓浏览器 PWA | 允许通知即可；国产机注意后台限制 |
| 电脑浏览器 | 允许通知 |

提醒时机：等确认约 10 秒以上；Agent 停止时带回复摘要。正在看的窗口不重复提醒。

## 让电脑一直在线（防睡眠）

中继跑在你电脑上，**一睡眠手机就断**。允许关屏 / 锁屏，但不要睡眠。

### Windows

设置 → 系统 → 电源：「接通电源后睡眠」选 **从不**。或：

```powershell
powercfg /change standby-timeout-ac 0
powercfg /change monitor-timeout-ac 10
powercfg /change hibernate-timeout-ac 0
```

### macOS

系统设置 → 电池 / 节能：防止自动睡眠；可用 `./scripts/service.sh install` 做登录自启中继。合盖会睡，远程用时不要合盖。

## 命令行备选（不用面板时）

```powershell
# 写入配置后一键：Cursor CDP + 中继
# {"relayUrl":"http://你的地址/"} → %USERPROFILE%\.cursor-remote-lite\config.json
npm run phone
# 或
powershell -ExecutionPolicy Bypass -File .\scripts\start-phone.ps1
```

| 命令 | 作用 |
|---|---|
| `npm run desktop` / `desktop:build` | 打开 / 编译 Windows 面板 |
| `npm run phone` | Windows CLI：CDP + 中继 |
| `npm run public:relay` | 仅中继（需 `CR_RELAY_URL`） |
| `npm run public` | Cloudflare 快速隧道 |
| `npm run pair` | 终端配对二维码 |
| `npm run devices` / `2fa` / `reset` | 设备与二次验证 |
| `./scripts/start-cursor.sh` | macOS：带调试端口重启 Cursor |
| `./scripts/service.sh install` | macOS LaunchAgent |

## 安全说明

- 配对链接含随机密钥 + 一次性码，10 分钟有效、用过即废。
- 电脑只存设备令牌哈希；可选 `npm run 2fa`。
- 调试端口与中继默认只绑本机；公网经你的隧道进入。
- **不要把配对链接发到群里。**

## 常见问题

**手机「未连上 Cursor」/ 一直连接中**  
Cursor 没用调试端口启动。面板点「检查 Cursor」，或运行 `scripts/start-cursor.ps1`（会先退出再打开 Cursor）。

**面板显示手机已连接，但手机仍转圈**  
WebSocket 已通，缺的是 CDP。同上。

**关掉面板后手机断了**  
关面板会停止中继（有意为之）。保持面板开启，或用 `npm run phone` / `service.sh` 常驻。

**公司网打不开 Cloudflare**  
改用 ChmlFrp 等 frp；或同局域网 `npm start`。

**SSH 窗口看不到文件**  
本机需能免密 `ssh` 到那台机器。

## 赞赏

| 微信 | 支付宝 |
|---|---|
| <img src="app/donate-wechat.jpg" width="220" alt="微信"> | <img src="app/donate-alipay.jpg" width="220" alt="支付宝"> |

`app/donate.json` 可改赞赏内容。

## License

MIT
