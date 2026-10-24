# Private monitor access

Source review: 24 October 2026. No user credentials, authenticated provider
requests, client registrations, logins or service-account provisioning were used.
This describes authentication capabilities independently of whether a quota or
usage adapter has a qualified data source.

## Provider decisions

| Provider | Smallest evidenced access | Resource identity and limits |
| --- | --- | --- |
| Groq | Existing inference API key for passive response headers. | Preserve the account/request attribution. No inference request solely for monitoring. Organization/project limits can overlap. |
| Fireworks | Existing Fireworks API key, Bearer authentication, for List/Get Quotas. A separate key is optional when the existing key lacks access. | Explicit Fireworks account ID; successful inference does not prove quota-read permission. The public role matrix does not name the minimum List/Get Quotas role. |
| Gemini Developer API | Separate short-lived Google access token for Cloud Monitoring; prefer `https://www.googleapis.com/auth/monitoring.read`. | Explicit Google project ID for the inference project, Monitoring API enabled, and IAM `monitoring.timeSeries.list` (e.g. Monitoring Viewer). A Gemini API key or userinfo scope is insufficient evidence. Project usage alone is not remaining model quota. |
| Cloudflare Workers AI | Existing API token if it already includes Account → Account Analytics → Read for the same account; otherwise an optional separate token with that read permission. | Explicit Cloudflare account ID matching the inference route. Auth is documented; a suitable Workers AI usage/neurons dataset must be qualified independently. No invented remaining allowance. |
| NVIDIA Build | Existing NVIDIA API Catalog key grants inference access. No suitable official Build budget-reading access was found in the reviewed sources. | Build account console remains the next step. NGC enterprise/Cloud Functions roles, self-hosted NIM metrics and registry access are different capabilities. |

[Groq rate headers](https://console.groq.com/docs/rate-limits) need no additional
credential. Its separate [Prometheus interface](https://console.groq.com/docs/prometheus-metrics)
uses the Groq API key but is explicitly Enterprise-only, not a free-account
capability. No official third-party desktop OAuth for Groq quota reporting was
qualified in the reviewed documentation.

Fireworks documents Bearer API-key auth on [List Quotas](https://docs.fireworks.ai/api-reference/list-quotas)
and [Get Quota](https://docs.fireworks.ai/api-reference/get-quota). Its
[user roles](https://docs.fireworks.ai/accounts/users) do not settle the minimum
quota-read role. A 403 needs an explicit permissions explanation; it is not a
zero balance. No supported native quota-reporting OAuth was found. Key entry:
[Fireworks API keys](https://app.fireworks.ai/settings/users/api-keys).

Google [timeSeries.list](https://docs.cloud.google.com/monitoring/api/ref_v3/rest/v3/projects.timeSeries/list)
accepts `monitoring.read`, `monitoring`, or `cloud-platform`; the first is the
narrow read-only choice. [Monitoring IAM](https://docs.cloud.google.com/monitoring/access-control)
is an independent requirement. Project identity is user-supplied and must not be
inferred from token text or a successful Google login. The API accepts project
numbers too, but the current reader requires a textual project ID to match
`consumed_api` response labels without an extra identity-resolution API.

Cloudflare's [Analytics token guide](https://developers.cloudflare.com/analytics/graphql-api/getting-started/authentication/api-token-auth/)
documents Account Analytics Read and resource restriction. An inference token
without that permission is not an analytics credential. Use the dashboard's
[API tokens](https://dash.cloudflare.com/profile/api-tokens) only when needed;
no global API key or additional account is required by this design.

NVIDIA's [API Catalog quickstart](https://docs.api.nvidia.com/nim/docs/api-quickstart)
and [NIM FAQ](https://forums.developer.nvidia.com/t/nvidia-nim-faq/300317) describe
inference and the Build console. [NGC key/role documentation](https://docs.nvidia.com/ngc/latest/ngc-user-guide.html)
distinguishes Public API Endpoints, registry and Cloud Functions permissions.
Neither a service key nor NGC's legacy API-key token exchange establishes a
public desktop OAuth flow or a Build budget endpoint. This is a qualified-source
gap, not a claim that no such provider capability can ever exist.

## Official OAuth and remaining implementation

**Google supports desktop OAuth.** The [native-app guide](https://developers.google.com/identity/protocols/oauth2/native-app)
requires a registered Desktop app client and system browser. It documents PKCE,
state binding, loopback callback, code exchange, expiry and refresh; client secrets
are optional for the documented public flow and must not be bundled. An owned
implementation must validate actual granted scopes, bind callbacks to a specific
account and operation, expire/cancel listeners, serialize refreshes, preserve the
refresh token when omitted from a response, and reconnect on invalid grants.
User-supplied external access-token snapshots are a separate capability and
must never trigger refresh or revocation.

The [Google OAuth overview](https://developers.google.com/identity/protocols/oauth2)
explains revocation/expiry and the seven-day refresh expiry for external projects
in Testing with scopes beyond basic identity. [OAuth policies](https://developers.google.com/identity/protocols/oauth2/policies)
and [production readiness](https://developers.google.com/identity/protocols/oauth2/production-readiness/policy-compliance)
require an appropriate registration, accurate identity, minimal scopes and
applicable verification/privacy obligations. No commercial-distribution approval
for lain is implied by technical protocol support. No registered lain Google
client or production qualification is supplied in this package; an owned browser
login/refresh implementation remains missing, not provider-unsupported.

**Cloudflare supports desktop/CLI OAuth.** Its [client registration guide](https://developers.cloudflare.com/fundamentals/oauth/create-an-oauth-client/)
requires Authorization Code with S256 PKCE and token authentication `none` for
public clients. Registration requires an authorized account role. Public client
visibility additionally requires verified client-URL domain ownership. API-token
permission names map to OAuth scopes, but the exact available analytics scope
must be obtained from the official scope list, not guessed from Wrangler scopes.

[Integration endpoints](https://developers.cloudflare.com/fundamentals/oauth/integrate-with-cloudflare/)
are under `https://dash.cloudflare.com/oauth2/` (`auth`, `token`, `revoke`, `logout`).
The [client API](https://developers.cloudflare.com/api/resources/iam/subresources/oauth_clients/methods/create/)
allows the optional `refresh_token` grant and explicit redirect URIs. The reviewed
pages do not settle acceptable desktop loopback registration, refresh rotation,
or token lifetime. No registered lain client is provided. Therefore no speculative
Cloudflare OAuth module is enabled; token-based analytics auth is available in
principle, while the reporting dataset and OAuth qualification remain separate.

## Implemented local capability

`MonitorAccessService` attaches setup to an existing stable account ID through
revision-guarded Set/Remove operations. `Get` returns secret-free metadata;
`GetCredential` is a private projection for the host's quota source only. Main
maps GET/PUT/DELETE `/api/v1/accounts/{id}/monitor` within the existing authenticated
local management boundary and enriches provider setup with
`MonitorAccessCatalog.Options(providerId)`.

The separate `credentials/monitors/{accountId}.json` file is private (0700 parent,
0600 file on Unix), atomically replaced, and bound to both account ID and provider.
Legacy account import and inference-key replacement do not write it. Selecting
`inference-key` stores no duplicate token. Explicit monitor kinds and resource
identities are validated before saving; Cloudflare's resource must match the
inference account, and an already recorded Fireworks account ID must agree.
Without a recorded Fireworks ID, the user-supplied ID remains unverified. Google
project ownership cannot be inferred from opaque token contents.

Saving makes no provider request and leaves account names, routes and inference
state intact. Status distinguishes `not-configured`, `configured-unverified`,
`expired`, `invalid`, and unsupported additional credentials. API permissions are
verified only by the relevant adapter's eventual read. External snapshots contain
no refresh token or renewal authority. Removing monitor access deletes only the
local file and never revokes an externally owned token. Reconnect means entering
a replacement credential locally. The separate capability is not exported to the
inference proxy.

Package status: locally checked in the Main-assigned slot on 24 October 2026.
`tests/monitor-auth/run.sh` passed 58 synthetic checks with zero build warnings
and errors. Its temporary project and state honor `TMPDIR`; the accepted rerun
used the worktree `artifacts/tmp` directory. No installed or live-qualified
monitor access is claimed. Main still verifies the integrated host endpoints.
