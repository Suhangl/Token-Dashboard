$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $out | Out-Null

# same compiler / framework detection as build.ps1
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (!(Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (!(Test-Path $csc)) { throw "csc.exe not found. Install .NET Framework 4.0+ or the Windows SDK." }

$netfx = "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0"
if (!(Test-Path $netfx)) {
    $netfx = Split-Path -Parent $csc
    if (!(Test-Path "$netfx\System.Xaml.dll")) {
        throw ".NET Framework reference assemblies not found at '$netfx'."
    }
}

& $csc /nologo /target:exe /main:ProgramTests /out:"$out\ProviderTests.exe" /reference:System.Web.Extensions.dll /reference:"$netfx\System.Xaml.dll" /reference:"$netfx\WindowsBase.dll" /reference:"$netfx\PresentationCore.dll" /reference:"$netfx\PresentationFramework.dll" "$root\Program.cs" "$root\ProgramTests.cs"
if ($LASTEXITCODE -ne 0) { throw "Test build failed" }

$result = & "$out\ProviderTests.exe" 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error $result
    throw "ProviderTests failed"
}
Write-Host $result
