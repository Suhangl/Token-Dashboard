# Token Dashboard
[![Build](https://github.com/Suhangl/Token-Dashboard/actions/workflows/windows-build.yml/badge.svg)](https://github.com/Suhangl/Token-Dashboard/actions)

Single-file WPF desktop widget for monitoring **Codex**, **MiniMax**, and **DeepSeek** token quotas, balances, and local usage estimates. Zero dependencies beyond .NET Framework 4.0+ (built into Windows).

![screenshot](docs/screenshot.png)

## License

**All rights reserved.** Not open-source. No license granted.

## Quick Start

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1   # → dist\CodexDashboard.exe
.\dist\CodexDashboard.exe                                # double-click works too
```

Right-click the widget → **Settings** to configure providers.

## Features

- System tray with hover-to-peek popup; pin via 📌 to keep visible
- Borderless always-on-top Quiet Glass panel, inspired by Windows liquid-glass styling
- **Codex**: 5h + weekly quota via local `app-server` JSON-RPC; token estimate from SQLite
- **MiniMax**: 5h + weekly percentage via `mmx` CLI or Token Plan remains API
- **DeepSeek**: balance via official API with manual/official/fallback modes
- Embedded status dot · auto-refresh 60s · position persisted
- Available meters use a pale-grey fill; unavailable meters use black, without an `Unavailable` label
- History tracking with reset detection; burn ghost rendering upcoming

### Tray icon modes

Right-click the tray icon and select **默认图标** or **Codex 5 小时百分比**.

- **默认图标** shows the default open-gauge icon.
- **Codex 5 小时百分比** shows the remaining Codex 5-hour allowance and preserves an exact `100`. If 5-hour data is unavailable, the tray automatically shows the default open-gauge icon. The percentage preference stays selected and recovers automatically when valid data returns.

Quiet Glass is Windows Liquid-Glass-inspired; it does not claim Apple-native Liquid Glass behavior.

## Data Sources & Privacy

| Provider | Source | Method |
|---|---|---|
| Codex (quota) | local `codex app-server --stdio` | JSON-RPC |
| Codex (tokens) | `%USERPROFILE%\.codex\state_5.sqlite` | read-only SQLite |
| MiniMax | `mmx` CLI or `minimaxi.com/v1/token_plan/remains` | process / HTTPS |
| DeepSeek | `api.deepseek.com/user/balance` | HTTPS |

No telemetry. API keys stored in **Windows Credential Manager**, never in `settings.json`. Settings saved at `%APPDATA%\CodexDashboard\settings.json`.

## Troubleshooting

| Problem | Check |
|---|---|
| Codex 0% | `codex --version` installed? npm path auto-detected |
| MiniMax 0% | `mmx --version` + signed in? subscription key entered? |
| DeepSeek no balance | API key in Settings? mode set to `autoThenManual`? |

## Known Limits

- Token counts are local estimates, not official
- MiniMax `remains_time` suppressed when API returns raw numeric with unconfirmed units
- Quiet Glass system effects require Windows 10+

## Roadmap

- [x] Codex / MiniMax / DeepSeek providers
- [x] Stale-data tracking & source tagging
- [x] Window position/size/topmost persistence
- [ ] Configurable refresh interval in UI
- [x] Tray icon with minimize-to-tray
- [ ] Hook-driven Codex status (working/approval/idle)

---

This project is **not affiliated with** OpenAI, MiniMax, or DeepSeek.
