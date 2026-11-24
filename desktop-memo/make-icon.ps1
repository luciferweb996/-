$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [Math]::Max(0.5, $size * 0.025)
    $bgPath = New-RoundedPath $pad $pad ($size - 2 * $pad) ($size - 2 * $pad) ($size * 0.22)
    $bgRect = New-Object System.Drawing.RectangleF(0, 0, $size, $size)
    $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $bgRect,
        [System.Drawing.Color]::FromArgb(255, 0xC8, 0x7C, 0x3E),
        [System.Drawing.Color]::FromArgb(255, 0xA2, 0x57, 0x1F),
        90.0)
    $g.FillPath($bgBrush, $bgPath)
    $bgBrush.Dispose()

    # Soft inner highlight along the top edge
    $highlightPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(55, 255, 244, 222), [Math]::Max(0.6, $size * 0.012))
    $g.DrawPath($highlightPen, $bgPath)
    $highlightPen.Dispose()

    # Paper page
    $pw = $size * 0.50
    $ph = $size * 0.60
    $px = ($size - $pw) / 2.0
    $py = $size * 0.195
    $pagePath = New-RoundedPath $px $py $pw $ph ($size * 0.055)
    $pageBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0xFF, 0xFA, 0xEF))
    $g.FillPath($pageBrush, $pagePath)
    $pageBrush.Dispose()

    # Folded corner
    $fold = $size * 0.11
    $foldPoints = @(
        (New-Object System.Drawing.PointF(($px + $pw - $fold), $py)),
        (New-Object System.Drawing.PointF(($px + $pw), ($py + $fold))),
        (New-Object System.Drawing.PointF(($px + $pw - $fold), ($py + $fold)))
    )
    $foldBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0xEC, 0xD9, 0xBB))
    $g.FillPolygon($foldBrush, $foldPoints)
    $foldBrush.Dispose()

    # Ruled lines
    $lineColor = [System.Drawing.Color]::FromArgb(255, 0xB4, 0x6B, 0x2D)
    $linePen = New-Object System.Drawing.Pen($lineColor, [Math]::Max(1.0, $size * 0.045))
    $linePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $linePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $x1 = $px + $pw * 0.20
    foreach ($frac in @(0.42, 0.57, 0.72)) {
        $y = $py + $ph * $frac
        $x2 = $x1 + $pw * $(if ($frac -eq 0.72) { 0.42 } else { 0.60 })
        $g.DrawLine($linePen, [float]$x1, [float]$y, [float]$x2, [float]$y)
    }
    $linePen.Dispose()

    $pagePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(40, 0x6A, 0x3E, 0x18), [Math]::Max(0.5, $size * 0.009))
    $g.DrawPath($pagePen, $pagePath)
    $pagePen.Dispose()

    $stream = New-Object System.IO.MemoryStream
    $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()
    $stream.Dispose()
    $bmp.Dispose()
    $g.Dispose()
    $pagePath.Dispose()
    $bgPath.Dispose()
    return ,$bytes
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = @()
foreach ($s in $sizes) {
    $images += ,@{ Size = $s; Bytes = (New-IconPng $s) }
}

$outPath = Join-Path $PSScriptRoot 'app.ico'
$fs = [System.IO.File]::Create($outPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $s = [int]$img.Size
    $b = [byte[]]$img.Bytes
    if ($s -ge 256) { $bw.Write([byte]0) } else { $bw.Write([byte]$s) }
    if ($s -ge 256) { $bw.Write([byte]0) } else { $bw.Write([byte]$s) }
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$b.Length)
    $bw.Write([UInt32]$offset)
    $offset += $b.Length
}
foreach ($img in $images) {
    $bw.Write([byte[]]$img.Bytes)
}
$bw.Flush()
$bw.Close()
$fs.Close()

Write-Output ("ICO written: $outPath ({0:N0} bytes, {1} sizes)" -f (Get-Item $outPath).Length, $images.Count)
