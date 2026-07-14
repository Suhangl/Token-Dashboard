[CmdletBinding()]
param(
    [string]$OutputPath,
    [switch]$VerifyOnly,
    [string]$ContactSheetPath
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $root "assets\app-icon.ico"
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$iconSizes = [int[]](16, 20, 24, 32, 48, 64, 128, 256)
$pngSignature = [byte[]](137, 80, 78, 71, 13, 10, 26, 10)

function New-QuietGlassPng {
    param([int]$Size)

    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
        $scale = $Size / 16.0
        $small = $Size -le 32
        $inset = if ($small) { [Math]::Max(1.7, 2.0 * $scale) } else { 2.15 * $scale }
        $stroke = if ($small) { [Math]::Max(1.35, 1.55 * $scale) } else { 1.72 * $scale }
        $ring = New-Object System.Drawing.RectangleF(
            [single]$inset, [single]$inset,
            [single]($Size - 2.0 * $inset), [single]($Size - 2.0 * $inset))

        if (-not $small) {
            $shadowRing = $ring
            $shadowRing.Offset(0.0, [single](0.42 * $scale))
            $shadow = New-Object System.Drawing.Pen(
                [System.Drawing.Color]::FromArgb(92, 36, 45, 56), [single]($stroke + 0.75 * $scale))
            try {
                $shadow.StartCap = $shadow.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
                $graphics.DrawArc($shadow, $shadowRing, 135.0, 270.0)
            }
            finally { $shadow.Dispose() }
        }

        $base = New-Object System.Drawing.Pen(
            [System.Drawing.Color]::FromArgb(246, 101, 123, 143), [single]$stroke)
        try {
            $base.StartCap = $base.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            $graphics.DrawArc($base, $ring, 135.0, 270.0)
        }
        finally { $base.Dispose() }

        $upper = New-Object System.Drawing.Pen(
            [System.Drawing.Color]::FromArgb($(if ($small) { 210 } else { 188 }), 210, 224, 235),
            [single]($(if ($small) { [Math]::Max(0.55, 0.56 * $scale) } else { 0.55 * $scale })))
        try {
            $upper.StartCap = $upper.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            $graphics.DrawArc($upper, $ring, 178.0, 138.0)
        }
        finally { $upper.Dispose() }

        if (-not $small) {
            $innerInset = $inset + $stroke * 0.62
            $innerRing = New-Object System.Drawing.RectangleF(
                [single]$innerInset, [single]$innerInset,
                [single]($Size - 2.0 * $innerInset), [single]($Size - 2.0 * $innerInset))
            $highlight = New-Object System.Drawing.Pen(
                [System.Drawing.Color]::FromArgb(105, 244, 248, 251), [single](0.34 * $scale))
            try {
                $highlight.StartCap = $highlight.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
                $graphics.DrawArc($highlight, $innerRing, 190.0, 118.0)
            }
            finally { $highlight.Dispose() }
        }

        $cx = $Size / 2.0
        $cy = $Size / 2.0
        $needle = New-Object System.Drawing.Pen(
            [System.Drawing.Color]::FromArgb(230, 91, 113, 133),
            [single]([Math]::Max(1.0, 1.25 * $scale)))
        try {
            $needle.StartCap = $needle.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            $graphics.DrawLine($needle, [single]$cx, [single]$cy,
                [single]($cx + 2.05 * $scale), [single]($cy - 1.4 * $scale))
        }
        finally { $needle.Dispose() }

        $dotRadius = [Math]::Max(1.0, 1.18 * $scale)
        $healthy = New-Object System.Drawing.SolidBrush(
            [System.Drawing.Color]::FromArgb(246, 112, 165, 139))
        try {
            $graphics.FillEllipse($healthy,
                [single]($cx - $dotRadius), [single]($cy - $dotRadius),
                [single](2.0 * $dotRadius), [single](2.0 * $dotRadius))
        }
        finally { $healthy.Dispose() }

        $stream = New-Object System.IO.MemoryStream
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            return ,$stream.ToArray()
        }
        finally { $stream.Dispose() }
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Write-QuietGlassIco {
    param([string]$Path)

    $payloads = New-Object System.Collections.Generic.List[byte[]]
    foreach ($size in $iconSizes) { $payloads.Add((New-QuietGlassPng -Size $size)) }

    $parent = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$iconSizes.Count)
        $offset = 6 + 16 * $iconSizes.Count
        for ($index = 0; $index -lt $iconSizes.Count; $index++) {
            $sizeByte = if ($iconSizes[$index] -eq 256) { 0 } else { $iconSizes[$index] }
            $writer.Write([byte]$sizeByte)
            $writer.Write([byte]$sizeByte)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$payloads[$index].Length)
            $writer.Write([uint32]$offset)
            $offset += $payloads[$index].Length
        }
        foreach ($payload in $payloads) { $writer.Write($payload) }
        $writer.Flush()
        [System.IO.File]::WriteAllBytes($Path, $stream.ToArray())
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

function Test-QuietGlassIco {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "ICO not found: $Path" }
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $stream = New-Object System.IO.MemoryStream(,$bytes)
    $reader = New-Object System.IO.BinaryReader($stream)
    try {
        if ($reader.ReadUInt16() -ne 0 -or $reader.ReadUInt16() -ne 1) { throw "Invalid ICO header." }
        $count = $reader.ReadUInt16()
        if ($count -ne $iconSizes.Count) { throw "Expected 8 ICO entries, found $count." }
        $previousEnd = 6 + 16 * $count
        for ($index = 0; $index -lt $count; $index++) {
            $encodedWidth = $reader.ReadByte()
            $encodedHeight = $reader.ReadByte()
            $width = if ($encodedWidth -eq 0) { 256 } else { [int]$encodedWidth }
            $height = if ($encodedHeight -eq 0) { 256 } else { [int]$encodedHeight }
            [void]$reader.ReadByte()
            [void]$reader.ReadByte()
            $planes = $reader.ReadUInt16()
            $bitCount = $reader.ReadUInt16()
            $length = $reader.ReadUInt32()
            $offset = $reader.ReadUInt32()
            if ($width -ne $iconSizes[$index] -or $height -ne $iconSizes[$index]) {
                throw "Unexpected ICO size at entry $index`: ${width}x${height}."
            }
            if ($planes -ne 1 -or $bitCount -ne 32) { throw "Invalid format metadata at entry $index." }
            if ($offset -ne $previousEnd -or $length -le 8 -or $offset + $length -gt $bytes.Length) {
                throw "Invalid PNG offset or length at entry $index."
            }
            for ($signatureIndex = 0; $signatureIndex -lt $pngSignature.Count; $signatureIndex++) {
                if ($bytes[$offset + $signatureIndex] -ne $pngSignature[$signatureIndex]) {
                    throw "Entry $index is not PNG-backed."
                }
            }
            if ($width -eq 16 -or $width -eq 32 -or $width -eq 256) {
                $png = New-Object byte[] $length
                [Array]::Copy($bytes, [int]$offset, $png, 0, [int]$length)
                $pngStream = New-Object System.IO.MemoryStream(,$png)
                $bitmap = New-Object System.Drawing.Bitmap($pngStream)
                try {
                    if ($bitmap.Width -ne $width -or $bitmap.Height -ne $height) {
                        throw "PNG dimensions disagree with ICO entry $index."
                    }
                    $lastX = $width - 1
                    $lastY = $height - 1
                    foreach ($point in @(@(0, 0), @($lastX, 0), @(0, $lastY), @($lastX, $lastY))) {
                        if ($bitmap.GetPixel($point[0], $point[1]).A -ne 0) {
                            throw "Representative ${width}px PNG does not have transparent corners."
                        }
                    }
                }
                finally {
                    $bitmap.Dispose()
                    $pngStream.Dispose()
                }
            }
            $previousEnd = [int]($offset + $length)
        }
        if ($previousEnd -ne $bytes.Length) { throw "ICO contains trailing or unreferenced bytes." }
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }
    Write-Host "Verified ICO: exactly $($iconSizes -join ', ') px, PNG-backed, contiguous offsets, transparent representative corners."
}

function Write-ContactSheet {
    param([string]$Path)

    $sheet = New-Object System.Drawing.Bitmap(760, 340, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($sheet)
    try {
        $graphics.Clear([System.Drawing.Color]::FromArgb(255, 25, 31, 39))
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
        $samples = @(16, 32, 256)
        $x = 30
        foreach ($size in $samples) {
            $cell = if ($size -eq 256) { 256 } else { 128 }
            $tile = New-Object System.Drawing.Bitmap($cell, $cell)
            $tileGraphics = [System.Drawing.Graphics]::FromImage($tile)
            $darkTile = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 61, 61, 61))
            $lightTile = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 78, 78, 78))
            $pngStream = New-Object System.IO.MemoryStream(,(New-QuietGlassPng -Size $size))
            $sample = New-Object System.Drawing.Bitmap($pngStream)
            try {
                $tileGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
                $tileGraphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
                for ($cy = 0; $cy -lt $cell; $cy += 16) {
                    for ($cx = 0; $cx -lt $cell; $cx += 16) {
                        $brush = if ((($cx / 16) + ($cy / 16)) % 2 -eq 0) { $darkTile } else { $lightTile }
                        $tileGraphics.FillRectangle($brush, $cx, $cy, 16, 16)
                    }
                }
                $tileGraphics.DrawImage($sample, 0, 0, $cell, $cell)
            }
            finally {
                $sample.Dispose()
                $pngStream.Dispose()
                $darkTile.Dispose()
                $lightTile.Dispose()
                $tileGraphics.Dispose()
            }
            $graphics.DrawImage($tile, $x, 48, $cell, $cell)
            $tile.Dispose()
            $scaleLabel = if ($cell -eq $size) { "native" } else { "x$([int]($cell / $size)) nearest" }
            $graphics.DrawString("${size}px ($scaleLabel)", [System.Drawing.SystemFonts]::MessageBoxFont, [System.Drawing.Brushes]::White, $x, 18)
            $x += $cell + 45
        }
        $parent = Split-Path -Parent ([System.IO.Path]::GetFullPath($Path))
        if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
        $sheet.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $sheet.Dispose()
    }
}

if (-not $VerifyOnly) {
    Write-QuietGlassIco -Path $OutputPath
    Write-Host "Generated $OutputPath from the shared procedural gauge geometry."
}
Test-QuietGlassIco -Path $OutputPath
if (-not [string]::IsNullOrWhiteSpace($ContactSheetPath)) {
    Write-ContactSheet -Path $ContactSheetPath
    Write-Host "Wrote review-only contact sheet to $ContactSheetPath"
}
