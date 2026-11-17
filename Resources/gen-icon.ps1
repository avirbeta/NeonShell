# Generates Resources\neon.ico (neon logo)
param(
    [string]$Dest = (Join-Path $PSScriptRoot "neon.ico")
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = New-Object System.Collections.Generic.List[byte[]]

foreach ($s in $sizes) {
    $size = [int]$s
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [Math]::Max(1, [int]($size * 0.02))
    $w = $size - (2 * $pad)
    $rect = New-Object System.Drawing.Rectangle($pad, $pad, $w, $w)
    $c1 = [System.Drawing.Color]::FromArgb(255, 0, 229, 255)
    $c2 = [System.Drawing.Color]::FromArgb(255, 255, 46, 166)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = $size * 0.22
    $d = 2 * $r
    $x1 = [float]$rect.X; $y1 = [float]$rect.Y
    $x2 = [float]($rect.Right - $d); $y2 = [float]($rect.Bottom - $d)
    $path.AddArc($x1, $y1, $d, $d, 180, 90)
    $path.AddArc($x2, $y1, $d, $d, 270, 90)
    $path.AddArc($x2, $y2, $d, $d, 0, 90)
    $path.AddArc($x1, $y2, $d, $d, 90, 90)
    $path.CloseFigure()
    $g.FillPath($brush, $path)

    # letter N
    $penW = [Math]::Max(1.5, $size * 0.115)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, [float]$penW)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $nx1 = $size * 0.32; $nx2 = $size * 0.68; $nyT = $size * 0.30; $nyB = $size * 0.70
    $g.DrawLine($pen, [float]$nx1, [float]$nyB, [float]$nx1, [float]$nyT)
    $g.DrawLine($pen, [float]$nx1, [float]$nyT, [float]$nx2, [float]$nyB)
    $g.DrawLine($pen, [float]$nx2, [float]$nyB, [float]$nx2, [float]$nyT)

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs.Add($ms.ToArray())
    $ms.Dispose()
    $bmp.Dispose()
    $brush.Dispose(); $pen.Dispose(); $path.Dispose()
}

$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($out)
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$sizes.Count)
$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = [int]$sizes[$i]
    $len = $pngs[$i].Length
    $bw.Write([byte]($s -band 0xFF))
    $bw.Write([byte]($s -band 0xFF))
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]$len)
    $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($Dest, $out.ToArray())
$bw.Dispose()
$out.Dispose()

Write-Host "Icon created: $Dest" -ForegroundColor Green
