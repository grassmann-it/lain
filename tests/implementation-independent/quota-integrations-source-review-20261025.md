# Quota integration source review, 2026-10-25

Reviewed Main `5c6dfc7c1178a64b83ec4f852c1eb5b1662d9ec6` and the in-progress
`20261025/google-quota` worktree's `GoogleGcloudLoginAdapter.cs` and
`GoogleGcloudProcess.cs`. Source only: no builds, provider calls, browser sessions,
services, private credentials, or runtime qualification. Line numbers below refer
to the inspected source; subsequent integration fixes require separate review.

## Findings sent to integration

1. **Cloudflare existing-key option always fails setup.**
   `src/Lain.Host/Quota/CloudflareQuotaAdapter.cs:31` rejects any non-null Monitor
   with a null/empty Secret, including `inference-key`. The offered option at
   `Accounts/Monitoring/MonitorAccessCatalog.cs:42` uses precisely that shape;
   `Quota/HostQuotaSource.cs:31-36` correctly projects its inference BearerToken.
   Restrict the missing-secret check to separate-token kinds and explicitly select
   the token by kind. Cover the actual HostQuotaSource path with a fixture.

2. **MiMo browser identity can change between identity and usage requests.**
   `Quota/MiMoQuotaAdapter.cs:36-45` and
   `Accounts/ConsoleConnections/MiMo/MiMoConsoleLoginAdapter.cs:40-55` verify the
   user profile before fetching usage. The interactive browser remains open;
   signing into another user between these requests can label that user's usage
   with the first user's identity. Local generation checks do not detect a browser
   login switch. Re-read and compare the profile after usage/detail before
   returning or preparing; reject the entire result on a mismatch. Test an
   A-profile / B-usage / B-profile sequence. Per-request browser serialization is
   not an identity transaction.

3. **gcloud private grant lifecycle needs integration cleanup.**
   `Accounts/Cloud/GoogleGcloudLoginAdapter.cs:212-234` cancels/releases processes
   and in-memory attempts, but never removes the private profile. Reconnect
   allocates another profile (`:74-76`), leaving previous SDK refresh credentials.
   Preserving an unpublished grant for recovery is legitimate; successful
   replacement and explicit account/monitor removal need an owned, generation-
   aware cleanup path. No such method exists in the inspected adapter. This is
   separate from Root's already acknowledged MiMo cleanup wiring.

## Qualification limits and lower-priority observations

- Cloudflare returns `usage-estimate`, measured Used only, and no Limit, Remaining,
  DataAsOf, or FreshUntil (`CloudflareQuotaAdapter.cs:69-74`). The generic
  `QuotaService.cs:185` still labels a successful row `ok` for the polling interval
  when FreshUntil is absent. Treat this as fetch/cache status, not evidence of
  provider data freshness; the row itself explicitly says freshness unconfirmed.
- gcloud URL validation checks the Google origin/path, state length, S256 shape,
  and localhost callback (`GoogleGcloudProcess.cs:104-123`) but does not inspect
  client_id or scope values. Its trusted source is the fixed, pinned official SDK,
  not an independent strict scope validator. Public installed SDK source shows
  default openid, userinfo.email, cloud-platform, appengine.admin, sqlservice.login,
  compute, plus accounts.reauth. Describe the grant as the normal broad gcloud
  grant, not read-only Monitoring authorization. Exact scope validation would
  strengthen the bridge boundary.

## Confirmed source properties

- MiMo normal quota reads never call OpenAsync. Browser launch is explicit Begin;
  it uses a random owned profile, fixed same-origin GET routes, bounded CDP
  responses, and no cookie extraction or borrowed/shared browser profile.
  Credential selection and post-read local generation checks are present.
- Cloudflare queries one exact account and a UTC-day account-wide aggregate;
  absent/grouped/error data does not become zero. Exceptions use fixed safe text.
- Groq Enterprise is explicit opt-in; missing direct access keeps passive reads.
  Project selection is validated by MonitorAccessService. Metrics retain paired
  underlying sample timestamps and model/status identity, report rates, and do not
  manufacture balances or remaining allowances. Partial failures preserve old
  omitted rows through the composite/service path.
- Foundation selects the saved quota purpose and checks account/credential
  versions before retaining results and again before projection. Transport
  recovery stays within saved same-key official transports. Safe diagnostic
  categories avoid provider response bodies and arbitrary exception messages.
- gcloud clears inherited environment, sets an owned CLOUDSDK_CONFIG, launches
  fixed CLI commands without a shell, and owns a bounded browser bridge/process.
  Refresh pins CLI account plus verified userinfo subject/email and project
  settings; SDK retains refresh ownership. No shared config or ADC import path
  was found in the inspected code. Real consent/entitlement was not tested.

## Fix follow-up: Main `2c4bf7b`

Source and fixture inspection only; no test execution.

- **Finding 1 closed by `87f745f`.** `CloudflareQuotaAdapter.cs:27-38`
  explicitly selects the existing BearerToken for no-monitor/inference-key and
  the separate Secret otherwise. It rejects an unexpected second secret on an
  inference-key reference, applies the saved inference expiry, and cannot fall
  back from an absent separate token. `tests/cloudflare-quota/Program.cs:43-73`
  exercises actual `MonitorAccessService.Set` → `HostQuotaSource` → adapter
  selection with synthetic private state and checks the outgoing token and
  measured result. Lines 86-109 cover missing separate tokens, unexpected secret,
  expired inference references, and independent monitor expiry behavior.
- **Finding 2 closed for the reported switch by `91cdf99`.** Login rechecks
  identity after usage before preparing a credential
  (`MiMoConsoleLoginAdapter.cs:50-58`). Quota rechecks after usage/detail, including
  optional-detail failure (`MiMoQuotaAdapter.cs:39-56`), before returning rows.
  The fixtures at `tests/console-connections/MiMoConnectionChecks.cs:43-57,81-87`
  cover switches during usage, detail and detail failure, the final request
  order, and no private generation/publication after a login switch. The browser
  remains explicit-only; the follow-up adds no OpenAsync to quota reads.
- Missing/closed/expired MiMo sessions now produce setup-required without a
  synthetic provider error (`MiMoQuotaAdapter.cs:25,37,53,59-68`); these paths
  return no new measured rows. QuotaService retains any prior rows as stale
  through unavailable support, preserving their original observation times.
- No new material defect found in these two fixes. This is not an atomic
  provider-side identity transaction: repeated A→B→A switching entirely between
  identity reads remains outside the demonstrated guarantee. The ordinary
  one-way login-switch failure reported above is covered.
- **Finding 3 remains pending** the separately developed gcloud profile
  retention/cleanup implementation. Root also owns MiMo cleanup integration;
  these two reader fixes do not establish that lifecycle wiring.
- Root reports an exact seven-scope gcloud validator and broad-grant UI copy in
  progress. At the follow-up read, the named google-quota worktree still exposed
  the earlier validator and `scope=openid` fixture, so that change is **not yet
  independently inspected**. Public SDK `auth/login.py:GetScopes` confirms the
  default six scopes plus accounts.reauth and optional explicit Drive addition;
  normal auth login does not expose a generic `--scopes` option. No recommendation
  to pass an unsupported flag is implied by this review.
- Foundation's retained-retry follow-up is separate and not claimed reviewed here.
