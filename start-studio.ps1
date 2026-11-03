$ErrorActionPreference = 'Stop'
python (Join-Path $PSScriptRoot 'tools\create_demo_pack.py')
if ($LASTEXITCODE -ne 0) { throw 'Demo preparation failed.' }
dotnet build (Join-Path $PSScriptRoot 'desktop\CassetteStudio.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed.' }
Start-Process -FilePath (Join-Path $PSScriptRoot 'desktop\bin\Release\net8.0-windows\CassetteStudio.exe') -WindowStyle Normal
