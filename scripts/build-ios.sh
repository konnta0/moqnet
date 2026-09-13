#!/usr/bin/env bash
# Cross-compile moqnet-ffi for iOS device and simulator. Requires macOS + Xcode.
# Video hardware backends are omitted; use audio only.
set -euo pipefail

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "Error: iOS builds require macOS with Xcode." >&2
  exit 1
fi
if ! command -v xcrun >/dev/null 2>&1; then
  echo "Error: xcrun not found. Install Xcode command line tools: xcode-select --install" >&2
  exit 1
fi

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CRATE="$ROOT/native/moqnet-ffi"
OUT="$ROOT/native/output"
FEATURES="${MOQNET_FEATURES:-audio}"

echo "======================================"
echo "Building MoqNet FFI for iOS"
echo "Features: $FEATURES"
echo "======================================"

cd "$CRATE"

build_target() {
  local triple="$1"
  local rid="$2"
  echo ""
  echo "Building $triple..."
  rustup target add "$triple"
  cargo build --release --target "$triple" --package moqnet_ffi \
    --no-default-features --features "$FEATURES" \
    --config 'lib.crate-type=["staticlib"]'
  mkdir -p "$OUT/$rid"
  cp "target/$triple/release/libmoqnet_ffi.a" "$OUT/$rid/"
  echo "Copied to $OUT/$rid/"
}

build_target aarch64-apple-ios ios-arm64
build_target aarch64-apple-ios-sim ios-sim-arm64
if rustup target list | grep -q 'x86_64-apple-ios'; then
  build_target x86_64-apple-ios ios-sim-x64 || echo "Warning: Intel simulator target failed (skipping)"
fi

if [[ -f "$OUT/ios-sim-arm64/libmoqnet_ffi.a" && -f "$OUT/ios-sim-x64/libmoqnet_ffi.a" ]]; then
  echo ""
  echo "Creating universal iOS simulator library..."
  mkdir -p "$OUT/ios-sim-universal"
  lipo -create \
    "$OUT/ios-sim-arm64/libmoqnet_ffi.a" \
    "$OUT/ios-sim-x64/libmoqnet_ffi.a" \
    -output "$OUT/ios-sim-universal/libmoqnet_ffi.a"
  echo "Created $OUT/ios-sim-universal/libmoqnet_ffi.a"
fi

echo "iOS build complete."
