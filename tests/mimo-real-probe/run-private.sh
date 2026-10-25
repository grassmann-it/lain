#!/usr/bin/env bash
set -euo pipefail
unset DISPLAY WAYLAND_DISPLAY DBUS_SESSION_BUS_ADDRESS DBUS_STARTER_ADDRESS DBUS_STARTER_BUS_TYPE
if [[ $# != 3 || $1 != --real-mimo-authgate || ${LAIN_MIMO_PROBE_SLOT:-0} != 1 ]]; then
  echo 'Assigned serial slot required: --real-mimo-authgate /absolute/probe.dll /absolute/new-evidence-directory' >&2
  exit 2
fi
probe=$(realpath -e -- "$2")
output=$(realpath -m -- "$3")
[[ "$output" = /* && ! -e "$output" && -f "$probe" && ${LAIN_MIMO_PROBE_HOST_SHA256:-} =~ ^[0-9a-fA-F]{64}$ ]] || exit 2
for dependency in dotnet Xvfb xauth dbus-daemon import timeout; do
  command -v "$dependency" >/dev/null || { echo "Missing dependency: $dependency" >&2; exit 2; }
done
umask 077
mkdir -p -- "$output"
runtime=$(mktemp -d /tmp/lain-mimo-real-probe-XXXXXXXX)
xvfb_pid=
dbus_pid=
probe_pid=
cleanup() {
  if [[ -n "$probe_pid" ]]; then kill "$probe_pid" 2>/dev/null || true; wait "$probe_pid" 2>/dev/null || true; fi
  # Timeout/failure may bypass managed disposal. Kill only receipts whose PID
  # and exact --user-data-dir still identify our fresh owned random profile.
  python3 - "$runtime" <<'PY'
import json, os, pathlib, signal, sys
root=pathlib.Path(sys.argv[1])
for receipt in root.glob('lain/credentials/console/mimo/*/lain-process.json'):
    try:
        saved=json.loads(receipt.read_text())
        pid=saved['pid']
        args=pathlib.Path(f'/proc/{pid}/cmdline').read_bytes().split(b'\0')
        if pid > 0 and ('--user-data-dir='+str(receipt.parent)).encode() in args:
            # The exact fresh random profile path binds this process to this run.
            os.kill(pid,signal.SIGTERM)
    except (FileNotFoundError, ProcessLookupError, PermissionError, KeyError, ValueError):
        pass
PY
  if [[ -n "$dbus_pid" ]]; then kill "$dbus_pid" 2>/dev/null || true; wait "$dbus_pid" 2>/dev/null || true; fi
  if [[ -n "$xvfb_pid" ]]; then kill "$xvfb_pid" 2>/dev/null || true; wait "$xvfb_pid" 2>/dev/null || true; fi
  rm -rf -- "$runtime"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
mkdir -p "$runtime/xdg/config" "$runtime/xdg/data" "$runtime/xdg/cache" "$runtime/run" "$runtime/lain"
touch "$runtime/Xauthority"
Xvfb -displayfd 3 -screen 0 1280x900x24 -nolisten tcp -auth "$runtime/Xauthority" \
  3>"$runtime/display" >"$output/xvfb.log" 2>&1 &
xvfb_pid=$!
for ((attempt=0; attempt<100; attempt++)); do
  [[ -s "$runtime/display" ]] && break
  kill -0 "$xvfb_pid" 2>/dev/null || exit 2
  sleep .05
done
display_number=$(cat "$runtime/display")
[[ "$display_number" =~ ^[0-9]+$ ]] || exit 2
export DISPLAY=":$display_number" XAUTHORITY="$runtime/Xauthority"
cookie=$(od -An -N16 -tx1 /dev/urandom | tr -d ' \n')
xauth -f "$XAUTHORITY" add "$DISPLAY" MIT-MAGIC-COOKIE-1 "$cookie"
unset cookie
# No inherited service directories or user session bus; portals/keyrings cannot
# autostart through this private bus. Chromium may report unavailable integration.
cat >"$runtime/dbus.conf" <<EOF
<busconfig><type>session</type><listen>unix:path=$runtime/run/bus</listen><auth>EXTERNAL</auth>
<policy context="default"><allow own="*"/><allow send_destination="*"/><allow receive_sender="*"/></policy></busconfig>
EOF
dbus-daemon --nofork --config-file="$runtime/dbus.conf" --print-address=1 \
  >"$runtime/bus-address" 2>"$output/dbus.log" &
dbus_pid=$!
for ((attempt=0; attempt<100; attempt++)); do
  [[ -s "$runtime/bus-address" ]] && break
  kill -0 "$dbus_pid" 2>/dev/null || exit 2
  sleep .05
done
export DBUS_SESSION_BUS_ADDRESS="$(head -n 1 "$runtime/bus-address")"
export XDG_CONFIG_HOME="$runtime/xdg/config" XDG_DATA_HOME="$runtime/xdg/data" XDG_CACHE_HOME="$runtime/xdg/cache" XDG_RUNTIME_DIR="$runtime/run"
export LAIN_HOME="$runtime/lain" LAIN_MIMO_PROBE_RUNTIME="$runtime"
export LAIN_TEST_XVFB_PID="$xvfb_pid" LAIN_TEST_DISPLAY="$DISPLAY"
export GDK_BACKEND=x11 QT_QPA_PLATFORM=xcb AVALONIA_USE_WAYLAND=0
timeout --signal=TERM --kill-after=5s 90s dotnet "$probe" --real-mimo-authgate "$output" >"$output/run.log" 2>&1 &
probe_pid=$!
captured=0
while kill -0 "$probe_pid" 2>/dev/null; do
  if [[ $captured == 0 && -f "$output/capture-ready" ]]; then
    if import -window root "$output/public-console-x11.png" 2>"$output/capture.log"; then
      printf 'Private X11 screen captured; requires manual inspection.\n' >"$output/capture-complete"
    else
      printf 'Capture failed; no visual auth-gate claim.\n' >"$output/capture-complete"
    fi
    captured=1
  fi
  sleep .1
done
set +e
wait "$probe_pid"
result=$?
set -e
probe_pid=
printf 'Probe process exit: %s\n' "$result" >>"$output/run.log"
exit "$result"
