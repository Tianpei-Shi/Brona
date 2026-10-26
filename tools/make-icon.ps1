# 生成程序图标 src/Brona/Assets/brona.ico
# 设计：紫蓝渐变圆角方块 + 顶部高光 + 白色分叉箭头（一条链接，两条去路）
# 用法：pwsh tools/make-icon.ps1 [预览PNG输出路径]
param([string]$PreviewPath)

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$out = Join-Path $PSScriptRoot '..\src\Brona\Assets\brona.ico'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

# 在 64x64 设计坐标系中绘制，按 scale 放大
function Draw-Icon([System.Drawing.Graphics]$g, [double]$scale) {
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.ScaleTransform($scale, $scale)

    # 圆角方块路径
    function RoundRect([double]$x, [double]$y, [double]$w, [double]$h, [double]$r) {
        $p = New-Object System.Drawing.Drawing2D.GraphicsPath
        $d = 2 * $r
        $p.AddArc($x, $y, $d, $d, 180, 90)
        $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
        $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
        $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
        $p.CloseFigure()
        return $p
    }
    $bg = RoundRect 2 2 60 60 15

    # 背景：紫 → 蓝 → 青 对角渐变
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(2, 2)), ([System.Drawing.PointF]::new(62, 62)), ([System.Drawing.Color]::Black), ([System.Drawing.Color]::Black)
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend 3
    $blend.Colors = @([System.Drawing.Color]::FromArgb(0x7C, 0x4D, 0xFF), [System.Drawing.Color]::FromArgb(0x44, 0x6B, 0xFF), [System.Drawing.Color]::FromArgb(0x1F, 0xC2, 0xF0))
    $blend.Positions = @(0.0, 0.5, 1.0)
    $grad.InterpolationColors = $blend
    $g.FillPath($grad, $bg)

    # 顶部柔和高光
    $g.SetClip($bg)
    $hl = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(0, 2)), ([System.Drawing.PointF]::new(0, 34)), ([System.Drawing.Color]::FromArgb(56, 255, 255, 255)), ([System.Drawing.Color]::FromArgb(0, 255, 255, 255))
    $g.FillRectangle($hl, 0, 0, 64, 34)
    $g.ResetClip()

    # 图形：先画一层偏移的半透明阴影，再画白色主体
    foreach ($layer in @(@{ Dy = 1.6; Color = [System.Drawing.Color]::FromArgb(45, 20, 20, 80) }, @{ Dy = 0; Color = [System.Drawing.Color]::White })) {
        $dy = $layer.Dy
        $brush = New-Object System.Drawing.SolidBrush $layer.Color
        $pen = New-Object System.Drawing.Pen $layer.Color, 5.2
        $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'

        # 主干与两条分支（用曲线让分叉更柔和）
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddLine(32, 45 + $dy, 32, 37 + $dy)
        $path.StartFigure()
        $path.AddBezier(32, 37 + $dy, 32, 31 + $dy, 26, 28 + $dy, 22, 24 + $dy)
        $path.StartFigure()
        $path.AddBezier(32, 37 + $dy, 32, 31 + $dy, 38, 28 + $dy, 42, 24 + $dy)
        $g.DrawPath($pen, $path)

        # 两个实心箭头（指向左上、右上）
        $left = [System.Drawing.PointF[]]@([System.Drawing.PointF]::new(15.5, 16.5 + $dy), [System.Drawing.PointF]::new(27.5, 19.5 + $dy), [System.Drawing.PointF]::new(18.5, 28.5 + $dy))
        $right = [System.Drawing.PointF[]]@([System.Drawing.PointF]::new(48.5, 16.5 + $dy), [System.Drawing.PointF]::new(36.5, 19.5 + $dy), [System.Drawing.PointF]::new(45.5, 28.5 + $dy))
        $g.FillPolygon($brush, $left)
        $g.FillPolygon($brush, $right)
        $tri = New-Object System.Drawing.Pen $layer.Color, 2.2
        $tri.LineJoin = 'Round'
        $g.DrawPolygon($tri, $left)
        $g.DrawPolygon($tri, $right)

        # 起点圆点（代表被点击的链接）
        $g.FillEllipse($brush, 27, 44.5 + $dy, 10, 10)
    }
}

# 以 4 倍超采样绘制，再高质量缩小到目标尺寸
function Render([int]$size) {
    $big = $size * 4
    $hi = New-Object System.Drawing.Bitmap $big, $big
    $g = [System.Drawing.Graphics]::FromImage($hi)
    $g.Clear([System.Drawing.Color]::Transparent)
    Draw-Icon $g ($big / 64.0)
    $g.Dispose()

    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g2 = [System.Drawing.Graphics]::FromImage($bmp)
    $g2.InterpolationMode = 'HighQualityBicubic'
    $g2.PixelOffsetMode = 'HighQuality'
    $g2.DrawImage($hi, 0, 0, $size, $size)
    $g2.Dispose(); $hi.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $bmp = Render $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    $bmp.Dispose()
}

# 写入 ICO（每个尺寸以 PNG 数据存储）
$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$pngs[$i].Length); $w.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close()
Write-Host "已生成 $out"

# 可选：输出一张预览图（大图 + 各小尺寸并排）
if ($PreviewPath) {
    $pv = New-Object System.Drawing.Bitmap 520, 280
    $g = [System.Drawing.Graphics]::FromImage($pv)
    $g.Clear([System.Drawing.Color]::FromArgb(243, 244, 248))
    $big = Render 256; $g.DrawImage($big, 12, 12); $big.Dispose()
    $x = 290
    foreach ($s in 64, 48, 32, 24, 16) {
        $b = Render $s; $g.DrawImage($b, $x, 140 - $s / 2); $x += $s + 14; $b.Dispose()
    }
    $g.Dispose(); $pv.Save($PreviewPath); $pv.Dispose()
    Write-Host "预览图 $PreviewPath"
}
