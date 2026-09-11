#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY_PKG="$ROOT/src/MoqNet.Unity/Assets/Plugins/com.moqnet.unity"

"$ROOT/scripts/copy-bindings.sh"
"$ROOT/scripts/bundle-upm-native-source.sh" "$UNITY_PKG"

if [[ -d "$ROOT/native/output" ]] && find "$ROOT/native/output" -type f \( -name '*.so' -o -name '*.dll' -o -name '*.dylib' -o -name '*.a' \) -print -quit | grep -q .; then
  "$ROOT/scripts/generate-unity-meta.sh" "$UNITY_PKG" "$ROOT/native/output" native
fi

echo "UPM package ready at $UNITY_PKG"
