# Development Handoff

## 架构

单文件 WPF (`Program.cs`，~1078 行)，`csc.exe` 编译，零外部依赖。

```
Program.cs
├── enum ProviderSource              — OfficialApi/Cli/Manual/LocalEstimate/Cached
├── static class NativeSqlite        — SQLite 只读，state_5.sqlite
├── class UsageSnapshot             — Codex token 估算
├── class QuotaSnapshot             — Codex 配额快照
├── class MiniMaxSnapshot           — MiniMax 配额快照（含 Source + RemainsTime）
├── class DeepSeekSnapshot          — DeepSeek 余额快照（含 Source）
├── static class ProviderMath       — 百分比/成本计算
├── class DashboardSettings         — JSON 设置读写 + balanceMode 归一化
├── class MiniMaxCommand            — CLI 命令模型
├── static class MiniMaxQuota       — mmx CLI 调用 + 解析
│   └── static class MiniMaxTime    — 时间格式化（分 CLI/API 源）
├── static class MiniMaxRemainsApi  — Token Plan API
├── static class WindowsCredentialStore — advapi32 凭据读写
├── static class DeepSeekBalance    — api.deepseek.com/user/balance
├── class ProcessResult / ProcessRunner — 进程包装
├── static class CodexAppServerQuota — codex app-server JSON-RPC（finally 清理 + JSON id 解析）
└── class LiquidWindow : Window     — WPF 主窗口
```

## Provider source 和 stale 模型

每个 Provider 快照携带 `ProviderSource`：
- `OfficialApi` — 官方 API 实时数据
- `Cli` — 命令行工具输出
- `Manual` — 用户手动输入值
- `Cached` — 上次成功值的缓存（IsStale=true）

刷新失败时保留旧值并标记 IsStale=true，不清空。

## DeepSeek balanceMode 规范值

内部仅允许三个值：`autoThenManual`、`officialOnly`、`manualOnly`。
设置 UI 使用内部值；`NormalizeBalanceMode()` 兼容旧带中文描述的配置。

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

## 窗口持久化

关闭时自动保存位置、尺寸和置顶状态到 `settings.json`。启动时恢复。
若保存位置不再处于任何屏幕范围内（例如外接显示器断开），自动回退到默认位置。
最小可见区域要求 50×50 像素。

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

## 复合资源条

- 上层 5H（12px），下层 W（6px），紧凑标签
- 颜色：绿 ≥50% / 黄 20-49% / 红 <20%（`BarColors.ForPercent`）
- 历史样本缓存 `HistoryRing`（32 点 ring buffer），每次刷新写入
- Reset 检测：当前值超过上一样本 2× 时清空历史
- 残影渲染 `RenderGhosts` 已就绪（5m/20m/30m 三层），待下一轮接入 BuildUi
- DeepSeek 保持单层条，使用相同颜色逻辑

## 未来 Roadmap

- [ ] 三层 burn ghost 渲染接入 UI
- [ ] 24h / 30min bucket 趋势 sparkline
- [ ] 请求次数历史趋势

## 不回归

- 不加 Python 原型
- 不加 Electron/webview
- 不加视觉纹理/渐变/装饰层
- 不读 `auth.json`
- 不用本地 token 估算伪造配额百分比
