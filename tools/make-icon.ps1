# Draws the V.T.T? icon (green orb with a microphone and spectrum ticks) and saves assets\vtt.ico.
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot "..\assets\vtt.ico"
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

$iconFont = "Segoe MDL2 Assets"
foreach ($name in "Segoe Fluent Icons", "Segoe MDL2 Assets") {
    $f = New-Object System.Drawing.Font($name, 10)
    if ($f.Name -eq $name) { $iconFont = $name; $f.Dispose(); break }
    $f.Dispose()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = "AntiAlias"
    $g.TextRenderingHint = "AntiAliasGridFit"
    $c = $size / 2.0
    $orbR = $size * ($(if ($size -ge 48) { 0.33 } else { 0.46 }))

    if ($size -ge 48) {
        # Spectrum ticks around the orb, like the app's main screen.
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(200, 48, 164, 108)), ($size * 0.035)
        $pen.StartCap = "Round"; $pen.EndCap = "Round"
        $lengths = 0.10, 0.14, 0.08, 0.12, 0.06, 0.11, 0.09, 0.13
        for ($i = 0; $i -lt 32; $i++) {
            $a = -[Math]::PI / 2 + ($i + 0.5) * 2 * [Math]::PI / 32
            $len = $lengths[[Math]::Min($i, 31 - $i) % $lengths.Length] * $size
            $r0 = $orbR + $size * 0.05
            $g.DrawLine($pen, [float]($c + [Math]::Cos($a) * $r0), [float]($c + [Math]::Sin($a) * $r0),
                [float]($c + [Math]::Cos($a) * ($r0 + $len)), [float]($c + [Math]::Sin($a) * ($r0 + $len)))
        }
        $pen.Dispose()
    }

    $rect = New-Object System.Drawing.RectangleF ([float]($c - $orbR)), ([float]($c - $orbR)), ([float]($orbR * 2)), ([float]($orbR * 2))
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 82, 190, 138)), ([System.Drawing.Color]::FromArgb(255, 30, 118, 77)), 90
    $g.FillEllipse($brush, $rect)
    $brush.Dispose()

    $font = New-Object System.Drawing.Font($iconFont, [float]($orbR * 0.95), [System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = "Center"; $fmt.LineAlignment = "Center"
    $g.DrawString([string][char]0xE720, $font, [System.Drawing.Brushes]::White, $rect, $fmt)
    $font.Dispose()
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

# ICO container with PNG-compressed images.
$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close()
Write-Host "Wrote $out"
