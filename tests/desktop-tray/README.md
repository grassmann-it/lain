# Private tray lifecycle smoke

```sh
./tests/desktop-tray/run-private-xvfb.sh /tmp/lain-tray-evidence
# After an integrated build:
LAIN_DESKTOP_SKIP_BUILD=1 ./tests/desktop-tray/run-private-xvfb.sh /tmp/lain-tray-evidence
```

The runner first discards inherited display, Wayland and desktop D-Bus settings.
It owns one Xvfb selected with `-displayfd`, a private Xauthority cookie, temporary
HOME/XDG/LAIN_HOME directories and a fresh `dbus-run-session`. It never connects
to the normal desktop bus. The harness refuses startup without these markers.
The 120-second timeout and traps terminate only the runner's Xvfb/private bus.

The actual production App theme, MainWindow and TrayWorkspaceController are used.
Window show/hide/close/minimize events are native Avalonia lifecycle operations on
the private display. An injected exit callback captures Quit UI, while an
injected quota poll, notification sink and controllable availability check keep
real host/provider services and notifications untouched. Native menu commands
use Avalonia's exporter event bridge, not OS pointer hit-testing. This validates
controller behavior; it does not by itself prove a real desktop tray host exists.

Coverage includes independent minimize/close behavior, no-host fallback, reopening
when a tray host disappears, Reopen racing an in-flight hide/close check, quota
menu lines, notification enable/disable, Quit UI, separately persisted View
checkboxes, and two required screenshot sizes. MainWindow uses the existing
in-memory allowlisted HTTP fixture. No service command is available to the fake.

IPC tests load UiInstanceOwner from the production assembly in real .NET child
processes. The owner holds/releases its mutex on its original main thread;
second launch requests reopening, independent XDG_RUNTIME_DIR scopes do not
signal each other, and a later process acquires the released owner. Children
inherit only the private display/bus/state environment and have bounded cleanup.
Evidence includes checks, screenshots, IPC result files and the production
assembly path/hash. No credentials or payloads are recorded.
