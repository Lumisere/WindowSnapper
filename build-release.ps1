$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "WindowSnapper.csproj"
$publishRoot = Join-Path $PSScriptRoot "publish"
$out = Join-Path $publishRoot "win-x64"
$zip = Join-Path $publishRoot "WindowSnapper-win-x64.zip"

function Run-DotNet {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed with exit code $LASTEXITCODE"
    }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET 8 SDK was not found."
}

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out -Force | Out-Null

Run-DotNet restore $project
Run-DotNet publish $project `
    -f net8.0-windows10.0.19041.0 `
    -c Release `
    -r win-x64 `
    --self-contained true `
    --no-restore `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $out

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip -CompressionLevel Optimal

Write-Host "Published Windows build to $out" -ForegroundColor Green
Write-Host "Release ZIP: $zip" -ForegroundColor Green
