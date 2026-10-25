# UXD-22 source state-flow evidence — 2026-10-25

Selected source state flows are functionally qualified by the separate runs
below. This is not an exact-package, installation, complete accessibility or
blanket native qualification. The evidence distinguishes native input,
programmatic production-command tests, native rendering, and reviewed images.

Chosen presentation: **Draft B** (direct navigation, persistent return/discard)
and **Simple Live A** (direct navigation). The cat/Mint baseline, adjacent
Accounts, Monitor quota table with long bars, fixed Apply review B and persistent
left Live list A remain the reviewed variants. Unchosen alternatives are history.

## Runs (historical results retained)

| Run | Functional result | Assertions | Page images | Meaning |
| --- | --- | --- | --- | --- |
| `native-r1` | 18/24 | 374 | 64 | Initial native state coverage; two fixture assumptions and empty programmatic menu failed |
| `native-r2` | 7/8 | 199 | 24 | Corrected menus/Connect, retained A while B is edited/saved; narrow final Edit locator failed |
| `native-final` | 17/18 | 352 | 46 | Final product assembly, Monitor/Live, Apply metadata and delayed responses; one narrow status fixture navigation failed |
| `native-status-final` | 2/2 | 29 | 2 | Corrected explicit narrow Back before selecting Workspace 3; both sizes pass |
| `native-scale125` | 11/11 | 238 | 34 | Actual `RenderScaling=1.25`; ten OS-input cases plus one programmatic long-label/editor case |

Each case above runs at 1120×780 and 800×560 logical client size except the
inherited long-label focused case, which targets 800×560. At 125%, actual native
window content is 1400×975 or 1000×700 pixels; X11 captures preserve that scale.
The inherited full Barebones/Simple/Advanced × Normal/Compact matrix was not
rerun wholesale. Native draft cases change level and Models density while
preserving inputs and window size; metadata reads actual page preferences.

The latest available result for each of 35 selected case names is PASS across
these separate manifests. This is **not** a single 35/35 run or a claim that all
requirements were exercised with OS input. Initial-snapshot cases use native
rendering and held HTTP, with no fictitious input trace.

## Coverage and its boundary

| Contract | Evidence |
| --- | --- |
| UXD-17 | Native toggle → Profiles → persistent banner/return/focus → level/density changes → rejected stale Save → explicit discard. Native context B cannot replace retained draft A; explicit B save does not delete A. Navigation itself sends no Save/Apply. |
| UXD-18 | Held first response: pending/data/empty/error; native A refresh → B with delayed success/error and stale B values ignored. All known windows, unknown/balance bars and mounted geometry also checked by the owner's production-control fixture. |
| UXD-19 | Native pointer/right-click/Shift+F10/Escape menu parity and filter reset; native retained A/explicit B save has request ID evidence. Captured-command selection/reorder/revision, missing-ID Save and reference locks are **programmatic production-command** checks, not OS-input claims. |
| UXD-20 Connect | Native Monitor/Gateway Core prerequisite review sends no login or operating-mode change. Delayed reconnect results and chooser error cannot overwrite a newer Models draft. |
| UXD-20 Setup | Native invalid executable/parent correction, Prepared without destination files, separate real private installer, unchanged ordinary-Codex sentinel, no Apply. Native Browse requires a new visible X11 window before capture; Escape restores destination focus. Prepared Cancel/target-change are not separate native cases here. |
| UXD-21 | Native fixed review/Cancel/focus, stale diff/explicit refresh, late preview target switch, double-click, response loss, failed/old/incomplete status, confirmed runtime_busy refusal, and delayed status after navigating elsewhere. Shell/list/review and saved/proxy metadata agree after clarification. |
| Monitor / Live | Native table long-bar geometry/all windows/adjacent detail and Escape; persistent left Live list/output, reachable actions/footer, stable scroll controls during refresh/level change, local Pause/Follow/Clear and no mutation/inference. Both sizes and 125% scaling. |

Additional production-control fixtures on the final source: **Accounts 34**,
**Monitor 28**, **Models 18** PASS log lines. Model fixture's earlier visual-tree
readiness failure remains recorded; owner fix `5306991` was rerun separately.
These fixtures do not acquire native-input status from their screenshots.

## Image and request evidence

[state-flow-evidence.json](state-flow-evidence.json) records absolute manifests,
assembly hashes, individual case results, and **27 PNGs personally opened by
UXD-22** with hashes/findings. Other generated images remain unreviewed. The
independent reviewer also reviewed final Monitor/Live/Apply and the delayed
status follow-up. Native root captures include actual popup and picker windows.

Two real product defects were found and fixed: empty programmatic model popup
(`2f51a69`) and stale saved/proxy metadata after status recovery (`77a6134`).
The corrected popup and saved3/proxy3 presentation were personally viewed.
The first Browse capture was too early to show a picker despite a focus change;
it is explicitly insufficient and superseded by the visible-window final case.
A long right-hand quota reset sentence clips in one earlier Gateway detail
capture; no general typography/accessibility pass is inferred from this work.

Final request details include `fixtureSession`, `currentCase`, sequence,
method/path/query/body, configuration revision and active-profile identity.
Startup reads join their case through the unique fixture session. No credentials,
real provider requests, browser login or inference are involved.

## Build/runtime boundary and exact-package handoff

Legacy test locks lack a RID target. An external SSD-cache project preserves the
original assembly name, original absolute Compile includes, original embedded
resource logical name and original desktop ProjectReference. Only its external
lock is generated; repository lockfiles remain unchanged. Builds use linux-x64,
self-contained, locked restore, one MSBuild worker and no shared compilation.
Every build/runtime ran serially under MemoryHigh1536M/MemoryMax2G/Swap256M,
CPU150%, nice10/ionice2:7. GUI uses owned Xvfb/Xauthority, owned session D-Bus,
private HOME/XDG/LAIN_HOME, with no user-display fallback.

The final reusable native harness directory is:
`/home/tomka/.cache/lain-qualification-20261024/uxd22-build/acceptance/bin/Debug/net10.0/linux-x64`.

- Harness SHA-256: `89bff8a48430ec392fd9b8ba2faf20849face29a311b925295981c6e372f301a`
- Source-qualified desktop SHA-256: `689a91279225725bcba4ee9bde43434731408dbfb3c1920ba1d8f44a05a62d3e`

Root may copy the harness, replace its three production DLLs with exact shipped
assemblies, and use `LAIN_TEST_ASSEMBLY` plus `LAIN_DESKTOP_SKIP_BUILD=1` with the
same private runner. Suggested focused filter:
`native-state-draft,native-state-retained-model-command,native-state-connect-late,native-state-status-late,native-state-apply,native-layout`.
That release-assembly result must remain separate from the source runs above.
No broader clipboard, screenreader, real-provider, installed-service or release
qualification is established by this source report.
