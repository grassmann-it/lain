# First public release work packages

31 October 2026 · The later six-Sol/four-GLM implementation request authorized
source work. This file retains the original package plan; current implementation
and acceptance are in [execution ledger](../recording-execution-2026-10-30.md).
Publication and the remaining release-scope decisions are still separate.
Requested after the [public-preview review](../public-preview-review-2026-10-30.md).
Reuse existing STAB/REC work, not a second competing stabilization implementation.
Version, exact public platform/model support and first-release recorder inclusion
remain open user decisions. Linux-x64 preview is the recommendation, not a change
to release metadata. Existing Artistic2.0 decision stays intact.

## Package map

| Package | Result | Priority / relation |
| --- | --- | --- |
| [PUB-00](00-baseline-scope.md) | Baseline, support promise, common contracts and ownership | Release gate; coordinates STAB-00/08 |
| [PUB-01](01-client-integrity.md) | Fresh client exports preserve image/context/compaction/effort capabilities | Release gate; extends existing source fixes, not a duplicate exporter |
| [PUB-02](02-active-state.md) | Desired/applied/client state and active quota fences stay coherent | Release gate; executes/refines STAB-01 |
| [PUB-03](03-errors-doctor.md) | Safe actionable errors, existing Doctor extended and UI support copy | Error correctness gate; UI diagnostics enhancement |
| [PUB-04](04-account-outcomes.md) | Actual-account inference/models/quota/client outcome summary | Existing onboarding commitment, reuses STAB-04/05/11 |
| [PUB-05](05-routing-explanation.md) | Explain actual selected access and only observed skip reasons | Proposed product enhancement, independent of content recorder |
| [PUB-06](06-fresh-install-delivery.md) | Exact-artifact fresh use/update/recovery + truthful release guide | Release gate, consolidates STAB-02/07/08 |
| [PUB-07](07-offline-demo.md) | Explicit synthetic offline demo | Optional; never blocks first public release |
| [PUB-08](08-explicit-free-routes.md) | Explicit free-model worker choice works without automatic strict-mode veto | Newly confirmed workflow correction; optional strict mode kept distinct |

PUB-08 reflects the subsequent explicit user instruction: don't introduce Lain's
`free_only` mode when the user chooses `openrouter/free`. Current applied routes
are `configured`; the check-free result concerns a requested stricter override,
not current route availability. Source/installed skill guidance was clarified;
that is not a real provider/worker usability test or removal of provider caps.

## Shared authority and no double ownership

- Integrator/PUB-00 owns Core DTO/schema changes, project/build/version files,
  Host Program and existing shared navigation/CLI dispatch. New migration work
  stays with the existing domain migration owner; no worker adds an independent
  chain. REC-04 remains exclusive owner of future recording migrations.
- PUB-01 owns export compatibility code and its tests. PUB-03 owns Doctor logic;
  consume the same capability evidence rather than implementing two validators.
- PUB-02 is the STAB-01 runtime lane, not a second worker touching the same files.
- PUB-04 merges into existing account/model/removal lanes; default/empty-profile
  deletion policy remains separately conditional. No new duplicate Connect wizard.
- PUB-05's new source facts require integrator approval at dispatch/journal seams;
  no full-body recorder dependency, guessed reasons or parallel Usage migration.
- PUB-06 is the existing packaging/acceptance lane; optional feature workers don't
  install into production or independently rename/publish release artifacts.
- PUB-08 owns routing-skill/cost-check presentation semantics, not relaxing active
  provider/account settings or all existing explicit route guards.

## Parallel sequence

1. PUB-00 refreshes inventory and freezes shared boundaries; PUB-06 prepares
   independent first-use/rollback cases. No production state changes.
2. PUB-01 export, PUB-02 active state, PUB-03 safe-error/Doctor foundations,
   PUB-04 account presentation and PUB-08 workflow correction can run in isolated
   owners after their shared interfaces are agreed. PUB-05 can use synthetic
   routing-decision fixtures; PUB-07 only if expressly included.
3. Coordinate shared Desktop changes:03Help/Diagnostics vs04Accounts vs05Request
   detail are separate new components; integrator alone edits shell/navigation.
4. PUB-06 freezes the integrated candidate and runs exact-artifact gates. One
   serial build/test/publish lane, cache TMPDIR, bounded fake load; GUI tests only
   private Xvfb/D-Bus. Preserve WIP/other workers, active traffic and legacy proxy.

Recording REC-00–08 remains the next planned product work. It can progress on its
own contracts; this register neither cancels it nor automatically makes all REC
packages first-public-release blockers. No grading/training/Memory is required to
read a saved conversation; no encryption requirement is reintroduced.

## Common acceptance receipt

For each case: status (satisfied/partial/not satisfied/not evaluated), exact source/
artifact/client/platform, test command/settings/evidence, supported scope and
limits. Planned tests aren't passes. No raw prompts/images/headers/tokens/provider
bodies in support receipts. Source, synthetic, native, packaged, installed and
real-provider evidence remain different. No purchases, paid fallback, unbounded
live model probes, automatic pending Apply or public upload from this planning task.

The earlier request for every configured model's real qualification is not
silently waived by recommending a smaller supported preview. PUB-00 must settle
that support/testing promise before release and cost-bearing validation.
