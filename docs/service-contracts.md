# Shared contracts for Host, UI, and CLI

Recorded 23 October 2026. Domain invariants for the [architecture](architecture.md).
Implemented DTOs and the executable v1 wire contract are documented below and
in `src/Lain.Core/Contracts.cs` and the shared API client.

## Identity and configuration

| Term | Shared meaning |
| --- | --- |
| Account | Stable lain ID, display name, provider, state, and credential references; two accounts from the same provider remain separate |
| Credential binding | Versioned link from a lain account to an observed Core credential identity; historical bindings remain traceable |
| Budget pool | Verified shared quota; multiple keys do not automatically create multiple budgets |
| Model route | Stable route ID pointing to an upstream model, provider/endpoint configuration, and account or explicitly described account pool; supported capabilities |
| Model profile | Name, selected routes/aliases, visibility, and target client; exports use account placeholders without secrets |
| Client binding | Supported harness with version and profile; provenance is evaluated only with reliable attribution |

Display names may change. Removed accounts/routes disappear from active selection;
historical usage evidence retains its identity. Reusing a credential does not
prove a second independent account. A strictly selected account must not silently
switch to another through Core retry; pool selection is explicit.

Configuration changes have a revision. The Host validates and applies them;
API responses distinguish desired and actually effective state. Failed Core
application does not successfully save an active route. Login/OAuth are separate
cancellable stateful operations; secrets do not belong in status responses.
Removal/updates must account for running requests and stored history.

## Request, execution attempt, and event

One logical client request can cause multiple upstream attempts. Totals count
user requests separately from actually consumed attempts. Each attempt records
the real model/provider/account identity and known route. Requested and actually
used models are distinguishable.

Events require documented provenance, unambiguous deduplication, and a reliable
request/attempt link. Package A determines usable source and normalized IDs.
Approximate timing or a model prefix does not replace this link.

Only sanitized metadata is stored for basic statistics, with no raw credentials,
headers, or complete request/response bodies. Re-ingesting an event does not
count it again. Stored data survives restarts. If event loss cannot be
reconstructed, the period remains marked as potentially incomplete; do not
invent accuracy guarantees for a volatile queue.

## Quantities, times, quotas, and costs

- Input/output/cache/reasoning values retain verified provider semantics;
  subsets are not added to the total again. Unknown values are JSON `null`,
  never a measured `0`.
- Day boundaries and period filters use a stated timezone. Late events update
  affected aggregates traceably.
- Latency and time to first output require suitable timestamps. Tokens per
  second require a verified token count and named interval; otherwise the
  metric remains unknown.
- Quota snapshots include unit, account/pool scope, source, observation time,
  reset, and state. A stale last success remains distinguishable from a fresh
  response. One account's error does not hide other accounts.
- `windowLabel` names the verified budget window; `usedPercent` retains an
  explicitly reported consumption percentage independently of quantity values.
  Missing amounts are not invented as tokens/credits from percentages;
  percentage points may be normalized to 100 with unit `percent`.
- `unsupported` means there is no verified reader for the provider; it is not a
  connection error. `error` and `stale` are reserved for actual retrieval
  failures and outdated observations respectively.
- Windows are not added together. An overview may highlight the tightest known
  window but must name that window and its remaining budgets.
- Subscription fees, included usage, purchased API credits, estimated token
  costs, and actual billing are different quantities. Shared budgets are counted
  only once in overall views.

## Shared use cases

| Area | Required operations in the initial state |
| --- | --- |
| Service/proxy | Status, version, readiness, desired/effective configuration; controlled start/stop/restart of Lain's proxy |
| Accounts | List, add, begin/check/cancel login, test connection, enable/disable, remove |
| Models/profiles | Read available models, select concrete routes, set visibility/aliases, save a named profile and apply/export for the reference client |
| Usage | Period overview, day/model/account breakdown, filtered request/error views; only verified harness/profile dimensions |
| Quota | Read snapshots, request refresh, identify running/failed retrieval |

Responses include contract version, snapshot/revision reference, collection
state, and coverage. GUI and CLI return identical numbers for identical data.
The UI may format but must not implement different domain aggregation. Lists
and periods are bounded or paginated; one request does not load unlimited
history into a client.

CLI commands provide JSON on stdout, diagnostics on stderr, and distinguishable
error states. A skill uses documented commands, not TUI screen scraping. Host
failure gives a clear message, with no silent replacement collector or directly
opened database. Changes follow the same validation/application path as Avalonia;
CLI access does not expand a skill's authorization.

## Contract acceptance before parallel implementation

Package A supplies at least sanitized examples of success, 429/retry, cancellation,
two accounts, replay, and a known coverage gap. API/JSON examples and field
definitions are then recorded so Avalonia and CLI can work independently against
the same contract. The proxy Core schema stays behind its adapter; its version
is not the public lain API version.

## Implemented v1 wire contract

`src/Lain.Core/Contracts.cs` contains shared DTOs;
`src/Lain.Client/LainApiClient.cs` is the typed reference client. JSON uses
camelCase and explicit null for unknown measurements. The API binds only
127.0.0.1:18418 and requires its own Host bearer token from private lain storage.
Inference and management tokens are separate. All mutations are serialized in
the Host; stale revisions return 409. Configuration distinguishes desired and
applied state. A profile is marked applied only after successful startup.
Invalid runtime generations must not be restarted.

Status optionally includes `activeProfileId` and `activeProfileRevision`. These
identify application proven by matching profile/runtime metadata; window selection
and desired configuration are not sources. Missing, old, or incompletely published
metadata yields `null`. Older installations without the unique runtime profile
identifier remain unknown until an explicitly triggered successful profile
application. The UI update does not apply pending configuration to fill the display.

Usage: GET `/api/v1/usage` and `/api/v1/attempts`, parameters `from`, `to`,
`timeZone`, `accountId`, `model`, `statusCode`, `offset`, `limit`. Intervals are
limited to 366 days, request pages to 500 entries. Endpoints return the same
aggregates for GUI, CLI, and skills.

Local credentials and provider errors are not exposed as public API data.
After Host authentication, `/api/v1/auth/token?audience=codex` supplies only
the local inference key.

## Additive extension of 24 October 2026

Executable DTO additions and the agreed implementation boundary are in
[observability and provider contracts](observability-contracts.md). The public
version remains v1; existing DTO parameters and endpoints retain their semantics.
New optional fields do not replace unknown values with estimates.

- `/api/v1/usage/analysis` adds complete Host aggregates for verified model
  families, provider/gateway, reasoning, routes, and worker references, plus
  performance distributions with measurement and population counts. Existing
  usage responses remain compatible.
- `/api/v1/inspector` and `/api/v1/inspector/events` supply only Lain's bounded,
  volatile stream observations with their own source/cursor reference, lifetime,
  redaction state, and visible gaps. Content does not become durable usage.
  Chats and workers are linked only through verified identities.
- Provider descriptions, setup, and the model matrix distinguish verified
  sources, prepared adapters, missing credentials, and actually tested
  connections. Quotas retain budget type, unit, and shared scope.
- Route/profile policies add explicit fallbacks. `free_only` rejects paid,
  trial/subscription-bound, and unknown-price alternatives and requires actual
  cost enforcement. A catalog claim alone does not authorize dispatch.

An early usage report and the actual stream end may differ. The additive journal
completion `attempt_finished` adds measurement and `completionState` without
rewriting original token/usage evidence. New analysis errors count the known
actual completion; the existing `/usage` error counter retains its historical
basis. Migrations and replay compatibility belong to the usage adapter, not clients.
