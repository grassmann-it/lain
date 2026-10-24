# Provider delivery, quotas and private setup

User-authorized implementation following the 24 October parallel handoff.
Base is `b5d3f81`; seven workers use isolated worktrees. Main owns shared
contracts, registration, transport hooks, serialized qualification and delivery.
Research files edited in another conversation remain untouched.

## Shared contracts

- Existing `/api/v1` and positional DTO members remain. New members are optional.
- Provider connections group endpoint/protocol scopes independently of model
  templates. `ProviderConnectionDto` contains Id, Name, BaseUrl, Protocol,
  BillingPlanIds and ProviderRouteIds. The Host derives and validates groups.
- `AddApiAccountRequest.ProviderConnectionId` selects the connection; the older
  singular ProviderRouteId remains supported but the two forms are exclusive.
  Optional `SelectedModelRouteIds` creates explicitly chosen model routes in the
  same revision transaction. Empty means save the account only. The private
  connection can serve every compatible, permitted template in that connection
  and chosen billing scope; it does not activate all those routes.
- `QuotaSupportDto(Kind,Message,ActionUrl,CredentialPurpose)` describes a source
  capability separately from Error. Kinds: api, passive, console, unsupported,
  unimplemented, setup-required, awaiting-observation, unavailable. No available
  adapter is not an HTTP failure. Actual auth/network/parsing errors remain visible.
- QuotaReading/Dto add stable WindowId, MeasurementKind (quota, balance, usage,
  spend, limit), Scope/ScopeId, Model, SourceUrl, DataAsOf, WindowStart/WindowEnd,
  FreshUntil, ProviderUpdatedAt and ApprovedLimit. Reading additionally carries
  its original ObservedAt. Unknown values stay null. Provider update timestamps
  are not resets or usage observation timestamps; approved ceilings are not the
  currently enforced limit. Passive polling must never freshen an old observation.
- `QuotaFetchResult.Support` carries neutral source/setup states. Passive adapters
  set `IQuotaAdapter.RequiresCredential=false` and never receive an inference key.
- QuotaCredential exposes nonsecret Settings and a separate private Monitor
  projection (Kind, Secret, ResourceId, ExpiresAt); no refresh material enters it.
  A configured separate monitor secret never silently falls back to the inference
  key. Monitor credentials bind to the existing stable account and provider.
- Monitor GET/PUT/DELETE uses `/accounts/{id}/monitor`; writes are revision-bound.
  Public MonitorAccessDto contains no secret. Manual API tokens and externally
  obtained access snapshots are distinct. Snapshot access never refreshes/revokes.

## Passive Groq source

The Core observes the six documented rate-limit headers on actual upstream
responses, with proven local account/request/model identity. Private authenticated
`GET /lain/quota/observations` returns version 1 and a bounded observations array:
accountId, provider, model, requestId, observedAt, statusCode and a header map of
string arrays. At most 512 account/model observations and a 1 MiB response.
The Host reads this source; refreshing quotas never starts inference. Tokens
represent TPM, request amounts RPD. Original observation time and scope survive
the UI/CLI projection; no account-wide aggregate from unrelated models.

## Delivery and acceptance

Each package distinguishes preparation, local qualification, installation and live
qualification. No purchases, top-ups, paid fallback or implicit privacy opt-in.
Workers use synthetic credentials only. Actual tokens are entered privately in
lain; Main coordinates permitted live probes, service operations and installation.
GUI tests run only on private Xvfb. Existing account IDs, routes, profiles and
history survive; pending profile changes are not automatically applied.

The Core packaging regression that dropped `codex-model-catalog.json` is included
in this delivery: restore and validate the catalog in fresh and Core-refresh
packages, including synthetic fresh onboarding. Current source provenance remains
honest about the earlier static model snapshot.

Standalone Monitor, broad UI navigation redesign, external history imports and
all other researched providers remain separate scopes. Account setup does change
now because its single-model connection picker is a confirmed usability defect.

## Integrated local qualification

The five provider gateway manifests, connection-first setup, same-account monitor
credentials, neutral quota capability presentation and private CLI wizards are
integrated. Groq observations retain their upstream age; Gemini reports scoped
usage only; Fireworks retains enforced limits separately from approved ceilings.
Cloudflare reporting still lacks a qualified dataset/reader; NVIDIA Build has no
qualified quota API. Neither limitation is displayed as a fetch failure.

The independent review found and fixed an old-key identity collision before any
private write, and a neutral empty-monitor display error. Partial refreshes retain
fresh successful windows while unavailable windows keep stale values. All eight
independent groups (57 assertions), relevant legacy and new provider/CLI suites,
35 private-Xvfb checks and the eight Core runtime groups passed. The full journal
was replayed ten times through the actual SQLite consumer without duplication.

Evidence: [local tests](../tests/acceptance/evidence/providers-20261024/local-tests.json),
[Core qualification](../tests/acceptance/evidence/providers-20261024/core-qualified.json).
Package/install/live acceptance is recorded separately after these source checks.

The separate Codex profile also disables native web search, keeping the normal
configuration and selected model. [Tool compatibility](providers/codex-gateway-tools.md)
distinguishes MiMo native search, Glyph provider-defined tools, Beam tool-count
limits and Laguna rate limiting; no silent tool truncation or paid fallback.
