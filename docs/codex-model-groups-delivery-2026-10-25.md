# Codex model groups — implementation and qualification

Delivered locally, 25 October 2026. This document distinguishes committed source,
synthetic qualification and the verified installation below.

The user requested groups in the native Codex CLI `/model` menu, followed by
`Automatisch` and the group's individual models. Deployment is restricted to
`codex --profile lain`; the ordinary Codex configuration remains unchanged.
The confirmed automatic rule is the user's model order, with the selected model
and account retained for the conversation. Errors do not select another route.

## Implementation

- Optional revisioned collections reference existing route IDs in user order.
  Provider groups reference the same routes; membership can overlap.
- The user confirmed automatic provider groups and configurable custom groups
  such as `1M`. Multiple active exported accounts of one provider must remain
  distinguishable, for example `Codex (Privat)` and `Codex (Work)`; account
  automatic selection must never include another account implicitly.
- Explicit collection routers are profile-bound. Saving, applying and exporting
  are separate actions. See [routing contract](collection-router-v1.md).
- The grouped catalog is opt-in through
  `lain profiles install-codex HOST_PROFILE_ID --revision N --model-groups`.
  Existing wire aliases and the selected default route remain unchanged.
- The real Codex TUI is patched over the public `rust-v0.164.0` source, commit
  `6aaac694803004a63c6f393d153c96d22521a6b9`. The source archive, patch bundle,
  Rust toolchain and locked external dependencies are pinned. The patch applies
  only to actual CLI profile `lain` plus effective provider `lain`.
- A separate executable and reversible profile dispatcher select the patched
  client only for unambiguous root interactive `--profile lain` invocations.
  Subcommands and ordinary invocations keep the original executable. The
  [dispatcher contract](../clients/codex/dispatcher.md) records its Python
  `argv[0]` boundary and unsupported concurrent foreign updater case.

The patched catalog extension is our client contract, not an upstream feature.
No classifier, paid fallback or change to approval policy is included.

## Qualification

The serial build/runtime lane uses bounded CPU and memory. Build temporaries
are under `~/.cache/lain-build-tmp`; native sessions use private namespaces and
synthetic data, without access to the user's active desktop.

The integrated .NET checks pass: models/collections/router fixtures, 101 profile
application checks, 82 Codex export assertions, 78 transport assertions, 104
applied-profile checks and 351 client/CLI checks. The initial six-suite account
run passed; the three affected suites were rerun after the final account-router
CLI validation and single-account continuity fixes. All six builds complete without
warnings or errors. The CLI fixture has 71 successful PTY/pipe/JSON captures,
including widths from 20 to 200 columns. The updated dispatcher previously
passed 21 offline tests and the source preparation passed four lock tests.

The exact new Core passes 214 Go test events across three packages, 15 synthetic
regression cases including 200 same-alias requests and durable replay/restart,
three real SDK/router HTTP cases, and an actual admission-drain HTTP probe.
No real provider request is part of these checks.

The exact patched Codex passes [seven native PTY cases](../tests/codex-picker/native-acceptance-2026-10-25.md).
Group/member menus, overlapping membership, Escape, visibility and exact model
slug persistence are verified; six out-of-scope/profile/provider cases retain
the upstream picker. The final separate mixed-package run passes nine Host/CLI/Core
checks in a private PID/network namespace, including two Responses turns across
a Host restart plus an independent account-scoped turn, matching Usage identities,
account groups before custom groups, preserved custom order, opt-in export and
stale CAS 409. All three synthetic attempts total 54 tokens.
Desktop remains byte-identical to the baseline. The complete Core library test
build reached the 3 GiB memory limit before executing any of its six targeted
tests. It is not counted as passed. A smaller harness includes the unchanged
production module and its six original tests with real, pinned dependencies;
all six pass without any source changes. Its peak memory is 2.20 GiB, with no
OOM or memory-limit events. Eight TUI library tests have not been started; the seven
real native PTY cases above are the completed menu qualification.

The narrow retained-Desktop update helper passed 15 synthetic file fixtures after
independent source review. Five additional tests exercise real exact-package
file transactions, an argv-reference process guard, a complete stopped synthetic
backup, tamper refusal and actual file-exchange reversal after a write fault.
Thirteen isolated mocked own-operator cases pass. The exact own-service update
plan and subsequent real update pass against the actual installed CLI baseline;
installed acceptance is recorded below.

The follow-up compact `lain usage` implementation now uses adaptive tables and
hides accounts without useful measurements behind `--full`. Single imported
email labels and generic account labels use short provider names in this view;
custom names and multiple accounts remain distinguishable. Empty reset columns
are omitted; mixed reset data uses a dash for missing values. Full/JSON preserve
the original data. No extra TUI package was introduced. Its integrated C# and
PTY checks pass; the installed display was also captured at 80 and 160 columns. Full and
Full-JSON are equal to their corresponding complete data contracts.

Evidence root: `~/.cache/lain-qualification-20261025/model-picker/`.
Original failures are retained beside later successful runs.

## Delivery

Installed Host and CLI are `13519efc6c5961f663695471530b6b0706e856d7`.
The installed Core is `191f4f17e4096558c32ebc5b28ec51341fadc6549f3269300af22800dfe6ce18`,
with its original qualified source `1a43aa7`; Desktop retains the exact `67fb26c`
bytes. Final package manifest:
`3ea1444b49e1cbd0303eadf361020c1dca9ab9fc72ec0a606b0be5c832dea7ea`.
The actual predecessor was the CLI-style package `b0f93369…`, with Host `5f41fba`,
CLI `b49096f` and Desktop `67fb26c`.

The first own-update attempt refused before stopping the service. The supplied
older quota-package baseline (`9bd1daf…`) differed from the installed CLI-style
baseline. Admission was restored and nothing was installed. The original
exception text was not retained; private failure reporting now preserves it.
The corrected Core and mixed packages were staged again from the same qualified
binaries, without rebuilding; five file-transaction cases and nine runtime
checks passed again for the new exact package. The original failed evidence is
retained. The second own-update completed with a full stopped-state snapshot,
preservation checks and no rollback.

Group activation then saved three collections and sixteen explicit routers,
applied the existing `lain` profile once and installed its grouped Codex artifacts
once. Ready revision is **54/54**, with **15 accounts, 58 original routes and
three original profiles preserved**. The menu has fourteen automatic provider
views followed by **Free, IT Sec and 1M**; 57 manual models and sixteen qualified
`Automatisch` entries. There is currently one exported account per provider;
separate account views were qualified using the two-account synthetic fixtures.

The reversible dispatcher is installed at `~/.local/bin/codex`. Only the root
interactive `--profile lain` invocation selects the separately qualified patched
client. Ordinary `codex --version` still executes the original `0.164.0` client.
The installed patched binary matches `5b3a0585…`; its original fallback remains
`b93f6811…`. Use:

```sh
codex --no-daemon --profile lain
```

Then open `/model`. Fixed-range history is unchanged after activation, the normal
Codex/legacy file fingerprints still match, and installed Usage JSON equals
Full-JSON. Evidence: `own-update-current-r1/update.json`,
`group-activation-current-r1/report.json`, `installed-final/report.json`,
`package-runtime-current-r1/` and `mixed-transactions-current-r1.json` below the
private evidence root above. No new real provider inference was sent.

The normal Codex config SHA-256 baseline is
`5d0f52fa3779660e0db053568e3ea84dce070d48abb73e1dfe77491328d429f6`.
That early file changed before this delivery's first installed mutation; its
cause is not established. The current preservation boundary uses SHA-256
`2a21ff22a80155ecda864c3757f9f824be284081c0b5f5b613c0c80fac7e94bb`
and separate opaque fingerprints for the other ordinary Codex files.

Group activation preserves all accounts, routes and profiles.
Free, IT Sec and 1M are collections over their existing route IDs. Automatic
provider/IT Sec targets must first pass the actual Host export compiler. Free
automation is not activated: no current route has effective `free_only`, and
the cached OpenRouter key spend cap is 20 USD; the qualified runtime guard
requires a fresh same-key zero-USD cap. Existing key limits and route policies
are not changed to conceal this prerequisite.

Existing account credentials, history, route IDs, ordinary Codex settings and
the separate legacy proxy are preserved.

## Editing groups

Automatic provider views follow the current profile. Multiple exported accounts
split into stable account views; explicit pools stay separate. Custom collections
share existing route IDs and can overlap. Their stored order determines the menu
order; their route order determines the automatic selection order. There is no
new graphical group editor in this delivery. Read the current revision, then
create or edit a collection through the CLI:

```sh
lain collections --json
lain collections save eigene --name "Eigene Gruppe" --routes "alias-eins,alias-zwei" --revision CURRENT_REVISION
```

Use actual existing route aliases or IDs. Use the **new revision from that save**
for both `lain profiles apply lain --revision NEW_REVISION` and
`lain profiles install-codex lain --revision NEW_REVISION --model-groups`.
Saved group changes never silently replace the normal Codex configuration.

## Desktop cleanup requested after delivery

Removed 133 old clean worktrees through Git's worktree command and 110 known,
ignored `bin`/`obj` build-output directories plus two Python caches. This includes
the generated outputs of the fifteen preserved worktrees; those are deleted,
not retained in the cache alongside the source edits. Every removed checkout's commit is
still referenced; the one detached review was first given an archive branch.
Fifteen worktrees with uncommitted work were moved atomically to
`~/.cache/lain-worktrees/preserved-desktop-20261025/`; their HEAD, index and Git
status were preserved. Uncommitted work was not discarded. The UXD-22 evidence
folder was moved to the private qualification root with its filesystem inventory
and inode preserved. Main repository source, index and HEAD were checked unchanged
during cleanup; other Desktop projects and the installed application were untouched.

The Desktop now contains only `lain` and `lain-referrals.html` among the
Lain-related entries; only the main repository remains registered there.
Observed filesystem free space increased by **1.685 GiB**. The removed paths'
allocated-size estimate was 19.082 GiB; that is not a claim about exclusive
physical space reclaimed. No build directory was moved elsewhere to hide it.
Private evidence: `desktop-cleanup-execution-r1.json`,
`desktop-cleanup-completed-r1.md` (all old/new paths), and
`desktop-preserved-content-manifest-r1.json` (explicitly post-move hashes), and
`desktop-cleanup-build-addendum-r1.json` (source/index unchanged after deleting
the preserved worktrees' generated outputs).
