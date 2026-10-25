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

## Executed qualification — 25 October 2026

Production source through `4ff4339` (the source handoff plus the Avalonia 12
`TopLevel.GetTopLevel` compile correction) passed the serialized native run:
**27 checks, four PNGs, exit 0**. Evidence is under
`/home/tomka/.cache/lain-qualification-20261025/usage-masterdetail/native-1/`.
`result.json` records the run scope. Both 800×560 densities and 1120×780 were
asserted; native XTest pointer, Space and text input were actually exercised.
All four PNGs were visually inspected. The narrow complete-data PNG also records
the horizontally scrolled core columns; the partial PNG shows group identity and
unknown components beside the table.

The targeted central `--usage-analysis-smoke` runner reuses the existing analysis
cases in the production MainWindow: **31 checks, four PNGs, exit 0**. Evidence is
under `/home/tomka/.cache/lain-qualification-20261025/usage-masterdetail/central-native-final/`.
It checks complete-range and group measurement populations, null identity,
ordering without refetch, selected identity after sorting/resize, exact native
800×560/1120×780 bounds, adjacent panes, applied dimension drilldown, incomplete
coverage, retained-analysis errors and grouping recovery. The screenshot reveal
explicitly scrolls past open filters; the final 800×560 image was visually checked.
These central control actions are routed events; native-input evidence belongs
to the separate fixture above. The full unrelated desktop suite was not run.

Both builds succeeded with no errors. Build/runtime scopes used MemoryHigh 1536M,
MemoryMax 2G, MemorySwapMax 256M, CPUQuota 150%, one MSBuild worker, no shared C#
compiler or node reuse, and SSD-backed TMPDIR. Runtime used owned Xvfb and DBus,
90-second caps, no provider traffic or installed-service changes. All four scopes
were confirmed inactive/dead at slot release. Only this fixture's generated lock
file is included; generated shared-project lock changes were discarded.
