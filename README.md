# Token Dashboard

单文件 WPF 桌面悬浮组件，显示 Codex / MiniMax / DeepSeek 三平台用量。

## 功能

- 无边框置顶悬浮窗，DWM Acrylic 原生玻璃效果
- Codex：5h + Week 额度百分比（app-server JSON-RPC）+ 本地 token 估算（SQLite）
- MiniMax：5h + Week 剩余百分比（mmx CLI / Token Plan API），含 5h 重置倒计时
- DeepSeek：余额查询（官方 API / 手动输入），含预算百分比进度条
- 每分钟自动刷新，状态灯（绿=正常 / 黄=刷新中 / 红=不可用）
- 右键菜单：刷新、设置、置顶、退出
- 设置存 `%APPDATA%\CodexDashboard\settings.json`，API 密钥存 Windows 凭据管理器

## 构建

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

输出：`dist\CodexDashboard.exe`

## 运行

```powershell
.\dist\CodexDashboard.exe
```

## 设置

右键 → 设置... 可分别开关 Codex / MiniMax / DeepSeek 面板。

- **Codex**：无需配置，自动读取本地 SQLite + app-server
- **MiniMax**：需 mmx CLI 已安装 + 登录；可选填 Token Plan 订阅密钥
- **DeepSeek**：填 API Key（platform.deepseek.com → API Keys），余额模式默认 autoThenManual

密钥不存 JSON，仅存 Windows 凭据管理器（`CodexDashboard.MiniMaxTokenPlan` / `CodexDashboard.DeepSeekApiKey`）。

## 自测

```powershell
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$netfx = "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0"
& $csc /nologo /target:exe /main:ProgramTests /out:.\dist\ProviderTests.exe /reference:System.Web.Extensions.dll /reference:"$netfx\System.Xaml.dll" /reference:"$netfx\WindowsBase.dll" /reference:"$netfx\PresentationCore.dll" /reference:"$netfx\PresentationFramework.dll" .\Program.cs .\ProgramTests.cs
.\dist\ProviderTests.exe
```
