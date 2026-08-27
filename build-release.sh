#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT/WindowSnapper.csproj"
PUBLISH_ROOT="$ROOT/publish"
OUT="$PUBLISH_ROOT/linux-x64"
ZIP="$PUBLISH_ROOT/WindowSnapper-linux-x64.zip"

command -v dotnet >/dev/null 2>&1 || { echo ".NET 8 SDK was not found." >&2; exit 1; }

rm -rf "$OUT"
mkdir -p "$OUT"

dotnet restore "$PROJECT"
dotnet publish "$PROJECT" \
  -f net8.0 \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  --no-restore \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -o "$OUT"

chmod +x "$OUT/WindowSnapper" || true
rm -f "$ZIP"
(
  cd "$OUT"
  if command -v zip >/dev/null 2>&1; then
    zip -qr "$ZIP" .
  else
    echo "zip is not installed; Linux build is available at $OUT"
  fi
)

echo "Published Linux build to $OUT"
[[ -f "$ZIP" ]] && echo "Release ZIP: $ZIP"
