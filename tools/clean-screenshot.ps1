# 清理窗口截图：按白色卡片边界裁剪，并加抗锯齿圆角透明遮罩（去除阴影区的黑边）
# 用法：pwsh tools/clean-screenshot.ps1 -Source 源.png -Output 输出.png [-Radius 15]
param(
    [Parameter(Mandatory)][string]$Source,
    [Parameter(Mandatory)][string]$Output,
    [double]$Radius = 15
)
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$src = New-Object System.Drawing.Bitmap ([System.IO.Path]::GetFullPath($Source))
$w = $src.Width; $h = $src.Height
function IsLight($c) { return ($c.R + $c.G + $c.B) -gt 600 }

# 沿中间一行/一列扫描，找到卡片（亮色像素）的边界
$my = [int]($h / 2); $mx = [int]($w / 2)
$x0 = -1; $x1 = -1; $y0 = -1; $y1 = -1
for ($x = 0; $x -lt $w; $x++) { if (IsLight $src.GetPixel($x, $my)) { if ($x0 -lt 0) { $x0 = $x }; $x1 = $x } }
for ($y = 0; $y -lt $h; $y++) { if (IsLight $src.GetPixel($mx, $y)) { if ($y0 -lt 0) { $y0 = $y }; $y1 = $y } }
$cw = $x1 - $x0 + 1; $ch = $y1 - $y0 + 1
Write-Host "卡片边界: x=$x0..$x1 y=$y0..$y1 ($cw x $ch)"

# 裁剪到 32bpp ARGB
$dst = New-Object System.Drawing.Bitmap $cw, $ch, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
for ($y = 0; $y -lt $ch; $y++) {
    for ($x = 0; $x -lt $cw; $x++) {
        $c = $src.GetPixel($x0 + $x, $y0 + $y)
        # 到最近圆角圆心的距离（仅角落区域需要处理）
        $cx = if ($x -lt $Radius) { $Radius } elseif ($x -gt $cw - 1 - $Radius) { $cw - 1 - $Radius } else { $x }
        $cy = if ($y -lt $Radius) { $Radius } elseif ($y -gt $ch - 1 - $Radius) { $ch - 1 - $Radius } else { $y }
        $d = [Math]::Sqrt(($x - $cx) * ($x - $cx) + ($y - $cy) * ($y - $cy))
        $a = [Math]::Min(1.0, [Math]::Max(0.0, $Radius + 0.5 - $d))
        $dst.SetPixel($x, $y, [System.Drawing.Color]::FromArgb([int][Math]::Round($c.A * $a), $c.R, $c.G, $c.B))
    }
}
$src.Dispose()
$dst.Save([System.IO.Path]::GetFullPath($Output), [System.Drawing.Imaging.ImageFormat]::Png)
$dst.Dispose()
Write-Host "已输出 $Output"
