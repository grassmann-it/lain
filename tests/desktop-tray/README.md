# Private tray lifecycle smoke

```sh
./tests/desktop-tray/run-private-xvfb.sh /tmp/lain-tray-evidence
# After an integrated build:
LAIN_DESKTOP_SKIP_BUILD=1 ./tests/desktop-tray/run-private-xvfb.sh /tmp/lain-tray-evidence
```

The runner first discards inherited display, Wayland and desktop D-Bus settings.
It owns one Xvfb selected with `-displayfd`, a private Xauthority cookie, temporary
HOME/XDG/LAIN_HOME directories, an owned Openbox window manager with readiness
check and a private copy of packaged Clearlooks theme assets, and a fresh `dbus-run-session`. The bus uses a minimal configuration with
no service directories, so portals and desktop services cannot autoactivate. It never connects
to the normal desktop bus. The harness refuses startup without these markers.
The 120-second timeout and traps terminate only the runner's Xvfb, Openbox,
Python fixture and private bus. Dependencies include Openbox, xprop, gdbus,
notify-send and the system Python dbus/gi modules.

The actual production App theme, MainWindow and TrayWorkspaceController are used.
Window show/hide/close/minimize events are native Avalonia lifecycle operations on
the private display. An injected exit callback captures Quit UI, while an
injected quota poll, notification sink and controllable availability check keep
real host/provider services and notifications untouched. Native menu commands
use Avalonia's exporter event bridge, not OS pointer hit-testing. This validates
controller behavior on the isolated X server.

A narrow native boundary check additionally uses the production Linux
TrayAvailability and LinuxDesktopNotificationSink without substitutes. A Python
fixture owns org.kde.StatusNotifierWatcher and org.freedesktop.Notifications on
the private bus. The test checks that watcher presence alone is insufficient,
Avalonia really registers an item owned by the .NET process (PID resolved by the
bus daemon), and its active Status property is reachable over D-Bus. Host loss
and recovery change real availability. A single notify-send delivery is captured
by the private service and checked for app/icon, escaping, urgency and expiry.
The fixture displays no notification and never connects to a user bus. This
qualifies native registration and adapters against a protocol fixture; it does
not claim interoperability with every Linux panel or compositor.

Coverage includes independent minimize/close behavior, no-host fallback, reopening
when a tray host disappears, Reopen racing an in-flight hide/close check, quota
menu lines, notification enable/disable, Quit UI, separately persisted View
checkboxes, native notification-toggle rollback when the preference lock blocks
saving, and two required screenshot sizes. MainWindow uses the existing
in-memory allowlisted HTTP fixture. No service command is available to the fake.

IPC tests load UiInstanceOwner from the production assembly in real .NET child
processes. The owner holds/releases its mutex on its original main thread;
second launch requests reopening, independent XDG_RUNTIME_DIR scopes do not
signal each other, and a later process acquires the released owner. Children
inherit only the private display/bus/state environment and have bounded cleanup.
Evidence includes checks, screenshots, IPC result files and the production
assembly path/hash. `native-bus.json` records only fixture registrations/PIDs and
the one deliberately synthetic notification. Image-data hints contain only
dimensions/format/byte count and a SHA-256 of row-normalized pixels, compared
with the packaged cat PNG decoded by GdkPixbuf; raw pixels are never recorded.
The icon may travel as the exact asset path or as matching image data.
`native-bus.log` and `openbox.log` retain isolated session diagnostics. No credentials or provider payloads are recorded.
