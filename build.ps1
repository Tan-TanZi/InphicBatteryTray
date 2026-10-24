# 一键编译 InphicBatteryTray.exe
# 只需要 Windows 自带的 .NET Framework 编译器，无需安装 SDK

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path $csc)) {
    throw "找不到 csc.exe，请确认已安装 .NET Framework 4.x"
}

$src    = Join-Path $root "src\InphicBatteryTray.cs"
$asm    = Join-Path $root "src\AssemblyInfo.cs"
$ico    = Join-Path $root "assets\app.ico"
$png    = Join-Path $root "assets\MouseIcon.png"
$out    = Join-Path $root "InphicBatteryTray.exe"

foreach ($f in @($src, $asm, $ico, $png)) {
    if (-not (Test-Path $f)) { throw "缺少文件: $f" }
}

Write-Host "编译中 ..."

& $csc /nologo /target:winexe /platform:x64 `
    /win32icon:$ico `
    "/resource:$png,MouseIcon.png" `
    /out:$out `
    /reference:System.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll `
    $src $asm

if ($LASTEXITCODE -ne 0) { throw "编译失败 (exit $LASTEXITCODE)" }

$item = Get-Item $out
Write-Host ("完成: {0}  ({1:N0} bytes)" -f $item.Name, $item.Length)
