# Contributing

## Workflow

1. Create a **feature branch** from `main`
2. Make your changes
3. Ensure the build and tests pass
4. Open a Pull Request against `main`

## Rules

- **Do not commit** `dist/`, `.exe`, `.dll`, or any build artifacts
- **Do not commit** API keys, tokens, or credentials of any kind
- Use `Path.GetTempPath()` or clearly fake test paths — never commit real user paths
- When modifying a Provider, add corresponding tests to `ProgramTests.cs`
- When changing data sources, update both `README.md` and `docs/DEVELOPMENT.md`
- For UI changes, include before/after screenshots in the PR description

## Build & Test

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Then build and run the test harness:

```powershell
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$netfx = "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0"
& $csc /nologo /target:exe /main:ProgramTests /out:.\dist\ProviderTests.exe /reference:System.Web.Extensions.dll /reference:"$netfx\System.Xaml.dll" /reference:"$netfx\WindowsBase.dll" /reference:"$netfx\PresentationCore.dll" /reference:"$netfx\PresentationFramework.dll" .\Program.cs .\ProgramTests.cs
.\dist\ProviderTests.exe
```

Both must pass before the PR is ready for review.
