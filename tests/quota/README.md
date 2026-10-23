# Quota synthetic verification

Run `bash tests/quota/run.sh`. The harness builds linked production sources in
an ephemeral `/tmp` project without changing repository projects, installing
packages, or reading credentials. All HTTP calls terminate in fake handlers.

## Host integration

`QuotaService` accepts `IQuotaAccountSource`, `IQuotaCredentialSource`, and the
Codex/Command Code adapters. Account identity, enabled state, explicit pool
identity and `SharedWithLegacy` come from account management. Credentials can be
resolved using `QuotaCredential.FromSecretJson(secretStore.ReadAccount(id))` on
the normalized private W3 store. Never point the adapter at a legacy auth path.
The normalized Codex kind may be `codex-access-snapshot` or `codex-owned`;
both expose only `accessToken`, `accountId` and `expiresAt` to quota. For owned
logins SecretStore hydrates current access material from the explicitly owned
core file. Refresh ownership remains with that core; quota neither reads nor
uses refresh/ID tokens, and missing access material stays unavailable.

Use an `HttpClientHandler` with `AllowAutoRedirect = false` for the provider
clients. Endpoints are fixed HTTPS URLs; 3xx responses must fail instead of
following a redirect with account headers. Responses are capped at 1 MiB and
each HTTP read has a 20-second cancellation timeout. No request retries,
inference requests, OAuth refresh, or revoke endpoints exist in these adapters.

Expose `GetSnapshotsAsync(ct)` as GET `/api/v1/quotas` and
`RefreshAsync(accountId, ct)` as POST `/api/v1/quotas/refresh`; both return
`QuotaDto[]`. Run `RunAsync(hostStoppingToken)` in the host background lifetime:
it refreshes immediately, then every five minutes. An explicit refresh can
target one account. Cancellation restores its prior snapshot and propagates.

`Source` contains the actual endpoint plus a stable fragment identifying the
window, because the v1 DTO has no separate window ID. Multiple rows per account
are normal. Row states are `unknown`, `ok`, `refreshing`, `stale`, `error`, and
`disabled`. `ObservedAt` is the latest attempt/observation; `LastSuccessAt` is
the last observation with a usable quantity for that row. A partial response
preserves missing old windows as stale while exposing fresh windows. Snapshot
cache is in memory and starts unknown after host restart.

Never sum account credits, rolling windows, org limits or model allowance rows.
These are overlapping constraints and different balances. A provider name is
not evidence of a shared pool; only an explicit account pool or a returned
Command Code organization/model identity establishes one. Organization rows do
not inherit an unrelated account pool. Shared legacy consumption is disclosed
through `SharedWithLegacy`, not subtracted from local usage estimates.

## Reference boundaries

The existing `~/.local/share/modelcoach/provider_status.py` was read as a code
reference only. Codex percentages stay percentages, not tokens. Command Code
account plan ceilings follow that reference's official CLI account plan table
(2026-10-17); an unrecognized or inactive plan leaves total and used unknown.
Purchased/bonus balances and rolling windows are separate. Per-model monthly
remaining quota stays unknown; documented $100/$150 ceilings are not substituted
for available budget. The usage-summary spend endpoint is unnecessary for quota
and is not called. No live provider compatibility claim follows from these
synthetic tests; integration must qualify separately provisioned credentials.
