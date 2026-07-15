$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $out | Out-Null

# same compiler / framework detection as build.ps1
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (!(Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (!(Test-Path $csc)) { throw "csc.exe not found. Install .NET Framework 4.0+ or the Windows SDK." }

# Match build.ps1: choose the highest installed targeting pack that contains WPF.
$referenceRoot = "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework"
$netfx = $null
foreach ($version in @("v4.8.1", "v4.8", "v4.7.2", "v4.7.1", "v4.7", "v4.6.2", "v4.6.1", "v4.6", "v4.5.2", "v4.5.1", "v4.5", "v4.0")) {
    $candidate = Join-Path $referenceRoot $version
    if (Test-Path (Join-Path $candidate "System.Xaml.dll")) { $netfx = $candidate; break }
}
if ($null -eq $netfx) {
    $netfx = Split-Path -Parent $csc
    if (!(Test-Path "$netfx\System.Xaml.dll")) {
        throw ".NET Framework WPF reference assemblies not found. Install a .NET Framework targeting pack or Windows SDK."
    }
}

& $csc /nologo /target:exe /main:ProgramTests /out:"$out\ProviderTests.exe" /reference:System.Web.Extensions.dll /reference:"$netfx\System.Xaml.dll" /reference:"$netfx\WindowsBase.dll" /reference:"$netfx\PresentationCore.dll" /reference:"$netfx\PresentationFramework.dll" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "$root\Program.cs" "$root\ProgramTests.cs"
if ($LASTEXITCODE -ne 0) { throw "Test build failed" }

$result = & "$out\ProviderTests.exe" 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error $result
    throw "ProviderTests failed"
}
Write-Host $result

& powershell -NoProfile -ExecutionPolicy Bypass -File "$root\tools\TestGenerateAppIcon.ps1"
if ($LASTEXITCODE -ne 0) { throw "Application icon evidence tests failed" }
