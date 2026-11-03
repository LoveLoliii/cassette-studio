param(
    [string]$GameDirectory = (Join-Path ${env:ProgramFiles(x86)} 'Steam\steamapps\common\Probably Stolen'),
    [string]$Pack = (Join-Path $PSScriptRoot 'packs\demo\pack.json'),
    [switch]$Install
)
$ErrorActionPreference = 'Stop'
$builderArguments = @('-u', (Join-Path $PSScriptRoot 'builder\build_pack.py'), $Pack, '--game', $GameDirectory)
if ($Install) { $builderArguments += '--install' }
python @builderArguments
if ($LASTEXITCODE -ne 0) { throw 'Pack build or installation failed. See the output above.' }
