param(
    [string]$Version = '0.1.0',
    [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+([-.][A-Za-z0-9.-]+)?$') { throw 'Invalid release version.' }
$releaseRoot = Split-Path -Parent $PSScriptRoot
if (!$OutputDirectory) { $OutputDirectory = Join-Path $releaseRoot 'dist\releases' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$releaseStamp = [Guid]::NewGuid().ToString('N').Substring(0,8)
$stageRoot = Join-Path $releaseRoot ".inspection\release-$Version-$releaseStamp"
$portableRoot = Join-Path $stageRoot 'CassetteStudio'
New-Item -ItemType Directory -Force $portableRoot | Out-Null
dotnet publish (Join-Path $releaseRoot 'desktop\CassetteStudio.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false `
    "-p:Version=$Version" -o $portableRoot
if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }
# Copy only runtime templates and public demo inputs; never package personal packs.
foreach ($relativeDirectory in @('builder','src','schemas','assets','tools','tools\Scanner','packs','packs\demo')) {
    New-Item -ItemType Directory -Force (Join-Path $portableRoot $relativeDirectory) | Out-Null
}
$releaseFiles = @(
    'builder\build_pack.py',
    'src\ModEntry.cs','src\DeliverySchedule.cs','src\PackConfig.cs','src\MondayCassettePack.csproj',
    'schemas\pack.schema.json','assets\default-gift-box.png',
    'tools\create_demo_pack.py','tools\Scanner\Program.cs','tools\Scanner\Scanner.csproj',
    'packs\demo\pack.json','build.ps1','README.md','STUDIO.md','LICENSE','THIRD_PARTY_NOTICES.md'
)
foreach ($relativeFile in $releaseFiles) {
    Copy-Item -LiteralPath (Join-Path $releaseRoot $relativeFile) -Destination (Join-Path $portableRoot $relativeFile)
}
# The single-file build is portable; debug symbols are not needed by end users.
$debugSymbols = Join-Path $portableRoot 'CassetteStudio.pdb'
if (Test-Path -LiteralPath $debugSymbols) { Remove-Item -LiteralPath $debugSymbols }
python (Join-Path $portableRoot 'tools\create_demo_pack.py')
if ($LASTEXITCODE -ne 0) { throw 'Demo audio generation failed.' }
@"
Cassette Studio $Version - Windows x64

1. Extract the entire ZIP into a writable folder.
2. Run CassetteStudio.exe. Keep builder/, src/, tools/, assets/ and packs/ next to it.

The desktop UI includes the .NET runtime. No separate .NET Desktop Runtime is required to open it.
To build game mods, install .NET 8 SDK, Python 3.10+ and FFmpeg/FFprobe/FFplay on PATH,
and select your local Probably Stolen installation in the app.
SteamCMD is optional and is only required to publish to Steam Workshop.

Only original demo melodies are included. No commercial soundtrack, game DLLs,
Steam credentials or personal Workshop configuration is included.

Docs: README.md and STUDIO.md
Source: https://github.com/LoveLoliii/cassette-studio
"@ | Set-Content -LiteralPath (Join-Path $portableRoot 'START-HERE.txt') -Encoding utf8
$archiveName = "CassetteStudio-$Version-win-x64.zip"
$archive = Join-Path $OutputDirectory $archiveName
if (Test-Path -LiteralPath $archive) { throw "Release archive already exists: $archive" }
Compress-Archive -LiteralPath $portableRoot -DestinationPath $archive -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $archiveName" | Set-Content -LiteralPath (Join-Path $OutputDirectory "$archiveName.sha256") -Encoding ascii
Write-Output "PORTABLE_ROOT:$portableRoot"
Write-Output "RELEASE_ARCHIVE:$archive"
Write-Output "SHA256:$hash"
