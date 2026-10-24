# AC-06 cloud adapters: local implementation and qualification boundary

Source package prepared 2026-10-24. Local test status at this commit: **not_run**;
all .NET builds and isolated callback tests are serialized by the integrator.
No live project/account read, OAuth login, client registration, real credential,
inference, resource creation, installation or service operation was performed.

## Implemented boundary

- `CloudResourceDiscovery` uses Google `projects:search` and Cloudflare account
  listing with fixed endpoints, page/resource/response/time bounds. It rejects
  ambiguous project numbers and incomplete Cloudflare pagination. Google binding
  uses `ResourceType=project`, `ResourceId=projectId`, `Subject=projectNumber`;
  Subject here is the immutable resource number, **not** the browser user. Its
  private settings repeat `projectId`/`projectNumber`. Cloudflare binds both
  Subject and ResourceId to the exact account ID, with `settings.accountId`.
- `CloudOAuthAdapter` implements the existing account adapter and credential-owner
  interfaces. It reuses the owned loopback listener, S256, state, one-use exchange,
  cancellation and cleanup. Only `quota` purpose is supported. Issued grants are
  checkpointed privately with empty purposes before discovery; no preliminary
  grant may be adopted/published. The resource picker produces the final bound
  generation. Reconnect cannot change the pinned resource. Refresh retains a
  returned rotated grant even when narrowed scopes remove its publication rights.
- `CloudProviderHttp` bounds each read to 20 seconds/1 MiB and disables redirects
  and cookies. Discovery has a 30-second total deadline; Google reporting 60 seconds.
  Errors contain fixed permission/authentication/rate-limit actions, not bodies.
- `GeminiModelQuotaAdapter` reads four documented, non-internal model metric
  families plus optional Cloud Quotas effective limits. It keeps model, method,
  location and limit identity separate, rejects conflicting/overlapping DELTAs,
  deduplicates replayed samples and rejects foreign projects. Query days use
  Pacific midnight with DST. Reported usage may have gaps and 150-second delay;
  its sample end is DataAsOf, never its fetch time. Missing series remain unknown.
- Effective limits retain their actual quota IDs/dimensions/units/refreshInterval
  and have separate source/window identities. They are **not joined** to usage:
  live `limit_name`/`quotaId`/dimension/window equivalence remains unqualified.
  No remaining value, percentage, minute reset or routing capacity is fabricated.
  Forbidden optional limits preserve separately successful usage.
- `CloudflareQuotaAdapter` is gated off by default. Its candidate query requests
  one account aggregate without model dimensions, not a truncated top-N sum.
  Reported neurons are `usage-estimate`; GraphQL errors, wrong accounts, empty or
  multiple aggregates yield no numeric usage. No 10,000-neuron denominator,
  remaining balance, source timestamp or freshness deadline is invented.

Existing Gemini snapshot monitoring remains unchanged. Register one
`GeminiCloudQuotaAdapter(providerHttp)`: verified provider-discovered project ID
and number select the new model reader, while existing snapshots retain the
legacy reader. QuotaService takes only the first provider match, so registering
both leaf adapters does not combine their results. The new reader must consume
the foundation's access-only projection.
No quota adapter receives or refreshes a refresh token.

## Maintainer prerequisites and integration

Construct Google with `new CloudOAuthAdapter(new CloudOAuthClient("gemini-api",
<owned Desktop client ID>, [CloudOAuthAdapter.GoogleCloudScope], Qualified: true))`
only after its own registration, loopback redirect, full initial consent,
production/scopes and refresh behavior are qualified. `Qualified` defaults false;
there is no distributed client secret, borrowed client ID or user client-ID prompt.
The broader cloud-platform scope is required for Cloud Quotas/discovery and is
not a read-only grant. Actual resource IAM remains independent. Google external
Testing clients may lose refresh grants after seven days. No incremental-consent
promise, project provisioning or inference OAuth is implied.

Construct Cloudflare using provider `cloudflare-workers-ai`, its **own** client ID,
actual scope-catalog IDs including `offline_access`, and an exact nonprivileged
HTTP localhost/127.0.0.1 callback registered for that client. Qualification must
cover public publisher prerequisites, endpoint acceptance, account discovery,
refresh/rotation and granted scope mapping. No Workers AI scope IDs are guessed
or Wrangler scopes copied. Until those prerequisites hold, retain `Qualified:false`
and catalog availability `maintainer-setup-required` (not available or impossible).
Register the same adapter object as `IAccountLoginAdapter` and `ICredentialOwner`;
owners are `google-oauth` and `cloudflare-oauth`.

Foundation integration agreed: these owner IDs are rotating restore-quarantined
owners. `cloud-oauth` JSON contains accessToken/refreshToken/clientId/scopes/expiry
and string settings. Only accessToken/settings reach quota readers. Monitoring
projection maps Google to access-token-snapshot and Cloudflare to api-token, using
handle resource/expiry. Recoverable empty-purpose grants can be stored but cannot
be published by CommitPrepared or adopted through recovery. Root owns registry,
Program, credential projection and method-catalog integration.

`CloudMaintainerConfiguration.Load(Path.Combine(stateRoot,
CloudMaintainerConfiguration.RelativePath))` reads the optional
`<own state root>/maintainer/cloud-oauth.json`. Missing file returns no clients
and a disabled neuron candidate. Unknown fields (including secrets/tokens),
symlinks, oversized files and duplicate providers are rejected. No file is
created by this package. `clients` contains the public CloudOAuthClient fields;
`cloudflareNeuronCandidateQualified` defaults false. Root may load this only as
explicit maintainer configuration; there is no registered client in this delivery.

`new GeminiModelQuotaAdapter()` is the documented leaf source reader; it is not a live
qualification claim. `new CloudflareQuotaAdapter()` returns explicit unavailable
support without any HTTP call. Only a maintainer's separately qualified dataset
configuration may pass `candidateQualified:true`. Fresh introspection, actual
account entitlement, sampling/confidence, REST/Bindings/Gateway/Unified-Billing
coverage and freshness remain mandatory source qualification. Billing Usage v2
Alpha/Restricted is not treated as a universal fallback.

## Evidence and remaining checks

The synthetic runner is `tests/cloud-connections/Cloud.Connections.Tests.csproj`.
It covers two Google identities, bounded pagination, safe 403, Pacific DST,
unknown/zero/duplicate/overlapping/delayed samples, resource isolation, separate
limits, missing optional permission, gated Cloudflare and partial GraphQL,
PKCE/loopback/resource selection/replay/cancel, checkpoint-before-discovery and
narrowed rotating grants. These local tests cannot establish provider consent,
real refresh semantics, public scope availability or actual dataset coverage.

Public documentation reread 2026-10-24 (no authenticated requests):

- https://developers.cloudflare.com/fundamentals/oauth/integrate-with-cloudflare/
  confirms authorization/token endpoints under `dash.cloudflare.com/oauth2`.
- https://docs.cloud.google.com/resource-manager/reference/rest/v3/projects/search
  confirms GET discovery, project permission and pagination.
- https://docs.cloud.google.com/docs/quotas/reference/rest/v1/projects.locations.services.quotaInfos
  confirms effective limits, metricUnit, refreshInterval and dimension precedence.
- Existing research and metric/source boundaries:
  [Google/Cloudflare research](../../quota-auth-research/google-cloudflare.md) and
  [AC-06 acceptance](../../account-connect-work-packages/06-cloud-oauth.md).
