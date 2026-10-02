# Draws the app icon (the tray's five-bar waveform, white on a dark rounded square) and writes
# IdleViz.App/Assets/IdleViz.ico. The .ico is committed; run this again only to change the design.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
# The waveform on a 16 x 16 grid: x, top, bottom of each bar. Same shape as the tray glyph.
$bars = @(@(2, 7, 9), @(5, 4, 12), @(8, 2, 14), @(11, 5, 11), @(14, 7, 9))

function New-IconPng([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $radius = $size * 0.22
    $d = $radius * 2
    $edge = $size - $d
    $square = New-Object System.Drawing.Drawing2D.GraphicsPath
    $square.AddArc(0, 0, $d, $d, 180, 90)
    $square.AddArc($edge, 0, $d, $d, 270, 90)
    $square.AddArc($edge, $edge, $d, $d, 0, 90)
    $square.AddArc(0, $edge, $d, $d, 90, 90)
    $square.CloseFigure()
    $background = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 27, 27, 27))
    $g.FillPath($background, $square)

    # The glyph fills the middle 62% of the square.
    $scale = $size * 0.62 / 16
    $offset = ($size - 16 * $scale) / 2
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([single](1.7 * $scale))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    foreach ($bar in $bars) {
        $x = [single]($offset + $bar[0] * $scale)
        $g.DrawLine($pen, $x, [single]($offset + $bar[1] * $scale), $x, [single]($offset + $bar[2] * $scale))
    }

    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bitmap.Dispose()
    return , $stream.ToArray()
}

$images = $sizes | ForEach-Object { , (New-IconPng $_) }

# An .ico file: a 6-byte header, one 16-byte entry per image, then the PNG data.
$out = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $out
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$dataOffset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $side = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$side); $writer.Write([byte]$side)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$dataOffset)
    $dataOffset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write($image) }
$writer.Flush()

$target = Join-Path $PSScriptRoot '..\IdleViz.App\Assets\IdleViz.ico'
[System.IO.File]::WriteAllBytes($target, $out.ToArray())
Write-Host "Wrote $([System.IO.Path]::GetFullPath($target)) ($($out.Length) bytes)"
