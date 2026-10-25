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
| Cohere stateless `store:false` and current exact free-model pin | Source `50ebc9e`; `internal/lain` and `internal/freeonly` passed against the pinned patched SDK. This is bounded compatibility, not general client/provider certification. |
| Known context rejection and oversized-error diagnostics | Source `768c583`, `631c1b9`, `ba40220` integrated; integrated Go and exact-Core process checks pending. Requests are not truncated. |
| xAI saved-key team discovery and independently verified replacement key | Source `69cd433`, `4dc9160` and production Host wiring integrated. Actual HTTP/CLI fixtures and native action check pending; no billing or quota access is inferred. |
| Persisted login recovery after target removal | Source `1a4cba8`, `d8f404b` and startup/periodic Host wiring integrated. Foundation, Google and MiMo cleanup fixtures pending. Ordinary failed-close retry is a separate remaining fix. |
| Opt-in segmented journal, flat export and recovery | Source `8f78803` integrated. Independent source review found no major segment-core defect. New tests, cross-boundary collector replay and exact-binary evidence remain mandatory. No user journal is migrated. |
| Native editor traversal and focus return | Dedicated XTEST harness prepared. Initial Profiles run exposed missing focus return after Save/Cancel; this is an open product defect, not a passing gate. Models readiness is being corrected in the harness. |
| Real MiMo public-console probe | Source `37014f4` prepared. Not yet built/run. A public auth-gate attempt does not qualify a signed-in UID, region or usable subscription quota. |
| Durable design handoff | Commit `a07a33b` includes the exact 16 reviewed documents and 70 synthetic reference PNGs; all inventory hashes checked. See [handoff](ux-reference-handoff-2026-10-25.md). Unrelated dirty provider/planning documents remain separate. |
| Cumulative delivery | Explicit previous-source package pinning integrated. Independent review identified updater guards; fixes and final review are in progress. No candidate package or live update is claimed here. |

Builds and native runs use one serialized lane with CPU/memory limits. GUI
automation uses private Xvfb and D-Bus, never the user's window or retained
drafts. The old proxy, its queue, ordinary Codex configuration and existing
account credentials remain outside these tests. Fresh package/Core identities
must be qualified before the [delivery procedure](cumulative-delivery-2026-10-25.md).

This is a progress ledger, not release acceptance. Subsequent results must name
their actual source/binary inputs and preserve the remaining qualification
boundaries instead of inheriting historical passing labels.
