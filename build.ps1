#requires -version 3.0
# 编译 TXT 文件管理器 -> dist\TxtManager.exe
# 仅使用 Windows 自带的 .NET Framework 4.8 编译器，不联网、不装依赖。

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $root

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) { throw "找不到 csc.exe（.NET Framework 4.x 未安装）" }

if (-not (Test-Path 'dist')) { New-Item -ItemType Directory -Path 'dist' | Out-Null }
if (-not (Test-Path 'src\app.ico')) { throw "缺少 src\app.ico" }

$sources = Get-ChildItem -Path 'src' -Filter '*.cs' | ForEach-Object { $_.FullName }
Write-Host "源文件 $($sources.Count) 个" -ForegroundColor DarkGray

$args = @(
    '/nologo'
    '/target:winexe'
    '/platform:anycpu'
    '/optimize+'
    '/debug-'
    '/codepage:65001'
    '/warn:4'
    '/win32icon:src\app.ico'
    '/out:dist\TxtManager.exe'
    '/reference:System.dll'
    '/reference:System.Core.dll'
    '/reference:System.Drawing.dll'
    '/reference:System.Windows.Forms.dll'
) + $sources

$sw = [Diagnostics.Stopwatch]::StartNew()
& $csc @args
$code = $LASTEXITCODE
$sw.Stop()

if ($code -ne 0) { throw "编译失败，退出码 $code" }

$exe = Get-Item 'dist\TxtManager.exe'
Write-Host ("编译成功: {0}  ({1:N0} 字节, 用时 {2:N1} 秒)" -f $exe.FullName, $exe.Length, $sw.Elapsed.TotalSeconds) -ForegroundColor Green
