#!/usr/bin/env bash
set -euo pipefail
unset DISPLAY WAYLAND_DISPLAY DBUS_SESSION_BUS_ADDRESS
repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
output=${1:?Pass a private evidence directory}
mkdir -p -- "$output"
output=$(cd -- "$output" && pwd)
runtime=$(mktemp -d /tmp/lain-ux-profiles.XXXXXXXX)
xvfb_pid=
cleanup() { if [[ -n "$xvfb_pid" ]]; then kill "$xvfb_pid" 2>/dev/null || true; wait "$xvfb_pid" 2>/dev/null || true; fi; rm -rf -- "$runtime"; }
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
umask 077
touch "$runtime/Xauthority"
Xvfb -displayfd 3 -screen 0 1600x1200x24 -nolisten tcp -auth "$runtime/Xauthority" 3>"$runtime/display" >"$output/xvfb.log" 2>&1 &
xvfb_pid=$!
for ((attempt=0; attempt<100; attempt++)); do [[ -s "$runtime/display" ]] && break; kill -0 "$xvfb_pid"; sleep 0.05; done
display_number=$(cat "$runtime/display")
[[ "$display_number" =~ ^[0-9]+$ ]]
export DISPLAY=":$display_number" XAUTHORITY="$runtime/Xauthority"
xauth -f "$XAUTHORITY" add "$DISPLAY" MIT-MAGIC-COOKIE-1 "$(od -An -N16 -tx1 /dev/urandom | tr -d ' \n')"
mkdir -p "$runtime/xdg" "$runtime/lain" "$runtime/config" "$runtime/data"
export XDG_RUNTIME_DIR="$runtime/xdg" XDG_CONFIG_HOME="$runtime/config" XDG_DATA_HOME="$runtime/data" LAIN_HOME="$runtime/lain"
export LAIN_TEST_XVFB_PID="$xvfb_pid" LAIN_TEST_DISPLAY="$DISPLAY" LAIN_TEST_OUTPUT="$output" AVALONIA_USE_WAYLAND=0
# Build/restore is intentionally separate and requires the integrator's slot.
timeout --signal=TERM --kill-after=5s 90s dotnet "$repo/tests/ux-profiles/bin/Debug/net10.0/linux-x64/UxProfiles.Tests.dll" 2>&1 | tee "$output/run.log"
