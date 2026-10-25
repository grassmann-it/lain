# UXD-13 Usage table and side detail

Implementation basis: `e48bc11`. Production changes are limited to
`UsageView.cs` and `UsageView.Analysis.cs`. The main desktop fixture changes only
its obsolete analysis-expander assertions, retaining the measurement and identity
checks. No host, proxy, collection or aggregation contract changes.

The permanent adjacent panes contain the single server-aggregate table on the
left and selected group detail plus explicitly scoped supplementary measurements
on the right. Both panes scroll independently. At 800×560 the table retains all
columns through horizontal scrolling. More filters can use the outer page scroll.
Selection uses dimension plus nullable group identity; sorting/polling do not
change it. If a selected group disappears, its detail states that explicitly.
Back captures selection and both pane offsets separately from applied query and
unsubmitted filter controls. Known subtotal/null/freshness calculations are not
changed.

`Lain.Desktop.Usage.Tests.csproj` links the production Desktop and read-only shared
fixture/native-input helpers. It exercises 1120×780 and 800×560, both densities,
XTest pointer/Space selection, native draft typing during polling, horizontal
column access, independent scrolling and navigation capture/restore, a 64,200
known-token partial fixture with Kimi unknown, stale analysis, disappearing group,
and source cursor regression. Global first-output remains 240/510 ms, 120/132.
The test app mounts the production UsageView with production styles in a native
window; shell navigation is covered by the existing central desktop fixture.

Build only in the coordinator-granted slot, serialized with bounded resources:

```sh
dotnet build tests/desktop-usage/Lain.Desktop.Usage.Tests.csproj --nologo -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -nr:false
bash tests/desktop-usage/run-private-xvfb.sh /absolute/private/evidence/directory
```

The runner never builds. It creates private Xvfb and DBus instances and rejects
user-display inheritance through the linked NativeInput guards. It uses only
in-memory HTTP; no actual host, credentials, providers, services or installation.
A 90-second timeout bounds the native run. `LAIN_TEST_ASSEMBLY` can override the
built test DLL location when the coordinator uses a runtime-specific output path.

Status at source handoff: **not run**; build/runtime slot still pending. PNGs and
`result.json` are created by the actual run, not committed or claimed in advance.
