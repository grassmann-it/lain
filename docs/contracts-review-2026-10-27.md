# Contract review: practical feature evaluation

27 October 2026. Read-only review at source HEAD `835d2e2` plus existing WIP.
The register, template and Live contract are uncommitted documents in this
working tree. Existing foreign WIP was preserved. No runtime, GUI, provider,
installation or full test suite was exercised. This report is a proposal for
improving contracts, not acceptance of the implementations discussed below.

## Assessment

The [register](feature-contracts.md) and [template](contracts/feature-template.md)
are a useful, proportionate foundation. They correctly distinguish accepted
outcomes, proposed policy, implementation and evidence; cover user workflows
rather than only DTOs; and explicitly exempt trivial changes from paperwork.
Keep this approach. The main weakness is that the register points into long,
dated documents containing both historical and current behavior. A reviewer can
find relevant material but cannot always identify the effective requirement
without reconstructing several amendments.

A contract review should produce a short list of mismatches and untested claims,
not a blanket “contract compliant” verdict. Code/schema links establish structure,
not entitlement, installed compatibility or a successful user workflow.

## Findings and priorities

### P1 — Make the effective requirement identifiable

[Routing](collection-router-v1.md) includes installed 25 October behavior, a
26 October source-tested/pending-deployment extension, and earlier exclusions.
The document explains supersession, but a top-level “current status” sentence
can still be read as acceptance of the whole later extension. The
[startup plan](startup-and-autostart-work-packages.md) still says implementation
has not been commissioned and describes missing source capabilities, while
Lifecycle source now exists. The register warns about this startup discrepancy,
but the authoritative entry itself remains stale.

Recommendation: retain history, add a short effective-scope table at the top of
these two documents: requirement/amendment, source or decision date, implemented
reference, last relevant evidence, remaining gaps. Only changed or ambiguous
contracts need it. Give important acceptance cases stable local IDs; the existing
FC handles identify areas, not individual pass/fail requirements. A contract
revision and an implementation revision remain separate.

**Acceptance:** a fresh reader can identify whether quota fallback, auto-sync and
login startup are accepted, implemented, installed and tested without treating
an old installation result as proof of a new amendment. Unresolved auto-Apply
policy stays unresolved; documentation cleanup does not decide it.

### P1 — Specify the boundary between saved, runtime and client state

The shared contract distinguishes desired/applied state. However, “failed Core
application does not successfully save an active route” is ambiguous about whether
the desired edit may already be saved. The current exporter resolves requested
saved configuration and compiles export proof; it does not require that revision
to be the active runtime:
[ModelEndpoints.cs:35](../src/Lain.Host/Models/ModelEndpoints.cs#L35),
[CodexProfileAdapter.cs:19](../src/Lain.Host/Models/CodexProfileAdapter.cs#L19).
That is appropriate for preview/export, but insufficient evidence that a generated
menu entry is presently executable.

Recommendation: define which operation is preview, preparation, activation or
client publication; record the generation tying runtime and published menu together.
Expose partial delivery and recovery rather than promising one atomic transaction
across the Host, Core and client files. Automatic publication still requires the
pending activation-policy decision. Do not auto-apply unrelated saved edits.

**Acceptance:** with desired B/runtime A, preparation of B is labeled as such;
failed activation never labels B available; a failure between runtime activation
and menu publication is distinguishable and recoverable; a client that only reads
its catalog on startup reports that limitation. Check both grouped and ordinary
exports, not just router proof generation.

### P1 — Protect active quota behavior from unrelated drafts

[RouterAvailabilityProjector.cs:23](../src/Lain.Host/Quota/RouterAvailabilityProjector.cs#L23)
returns no exclusions when desired and applied revisions differ.
[RouterAvailabilityPublisher.cs:51](../src/Lain.Host/Quota/RouterAvailabilityPublisher.cs#L51)
then publishes an empty replacement. This is a source finding, not a new runtime
reproduction. The current routing contract does not clearly specify this
pending-edit behavior. A valid user workflow can therefore change active
quota exclusion behavior before Apply.

Recommendation: state an active-runtime invariant and evaluate availability
against a proven applied snapshot, matching bindings and credential generations.
A new desired credential removal or recovery fence must still prevent unsafe
use; simply ignoring desired state wholesale is not sufficient.

**Acceptance:** unrelated draft edits leave the same valid applied exclusion
until its original freshness/reset expiry; changing credentials or entering
recovery cannot make a retired credential eligible; applying a new generation
rejects old delayed availability messages. Other providers remain unknown until
their source/account/transport relation is separately qualified.

### P1 — Turn removal intent into an operation contract

[ModelsService.cs:79](../src/Lain.Host/Models/ModelsService.cs#L79) rejects profile
and fallback references; [AccountService.cs:459](../src/Lain.Host/Accounts/AccountService.cs#L459)
rejects any route reference. Some collection cleanup already exists in
[ConfigurationRepository.cs:37](../src/Lain.Host/Operations/ConfigurationRepository.cs#L37),
and deleting a router clears collection targets. Do not duplicate or bypass this
existing behavior in several GUI calls.

Recommendation: one revision-bound impact plan and mutation for dependent saved
changes, with separate runtime/credential completion. Enumerate shared pools,
empty groups/routers, profile defaults, continuation pins and durable credential
removal fences. Deleting desired configuration is not proof that an old active
runtime has stopped using it. Replacement/empty-profile policy must be selected;
current `PutProfile` rejects an empty route list.

**Acceptance:** reviewed removal resolves every listed reference or changes
nothing on a stale revision; history identity survives; a default is never
silently replaced by an unapproved paid route; failures expose the effective
runtime and credential state; refresh cannot recreate deliberately removed access.

### P2 — Provider descriptions need a scoped refresh contract

[ProviderContracts.cs](../src/Lain.Core/ProviderContracts.cs) already describes
connections, plans, sources and nullable capabilities. `Sources.CheckedAt` and
syntax validation are not a complete discovery or evidence contract. In
particular, “model listed”, “account permitted”, “tools advertised” and “tested
with this client” are different claims. `AttachModels` selects catalog templates;
it does not establish recurring authoritative discovery.

Recommendation: extend this model rather than create a second provider catalog.
Specify provider/connection/product/region scope, source kind and observation date,
full/partial coverage, pagination/cache behavior, stable identity and alias changes,
manual overrides/suppressions, entitlement uncertainty and bounded errors. Keep
research findings separate from executable adapters. A rejected credential must
not be sent to a newly researched URL merely because a descriptor mentions it.

**Acceptance:** ten refreshes are idempotent; page failure causes no deletion;
model removal is marked only after a qualified complete observation; custom names
and suppressions persist; alias rotation does not silently change the underlying
model/product; account entitlement remains unknown when the source cannot prove it.

### P2 — Define the 429 population before adding adaptive control

Existing attempt DTOs expose timestamps/status and performance measurements, but
[AttemptMeasurementsDto](../src/Lain.Core/ObservabilityContracts.cs#L17) has no
explicit concurrency-at-dispatch field. This selected DTO review does not prove
that all lower-level observations are absent; inventory the transport hooks first.

Recommendation: define what is counted (actual upstream attempt, not worker/tab),
account/model/shared-pool scope, slot entry/exit across streamed response lifetime,
retries, cancellations, source generation and uncertainty after crashes. Define
both numerator and denominator: completed observed attempts in a dispatch-cohort
bucket with unknown/inflight/canceled counts reported separately. Reconstructed
historical overlap must be labeled as an estimate, not backfilled exact telemetry.

**Acceptance:** a stream holds its slot until completion/cancel; queued work does
not inflate upstream parallelism; retries are separate attempts; “429 at one”
and “429 at eight” show sample counts, comparable window/scope and coverage;
external usage remains unknown. A generic429 does not prove subscription
exhaustion or an official concurrency limit. Adaptive thresholds remain a later
policy decision rather than an inferred requirement of telemetry.

### P2 — Make AI assistance evaluable without expanding authority

The current local research proposal usefully separates discovery from activation.
Carry its necessary rules into the public feature contract before implementation:
allowed input data and sources, output schema with evidence links and diff,
classification of unverified statements, execution budget/timeout, review boundary,
and behavior when sources conflict or disappear. No chosen provider/model or
permission to send private account/prompt data should be inferred from “AI fallback”.

**Acceptance:** unavailable AI leaves the last verified adapter functional;
unsupported claims stay proposals; malicious instructions in fetched documents
cannot trigger token export, code execution or activation; no new parser/URL is
trusted solely from a model answer. Public research and authenticated discovery
are separate workflows.

## Bounded work packages

| Package | Deliverable | Ready / dependency |
| --- | --- | --- |
| CR1: Contract orientation | Effective-scope headers for routing/startup and stable IDs for newly touched criteria; clarify saved/applied wording | Documentation reconciliation can proceed now; do not invent missing evidence |
| CR2: Removal workflow | Public impact/removal contract and synthetic dependency scenarios, then shared Host operation | Default/empty-profile and active-removal policy needed before those parts execute |
| CR3: Active generation | Availability invariant plus desired/runtime/menu generation scenarios | Investigate applied-snapshot and credential-fence interaction; auto-sync opt-in remains separate |
| CR4: Provider refresh | One versioned discovery/evidence extension qualified on two different connection types | Preserve existing model templates; choose authoritative coverage/entitlement behavior |
| CR5: Load evidence | Transport inventory, measurement contract, replay/migration and UI/CLI acceptance | No adaptive throttling or artificial traffic required |
| CR6: Research assistant | Evidence-only research contract and failure fixtures | Choose data/budget/review scope before execution; not on the inference critical path |

Do not make all six prerequisites for every change. A Live typography improvement
needs its affected FC-LIVE cases, not a completed provider refresh platform.
A meaningful PR can link the relevant contract section and state behavior delta,
focused validation and remaining uncertainty in a few sentences.

## Evaluation receipt to keep lightweight

For each affected criterion: `criterion ID | implementation reference | result |
evidence reference/command + artifact/client/source scope | remaining limitation`.
Useful results are satisfied, partial, not satisfied, not evaluated. Store detailed
test evidence once and link it; do not copy a growing full matrix into each PR.
Small changes can use prose. Require contract fields only when they affect the
claim, and distinguish document acceptance from implementation acceptance.

Sources for this review are the linked repository documents/code and current
user requirements summarized in the register. No external web source was needed:
the concrete gaps concern Lain's own state/ownership semantics. No acceptance
criteria above were newly executed, and this review changes no shared contract.
