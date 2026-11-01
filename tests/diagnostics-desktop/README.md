# Desktop Diagnostics (PUB-03)

Prepared offline tests for the integrator's serial lane:

```sh
dotnet run --project tests/diagnostics-desktop/DiagnosticsDesktop.Tests.csproj
dotnet build src/Lain.Desktop/Lain.Desktop.csproj --no-restore
```

The first command links only the presentation and process bridge, adds no package,
uses fake local executables and touches no installed configuration or provider.
It checks schema allowlisting, known version metadata, copied-report filtering,
size/count bounds, fixed CLI arguments, missing executable, discarded stderr,
output overflow and process cancellation. It does not initialize a GUI.

The view uses the application's existing Avalonia packages and `Ui` palette. A
native GUI check belongs in a private Xvfb session: open the view with an injected
`IDoctorReportSource`, confirm no source call on attach, click Run, inspect finding
code/state/source/next step, copy the report, start a pending fixture and navigate
away to verify its cancellation token is signalled. No GUI or .NET tests were run
at handoff; the shared serial build/test lane remains with the integrator.

Root integration:

```csharp
new Lain.Desktop.Diagnostics.DiagnosticsView(trustedInstalledCliAbsolutePath)
// or fixture:
new Lain.Desktop.Diagnostics.DiagnosticsView(IDoctorReportSource)
```

Place in the existing bounded page/navigation surface (for example `Ui.Viewport`)
and add a Settings/Diagnostics button. The trusted CLI path comes from existing
application installation metadata, with no user-editable URL/command textbox. The
view never starts a diagnosis on attach: only Run invokes `lain doctor --json` or
`lain doctor --target codex-cli|codex-vscode --json`. It invokes no repair, Apply,
restart, login, provider probe or model validation. It reuses Doctor JSON semantics.

Limits: 30-second process budget, 256 KiB stdout, 64 KiB discarded stderr, 64
findings, field limits up to 512 characters, 128 KiB support-copy ceiling. Detach
cancels and invalidates pending view updates. Process cancellation kills its tree.
Only schema 1 and known presentation fields are accepted. Error codes/messages are
fixed; raw stderr/exception text never appear. Copied reports use the same strict
allowlist and remove recognized credentials, URLs and absolute/home paths. This
is a support view for the trusted CLI's safe diagnostic schema, not a general raw
log viewer. Unknown arbitrary secrets in ordinary prose cannot all be detected.
