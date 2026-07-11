# Privacy

Token Dashboard does not collect telemetry and does not upload your local data.

## What the Application Does

- Runs entirely on your machine
- Makes **no** outbound network requests by default
- Only contacts external services when you explicitly enable a Provider

## Network Requests (when enabled)

| Provider | Destination | Purpose |
|---|---|---|
| Codex | Local `codex app-server --stdio` | Read quota percentages (local process) |
| MiniMax | `minimaxi.com/v1/token_plan/remains` | Read Token Plan remains (only with subscription key) |
| MiniMax | Local `mmx` CLI | Read quota via installed CLI |
| DeepSeek | `api.deepseek.com/user/balance` | Read account balance |

- Codex local SQLite data is **never** uploaded
- The application does **not** send usage logs, error reports, or analytics

## Where Data is Stored

| Data | Location |
|---|---|
| Settings (toggles, paths, budget numbers) | `%APPDATA%\CodexDashboard\settings.json` |
| MiniMax subscription key | Windows Credential Manager → `CodexDashboard.MiniMaxTokenPlan` |
| DeepSeek API key | Windows Credential Manager → `CodexDashboard.DeepSeekApiKey` |

API keys are **never** written to `settings.json`, log output, or exception messages.

## How to Delete All Application Data

1. **Delete settings**:
   ```
   del %APPDATA%\CodexDashboard\settings.json
   ```
2. **Remove stored credentials**:
   Open **Credential Manager** (Windows) → Windows Credentials → Generic Credentials → remove `CodexDashboard.MiniMaxTokenPlan` and `CodexDashboard.DeepSeekApiKey`
3. **Delete the application**:
   Delete the `CodexDashboard` folder
4. **No other traces**: the application creates no registry keys, no scheduled tasks, and no hidden caches.

## What This Application Does NOT Do

- It does **not** read your `auth.json` or any other credential file
- It does **not** install browser extensions or system services
- It does **not** auto-start on boot (unless you configure that yourself)
- It does **not** use cookies or track your activity across sessions
