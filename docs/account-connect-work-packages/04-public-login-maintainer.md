# AC-04 public browser login implementation

OpenRouter uses `OpenRouterLoginAdapter`, method `browser`, with no Core or client
registration. The adapter binds a fresh S256 verifier/state to a bounded IPv4
loopback callback and exchanges once at `https://openrouter.ai/api/v1/auth/keys`.
The prepared host-owned API key uses `https://openrouter.ai/api/v1`; it may serve
inference and own-key monitoring. Requested purposes limit lain’s enabled
capabilities; a monitor-only choice does not make the issued key read-only or
remove its provider-side spending permissions. The
adapter checkpoints the issued key through the shared operation service before
any further network call, then reads only `GET /api/v1/key` after issuance, to confirm any returned
`creator_user_id`, `workspace_id` and `expires_at`. A requested workspace is sent
as `required_workspace_id`; a previously confirmed user/workspace must also match
the returned metadata. Missing metadata does not invent a verified identity.
A new key can connect with unknown identity; an unconfirmed reconnect result is
retained for explicit recovery. No refresh, management-key creation, inference,
model routes, browser cookies or revocation are performed.

Poe uses `PoeLoginAdapter`, method `browser`, with the existing `PoeOAuthAttempt`
and `apikey:create` scope. **The shipped default is unavailable.** The method must
be omitted from the usable catalog until the release maintainer has registered
lain's public client and qualified the actual release callback. Always offer
“Use an existing API key” with `https://poe.com/api_key`; do not ask the end user
to create an OAuth client. Default inference base is `https://api.poe.com/v1`.

The integrator may load a package-owned JSON file using
`PoeLoginOptions.LoadMaintainerConfiguration(lainHome)` and pass the result to
`PoeLoginAdapter`. This reads only `<LAIN_HOME>/maintainer/poe-login.json`; the
state directory, maintainer directory and file must not be symbolic links.
The file is public configuration, with this schema:

```json
{
  "clientId": "MAINTAINER_REGISTERED_PUBLIC_CLIENT_ID",
  "callbackUri": "http://127.0.0.1:18419/lain/poe/callback",
  "registrationQualified": false
}
```

The placeholder is documentation only; no usable client ships in source. Before
setting `registrationQualified` to true, register the actual application through
`https://poe.com/api/clients`, confirm its permitted release use, and qualify the
exact callback host, fixed port and path with the provider. The implementation
accepts HTTP `127.0.0.1` or `localhost`, a nonprivileged explicit port and a
non-root path; it binds only IPv4 loopback. A `localhost` registration must be
qualified for IPv4, including browser resolution on each supported platform.
A busy registered port produces a setup error instead of choosing a different
unregistered callback. Client secrets are neither required nor accepted.
No release registration or real provider login was performed here.

Poe returns an API key and optional expiry, with no proven stable user ID. The
adapter returns `Provenance=unverified`; it never deduplicates by display name.
Reconnect targets the explicit existing lain account, preserving its identity;
a previously confirmed provider identity cannot be silently claimed by this flow.

Both adapters expose safe waiting/failed/cancelled/prepared/recovery-required
results to the shared `LoginOperationService`; they never write accounts or
secrets. An exchange is attempted once. Once sent, its bounded response handling
continues across caller cancellation, allowing the shared service to preserve a
late key before refusing publication. An uncertain/lost/malformed issuance result
requires inspecting the provider key list before an explicit new bootstrap.
There is no automatic retry or invented recovery of a lost one-time key. A
received key always has `PreserveForRecovery=true`; local publication, account
revision/generation checks, disconnect and durable recovery remain AC-01-owned.
After terminal adoption/recovery, the shared service calls `ReleaseAsync` to
remove the adapter’s in-memory attempt. Callback listeners expire with the operation, close on acceptance/cancel/timeout,
reject wrong state/path/duplicate query parameters, and accept at most one code.

`tests/public-login` links the production adapter sources against the frozen
AC-01 adapter seam. It exercises actual local HTTP callbacks with fake provider
HTTP; it does not open a browser, use keys, contact an authentication endpoint,
or prove real provider callback/client compatibility. Run:

```sh
dotnet run --project tests/public-login/PublicLogin.Tests.csproj
```

The root Host build and shared lifecycle tests are required integration checks.
A separately authorized real browser consent plus read-only quota check remains
a different qualification step; no model prompt is necessary.
