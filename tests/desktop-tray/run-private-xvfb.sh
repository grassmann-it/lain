#!/usr/bin/env bash
set -euo pipefail

# Never inherit the user's desktop, even on a failing dependency/build path.
unset DISPLAY WAYLAND_DISPLAY DBUS_SESSION_BUS_ADDRESS
export GDK_BACKEND=x11 QT_QPA_PLATFORM=xcb AVALONIA_USE_WAYLAND=0
repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
output=${1:-"$repo/tests/desktop-tray/artifacts/$(date -u +%Y%m%dT%H%M%SZ)-$$"}
if (( $# > 0 )); then shift; fi
mkdir -p -- "$output"
output=$(cd -- "$output" && pwd)
for dependency in dotnet Xvfb xauth timeout dbus-run-session openbox xprop rg sha256sum /usr/bin/python3 /usr/bin/gdbus /usr/bin/notify-send; do
  command -v "$dependency" >/dev/null || { echo "Missing dependency: $dependency" >&2; exit 2; }
done

/usr/bin/python3 -c 'import dbus, gi; gi.require_version("GdkPixbuf", "2.0"); from gi.repository import GdkPixbuf'

if [[ ${LAIN_TRAY_HARNESS_DLL+x} == x ]]; then
  # An explicit artifact selection never builds or falls back to workspace output.
  [[ "$LAIN_TRAY_HARNESS_DLL" == /* && -f "$LAIN_TRAY_HARNESS_DLL" ]] || {
    echo "LAIN_TRAY_HARNESS_DLL must name an existing absolute DLL path" >&2; exit 2;
  }
  harness="$LAIN_TRAY_HARNESS_DLL"
else
  # Building does not start a GUI. Skip only after an integrated build.
  if [[ ${LAIN_DESKTOP_SKIP_BUILD:-0} != 1 ]]; then
    dotnet build "$repo/tests/desktop-tray/Lain.Desktop.Tray.Smoke.csproj" --nologo -m:1 -p:BuildInParallel=false -nr:false
  fi
  harness="$repo/tests/desktop-tray/bin/Debug/net10.0/Lain.Desktop.Tray.Smoke.dll"
fi
harness_directory=$(cd -- "$(dirname -- "$harness")" && pwd -P)
export LAIN_TRAY_HARNESS_DLL="$harness_directory/$(basename -- "$harness")"
[[ "$LAIN_TRAY_HARNESS_DLL" == *.dll ]] || { echo "Tray harness must be a DLL" >&2; exit 2; }
for artifact in "$LAIN_TRAY_HARNESS_DLL" "${LAIN_TRAY_HARNESS_DLL%.dll}.deps.json" "${LAIN_TRAY_HARNESS_DLL%.dll}.runtimeconfig.json" \
  "$harness_directory/Lain.Desktop.dll" "$harness_directory/Lain.Client.dll" "$harness_directory/Lain.Core.dll" \
  "$harness_directory/Assets/Brand/lain-cat.png"; do
  [[ -f "$artifact" ]] || { echo "Selected tray target is incomplete: $artifact" >&2; exit 2; }
done
# This manifest describes selected on-disk artifacts; evidence.json separately
# records the loaded production assembly, so the frozen target can be compared.
sha256sum "$LAIN_TRAY_HARNESS_DLL" "$harness_directory/Lain.Desktop.dll" \
  "$harness_directory/Lain.Client.dll" "$harness_directory/Lain.Core.dll" \
  "$harness_directory/Assets/Brand/lain-cat.png" > "$output/target-assemblies.sha256"
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
mkdir -p "$runtime/home/config" "$runtime/home/data" "$runtime/xdg" "$runtime/lain"
# Runtime cannot discover the real user's host credentials or desktop portal.
export HOME="$runtime/home" XDG_CONFIG_HOME="$runtime/home/config" XDG_DATA_HOME="$runtime/home/data"
export XDG_RUNTIME_DIR="$runtime/xdg" LAIN_HOME="$runtime/lain"
# System-only definitions are needed by glycin/GdkPixbuf to decode PNGs.
# The private bus below has no service directories, even with /usr/share visible.
export XDG_DATA_DIRS=/usr/share XDG_CACHE_HOME="$runtime/home/cache"
unset SESSION_MANAGER DESKTOP_STARTUP_ID AT_SPI_BUS_ADDRESS
# No servicedir: portals, accessibility daemons and desktop services cannot be
# autoactivated. Only our explicitly started watcher/notification fixture exists.
cat > "$runtime/bus.conf" <<XML
<!DOCTYPE busconfig PUBLIC "-//freedesktop//DTD D-Bus Bus Configuration 1.0//EN" "http://www.freedesktop.org/standards/dbus/1.0/busconfig.dtd">
<busconfig>
  <type>session</type>
  <listen>unix:tmpdir=$runtime</listen>
  <auth>EXTERNAL</auth>
  <policy context="default">
    <allow own="*"/>
    <allow send_destination="*"/>
    <allow receive_sender="*"/>
  </policy>
</busconfig>
XML
export LAIN_TEST_PRIVATE_BUS=1
export LAIN_TEST_XVFB_PID="$xvfb_pid" LAIN_TEST_DISPLAY="$DISPLAY" LAIN_TEST_OUTPUT="$output"
rm -f -- "$output/native-bus.ready"
timeout --signal=TERM --kill-after=5s 120s dbus-run-session --config-file="$runtime/bus.conf" -- \
  bash "$repo/tests/desktop-tray/private-session.sh" "$repo" "$@" 2>&1 | tee "$output/run.log"
echo "Tray evidence: $output"
