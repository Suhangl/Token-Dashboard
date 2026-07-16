$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $root "dist"
$appIcon = Join-Path $root "assets\app-icon.ico"
if (!(Test-Path -LiteralPath $appIcon -PathType Leaf)) {
    throw "Application icon not found at '$appIcon'. Run tools\GenerateAppIcon.ps1 before building."
}
New-Item -ItemType Directory -Force -Path $out | Out-Null

# Locate C# compiler
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (!(Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (!(Test-Path $csc)) { throw "csc.exe not found. Install .NET Framework 4.0+ or the Windows SDK." }

# Locate the highest installed .NET Framework WPF reference assemblies. GitHub's
# windows-latest image may retain the v4.0 folder without its WPF DLLs.
$referenceRoot = "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework"
$netfx = $null
foreach ($version in @("v4.8.1", "v4.8", "v4.7.2", "v4.7.1", "v4.7", "v4.6.2", "v4.6.1", "v4.6", "v4.5.2", "v4.5.1", "v4.5", "v4.0")) {
    $candidate = Join-Path $referenceRoot $version
    if (Test-Path (Join-Path $candidate "System.Xaml.dll")) { $netfx = $candidate; break }
}
if ($null -eq $netfx) {
    # Fallback to the runtime directory for developer machines without a targeting pack.
    $netfx = Split-Path -Parent $csc
    if (!(Test-Path "$netfx\System.Xaml.dll")) {
        throw ".NET Framework WPF reference assemblies not found. Install a .NET Framework targeting pack or Windows SDK."
    }
}

& $csc /nologo /target:winexe /optimize+ /win32icon:"$appIcon" /out:"$out\CodexDashboard.exe" /reference:System.Web.Extensions.dll /reference:"$netfx\System.Xaml.dll" /reference:"$netfx\WindowsBase.dll" /reference:"$netfx\PresentationCore.dll" /reference:"$netfx\PresentationFramework.dll" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "$root\Program.cs"
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Write-Host "Built $out\CodexDashboard.exe"
