# Development Handoff

This document is for Hermes or a follow-up coding agent continuing the widget.

## Goal

Keep this as a native-feeling Windows desktop widget with minimal code and
minimal setup. The user explicitly rejected temporary Python prototypes,
manual quota entry, heavy packaging, and visually noisy backgrounds.

The desired product shape is:

- Small borderless floating panel.
- Pure glass/tint background, no texture, no gradient stripes, no decorative
  line art.
- One small dynamic status dot.
- Compact aligned quota rows.
- Official Codex quota percentages, refreshed automatically.
- 5-hour reset/refresh timing visible.

## Current Architecture

Files:

- `Program.cs` - single-file C# WPF application.
- `ProgramTests.cs` - dependency-free self-test for provider parsing and calculation.
- `build.ps1` - builds with the Windows/.NET Framework C# compiler.
- `dist/CodexDashboard.exe` - generated executable.

The app uses WPF controls/layout for the UI and DWM APIs for the window feel.
It deliberately avoids a hand-drawn GDI/WinForms background because that caused
visible edges, texture-like artifacts, and poor text/layout polish.

Important implementation areas in `Program.cs`:

- `DashboardApp` - WPF application entry point.
- `LiquidWindow` - borderless widget window, layout, timer, rendering.
- `NativeSqlite` - read-only access to Codex local SQLite state.
- `CodexAppServerQuota` - official quota fetch through Codex app-server JSON-RPC.

## Runtime Flow

1. `LiquidWindow` starts a 1-second dispatcher timer.
2. Every tick updates the clock and refresh countdown.
3. Every refresh interval, `StartRefresh()` runs work on a ThreadPool thread.
4. The worker reads:
   - local token estimate from SQLite
   - official quota from Codex app-server
5. UI updates are marshaled back through the WPF dispatcher.

Keep this split. UI should stay responsive even if Codex app-server is slow.

## Current Provider Extension (2026-07-11)

This section supersedes the older DeepSeek planning notes below where they
conflict. The implementation remains one WPF source file and adds no packages.

### Shared provider behavior

- `DashboardSettings` loads and saves `%APPDATA%\CodexDashboard\settings.json`.
  Missing or malformed files use safe defaults.
- The JSON file contains no credentials. MiniMax credentials are never read.
  DeepSeek reads only `DEEPSEEK_API_KEY` from the current process environment.
- Codex, MiniMax, and DeepSeek refresh in independent ThreadPool work items.
  The one-second dispatcher timer only renders time/countdown state.
- Each provider retains its most recent successful snapshot after its own
  failure. MiniMax/DeepSeek failures do not affect the Codex status light.
- The compact main UI only expands for providers enabled in Provider Settings.

### MiniMax Token Plan

`MiniMaxQuota` is hidden by default. When enabled, executable discovery uses:

1. absolute configured `minimax.mmxPath`;
2. `where.exe mmx.cmd`;
3. `where.exe mmx`;
4. `%APPDATA%\npm\mmx.cmd`;
5. `npm.cmd prefix -g`, then `mmx.cmd` / `bin\mmx.cmd` below that prefix.

It runs the official CLI shape:

```text
mmx quota show --non-interactive --quiet --output json
```

`ProcessRunner` disables shell execution, redirects stdout/stderr, creates no
console window, enforces a 15-second timeout, and kills a timed-out child. A
resolved `.cmd` or `.bat` is run with `cmd.exe /d /s /c` and an absolute shim
path, never as a normal executable. The child receives explicit `USERPROFILE`,
`HOME`, `MMX_CONFIG_DIR`, `APPDATA`, and a PATH that includes `%APPDATA%\npm`.

Do not diagnose an MMX installation from a failed dashboard lookup alone:
desktop apps can inherit a different PATH from an ordinary CMD. An absolute
`mmxPath` is the deterministic override.

### MiniMax Token Plan remains API

The optional Token Plan Subscription Key is stored under the Generic Credential
target `CodexDashboard.MiniMaxTokenPlan` through Windows Credential Manager.
It is never placed in `settings.json`, `.mmx` configuration, logs, UI output,
or exceptions. This subscription key is distinct from a regular pay-as-you-go
MiniMax API key.

When present, provider priority is: the official remains API, local MMX CLI,
last good snapshot, then unavailable. The API request is:

```text
GET https://www.minimaxi.com/v1/token_plan/remains
Authorization: Bearer <Token Plan Subscription Key>
```

The parser keeps `remains_time` and `weekly_remains_time` for the tooltip in
addition to the two server-issued remaining percentages.

The parser recursively supports arrays and nested objects. It prefers the
configured `quotaModelName` (`MiniMax-M*` by default) and can fall back only to
a non-media language/general plan that contains both of these server values:

```text
current_interval_remaining_percent
current_weekly_remaining_percent
```

Those values are clamped to 0-100. Count fields are intentionally not used,
including in `0/0` cases. A missing CLI, login problem, timeout, nonzero exit,
or malformed JSON leaves the last successful values visible with tooltip status.

### DeepSeek Balance

`DeepSeekBalance` treats DeepSeek as a money balance provider, never as a
time-window quota. When an API key exists, the app calls the official
`GET https://api.deepseek.com/user/balance` endpoint with an in-memory Bearer
header and a 15-second timeout. It parses `is_available`, chooses the configured
currency (default `CNY`) from `balance_infos`, and reads `total_balance`,
`granted_balance`, and `topped_up_balance` as `decimal`.

Available balance modes:

- `autoThenManual`: official balance when possible, otherwise `manualBalance`.
- `officialOnly`: official endpoint only.
- `manualOnly`: no request; use `manualBalance`.

The main percentage is `currentBalance / referenceBudget * 100`, clamped to
0-100. A zero/negative reference budget intentionally displays no percentage.
The tooltip-only request estimate uses editable token and pricing assumptions;
it is explicitly approximate and never presented as an official remaining-call
count. This version does not invent automatic DeepSeek spend without a real
usage-reporting path.

### Provider Settings

The right-click `Provider Settings...` dialog edits non-secret MiniMax and
DeepSeek settings, including mode, manual balance, reference budget, estimate
tokens/cache ratio, and price per million tokens. It also accepts an optional
MiniMax Token Plan Subscription Key through a password field that writes only
to Windows Credential Manager. Saving immediately rebuilds conditional rows and
triggers a provider refresh. DeepSeek intentionally has no API-key field.

### Test and local verification

`ProgramTests.cs` checks nested MiniMax JSON, percentage clamping, DeepSeek
currency selection, zero budget behavior, and cache-ratio clamping. Compile it
with `/main:ProgramTests` alongside `Program.cs`, then run `ProviderTests.exe`.
Build the widget with `powershell -ExecutionPolicy Bypass -File .\build.ps1`.

Current machine verification boundary: on 2026-07-11, the dashboard process
environment did not resolve `mmx` through `where.exe` and its
`%APPDATA%\npm` prefix had no shim. This proves only that this launch context
does not expose MMX; it does not establish that MMX is not installed or cannot
be queried by an ordinary CMD session. `DEEPSEEK_API_KEY` was not set. Provider
self-tests and the application build were run, but live MiniMax/DeepSeek
fetches remain unverified in this process context.

## Official Quota Source

The working source of truth is:

```text
codex app-server --stdio
```

JSON-RPC sequence:

1. `initialize`
2. `initialized`
3. `account/rateLimits/read`

Parse quota windows recursively:

- `windowDurationMins == 300` means 5-hour quota.
- `windowDurationMins == 10080` means weekly quota.
- `usedPercent` is official used percentage.
- remaining percentage is `100 - usedPercent`.
- `resetsAt` is a Unix timestamp and should drive the 5-hour reset display.

This matches the strategy used by the Codex desktop client and by the reference
project `Adam-yi/codex-traffic-light`.

## Local Token Estimate

Read-only SQLite file:

```text
%USERPROFILE%\.codex\state_5.sqlite
```

Current query shape:

```sql
select updated_at, coalesce(tokens_used, 0) from threads
```

The token display is approximate. Do not use it to derive quota percentage.

## Planned DeepSeek API Cost Tracking

User requirement:

- The user can manually enter DeepSeek balance.
- The user can modify accounting rules and add usage statistics.
- The widget shows progress bars and percentages.
- The widget refreshes automatically every 60 seconds.

### Design Principle

Treat DeepSeek as a money/balance provider, not as a quota-window provider.
Codex usage is official percentage based; DeepSeek usage should be balance and
ledger based.

Do not hardcode provider prices in application code. DeepSeek's official pricing
page says billing is token based and prices may change. Seed defaults from docs
if helpful, but store them in editable user settings.

Primary DeepSeek documentation:

- Balance endpoint: `GET https://api.deepseek.com/user/balance`
- Pricing and deduction rules: <https://api-docs.deepseek.com/quick_start/pricing>
- Token usage notes: <https://api-docs.deepseek.com/quick_start/token_usage>

### Proposed Settings File

Use one settings file under:

```text
%APPDATA%\CodexDashboard\settings.json
```

Suggested shape:

```json
{
  "refreshSeconds": 60,
  "deepseek": {
    "enabled": true,
    "currency": "USD",
    "balanceMode": "manualMinusLedger",
    "manualBalance": 10.0,
    "manualBudget": 10.0,
    "ledgerPath": "%APPDATA%\\CodexDashboard\\deepseek-usage.jsonl",
    "pricesPerMillionTokens": {
      "deepseek-v4-flash": {
        "inputCacheHit": 0.0028,
        "inputCacheMiss": 0.14,
        "output": 0.28
      },
      "deepseek-v4-pro": {
        "inputCacheHit": 0.003625,
        "inputCacheMiss": 0.435,
        "output": 0.87
      }
    }
  }
}
```

`manualBalance` is entered by the user. `manualBudget` is the denominator for
the progress bar. If the user wants a monthly or project budget later, add a
separate budget field instead of overloading balance.

### Balance Modes

Support two modes:

- `manualMinusLedger` - no API key required. Remaining balance is
  `manualBalance - sum(local ledger cost)`.
- `officialBalance` - optional. If `DEEPSEEK_API_KEY` is available, call
  DeepSeek's `/user/balance` endpoint and use returned `total_balance` for the
  selected currency.

Recommended default is `manualMinusLedger`, because the user explicitly asked
for manually entered balance and editable rules. `officialBalance` is a later
upgrade.

Never store the DeepSeek API key in plaintext settings. Read it from
`DEEPSEEK_API_KEY` first. If Hermes later adds UI key management, use Windows
Credential Manager or DPAPI, not a normal JSON file.

### Usage Ledger

Store local usage events as append-only JSONL:

```text
%APPDATA%\CodexDashboard\deepseek-usage.jsonl
```

Example row:

```json
{"ts":"2026-07-03T21:40:00Z","provider":"deepseek","model":"deepseek-v4-flash","inputTokens":12000,"cacheHitInputTokens":8000,"cacheMissInputTokens":4000,"outputTokens":1800,"cost":0.001064,"currency":"USD","source":"manual"}
```

The widget cannot know every DeepSeek API call unless the call path reports
usage. Hermes should add one or more collection paths:

- Manual entry in the widget settings panel.
- A small local CLI command, for example
  `CodexDashboard.exe deepseek add-usage --model ... --input ... --output ...`.
- A local proxy/wrapper used by future DeepSeek tooling. This is the most
  accurate method because API responses usually include usage fields.
- Optional import from worker logs, only if those logs reliably contain model
  and token usage.

### Cost Formula

For each ledger row:

```text
inputCost =
  cacheHitInputTokens  / 1_000_000 * inputCacheHit +
  cacheMissInputTokens / 1_000_000 * inputCacheMiss

outputCost =
  outputTokens / 1_000_000 * output

totalCost = inputCost + outputCost
```

If cache-hit/cache-miss split is unavailable, treat all input as cache miss or
allow a user-configurable fallback ratio. Prefer conservative accounting over
under-counting.

### Display

Do not crowd the current compact widget. Recommended first UI:

- Keep the Codex rows as-is.
- Add a third optional row: `DS` or `DeepSeek`.
- Show remaining amount and percentage:
  - left: `DS`
  - middle: remaining balance, for example `$8.42`
  - right: remaining percent, for example `84%`
- Use the same green progress style as Codex.

Progress formula:

```text
remainingPercent = clamp(remainingBalance / manualBudget * 100, 0, 100)
```

Refresh once per minute. The 1-second timer can continue updating the countdown,
but provider refresh work should remain at 60 seconds or slower.

### Error Handling

- If official balance query fails, keep the last good official value and show a
  stale indicator in settings or logs, not in the compact main UI.
- If the ledger is corrupt, ignore only the bad row and append a diagnostic log.
- If no DeepSeek balance is configured, hide the row by default.
- If calculated remaining balance is negative, show `0%` and keep the negative
  amount visible in settings for diagnosis.

### Files Hermes Should Touch

For the current single-file WPF version:

- `Program.cs`
  - Add settings load/save.
  - Add DeepSeek ledger parser.
  - Add optional DeepSeek balance refresh.
  - Add one optional UI row.
- `README.md`
  - Update user setup instructions.
- `docs/DEVELOPMENT.md`
  - Keep this section updated when implementation choices change.

If Hermes moves to a better native architecture, split these responsibilities:

- `Providers/CodexUsageProvider.*`
- `Providers/DeepSeekUsageProvider.*`
- `Storage/SettingsStore.*`
- `Storage/UsageLedger.*`
- `Ui/DashboardWindow.*`

## UI Rules

Preserve these unless the user changes direction:

- No title bar or window control buttons.
- No large central percentage.
- No background image.
- No gradient-orb/blob decorations.
- No GDI rounded `Region` masking.
- Let DWM handle corners/backdrop where available.
- Keep typography compact and aligned.
- Use one status dot, not three traffic-light dots.

The current design uses WPF with `AllowsTransparency=true` for real desktop
transparency. This fixes black-corner/backplate issues, but layered WPF windows
can drag less smoothly than native Windows composition surfaces.

If the user wants a truly native-feeling, smooth widget, prefer one of the
native paths below instead of continuing to tune WPF effects.

## Better Desktop Widget Architecture Options

### Option A: C++ Win32 + Direct2D/DirectComposition

Best for a tiny always-on-top desktop widget that should feel like a native
Windows component.

- Use a borderless Win32 window.
- Use DWM for rounded corners and backdrop policy where available.
- Draw text/progress bars with Direct2D/DirectWrite.
- Use DirectComposition or DWM composition rather than WPF layered-window
  transparency.
- Handle `WM_NCHITTEST` for drag/resize.

Pros:

- Highest control over frame pacing, hit testing, resize, and glass fallback.
- No XAML tree overhead.
- Best chance of smooth 60 fps drag/resize.

Cons:

- More C++ code.
- Slower for Hermes to modify unless it is comfortable with Win32.

### Option B: WinUI 3 / Windows App SDK

Best if Hermes can install/use the proper SDK toolchain.

- Use WinUI 3 windowing.
- Use `Window.SystemBackdrop`, `MicaBackdrop`, or
  `DesktopAcrylicController`/`MicaController`.
- Use transparent content layers so the backdrop shows through.
- Use AppWindow/Win32 interop for borderless topmost behavior.

Pros:

- Most aligned with modern Windows visual language.
- Native Mica/Acrylic APIs and rounded-window behavior.
- Easier UI iteration than raw C++.

Cons:

- Requires .NET SDK / Windows App SDK / NuGet restore.
- More packaging complexity than the current single-file build.

### Option C: Current WPF Single-File App

Best for minimum setup and current iteration speed.

- No SDK install.
- Builds with .NET Framework `csc.exe`.
- Easy to edit quickly.

Limits:

- `AllowsTransparency=true` enables real transparency but can hurt drag
  performance.
- WPF effects such as `DropShadowEffect` are expensive on transparent windows.
- Native Acrylic/Mica cannot be made as reliable as WinUI 3 or Win32
  composition.

Recommendation:

- Keep WPF for the current proof of concept.
- If the user approves installing toolchain, move to WinUI 3 first.
- If the main goal becomes maximum smoothness and minimal runtime overhead,
  rebuild as C++ Win32 + Direct2D/DirectComposition.

## Next Development Tasks

1. Real status dot states
   - Integrate Codex hooks or a small state file so the dot can mean:
     - green: idle/available
     - yellow: working
     - red: approval needed
   - The reference repo `Adam-yi/codex-traffic-light` is useful for hook ideas.

2. Persist window settings
   - Save position, topmost, and refresh interval under:
     `%APPDATA%\CodexDashboard\settings.json`
   - Keep defaults sane if the file is missing or corrupt.

3. Better stale-data handling
   - Preserve last good official quota.
   - Show a subtle stale age if app-server fails.
   - Do not silently replace official quota with token-derived guesses.

4. Packaging
   - Optional later step: autostart shortcut, icon, and small installer script.
   - Avoid heavy packaging until the UI/data behavior is accepted.

5. DeepSeek API cost tracking
   - Add user-entered balance and editable pricing rules.
   - Add append-only local usage ledger.
   - Add optional official balance query through `DEEPSEEK_API_KEY`.
   - Refresh every 60 seconds.
   - Add an optional DeepSeek row with progress bar and remaining percentage.

## Do Not Regress

- Do not reintroduce a Python prototype.
- Do not require manual quota input.
- Do not read or display secrets from `auth.json`.
- Do not fake official quota from local token totals.
- Do not add Electron or webview unless the user explicitly chooses that path.
- Do not add visual textures, background images, or decorative line patterns.

## Build Verification

Use:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Expected output:

```text
Built dist\CodexDashboard.exe
```

Manual visual check:

1. Run `dist\CodexDashboard.exe`.
2. Confirm the widget has no title bar.
3. Confirm quota percentages match Codex desktop usage view.
4. Confirm 5-hour reset/refresh timing is visible.
5. Confirm right-click menu works.

## Attribution

Reference studied:

```text
https://github.com/Adam-yi/codex-traffic-light
```

The useful discovery was its Codex app-server quota approach and hook-based
status concept. If future work copies code from that repository, preserve its
license and attribution.

## Reference Repo Implementation Notes

Reference:

```text
https://github.com/Adam-yi/codex-traffic-light
```

Observed implementation shape:

- Windows version is a single C# WinForms program built by the system
  .NET Framework compiler. It does not require .NET SDK.
- The same executable codebase runs in three roles based on executable name:
  - floating GUI
  - command-line state tool
  - Codex hook command
- State is stored under `%APPDATA%\CodexTrafficLight`, mainly:
  - `state.json`
  - `preferences.json`
  - `hook-mxp.log`
  - `quota-mxp.log`
- Codex Hooks write state changes through a command configured in
  `%USERPROFILE%\.codex\config.toml`.
- Hook mapping:
  - `UserPromptSubmit` and `PreToolUse` -> working
  - `PermissionRequest` -> waiting
  - `Stop` and `SubagentStop` -> done, unless the final assistant message looks
    like it is waiting for user input
- Aggregate priority:
  - waiting beats working
  - working beats recent done
  - otherwise idle
- Quota is read through `codex app-server --stdio` and
  `account/rateLimits/read`, then parsing 300-minute and 10080-minute windows.
- Its Windows UI is custom WinForms/GDI+ drawing with tray menu, sounds, and
  transparent-key window shaping. It is not real WinUI/Acrylic glass.
- Default quota refresh in that project is 300 seconds unless overridden by
  environment variable. Our widget should use the user's requested 60 seconds.
