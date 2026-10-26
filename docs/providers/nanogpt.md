# NanoGPT account connection and subscription monitoring

Contract checked against public official documentation on 26 October 2026.
The user's subscription exists; its tier, price, quotas and model coverage are
unknown. This implementation does not choose a commercial plan or perform
inference, a purchase, a top-up, or a paid fallback.

## Inference connection

[OAuth PKCE](https://docs.nano-gpt.com/api-reference/miscellaneous/oauth-pkce)
explicitly supports local public clients through its **Shortcut key handoff**.
Lain uses `GET https://nano-gpt.com/auth`, its own `client_name=lain`, an
operation-specific IPv4 loopback callback with an explicit ephemeral port,
random state and S256 PKCE, and `scope=api.use models.read`. NanoGPT creates or
reuses its internal callback client. This documented shortcut needs neither a
borrowed client ID nor a maintainer registration/admission gate. It shows consent
for loopback/shortcut clients. No referral is attached.

After the user approves, lain exchanges the one-time code once at
`POST https://nano-gpt.com/api/v1/auth/keys` with JSON `grant_type`, `code` and
`code_verifier`. The returned `key`/`access_token` is an app-specific `sk-nano-*`
inference key. It is **spend-capable** subject to the account's subscription,
balance, key settings, optional consent-screen caps and expiration. It is not a
read-only token and cannot read Management API quotas. NanoGPT does not issue a
refresh token or expose OAuth revocation; users revoke keys in NanoGPT settings.
No `/me` request, account read or inference probe is added to login.

The existing public browser adapter owns callback/state validation, bounded
exchange, cancellation and one-time-result handling; the existing login operation
service owns durable checkpoint, recovery and publication generation checks.
The provider adapter checkpoints issued keys with zero purposes before optional
response metadata validation. Only a fully validated prepared result receives
the requested purposes; recovery cannot bypass scope or identity validation. Invalid scopes, mismatched optional key aliases, checkpoint failure,
or an unconfirmed bound identity lead to recovery instead of a second issuance.
The shortcut's documented optional `user_id`, when valid, is an observed subject;
missing identity remains unverified. An expected subject/resource is never
silently confirmed from the requested account. No fabricated `self` identity is
used. The key is persisted as a static key for inference/models, without an
invented expiry or quota purpose.

An existing inference API key remains a private alternative. Management secrets
(`sk-nano-mgmt-*`) must be rejected by every inference credential entry path;
`NanoGptCredentialPolicy` provides the provider namespace checks. Root integration
owns those shared entry paths and runtime projection.

The API base is `https://api.nano-gpt.com/api/v1`, through lain's existing
OpenAI-compatible transport. Account connection alone adds no route. Optional
`deepseek/deepseek-v3.2` and `moonshotai/kimi-k2.5` templates were confirmed in the
unauthenticated official [model catalog](https://api.nano-gpt.com/api/v1/models)
on the check date (670 entries). No capabilities, pricing, included entitlement,
or free-only eligibility are inferred from listing. Model selection is optional.

## Separate subscription monitor

[Management API](https://docs.nano-gpt.com/api-reference/management-api) and the
[official OpenAPI contract](https://nano-gpt.com/management-openapi.json) publish
`GET https://nano-gpt.com/api/management/v1/subscription/usage`. The user creates
and privately enters a token at
[Settings → Management API tokens](https://nano-gpt.com/settings#management-api-tokens),
choosing **Usage only — view subscription quotas** (`usage:read`). The token is
shown once. Expiration defaults to 90 days with a maximum of 365 days on the
provider; lain does not invent an expiry when none was supplied locally.

The monitor has kind `management-token`, requires a secret and has
`ResourceRequired=false`. The endpoint accepts **no account selector**. Both a
monitor-only account and an optional monitor attached to an inference account use
the token owner's personal subscription; no user ID is returned. A successful
read therefore does not verify that the token owner matches an inference key.
The reading's support description states that association is unverified, and no
scope ID is fabricated. Inference token expiration/base URL do not affect a
separately selected management credential. No inference key fallback exists.

The reader only sends this separate token to the fixed management endpoint and
rejects an inference key, a resource selector or a known expired monitor before
HTTP. It performs no key listing, creation, mutation, inference, redirect or
permission probing. Missing monitor setup is neutral `setup-required` guidance,
not a failed provider request. HTTP 401/403/429/5xx use sanitized shared quota
errors without provider response bodies or secrets.

Normalization follows the returned values:

- `dailyInputTokens` and `weeklyInputTokens` are input-token windows;
  `dailyImages` counts images. Limits come from `limits`, never advertised plans.
- `percentUsed` is fractional, multiplied by 100, and may exceed 100%.
- `resetAt` is Unix **milliseconds**, independent of the billing period end.
  Missing/malformed resets stay unknown; no daily/weekly reset is calculated.
- Missing/null quantities remain unknown. A degraded window discards its usage,
  remaining and percentage counters even if unexpected numeric values coexist.
  A separately reported limit remains a limit, never an inferred full allowance.
- Null quota windows are not configured/applicable. `active` is subscription
  status, never proof of remaining usage or model coverage.
- Token trials are supported only when `usageUnits=tokens`, with
  `tokenLimits.total` and the `tokens` quota object.
- No wallet, billing identity, plan price, cash value, account selector or model
  entitlement is inferred. No subscription pool is shared across accounts.

## Verification boundary

`tests/nanogpt/NanoGpt.Tests.csproj` links the provider implementation and existing
browser/quota seams with the real Core DTOs, without new dependencies. It uses
fake provider HTTP and local callbacks only, covering PKCE/state, one-time
exchange, durable checkpoint/recovery, unknown/mismatched identity, credential
separation, monitor-only access, setup guidance, exact units, fraction/overage,
millisecond reset, degraded/null/invalid data, trial tokens and sanitized errors.
It does not authenticate to NanoGPT or prove the user's live subscription quotas.

Shared registration, account/store/runtime credential checks and resource-less
monitor UI are owned and verified by the integration package. The focused test
result is recorded in the package handoff after its serial build slot; live
login and management token entry remain separate visible user steps.
