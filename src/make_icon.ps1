$ErrorActionPreference = 'Stop'

# 生成考研提醒的程序图标 app.ico（多尺寸 PNG 压缩 ICO）
Add-Type -AssemblyName System.Drawing

$outPath = Join-Path $PSScriptRoot 'app.ico'
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$pngs = @()

foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 255, 138, 61),
        [System.Drawing.Color]::FromArgb(255, 242, 85, 45),
        [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
    $g.FillEllipse($brush, 1, 1, $size - 3, $size - 3)
    $brush.Dispose()

    # 表盘
    $penWidth = [Math]::Max(1.2, $size * 0.075)
    $facePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(245, 255, 255, 255), $penWidth)
    $inset = $size * 0.20
    $g.DrawEllipse($facePen, $inset, $inset, $size - 2 * $inset, $size - 2 * $inset)
    $facePen.Dispose()

    # 指针
    $handPen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, [Math]::Max(1.4, $size * 0.085))
    $handPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $handPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $cx = $size / 2.0
    $cy = $size / 2.0
    $g.DrawLine($handPen, $cx, $cy, $cx, $size * 0.27)
    $g.DrawLine($handPen, $cx, $cy, $size * 0.72, $size * 0.62)
    $handPen.Dispose()

    # 顶部小点（12 点刻度）
    $tickBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 196, 120))
    $tickSize = [Math]::Max(1.4, $size * 0.075)
    $g.FillEllipse($tickBrush, $cx - $tickSize / 2, $size * 0.10, $tickSize, $tickSize)
    $tickBrush.Dispose()

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $pngs += , $ms.ToArray()
    $ms.Dispose()
}

$count = $pngs.Count
$headerSize = 6
$entrySize = 16
$offset = $headerSize + $entrySize * $count
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$count)
for ($i = 0; $i -lt $count; $i++) {
    $size = $sizes[$i]
    $data = $pngs[$i]
    $dim = if ($size -ge 256) { 0 } else { $size }
    $bw.Write([Byte]$dim)
    $bw.Write([Byte]$dim)
    $bw.Write([Byte]0)
    $bw.Write([Byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($data in $pngs) { $bw.Write($data) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($outPath, $ms.ToArray())
$bw.Close(); $ms.Close()
Write-Host "图标已生成：$outPath ($([math]::Round((Get-Item $outPath).Length / 1KB, 1)) KB)"