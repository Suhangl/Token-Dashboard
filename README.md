# Codex Dashboard

Lightweight Windows floating dashboard for Codex usage.

This project is intentionally small: one C# WPF program, one PowerShell build
script, no Python prototype, no Electron, no NuGet dependency.

## Current Features

- Borderless always-on-top desktop widget.
- Lightweight translucent floating panel.
- Official Codex quota percentages for:
  - 5-hour window
  - Weekly window
- Progress bars, used token estimate, refresh countdown, and 5-hour reset time.
- Single status dot:
  - Yellow while refreshing
  - Green when official quota is available
  - Red when quota cannot be read
- Drag anywhere on the widget to move it.
- Right-click menu: Refresh, Topmost, Exit.
- Optional MiniMax Token Plan rows (5-hour and weekly values remain separate).
- Optional DeepSeek balance row with a user-defined reference-budget percentage.
- Native compact `Provider Settings...` dialog; no API key is stored in settings.

## Data Sources

Quota percentage comes from the Codex desktop/CLI app-server API:

```text
codex app-server --stdio
account/rateLimits/read
```

The app reads the official `usedPercent` values from the 300-minute and
10080-minute quota windows, then displays remaining percentage as
`100 - usedPercent`.

Approximate token usage is read locally and read-only from:

```text
%USERPROFILE%\.codex\state_5.sqlite
```

The app does not read `auth.json`, does not ask for a token, and does not fake
quota percentages from local token estimates.

## Build

From this folder:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Output:

```text
dist\CodexDashboard.exe
```

## Run

```powershell
.\dist\CodexDashboard.exe
```

## Optional Providers

Open the widget's right-click menu and choose `Provider Settings...`. Settings
are stored at:

```text
%APPDATA%\CodexDashboard\settings.json
```

It contains no credentials.

### MiniMax Token Plan

Enable MiniMax only when the `mmx` CLI is installed and signed in. The widget
tries, in order: an absolute configured `mmxPath`, `where.exe mmx.cmd`,
`where.exe mmx`, `%APPDATA%\npm\mmx.cmd`, then npm's global prefix/bin path.
When the resolved target is `.cmd` or `.bat`, the widget runs it through:

```text
cmd.exe /d /s /c ""<absolute-mmx.cmd>" quota show --output json"
```

instead of attempting to start the shim as an executable. It supplies the
current-user `USERPROFILE`, `HOME`, `MMX_CONFIG_DIR`, `APPDATA`, and PATH
(including `%APPDATA%\npm`) to the child process. The CLI query is:

```text
mmx quota show --non-interactive --quiet --output json
```

The display uses only server-provided
`current_interval_remaining_percent` and
`current_weekly_remaining_percent`; it does not infer percentages from count
fields. A missing CLI or failed command keeps the latest successful snapshot
and exposes a concise status through the row tooltip.

Optionally, enter a **Token Plan Subscription Key** in `Provider Settings...`.
It is stored only as the `CodexDashboard.MiniMaxTokenPlan` Generic Credential
in Windows Credential Manager, never in `settings.json`. When present, the
dashboard tries the official Token Plan remains API first, then falls back to
the local CLI, then retains the last successful snapshot. This key is distinct
from a normal pay-as-you-go MiniMax API key.

### DeepSeek Balance

The balance mode is one of:

- `autoThenManual` (default): use `DEEPSEEK_API_KEY` with DeepSeek's official
  balance endpoint when present; otherwise use the configured manual balance.
- `officialOnly`: use only the official endpoint.
- `manualOnly`: do not make a network request.

The optional progress percentage is:

```text
currentBalance / referenceBudget * 100
```

and is clamped to `0-100`. A zero reference budget deliberately shows no
percentage. The optional request figure in the tooltip is an estimate from
editable token/pricing assumptions, not an official remaining-call allowance.

The key is read only from `DEEPSEEK_API_KEY`; it is never written to JSON,
logs, exceptions, or the UI.

## Provider Self-Test

The repository includes a small dependency-free test harness for provider
parsing and calculations. Run it with the system compiler:

```powershell
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$netfx = "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0"
& $csc /nologo /target:exe /main:ProgramTests /out:.\dist\ProviderTests.exe /reference:System.Web.Extensions.dll /reference:"$netfx\System.Xaml.dll" /reference:"$netfx\WindowsBase.dll" /reference:"$netfx\PresentationCore.dll" /reference:"$netfx\PresentationFramework.dll" .\Program.cs .\ProgramTests.cs
.\dist\ProviderTests.exe
```

## Known Limits

- The status dot currently reflects refresh/quota availability. It is not yet a
  true Codex hook-driven "working / approval needed / idle" state indicator.
- The glass effect depends on the current Windows DWM support. On some Windows
  builds it may fall back to a simpler acrylic-like translucent window.
- Token usage is an approximate local-thread estimate. Official quota percentage
  is the source of truth for the progress bars.

## Developer Handoff

See [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) for Hermes continuation notes.
