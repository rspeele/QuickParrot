#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
output_dir="$repo_dir/artifacts/release/win-x64"

dotnet publish "$repo_dir/src/QuickParrot.App/QuickParrot.App.csproj" \
    --configuration Release \
    --runtime win-x64 \
    --self-contained true \
    --output "$output_dir" \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:PublishTrimmed=false \
    -p:DebugType=None \
    -p:DebugSymbols=false

printf '\nRelease executable: %s/QuickParrot.App.exe\n' "$output_dir"
