# Desktop tray lifecycle

The UI instance owner is scoped to the current user, the absolute normalized
`LAIN_HOME` state directory and the absolute normalized `XDG_RUNTIME_DIR` desktop
session directory. When `XDG_RUNTIME_DIR` is unset or empty, the runtime scope is
`Path.GetTempPath()/lain-ui-<user>`. The scope is hashed for the named mutex and
current-user-only named pipe. Reopening sends only the fixed `reopen` command;
it does not read host credentials or modify host state.

A second `lain ui` launch in the same scope reopens the existing window. A private
Xvfb/DBus verification session must supply its own `XDG_RUNTIME_DIR`, including
when it deliberately reads the same `LAIN_HOME`; it then owns a separate UI and
cannot reopen the normal desktop's window.

The window hides only after a fresh check finds a registered tray host and this
process's registered tray item. Without that confirmation, minimizing and closing
retain normal window behavior. Losing the tray host restores a hidden window.
The tray tooltip includes the quota summary, bounded to eight lines and less than
1000 characters, with a menu hint when more quota details are available. The menu
repeats the summary for desktop environments without hover tooltips. Quit UI exits the UI while the
independent host stays running.

Desktop notification delivery failures are visible in the tray menu. Recent
failed alerts retry by alert ID, bounded to 20 queued alerts and two minutes of
age. Each poll attempts at most three deliveries, rotating failed alerts behind
alerts not attempted yet. Disabling notifications clears the queue and discards
new alerts; enabling them does not replay disabled-period alerts. Alerts that
expire or are displaced by the queue limit are not replayed later.
