# Development Handoff

## 架构

单文件 WPF (`Program.cs`，~1032 行)，`csc.exe` 编译，零外部依赖。

```
Program.cs
├── static class NativeSqlite          — SQLite 只读，state_5.sqlite
├── class UsageSnapshot               — Codex token 估算
├── class QuotaSnapshot               — Codex 配额快照
├── class MiniMaxSnapshot             — MiniMax 配额快照
├── class DeepSeekSnapshot            — DeepSeek 余额快照
├── static class ProviderMath         — 百分比/成本计算
├── class DashboardSettings           — JSON 设置读写
├── class MiniMaxCommand              — CLI 命令模型
├── static class MiniMaxQuota         — mmx CLI 调用 + 解析
├── static class MiniMaxRemainsApi    — Token Plan API
├── static class WindowsCredentialStore — advapi32 凭据读写
├── static class DeepSeekBalance      — api.deepseek.com/user/balance
├── class ProcessResult / ProcessRunner — 进程包装
├── static class CodexAppServerQuota  — codex app-server JSON-RPC
└── class LiquidWindow : Window       — WPF 主窗口
    ├── BuildUi()    — 布局构建
    ├── Render()     — 数据刷新
    ├── StartRefresh() / RefreshCodex() / RefreshMiniMax() / RefreshDeepSeek()
    ├── ShowProviderSettings() — 设置对话框
    └── EnableSystemGlass()   — DWM Acrylic
```

## 数据源

| 平台 | 来源 | 方法 |
|---|---|---|
| Codex | `codex app-server --stdio` → `account/rateLimits/read` | CodexAppServerQuota.Fetch() |
| Codex tokens | `%USERPROFILE%\.codex\state_5.sqlite` | NativeSqlite.ReadState() |
| MiniMax | `mmx quota show --output json` | MiniMaxQuota.Fetch() |
| MiniMax API | `minimaxi.com/v1/token_plan/remains` | MiniMaxRemainsApi.Fetch() |
| DeepSeek | `api.deepseek.com/user/balance` | DeepSeekBalance.Fetch() |

## 设置

- `%APPDATA%\CodexDashboard\settings.json` — 开关/路径/余额/预算/圆角
- Windows 凭据管理器 — `CodexDashboard.MiniMaxTokenPlan` / `CodexDashboard.DeepSeekApiKey`

## 构建

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

输出 `dist\CodexDashboard.exe`（~65KB）。

## 自测

```powershell
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$netfx = "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0"
& $csc /nologo /target:exe /main:ProgramTests /out:.\dist\ProviderTests.exe /reference:System.Web.Extensions.dll /reference:"$netfx\System.Xaml.dll" /reference:"$netfx\WindowsBase.dll" /reference:"$netfx\PresentationCore.dll" /reference:"$netfx\PresentationFramework.dll" .\Program.cs .\ProgramTests.cs
.\dist\ProviderTests.exe
```

## 视觉

- DWM Acrylic 原生模糊（`SetWindowCompositionAttribute` + `DwmSetWindowAttribute`）
- 半透明面板 `Color.FromArgb(112, 28, 33, 48)`
- 圆角半径可调（`settings.glass.cornerRadius`，默认 15）
- 无手写渐变、无贴图 overlay

## 不回归

- 不加 Python 原型
- 不加 Electron/webview
- 不加视觉纹理/渐变/装饰层
- 不读 `auth.json`
- 不用本地 token 估算伪造配额百分比
