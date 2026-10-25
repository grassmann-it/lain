# Cumulative implementation qualification, 25 October 2026

This records the continuing AC/ST/UXD implementation, separately from the
[real account calls](live-account-qualification-2026-10-25.md). The user confirmed
that both the resent Codex request and its output were visible in Live. Finished
streams are temporary inspector data, not replayed request history. No further
provider inference is needed for this qualification round.

The installed application is now **`5f41fba5324e1cf1e58e2229d6c35c90ba800846`**,
Gateway mode, ready, desired/applied revision **35/35**, active profile `lain`.
The previous installed source was `d7200d7d700bd2d23abf8bf028d40526eb235bf7`.
A separate [read-only reconciliation](../tests/delivery-stabilization/evidence/cumulative-20261025/reconciliation.json)
confirmed all 15 accounts, 58 model entries, fixed-range History, selected
credentials, saved runtime/journal files and all three Codex file fingerprints
unchanged. The original updater failure is retained; see the operational boundary
below. Source, synthetic qualification and installation remain separate facts.

| Change | Current evidence and remaining gate |
| --- | --- |
| Inline Settings save error, rollback and retry of the original choice | Source `af79d82`; bounded build and 44 checks passed, seven PNGs. Integrator inspected the narrow failure image. The exact-package Settings overlay also passed 44 checks. |
| Usage table beside selected-group detail at both supported widths | Source `92c4275`, `940d6e1`, compile fix `4ff4339`; 27 native XTEST checks plus 31 central analysis checks passed, eight PNGs. The exact-package overlays also passed 58 checks; integrator inspected the narrow side-by-side image. |
| Cohere stateless `store:false` and current exact free-model pin | Source `50ebc9e` and exact Liquid free descriptors integrated. Full prepared-adapter Go suite, 44 C# assertions and all 27 actual-Core Cohere/Kilo cases passed, including eight Responses cases. This is bounded synthetic compatibility, not general client/provider certification. |
| Known context rejection and oversized-error diagnostics | Source `768c583`, `631c1b9`, `ba40220` integrated. Full Go suite and all 14 exact-Core process cases passed. Requests are not truncated. |
| xAI saved-key team discovery and independently verified replacement key | Source `69cd433`, `4dc9160` and production Host wiring integrated. HTTP/CLI and reconnect-race fixtures passed 60 assertions; the exact-package native action passed 15 checks. No billing or quota access is inferred. |
| Persisted login recovery after target removal | Source `1a4cba8`, `d8f404b`, startup/periodic Host wiring and ordinary failed-close retry integrated. Foundation 199, Cloud connections 149 and quota Host 69 assertions passed. |
| Opt-in segmented journal, flat export and recovery | Source `8f78803` and bounded cleanup/backup boundaries integrated. Full Go suite passed: 95 top-level tests, 208 pass events, three packages, zero failures and one optional measurement skip; 22 Upgrade/legacy Python regressions passed. All 12 exact-Core archive checks passed with actual Host collector/SQLite, rotation, crash/replay, matching synthetic whole-tree restore and flat export. Old binary execution on the archive and full Host lifecycle remain explicitly outside this gate. No user journal is migrated. |
| Native editor traversal and focus return | Initial XTEST run found eight Save/Cancel focus failures. The four-case rerun on `ef507a7` passed 282 assertions, closing all eight functional failures. Source `7d6d425` additionally requests visible keyboard focus; the exact-package native run passed 282 checks, and integrator inspected all four Saved images plus two native X11 captures, confirming visible focus outlines. Historical failure evidence is retained. |
| Real MiMo public-console probe | Reference-only build passed; production `OpenAsync` rejected before completed browser opening, reads or screenshots. Owned profile/process cleanup passed. Cause remains unknown; [recorded evidence](../tests/mimo-real-probe/public-probe-evidence.json) does not qualify login, identity or quota. |
| Durable design handoff | Commit `a07a33b` includes the exact 16 reviewed documents and 70 synthetic reference PNGs; all inventory hashes checked. See [handoff](ux-reference-handoff-2026-10-25.md). Unrelated dirty provider/planning documents remain separate. |
| Cumulative delivery | Explicit previous-source package pinning integrated. Three updater guard findings are independently closed on source hash `d2ce23cb9468ab2623004a9724c5ad103284d1043ccb7d5e3ec053d749b825c7`; nine file tests passed. Three exact-Core wrapper findings are independently closed on wrapper hash `c4464be30e465573f444e62538b4776ae8c1efd46b67cba0ca74f2db124127e4`; 31 fixtures passed. The final package pair and full four-package runtime gate passed. Installed state passed separate read-only reconciliation; the GET-only installed-UI follow-up also passed 100 checks. |

The bounded C# batch completed seven suites with **780 assertions** (Foundation,
Cloud connections, quota Host, xAI, provider policy, Inspector policy and
Cohere/Kilo) and **22/22 SQLite Usage tests**. Host/Core exporter was built, not
executed. Production source equals `65fba19`; subsequent changes in that worker
are isolated fixture corrections and a test dependency lock. Production locks
are unchanged. xAI has two nullable warnings in its fixture; other successful
builds have none. Exact commands, per-suite source pins, DLL/log hashes and
preserved failed attempts are in the private evidence index
`~/.cache/lain-qualification-20261025/foundation-csharp-65fba19-summary.json`,
SHA-256 `a3611ac446677a87c1f08540881875c7da853c73478776e58ce23d0a864c1718`.
This batch does not replace the remaining exact-Core and package gates.

The fresh Core is SHA-256
`e05d21027a9aa0c543430db8bee6fd8c34d5a9fff78ea81f80953e8f29cf485e`,
with source-manifest SHA-256
`9c6bed3c8be2af1c3f98942b8402e7b49d6cbaae2ea96cde8132e1b3046739b1`.
[Unmodified synthetic reports](../tests/core-stabilization/evidence/cumulative-20261025/run-index.json)
record **8 Upgrade, 6 lifecycle/dispatch, 27 provider, 14 context and 12 archive
checks**. The original failed runs are retained privately: two replay fixtures
omitted the existing nullable `cacheCreation` field, and cancellation polling
observed usage before its separate terminal fact. The narrowly corrected probes
passed without changing Core bytes, counts, identities or unknown measurements.
Final package publication must reproduce the exact qualified Core bytes.

The first cumulative package pair, source `ae79589`, built successfully and
reproduced that Core. The strict four-package runtime gate then rejected a new
empty `credentials/recovery` directory created by passive startup scanning.
[The isolated diagnostic](../tests/delivery-stabilization/startup-recovery-delta-20261025.json)
confirms this as the only path difference and `databaseChanged: false`.
These packages have **not** qualified for installation. Product fix `b7cbe22`
keeps missing recovery/removal paths absent during reads, preserves link/path
validation and creates parents only on actual writes. Independent source review
found no major defect. The focused rerun from `c8f84b6` passed **210 assertions**
with zero build warnings/errors; its only follow-up change unlinks synthetic
dangling paths correctly during fixture cleanup. [The retained summary](../tests/delivery-stabilization/startup-recovery-foundation-20261025.json)
records exact assembly/log hashes and actual CPU/memory limits. Fresh `5f41fba` Monitor/Gateway packages and the complete unchanged four-package
runtime gate subsequently passed. The Core is unchanged, and publication
reproduced its exact qualified bytes.

The final delivery evidence now includes all **506 exact-package UI checks**
(27 Usage native, 31 Usage analysis, 44 Settings, 15 xAI action, 107 Inspector,
282 native editor checks). [The case index](../tests/delivery-stabilization/evidence/cumulative-20261025/ui-index.json)
preserves the first Inspector operator failure: the historical manifest expected
106 while source `4c6dc9d` had replaced one empty-state assertion with two precise
retention assertions. Only that expected count was corrected in a separate input
manifest; the Inspector and pending native cases passed in a new run. Earlier
passing cases and the failed report were not rewritten. The separate
[manual review](../tests/delivery-stabilization/evidence/cumulative-20261025/ui-manual-review.json)
confirms the saved focus outlines. A targeted final-Monitor
[full stopped restore](../tests/delivery-stabilization/evidence/cumulative-20261025/restore-report.json)
also passed with history/identity retained, a rotating grant quarantined and a
static account retained.

The live updater did **not** complete cleanly. Its first attempt refused omitted
empty systemd lifecycle properties before stopping anything. The private helper
now requires an authoritative exact typed empty D-Bus array for each omitted
property; existing nonempty hooks remain refused. Nine new fixture tests plus
all nine earlier updater guard tests passed on helper SHA-256
`5fd2a474b46a4db67f64c0d05594cba4c53873908c2c2ae83fff4edb60eac7a6`.
The second attempt created a full stopped backup and installed the exact package,
then reported an unreadable own-process audit failure; cleanup could not confirm
service stop. The new Host remained available but initially reported Core startup
failure. A controlled restart produced [local health 503 then 200](../tests/delivery-stabilization/evidence/cumulative-20261025/core-start-health.json)
with a healthy journal, and the Host returned ready at revision 35/35. The cause
of that initial startup/audit failure remains unknown. No inference was sent,
configuration applied or old state/grants restored during this recovery.

The original failed update records remain private and unchanged. A separate
bounded read-only reconciliation, holding the same updater lease, then verified
both current CLI captures against the original fixed-range baseline, all four
package pins, installed new/previous application bytes, full stopped-snapshot
protected-file hashes, current selected credentials and ordinary/separate Codex
file fingerprints. Its PASS qualifies the resulting installed state, not an
uninterrupted updater run or a general startup/drain guarantee. Full stopped
backup and earlier application copies remain private. The subsequent
[installed-UI run](../tests/delivery-stabilization/evidence/cumulative-20261025/installed-ui-summary.json)
passed **100 checks**, captured eight PNGs and made 23 allowlisted local GETs with
zero blocked requests. Host readiness, revisions and historical data were
unchanged across closing and reopening that private test window. Integrator
inspected the six distinct images; reopened Accounts images were byte-identical.
No real user window or preferences were changed.

Builds and native runs use one serialized lane with CPU/memory limits. GUI
automation uses private Xvfb and D-Bus, never the user's window or retained
drafts. The old proxy, its queue, ordinary Codex configuration and existing
account credentials remain outside these tests. Exact package/Core identities
and the resulting installation are now qualified in the bounded evidence above;
the [delivery procedure](cumulative-delivery-2026-10-25.md) remains distinct from
the failed live-updater execution and its separate reconciliation.

Cleanup removed 11 superseded package archives/directories and disposable UI
overlays, freeing **1.74 GiB** without moving files. Final packages, baseline
packages, stopped backups, current app/app.previous, original harnesses, logs,
reports and images remain. [Cleanup inventory](../tests/delivery-stabilization/evidence/cumulative-20261025/cleanup.json).

Provider qualification remains method-specific. In the real installed view,
Cloudflare monitoring still reports a GraphQL dataset/permission problem; the
existing check does not distinguish those causes. MiMo public browser opening
remains unqualified as described above. These are not successful quota reads.
No additional real inference was needed after the confirmed visible Codex probe.

This records a bounded installed delivery, not public release acceptance. Subsequent results must name
their actual source/binary inputs and preserve the remaining qualification
boundaries instead of inheriting historical passing labels.
