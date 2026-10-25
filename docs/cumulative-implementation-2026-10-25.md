# Cumulative implementation qualification, 25 October 2026

This records the continuing AC/ST/UXD implementation, separately from the
[real account calls](live-account-qualification-2026-10-25.md). The user confirmed
that both the resent Codex request and its output were visible in Live. Finished
streams are temporary inspector data, not replayed request history. No further
provider inference is needed for this qualification round.

The installed application remains `d7200d7d700bd2d23abf8bf028d40526eb235bf7`.
A read-only status check confirmed desired/applied revision **35/35**, active
profile `lain`, Gateway mode. The cumulative source changes below are **not yet
installed**. Source integration, synthetic qualification, package qualification
and installation remain separate facts.

| Change | Current evidence and remaining gate |
| --- | --- |
| Inline Settings save error, rollback and retry of the original choice | Source `af79d82`; bounded build and 44 checks passed, seven PNGs. Integrator inspected the narrow failure image. Exact-package follow-up remains. |
| Usage table beside selected-group detail at both supported widths | Source `92c4275`, `940d6e1`, compile fix `4ff4339`; 27 native XTEST checks plus 31 central analysis checks passed, eight PNGs. Integrator inspected wide and narrow partial-measurement images. |
| Cohere stateless `store:false` and current exact free-model pin | Source `50ebc9e` and exact Liquid free descriptors integrated. Full prepared-adapter Go suite passed; C# Cohere/Kilo 44 assertions passed. Eight additional real-Core Responses fixtures (27 total provider cases) are prepared; their execution is pending. This is bounded compatibility, not general client/provider certification. |
| Known context rejection and oversized-error diagnostics | Source `768c583`, `631c1b9`, `ba40220` integrated; integrated Go suite passed. The 14-case exact-Core process check is pending. Requests are not truncated. |
| xAI saved-key team discovery and independently verified replacement key | Source `69cd433`, `4dc9160` and production Host wiring integrated. HTTP/CLI and reconnect-race fixtures passed 60 assertions; native action check remains. No billing or quota access is inferred. |
| Persisted login recovery after target removal | Source `1a4cba8`, `d8f404b`, startup/periodic Host wiring and ordinary failed-close retry integrated. Foundation 199, Cloud connections 149 and quota Host 69 assertions passed. |
| Opt-in segmented journal, flat export and recovery | Source `8f78803` and bounded cleanup/backup boundaries integrated. Independent source review found no major segment-core defect. Full Go suite passed: 95 top-level tests, 208 pass events, three packages, zero failures and one optional measurement skip. Upgrade/legacy Python regressions passed 22 tests. Exact-binary cross-boundary collector replay remains mandatory. No user journal is migrated. |
| Native editor traversal and focus return | Initial XTEST run found eight Save/Cancel focus-return failures in Models/Profiles at two widths; other 272 assertions passed. Product fix `0800b0b` and original-trigger locator fix integrated. Focused native rerun of four editor cases is in progress; historical failure evidence is retained. |
| Real MiMo public-console probe | Source `37014f4` prepared. Not yet built/run. A public auth-gate attempt does not qualify a signed-in UID, region or usable subscription quota. |
| Durable design handoff | Commit `a07a33b` includes the exact 16 reviewed documents and 70 synthetic reference PNGs; all inventory hashes checked. See [handoff](ux-reference-handoff-2026-10-25.md). Unrelated dirty provider/planning documents remain separate. |
| Cumulative delivery | Explicit previous-source package pinning integrated. Three updater guard findings are independently closed on source hash `d2ce23cb9468ab2623004a9724c5ad103284d1043ccb7d5e3ec053d749b825c7`. Final file fixtures remain pending. Independent review of the new exact-Core evidence wrapper is finding and correcting report-substitution gaps before use. No candidate package or live update is claimed here. |

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

Builds and native runs use one serialized lane with CPU/memory limits. GUI
automation uses private Xvfb and D-Bus, never the user's window or retained
drafts. The old proxy, its queue, ordinary Codex configuration and existing
account credentials remain outside these tests. Fresh package/Core identities
must be qualified before the [delivery procedure](cumulative-delivery-2026-10-25.md).

This is a progress ledger, not release acceptance. Subsequent results must name
their actual source/binary inputs and preserve the remaining qualification
boundaries instead of inheriting historical passing labels.
