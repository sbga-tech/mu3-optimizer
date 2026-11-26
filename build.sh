#!/usr/bin/env bash
set -euo pipefail

if [ -f .env ]; then
    set -o allexport
    source .env
    set +o allexport
fi

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
readonly MANAGED_OUTPUT="bin/$CONFIGURATION/net35"
readonly TOOL_PROJECT="tools/MonoModTool/MonoModTool.csproj"
readonly TOOL="tools/MonoModTool/bin/$CONFIGURATION/net8.0/MonoModTool.dll"
readonly WORKFLOW_ROOT="$ROOT/../mu3-workflow"
readonly ENDPOINT="${MU3_ENDPOINT:-$ROOT/../.omp/current-endpoint.json}"
ENDPOINT_HOST=
UNITY_BUNDLE_STAGE=
UNITY_BUNDLE_IMPORT_STAGE=

usage() {
    cat <<'EOF'
Usage: ./build.sh [all|build-assets|build-native|build-main|patch|check|deploy SESSION]

  all           Build every labelled AssetBundle, collect every native DLL,
                build the managed assemblies, and apply both MonoMod patches.
  build-assets  Rebuild every labelled AssetBundle with Unity 5.6.4f1 under
                Wine and collect the flat bundle set into UnityAssets/Build.
  build-native  Cross-compile the Zig player hook and remaining Cargo crates;
                collect all produced Windows DLLs into Native/Build.
  build-main    Collect native DLLs, then build Steroid.sln in the pinned
                .NET SDK container using UnityAssets/Build and Native/Build.
  patch         Apply both MonoMod assemblies in the pinned .NET SDK container.
  check         Run the native host tests, build-main, and the switch check:
                every switch off must match an empty MonoMod pass, and each
                switch alone may change only the game types it patches.
  deploy SESSION
                Copy both .mm.dll files into an idle mu3perf session's
                package/BepInEx/monomod and print local and remote SHA-256.
                The runner endpoint is ../.omp/current-endpoint.json unless
                MU3_ENDPOINT names another file.

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
        --env MU3_CONFIG_PATH=/workspace/mu3-optimizer/mu3.ini \
        --env DOTNET_NOLOGO=1 \
        --volume "$ROOT:/workspace/mu3-optimizer" \
        --volume "$ROOT/../mu3-reference:/workspace/mu3-reference" \
        --volume "$nuget_packages:/nuget" \
        --workdir /workspace/mu3-optimizer \
        --entrypoint /usr/share/dotnet/dotnet \
        "$DOTNET_DOCKER_IMAGE" \
        "$@"
}

build_native() {
    require_command zig
    require_command cargo
    require_command x86_64-w64-mingw32-gcc

    rm -rf "$NATIVE_BUILD"
    mkdir -p "$NATIVE_BUILD"

    local manifest crate_dir target_output artifact artifact_name
    local zig_project_count=0
    local manifest_count=0
    local artifact_count=0
    local crate_artifact_count
    for manifest in "$NATIVE_ROOT"/*/build.zig; do
        [[ -f "$manifest" ]] || continue
        zig_project_count=$((zig_project_count + 1))
        crate_dir="${manifest%/build.zig}"
        target_output="$crate_dir/target/zig/bin"
        rm -f "$target_output"/*.dll

        (
            cd "$crate_dir"
            zig build -Doptimize=ReleaseFast --prefix target/zig
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
            fail "native Zig project produced no Windows DLL: $manifest"
    done

    [[ "$zig_project_count" -gt 0 ]] || fail "no native Zig projects found under Native/"
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

    [[ "$manifest_count" -gt 0 ]] || fail "no native Cargo crates found under Native/"
    printf 'Collected %d native DLLs into Native/Build.\n' "$artifact_count"
}


build_main() {
    build_native
    run_dotnet build Steroid.sln -c "$CONFIGURATION" --no-incremental --force
}

build_tool() {
    run_dotnet build "$TOOL_PROJECT" -c "$CONFIGURATION" --no-incremental --force
    [[ -f "$ROOT/$TOOL" ]] || fail "MonoModTool not found: $ROOT/$TOOL"
}

apply_one_monomod() {
    local stem="$1"
    local assembly="$MANAGED_OUTPUT/$stem.dll"
    local mod="$MANAGED_OUTPUT/$stem.Steroid.mm.dll"
    local patched="$MANAGED_OUTPUT/MONOMODDED_$stem.dll"

    [[ -f "$ROOT/$assembly" ]] || fail "$stem input assembly not found: $ROOT/$assembly"
    [[ -f "$ROOT/$mod" ]] || fail "$stem mod assembly not found: $ROOT/$mod"
    rm -f "$ROOT/$patched" "$ROOT/$patched.mdb"
    run_dotnet "$TOOL" apply "$assembly" "$patched" "$mod"
    [[ -s "$ROOT/$patched" ]] || fail "MonoMod did not produce $ROOT/$patched"
}

apply_monomod() {
    build_tool
    apply_one_monomod "Assembly-CSharp"
    apply_one_monomod "AMDaemon.NET"
}

test_native() {
    require_command zig
    require_command cargo

    local manifest project_dir test_file
    for manifest in "$NATIVE_ROOT"/*/Cargo.toml; do
        [[ -f "$manifest" ]] || continue
        (cd "${manifest%/Cargo.toml}" && cargo test --quiet)
    done
    for manifest in "$NATIVE_ROOT"/*/build.zig; do
        [[ -f "$manifest" ]] || continue
        project_dir="${manifest%/build.zig}"
        while IFS= read -r test_file; do
            (cd "$project_dir" && zig test "$test_file")
        done < <(cd "$project_dir" && grep -rl --include='*.zig' '^test "' src | sort)
    done
}

check_switches() {
    local stem
    build_tool
    rm -rf "${ROOT:?}/obj/switch-check"
    for stem in Assembly-CSharp AMDaemon.NET; do
        printf 'Switch check: %s\n' "$stem"
        run_dotnet "$TOOL" check \
            "$MANAGED_OUTPUT/$stem.dll" \
            "$MANAGED_OUTPUT/$stem.Steroid.mm.dll" \
            "obj/switch-check/$stem"
    done
}

mu3perf() {
    uv run --quiet --project "$WORKFLOW_ROOT" mu3perf --json \
        --ssh-config "$ENDPOINT" --host "$ENDPOINT_HOST" "$@"
}

deploy() {
    local session="${1:-}"
    [[ "$session" =~ ^[A-Za-z0-9_-]{1,80}$ ]] || fail "deploy needs a runner session name"
    require_command jq
    require_command uv
    require_command ssh
    require_command scp
    [[ -f "$ENDPOINT" ]] || fail "runner endpoint not found: $ENDPOINT"
    ENDPOINT_HOST="$(jq -er '.hosts | keys | if length == 1 then .[0] else error("set MU3_ENDPOINT to a file with one host") end' "$ENDPOINT")"

    local status busy
    status="$(mu3perf status --session "$session")" || \
        fail "mu3perf status failed: $(jq -r '.error // .' <<<"$status")"
    busy="$(jq -r '[.runs[] | select(.state == "queued" or .state == "running") | .run_id] | join(" ")' <<<"$status")"
    [[ -z "$busy" ]] || fail "session $session has queued or running work: $busy"

    local address user port transfer_root
    local -a ssh_options
    address="$(jq -er --arg h "$ENDPOINT_HOST" '.hosts[$h].host' "$ENDPOINT")"
    user="$(jq -er --arg h "$ENDPOINT_HOST" '.hosts[$h].username' "$ENDPOINT")"
    port="$(jq -er --arg h "$ENDPOINT_HOST" '.hosts[$h].port' "$ENDPOINT")"
    transfer_root="$(jq -er --arg h "$ENDPOINT_HOST" '.hosts[$h].transfer_root' "$ENDPOINT")"
    mapfile -t ssh_options < <(jq -r --arg h "$ENDPOINT_HOST" '.hosts[$h].ssh_options // [] | .[]' "$ENDPOINT")
    ssh_options+=(-o BatchMode=yes)

    local destination="$transfer_root/sessions/$session/package/BepInEx/monomod"
    local stem local_file remote_file
    ssh -p "$port" "${ssh_options[@]}" "$user@$address" "test -d '$destination'" || \
        fail "session package not found on $ENDPOINT_HOST: $destination"
    for stem in Assembly-CSharp AMDaemon.NET; do
        local_file="$ROOT/$MANAGED_OUTPUT/$stem.Steroid.mm.dll"
        remote_file="$destination/$stem.Steroid.mm.dll"
        [[ -s "$local_file" ]] || fail "mod assembly not found; run ./build.sh check first: $local_file"
        scp -q -P "$port" "${ssh_options[@]}" "$local_file" "$user@$address:$remote_file.partial"
        printf '%s  local  %s\n' "$(shasum -a 256 "$local_file" | cut -d' ' -f1)" "$stem.Steroid.mm.dll"
        ssh -p "$port" "${ssh_options[@]}" "$user@$address" \
            "mv -f '$remote_file.partial' '$remote_file' && sha256sum '$remote_file'" | \
            awk -v name="$stem.Steroid.mm.dll" '{ print $1 "  remote " name }'
    done
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
    check)
        test_native
        build_main
        check_switches
        ;;
    deploy) deploy "${2:-}" ;;
    -h|--help|help) usage ;;
    *) usage >&2; fail "unknown command: $command" ;;
esac
