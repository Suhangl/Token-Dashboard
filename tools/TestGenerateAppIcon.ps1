[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$generator = Join-Path $root "tools\GenerateAppIcon.ps1"
$iconPath = Join-Path $root "assets\app-icon.ico"
$contactSheetPath = Join-Path ([System.IO.Path]::GetTempPath()) (
    "codex-dashboard-app-icon-theme-evidence-{0}.png" -f [Guid]::NewGuid().ToString("N"))

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)

    if ($Expected -ne $Actual) {
        throw "$Message Expected '$Expected', got '$Actual'."
    }
}

function Assert-Rgb {
    param(
        [System.Drawing.Bitmap]$Bitmap,
        [int]$X,
        [int]$Y,
        [int]$Red,
        [int]$Green,
        [int]$Blue,
        [string]$Message
    )

    $actual = $Bitmap.GetPixel($X, $Y)
    $expected = [System.Drawing.Color]::FromArgb(255, $Red, $Green, $Blue)
    if ($actual.ToArgb() -ne $expected.ToArgb()) {
        throw "$Message Expected $expected at ($X,$Y), got $actual."
    }
}

function Assert-BackgroundRect {
    param(
        [System.Drawing.Bitmap]$Bitmap,
        [int]$X,
        [int]$Y,
        [int]$Width,
        [int]$Height,
        [string]$Message
    )

    $background = [System.Drawing.Color]::FromArgb(255, 25, 31, 39).ToArgb()
    for ($py = $Y; $py -lt $Y + $Height; $py++) {
        for ($px = $X; $px -lt $X + $Width; $px++) {
            if ($Bitmap.GetPixel($px, $py).ToArgb() -ne $background) {
                throw "$Message Found label content at ($px,$py)."
            }
        }
    }
}

$shaBefore = (Get-FileHash -Algorithm SHA256 -LiteralPath $iconPath).Hash
try {
    $output = & powershell -NoProfile -ExecutionPolicy Bypass -File $generator `
        -VerifyOnly -ContactSheetPath $contactSheetPath 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Contact-sheet generation failed:`n$($output -join [Environment]::NewLine)"
    }

    $shaAfter = (Get-FileHash -Algorithm SHA256 -LiteralPath $iconPath).Hash
    Assert-Equal $shaBefore $shaAfter "Review-only generation changed the committed ICO SHA-256."

    $sheet = New-Object System.Drawing.Bitmap($contactSheetPath)
    try {
        Assert-Equal 760 $sheet.Width "Unexpected contact-sheet width."
        Assert-Equal 650 $sheet.Height "Expected separate dark/light evidence rows."

        $sampleX = [int[]](30, 203, 376)
        foreach ($x in $sampleX) {
            Assert-Rgb $sheet ($x + 1) 49 31 31 31 "Dark-theme checker swatch is missing."
            Assert-Rgb $sheet ($x + 17) 49 47 47 47 "Dark-theme alternate checker swatch is missing."
            Assert-Rgb $sheet ($x + 1) 363 235 235 235 "Light-theme checker swatch is missing."
            Assert-Rgb $sheet ($x + 17) 363 219 219 219 "Light-theme alternate checker swatch is missing."
        }
        foreach ($labelY in [int[]](18, 332)) {
            Assert-BackgroundRect $sheet 180 $labelY 19 20 "First and second size labels overlap."
            Assert-BackgroundRect $sheet 353 $labelY 19 20 "Second and third size labels overlap."
        }
    }
    finally { $sheet.Dispose() }

    Write-Host "Application icon theme evidence test passed; ICO SHA-256 remained $shaAfter."
}
finally {
    if (Test-Path -LiteralPath $contactSheetPath) {
        Remove-Item -LiteralPath $contactSheetPath -Force
    }
}
