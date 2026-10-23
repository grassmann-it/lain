#!/usr/bin/env bash
set -euo pipefail

# Never inherit the user's desktop, even on a failing dependency/build path.
unset DISPLAY WAYLAND_DISPLAY DBUS_SESSION_BUS_ADDRESS
export GDK_BACKEND=x11 QT_QPA_PLATFORM=xcb AVALONIA_USE_WAYLAND=0
repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
output=${1:-"$repo/tests/desktop/artifacts/$(date -u +%Y%m%dT%H%M%SZ)-$$"}
mkdir -p -- "$output"
output=$(cd -- "$output" && pwd)
for dependency in dotnet Xvfb xauth timeout; do
  command -v "$dependency" >/dev/null || { echo "Missing dependency: $dependency" >&2; exit 2; }
done

# Building does not start a GUI. Set LAIN_DESKTOP_SKIP_BUILD=1 only after a build.
if [[ ${LAIN_DESKTOP_SKIP_BUILD:-0} != 1 ]]; then
  dotnet build "$repo/tests/desktop/Lain.Desktop.Smoke.csproj" --nologo -m:1 -p:BuildInParallel=false -nr:false
fi
runtime=$(mktemp -d /tmp/lain-desktop-xvfb.XXXXXXXX)
xvfb_pid=
cleanup() {
  if [[ -n "$xvfb_pid" ]]; then kill "$xvfb_pid" 2>/dev/null || true; wait "$xvfb_pid" 2>/dev/null || true; fi
  rm -rf -- "$runtime"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
umask 077
touch "$runtime/Xauthority"
# -displayfd lets Xvfb atomically choose an unused display; no fixed :99 or fallback.
Xvfb -displayfd 3 -screen 0 1600x1200x24 -nolisten tcp -auth "$runtime/Xauthority" \
  3>"$runtime/display" >"$output/xvfb.log" 2>&1 &
xvfb_pid=$!
for ((attempt=0; attempt<100; attempt++)); do
  [[ -s "$runtime/display" ]] && break
  kill -0 "$xvfb_pid" 2>/dev/null || { echo "Private Xvfb failed; see $output/xvfb.log" >&2; exit 2; }
  sleep 0.05
done
display_number=$(cat "$runtime/display")
[[ "$display_number" =~ ^[0-9]+$ ]] || { echo "Xvfb did not allocate a display" >&2; exit 2; }
export DISPLAY=":$display_number" XAUTHORITY="$runtime/Xauthority"
cookie=$(od -An -N16 -tx1 /dev/urandom | tr -d ' \n')
xauth -f "$XAUTHORITY" add "$DISPLAY" MIT-MAGIC-COOKIE-1 "$cookie"
unset cookie
mkdir -p "$runtime/home" "$runtime/xdg" "$runtime/lain"
# Runtime cannot discover the real user's host credentials or desktop portal.
export HOME="$runtime/home" XDG_CONFIG_HOME="$runtime/home/config" XDG_DATA_HOME="$runtime/home/data"
export XDG_RUNTIME_DIR="$runtime/xdg" LAIN_HOME="$runtime/lain"
export LAIN_TEST_XVFB_PID="$xvfb_pid" LAIN_TEST_DISPLAY="$DISPLAY" LAIN_TEST_OUTPUT="$output"
timeout --signal=TERM --kill-after=5s 150s dotnet \
  "$repo/tests/desktop/bin/Debug/net10.0/Lain.Desktop.Smoke.dll" 2>&1 | tee "$output/run.log"
echo "Desktop evidence: $output"
