# Cloudflare Workers AI monitoring

Implementation, 25 October 2026. This replaces the global
`CloudflareNeuronCandidateQualified` gate for reads. The constructor retains its
old argument so existing host wiring remains compatible; the selected account's
response now determines availability. No account IDs, route IDs, stored
credentials, login ownership, or usage history are migrated.

## Source and access

The reader sends one bounded read-only GraphQL POST to
`https://api.cloudflare.com/client/v4/graphql`. It selects the bound Cloudflare
account and requests `aiInferenceAdaptiveGroups(limit: 1)` with
`sum { totalNeurons }`, from the start of the current UTC day to the observation
time. No grouping dimensions, model totals, inference calls, prices, or local
request counters participate. A successful response must identify exactly that
account and return one nonnegative, finite neuron aggregate. Missing rows are
unknown, while an explicit numeric zero is measured zero. GraphQL errors invalidate
partial data and return only fixed sanitized diagnostics.

A valid response qualifies schema and access for this telemetry read. It does
**not** qualify billing completeness, sampling accuracy, plan, or source freshness.
A schema rejection triggers at most one official introspection request for
`__type(name: "Account") { fields { name } }`, distinguishing an absent dataset
from an unresolved schema/entitlement problem. Introspection does not substitute
for a successful account-scoped usage response. HTTP responses are capped at 1 MiB
and depth 32, each request at 20 seconds, the complete operation at 30 seconds.
No redirects, cookies, or arbitrary endpoints are enabled.

[GraphQL API-token authentication](https://developers.cloudflare.com/analytics/graphql-api/getting-started/authentication/api-token-auth/)
requires **Account > Account Analytics > Read** for the intended account.
An existing inference token can be used if it already has this permission; an
optional separate analytics token can be attached through the existing monitor
access flow. Separate monitor credentials take precedence, must select the same account,
and are never silently replaced with an inference token when absent/expired.
The `inference-key` monitor kind instead explicitly references the saved inference
credential: its monitor secret is null, its bearer comes from `HostQuotaSource`,
and the saved inference expiry remains enforced. No duplicate key entry is needed.
HTTP 401/403 directs the user to renew/review that access. Discovery and binding
remain in the shared cloud connection flow; this reader never guesses an account
from a token or switches to another accessible account.

Cloudflare has an official
[OAuth client registration flow](https://developers.cloudflare.com/fundamentals/oauth/create-an-oauth-client/)
and [OAuth integration endpoints](https://developers.cloudflare.com/fundamentals/oauth/integrate-with-cloudflare/).
The existing shared cloud OAuth module requires a genuinely registered public
client, approved callback and actual scopes from Cloudflare's scope catalog.
This implementation supplies no borrowed client ID, Wrangler scope assumptions,
client secret, browser cookie import, or speculative login. API-token read access
works independently of availability of a registered lain OAuth client.

## Numbers and day boundary

The reading retains source ID `cloudflare:workers-ai:adaptive-neurons`, unit
`neurons`, account pool `cloudflare-account:<id>`, and measurement kind
`usage-estimate`. UTC day start/end are explicit; the reporting window ends at
00:00 UTC regardless of local timezone/DST. Observation time is when lain reads;
`DataAsOf` and `FreshUntil` remain unknown because the response does not establish
them. At precisely midnight an empty interval yields unknown, not zero.

[Workers AI pricing](https://developers.cloudflare.com/workers-ai/platform/pricing/)
documents the free daily allocation of 10,000 neurons and reset at 00:00 UTC.
[Adaptive sampling](https://developers.cloudflare.com/analytics/graphql-api/sampling/)
means this aggregate is not automatically an exact billable-consumption total.
Consequently the reading does **not** create a 10,000-neuron denominator, remaining
allowance, enforced-quota reset, or exhaustion indicator. `WindowEnd` is the UTC
reporting boundary; `ResetsAt` stays unknown. Workers Free, Workers Paid,
Gateway/prepaid-credit coverage, and particular model billing routes cannot be
inferred from the presence of this dataset. A confirmed plan and authoritative
compatible usage are required before adding a free-quota balance.

## Qualification

Synthetic coverage is in `tests/cloudflare-quota/Program.cs`: default activation,
fixed endpoint/account/time filter, same-account binding, token precedence,
expiry, zero/missing/invalid values, grouped data, cross-account responses,
partial errors, schema discovery, native units, UTC/DST boundaries, and safe
permission diagnostics. Tests use only fixture tokens and injected HTTP handlers.
Build/test execution is coordinated by the integrator.

The prior `google-cloudflare` research sheet also records a historical official
Cloudflare dataset reference, a third-party schema mirror exposing
`AccountAiInferenceAdaptiveGroupsSum.totalNeurons`, and the flareclerk usage query.
These are the candidate
[source chain](https://pages.johnspurlock.com/graphql-schema-docs/cloudflare.html),
not a replacement for an actual account response. The restricted Billing Usage
v2 endpoint is not used without a qualified Workers AI metric identity.

The worker did not read real credentials, invoke live provider APIs, change login
state, or run inference. Public documentation fetching on 25 October failed with
DNS/connector transport errors; source links and previously recorded documented
semantics were reviewed locally. No fresh official confirmation of the candidate
field or successful live account read is claimed. The integrated runtime can now
attempt the bounded source using already authorized access and report its actual
result instead of stopping at a global qualification flag. Live success, real
entitlements, sampling/coverage and registered OAuth remain separate checks.
