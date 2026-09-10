#!/usr/bin/env bash
set -euo pipefail

echo "======================================"
echo "Building MoqNet FFI for the host"
echo "======================================"

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT/native/moqnet-ffi"

if ! command -v cargo >/dev/null 2>&1; then
  echo "Error: cargo not found. Install Rust: https://rustup.rs/" >&2
  exit 1
fi

FEATURES="${MOQNET_FEATURES:-audio,video}"
echo "Features: ${FEATURES}"

# openh264 / video need libstdc++. Some images only ship the soname symlink
# under the gcc lib dir, which rust-lld does not search by default.
if [[ -z "${LIBRARY_PATH:-}" ]]; then
  gcc_lib="$(dirname "$(gcc -print-file-name=libstdc++.so 2>/dev/null || true)")"
  if [[ -n "$gcc_lib" && -d "$gcc_lib" ]]; then
    export LIBRARY_PATH="$gcc_lib"
  fi
fi

cargo build --release --features "${FEATURES}"

NATIVE_TARGET="$(rustc -vV | awk '/^host:/{print $2}')"
echo "Detected native target: ${NATIVE_TARGET}"

OUT="$ROOT/native/output"
case "$NATIVE_TARGET" in
  *darwin*)
    if [[ "$NATIVE_TARGET" == *aarch64* ]]; then
      dest="$OUT/osx-arm64"
    else
      dest="$OUT/osx-x64"
    fi
    mkdir -p "$dest"
    cp target/release/libmoqnet_ffi.dylib "$dest/"
    ;;
  *linux*)
    if [[ "$NATIVE_TARGET" == *aarch64* ]]; then
      dest="$OUT/linux-arm64"
    else
      dest="$OUT/linux-x64"
    fi
    mkdir -p "$dest"
    cp target/release/libmoqnet_ffi.so "$dest/"
    ;;
  *windows*)
    if [[ "$NATIVE_TARGET" == *aarch64* ]]; then
      dest="$OUT/win-arm64"
    else
      dest="$OUT/win-x64"
    fi
    mkdir -p "$dest"
    cp target/release/moqnet_ffi.dll "$dest/"
    ;;
  *)
    echo "Unsupported host target: $NATIVE_TARGET" >&2
    exit 1
    ;;
esac

echo "Copied native library to $dest"
echo "C# bindings: $ROOT/native/output/generated/NativeMethods.g.cs"
