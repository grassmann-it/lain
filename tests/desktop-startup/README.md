# Desktop startup acceptance

Run only through `./tests/desktop-startup/run-private-xvfb.sh <evidence-directory>`.
The runner builds serially and creates its own authenticated Xvfb display,
synthetic HOME/XDG directories and LAIN_HOME. It clears the user's display and
session bus first. The executable refuses a non-private test environment.

The harness supplies in-memory ensure/inspect/login-setting callbacks, a memory
startup-preference store and HTTP message handler. It never creates production
lifecycle services, connects to a real Host/provider or invokes a service manager.
Normal workspace preferences use an explicit synthetic file path.

Cases cover immediate window/Settings availability during delayed bootstrap,
concurrent launch/reopen coalescing, missing-token first start, healthy reopen,
tray/pipe callback wiring, independent startup choices, authoritative rollback
and explicit setting retries, absent-Host retry with start-on-open disabled,
Monitor without Core, pending revision preservation, explicit Stop, recovery,
setup-needed and Host-ready/Core-not-ready feedback, and maintenance blocking.
Native shortcut arguments are checked before discovery, including paths with
spaces and rejection of relative paths. Evidence includes checks, in-memory
requests, callback counts and the exact tested Desktop assembly path.

After a successful serial build, set `LAIN_DESKTOP_SKIP_BUILD=1` to reuse that
payload for the private Xvfb run. Existing Settings acceptance remains
`./tests/desktop-settings/run-private-xvfb.sh <evidence-directory>`.

This verifies Desktop's native UI behavior and dependency boundary. Real OS
manager/installer, login/reboot, live streams and actual Host/Core lifecycle
acceptance are separate gates; fixture callbacks cannot establish them.
