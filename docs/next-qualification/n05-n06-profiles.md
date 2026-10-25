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

## Confirmed review correction

N09 confirmed that the shell snapshot cannot see setup controls after the prepared
review replaces them. A small explicit `CodexSetupDraft` now captures executable,
parent folder, target and groups in the editor rebuild closure. Review latest
resolves the same stable profile ID and rebuilds only those inputs at the current
revision; prepared artifacts, dispatcher proof and install state are discarded.
A removed profile cannot retarget an identically named replacement. The original
prepared controls remain available if rebuilding fails. The focused fixture adds
this regression; native confirmation is owned by N09. This correction was checked
statically (`git diff --check`); no additional build/runtime started during Root's
backend lane.

Three further independent review findings were corrected without a build during
Root's backend lane. Collection membership and group-order drafts now have explicit
models captured by the shell rebuild closure; surviving order is retained across
Review latest, removed groups cannot be recreated, and newly added groups append
with a visible inventory-change explanation. The router enabled toggle is named
for snapshot restoration.

Codexsetup now owns a generation. Cancel, rebuild and changing preparation invalidate
older operations. After every awaited dispatcher/launch check, installation and
clipboard initiation recheck cancellation and generation before effects. Cancel
during an install prerequisite cannot subsequently write files; operations already
completed synchronously before Cancel are not rolled back or described as atomic.

A router referenced by a collection cannot change its group in this first UI. The
locked target names those collections and explains Cancel, explicit removal (which
clears RouterTargetId), then recreation. Unreferenced provider-to-account repair
remains available. The production-control fixture adds all three regressions;
runtime and independent rereview remain pending, with original failure evidence kept.

The N03 pre-execution review also identified FIFO blocking in ordinary file reads.
Dispatcher inputs now use Linux O_NONBLOCK/O_NOFOLLOW/O_CLOEXEC open followed by
statx on that exact descriptor (AT_EMPTY_PATH). Only regular files at most 1 MiB
are read; symlinks, directories, FIFOs and changing lengths fail closed. This does
not rely on File.Exists/attributes to classify a FIFO. Missing libc statx also
fails closed before launching maintenance.

Both the on-disk receipt and ready status reply must identify schema 1, grammar
codex-0.164.0 and the exact supported default entry/state/maintenance paths. The
maintenance and manifest hashes are checked before execution. Custom dispatcher
entries are not configured by this UI and require separate support. Synthetic
regular/symlink/directory/FIFO and malformed receipt fixtures use only runner
output, with a two-second FIFO refusal bound. They are added, not yet executed
under the currently frozen build lane.
