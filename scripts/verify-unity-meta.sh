#!/usr/bin/env bash
# Fail if any Unity asset under the given roots is missing a sibling .meta file.
# --source-only skips pack-time native binaries (dll/so/dylib/a) that are injected later.
set -euo pipefail

SOURCE_ONLY=0

is_skipped_path() {
    local path="$1"
    local base
    base="$(basename "$path")"

    case "$base" in
        .DS_Store|Thumbs.db) return 0 ;;
    esac

    case "$path" in
        */Library/*|*/Temp/*|*/Obj/*|*/Logs/*|*/UserSettings/*) return 0 ;;
        *.xcframework/*) return 0 ;;
    esac

    if [[ "$SOURCE_ONLY" -eq 1 ]]; then
        case "$base" in
            *.dll|*.so|*.dylib|*.a|*.bundle) return 0 ;;
            *.xcframework) return 0 ;;
        esac
    fi

    return 1
}

verify_tree() {
    local root="$1"
    if [[ ! -d "$root" ]]; then
        echo "verify-unity-meta: not a directory: $root" >&2
        exit 1
    fi
    root="$(cd "$root" && pwd)"

    local missing=0
    local path
    while IFS= read -r -d '' path; do
        is_skipped_path "$path" && continue
        if [[ ! -f "${path}.meta" ]]; then
            echo "ERROR: missing Unity metadata: ${path}.meta" >&2
            missing=1
        fi
    done < <(find "$root" -mindepth 1 \( -type f -o -type d \) ! -name '*.meta' -print0)

    if [[ "$missing" -ne 0 ]]; then
        exit 1
    fi
    echo "OK: every asset under $root has a .meta"
}

if [[ $# -lt 1 ]]; then
    echo "Usage: $0 [--source-only] <unity_asset_or_package_dir> [more_dirs...]" >&2
    exit 1
fi

if [[ "$1" == "--source-only" ]]; then
    SOURCE_ONLY=1
    shift
fi

if [[ $# -lt 1 ]]; then
    echo "Usage: $0 [--source-only] <unity_asset_or_package_dir> [more_dirs...]" >&2
    exit 1
fi

for root in "$@"; do
    verify_tree "$root"
done
