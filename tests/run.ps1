param([switch]$Probe)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $csc)) { throw 'The .NET Framework C# compiler was not found.' }

$output = Join-Path ([IO.Path]::GetTempPath()) ('InphicAtkChecks-' + [guid]::NewGuid() + '.exe')
try {
    & $csc /nologo /target:exe /platform:x64 /main:InphicBatteryTray.AtkChecks "/out:$output" `
        /reference:System.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll `
        (Join-Path $root 'src\InphicBatteryTray.cs') (Join-Path $PSScriptRoot 'AtkChecks.cs')
    if ($LASTEXITCODE -ne 0) { throw 'ATK checks did not compile.' }
    if ($Probe) { & $output --probe } else { & $output }
    if ($LASTEXITCODE -ne 0) { throw 'ATK checks failed.' }
}
finally {
    if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output }
}
