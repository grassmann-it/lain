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

## Final backend follow-up: Main `fee826a`

Inspected source and synthetic fixtures for Google `c82b2d0`/`4361fb1`/`36a31d6`,
Root integration `32d390e`/`fee826a`, Foundation `2e0b3f3`/`9d27f92`, and MiMo
`25ced79`. No build, service, browser login, provider call, or real credential read
was performed. Public installed SDK source was inspected only as code.

### New actionable finding

**Google removed-target cleanup leaks an adapter operation slot and can report
unfinished cleanup as complete.** `GoogleGcloudLoginAdapter.cs:302-330` cancels
and may delete its owned profile, but never removes the operation from `attempts`
or disposes its CTS. `LoginOperationService.CleanupRemovedTargetAsync` marks a
normally returning hook `TargetCleanupCompleted=true` and `Released=true`, so
the ordinary `ReleaseAsync` removal at `GoogleGcloudLoginAdapter.cs:344-348` does
not run. After eight such attempts, the global `attempts.Count >= 8` guard at
line 71 rejects subsequent gcloud connects until restart. The existing fixture
checks profile deletion once, not slot reuse.

The same hook returns normally when login remains active (line 306), and swallows
an IOException during profile deletion (line 328). That tells Foundation cleanup
succeeded although the comment promises a later cleanup pass. Preserve the retry
signal on busy/IO failure; only remove the adapter entry/dispose CTS after
confirmed local completion. Add repeated removed-target connects followed by
another Begin and a held-profile-lease retry fixture. No remote revocation is
needed. Finding sent to Root before this report update.

### Closed or strengthened boundaries

- Removed-account operations are explicitly invalidated. The durable
  `target-removed` state survives late prepared/publication writes, and Adopt
  rejects it before considering a recreated account. In-flight late results
  retain private evidence without regaining publication rights. The default
  adapter hook releases local bookkeeping without invoking cancellation/revoke;
  MiMo's specific hook closes only the owned profile. Fixtures cover waiting,
  late completion, already released recovery, same-ID recreation, and the default
  external-grant behavior (`tests/quota-host-integration/Program.cs:127-187`).
- Generic retry scheduling is fair by last attempt rather than repeatedly taking
  the same eight entries. One shared five-second cancellation budget covers the
  pass, including removed-target cleanup from middleware. The fixture places eight
  failures before a ninth healthy item and checks that the second pass reaches it
  (`:189-209`). This bounds waiting in cooperative callbacks, not synchronous
  filesystem stalls or callback code that ignores cancellation.
- MiMo shutdown now erases owned profiles. Startup scans a bounded receipt set,
  refuses unsafe paths/receipts, checks process start identity, preserves a live
  owner host, and closes stale owned sessions instead of resuming browser access
  (`MiMoConsoleBrowser.cs:161-220,234`). Fixtures cover stale receipts, PID reuse,
  live-owner recovery, links and enumeration bounds. Startup reconciliation is
  invoked before serving requests; runtime quota still never opens a browser.
- gcloud startup retention and disconnect run under the shared mutations gate
  (`Program.cs:103-115`). Retention includes all selected capability references
  and unpublished prepared Google recovery references. Unknown/malformed recovery,
  restore quarantine or exceeded scan bounds preserve grants. Owner receipts and
  private profile tree are validated, and an exclusive CLI lease protects deletion.
  Startup can retire an unreferenced profile without a previous in-memory AccountDto.
- gcloud is registered as login adapter and refresh owner. The selected quota
  handle callback requires owner `google-gcloud`; MonitorCredentialStore projects
  only its access token/resource/real handle expiry, and the catalog accurately
  labels the broad standard CLI grant. Refresh remains bound to the saved account,
  verified userinfo subject/email and project settings.
- The URL bridge now verifies the installed SDK client ID and the exact seven
  default scopes, including accounts.reauth and excluding Drive. The SDK owns
  OAuth/PKCE; lain only validates the emitted URL. Fixed commands do not use an
  unsupported `--scopes` flag. Environment clearing, private CLOUDSDK_CONFIG,
  disabled metadata checks, a freshly seeded false SDK GCE cache, and the explicit
  metrics environment prevent the inspected SDK paths from using ambient metadata.
  A source fixture denies non-loopback connects/DNS and checks the bridge and GCE
  cache; these are fixture definitions inspected here, not reviewer execution.

### Explicit remaining limits

- The generic retry dictionary still has no hard absolute cap. Failed references
  are intentionally not silently dropped; fairness/time budgeting does not bound
  accumulated records or persist that dictionary across restart.
- Removed-target invalidation covers known live/retained operation records.
  Previously pruned recovery archives are not globally searched and invalidated.
- Google retention deliberately defers deletion on uncertainty. Its normal
  disconnect wrapper swallows uncertainty and ignores the adapter's false/busy
  return, so the generic queue can consider that callback finished; startup
  reconciliation is the fallback, not a proven continuous retry mechanism for
  every deferred profile.
- MiMo's bounded startup reconciliation can leave unvisited or invalid receipts;
  the host currently discards the returned reconciliation counts. No full-state
  cleanup guarantee is inferred for malformed or over-bound profile trees.
- No live Google/MiMo consent, real account entitlement, provider availability or
  production login qualification is claimed. Existing measurement/freshness limits
  described earlier remain unchanged.
