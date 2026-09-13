#!/usr/bin/env bash
# Verify MoqFfiVersion, package Version, Cargo.toml, and UPM package.json stay in sync.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSIONS_PROPS="$ROOT/build/Versions.props"
CARGO_TOML="$ROOT/native/moqnet-ffi/Cargo.toml"
PACKAGE_JSON="$ROOT/src/MoqNet.Unity/Assets/Plugins/com.moqnet.unity/package.json"

fail() {
  echo "verify-versions: $*" >&2
  exit 1
}

[[ -f "$VERSIONS_PROPS" ]] || fail "missing $VERSIONS_PROPS"

read_prop() {
  local file="$1" name="$2"
  grep -E "<${name}>" "$file" | head -1 | sed -E "s/.*<${name}>([^<]+)<.*/\1/" | tr -d '[:space:]'
}

MOQ_FFI_VERSION="$(read_prop "$VERSIONS_PROPS" MoqFfiVersion)"
PACKAGE_VERSION="$(read_prop "$VERSIONS_PROPS" Version)"

[[ -n "$MOQ_FFI_VERSION" ]] || fail "MoqFfiVersion is empty in Versions.props"
[[ -n "$PACKAGE_VERSION" ]] || fail "Version is empty in Versions.props"

if [[ ! "$PACKAGE_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  fail "Version must be four numeric segments (e.g. 0.3.17.0), got '$PACKAGE_VERSION'"
fi

if [[ ! "$PACKAGE_VERSION" =~ ^${MOQ_FFI_VERSION//./\\.}\.[0-9]+$ ]]; then
  fail "Version '$PACKAGE_VERSION' must start with MoqFfiVersion '$MOQ_FFI_VERSION.' (e.g. ${MOQ_FFI_VERSION}.0)"
fi

CARGO_VERSION="$(grep -E '^version\s*=' "$CARGO_TOML" | head -1 | sed -E 's/.*"([^"]+)".*/\1/')"
[[ "$CARGO_VERSION" == "$MOQ_FFI_VERSION" ]] || fail "Cargo.toml version ($CARGO_VERSION) != MoqFfiVersion ($MOQ_FFI_VERSION)"

read_json_version() {
  python3 -c "import json, sys; print(json.load(open(sys.argv[1]))['version'])" "$1"
}

UPM_VERSION="$(read_json_version "$PACKAGE_JSON")"
EXPECTED_UPM_VERSION="$("$ROOT/scripts/upm-version.sh" "$PACKAGE_VERSION")"
[[ "$UPM_VERSION" == "$EXPECTED_UPM_VERSION" ]] || fail "package.json ($UPM_VERSION) != expected UPM version ($EXPECTED_UPM_VERSION) from Version ($PACKAGE_VERSION)"

for csproj in "$ROOT"/src/*/*.csproj; do
  if grep -q '<Version>' "$csproj"; then
    fail "per-project <Version> override found in $csproj (use build/Versions.props)"
  fi
done

echo "verify-versions: OK (moq-ffi ${MOQ_FFI_VERSION}, packages ${PACKAGE_VERSION}, UPM ${UPM_VERSION})"
