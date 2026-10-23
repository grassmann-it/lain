# Observability and provider expansion contracts

Contract preparation: 2026-10-24, based on the explicitly authorized
[expansion](expansion-2026-10-24.md). This document specifies the additive v1
implementation boundary; it does not claim that a provider connection, release,
or live test has passed. DTOs are in
[`ObservabilityContracts.cs`](../src/Lain.Core/ObservabilityContracts.cs),
[`ProviderContracts.cs`](../src/Lain.Core/ProviderContracts.cs), and
[`Contracts.cs`](../src/Lain.Core/Contracts.cs).

The architectural choice is to keep durable measurement metadata in SQLite and
stream content in bounded, expiring memory. The Host owns aggregation, cursor
translation, configuration validation, and provider registration. Clients render
the returned facts. This is an implementation decision within the user's
delegation; it does not authorize accounts, purchases, legacy changes, or new
provider requests without credentials.

## Compatibility and transport

- Public management paths remain `/api/v1`, authenticated with the existing
  local Host token. `Api.Version` remains 1. Added constructor parameters have
  defaults; existing operations and fields retain their meanings.
- JSON is camelCase. Missing optional metadata and unknown measurements are
  `null`; a reported zero is `0`. Clients tolerate additional fields and unknown
  future string values. Host inputs validate known values and fail closed for
  unknown policy values.
- Public timestamps are UTC instants with an offset (`Z` preferred). Usage
  `timeZone` controls calendar grouping, not elapsed duration. Durations use a
  monotonic clock, in milliseconds.
- Configuration writes retain `RevisionRequest<T>`/the existing revision
  envelope, serialized Host mutations, and HTTP 409 on stale revisions.
  Route/profile policies use that same path. Read-only policy inspection and
  onboarding documentation do not mutate configuration.
- The durable journal remains a private adapter contract. New optional journal
  properties are omitted when null during canonical serialization, preserving
  replay digests of historical schema-v1 events. New public DTO fields do not
  justify rewriting stored journal events or backfilling inferred identities.

## Durable dimensions and grouping evidence

`AttemptDto` adds `sourceId`, `requestedModel`, `dimensions`, `measurements`,
and `completionState`. `(sourceId, requestId)` identifies a logical incoming
request; `(sourceId, id)` identifies an actual execution attempt. A retry keeps
the request ID and receives another attempt ID. A transport-specific credential
identity is private and is not a substitute for the stable lain account ID.

`AttemptDimensionsDto` has these nullable fields:

| Fields | Meaning |
| --- | --- |
| `modelFamily`, `manufacturer` | Documented comparable model family and model maker |
| `inferenceProvider`, `gateway` | Actual known execution provider and intermediary; neither is guessed from the model name |
| `billingAccountId` | Known stable billing identity, separate from provider/gateway labels |
| `reasoningEffort`, `mode` | Explicit requested or observed settings, not conclusions drawn from generated text |
| `conversationId`, `turnId` | Explicit harness conversation and turn references |
| `workerId`, `parentWorkerId` | Explicit worker identity and parent relation |
| `parentRequestId`, `toolCallId` | Explicit request parent or an unambiguous tool reference |
| `responseId`, `previousResponseId` | Explicit protocol response-chain references |
| `harness`, `profileId` | Proven client/profile attribution |

Each known dimension needs an `evidence` entry: `{field, source, key}`. `field`
uses the exact camelCase property name. Accepted source kinds are
`route_binding`, `harness_header`, `request_body`, `provider_response`, and
`core`. `key` names the curated field/header/path, never its raw value, a secret,
or a full payload. Evidence records an observed assertion, not an authentication
guarantee. Correlation metadata must not grant account membership or routing
authority.

For example:

```json
{
  "workerId": "worker-2",
  "parentWorkerId": "worker-1",
  "conversationId": null,
  "evidence": [
    {"field":"workerId","source":"harness_header","key":"x-agent-id"},
    {"field":"parentWorkerId","source":"harness_header","key":"x-parent-agent-id"}
  ]
}
```

The example illustrates the schema, not an additional accepted header. The
instrumentation's tested allowlist determines which harness fields are read.
The current Core bounds each identity/static dimension value to 192 ASCII
characters from its tested character allowlist and rejects credential-shaped
material. Only curated, bounded scalar identities enter the journal. Prompt text, arbitrary
metadata, authentication headers, keys, and tool arguments/results do not.
Terminal events may fill an unknown dimension; they may not silently change an
already known identity or its evidence. Conflicting assertions are not merged
into an invented group.

Clients may group attempts by their explicit request ID and then by explicit
conversation/turn/worker relations. A tool continuation is not a new chat merely
because it has another request. Conversely, absence of correlation evidence must
remain visible: matching text, timestamps, models, routes, or account names do
not establish a conversation or worker. Several tool calls do not become one
invented `toolCallId`; their individual event references remain separate.
Response-chain links do not automatically fabricate a conversation or turn ID.

## Completion and performance

`AttemptMeasurementsDto` contains `firstOutputLatencyMs`, `totalDurationMs`,
`firstOutputAt`, `finishedAt`, and `source`. Source `core_monotonic_v1` identifies
the newly instrumented owned-core measurement. Old `durationMs` and
`firstTokenMs` retain their old meanings and are not silently promoted to the new
measurement source.

A provider may report usage before its wrapper stream actually closes. The
durable sequence can therefore be:

1. `attempt_started`, fsynced before dispatch;
2. `usage`, preserving the reported token quantities, outcome, and status;
3. `attempt_finished`, carrying final dimensions and completion measurements
   without replacing tokens, usage outcome, or status.

When no earlier usage was reported, the final `usage` can carry the completion
facts itself. Optional journal `completionState` is set only by the actual
completion hook (`success`, `failed`, or `canceled`). `AttemptDto.state` remains
the historical usage-outcome field; `completionState` expresses the later known
wrapper outcome. A reported usage success followed by cancellation therefore
remains distinguishable. Missing final measurement is unknown, not zero.

| Analysis metric | Definition and eligibility |
| --- | --- |
| `firstOutputLatencyMs` | Start of the attempt's durable dispatch gate to the first meaningful emitted text/reasoning or tool activity, using the qualified core parser and monotonic clock; protocol keepalive/role/usage metadata is not output |
| `totalDurationMs` | Start of that dispatch gate to the wrapper's completion hook after its upstream source has drained or failed/canceled |
| `outputTokensPerSecond` | Reported output tokens divided by qualified total duration in seconds; both must exist and duration must be positive |

Both durations include the required pre-dispatch journal fsync. Total duration
ends before the final journal fsync and downstream completion-marker delivery;
it is not the client's receipt of the final byte. Tool argument activity can
establish first output without retaining arguments in the inspector. These
boundaries are part of `core_monotonic_v1`, not interchangeable with another
provider's time-to-first-token field.

The rate basis is
`reported_output_tokens_per_total_duration_second`. It includes startup time;
it is not a decode rate or a claim about visible-text tokens. Provider output
and reasoning/cache subset semantics remain unchanged. Zero reported output
with a known positive duration yields a measured zero rate; missing output does
not. Wall-clock subtraction is not a fallback for a monotonic duration.

Each `MetricStatisticsDto` returns `samples`, `eligible`, `minimum`, `mean`,
`p50`, `p95`, and `maximum`. Eligible is the full matching attempt count; samples
is the number with the required qualified values. With no samples every
statistic is null. Percentiles use the full filtered set and continuous linear
interpolation at rank `(n - 1) * p`; one sample yields that value. Clients do not
compute medians from pages or average group medians. Complete journal coverage
does not imply complete metric coverage; sample counts remain necessary.

## Usage analysis API

`GET /api/v1/usage/analysis` returns `UsageAnalysisDto`:
`contractVersion`, resolved `from`/`to`/`timeZone`, `observedAt`, `cursor`,
`complete`, `totals`, `performance`, and `dimensions`.

All usage endpoints share the original filters plus `routeId`, `modelFamily`,
`manufacturer`, `inferenceProvider`, `gateway`, `billingAccountId`,
`reasoningEffort`, `mode`, `conversationId`, `turnId`, `workerId`, `harness`, and
`profileId`. All filters are exact identity matches, not fuzzy display labels.
The interval is `[from, to)`, applied to attempt start time, positive and at most
366 days. Default is the previous 30 days ending at Host time. Unknown time
zones, invalid dates, or invalid bounds are 400 errors.

`groupBy` is a comma-separated list of at most three distinct allowlisted keys.
Default: `modelFamily,accountId,reasoningEffort`. Allowed keys are `accountId`,
`routeId`, `model`, `provider`, and every dimension field listed above, excluding
`evidence`. `groupLimit` is 1–500, default 100, independently per dimension.
Each requested dimension returns a separate breakdown, not a Cartesian product.
Unknown values have a null group `key`; the string `unknown` is not invented as
an identity. `truncated` explicitly marks a bounded group list. Total/performance
aggregates still cover the entire matching set, not just returned groups.

`offset`/`limit` remain attempt-page controls (limit 1–500); they do not limit
the population of analysis aggregates. `complete=false` covers journal gaps,
pending attempts, a collector that is not caught up, or a truncated breakdown.
Clients retain cursor/freshness/coverage and metric samples together.

Analysis error counts prefer known completion state over the earlier usage
outcome, falling back to the historical state/status when completion is unknown.
The existing `/usage` summary preserves its original error basis for v1
compatibility; both use the same tokens and logical-request/attempt populations.
The analysis API does not infer money from tokens, plan prices, points, or
remaining quotas. A future cost metric requires a separately identified price
or billing source; absence of that evidence stays unknown.

## Inspector API and ephemeral dataflow

```text
own Core allowlisted stream observations
  -> bounded private RAM replay /lain/inspector/events
  -> Host collector, cursor translation and redaction
  -> bounded Host RAM ring
  -> authenticated /api/v1/inspector and /inspector/events

own Core curated journal metadata -> Host SQLite -> /usage, /attempts, /usage/analysis
```

The inspector does not read or drain a legacy proxy, and content is not copied
to the durable usage store or normal logs. The core's private feed uses
`instanceId`, event `sequence`, and `droppedCount`. The Host translates these to
its own `sourceId`, event `cursor`, and `droppedEvents`. A new Host ring or changed
Core instance starts a new Host generation; an old cursor must never be silently
reinterpreted in a new generation. Core cursors are not exposed as Host cursors.

| Endpoint | Query and response |
| --- | --- |
| `GET /api/v1/inspector` | Optional `requestId`, `limit` (default 100, bounded by `limits.maxStreams`); returns `InspectorSnapshotDto` |
| `GET /api/v1/inspector/events` | Optional `sourceId`, `requestId`, `after` (default 0), `limit` (default 100, maximum 500); returns `InspectorEventPageDto` |

Snapshot streams carry stable request/attempt references, scope, state, timing,
dimensions, first/last cursor, and truncation. Only actual attempt lifecycles are
asserted by the current Core feed. A client can present their explicit request
group without inventing a separately observed request lifecycle. Snapshot
`complete`, `state`, `observedAt`, and `error` describe capture health, not a
guarantee that every output token was retained.

Pages are non-destructive. They carry `nextCursor`, `highWatermark`,
`earliestCursor`, `droppedEvents`, `truncated`, and `gapReason`. A filter can
produce no matching events while advancing `nextCursor` past scanned global
events. Source mismatch, eviction, or an overflow must be visible as a gap or
explicit reset; neither an empty page nor source restart can certify a clean
continuation. Negative cursors and invalid limits are 400 errors. A stale source
is not a configuration revision conflict.

The snapshot exposes actual configured `limits`; defaults are ceilings for
the initial Host implementation: 256 stream descriptors, 8,192 events, 32 MiB
global content, 1 MiB per stream, 256 KiB per event, 120 seconds content lifetime,
60 seconds terminal metadata lifetime, and 500 events per page. The source may
have tighter limits. Retention is independent of open clients. Dropped/expired
content is not reconstructed from durable records; a disconnected stream is not
automatically marked successful.

`contentPolicy` names the implemented redaction policy. The initial
`redacted_output_only_v1` policy permits bounded display output and tool
names/IDs, not request prompts, raw headers, authentication values, tool
arguments/results, or unfiltered errors. Explicit output channels distinguish
text, provider-visible reasoning output, tools, and status. There is no content
export or cancellation action in this read-only inspector contract.
`redactionState` distinguishes `sanitized` from `removed`; null means the source
did not report that state. Sanitization is a lossy display transformation, not a
promise that arbitrary model prose contains no private information. Raw content
must never be used as a fallback after sanitizer failure. TTL/size truncation
is reported independently of redaction.

## Provider catalog, onboarding, and route matrix

Independent providers contribute secret-free embedded manifests under
`src/Lain.Host/Providers/Catalog/{providerId}.json`, deserialized as
`ProviderDescriptorDto`. The Host registry validates IDs, routes, official links,
and field values. A source includes URL, title, actual `checkedAt` UTC timestamp,
and optional notes; null check time means unverified. User-supplied prices and
limits are research leads, not evidence. Provider-specific research documents
and manifests retain their official source citations.

`GET /api/v1/providers` returns `ProviderCatalogDto`.
`GET /api/v1/providers/{id}/onboarding` returns `ProviderOnboardingDto` with
descriptor, configured account references, missing credential fields, and honest
setup/live state. Credentials describe requirements; their values are never
returned. A catalog descriptor may be `prepared` before its transport is
available. `liveVerification=not_tested` remains independent of mock success.
Missing keys do not prevent catalog/onboarding/mock work and do not mean a live
connection failed.

The account creation adapter may add optional `providerRouteId` and a bounded
`settings` dictionary to its existing request. This dictionary is for declared
non-secret configuration such as account/project/region/workspace, billing-plan,
or deployment-scope selection. Allowed keys come from the descriptor plus the
provider-specific validator; unknown keys and secret credential fields are
rejected without echoing their values. The existing dedicated secret input and
private credential storage remain separate. The adapter validates and resolves
endpoint templates before configuration becomes executable.

The account creation API accepts only its supported fields. Declaring
OAuth, account/project/region/workspace requirements does not magically implement
their enrollment. Unsupported or missing setup steps must be shown explicitly;
the Host cannot claim a working connection merely because a descriptor exists.
No browser registration, account purchase, subscription, or OAuth exchange is
implicit in viewing an onboarding page.

Billing plans distinguish `free`, `trial`, `subscription`, `payg`, and `unknown`.
Scope, included amount, reset description, expiry, currency, price interval, and
rate/concurrency limits keep their reported units and provenance. Unknown amount
or expiry remains null. USD/CNY, credits, points, requests, and Neurons are not
converted to tokens. Shared account/project/workspace pools and model allowance
references are not summed as independent money or budgets. An expiring trial
does not become a permanent free tier. Existing `QuotaDto` continues to carry
actual observed quota windows; catalog plans do not fabricate live balances.

The route matrix is `descriptor.routes`. Each globally unique route descriptor
has a concrete model, endpoint or clearly marked setup template, protocol,
billing-plan references, and nullable capabilities. Protocol tokens are
`openai-chat`, `openai-responses`, `anthropic-messages`, and `gemini`.
Context/output lengths are token counts when known. Null tools/streaming/
reasoning/images/structured-output values mean unverified, not false. Publisher
and publisher type preserve vendor-versus-third-party evidence separately from
model manufacturer, inference provider, gateway, and billing identity.

`RouteDto.providerRouteId` links a configured route to that descriptor. It is not
permission to change an existing route silently when catalog documentation
changes. Historical usage records retain their captured evidence, and config
validation still checks actual account, endpoint, model, and protocol.

## Strict free-only routing

`RoutingPolicyDto` has `costPolicy` (`configured` or `free_only`), explicit
`fallbackRouteIds`, and `allowPaidFallback` (default false). Routes and profiles
may carry a policy. A missing policy preserves existing configured behavior;
new declarations cannot silently impose guessed billing facts on legacy routes.
When either applicable profile or route policy requires `free_only`, every
selected/retried/fallback route must satisfy it. An explicit paid opt-in never
weakens an applicable free-only restriction.

For strict free-only eligibility the Host must validate all of:

1. The concrete route has known `pricingClass=free` and documented
   `freeOnlyEligible=true`.
2. An implemented runtime enforcement mechanism pins the qualified model,
   endpoint, account posture, and any upstream-provider restriction needed to
   prevent paid overflow. Documentation alone does not enforce this.
3. Every explicit or core-managed fallback candidate satisfies the same checks.
   Unknown, paid, trial-only, and subscription routes are excluded.

`freeOnlyEnforcement` is a human explanation.
`freeOnlyEnforcementKind` is a machine discriminator: `free_model_pin`,
`free_endpoint`, `provider_control_required`, or `unsupported`. Unknown kinds
fail closed. Even a recognized kind needs an implemented provider-specific
check; the string alone grants no dispatch permission. Account-dependent free
allowances on endpoints with automatic paid overflow remain blocked until their
enforcement can actually be established. A budget balance or a request-time
estimate is not a substitute for an upstream hard control.

`GET /api/v1/routes/{id}/cost-policy?costPolicy=free_only` returns
`RouteCostDecisionDto` with allowed status, pricing class, and reasons. It is a
read-only inspection of the same policy used by configuration/runtime, not an
alternate way to enable a route. Paid fallback requires explicit configuration
and is never added automatically because a free route is exhausted.

## Verification handoff

Contract acceptance requires old-record deserialization, unchanged legacy replay
digests, null-versus-zero cases, duplicate replay, and source/identity conflict
tests. Usage tests cover filtered whole-window percentiles, metric sample counts,
unknown groups, group truncation, timezone boundaries, and post-usage
cancellation. Inspector tests cover explicit worker/turn/tool continuation IDs,
unrelated same-model/text requests, source resets, bounds/TTL/overflow, and
redaction failure. Provider tests use synthetic credentials and mocked HTTP;
descriptor validation, official-source metadata, missing-key states, and free-only
rejection of every paid/unknown fallback are independently checked.

The integrator serializes builds and owns installation/release verification.
Neither DTO preparation nor mock success claims a real provider connection.
