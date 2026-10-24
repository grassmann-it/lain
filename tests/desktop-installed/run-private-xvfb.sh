#!/usr/bin/env bash
# Execute a previously built harness with exact installed/staged assemblies.
# Never build, install, start/stop a host or inherit the user's desktop.
set -euo pipefail
original_lain_home=${LAIN_HOME:-"$HOME/.local/share/lain"}
unset DISPLAY WAYLAND_DISPLAY DBUS_SESSION_BUS_ADDRESS SESSION_MANAGER
export GDK_BACKEND=x11 QT_QPA_PLATFORM=xcb AVALONIA_USE_WAYLAND=0
repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
app= output= from= to= harness="$repo/tests/desktop-installed/bin/Debug/net10.0" state="$original_lain_home"
while (($#)); do
  case "$1" in
    --app) app=${2:?}; shift 2;;
    --output) output=${2:?}; shift 2;;
    --from) from=${2:?}; shift 2;;
    --to) to=${2:?}; shift 2;;
    --harness) harness=${2:?}; shift 2;;
    --state) state=${2:?}; shift 2;;
    *) echo 'Usage: run-private-xvfb.sh --app APP_OR_DESKTOP --output NEW_DIR --from YYYY-MM-DD --to YYYY-MM-DD [--harness BUILT_DIR] [--state OWN_LAIN_HOME]' >&2; exit 2;;
  esac
done
[[ -n "$app" && -n "$output" && -n "$from" && -n "$to" ]] || { echo 'app, output, from and to are required' >&2; exit 2; }
[[ "$app" == /* && "$output" == /* && "$harness" == /* && "$state" == /* && ! -e "$output" ]] || { echo 'Use absolute paths and a nonexistent output directory' >&2; exit 2; }
# Validate complete UTC calendar boundaries before opening a credential or GUI.
python3 - "$from" "$to" <<'PY'
import datetime,sys
start,end=(datetime.datetime.strptime(value,'%Y-%m-%d').replace(tzinfo=datetime.timezone.utc) for value in sys.argv[1:])
if not start < end <= datetime.datetime.now(datetime.timezone.utc) or end-start > datetime.timedelta(days=366):
    raise SystemExit('Use an ordered historical UTC range of at most 366 days')
PY
for dependency in dotnet Xvfb xauth timeout python3; do
  command -v "$dependency" >/dev/null || { echo "Missing dependency: $dependency" >&2; exit 2; }
done
umask 077
mkdir -p -- "$output"
runtime=$(mktemp -d /tmp/lain-installed-probe.XXXXXXXX)
xvfb_pid=
cleanup() {
  if [[ -n "$xvfb_pid" ]]; then kill "$xvfb_pid" 2>/dev/null || true; wait "$xvfb_pid" 2>/dev/null || true; fi
  rm -rf -- "$runtime"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
python3 "$repo/tests/desktop-installed/prepare-target.py" --app "$app" --harness "$harness" --runtime "$runtime"
cp "$runtime/assemblies.json" "$output/selected-assemblies.json"
cp "$runtime/target-selection.json" "$output/target-selection.json"
touch "$runtime/Xauthority"
Xvfb -displayfd 3 -screen 0 1600x1200x24 -nolisten tcp -auth "$runtime/Xauthority" \
  3>"$runtime/display" >"$output/xvfb.log" 2>&1 &
xvfb_pid=$!
for ((attempt=0; attempt<100; attempt++)); do
  [[ -s "$runtime/display" ]] && break
  kill -0 "$xvfb_pid" 2>/dev/null || { echo "Private Xvfb failed; see $output/xvfb.log" >&2; exit 2; }
  sleep 0.05
done
display_number=$(cat "$runtime/display")
[[ "$display_number" =~ ^[0-9]+$ ]] || { echo 'No private display allocated' >&2; exit 2; }
export DISPLAY=":$display_number" XAUTHORITY="$runtime/Xauthority"
cookie=$(od -An -N16 -tx1 /dev/urandom | tr -d ' \n')
xauth -f "$XAUTHORITY" add "$DISPLAY" MIT-MAGIC-COOKIE-1 "$cookie"
unset cookie
mkdir -p "$runtime/home" "$runtime/xdg" "$runtime/bin"
# Defense in depth for accidental framework URL launch: no portal bus, isolated
# MIME configuration, and blocking common Linux browser/desktop launchers.
for command_name in xdg-open gio sensible-browser x-www-browser gnome-open kde-open kde-open5 xdg-email; do
  cat > "$runtime/bin/$command_name" <<'BLOCK'
#!/bin/sh
: > "$LAIN_INSTALLED_RUNTIME/blocked-browser"
exit 126
BLOCK
  chmod 700 "$runtime/bin/$command_name"
done
export BROWSER="$runtime/bin/xdg-open" PATH="$runtime/bin:$PATH"
export HOME="$runtime/home" XDG_CONFIG_HOME="$runtime/home/config" XDG_DATA_HOME="$runtime/home/data" XDG_CACHE_HOME="$runtime/home/cache"
export XDG_RUNTIME_DIR="$runtime/xdg" LAIN_HOME="$state"
export LAIN_TEST_XVFB_PID="$xvfb_pid" LAIN_TEST_DISPLAY="$DISPLAY" LAIN_INSTALLED_OUTPUT="$output"
export LAIN_INSTALLED_RUNTIME="$runtime" LAIN_INSTALLED_MANIFEST="$runtime/assemblies.json"
export LAIN_USAGE_FROM="${from}T00:00:00Z" LAIN_USAGE_TO="${to}T00:00:00Z"
timeout --signal=TERM --kill-after=5s 180s dotnet "$runtime/target/Lain.Desktop.Installed.dll" 2>&1 | tee "$output/run.log"
echo "Installed target evidence: $output/evidence.json"
