# Parallel recording work packages

31 October 2026 · Implementation authorized and integrated. This document retains
the original ownership/acceptance plan. Actual completion, limitations and evidence
are tracked in [execution ledger](../recording-execution-2026-10-30.md); a package
being listed here is not proof of its acceptance. Production recording remains off.

## Accepted result and authoritative sources

Build a local, opt-in conversation archive for human inspection and model/skill/
prompt comparisons: coherent messages, tool requests/client-reported results,
retries/model changes and evidenced workers, available in lain after restart.
Use private **unencrypted** local storage; no key management. Include every used
model by default within the enabled capture scope. Unknown training rights alone
do not block local inspection; review/anonymized export/training remain separate.
Exclude credentials before persistence, preserve routing/auth/stream/accounting,
bound RAM/disk and make gaps visible. Do not import history or intercept bypassing
traffic. No actual local execution claims from mere tool requests/results.

Current authority: [Sprint1 plan](../training-logging-plan.md),
[audit](../observability-audit.md), [coverage](../capture-coverage.md),
[existing observability guarantees](../observability-contracts.md),
[Core integration](../../proxy/README.md).
Historical encrypted-archive proposals do not override the later user decision.

Baseline observed: installed upstream8.0.41/lain patch4, Core SHA256
`191f4f17e4096558c32ebc5b28ec51341fadc6549f3269300af22800dfe6ce18`,
Host835d2e2, desired919/applied917 at audit; checkout patch5. All facts must be
re-read before future implementation. Preserve pending changes, foreign CLI/UI
WIP and installed Desktop fix. Never ship patch5 behavior as an incidental recorder
update. These packages don't authorize deployment, real capture or paid calls now.

## Packages and proposed owners

| ID | Result | Exclusive owner | Depends on |
| --- | --- | --- | --- |
| [REC-00](00-contracts.md) | Contracts, shared fixtures, wiring ownership and baseline freeze | Integrator | Current plan; settles shared boundary choices |
| [REC-01](01-policy-redaction.md) | Opt-in/control, secret redaction and scope semantics | Policy worker | REC-00 |
| [REC-02](02-observer.md) | Qualified one-way Core observation + private source feed | Observer worker | REC-00; policy snapshot from01 for activation |
| [REC-03](03-normalization.md) | Protocol/message/tool stream assembly | Protocol worker | REC-00; consumes02 records, can start with fixtures |
| [REC-04](04-storage.md) | Source journal, payloads, recovery, retention and all recording migrations | Storage worker | REC-00; redaction from01 before persistence |
| [REC-05](05-normalization-index.md) | Rebuildable conversation/worker/run projections and paged queries | Projection worker | REC-00 +03 +04; schema requests to04 |
| [REC-06](06-api-client-cli.md) | Host handoff collector, local API, typed client and CLI | API worker | REC-00; real integration01/02/04/05 |
| [REC-07](07-desktop.md) | Saved conversations and skill/run variant inspection | Desktop worker | REC-00/06 contracts; fixtures first, real05 later |
| [REC-08](08-qualification-delivery.md) | Independent parity/privacy/resource/native acceptance and deliverable | Qualification worker + integrator | Criteria can start now; final integrated01–07 |

Names describe ownership, not agents already coding. Recommended eventual maximum:
eight bounded workers plus integrator, within the historical twelve-agent ceiling.
Use fewer for early waves; builds/tests/publishes share one serial lane.

## Parallel waves

1. **Contract wave:** REC-00 publishes v1 DTO/interface/schema draft plus executable
   synthetic fixtures and dependency pins. In parallel REC-02 may prepare a read-only
   hook-spike design and REC-08 its independent test plan. No separate DTO forks.
2. **Independent wave:** REC-01 policy/redaction, REC-02 static hook spike, REC-03
   pure parser, REC-04 store, REC-05 fixture-based projection, REC-07 fixture UI.
   API scaffolding in06 uses the same agreed contracts; no live traffic required.
3. **Wiring wave:** REC-06 connects Core feed -> policy -> source commit -> projection
   -> client. REC-05/07 switch from fixtures to actual paged store. Integrator owns
   Program.cs, admission integration and shared registration changes.
4. **Qualification wave:** REC-08 runs frozen baseline/off/on/fault suites serially,
   independent review, then exact-artifact/native/isolation/rollback gates. Source
   completion is not installed completion. Recording ships off unless separately
   enabled for the intended installation.

## Handoff seams

Detailed shared seams and ownership are in [REC-00](00-contracts.md). In particular:

- REC-02 produces bounded `SourceObservation` batches with epoch/sequence/gaps;
  it does not write SQLite/files or call Host synchronously on an inference path.
- REC-01 sanitizes a copy for allowed retention. REC-04 only accepts a typed
  `SanitizedObservation`; original request/response bytes continue untouched.
- REC-03 derives semantic records and usage from retained observations without
  inventing complete messages or losing source references. REC-04 commits source;
  REC-05 applies versioned projections after durable commit and can rebuild.
- REC-06 serves bounded safe query DTOs and structured health. REC-07 uses only the
  shared client, never reads files/SQLite or fetches full conversations on refresh.

## Source/file boundaries

All are proposed future paths, not files already implemented:

- Integrator: `src/Lain.Core/RecordingContracts.cs`, shared fixtures/schema, solution/
  project/dependency/build files, `src/Lain.Host/Program.cs`, existing Core/main
  entry wiring and shared navigation registrations. Worker changes to these are
  submitted as focused requests/patches, not concurrent direct edits.
- Policy: `src/Lain.Host/Recording/Policy/**`, `tests/recording-policy/**`.
- Observer: `proxy/adapter/internal/lain/recording*.go`, own Go tests. Existing
  main/http/executor/integration/manifest edits go through integrator after spike.
- Protocol: `src/Lain.Host/Recording/Normalization/**`, `tests/recording-normalization/**`.
- Storage: `src/Lain.Host/Recording/Storage/**`, **all** new recording SQL migrations,
  `tests/recording-storage/**`. No existing Usage migration changes.
- Projection: `src/Lain.Host/Recording/Projection/**`, `tests/recording-projection/**`.
  SQL changes requested from storage; no private databases outside test roots.
- API/client/CLI: `src/Lain.Host/Recording/Api/**`, collector handoff module,
  `src/Lain.Client/LainApiClient.Recording.cs`, `src/Lain.Cli/RecordingCommands.cs`,
  `tests/recording-api/**`, `tests/recording-cli/**`. Existing dispatch edits requested
  from integrator because CliApplication has foreign WIP.
- Desktop: `src/Lain.Desktop/Recording/**`, `tests/recording-desktop/**`. Existing
  MainWindow/navigation/shared theme files remain integrator-owned.
- Qualification: `tests/recording-acceptance/**`, own runner/evidence docs; no
  implementation edits during independent review without returning to owner.

## Open decisions vs routine engineering

Confirmed: no encryption, all-model inspection scope, local private permissions,
opt-in/global stop, causal integrity, bounded fail-open archive, no automatic
training/export/skill injection. Do not reopen these without new evidence.

REC-00/01 must make these remaining choices explicit before enabling capture:
trusted per-client identity vs explicit whole-owned-Gateway scope; exact hook/
handoff boundaries; stop/resume and in-flight commit semantics; retention/disk
budget defaults and measurement budget. Whole-owned-Gateway capture is a proposed
usable initial scope and must not be sold as verified application/project isolation.
Narrow policy requiring unavailable evidence must reject that configuration or
omit content honestly, rather than guessing identities.

Numbers in the parent plan (7/30days,5GiB,16MiB queue,2ms/2% benchmark budget) are
recommendations, **not user-approved acceptance facts**. Implement tunable bounded
limits, record the tested settings and settle the release budget explicitly.
Frame layout, indices and codec details are engineering choices within the agreed
recovery/compatibility/resource contract. CAS is optional initially, not grounds
for delaying the first coherent browser or weakening deletion correctness.

## Common worker completion receipt

Return: owned paths and commit/diff; contract/fixture version; actual tests with
exact source/artifact pin and settings; supported source/representation/protocol
cells; negative/fault cases; material gaps; resource measurements if performed;
integration requests and rollback/migration implications. Never print private
traffic, tokens or unsanitized provider errors in a receipt. Preserve source,
fixture, build, native, installation and real-provider evidence as different stages.

Unresolved shared-contract changes return to REC-00. Do not silently add a cloud
sink, auth change, new forwarding endpoint, migration to Usage DB, training export,
blind-review workflow or retrospective reasoning skill to make a package pass.
