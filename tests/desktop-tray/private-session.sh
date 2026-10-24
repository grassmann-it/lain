#!/usr/bin/env bash
set -euo pipefail
[[ ${LAIN_TEST_PRIVATE_BUS:-0} == 1 && -n ${DBUS_SESSION_BUS_ADDRESS:-} ]] || exit 2
repo=$1
shift
wm_pid=
stub_pid=
cleanup() {
  for owned_pid in "$stub_pid" "$wm_pid"; do
    if [[ -n "$owned_pid" ]]; then kill "$owned_pid" 2>/dev/null || true; wait "$owned_pid" 2>/dev/null || true; fi
  done
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
# Explicit config avoids any inherited autostart/session manager or desktop apps.
cat > "$XDG_CONFIG_HOME/openbox.xml" <<'XML'
<openbox_config xmlns="http://openbox.org/3.4/rc"><desktops><number>1</number></desktops><focus><focusNew>yes</focusNew></focus></openbox_config>
XML
openbox --sm-disable --config-file "$XDG_CONFIG_HOME/openbox.xml" >"$LAIN_TEST_OUTPUT/openbox.log" 2>&1 &
wm_pid=$!
for ((attempt=0; attempt<100; attempt++)); do
  if xprop -root _NET_SUPPORTING_WM_CHECK 2>/dev/null | rg -q 'window id # 0x[1-9a-fA-F]'; then break; fi
  kill -0 "$wm_pid" 2>/dev/null || { echo 'Private Openbox exited' >&2; exit 2; }
  sleep 0.05
done
xprop -root _NET_SUPPORTING_WM_CHECK | rg -q 'window id # 0x[1-9a-fA-F]' || { echo 'Private window manager did not become ready' >&2; exit 2; }
export LAIN_TEST_WM_PID="$wm_pid"
/usr/bin/python3 "$repo/tests/desktop-tray/private-bus-stub.py" >"$LAIN_TEST_OUTPUT/native-bus.log" 2>&1 &
stub_pid=$!
for ((attempt=0; attempt<100; attempt++)); do
  [[ -f "$LAIN_TEST_OUTPUT/native-bus.ready" ]] && break
  kill -0 "$stub_pid" 2>/dev/null || { echo 'Private bus fixture exited' >&2; exit 2; }
  sleep 0.05
done
[[ -f "$LAIN_TEST_OUTPUT/native-bus.ready" ]] || { echo 'Private bus fixture did not become ready' >&2; exit 2; }
export LAIN_TEST_BUS_STUB_PID="$stub_pid"
dotnet "$repo/tests/desktop-tray/bin/Debug/net10.0/Lain.Desktop.Tray.Smoke.dll" "$@"
