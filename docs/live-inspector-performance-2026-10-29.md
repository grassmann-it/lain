# Live Inspector responsiveness · 29 October 2026

Status: implemented in the working tree over `9d5fbdc`, built and qualified with synthetic native Desktop tests. **Installed locally on 29 October 2026 after the user requested installation; not published as a release.** See the delivery section below. Host, proxy and configuration were not changed or restarted.

## Reproduced cost and fix

The Inspector recreated its entire event control tree whenever another page arrived. It also eagerly built collapsed event/stream JSON, and serialized all snapshot streams before removing their JSON property. Request relationship resolution repeatedly scanned the snapshot; unchanged sidebar entries received new content controls each second.

- Reconcile event cards by source/request/cursor and update only changed text. Retain existing controls, explicit detail choices and scroll handling.
- Build identity, source, stream and event details only when opened. Release detail controls on collapse. Exclude snapshot streams and event text **before** JSON serialization. Inspect constituent event metadata in pages of 25.
- Index request/response relationships once per snapshot and reuse unchanged sidebar content. Existing evidence/conflict grouping rules still apply.
- Bound long message layout to an explicit 8,192-character reading page, split into at most 4,096-character selectable sections without splitting UTF-16 surrogate pairs. Earlier/Later/Latest actions navigate the retained text. The position label states which characters are visible; the copy action is explicitly named **Copy full retained output**.
- Reading earlier output disables follow; Latest restores the live tail. The existing 500-event / 262,144-character buffer and TTL remain in force. No capture, redaction, Host, Core or durable Usage changes.

A first native stress run after card reuse/lazy metadata still recorded an 8,703ms maximum input heartbeat interval with a growing unbounded text layout. Splitting that entire layout without limiting its total visible size still recorded 7,312ms. These are intermediate implementations, not measurements of the original installed application. The final bounded-page run recorded **363.0ms**. This is a local synthetic measurement, not a general latency guarantee.

## Verification

Serial build, no package restore or real provider calls:

```sh
TMPDIR="$HOME/.cache/lain-verification-tmp" flock -n "$HOME/.cache/lain-verification-lane.lock" \
  dotnet build tests/desktop/Lain.Desktop.Smoke.csproj --no-restore --nologo -m:1 \
  -p:BuildInParallel=false -p:UseSharedCompilation=false -nr:false
LAIN_DESKTOP_SKIP_BUILD=1 TMPDIR="$HOME/.cache/lain-verification-tmp" \
  tests/desktop/run-private-xvfb.sh "$HOME/.cache/lain-inspector-performance-20261029-verified" --inspector-smoke
```

Build: zero errors, nine existing fixture-field warnings. Native test: **123 checks passed**, 12 screenshots at 800×560 and 1120×780, private Xvfb and private D-Bus, synthetic in-memory HTTP transport only.

The stress case delivers 490 deltas (235,200 additional characters), waits for all pages, verifies the exact visible tail and the **complete clipboard text**, earlier/latest paging, retained card identity, absent collapsed detail controls, 25-event metadata paging and an input-priority UI heartbeat. Existing checks cover request switch, conflicting/unproven identity evidence, filters, pause/manual refresh, Clear/cursor monotonicity, gaps, source restart, expired selection, connection loss, recovery from local render failure and cancellation on detach. Screenshots were generated; no independent pixel/visual review is claimed.

Local evidence: `/home/tomka/.cache/lain-inspector-performance-20261029-verified/inspector-evidence.json`. Debug artifacts qualified by this run:

| Artifact | SHA-256 |
| --- | --- |
| `src/Lain.Desktop/Inspector/InspectorView.cs` | `b412d231cb900c968619fa6dce1cebcbbaadb7197050bd9ed9ce68de682ea71e` |
| `src/Lain.Desktop/bin/Debug/net10.0/Lain.Desktop.dll` | `8cc3432989e43c7cb9c0b7c823cc2e32d983823bd9fd94b5c6588addc7253afa` |
| `tests/desktop/bin/Debug/net10.0/Lain.Desktop.Smoke.dll` | `4fee02638196371b57693bcf3a3b05734b34d42627d1e83bdd5b5172f35a1f10` |

## Remaining delivery and scope

The local installation below now supplies this change to ordinary `lain ui`. Do not overwrite a live app tree or mislabel this Debug test as installed acceptance. This change does not complete STAB-03: semantic lifecycle/tool-card simplification, qualified fragmented tool identity, capture-latency work and a virtualized whole-conversation UI remain separate. The stress test covers a long contiguous response; it does not establish responsiveness for every possible distribution of 500 independent cards or real concurrent provider streams.

## Local Desktop delivery · 29 October 2026

Frozen on local detached source commit `7557db08609991b31cf360e98f2905bf433375f5` over `9d5fbdc`, containing only the Inspector and its two fixture/test files. Foreign CLI/documentation WIP was excluded. The source reference is retained locally, not pushed. Self-contained Linux release Desktop keeps the installation's existing `1.0.0` display version and identifies this component with the full commit. This does not settle the separate product-version discussion.

The exact published Desktop DLL passed the same 123 private Xvfb/D-Bus checks through the fixture harness, with a 60.99ms maximum input heartbeat interval in the synthetic 490-delta case. That is a separate run from the earlier 363ms Debug result. Its hash equals the installed DLL:

`ec645986f18bebf29808021ad2ca17dbfe6e2a9e531d86cc433717ac6f3dee32`

Desktop, dependency notices and their component metadata/checksums were swapped with a rollback backup. Host, CLI, Core, launcher and private state were retained. Full installed manifest verification passed; Host PID1649 and Core PID1832 retained their start identities. Desired919/applied917 remained pending; no Apply or service restart occurred. `lain ui` now selects the new payload; no real user GUI was open during replacement.

Receipt: `/home/tomka/.cache/lain-ui-live-delivery-20261029/installation-receipt.json`. Rollback backup: `/home/tomka/.local/share/lain/desktop-backups/live-performance-20261029T225236Z` (previous Desktop/notices plus original metadata). Exact published-binary native evidence: `/home/tomka/.cache/lain-ui-live-delivery-20261029/native-evidence/inspector-evidence.json`. The historical staging helper included the installation ownership marker `.lain-package` in its checksum list; staging was corrected to exclude that marker consistently with the existing strict package verifier, then verified before installation. No verification guard was disabled.
