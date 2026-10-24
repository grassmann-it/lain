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

## Installed delivery

The full Linux package is installed from application source
`413588c` with Core SHA-256
`a03e0ba2f4d2723a6a26fb10c89d838de75d6b14119b6ec4be3a2f1d7af4a185`.
The real upgrade preserved ten accounts, 46 configured routes, three profiles,
credentials, the applied runtime and all historical attempts. Desired/applied
revision remains **10/2**; no pending profile was applied. The legacy service,
its protected files, ordinary Codex configuration, separate Codex profile/catalog
and UI preferences remain unchanged by installation. The installed usage skill
now documents private setup and the extended quota contract.

| Area | Prepared and locally qualified | Installed | Live provider qualification |
| --- | --- | --- | --- |
| SambaNova, Upstage, Cohere, Kilo | Gateway manifests, transport/identity/error fixtures, connection-first setup | Yes | No new live inference in this delivery; exhausted Kilo was not retried or topped up |
| Fireworks | Gateway plus independent official quota adapter and separate monitoring access | Yes | Requires a locally supplied account and appropriate quota permissions |
| Groq | Actual Core-process capture: two accounts, one model, 200/429, six headers, persistence and unchanged observation age | Yes | Synthetic upstream qualified end to end; existing real access denial is not claimed fixed |
| Gemini | Same-account Google Monitoring access snapshot, expiry and scoped usage-only readings | Yes | No monitoring access token was requested in chat or provisioned automatically |
| Cloudflare | Neutral explanation and official guidance | Yes | Usage reader/dataset remains unimplemented; no remaining Neurons are invented |
| NVIDIA Build | Neutral explanation and official guidance | Yes | No suitable Build quota API qualified; inference availability remains separate |

Exact packaged tests passed: four safe startup-failure cases, five independent
Host HTTP groups and seven installation/lifecycle groups including synthetic
inference, persistence, update and rollback. The installed GUI passed **182 checks**
on private Xvfb, with 24 captures and 22 retained image files. Closing/reopening
the test window preserved history and the background service. CLI/API history
and compact/full JSON agree; both private wizards appear in installed help.

The first installation attempt did not bring up the Core and was explicitly
rolled back with state preservation. Its cause remains unproven. The Host now
retains bounded startup-stage diagnostics, keeps its repair API available, handles
failed process launch safely and starts passive quota readers after the initial
Core attempt. No raw Core output is exposed. A reproduced TCP TIME_WAIT case did
not reproduce the failure and prompted no speculative port change. The corrected
package passed the real upgrade and a further real service restart (ready in
1.97 seconds), preserving configuration, credentials and fixed historical usage.

The separate installed Codex profile retains `mimo-v2.6-pro` and all 45 catalog
entries, with native search disabled. Start a **new** session using
`codex --no-daemon --profile lain`; an existing session does not reload this
setting by changing models. Glyph's provider-defined tool rejection, Beam's
128-tool ceiling and Laguna's 429 are distinct remaining compatibility/access
limits. No MCP tools were silently removed and no paid fallback was enabled.

An already-open UI keeps its loaded build. Use the tray's **Quit UI — host stays
running**, then `lain ui`, to load this delivery without changing the service.

Evidence: [installed upgrade](../tests/acceptance/evidence/providers-20261024/installed-upgrade.json),
[installed desktop](../tests/acceptance/evidence/providers-20261024/installed-desktop.json),
[installed CLI](../tests/acceptance/evidence/providers-20261024/installed-cli.json),
[restart](../tests/acceptance/evidence/providers-20261024/installed-restart.json),
[safe startup diagnostics](../tests/acceptance/evidence/providers-20261024/startup.json),
[real Groq Core-process probe](../tests/acceptance/evidence/providers-20261024/groq-process.json).

Final archive: `artifacts/lain-linux-x64-0.1.0-providers-r2-20261024.tar.gz`.
All 1,038 files match the installed package and final checksum inventory; executable
payloads stayed unchanged during acceptance. [Final delivery manifest](../tests/acceptance/evidence/providers-release-final.json)
records the package/archive hashes and the deliberately limited live scope.
