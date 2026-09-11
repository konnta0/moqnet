#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
"$ROOT/scripts/copy-bindings.sh"
"$ROOT/scripts/bundle-upm-native-source.sh"
echo "UPM package ready at src/MoqNet.Unity/Assets/Plugins/com.moqnet.unity"
