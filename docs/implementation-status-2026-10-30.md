# Implementation, release and recording status

Historical pre-implementation snapshot; superseded by [31 October execution and evidence](recording-execution-2026-10-30.md).

30 October 2026 · Read-only source/runtime reconciliation, no build or deployment.
This ledger concerns the currently inspected working tree and installation;
package descriptions aren't completion evidence and other branches aren't audited.

## Direct answer

The recently prepared **REC-00–08 recording packages are not implemented**. Their
contracts/ownership/acceptance plans exist, but there is no new Recording source
module/store/migration/collector/API/browser in this working tree. Host Program
still registers only existing accounting and transient Inspector observation.
The **PUB-00–08 release set is not collectively completed/accepted**; concrete
partial fixes below don't complete the public-release checklist.

## What exists and what was actually changed

| Area | Evidence/status | Material remaining gap |
| --- | --- | --- |
| Existing product | Accounts, qualified quota readers, model/profile routing, Host/Core, persistent Usage, CLI and Avalonia exist | This is the pre-existing foundation, not new release qualification for every current model/platform |
| Persistent Usage | Existing metadata journal and SQLite accounting path | Not full messages/tools/worker trajectory archive |
| Live Inspector | Existing bounded transient redacted output; performance fix locally installed on frozen7557db0 Desktop,123native checks on exact release DLL | Deliberately excludes prompts/tool arguments/results; no saved conversation browser |
| Images/context/auto-compaction | Exporter source changed and117export assertions; native installed CLI and VS Code backend loopback fixtures; local applied917 catalogs repaired | Running Host835d2e2 exporter not upgraded; re-export from it can regress repaired metadata |
| Free endpoint worker workflow | Source skills/AGENTS + installed global usage skill clarified: ordinary explicit openrouter/free uses configured, no automatic check-free veto | Not a real worker/provider execution acceptance or rebuilt embedded CLI skill |
| Public preview extras | PUB plans cover Doctor/support UI, account outcomes, actual routing explanation, optional demo | No completion inferred from document creation; existing Doctor CLI is real but new checks/UI not accepted |
| Recording | Audits, source/coverage research, current plan and REC handoffs | Observer, durable content store, causal projection, run/skill manifests, UI and qualification still to build |

Source HEAD remains9d5fbdc plus WIP. Latest inspected runtime at20:06UTC:
Host1.0.0+835d2e2, Gateway ready, desired919/applied917. Desired!=applied is a valid
pending-edit state; don't Apply it merely to eliminate a warning. Source still
contains `RouterAvailabilityProjector`'s Revision!=AppliedRevision ->[] branch.
Its active-exclusion behavior is an outstanding PUB-02/STAB-01 correctness gate.
Version choice0.7 proposal vs prior0.8 and component1.0 metadata remains unresolved.

## Minimum release work

1. Integrate/freeze/deliver image/context fixes in the actual candidate Host, then
   test fresh export/install/native-client use and re-export; no private repair
   scripts required. Preserve independent component provenance and user config.
2. Fix applied-runtime/quota fence behavior under ordinary pending edits; preserve
   current retirement/recovery gates and unknown/stale/reset semantics.
3. Make errors actionable via bounded safe source evidence. Current reproducible
   VS Code400 -> missing-completion symptom doesn't retain exact upstream cause;
   auto-compaction defect is proven, sole400 causation isn't. No raw-body logging
   or fake completed events as a workaround.
4. Finish/requalify existing connect/model/removal/lifecycle flows and actual
   support promise on frozen packages; don't rebuild completed foundations.
5. Exact fresh install, update/preservation/recovery, native UI/client, bounded
   resource and provider/model support evidence; source/About/package/version/
   README alignment and public artifact/secret/notice review before publication.

Doctor UI/support copy and account outcomes improve usability; routing explanation
is proposed; offline demo is optional. Their inclusion doesn't replace correctness
gates. Recorder inclusion in the first public artifact remains a scope decision.
The user's prior per-configured-model qualification requirement isn't waived.

## Minimum useful recording work

[REC-00 contracts](recording-work-packages/00-contracts.md) -> policy/redaction,
Core observation, protocol normalization and durable store in parallel -> causal
index/run variants/paged queries -> shared API/CLI/saved conversation UI -> failure/
privacy/resource/native acceptance. File owners and wave ordering already exist;
no additional planning layer is needed to start when implementation is requested.

Current accepted design: local unencrypted private files, explicit opt-in, all
models in enabled scope, no training-permission veto on ordinary local inspection,
secret exclusion before retention, bounded count/bytes/disk, compression/recovery,
real gaps and distinct request/attempt/tool/worker identities. Skill revision can
be configured/reported/observed; don't invent loaded/compliant skill proof.

Recording must outlive the UI, keep related available content across restart and
make incomplete/missing/unobserved local execution explicit. Existing error files
are not an approved archive and cannot substitute for this work. No recorder
capture, real transcript replay, evaluation, export or training is enabled.

## Follow existing packages

- [Public release handoff](public-release-work-packages/README.md).
- [Recording handoff](recording-work-packages/README.md).
- [Stabilization foundation](stabilization-work-packages/README.md).
- [Image repair scope](codex-image-input-fix-2026-10-30.md).
- [Compaction repair scope](codex-auto-compaction-fix-2026-10-30.md).
- [Worker/free workflow scope](openrouter-free-workflow-fix-2026-10-30.md).
