# Groq, Gemini, Cloudflare and NVIDIA quota sources

Source qualification: 24 October 2026. Public official documentation only; no
private credentials, authenticated provider queries, account browser sessions or
inference calls were used. Source availability, implementation, installation and
live account qualification are separate claims.

## Groq: passive response headers

[Official rate-limit documentation](https://console.groq.com/docs/rate-limits)
documents these six response headers:

| Headers | Meaning | Unit / window |
| --- | --- | --- |
| `x-ratelimit-limit-requests`, `x-ratelimit-remaining-requests`, `x-ratelimit-reset-requests` | Organization rate limit observed by the request | Requests per day (RPD) |
| `x-ratelimit-limit-tokens`, `x-ratelimit-remaining-tokens`, `x-ratelimit-reset-tokens` | Organization rate limit observed by the request | Tokens per minute (TPM) |

Reset values are durations relative to the actual response observation, such as
`2m59.56s`, not epoch timestamps. `retry-after` applies to 429 responses and is not
a third budget. Missing/ambiguous/invalid values remain unknown. A response with
zero remaining is evidence of zero, not a missing value. A larger remaining value
than its limit is inconsistent and is discarded by the existing parser.

Limits are organization-level and model-dependent. A local account identifies
which credential observed a response; it does not prove the organization's ID or
that another account/model has an independent pool. Rows therefore retain the
local account, upstream model and window separately and do not inherit a guessed
shared pool. No key is needed to read the already observed local data.

The implementation connects the ordinary upstream response to a bounded durable
core observation store, then to `GroqQuotaAdapter`, `QuotaService` and the existing
quota DTO. A refresh reads the private core endpoint and never sends inference.
Before the first valid response, support is `awaiting-observation`. Re-reading the
same observation retains its original `ObservedAt` and `DataAsOf`; it cannot make
an old token window fresh. `FreshUntil` is the earlier of observation + five
minutes or the supplied reset. Retained stale amounts are not refilled or reset
locally.

The manual route is [Groq Console limits](https://console.groq.com/settings/limits).
The separately documented [Prometheus API](https://console.groq.com/docs/prometheus-metrics)
requires Enterprise entitlement; its existence does not establish access for a
Free/Developer account. No Enterprise probing is added.

## Gemini Developer API: optional project usage

The inference key is insufficient for the implemented source. The official
[Monitoring timeSeries.list API](https://docs.cloud.google.com/monitoring/api/ref_v3/rest/v3/projects.timeSeries/list)
accepts OAuth scopes `monitoring.read`, `monitoring` or `cloud-platform` and needs
IAM permission `monitoring.timeSeries.list`. The user attaches an unexpired,
explicitly scoped access-token snapshot and an explicit Google project ID
to the existing inference account through the separate monitor credential path.
No service account is created, no consumer login is treated as authorization and
no refresh token is used by this adapter.

The source requires the textual project ID (for example `my-project`), matching
`resource.labels.project_id`. Project-number-to-ID resolution is not implemented;
accepting a number as an equivalent label would hide valid series or weaken scope
validation.

`GeminiMonitoringQuotaAdapter` reads only
`https://monitoring.googleapis.com/v3/projects/{project}/timeSeries`, filtered to
`serviceruntime.googleapis.com/api/request_count`, resource `consumed_api`, the
exact project, and service `generativelanguage.googleapis.com`. It validates the
same scope on each returned series. The official
[metric definition](https://docs.cloud.google.com/monitoring/api/metrics_gcp_p_z#serviceruntime)
defines this as DELTA, INT64, unit `1`, sampled every 60 seconds and potentially
invisible for up to 1,800 seconds after sampling. Resource labels are defined in
the [monitored-resource reference](https://docs.cloud.google.com/monitoring/api/resources#tag_consumed_api).

The reading is **project API requests for the current UTC day**, across keys and
API methods, including errors reported in that metric. It is usage, not model
inference-only usage, tokens, spend or remaining quota. It uses raw delta points,
bounded pagination, exact interval validation and duplicate-point protection.
`WindowStart`/`WindowEnd` identify the query window; `DataAsOf` is the latest
returned point; `ObservedAt` is retrieval time. The delay is explicit in the label
and support message. No series means unavailable data, not zero. An explicit zero
point is displayed as zero. API permission/transport failures remain errors;
previous valid values are retained by `QuotaService` as stale.

The general Monitoring API and metric are official. Actual export of this
service's series for the user's project still requires a live read; this is not
a claim that every Gemini project publishes it. There is no model label in the
qualified metric contract, and no model-specific denominator is fabricated.
The [Gemini limit documentation](https://ai.google.dev/gemini-api/docs/rate-limits)
defines project/model limits and Pacific-midnight RPD resets. That is a different
period from this explicitly labeled UTC usage window. [AI Studio usage](https://aistudio.google.com/usage)
is the manual route for the project's active model limits.

Google supports official installed-app OAuth, but an owned browser flow and
registered OAuth client are separate implementation work. Supporting a private
expiring snapshot does not claim an automatic login/refresh implementation.

## Cloudflare Workers AI: documented dashboard, API schema still unqualified

[Workers AI pricing](https://developers.cloudflare.com/workers-ai/platform/pricing/)
documents 10,000 free Neurons per UTC day, resetting at 00:00 UTC, and links to the
[account Workers AI dashboard](https://dash.cloudflare.com/?to=/:account/ai/workers-ai)
to monitor Neuron usage. This allocation alone is not an observation of remaining
quota, a confirmed plan or evidence that every model uses this billing path.
Workers Paid and AI Gateway prepaid credit routes must remain distinct.

An official [GraphQL analytics API](https://developers.cloudflare.com/analytics/graphql-api/)
exists at `https://api.cloudflare.com/client/v4/graphql`.
[API token authentication](https://developers.cloudflare.com/analytics/graphql-api/getting-started/authentication/api-token-auth/)
requires Account > Account Analytics > Read and the intended account resource.
An existing inference token can suffice if it has that permission; a separate
token is an optional least-privilege choice. Official schema discovery is
[introspection](https://developers.cloudflare.com/analytics/graphql-api/features/discovery/introspection/).
Cloudflare also supports an official desktop OAuth flow; details belong to the
monitor-auth source sheet. Lack of implemented OAuth is not provider prohibition.

The public Workers AI documentation, analytics tutorials and official REST
reference reviewed here did not establish a specific Workers AI Neuron usage
dataset/field with its account scope, aggregation, billing coverage and freshness.
Unauthenticated schema discovery is not sufficient. This is a **source schema and
live qualification gap**, not a proof that Cloudflare has no official analytics
capability. No guessed `workersAi...` dataset query is shipped. Workers script
invocations, AI Gateway logs, prices multiplied by local tokens and the static
10,000 allocation cannot substitute for the missing evidence. No usage/remaining
adapter is registered until an official schema and its semantics are qualified.

## NVIDIA Build: no suitable public quota API qualified

The [official quickstart](https://docs.api.nvidia.com/nim/docs/api-quickstart)
documents NVIDIA Developer account/API-key setup for Build-hosted inference.
The [product FAQ](https://docs.api.nvidia.com/nim/docs/product) states:
“Members of the NVIDIA Developer Program have free access to NIM API endpoints
for prototyping”. It separately describes downloadable/self-hosted NIM and
Enterprise trial/production licenses.

These sources and the Build inference reference do not establish a Build-hosted
remaining-credit endpoint, per-account limit schema, read scope, reporting delay
or a supported desktop quota OAuth flow. That is the precise public qualification
boundary, not proof that every private account dashboard lacks information.
[NVIDIA Build](https://build.nvidia.com/) remains the account/manual route.
Enterprise NIM metrics, self-hosted GPU limits, NGC organization data and the
90-day Enterprise trial are not Build inference balances. No constant credit
balance or unlimited status is fabricated and no cookie/session import is added.

## Integration contract and validation boundary

The private core returns `GET /lain/quota/observations`, authorized exactly like
its other management endpoints:

```json
{
  "version": 1,
  "observations": [{
    "accountId": "local-account-id",
    "provider": "groq",
    "model": "openai/gpt-oss-20b",
    "requestId": "local-request-id",
    "observedAt": "2026-10-24T12:00:00Z",
    "statusCode": 200,
    "headers": {
      "x-ratelimit-limit-requests": ["1000"],
      "x-ratelimit-remaining-requests": ["999"],
      "x-ratelimit-reset-requests": ["1h"],
      "x-ratelimit-limit-tokens": ["8000"],
      "x-ratelimit-remaining-tokens": ["7900"],
      "x-ratelimit-reset-tokens": ["7.66s"]
    }
  }]
}
```

Main owns the real transport hook, durable core store (maximum 512 account/model
items), private endpoint and Host registration. W4 owns `GroqQuotaAdapter`,
`GeminiMonitoringQuotaAdapter`, bounded JSON transport, this source sheet and
`tests/quota-sources`. The Host client must have redirects disabled; the core
client must carry only local management authorization. The source tests cover
fake HTTP through real adapters and `QuotaService`, including account/model
separation, old timestamp retention, missing/zero data, HTTP errors, pagination
and cancellation. The Go transport/store tests and root integrated build are
necessary to claim the actual Groq bridge; parser tests alone are insufficient.
Installation and private live reads are performed separately by Main.
