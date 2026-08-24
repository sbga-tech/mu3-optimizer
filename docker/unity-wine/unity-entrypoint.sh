#!/usr/bin/env bash
set -Eeuo pipefail

LICENSE_DIR=/opt/wine/drive_c/ProgramData/Unity
LICENSE_FILE="$LICENSE_DIR/Unity_v5.x.ulf"
RETURN_LICENSE_FILE=/tmp/unity-personal-license.ulf
UNITY_EXE=/opt/unity/Editor/Unity.exe
UNITY_TIMEOUT_SECONDS=1200
GENERATED_LICENSE=0
XVFB_PID=0

log() {
    printf '==> %s\n' "$*"
}

load_secret_file() {
    local variable=$1
    local file_variable="${variable}_FILE"
    local file=${!file_variable:-}

    if [[ -n "$file" ]]; then
        if [[ -n "${!variable:-}" ]]; then
            printf 'error: set only %s or %s, not both\n' "$variable" "$file_variable" >&2
            return 1
        fi
        if [[ ! -f "$file" ]]; then
            printf 'error: %s does not exist: %s\n' "$file_variable" "$file" >&2
            return 1
        fi
        printf -v "$variable" '%s' "$(<"$file")"
        export "$variable"
    fi
}

return_generated_license() {
    local build_status=$?
    local return_status=0
    trap - EXIT INT TERM
    set +e

    if (( GENERATED_LICENSE )); then
        log "Returning ephemeral Unity Personal license"
        if [[ -f "$RETURN_LICENSE_FILE" ]]; then
            UNITY_EMAIL="$UNITY_EMAIL" UNITY_PASSWORD="$UNITY_PASSWORD" \
                unity-personal-activate return --license "$RETURN_LICENSE_FILE"
            return_status=$?
        else
            printf 'error: generated Unity return license disappeared\n' >&2
            return_status=1
        fi
        rm -f "$LICENSE_FILE" "$RETURN_LICENSE_FILE"
    fi
    if (( XVFB_PID )); then
        kill "$XVFB_PID" >/dev/null 2>&1 || true
        wait "$XVFB_PID" >/dev/null 2>&1 || true
    fi

    wineserver -k >/dev/null 2>&1 || true

    if (( build_status != 0 )); then
        exit "$build_status"
    fi
    exit "$return_status"
}

trap return_generated_license EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

load_secret_file UNITY_EMAIL
load_secret_file UNITY_PASSWORD

if [[ -z "${UNITY_EMAIL:-}" || -z "${UNITY_PASSWORD:-}" ]]; then
    printf '%s\n' \
        'error: UNITY_EMAIL and UNITY_PASSWORD are required for ephemeral activation' >&2
    exit 2
fi
if grep -q VirtualApple /proc/cpuinfo; then
    unity-mono-rosetta-patch
fi


log "Activating ephemeral Unity Personal license"
mkdir -p "$LICENSE_DIR"
rm -f "$LICENSE_FILE" "$RETURN_LICENSE_FILE"
unity-personal-activate activate --output "$RETURN_LICENSE_FILE"
GENERATED_LICENSE=1
install -m 600 "$RETURN_LICENSE_FILE" "$LICENSE_FILE"

# Keep credentials in this shell for the EXIT trap without exposing them to Unity.
export -n UNITY_EMAIL UNITY_PASSWORD

if [[ ! -s "$LICENSE_FILE" ]]; then
    printf 'error: Unity license file is empty: %s\n' "$LICENSE_FILE" >&2
    exit 2
fi

if [[ $# -eq 0 ]]; then
    printf 'error: Unity command-line arguments are required\n' >&2
    exit 2
fi

mkdir -p /tmp/runtime-dir
chmod 700 /tmp/runtime-dir
export XDG_RUNTIME_DIR=/tmp/runtime-dir

log "Starting virtual display"
Xvfb :99 -screen 0 1280x720x24 -nolisten tcp >/tmp/xvfb.log 2>&1 &
XVFB_PID=$!
export DISPLAY=:99

for _ in {1..50}; do
    if [[ -S /tmp/.X11-unix/X99 ]]; then
        break
    fi
    if ! kill -0 "$XVFB_PID" 2>/dev/null; then
        cat /tmp/xvfb.log >&2
        exit 1
    fi
    sleep 0.1
done

if [[ ! -S /tmp/.X11-unix/X99 ]]; then
    cat /tmp/xvfb.log >&2
    printf 'error: Xvfb did not become ready\n' >&2
    exit 1
fi

log "Executing Unity: $*"
timeout --foreground --signal=TERM "$UNITY_TIMEOUT_SECONDS" \
    env -u UNITY_EMAIL -u UNITY_PASSWORD \
    wine "$UNITY_EXE" "$@"
