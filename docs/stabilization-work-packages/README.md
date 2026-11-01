# Stabilization work packages —28 October2026

Prepared at the user's request. This is a documented implementation handoff, not
a newly started implementation, test run or release. Version0.7 is proposed;
the previously selected0.8 is reopened, not silently changed. The packages are
usable without chat history; all necessary shared contracts are linked here.

## Target and starting point

A dependable existing Linux-x64 Monitor and basic Gateway, with honest quota,
active-runtime/menu state, readable Live and preserved user data. See the
[scope/refactoring review](../release-scope-review-2026-10-28.md).

Planning source:47f3c79 plus existing WIP. Earlier source reviews reference835d2e2;
re-read current code before reproducing findings. Current live import/application
work belongs to another active task: inspect its state/owner and coordinate;
do not rerun a second account creation/route attachment/apply loop.
Historical read-only runtime at27 October23:15UTC was desired517/applied135 and
ready. That is not the current state guarantee or instruction to Apply.

## Packages

| Package | Outcome | Readiness |
| --- | --- | --- |
| [STAB-00](00-baseline.md) | Baseline, scope and effective contracts | Ready for inventory |
| [STAB-01](01-active-state.md) | Applied runtime, quota exclusion and client generation | Ready for source investigation and bounded fixtures |
| [STAB-02](02-lifecycle.md) | Linux lifecycle and Monitor acceptance | Ready for targeted source checks and isolated Linux tests |
| [STAB-03](03-live.md) | Readable Live Inspector | Semantic UI projection ready |
| [STAB-04](04-model-import.md) | Bounded model reconciliation and honest menu delivery | Ready to evaluate batching |
| [STAB-05](05-removal.md) | Dependency-aware removal | Impact inventory ready |
| [STAB-06](06-providers.md) | Provider/model and quota qualification | Offline checks ready |
| [STAB-07](07-persistence-resources.md) | Persistence and bounded resource checks | Ready for bounded synthetic scenarios |
| [STAB-08](08-delivery.md) | Exact-artifact release acceptance | Staging preparation possible |
| [STAB-09](09-claude.md) | Claude monitoring and separate Anthropic API access | Additional requested scope: research ready, implementation conditional on verified access contract. Not a0.7 release prerequisite by default. |

[STAB-11: Account-connect experience](11-connect-experience.md) is a stabilization
priority: finish existing agreed Desktop/CLI onboarding and surface qualified
browser/CLI methods, coordinating model add/remove with STAB-04/05. Entirely new
vendor OAuth integrations are separate, not a reason to defer usable current paths.

[STAB-10: Brand/About](10-brand-about.md) adds the requested header-cat interaction
and discoverable version/info workflow, reusing existing About. It can run alongside
other packages after reserving shared Desktop files.

STAB-09 is an additive Claude research package. It need not delay stabilization;
its inclusion in an actual0.7 feature scope remains a decision.

## Order and parallel ownership

Start with STAB-00 inventory. STAB-01,02,03,06 investigation and07 baseline can
proceed independently in bounded work areas. STAB-04 and05 touch common account/
model changes: one implementation lane or integrator-owned shared planning,
not concurrent conflicting rewrites. STAB-05 cascade policy is conditional.
STAB-08 final exact-source freeze follows integrated changes and agreed version/
model/client scope. Each earlier package can prepare its tests before final freeze.
Research for STAB-09 is independent; OAuth-inference prohibition is fixed.

Integrator owns common DTO/API revisions, configuration/apply interfaces,
project/build/version files and migration assignment. Usage owner exclusively
owns DB migrations; any need is agreed with integrator. Domain workers own their
listed code and tests; request cross-owner changes instead of editing blindly.
No worker count/model selection or new execution is implied by this index.

## Shared execution boundaries

- Re-read AGENTS.md, [development](../development.md) and relevant contracts.
  Preserve existing WIP/active work, account IDs, history and external clients.
- One serial build/test lane. Use private cache TMPDIR, not RAM-backed /tmp;
  no simultaneous large builds. Respect the existing verification lock/resource
  limits and private-Xvfb/D-Bus GUI harness. Clean only known disposable artifacts.
- Configuration writes through revision-bound supported APIs/CLI, never direct
  edits to live configuration. No implicit Apply of unrelated pending changes.
- Tests use synthetic credentials and isolated state. Provider/live requests,
  account logins, production install/restart/update, purchases and publication
  require applicable explicit authorization; do not infer these from file creation.
- Do not recreate completed work. Every claim is scoped to exact changed source,
  artifacts, client/platform and provider request extent where relevant.

## Contract references

[Feature evaluation](../feature-contracts.md), [shared service contracts](../service-contracts.md),
[observability](../observability-contracts.md), [routing](../collection-router-v1.md),
[Live workflow](../contracts/live-inspector.md),
[contract review](../contracts-review-2026-10-27.md),
[Live review](../live-inspector-review-2026-10-27.md),
[Linux recovery](../linux-delivery-stabilization.md),
[client install proposal](../contracts/client-integration-installation.md).
Read dated amendments: older startup/release present-tense statements can be stale.

## Open decisions and deferred work

- Confirm release version and exact model/client support scope. Prior request
  to really qualify every configured model is not automatically waived by a
  proposed smaller0.7 support subset. Agree scope before paid/large real testing.
- Removal-default/empty-profile policy affects STAB-05 cascade only.
- Auto-sync/automatic global activation remains unresolved; no package may use
  it to bypass existing explicit Apply review.
- General discovery platform, nested manufacturer/model/access picker, budget
  allocation, adaptive concurrency and client-install facade are1.0 proposals.
  LLM selector and node editor are optional directions. ZCode explicitly skipped.

## Evidence receipt

Per affected acceptance case record: case reference, result (satisfied/partial/
not satisfied/not evaluated), source/artifact/client scope, actual test command
or evidence link, and limitation. Keep detailed logs once; link them. Never save
keys, raw provider errors or private chat contents into public reports. Package
status begins planned and advances only against its actual observations.

## Public-preview refinement · 30 October 2026

The [PUB-00–08 handoff](../public-release-work-packages/README.md) adds fresh-export
integrity, Doctor/support UI, actual-account outcomes and optional routing/demo
ideas with exclusive ownership. PUB-02 is this register's STAB-01 lane; PUB-04
coordinates STAB-04/05/11; PUB-06 is the existing delivery/acceptance lane. Do not
start a second worker editing the same files because a requirement has two IDs.
