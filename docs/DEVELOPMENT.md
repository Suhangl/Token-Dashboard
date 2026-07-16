# Development Handoff

## 架构

单文件 WPF (`Program.cs`，~1580 行)，`csc.exe` 编译，零外部依赖。

```
Program.cs
├── enum ProviderSource
├── static class NativeSqlite
├── class UsageSnapshot / QuotaSnapshot / MiniMaxSnapshot / DeepSeekSnapshot
├── static class ProviderMath
├── static class DashboardState           — 数据共享层
├── class DashboardSettings + *Settings 子类
├── class HistoryPoint / HistoryRing
├── static class BarColors
├── static class MiniMaxQuota (+ MiniMaxRemainsApi, MiniMaxTime)
├── static class WindowsCredentialStore
├── static class DeepSeekBalance
├── static class ProcessRunner
├── static class CodexAppServerQuota
├── static class BitmapFactory            — 程序生成托盘图标
├── interface INotifyIconBackend
├── class TrayIconBackend                 — NotifyIcon 包装
├── class Bindings                        — UI 控件引用集合
├── static class BuildUiFactory           — 视觉树工厂
├── class PopupWindow : Window            — 按需显示的仪表盘弹窗
├── class TrayController                  — 托盘、菜单和计时器状态机
├── static class SettingsDialog           — Provider 设置对话框
└── static class Program
```

## 托盘 + 弹窗模式

### 启动序列

1. `Program.Main()` 加载 `DashboardSettings`，并将 WPF 的 `ShutdownMode` 设为 `OnExplicitShutdown`。
2. 隐藏的 bootstrapper 窗口初始化 WPF Dispatcher；随后创建 `PopupWindow`、`TrayIconBackend` 和 `TrayController`。
3. `TrayController` 生成并显示托盘图标、绑定菜单和计时器。默认启动时只显示托盘图标；仅当 `popupStickyOnLaunch=true` 时才以 Sticky 状态显示弹窗。
4. `PopupWindow` 在后台持续刷新 `DashboardState`，即使弹窗隐藏，时钟和 Provider 数据也会继续更新。

### Unsticky 与 Sticky

- **Unsticky**：弹窗不置顶。悬停或单击托盘图标可显示弹窗；离开托盘和弹窗后会延迟隐藏。
- **Sticky**：单击弹窗中的 📌，或选择托盘菜单的“钉住 popup”，会将 `IsSticky` 设为 `true` 并开启 `Topmost`。Sticky 状态不受离开计时器影响。
- Sticky 弹窗失去激活时会调用 `LeaveSticky()`：取消置顶、恢复 Unsticky，并隐藏弹窗。
- 显示时优先恢复已保存且仍在屏幕范围内的 `popupLeft` / `popupTop`；否则停靠工作区右下角。拖动后以 500ms debounce 保存位置。

### Hover / leave 计时器

- 托盘收到 `MouseMove` 且弹窗不可见时启动 hover timer，默认 `popupHoverDelayMs=400`；到时显示弹窗。
- `NotifyIcon` 没有可靠的 MouseLeave，因此每 100ms 轮询鼠标位置。离开托盘区域、且鼠标不在 Unsticky 弹窗上时，启动 dismiss timer。
- 鼠标进入弹窗会停止 dismiss timer；离开 Unsticky 弹窗时重新启动。默认 `popupDismissDelayMs=300`，到时隐藏弹窗。

### 关闭与退出

| 操作 | 结果 |
|---|---|
| Unsticky 状态离开托盘/弹窗 | 仅隐藏弹窗，托盘和后台刷新继续运行 |
| Sticky 弹窗失去激活 | 取消 Sticky 和置顶，并隐藏弹窗 |
| Sticky 状态收到窗口关闭请求 | 取消关闭并隐藏弹窗，应用继续驻留托盘 |
| 托盘菜单“退出” | 显式关闭应用；停止计时器、关闭弹窗并释放 NotifyIcon |

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
