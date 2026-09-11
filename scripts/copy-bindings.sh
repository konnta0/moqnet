#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC="$ROOT/native/output/generated/NativeMethods.g.cs"
DEST_NATIVE="$ROOT/src/MoqNet.Native/NativeMethods.g.cs"
DEST_UNITY="$ROOT/src/MoqNet.Unity/Assets/Plugins/com.moqnet.unity/Runtime/Native/NativeMethods.g.cs"

if [[ ! -f "$SRC" ]]; then
  echo "Generated bindings not found at $SRC" >&2
  echo "Build native first: ./scripts/build-native.sh" >&2
  exit 1
fi

mkdir -p "$(dirname "$DEST_NATIVE")"
mkdir -p "$(dirname "$DEST_UNITY")"
cp "$SRC" "$DEST_NATIVE"
cp "$SRC" "$DEST_UNITY"
echo "Copied NativeMethods.g.cs to Native and Unity packages"
