#!/usr/bin/env bash
# Place native binaries into a UPM package and generate Unity .meta files.
# Accepts either CI artifact folders (native-linux-x64) or local output (linux-x64).
set -euo pipefail

hash_path() {
    if command -v md5sum >/dev/null 2>&1; then
        printf '%s' "$1" | md5sum | cut -d' ' -f1
    else
        printf '%s' "$1" | md5 -q
    fi
}

generate_plugin_meta_windows() {
    local file="$1"
    local guid
    guid="$(hash_path "$file")"

    cat > "${file}.meta" << METAEOF
fileFormatVersion: 2
guid: ${guid}
PluginImporter:
  externalObjects: {}
  serializedVersion: 2
  iconMap: {}
  executionOrder: {}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
  - first:
      Any:
    second:
      enabled: 0
      settings: {}
  - first:
      Editor: Editor
    second:
      enabled: 1
      settings:
        DefaultValueInitialized: true
  - first:
      Standalone: Win64
    second:
      enabled: 1
      settings:
        CPU: x86_64
  userData:
  assetBundleName:
  assetBundleVariant:
METAEOF
}

generate_plugin_meta_linux() {
    local file="$1"
    local guid
    guid="$(hash_path "$file")"

    cat > "${file}.meta" << METAEOF
fileFormatVersion: 2
guid: ${guid}
PluginImporter:
  externalObjects: {}
  serializedVersion: 2
  iconMap: {}
  executionOrder: {}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
  - first:
      Any:
    second:
      enabled: 0
      settings: {}
  - first:
      Editor: Editor
    second:
      enabled: 1
      settings:
        DefaultValueInitialized: true
  - first:
      Standalone: Linux64
    second:
      enabled: 1
      settings:
        CPU: x86_64
  userData:
  assetBundleName:
  assetBundleVariant:
METAEOF
}

generate_plugin_meta_macos() {
    local file="$1"
    local guid
    guid="$(hash_path "$file")"

    cat > "${file}.meta" << METAEOF
fileFormatVersion: 2
guid: ${guid}
PluginImporter:
  externalObjects: {}
  serializedVersion: 2
  iconMap: {}
  executionOrder: {}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
  - first:
      Any:
    second:
      enabled: 0
      settings: {}
  - first:
      Editor: Editor
    second:
      enabled: 1
      settings:
        DefaultValueInitialized: true
  - first:
      Standalone: OSXUniversal
    second:
      enabled: 1
      settings:
        CPU: AnyCPU
  userData:
  assetBundleName:
  assetBundleVariant:
METAEOF
}

generate_plugin_meta_ios() {
    local file="$1"
    local cpu="${2:-ARM64}"
    local guid
    guid="$(hash_path "$file")"

    cat > "${file}.meta" << METAEOF
fileFormatVersion: 2
guid: ${guid}
PluginImporter:
  externalObjects: {}
  serializedVersion: 2
  iconMap: {}
  executionOrder: {}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
  - first:
      Any:
    second:
      enabled: 0
      settings: {}
  - first:
      Editor: Editor
    second:
      enabled: 0
      settings:
        DefaultValueInitialized: true
  - first:
      iPhone: iOS
    second:
      enabled: 1
      settings:
        AddToEmbeddedBinaries: false
        CPU: ${cpu}
        CompileFlags:
        FrameworkDependencies:
  userData:
  assetBundleName:
  assetBundleVariant:
METAEOF
}

generate_plugin_meta_android() {
    local file="$1"
    local cpu="$2"
    local guid
    guid="$(hash_path "$file")"

    cat > "${file}.meta" << METAEOF
fileFormatVersion: 2
guid: ${guid}
PluginImporter:
  externalObjects: {}
  serializedVersion: 2
  iconMap: {}
  executionOrder: {}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
  - first:
      Any:
    second:
      enabled: 0
      settings: {}
  - first:
      Editor: Editor
    second:
      enabled: 0
      settings:
        DefaultValueInitialized: true
  - first:
      Android: Android
    second:
      enabled: 1
      settings:
        CPU: ${cpu}
  userData:
  assetBundleName:
  assetBundleVariant:
METAEOF
}

generate_folder_meta() {
    local folder="$1"
    local guid
    guid="$(hash_path "$folder")"
    [[ -f "${folder}.meta" ]] && return 0

    cat > "${folder}.meta" << METAEOF
fileFormatVersion: 2
guid: ${guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
METAEOF
}

UNITY_PKG="${1:-}"
NATIVE_LIBS="${2:-}"
ARTIFACT_PREFIX="${3:-native}"

if [[ -z "$UNITY_PKG" || -z "$NATIVE_LIBS" ]]; then
    echo "Usage: $0 <unity_pkg_path> <native_libs_path> [artifact_prefix]" >&2
    echo "  artifact_prefix defaults to 'native'" >&2
    exit 1
fi

art_dir() {
    local rid="$1"
    if [[ -d "$NATIVE_LIBS/${ARTIFACT_PREFIX}-${rid}" ]]; then
        printf '%s\n' "$NATIVE_LIBS/${ARTIFACT_PREFIX}-${rid}"
    elif [[ -d "$NATIVE_LIBS/${rid}" ]]; then
        printf '%s\n' "$NATIVE_LIBS/${rid}"
    fi
}

strip_lib_prefix() {
    local name
    name="$(basename "$1")"
    printf '%s\n' "${name#lib}"
}

mkdir -p "$UNITY_PKG/Plugins"
generate_folder_meta "$UNITY_PKG/Plugins"

WIN_DIR="$(art_dir win-x64)"
if [[ -n "$WIN_DIR" ]]; then
    mkdir -p "$UNITY_PKG/Plugins/Windows/x86_64"
    generate_folder_meta "$UNITY_PKG/Plugins/Windows"
    generate_folder_meta "$UNITY_PKG/Plugins/Windows/x86_64"
    cp "$WIN_DIR"/*.dll "$UNITY_PKG/Plugins/Windows/x86_64/"
    for f in "$UNITY_PKG/Plugins/Windows/x86_64"/*.dll; do
        generate_plugin_meta_windows "$f"
    done
    echo "Copied Windows x64 native library"
fi

LINUX_DIR="$(art_dir linux-x64)"
if [[ -n "$LINUX_DIR" ]]; then
    mkdir -p "$UNITY_PKG/Plugins/Linux/x86_64"
    generate_folder_meta "$UNITY_PKG/Plugins/Linux"
    generate_folder_meta "$UNITY_PKG/Plugins/Linux/x86_64"
    for src in "$LINUX_DIR"/*.so; do
        cp "$src" "$UNITY_PKG/Plugins/Linux/x86_64/$(strip_lib_prefix "$src")"
    done
    for f in "$UNITY_PKG/Plugins/Linux/x86_64"/*.so; do
        generate_plugin_meta_linux "$f"
    done
    echo "Copied Linux x64 native library"
fi

OSX_ARM="$(art_dir osx-arm64)"
OSX_X64="$(art_dir osx-x64)"
if [[ -n "$OSX_ARM" || -n "$OSX_X64" ]]; then
    mkdir -p "$UNITY_PKG/Plugins/macOS"
    generate_folder_meta "$UNITY_PKG/Plugins/macOS"
    SRC_DIR="${OSX_ARM:-$OSX_X64}"
    for src in "$SRC_DIR"/*.dylib; do
        cp "$src" "$UNITY_PKG/Plugins/macOS/$(strip_lib_prefix "$src")"
    done
    for f in "$UNITY_PKG/Plugins/macOS"/*.dylib; do
        [[ -f "$f" ]] || continue
        generate_plugin_meta_macos "$f"
    done
    if [[ -n "$OSX_ARM" ]]; then
        echo "Copied macOS ARM64 native library"
    else
        echo "Copied macOS x64 native library"
    fi
fi

IOS_XCFRAMEWORK_DIR="$(art_dir ios-xcframework)"
IOS_DIR="$(art_dir ios-arm64)"
if [[ -n "$IOS_XCFRAMEWORK_DIR" && -d "$IOS_XCFRAMEWORK_DIR/moqnet_ffi.xcframework" ]]; then
    mkdir -p "$UNITY_PKG/Plugins/iOS"
    generate_folder_meta "$UNITY_PKG/Plugins/iOS"
    rm -f "$UNITY_PKG/Plugins/iOS/moqnet_ffi.a" "$UNITY_PKG/Plugins/iOS/moqnet_ffi.a.meta"
    rm -rf "$UNITY_PKG/Plugins/iOS/moqnet_ffi.xcframework"
    cp -R "$IOS_XCFRAMEWORK_DIR/moqnet_ffi.xcframework" "$UNITY_PKG/Plugins/iOS/"
    generate_plugin_meta_ios "$UNITY_PKG/Plugins/iOS/moqnet_ffi.xcframework" AnyCPU
    echo "Copied iOS device + Simulator XCFramework"
elif [[ -n "$IOS_DIR" ]]; then
    mkdir -p "$UNITY_PKG/Plugins/iOS"
    generate_folder_meta "$UNITY_PKG/Plugins/iOS"
    for src in "$IOS_DIR"/*.a; do
        cp "$src" "$UNITY_PKG/Plugins/iOS/$(strip_lib_prefix "$src")"
    done
    for f in "$UNITY_PKG/Plugins/iOS"/*.a; do
        [[ -f "$f" ]] || continue
        generate_plugin_meta_ios "$f"
    done
    echo "Copied iOS ARM64 native library"
fi

copy_android() {
    local rid="$1"
    local abi="$2"
    local cpu="$3"
    local src_dir
    src_dir="$(art_dir "$rid")"
    [[ -n "$src_dir" ]] || return 0
    mkdir -p "$UNITY_PKG/Plugins/Android/${abi}"
    generate_folder_meta "$UNITY_PKG/Plugins/Android"
    generate_folder_meta "$UNITY_PKG/Plugins/Android/${abi}"
    # Android keeps the lib prefix (DllImport("moqnet_ffi") -> libmoqnet_ffi.so).
    cp "$src_dir"/*.so "$UNITY_PKG/Plugins/Android/${abi}/"
    for f in "$UNITY_PKG/Plugins/Android/${abi}"/*.so; do
        generate_plugin_meta_android "$f" "$cpu"
    done
    echo "Copied Android ${abi} native library"
}

copy_android android-arm64 arm64-v8a ARM64
copy_android android-arm armeabi-v7a ARMv7
copy_android android-x86_64 x86_64 X86_64

find "$UNITY_PKG" -name '*.meta' | while read -r meta; do
    asset="${meta%.meta}"
    if [[ ! -e "$asset" ]]; then
        echo "WARNING: removing orphaned meta without asset: $meta"
        rm -f "$meta"
    fi
done

echo "=== Native libraries in Unity package ==="
find "$UNITY_PKG/Plugins" -type f 2>/dev/null | sort || echo "No plugins found"
