$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $out | Out-Null

# Locate C# compiler
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (!(Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (!(Test-Path $csc)) { throw "csc.exe not found. Install .NET Framework 4.0+ or the Windows SDK." }

# Locate .NET Framework reference assemblies (for WPF: System.Xaml, WindowsBase, PresentationCore, PresentationFramework)
$netfx = "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0"
if (!(Test-Path $netfx)) {
    # Fallback to the runtime directory (System.Xaml.dll etc. also live alongside the runtime in Framework64)
    $netfx = Split-Path -Parent $csc
    if (!(Test-Path "$netfx\System.Xaml.dll")) {
        throw ".NET Framework reference assemblies not found at '$netfx'. Install .NET Framework 4.0 targeting pack or Windows SDK."
    }
}

& $csc /nologo /target:winexe /optimize+ /out:"$out\CodexDashboard.exe" /reference:System.Web.Extensions.dll /reference:"$netfx\System.Xaml.dll" /reference:"$netfx\WindowsBase.dll" /reference:"$netfx\PresentationCore.dll" /reference:"$netfx\PresentationFramework.dll" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "$root\Program.cs"
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Write-Host "Built $out\CodexDashboard.exe"
