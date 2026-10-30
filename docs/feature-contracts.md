# Feature contracts and change evaluation

Established at the user's request on 27 October 2026: keep explicit descriptions
against which new features and changes to existing behavior can be evaluated.
This register connects existing sources; it does not replace their contracts or
turn proposals into accepted architecture. Individual implementation, package and
runtime claims require their own evidence.

## A contract describes observable behavior

For a meaningful new capability or behavioral change, identify the user outcome,
affected existing guarantees, expected success/failure behavior, and how to verify
it. Use the [short template](contracts/feature-template.md) where no suitable
contract exists. Extend the authoritative document where one already exists.
A copy edit or routine internal refactor does not require another contract file.

Keep three independent statements clear:

- **Requirement / decision:** accepted outcome and scope, with its source;
  proposed details and unresolved choices remain explicitly marked.
- **Implementation:** the relevant code and revision, including known gaps.
  Existing behavior can conflict with the requirement; do not rewrite the
  requirement simply to make the code pass.
- **Evidence:** what was actually checked, against which source/artifact/client
  version, and with what limitations. Planned tests are not completed tests.

Contracts cover visible workflows as well as wire formats. A structurally valid
configuration that forces users to repair references across several screens may
still fail its user-workflow contract.

## Contract register

These IDs are stable review handles, not new API names or universal pass marks.
Read the linked document's dated scope and later amendments before applying it.

| Handle | Behavior to evaluate | Authoritative starting point |
| --- | --- | --- |
| FC-IDENTITY | Account/route identity, revisions, history retention and configuration state | [Shared contracts](service-contracts.md#identity-and-configuration) |
| FC-USAGE | Durable attempts/events, deduplication, null/zero and measurement meaning | [Shared contracts](service-contracts.md), [observability](observability-contracts.md) |
| FC-PROVIDER | Provider/connection/product definitions, model capabilities and source evidence | [Provider contracts](../src/Lain.Core/ProviderContracts.cs), [expansion contracts](observability-contracts.md), [connect contracts](account-connect-work-packages/00-integration-contracts.md) |
| FC-ROUTING | Strict account binding, explicit pools, cost policies, sticky routing and bounded quota fallback | [Routing contract](collection-router-v1.md), [proxy boundary](../proxy/README.md) |
| FC-CLIENT | Model groups, runtime/export consistency, supported client builds and continuation compatibility; proposed model-first access navigation | [Routing/groups](collection-router-v1.md), [harnesses](harnesses.md), [Codex exporter](../src/Lain.Host/Models/CodexProfileAdapter.cs), [model/access picker proposal](contracts/model-access-picker.md), [progressive setup and client installation](contracts/client-integration-installation.md) |
| FC-LIVE | Readable agent output, tool classification and collapsed technical metadata | [Live Inspector workflow](contracts/live-inspector.md), [capture policy](observability-contracts.md#inspector-api-and-ephemeral-dataflow) |
| FC-UI | Navigation/draft continuity, model actions, connect prerequisites, state/apply consistency | [UXD packages](ux-design-work-packages/README.md), [product decisions](product-scope.md) |
| FC-LIFECYCLE | Window/service independence, start intent, autostart, maintenance and recovery | [Startup work](startup-and-autostart-work-packages.md), [delivery/recovery](linux-delivery-stabilization.md); startup text includes historical proposal states and needs code/evidence reconciliation |
| FC-DELIVERY | Source/artifact provenance, installation, update preservation and platform acceptance | [Development](development.md), [release scope](release-0.8.0.md), [delivery/recovery](linux-delivery-stabilization.md) |

Code links describe the implemented structure; they do not by themselves approve
new behavior or prove live access. This register was assembled from documentation
and selected source reads, not a new full-system acceptance run.

## Newly requested outcomes: keep unresolved policy visible

The following are required directions from the current discussion, with design
choices still open. They are not silently added to the existing wire contracts.

| Area | Requested result | Still unresolved |
| --- | --- | --- |
| Account/model removal | Resolve profile/group/fallback dependencies in the removal workflow; preserve history | Default replacement, empty-profile behavior and coordinated runtime removal details |
| Model refresh/menu delivery | Adopt appropriate account models, support refresh, reliably reach client menus | Automatic activation opt-in, aggregator visibility defaults and client reload support |
| Budget distribution | Deterministic selection, skip applicable exhausted allowances, optional later LLM selector | Allocation policy/reserves, unknown-quota policy, classifier data and cost scope |
| Provider update contract | Describe catalog/quota sources and update behavior; consider AI research for missing knowledge | Executable discovery schema, reviewed update path and research execution settings |
| Rate-limit context | Explain429 with observed concurrency, relevant scope, attempt counts and coverage | Exact telemetry fields, migration and any adaptive throttling policy |

Current accepted outcomes are recorded in [product scope](product-scope.md).
Local design reasoning is available through `.architecture-sparring/index.md`
in the working project; ignored notes are not assumed to exist in other clones.
Any future executable work package must carry its required design details and
explicit remaining blockers into ordinary project documentation.

## How to evaluate a change

1. **Find the relevant contract.** State the user outcome and affected handles.
   Reuse existing terminology and identify scope: provider/product, operating
   mode, account, client and platform as appropriate.
2. **Describe the behavioral delta.** What changes, which guarantees remain,
   and which accepted decisions would need revision? An open requirement is
   a scoped blocker, not an excuse to stop unrelated work.
3. **Describe the complete workflow.** Include dependencies, saved/applied/client
   states, cancellation, failure midway, retries/replay, restart and concurrent
   edits where relevant. Prefer an action that completes the user's task over
   making the user navigate internal dependencies.
4. **Write observable acceptance cases.** Include an ordinary case, the important
   failure/boundary cases and preservation of affected guarantees. Avoid tests
   that merely repeat the implementation.
5. **Compare implementation and evidence.** Report each affected criterion as
   satisfied, partly satisfied, not satisfied, or not evaluated. Give a source
   reference or scoped test result and explain practical impact. Unknown is not
   success; unsupported is not automatically a runtime failure.
6. **Maintain the existing contract.** Record accepted changes and their reason,
   link evidence, and leave replaced behavior identifiable. Do not copy every
   guarantee into every feature file. No blanket repository-wide paperwork gate
   or new approval process is introduced by this guide.

For a PR/work package, a short `Contracts / behavior delta / validation / gaps`
paragraph is enough when the change is small. Larger changes can link a dedicated
contract based on the template. Contract review may use an LLM to identify gaps;
its judgment is not a substitute for recorded sources, required user decisions
or executable/native/live verification.

## Example evaluation: removing a model access

**Requested outcome:** remove a model without manually visiting every referencing
profile/group/fallback; preserve its historical usage identity.

**Current source observation:** `ModelsService.DeleteRoute` rejects profile and
fallback references. This protects referential integrity but does not yet fulfill
the requested coordinated-removal workflow. This observation is not a new GUI or
installed-runtime test.

**Proposed cases, not executed acceptance:**

- A route referenced by two profiles, a collection and a fallback can be removed
  with its reviewed dependent changes in one user operation; no dangling refs.
- A stale revision applies none of an outdated removal plan.
- A removed default is handled according to the chosen replacement/empty-profile
  policy, without silently selecting an unapproved paid route.
- History remains identifiable; model refresh does not recreate an intentionally
  removed automatically managed access.
- Failure during runtime/client delivery shows the effective state and offers
  bounded recovery; old active routes are not misreported as already removed.

The default replacement and delivery policy remain to be decided. This example
specifies an evaluation target, not authorization for a particular cascade,
rollback or automatic Apply implementation.

## Recording/conversation inspection proposal · 30 October 2026

[The parallel REC-00–08 handoff](recording-work-packages/README.md) prepares the
next optional local recorder, conversation browser and skill/run manifest work.
It is not an implemented contract. REC-00 must freeze the executable v1 contracts
and synthetic fixtures before dependent implementations. Current scope: private
unencrypted storage, all used models in enabled capture scope, explicit gaps, no
routing/accounting mutation, no credential retention, separate later export/review/
training. The prior output-only Inspector policy remains unchanged until a distinct
recording policy is implemented and enabled.

## Public-preview handoff · 30 October 2026

[PUB-00–08](public-release-work-packages/README.md) prepares release correctness
and proposed small product additions. Existing STAB and REC contracts remain their
domain authority; no new implementation/publication follows from the register.
Explicit selection of openrouter/free uses the configured worker route; optional
strict-policy qualification isn't inferred from a free model name or treated as
a route-availability result. See [workflow correction](openrouter-free-workflow-fix-2026-10-30.md).
