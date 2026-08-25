$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "WindowSnapper.csproj"
$out = Join-Path $PSScriptRoot "publish\\win-x64"

dotnet restore $project
dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $out

Write-Host "Published to $out"
