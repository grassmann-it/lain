# Independent source review, 24 October 2026

Baseline: `7a58b01`; reviewed evolving implementation worktrees, not merely
worker reports. The integrator owns final source freeze and binary qualification.
Root executed the independently authored checks in its serialized integration
slot; the reviewer independently inspected their outputs. Source conclusions and
the bounded checks below do not establish AC-10 completion.

| Finding and trigger | Impact | Follow-up |
| --- | --- | --- |
| Requesty `/self` fails for a resource-bound account, `/usage` succeeds | Unverified usage attached to a known resource | Reader fix `1ac7bbc`; owner reports 52 checks passing; independent scenario prepared |
| Manual reconnect omits old verified subject/resource | New key inherits old verified identity | Foundation added strict prepared identity/provenance checks; independent scenario prepared |
| `ReplaceApiCredential` inherits verified binding directly | Same identity bypass through legacy replacement API | Foundation rejects unverified replacement of verified identity; independent scenario prepared |
| Inference replacement always republishes both inference and quota | Separate monitor credential silently disappears | Foundation preserves separately referenced quota handle; independent scenario prepared |
| Process dies after immutable generation write, before recovery pointer | Durable once-only credential cannot be adopted | Foundation reconciles operation-bound generation files; independent scenario prepared |
| New metadata generation exists but prepared recovery pointer still names first checkpoint | Durable verified generation ignored on recovery | Foundation reconciling newest unpublished same-ID generation; independent scenario prepared |
| Missing explicit purpose handle falls back to inference/legacy storage | Wrong credential satisfies explicitly bound purpose | Foundation failing closed for absent explicit references; independent scenario prepared |
| Quota-only Core credential resolves via inference selection | Valid quota capability fails without inference grant | Foundation projects the selected ownership descriptor; independent scenario prepared |
| Successful Codex operations never release adapter dictionary entries | Adapter references grow despite service terminal cap | Explicit terminal ReleaseAsync added without grant revoke |
| Fresh Kilo wallet plus failed Pass read retains old Pass under fresh timestamps | Stale metadata presented as current | Safe narrow fix drops unavailable metadata; metadata-specific last-success retention remains a separate gap |
| OpenRouter key is received, then optional metadata network read precedes private persistence | Long crash window loses already received once-only key | Shared immediate checkpoint added before metadata network request; final metadata supersedes same credential generation |
| Final generation write fails after replacing the in-memory prepared payload | Retry treats old handle as if it contained the newer result | Reported to foundation; distinguish pending payload from last durable payload |
| Drain timeout followed by failed admission-resume request | Old Core may remain draining while prior completed/ready status is restored | Reported to integrator; degraded/recovery state must expose an unconfirmed resume without killing streams |
| Legacy runtime and active-profile explicitly name different profiles at same revision | Legacy fallback accepts contradictory files | Reported to application owner; unknown legacy identity must remain distinct from affirmative mismatch |

Main integration follow-up additionally caught new Codex credential kind versus
legacy quota-adapter guards, malformed optional Poe constructor validation outside
the boot catch, Monitor inference-export gating, provider-wide Step Plan quota
claims, and cloud discovery provenance not recognized by the central verified
identity guard. Root addressed all five in evolving source; an independent
cloud-discovery-to-manual-reconnect scenario protects the last invariant.

Integrated evidence inspected on 24 October: `followup-results.json` records exit
code zero for all six suites. `implementation-independent.log` reports 12/12 and
`monitor-host.log` reports the isolated restored Monitor probe passed with zero
provider requests. Both identify exact Host SHA256
`4d4df715d74ba413f633243be47ab4950aee8908221ed2eb0992ccfc73896bc1`.
The other inspected outputs report 117 foundation assertions, 63 provider
transport assertions, 56 public-login checks and the models checks passed.
Root ran these tests; the reviewer authored the independent cases and inspected
the output. No result applies automatically to a later Host artifact.

The integrated cases now exercise pending-versus-durable retry, contradictory
legacy identity rejection, Codex quota projection, cloud-discovered identity
protection and literal historical journal replay as well as the earlier identity
and credential invariants. Drain recovery handling was source reviewed; that
source review is not an independently executed crash/interleaving test.

An additional recovery blocker remains: after restore quarantines owned grants,
saved Gateway runtime files still reference the removed grants. Codex reconnect
requires Core management, but starting that saved runtime fails before login.
Root is implementing a separate empty login runtime. Acceptance requires separate
config/bindings/journal files, explicit management-only readiness, unchanged active
profile/application proof, no replacement of a live draining child, and normal
transactional Apply before inference readiness. A synthetic exact-artifact restored
Gateway check is still outstanding; no live OAuth is authorized for it.

Additional source checks found no concrete blocker in bounded loopback callback
parsing/state validation, profile transaction marker checks, partial token SQL
aggregation, or Core original/final tool-validation placement. This is not a
claim that all runtime interleavings or provider behavior were tested.

Final integrated checks must still cover Monitor startup/package without Core,
restored owned-auth quarantine before Core refresh can run, actual Core dispatch
against a local fake provider, parent-death/drain behavior, crash/restart gates,
exact legacy canonical replay, isolated install/update/rollback, and CLI/Desktop
projection consistency. Unimplemented provider methods and unavailable external
registrations must stay explicitly distinguished from shipped, qualified methods.
