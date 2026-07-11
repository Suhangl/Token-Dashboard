# Contributing

This project is **not currently accepting external code contributions** (Pull Requests).

## How to Help

- **Bug reports and feature suggestions**: Open a GitHub Issue with:
  - Steps to reproduce
  - Expected vs actual behavior
  - Your Windows version and DPI setting (if UI-related)
- **Documentation corrections**: Open an Issue describing the error
- **Security issues**: See [SECURITY.md](SECURITY.md) — do not use public Issues

## Build & Test (for reference)

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

```powershell
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$netfx = "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0"
& $csc /nologo /target:exe /main:ProgramTests /out:.\dist\ProviderTests.exe /reference:System.Web.Extensions.dll /reference:"$netfx\System.Xaml.dll" /reference:"$netfx\WindowsBase.dll" /reference:"$netfx\PresentationCore.dll" /reference:"$netfx\PresentationFramework.dll" .\Program.cs .\ProgramTests.cs
.\dist\ProviderTests.exe
```

## Rules

- Do not commit `dist/`, `.exe`, or build artifacts
- Do not commit API keys or credentials
