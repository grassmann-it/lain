# AC-08: bounded Console qualification

**Historical source-only stage.** The 2026-10-25 MiMo implementation and its
remaining live-qualification limits are documented in [mimo.md](mimo.md).

Date: 2026-10-24. Source base `de4ed85`. This is local source/fixture work, not
provider login or a production connector. No browser sign-in, provider API,
credential, key creation/reveal or installed session was used. Public static
assets and browser-vendor documentation were read without authentication; exact
URLs, HTTP results and SHA-256 are in `console-public-sources-2026-10-24.json`.

## Linux browser decision

| Candidate | Public evidence | Remaining proof | Decision here |
| --- | --- | --- | --- |
| A: explicitly started browser helper with its own persistent profile | Playwright documents `launchPersistentContext(userDataDir)`, one context, profile ownership and restrictions on automating the normal Chrome profile | Runtime distribution, supported Linux browser, MiMo IdP/MFA acceptance, private session persistence, lifecycle when UI closes, genuine UID/region response and permitted client use | Suitable for a bounded future probe; no production transport implemented |
| B: user-installed extension and native-messaging host | Chrome documents extension `host_permissions`, cross-origin behavior, native host `allowed_origins`, Linux host registration and native-message framing | Extension identity/distribution, installation burden, authenticated host pairing, provider same-origin/CSRF compatibility, genuine identity/region and lifetime | Separate product/install decision; no extension installed or shipped |
| System browser open only | Current Avalonia launcher opens a URL | No callback/session handoff contract | Insufficient for Console monitoring |
| Embedded WebView | No existing runtime in this application | Linux engine/dependencies, IdP/MFA compatibility and session isolation | No implementation choice made |

Vendor automation/extension capabilities do not establish provider permission,
CSRF compatibility or successful sign-in. No chosen option may extract cookies
from normal profiles, scan personal browser stores, bypass an IdP or expose
arbitrary JS/fetch operations. Source evidence does not justify committing the
project to a new browser runtime. Root can choose a bounded A probe after those
requirements are explicit; B remains a distinct installation workflow.

## Local boundary delivered

`Accounts/ConsoleConnections/ConsoleReadBoundary.cs` defines exactly three
candidate MiMo reads: identity `/api/v1/userProfile`, Individual detail and
Individual usage, always GET at `https://platform.xiaomimimo.com`. There is no
caller URL, arbitrary query/JS, cookie export, key listing, key reveal, key
creation, purchase or refresh operation. Unknown enum operations fail closed.

A read lease binds an existing account-connect operation ID, random nonce,
account, revision, subject, region and product, expires after two minutes, and
accepts at most one matching reply. The existing AC-01 lifecycle must supply its
current revision and liveness while holding its operation gate through reply
acceptance and result publication; it remains the only lifecycle/credential owner.
The parser caps response bytes/depth and returns fixed errors. Envelopes redact
`ToString`; they are private and must never become public API DTOs or logs.

This validates a bridge message contract only. There is deliberately no live
browser transport, bridge listener, second credential store or registry entry.
Nonce/correlation is not bridge authentication. A future helper must authenticate
the expected local peer, keep sessions in its own browser context, execute these
reads there with original CSRF/SameSite/Origin rules, prevent redirects to other
origins, and bind the verified identity to all reads. A malicious or unqualified
bridge could fabricate both identity and usage; synthetic replies prove no
provider identity. No production login button is justified by this boundary.

## Provider decisions

| Method/scope | Local outcome | Production decision and exact next proof |
| --- | --- | --- |
| MiMo Individual CN/EU | GO for pure schema/normalization and private message fixtures | NO-GO for session/login: need allowed browser path, real `/userProfile` identity, documented account/region mapping, matching authenticated usage and expiry/cancel lifecycle |
| MiMo Individual SGP | No inferred region | NO-GO: website defaults every non-310/non-314 idc to SGP; this is insufficient evidence for a verified SGP identity. Need explicit idc/region semantics |
| MiMo Team | Separate pure `usedPercent` normalization only, no browser route enabled | NO-GO: genuine project/member/seat binding, role coverage, reset/history semantics and live schema remain unqualified |
| MiMo PAYG | No balance reader shipped | NO-GO: currency/cash/gift/frozen/overdraft semantics and matching identity/session need qualification; no mixing with plan credits |
| SambaNova Cloud Free/Developer | Current billing source confirms origin-relative `/resources` + `/credits/balance`, `amount`, `readAt` | NO-GO: session/CSRF, own user identity from `/api/auth:me`, permitted client, currency/unit and settlement delay not proved. Existing API key + portal/passive request headers remain fallback; no zero-credit simulation |
| Upstage direct Solar | Current public BFF client includes `x-console-request` header constant | NO-GO: header is not auth; need session/CSRF/client contract, user/org/role discovery, exact v3 spend/promotions/usage pagination/time/decimal schemas. Existing API key + portal/CSV remain fallback |
| Cohere Pairing Evaluation/Production | Current auth-cli asset calls `cLIAuthConfirm({secret})`; shared asset calls `getOrCreateDefaultAPIKey({canReturnProductionKey:true})` (explicitly forbidden to this monitor) | NO-GO: own allowed pairingclient, Begin/Check rights, TTL/polling, token lifetime/refresh/revoke, org identity and exact read-RPC schemas need qualification. Do not impersonate Cohere CLI or join verified orgs. Existing key + portal remain fallback |

No placeholder SambaNova/Upstage/Cohere quota adapter is registered: labels without
an executable admissible credential/read contract would falsely advertise a
method. MiMo failure does not disable existing key routes or passive sources.

## MiMo schema evidence and normalization

The re-fetched official main bundle contains `refreshUserInfo` reading
`/userProfile`, `userInfo.userId ?? userInfo.id`, explicit idc 310→CN and 314→EU,
and a broad fallback to SGP. The candidate parser accepts only explicit CN/EU,
rejects absent/conflicting/malformed IDs, and does not treat cookies, email,
localStorage region, or a successful browser page as verified identity. It does
not infer an API region URL or EU→AMS deployment mapping.

The same bundle uses `credentials:"same-origin"`, prefixes `/api/v1`, and checks
JSON `code` before extracting `data`. The website accepts 0, 200 and 429 in one
helper; lain deliberately treats application-code 429 as unavailable rather than
successful zero usage. Only numeric success code 0/200 with object data is parsed.

The official plan chunk reads `usage.items`, considers every bucket including
`compensation_total_token`, and multiplies Individual `percent` 0…1 for display.
`MiMoConsoleQuotaReader.Individual` preserves all pools without summing them,
uses weighted plan credits, keeps missing components unknown, and reports
partial measurements. Invalid percentages are not clamped into apparent
certainty. It does not convert `currentPeriodEnd` into a refill/reset time.
A mismatched identity yields no readings; callers retain the last valid snapshot
with its original observation time. There is no fabricated zero quota.

Team `usedPercent` is already 0…100. The separate pure Team method exposes only
that scalar percentage and explicit next reset, with project scope, no invented
used/limit credit counts and no claim that project usage is a seat's usage.
Team membership verification remains a prerequisite outside this fixture reader;
no Team request is admitted by the bridge catalog. Money, seat history and plan
validity remain separate unimplemented scopes.

## Validation and integration

`tests/console-connections` contains synthetic JSON and an executable .NET
fixture harness for the actual production parser/boundary classes. It checks
wrong UID/region/idc, conflicting identity, application error, partial values,
all pools, Individual/Team percentage separation, no invented reset, nonce,
origin, revision, cancellation, expiry, replay, HTTP rejection, payload bounds,
unknown operations and private envelope formatting.

Build/runtime is serialized by root. Until that harness runs in the assigned
slot its result is `not_run`; public asset retrieval is source evidence only.
No real provider or browser result can be inferred from synthetic tests.
A future integration must provide qualified bridge transport and foundation
operation ownership before registering any Console method. Full AC-08 remains
open; these commits make bounded preparatory functionality and blockers concrete.
