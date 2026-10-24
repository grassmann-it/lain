# Exact-target installed desktop observation

This independent harness opens the production `MainWindow` and initializes the
production `App` resources from a selected installed application or frozen publish.
It observes the already running own Host at `127.0.0.1:18418` through the public
`LainApiClient` and `LocalDiscovery` token reader. It does not start the Host.

Build once, in the integrator's serialized build slot:

```sh
dotnet restore tests/desktop-installed/Lain.Desktop.Installed.csproj --locked-mode -m:1 -p:BuildInParallel=false -nr:false
dotnet build tests/desktop-installed/Lain.Desktop.Installed.csproj --no-restore --nologo -m:1 -p:BuildInParallel=false -nr:false
```

The runner never builds. After the verified update and controlled Host restart,
select the actual installed `app` folder and a fixed UTC historical calendar range
containing known activity. The upper bound is exclusive and cannot be in the
future; a maximum of 366 days is accepted. Use a new absolute evidence directory:

```sh
./tests/desktop-installed/run-private-xvfb.sh \
  --app /home/tomka/.local/share/lain/app \
  --state /home/tomka/.local/share/lain \
  --output /absolute/new/installed-ui-evidence \
  --from 2026-10-01 --to 2026-10-24
```

`--app` also accepts a standalone frozen Desktop publish directory. In that case
the report describes that selected publish; it does not claim installation parity.
`--harness` overrides the previously built harness directory. `--state` defaults
to the caller's original `LAIN_HOME`, or original `~/.local/share/lain`. The runner
retains that explicit path solely for the management-token reader while isolating
HOME and all XDG directories. No credentials are copied into evidence.

The runner copies the selected payload into a temporary runtime, then overlays
only the harness DLL, deps and runtime configuration. It never overlays production
dependencies from the build directory. Before platform initialization and again
after the GUI workflow, the harness checks the loaded locations and SHA-256 of
Desktop, Client, Core and Avalonia.Controls against both copied and original
selected files. `selected-assemblies.json`, `target-selection.json` and the final
report record those bindings. A target without the original cat PNG or the safe
`MainWindow(client, preferencesPath)` overload is refused.

A private Xvfb is mandatory: the runner clears DISPLAY, Wayland and desktop bus
variables before any runtime operation, creates a new Xauthority and requests an
unused display with `-displayfd`. The executable verifies that the owned process
is Xvfb with that authority. There is no fallback to a user display. Screenshots
are `RenderTargetBitmap` captures of that window at 1120 × 780 and 800 × 560.
They do not prove native taskbar/launcher artwork or OS pointer hit testing.

Every production API client uses the same fail-closed transport. Only GET is
accepted, for an explicit inventory/status/usage/inspector/provider catalog
endpoint allowlist on HTTP `127.0.0.1:18418`. Redirects and proxies are disabled.
Credential issuance, login polling, account checks, quota refresh, service actions,
all write methods, external URLs and all unlisted endpoints are blocked. The UI
click helper separately permits only five navigation actions, provider discovery,
View options and Usage Apply. It never clicks documentation/browser/provider
refresh, login or export controls. Common browser/desktop launch commands are
replaced with blockers; the isolated HOME/XDG and absent portal bus provide
additional isolation. This is an observation harness, not an OS network sandbox.

Checks cover Accounts as the initial page, exactly five destinations, the semantic
`Application logo` image and window icon, the complete configured account inventory
and named quota windows with supplied percentages and explicit remaining states,
configured Models, explicit known/unknown applied profile state, real provider
catalog names, persistent Usage and the observed Live source state. Screenshots
cover each area and Accounts in Barebones/Advanced/Simple with independent density.
Presentation writes only the explicit temporary preferences path; its final JSON
is copied to `preferences-observed.json`, never to installed preferences.

The same historical Usage range is read before the GUI, while closed and after
reopening. Range, totals, groups and coverage must remain equal; observed timestamps
and global cursor are recorded but excluded from equality because independent
current activity can advance a cursor. Configuration must remain identical and
Host/proxy ready through close/reopen. Concurrent external activity is neither
disabled nor attributed to this probe; a changed historical range fails with
observable evidence. Reopen occurs in the same harness process.

`evidence.json` is written only on success. Failures create `failure.json` and, when
possible, a private window image; the shell also preserves `run.log` and Xvfb log.
Temporary runtime/preferences are removed on exit. The scope does not establish
live provider access, inference correctness, native desktop entry launch, or an
independent installed-file manifest comparison: the integrator performs those
separate package/update checks.
