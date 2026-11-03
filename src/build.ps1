param(
    [string]$OutDir = '',
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'
$srcDir = $PSScriptRoot
if ([string]::IsNullOrEmpty($OutDir)) {
    $OutDir = Join-Path (Split-Path -Parent $srcDir) 'dist'
}
$OutDir = [System.IO.Path]::GetFullPath($OutDir)
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw '找不到 .NET Framework 编译器 csc.exe' }

$refs = @(
    'System.dll',
    'System.Core.dll',
    'System.Drawing.dll',
    'System.Windows.Forms.dll',
    'System.Xml.dll'
)
$refArgs = @()
foreach ($r in $refs) { $refArgs += "/r:$r" }

$icon = Join-Path $srcDir 'app.ico'
$manifest = Join-Path $srcDir 'app.manifest'
$outExe = Join-Path $OutDir '考研提醒.exe'
$files = @(Get-ChildItem -Path $srcDir -Filter *.cs | Sort-Object Name | ForEach-Object { $_.FullName })
if ($files.Count -eq 0) { throw '没有找到 C# 源文件' }

Write-Host "编译 $($files.Count) 个源文件 -> $outExe"
$args = @('/nologo', '/codepage:65001', '/target:winexe', '/platform:anycpu', '/optimize+', '/warn:2', '/utf8output')
$args += $refArgs
$args += "/win32icon:$icon"
$args += "/win32manifest:$manifest"
$args += "/out:$outExe"
$args += $files

& $csc @args 2>&1 | ForEach-Object { Write-Host $_ }
if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码 $LASTEXITCODE" }
Write-Host "编译成功：$outExe ($([math]::Round((Get-Item $outExe).Length / 1KB, 1)) KB)"