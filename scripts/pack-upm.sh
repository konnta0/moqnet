#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY_PKG="$ROOT/src/MoqNet.Unity/Assets/Plugins/com.moqnet.unity"
UNITY_ASSETS="$ROOT/src/MoqNet.Unity/Assets"
SAMPLE_ASSETS="$ROOT/samples/Unity/Assets"

"$ROOT/scripts/copy-bindings.sh"
"$ROOT/scripts/bundle-upm-native-source.sh" "$UNITY_PKG"
"$ROOT/scripts/ensure-unity-source-meta.sh" "$UNITY_ASSETS" "$SAMPLE_ASSETS"

if [[ -d "$ROOT/native/output" ]] && find "$ROOT/native/output" -type f \( -name '*.so' -o -name '*.dll' -o -name '*.dylib' -o -name '*.a' \) -print -quit | grep -q .; then
  "$ROOT/scripts/generate-unity-meta.sh" "$UNITY_PKG" "$ROOT/native/output" native
fi

"$ROOT/scripts/verify-unity-meta.sh" --source-only "$UNITY_PKG" "$SAMPLE_ASSETS/MoqNet.Samples"

echo "UPM package ready at $UNITY_PKG"
