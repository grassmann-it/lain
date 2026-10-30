# First public preview: release priorities and small product ideas

30 October 2026 · Architecture recommendation, **not a release decision or build/publication task**.
This updates the [28 October scope review](release-scope-review-2026-10-28.md)
with the latest client failures and recorder planning. Earlier0.8 selection vs
suggested0.7 stabilization remains unresolved; no version files changed here.

## Recommended public promise

A qualified **Linux-x64 preview** for account/quota inspection, optional local
Gateway and explicitly supported client integrations. Keep Monitor usable without
client patches. Claim only actual provider/method/client support; an account or
catalog entry is not universal inference/quota compatibility. Native Windows/macOS
support and a particular version number require separate decisions/evidence.

The proposed recorder remains an independently deliverable feature: local,
unencrypted, opt-in, all models in enabled scope, conversations/tools/evidenced
workers and skill/run provenance first. Do not make training/export/Memory a
prerequisite for the first public download. This recommendation does not cancel
or postpone the requested next REC work; including an experimental recorder is a
release-scope choice, not an assumption in this review.

## Observed issues and required release outcomes

| Priority | Observed basis | Required outcome / existing work |
| --- | --- | --- |
| P0 client artifacts | Image and context fields omitted by exporter; locally repaired catalogs work, source fixes117 assertions and two native-backend fixtures, running Host exporter still old | Fresh installed Host export -> client startup must preserve image input, known context/auto-compaction and qualified reasoning/tool metadata. No private repair script needed. STAB-00/08 plus image/compaction reports |
| P0 active state | Current source RouterAvailabilityProjector returns[] when desired revision differs from applied | Ordinary pending edits must not clear valid applied quota exclusions; keep current credential retirement/recovery fences. STAB-01, with synthetic reproduction and exact artifact proof |
| P0 errors | Specific VS Code conversation repeatedly recorded400, surfaced only as missing response.completed; exact rejection still unknown | Preserve legitimate error/terminal semantics and provide bounded safe error category/action. No fabricated successful completion, arbitrary history stripping or raw provider-body logs |
| P0 candidate consistency | Buildprops0.8 vs installed components1.0 and mixed dated qualifications; uncommitted integration work | One frozen reviewed candidate, agreed version/support scope, matching component hashes/metadata, fresh install/update/rollback evidence. STAB-00/08 |
| Existing usability commitment | Connect/model adoption and dependency-aware remove already requested; do not sell these as new1.0 ideas | Account -> useful supported outcomes -> chosen models -> Apply/export/actual menu; remove/disable lists references and preserves history. STAB-04/05/11 |
| Existing operational commitment | Source has startup/login/tray/Monitor paths; old installer prose conflicts with newer lifecycle | Exact fresh package cold start, window-closed service, explicit Stop, update preservation and Monitor-without-Core. STAB-02/07/08 |

The observed missing automatic compaction threshold is proven in isolated native
fixtures. It is **not** proof that every recorded400 has the same context cause.
Actual error details and post-repair real request outcome remain separate evidence.
Likewise, historical per-model live tests do not qualify the expanded catalog.
The user's prior every-configured-model qualification requirement remains; a
smaller publicly supported set is a proposed scope decision, not a silent waiver.

## Three small additions with high first-user value

### 1. Account capability/outcome summary

A compact actual-account view: inference access, model discovery, quota reading,
client setup, source/freshness and next usable action. Reuse existing connection
method/provider/capability metadata and STAB-11 rather than inventing another
wizard or provider registry. "Credential saved", "route configured" and "successful
request observed" must remain distinct. Unknown/unsupported quota is neutral;
"Pro required" or "OAuth required" only when the concrete service evidence supports it.
No automatic paid probe to turn a status green.

### 2. UI-accessible diagnosis with a safe copy action

There is already a Doctor engine for reachability, generations, selected model,
effort, overrides and maintenance. Extend the existing checks for qualified input
modalities, context/auto-compaction metadata and exact root/profile/catalog binding;
then expose a bounded copyable report in Help/Settings or the client setup outcome.
Report versions, applied/desired/client generation and safe failure IDs/codes.
No prompts, images, tool bodies, credentials, arbitrary filesystem paths or raw
provider messages in the support copy. A preview of included fields avoids
requesting a full log dump for routine support. Repair shows a concrete diff and
preserves unrelated drafts/credentials; don't silently Apply pending changes.

### 3. Explain the actually selected access

A small last-request detail: requested model/router, actual selected access,
known skipped candidates and evidence-based reasons (disabled, applicable fresh
quota exhausted, source unavailable), freshness and shared-pool context. Reuse
observed attempt/binding data; don't invent a reason from current quota state
which wasn't the state at dispatch. Distinguish actual from hypothetical routing.
This supports user trust and debugging without needing an LLM selector or a
full persistent content recorder. Safe bounded metadata history only.

Optional low-priority idea: an explicit offline sample/demo view with synthetic
quota/model/trace data and a persistent Demo badge. Useful for trying the product
before connecting an account, screenshots and support reproduction. Never mingle
sample budgets with genuine observations. Not a public-release gate.

## A fresh-user acceptance story

1. Install outside the developer machine's cwd with clean private state.
2. Choose monitoring purpose, connect a genuinely supported method and understand
   what can/cannot be read. Monitor may not require editor/client modification.
   Codex standalone monitoring-login limitations must be stated honestly rather
   than inherited from a Gateway-only method label.
3. Optionally enable Gateway, configure a supported route, review the actual
   saved/applied state and prepare a separate client integration.
4. In a fresh supported client session, normal conversation, function tool/result,
   supported image input and normal automatic compaction work through the exact
   package. Unsupported native tools get an actionable explanation.
5. Close/reopen UI; usage remains. Restart/update preserves names, accounts,
   desired/applied differences, history and client settings. Client patch restore
   works on the exact supported version; unknown updates are not blindly patched.

CLI/config fixtures, GUI geometry, source unit tests and actual provider calls
prove different parts of this story. Only claim tested paths. Images and context
metadata need regression tests at export/install/native levels so another export
cannot undo a fix again.

## Release content and ownership

Reuse [STAB work packages](stabilization-work-packages/README.md), especially
01active state,02lifecycle,04/05/11connect/change flow,07resources and08delivery.
Use [REC handoff](recording-work-packages/README.md) for recorder implementation;
this review doesn't start either set. One build lane and private GUI testing remain.

Before publishing, reconcile README/install text with actual defaults, all chosen
component versions/source/notices, optional patched-client version/restore limits,
and actual distribution permissions. Existing Artistic2.0 first-party choice is
not reopened. Secret/publication review and truthful support matrix belong to the
release artifact, not a claim that every local file/report can be uploaded.

No new branding, provider sprawl, node editor, adaptive AI routing, training system
or context/Memory injection is required by this recommendation. Those can be
independently evaluated; a useful preview can stay bounded. Final version, exact
provider/model support promise, and whether recorder belongs in the first public
artifact remain decisions for the user.

## Prepared work packages

[Public-release PUB-00–08](public-release-work-packages/README.md) turns this review
into bounded implementation handoffs and reuses STAB/REC owners. PUB-08 incorporates
the user's explicit later rejection of an automatically inferred free_only worker
gate for openrouter/free. These remain plans, not an execution/publication task.
