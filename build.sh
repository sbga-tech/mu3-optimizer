#!/usr/bin/env bash
set -euo pipefail

readonly ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly UNITY_PROJECT="$ROOT/UnityAssets"
readonly UNITY_IMAGE_CONTEXT="$ROOT/docker/unity-wine"
readonly UNITY_DOCKER_IMAGE="mu3-optimizer-unity:5.6.4f1-wine"
readonly UNITY_DOCKER_PLATFORM="linux/amd64"
readonly DOTNET_DOCKER_IMAGE="mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim@sha256:5dfdb3b19e6900a3dc449760547a0a3fdd1f47900062d274d57e7ab725484c16"
readonly DOTNET_DOCKER_PLATFORM="linux/amd64"
readonly CONFIGURATION="Release"
readonly NATIVE_TARGET="x86_64-pc-windows-gnu"
readonly NATIVE_ROOT="$ROOT/Native"
readonly NATIVE_BUILD="$NATIVE_ROOT/Build"
readonly DOCKER="docker"
UNITY_BUNDLE_STAGE=
UNITY_BUNDLE_IMPORT_STAGE=

usage() {
    cat <<'EOF'
Usage: ./build.sh [all|build-assets|build-native|build-main|patch]

  all           Build every labelled AssetBundle, collect every native DLL,
                build the managed assemblies, and apply both MonoMod patches.
  build-assets  Rebuild every labelled AssetBundle with Unity 5.6.4f1 under
                Wine and collect the flat bundle set into UnityAssets/Build.
  build-native  Cross-compile every Cargo crate directly under Native/ and
                collect all produced Windows DLLs into Native/Build.
  build-main    Collect native DLLs, then build Steroid.sln in the pinned
                .NET SDK container using UnityAssets/Build and Native/Build.
  patch         Apply both MonoMod assemblies in the pinned .NET SDK container.

Unity activation for all/build-assets:
  UNITY_EMAIL            Unity account email
  UNITY_PASSWORD         Unity account password
EOF
}

fail() {
    printf 'error: %s\n' "$*" >&2
    exit 1
}

require_command() {
    command -v "$1" >/dev/null 2>&1 || fail "required command not found: $1"
}


cleanup_unity_build() {
    if [[ -n "$UNITY_BUNDLE_STAGE" ]]; then
        rm -rf "$UNITY_BUNDLE_STAGE"
        UNITY_BUNDLE_STAGE=
    fi
    if [[ -n "$UNITY_BUNDLE_IMPORT_STAGE" ]]; then
        rm -rf "$UNITY_BUNDLE_IMPORT_STAGE"
        UNITY_BUNDLE_IMPORT_STAGE=
    fi
}


build_unity_image() {
    require_command "$DOCKER"
    [[ -f "$UNITY_IMAGE_CONTEXT/Dockerfile" ]] || fail "Unity Dockerfile not found"
    [[ -x "$UNITY_IMAGE_CONTEXT/unity-entrypoint.sh" ]] || fail "Unity entrypoint is not executable"


    printf 'Building Unity 5.6.4f1 Wine image %s from the pinned GameCI base...\n' "$UNITY_DOCKER_IMAGE"
    DOCKER_BUILDKIT=1 "$DOCKER" build \
        --pull \
        --platform "$UNITY_DOCKER_PLATFORM" \
        --tag "$UNITY_DOCKER_IMAGE" \
        "$UNITY_IMAGE_CONTEXT"
    "$DOCKER" image inspect "$UNITY_DOCKER_IMAGE" >/dev/null 2>&1 || \
        fail "Docker image build did not produce $UNITY_DOCKER_IMAGE"
}


build_assets() {
    [[ -n "${UNITY_EMAIL:-}" && -n "${UNITY_PASSWORD:-}" ]] || \
        fail "build-assets requires UNITY_EMAIL and UNITY_PASSWORD"
    build_unity_image

    UNITY_BUNDLE_STAGE="$UNITY_PROJECT/.AssetBundleStage"
    trap cleanup_unity_build EXIT
    trap 'exit 129' HUP
    trap 'exit 130' INT
    trap 'exit 143' TERM


    rm -rf "$UNITY_BUNDLE_STAGE"
    mkdir -p "$UNITY_BUNDLE_STAGE"

    printf 'Building AssetBundles inside Docker with Windows Unity 5.6.4f1 under Wine...\n'
    "$DOCKER" run --rm \
        --init \
        --platform "$UNITY_DOCKER_PLATFORM" \
        --shm-size=1g \
        --env UNITY_EMAIL \
        --env UNITY_PASSWORD \
        --env 'MU3_ASSET_BUNDLE_OUTPUT_DIR=Z:\workspace\.AssetBundleStage' \
        --volume "$UNITY_PROJECT:/workspace" \
        "$UNITY_DOCKER_IMAGE" \
        -batchmode \
        -nographics \
        -quit \
        -projectPath 'Z:\workspace' \
        -executeMethod BuildAssetBundles.BuildAll \
        -logFile -

    local bundle_list="$UNITY_BUNDLE_STAGE/bundles.list"
    [[ -s "$bundle_list" ]] || fail "Unity did not report any built AssetBundles"

    UNITY_BUNDLE_IMPORT_STAGE="$UNITY_PROJECT/.CollectedAssetBundles"
    rm -rf "$UNITY_BUNDLE_IMPORT_STAGE"
    mkdir -p "$UNITY_BUNDLE_IMPORT_STAGE"

    local bundle bundle_count=0
    while IFS= read -r bundle || [[ -n "$bundle" ]]; do
        bundle="${bundle%$'\r'}"
        [[ -n "$bundle" ]] || continue
        case "$bundle" in
            */*|*\\*) fail "AssetBundle names must be flat: $bundle" ;;
        esac
        [[ -s "$UNITY_BUNDLE_STAGE/$bundle" ]] || \
            fail "Unity reported a missing AssetBundle: $bundle"
        cp -f "$UNITY_BUNDLE_STAGE/$bundle" "$UNITY_BUNDLE_IMPORT_STAGE/$bundle"
        bundle_count=$((bundle_count + 1))
    done < "$bundle_list"
    [[ "$bundle_count" -gt 0 ]] || fail "Unity built no AssetBundles"

    rm -rf "$UNITY_PROJECT/Build"
    mv "$UNITY_BUNDLE_IMPORT_STAGE" "$UNITY_PROJECT/Build"
    UNITY_BUNDLE_IMPORT_STAGE=
    cleanup_unity_build
    trap - EXIT HUP INT TERM

    printf 'Collected %d AssetBundles into UnityAssets/Build.\n' "$bundle_count"
}

run_dotnet() {
    require_command "$DOCKER"
    [[ -d "$ROOT/../mu3-reference" ]] || fail "mu3-reference not found beside this repository"

    local uid gid nuget_packages
    uid="$(id -u)"
    gid="$(id -g)"
    nuget_packages="${HOME:?HOME is required}/.nuget/packages"
    mkdir -p "$nuget_packages"

    "$DOCKER" run --rm \
        --init \
        --platform "$DOTNET_DOCKER_PLATFORM" \
        --user "$uid:$gid" \
        --env HOME=/tmp/dotnet-home \
        --env NUGET_PACKAGES=/nuget \
        --env DOTNET_CLI_TELEMETRY_OPTOUT=1 \
        --env DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
        --env DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true \
        --env DOTNET_NOLOGO=1 \
        --volume "$ROOT:/workspace/mu3-optimizer" \
        --volume "$ROOT/../mu3-reference:/workspace/mu3-reference:ro" \
        --volume "$nuget_packages:/nuget" \
        --workdir /workspace/mu3-optimizer \
        --entrypoint /usr/share/dotnet/dotnet \
        "$DOTNET_DOCKER_IMAGE" \
        "$@"
}

build_native() {
    require_command cargo
    require_command x86_64-w64-mingw32-gcc

    rm -rf "$NATIVE_BUILD"
    mkdir -p "$NATIVE_BUILD"

    local manifest crate_dir target_output artifact artifact_name
    local manifest_count=0
    local artifact_count=0
    local crate_artifact_count
    for manifest in "$NATIVE_ROOT"/*/Cargo.toml; do
        [[ -f "$manifest" ]] || continue
        manifest_count=$((manifest_count + 1))
        crate_dir="${manifest%/Cargo.toml}"
        target_output="$crate_dir/target/$NATIVE_TARGET/release"
        rm -f "$target_output"/*.dll

        (
            cd "$crate_dir"
            cargo build --release --target "$NATIVE_TARGET"
        )

        crate_artifact_count=0
        for artifact in "$target_output"/*.dll; do
            [[ -f "$artifact" ]] || continue
            artifact_name="${artifact##*/}"
            [[ ! -e "$NATIVE_BUILD/$artifact_name" ]] || \
                fail "duplicate native module name: $artifact_name"
            cp -f "$artifact" "$NATIVE_BUILD/$artifact_name"
            crate_artifact_count=$((crate_artifact_count + 1))
            artifact_count=$((artifact_count + 1))
        done
        [[ "$crate_artifact_count" -gt 0 ]] || \
            fail "native crate produced no Windows DLL: $manifest"
    done

    [[ "$manifest_count" -gt 0 ]] || fail "no Cargo crates found under Native/"
    [[ "$artifact_count" -gt 0 ]] || fail "native build produced no Windows DLLs"
    printf 'Collected %d native DLLs into Native/Build.\n' "$artifact_count"
}


build_main() {
    build_native
    run_dotnet build Steroid.sln -c "$CONFIGURATION" --no-incremental --force
}

apply_one_monomod() {
    local helper="$1"
    local output="$2"
    local stem="$3"
    local assembly="$output/$stem.dll"
    local mod="$output/$stem.Steroid.mm.dll"
    local patched="$output/MONOMODDED_$stem.dll"

    [[ -f "$ROOT/$assembly" ]] || fail "$stem input assembly not found: $ROOT/$assembly"
    [[ -f "$ROOT/$mod" ]] || fail "$stem mod assembly not found: $ROOT/$mod"
    rm -f "$ROOT/$patched" "$ROOT/$patched.mdb"
    run_dotnet "$helper" "$assembly" "$patched" "$mod"
    [[ -s "$ROOT/$patched" ]] || fail "MonoMod did not produce $ROOT/$patched"
}

apply_monomod() {
    local helper_project="tools/MonoModApply/MonoModApply.csproj"
    local helper="tools/MonoModApply/bin/$CONFIGURATION/net8.0/MonoModApply.dll"
    local output="bin/$CONFIGURATION/net35"

    run_dotnet build "$helper_project" -c "$CONFIGURATION" --no-incremental --force
    [[ -f "$ROOT/$helper" ]] || fail "MonoMod helper not found: $ROOT/$helper"
    apply_one_monomod "$helper" "$output" "Assembly-CSharp"
    apply_one_monomod "$helper" "$output" "AMDaemon.NET"
}

command="${1:-all}"
case "$command" in
    all)
        build_assets
        build_main
        apply_monomod
        ;;
    build-assets) build_assets ;;
    build-native) build_native ;;
    build-main) build_main ;;
    patch) apply_monomod ;;
    -h|--help|help) usage ;;
    *) usage >&2; fail "unknown command: $command" ;;
esac
