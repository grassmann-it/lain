# Google Monitoring: existing snapshots and browser connections

Implementation based on `9b1cd4b`. No live provider API calls, authentication,
client registration, user-state reads or existing-account mutation were performed.
Public documentation was read unauthenticated on 2026-10-25.

## Usable data path

`GeminiCloudQuotaAdapter` now sends both owned OAuth projections and existing
Monitoring access-token snapshots through the model reader. A snapshot still
needs its expiry and the actual Gemini project ID. An inference API key cannot
authorize Google Monitoring, and no replacement API key is created.

For a snapshot without a project number, `GET
https://cloudresourcemanager.googleapis.com/v3/projects/{projectId}` verifies the
selected active project and obtains its immutable number. A denied optional read
leaves project-ID-labelled metrics and generic API usage available; it does not
silently accept numeric metric identities from other projects. `resourcemanager.projects.get`
is needed for that identity read. Snapshot values are never persisted or rotated
by a quota adapter. Existing account/credential IDs and inference keys stay intact.

Four documented Gemini model metric families are read under the project's
Monitoring API. In addition to DELTA `/usage`, their GAUGE `/limit` series now
provide **actual model limits**, using the same Monitoring access and
`monitoring.timeSeries.list` permission. The reader picks the newest valid sample
within ten minutes; it never sums gauges. Foreign projects, conflicting identical
timestamps, malformed/stale data and non-GAUGE values are excluded. Model,
`limit_name`, resource location and all source labels remain part of identity.
`DataAsOf` is the provider sample timestamp. A genuine zero limit is retained.

Cloud Quotas is an optional fallback when Monitoring supplies no model limits.
Its additional permission and API are no longer required to see limits already
reported by Monitoring. Generic `serviceruntime` request counts remain a fallback
when no model usage is available. Source identities remain distinct.

Usage remains reported partial usage since Pacific midnight; effective limits
remain separate observations. The documented reporting delay is up to 150 seconds.
An RPM limit plus delayed daily DELTAs cannot establish exact current-minute
remaining capacity. No missing series is turned into zero, no global balance is
created, and no unknown reset time or percentage is invented.

## Browser prerequisite and implemented lifecycle

Google requires an **application-owned OAuth Desktop client registration**.
This repository contains no provider-issued lain client ID. Neither Google CLI
client IDs nor other applications' identities may substitute for that registration.
The API key cannot be converted into a user authorization grant.

The concrete maintainer action is to create a Desktop OAuth client for lain in
[Google Auth Platform → Clients](https://console.cloud.google.com/auth/clients),
configure the consent audience/scopes, then place its public registration in the
existing `<lain state root>/maintainer/cloud-oauth.json`:

```json
{
  "clients": [
    {
      "provider": "gemini-api",
      "clientId": "REPLACE_WITH_LAIN_CLIENT_ID.apps.googleusercontent.com",
      "scopes": ["https://www.googleapis.com/auth/cloud-platform"],
      "qualified": false
    }
  ]
}
```

This is a schema example, not a usable client identity. Registration is external
maintainer work. Set `qualified: true` only after the owned Desktop registration,
consent, random-port loopback, refresh and required resource access have been
qualified. Restart the host after installing its registration; the same adapter
is already registered as login adapter and credential owner. Production consent
is needed for a public release: external Testing grants can expire after seven
days. `cloud-platform` is a broad grant, not a read-only OAuth scope; actual IAM
remains independent and the implementation only performs the specified reads.

The official desktop documentation marks `client_secret` optional and explains
that installed apps are public clients. The implementation distributes no secret.
It already supports system-browser authorization, S256 PKCE, state validation,
one-use code exchange, offline refresh grants, private durable checkpoint before
project discovery, resource selection and owner-only refresh. Owned refresh values
never enter quota DTOs or generic snapshots. Reconnect now verifies the exact pinned
project with `projects.get`; it does not enumerate unrelated projects or silently
switch the selected resource. A new connection still uses `projects:search` and
the existing project picker.

## Verification

`tests/cloud-connections/Cloud.Connections.Tests.csproj` extends synthetic HTTP
and isolated-loopback coverage for snapshot model usage/limits, latest-versus-sum
gauges, explicit zero, stale/conflicting/foreign limits, optional project IAM,
expired and mismatched snapshots, and exact-resource reconnect. The runner passed
**77 assertions**, with a successful linux-x64 self-contained build. Execution used
the integrator's sole heavy-test slot, capped at 1.5 CPU / 2 GiB RAM / 256 MiB swap,
low I/O priority and SSD TMPDIR. Locked restore produced no lockfile drift. The
initial restore reported unavailable NuGet audit data; a cached-source restore
with audit disabled and the same build/test completed with zero warnings/errors.
Logs are in the private qualification cache under `google-quota/test.log` and
`google-quota/test-retry.log`. Local fixtures do not establish real consent,
real project entitlement or source coverage.

Official references:

- https://developers.google.com/identity/protocols/oauth2/native-app
- https://docs.cloud.google.com/monitoring/api/metrics_gcp_d_h
- https://docs.cloud.google.com/monitoring/api/ref_v3/rest/v3/projects.timeSeries/list
- https://docs.cloud.google.com/resource-manager/reference/rest/v3/projects/get
- https://docs.cloud.google.com/resource-manager/reference/rest/v3/projects/search
- https://docs.cloud.google.com/docs/quotas/reference/rest/v1/projects.locations.services.quotaInfos/list
