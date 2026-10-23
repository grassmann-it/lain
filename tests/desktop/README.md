# Desktop smoke test on private Xvfb

Run from any directory:

```sh
./tests/desktop/run-private-xvfb.sh
# Or keep evidence in an explicit directory:
./tests/desktop/run-private-xvfb.sh /tmp/lain-desktop-evidence
```

Requires .NET 10 SDK, Xvfb and xauth. The script builds the integrated
`src/Lain.Desktop` project through a project reference; no copied UI source or
substitute theme is compiled. After an existing build, use
`LAIN_DESKTOP_SKIP_BUILD=1 ./tests/desktop/run-private-xvfb.sh`.
NuGet restore follows the repository's package configuration.

The runner unsets inherited DISPLAY, WAYLAND_DISPLAY and the desktop D-Bus
session before doing anything else. Xvfb chooses a free display atomically with
`-displayfd`, disables TCP, and requires a random Xauthority cookie. Only this
process's display is passed to the harness. An isolated temporary HOME,
XDG_RUNTIME_DIR and LAIN_HOME prevent real local credential discovery. The
harness refuses to initialize Avalonia unless the runner's live Xvfb PID and
display marker are present. There is no native desktop fallback. EXIT/INT/TERM
cleanup terminates only the child Xvfb and removes its private temporary files;
a 100-second timeout bounds the harness.

Production `App.Initialize()` supplies theme resources/styles; its framework
startup is deliberately not invoked because it constructs the real local API
client. The test's MainWindow and UsageView are the actual production classes.
Every HTTP request goes to an in-memory handler with an explicit allowlist and
no Authorization header. Five accounts, 45 routes, three profiles and persistent
usage totals provide realistic density. All account/import/route/profile/login
mutations affect that handler alone. Codex export writes fixture content only
inside the chosen evidence directory, without launching anything.

Coverage includes account add/disable/import, revisioned route/profile saves,
profile apply, Codex export, login completion/cancel, null token values, API
errors/recovery, stale offline state, five-second Usage polling, input focus
across polling, detach cancellation and window close/reopen. Every captured
state is rendered at both 1120×780 and 800×560, with actual client size asserted.
`evidence.json` records checks, requests, image names, the production assembly
path and SHA-256. `run.log`, `xvfb.log`, `failure.txt` (on failure), PNGs and fake
export files stay in the evidence directory.

Button interactions raise Avalonia routed Click events. These validate actual
handlers and enabled-state transitions, not physical pointer hit-testing,
complete keyboard tab order, desktop dialogs, clipboard integration or browser
authorization. Input focus assertions do not replace keyboard accessibility
review. PNGs require independent visual review for clipping, density and theme
acceptance. This fixture cannot establish real host/provider behavior.
