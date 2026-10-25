# N05/N06 Profiles implementation

Source built on `7661886`, with N06 shared contract `d5b9a2d` applied as a dependency.
No installed application, real Host, provider access or ordinary Codex configuration was changed.

## Result

Profiles offers explicit grouped Codex export and two destinations: a new private
Codex folder, or separate lain profile files in `~/.codex`. Prepare exports without
writing files. Install uses that exact export generation/revision; known configuration
changes invalidate it. Host revision checking and local installation are separate,
not an atomic cross-process transaction. No Dispatcher installation/repair is performed.

Grouped/shared setup uses the N08 read-only maintenance status and pins its receipt
through Prepare, Install and Copy. Before executing the installed checker, Desktop
checks regular paths and receipt hashes for maintenance.py and manifest.json.
A legacy/missing/changed checker requires explicit maintenance. Launch commands use
the checked absolute entry and an explicit CODEX_HOME, avoiding unrelated PATH entries.
Upstream Codex 0.164 loads separate lain.config.toml for actual --profile lain.

Custom groups are edited under Profiles. Their global effect is explicit. Memberships
reuse route IDs and may overlap. Global collection order and member order have ordinary
buttons sharing commands with context menus/keyboard. Group deletion lists router
references across profiles. Automatic targets retain stable IDs and cost policies;
an ambiguous provider requires an explicit account/group selection. There is no new
visibility or cost policy, no inference retry and no implicit Apply/export/install.

## Verification

Desktop and desktop-smoke projects compile in serial cgroups with MemoryHigh 1536 MiB,
MemoryMax 2 GiB, MemorySwapMax 256 MiB and CPUQuota 150%; actual limits were read back.
Build r1 identified two extraction issues; they were corrected and original logs retained.
Desktop r2: zero warnings/errors. Final smoke build r2: zero errors, eleven existing
CS0649 fixture warnings. No generated lockfile edits retained.

`tests/desktop/N05N06ProfilesCases.cs` exercises production controls and an in-memory
Host with injected dispatcher readiness. It covers explicit group flag, dateifree
Prepare/Cancel/change, revision/receipt invalidation, separate private installation,
overlapping memberships, both orders, account repair and retained stale drafts.
It is a routed-event fixture, not native pointer/keyboard or real client acceptance.
Entrypoint is owned by N09 (`--n05n06-profiles-smoke`); no duplicate Program.cs edit.

Runtime result is pending the private Xvfb lane. Real Dispatcher/native /model and
integrated mode/scale acceptance remain N08/N09 gates, not inferred from compilation.
Private logs: `~/.cache/lain-qualification-20261025/next-n05-n06/`.
