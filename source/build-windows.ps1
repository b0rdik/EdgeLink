$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'windows/EdgeLink.csproj'
$destination = Join-Path (Split-Path $PSScriptRoot) 'Windows'
$version = (Get-Content (Join-Path (Split-Path $PSScriptRoot) 'VERSION') -Raw).Trim()
dotnet publish $project -c Release -r win-x64 --self-contained true -p:Version=$version -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -o $destination
if ($LASTEXITCODE -ne 0) { throw 'Windows build failed' }
