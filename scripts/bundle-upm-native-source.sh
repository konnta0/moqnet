#!/usr/bin/env bash
set -euo pipefail

# Copies the managed Native layer into the UPM package so Unity can apply
# platform-specific DllImport names from the generated bindings.
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY_PKG="${1:-$ROOT/src/MoqNet.Unity/Assets/Plugins/com.moqnet.unity}"
NATIVE_SRC="$ROOT/src/MoqNet.Native"
DEST="$UNITY_PKG/Runtime/Native"

mkdir -p "$DEST"
find "$NATIVE_SRC" -maxdepth 1 -name '*.cs' -print0 | while IFS= read -r -d '' file; do
  cp "$file" "$DEST/"
done

echo "Bundled Native C# sources into $DEST"
