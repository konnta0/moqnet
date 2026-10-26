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
export IPHONEOS_DEPLOYMENT_TARGET="${IPHONEOS_DEPLOYMENT_TARGET:-15.0}"

echo "======================================"
echo "Building MoqNet FFI for iOS"
echo "Features: $FEATURES"
echo "Deployment target: iOS $IPHONEOS_DEPLOYMENT_TARGET"
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

SIM_LIB="$OUT/ios-sim-arm64/libmoqnet_ffi.a"
if [[ -f "$OUT/ios-sim-universal/libmoqnet_ffi.a" ]]; then
  SIM_LIB="$OUT/ios-sim-universal/libmoqnet_ffi.a"
fi

if [[ -f "$OUT/ios-arm64/libmoqnet_ffi.a" && -f "$SIM_LIB" ]]; then
  echo ""
  echo "Creating iOS XCFramework..."
  XCFRAMEWORK_DIR="$OUT/ios-xcframework/moqnet_ffi.xcframework"
  rm -rf "$XCFRAMEWORK_DIR"
  mkdir -p "$OUT/ios-xcframework"
  xcodebuild -create-xcframework \
    -library "$OUT/ios-arm64/libmoqnet_ffi.a" \
    -library "$SIM_LIB" \
    -output "$XCFRAMEWORK_DIR"
  echo "Created $XCFRAMEWORK_DIR"
fi

echo "iOS build complete."
