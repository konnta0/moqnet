#!/usr/bin/env bash
# Cross-compile moqnet-ffi for Android. Requires Android NDK (r21+).
# Video hardware backends are omitted; use audio only.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CRATE="$ROOT/native/moqnet-ffi"
OUT="$ROOT/native/output"
FEATURES="${MOQNET_FEATURES:-audio}"
API="${ANDROID_API_LEVEL:-21}"

NDK_PATH="${ANDROID_NDK_HOME:-${NDK_HOME:-}}"
if [[ -z "$NDK_PATH" ]]; then
  echo "Error: set ANDROID_NDK_HOME or NDK_HOME to an NDK r21+ install." >&2
  exit 1
fi
if [[ ! -d "$NDK_PATH" ]]; then
  echo "Error: NDK path does not exist: $NDK_PATH" >&2
  exit 1
fi

detect_ndk_host_tag() {
  local candidates=()
  case "$(uname -s)-$(uname -m)" in
    Darwin-arm64) candidates=(darwin-arm64 darwin-x86_64) ;;
    Darwin-*) candidates=(darwin-x86_64) ;;
    Linux-aarch64) candidates=(linux-aarch64 linux-x86_64) ;;
    Linux-*) candidates=(linux-x86_64) ;;
    *)
      echo "Error: unsupported host for Android NDK: $(uname -s) $(uname -m)" >&2
      return 1
      ;;
  esac
  local tag
  for tag in "${candidates[@]}"; do
    if [[ -d "$NDK_PATH/toolchains/llvm/prebuilt/$tag" ]]; then
      printf '%s\n' "$tag"
      return 0
    fi
  done
  echo "Error: no NDK llvm prebuilt toolchain under $NDK_PATH/toolchains/llvm/prebuilt" >&2
  return 1
}

HOST_TAG="$(detect_ndk_host_tag)"
LLVM="$NDK_PATH/toolchains/llvm/prebuilt/$HOST_TAG/bin"

echo "======================================"
echo "Building MoqNet FFI for Android"
echo "NDK: $NDK_PATH"
echo "Host tag: $HOST_TAG"
echo "API: $API"
echo "Features: $FEATURES"
echo "======================================"

cd "$CRATE"

copy_android_lib() {
  local triple="$1"
  local rid="$2"
  mkdir -p "$OUT/$rid"
  cp "target/$triple/release/libmoqnet_ffi.so" "$OUT/$rid/"
  echo "Copied target/$triple/release/libmoqnet_ffi.so -> $OUT/$rid/"
}

if command -v cargo-ndk >/dev/null 2>&1; then
  echo "Using cargo-ndk"
  cargo ndk \
    -t arm64-v8a -t armeabi-v7a -t x86_64 \
    -- build --release --package moqnet_ffi --no-default-features --features "$FEATURES"
  copy_android_lib aarch64-linux-android android-arm64
  copy_android_lib armv7-linux-androideabi android-arm
  copy_android_lib x86_64-linux-android android-x86_64
  echo "Android build complete."
  exit 0
fi

clang_bin() {
  local triple="$1"
  local bin="$LLVM/${triple}${API}-clang"
  if [[ ! -x "$bin" ]]; then
    echo "Error: missing NDK clang: $bin" >&2
    exit 1
  fi
  printf '%s\n' "$bin"
}

build_target() {
  local triple="$1"
  local rid="$2"
  local clang_triple="$3"
  local linker
  linker="$(clang_bin "$clang_triple")"

  echo ""
  echo "Building $triple..."
  rustup target add "$triple"

  case "$triple" in
    aarch64-linux-android)
      export CARGO_TARGET_AARCH64_LINUX_ANDROID_LINKER="$linker"
      export CC_aarch64_linux_android="$linker"
      export AR_aarch64_linux_android="$LLVM/llvm-ar"
      ;;
    armv7-linux-androideabi)
      export CARGO_TARGET_ARMV7_LINUX_ANDROIDEABI_LINKER="$linker"
      export CC_armv7_linux_androideabi="$linker"
      export AR_armv7_linux_androideabi="$LLVM/llvm-ar"
      ;;
    x86_64-linux-android)
      export CARGO_TARGET_X86_64_LINUX_ANDROID_LINKER="$linker"
      export CC_x86_64_linux_android="$linker"
      export AR_x86_64_linux_android="$LLVM/llvm-ar"
      ;;
  esac

  cargo build --release --target "$triple" --package moqnet_ffi --no-default-features --features "$FEATURES"
  copy_android_lib "$triple" "$rid"
}

build_target aarch64-linux-android android-arm64 aarch64-linux-android
build_target armv7-linux-androideabi android-arm armv7a-linux-androideabi
build_target x86_64-linux-android android-x86_64 x86_64-linux-android

echo "Android build complete."
