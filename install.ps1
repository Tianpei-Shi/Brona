# Brona 一键安装：编译 → 复制到 %LocalAppData%\Programs\Brona → 注册 → 打开默认应用设置
# 用法：在仓库根目录运行  pwsh ./install.ps1
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$target = Join-Path $env:LOCALAPPDATA 'Programs\Brona'

Write-Host '正在编译…'
dotnet publish (Join-Path $root 'src\Brona\Brona.csproj') -c Release -o $dist -nologo -v q
if ($LASTEXITCODE -ne 0) { throw '编译失败' }

# 正在运行的 Brona 会占用文件，先关闭
Get-Process Brona -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host "正在安装到 $target"
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item (Join-Path $dist '*') $target -Recurse -Force

$exe = Join-Path $target 'Brona.exe'
$p = Start-Process $exe -ArgumentList '--register' -PassThru -Wait
if ($p.ExitCode -ne 0) { throw '注册失败' }

Write-Host '已注册。请在打开的 Windows 设置中点击「设为默认值」完成设置。'
Start-Process 'ms-settings:defaultapps?registeredAppUser=Brona'
