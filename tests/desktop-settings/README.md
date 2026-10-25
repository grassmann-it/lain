# UXD-08 Settings fixture

Integrator's serialized build/runtime slot only:

```sh
tests/desktop-settings/run-private-xvfb.sh /absolute/evidence/directory
```

The runner allocates private Xvfb, isolates LAIN_HOME/XDG paths and rejects the
user's display. The executable uses production `MainWindow`, production theme
resources and the existing in-memory HTTP fixture; it makes no network request,
service change or provider call. Preferences and the intentional write failure
are confined to the runner's new `/tmp/lain-settings-*` home.

Checks cover three wide groups, narrow scrolling to help, one information-level
selector, section density, independent flags, both tray-availability descriptions,
actual assembly version, cancelled revision-bound host review, no host mutation,
preference write failure/checkbox rollback, inline failure and explicit retry of
the captured requested value, retry failure followed by successful storage recovery,
unchanged saved bytes with cleaned temporary writes, and a new window reading
the retried value. PNGs and JSON are written to the requested output; inspect those images
before claiming visual acceptance. The native tray availability source is fake;
this proves Settings projection, not a real desktop tray implementation.

No threshold setting exists in the baseline preference contract. This change
preserves `DesktopSignalMonitor` and its thresholds entirely. Settings do not
create a new threshold, notification engine or persistence store.

Result at handoff: **not_run**, pending root's build/runtime slot. Shell changes
are owned by UXD-01/02; central screenshot/Back/focus/editor scenarios remain in
`tests/desktop` and UXD-09.

The failure check waits for Avalonia to materialize and lay out the previously
collapsed notice before inspecting its visual tree. It still requires the actual
message inside the window viewport, immediate checkbox rollback, unchanged saved
bytes, and restart persistence; it does not substitute a backing-field assertion.
