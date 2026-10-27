#!/usr/bin/env bash
# Generate missing Unity .meta files for source assets (scripts, folders, text).
# GUIDs are MD5 of a repo-relative path so they stay stable across machines.
# Existing .meta files are never overwritten.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

hash_key() {
    if command -v md5sum >/dev/null 2>&1; then
        printf '%s' "$1" | md5sum | cut -d' ' -f1
    else
        printf '%s' "$1" | md5 -q
    fi
}

stable_key() {
    local abs="$1"
    if [[ "$abs" == "$REPO_ROOT/"* ]]; then
        printf '%s\n' "${abs#"$REPO_ROOT"/}"
    else
        printf '%s\n' "$abs"
    fi
}

is_skipped_path() {
    local path="$1"
    local base
    base="$(basename "$path")"

    case "$base" in
        .DS_Store|Thumbs.db) return 0 ;;
        *.dll|*.so|*.dylib|*.a|*.bundle) return 0 ;;
        *.xcframework) return 0 ;;
    esac

    case "$path" in
        *.xcframework|*.xcframework/*) return 0 ;;
        */Library/*|*/Temp/*|*/Obj/*|*/Logs/*|*/UserSettings/*) return 0 ;;
    esac

    return 1
}

write_if_missing() {
    local dest="$1"
    [[ -f "$dest" ]] && return 0
    cat > "$dest"
    echo "generated $dest"
}

write_folder_meta() {
    local folder="$1"
    local guid
    guid="$(hash_key "$(stable_key "$folder")")"
    write_if_missing "${folder}.meta" << METAEOF
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

write_cs_meta() {
    local file="$1"
    local guid
    guid="$(hash_key "$(stable_key "$file")")"
    write_if_missing "${file}.meta" << METAEOF
fileFormatVersion: 2
guid: ${guid}
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
METAEOF
}

write_asmdef_meta() {
    local file="$1"
    local guid
    guid="$(hash_key "$(stable_key "$file")")"
    write_if_missing "${file}.meta" << METAEOF
fileFormatVersion: 2
guid: ${guid}
AssemblyDefinitionImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
METAEOF
}

write_text_meta() {
    local file="$1"
    local guid
    guid="$(hash_key "$(stable_key "$file")")"
    write_if_missing "${file}.meta" << METAEOF
fileFormatVersion: 2
guid: ${guid}
TextScriptImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
METAEOF
}

write_default_meta() {
    local file="$1"
    local guid
    guid="$(hash_key "$(stable_key "$file")")"
    write_if_missing "${file}.meta" << METAEOF
fileFormatVersion: 2
guid: ${guid}
DefaultImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
METAEOF
}

ensure_tree() {
    local root="$1"
    if [[ ! -d "$root" ]]; then
        echo "ensure-unity-source-meta: not a directory: $root" >&2
        exit 1
    fi
    root="$(cd "$root" && pwd)"

    local path
    while IFS= read -r -d '' path; do
        is_skipped_path "$path" && continue
        write_folder_meta "$path"
    done < <(find "$root" -mindepth 1 -type d -print0)

    while IFS= read -r -d '' path; do
        is_skipped_path "$path" && continue
        case "$path" in
            *.cs) write_cs_meta "$path" ;;
            *.asmdef|*.asmref) write_asmdef_meta "$path" ;;
            *.json|*.md|*.txt) write_text_meta "$path" ;;
            *.gitkeep) write_default_meta "$path" ;;
        esac
    done < <(find "$root" -mindepth 1 -type f ! -name '*.meta' -print0)
}

if [[ $# -lt 1 ]]; then
    echo "Usage: $0 <unity_asset_or_package_dir> [more_dirs...]" >&2
    exit 1
fi

for root in "$@"; do
    ensure_tree "$root"
done
