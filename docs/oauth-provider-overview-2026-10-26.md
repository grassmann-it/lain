# Provider OAuth and login overview

As of: 26.10.2026. Inventory of the reviewed working copy at HEAD
`fef478f601f46b315939fa53f02caa9791086f1d`, including existing WIP.
**Implemented, offered in the catalog, delivered and tested against the actual
provider are different states.** This overview reviews source code and documented
evidence, not the current private installation. No update is planned.

## Starting point: which login options actually exist?

I = inference, M = models, Q = quota/usage; Q does not promise a monetary balance.
“Offered” refers to the source catalog, not a provider login confirmed in this
research. API keys remain a separate connection method.

| Provider / method | Actual current path | Purpose / renewal | What the next implementation or acceptance step requires |
| --- | --- | --- | --- |
| Codex `codex-owned` | Dedicated browser login through the existing gateway/core | I/Q; core owns the grant | Preserve the existing path. A separate standalone monitor needs an isolated official app-server and its own lifecycle evidence. |
| OpenRouter `browser` | Implemented public S256-PKCE → dedicated API key | I/M/Q; static key, no OAuth refresh | Real callback/key/reconnect acceptance; no separate client registration required. |
| Kilo `device` | Implemented personal device-key flow | I/M/Q; `static-key`, reconnect on rejection | Test a real new login, identity and transport/quota; no client-ID setup. |
| Kimi `kimi-device` | Implemented device flow; explicitly choose global/CN region | **Q only**; dedicated host owner `kimi-oauth` | Real login, restart and refresh with `/me`/usage binding; existing inference keys stay separate. No additional registration under the accepted toolkit contract. |
| NanoGPT `browser` | Implemented shortcut PKCE → app-specific inference key | I/M; static key, no refresh owner | Real key-issuance/reconnect acceptance. Q currently uses a separate management token; same-key Q is a new work package. No manual client registration for the shortcut. |
| Google `official-cli` | Implemented login using official gcloud 584.0.0 in a dedicated private profile; depends on local installation | Q only; CLI holds the refresh grant, host owner `google-gcloud` projects access | Matching pinned CLI and real login/project/renewal/monitoring acceptance. No separate OAuth registration for the delegated CLI path. |
| Google / Cloudflare `browser` | OAuth adapters implemented, **maintainer gate**, no evidenced registration of our own | Q only; `google-oauth` / `cloudflare-oauth` | Our own clients, accepted scopes/callbacks and real refresh/resource/read acceptance; details below. |
| Poe `browser` | PKCE key adapter implemented, **registration/callback gate** | I/M/Q; issued API key, possibly with expiry, no refresh grant | Our own Poe client and exact release callback, real key/reconnect acceptance. |
| Requesty `official-cli` | General product adapter **unavailable**; one documented isolated official CLI login with private key export succeeded | Adopted static key: I/M and self-Q; no lain OAuth refresh | Turn the proven operator procedure into a general isolated adapter with recovery/identity binding. One successful case does not enable a button. |
| Fireworks `official-cli` | Specific adapter implemented, **hard-blocked in production**, even with a matching binary | Planned I/M/Q after official CLI login and visible creation of a new key; static key | firectl 1.8.19 binds `:18000` to all interfaces. Resolve and verify callback isolation first, then real SSO/account/key acceptance. |
| MiMo `console-browser` | Dedicated console session implemented; Linux/browser/desktop gate; **not an OAuth adapter** | Q only; browser-bound session, reconnect after closing/restarting | Authenticated UID/region/individual-read acceptance is missing; no new browser automation in this effort. |
| Codex / Google snapshot | Deliberate external access-token snapshot with identity/resource and expiry | Q only; **no refresh or revocation by lain** | Deliberate handoff again after expiry; never relabel it as an owned grant. |

**Real evidence:** The [OAuth delivery report](oauth-login-implementation-2026-10-25.md)
evidences Kilo/Kimi packaging, native offline acceptance and installation at that
time, explicitly not a new real provider login/refresh. It separately documents
the successful Requesty CLI login and an automatic self-read, not inference or an
organization wallet balance. [EC1–EC4](evaluation-account-connections-2026-10-26.md)
evidence NanoGPT/DeepSeek source and package acceptance, but no completed
installation/provider login there; Fireworks remained blocked. The
[gcloud evidence](account-connect-qualification/cloud/google-gcloud-2026-10-25.md)
is source/offline qualification, not a real login/project read.
The [native OpenAI usage evidence](native-openai-usage-delivery-2026-10-26.md)
evidences a separate native Codex login with usage; it does not retroactively
qualify a new lain standalone owner.

## Authoritative implementation and limits

The authoritative list of offered methods is in
[`ConnectionMethodCatalog.cs`](../src/Lain.Host/Accounts/ConnectionMethodCatalog.cs);
the adapters/refresh owners actually registered are in
[`Program.cs`](../src/Lain.Host/Program.cs). Historical
[Account Connect packages](account-connect-work-packages/README.md) and their
[provider matrix](account-connect-work-packages/provider-map.md) partly describe
earlier planning states. In particular, “Kilo Device later”, “Kimi unqualified”
and “NanoGPT connector missing” are outdated as a current inventory.

| Code evidence relative to `src/Lain.Host/` | Assessment |
| --- | --- |
| `Accounts/CodexOwnedLoginAdapter.cs`, `HttpCodexOwnedLoginBackend.cs` | Existing core login; not a general new host refresh owner. Core renewal of active applied owned bindings does not evidence route-independent monitoring. |
| `Accounts/PublicLogin/PublicBrowserLoginAdapter.cs`, `OpenRouterLoginAdapter.cs`, `NanoGptLoginAdapter.cs`, `PoeLoginAdapter.cs` | Shared browser/callback/checkpoint lifecycle; OAuth bootstraps a key. No invented refresh grants. NanoGPT offers only I/M in the catalog. |
| `Accounts/Kilo/KiloDeviceLoginAdapter.cs`, `KiloTransportHandoff.cs` | Personal device key, identity verification and reconnect preserving existing state; organization accounts are not universally supported. |
| `Accounts/Managed/KimiDeviceLoginAdapter.cs`, `KimiDeviceProtocol.cs` | Actual registered device adapter and `ICredentialOwner`; Q only. The old Kimi placeholder class is not counterevidence: the catalog filters out its entry. |
| `Accounts/Cloud/CloudOAuthAdapter.cs`, `CloudMaintainerConfiguration.cs` | Complete code/refresh path gated by our own qualified registration; `Program.cs` accepts only `Qualified` clients. |
| `Accounts/Cloud/GoogleGcloudLoginAdapter.cs`, `GoogleGcloudProcess.cs` | Dedicated isolated official CLI owner. No adoption of other gcloud/ADC stores; no export of the CLI refresh secret to a second owner. The grant is broad, not “read-only OAuth”. |
| `Accounts/CliConnections/RequestyCliLoginAdapter.cs`, `CommandCodeCliLoginAdapter.cs`, `MistralVibeCliLoginAdapter.cs` | Blocked classes with limited helper contracts, not released login implementations. |
| `Accounts/CliConnections/AlibabaCliLoginAdapter.cs`, `QualifiedCliLoginAdapter.cs` | Prepared executable CLI path/profile reader, but enabled in neither the active adapter array nor the method catalog. Not a finished OAuth monitor. |
| `Accounts/CliConnections/FireworksCliLoginAdapter.cs`, `FireworksCliQualification.cs` | Registered but specifically blocked adapter. `Validate()` rejects the product path; a hash or availability flag does not remove the callback blocker. |
| `Accounts/Managed/ManagedLoginQualification.cs`, `CodexStandaloneConnectAdapter.cs`, `ZaiConnectAdapter.cs` | Deliberately unusable candidates/protocol components; no standalone Codex or Z.ai product login. |
| `Accounts/Monitoring/MonitorAccessCatalog.cs` | Separate monitor secrets and external snapshots; their existence does not evidence an OAuth method. |

lain stores a static API key but does not renew it through OAuth refresh. A CLI
export of such a key is not a rotating OAuth snapshot. For delegated renewable
grants, the official CLI owns refresh; the host only invokes the qualified helper
in its dedicated profile. External snapshots remain entirely under external
refresh ownership.

## Complete provider coverage

Coverage: 22 current catalog files plus the five additional providers Codex,
Command Code, Kimi, MiMo and Z.ai = **27 providers**. Each row describes current
source capabilities, not blanket live acceptance. Every ordinary key has no
OAuth refresh; replacement/reconnect is through the provider. No additional
OAuth registration is required for key entry. “Not evidenced” refers to the
reviewed scope, not worldwide nonexistence.

| Provider ID | Login status beyond API keys | Inference / monitoring in the current scope |
| --- | --- | --- |
| `alibaba-model-studio` | Official CLI prepared, blocked; no OAuth client of our own | Regional key for I/M; console/coding-plan monitoring is not a generally released CLI login. |
| `cloudflare-workers-ai` | Dedicated browser OAuth adapter behind a registration gate | I/M via account token; Q requires separate analytics permission/dataset. OAuth currently provides Q, no qualified OAuth inference. Real dataset diagnostics found no usable neuron count. |
| `codex` | Core-owned browser login; external snapshot; standalone is only a candidate | Core I/Q; snapshot Q and expiry; route-independent renewal unresolved. |
| `cohere` | Key; console method unavailable | I/M; evaluation/trial/production separate, no qualified OAuth/console quota monitor. |
| `commandcode` | Key; official CLI adapter unavailable | I/M and direct key quota reader; login/cache/export contract not qualified for the product. |
| `deepseek` | Key; no OAuth adapter | I/M and same-key balance implemented; native currencies, no percentage quota. EC acceptance is not a real balance read. |
| `fireworks` | Key; official SSO CLI adapter blocked | I/M; Q with an authorized key and account ID. Resource quotas are neither credit nor Fire Pass. |
| `gemini-api` | Key; gcloud implemented; dedicated browser behind registration; external snapshot | I/M by key; Google project Q separate. OAuth I for the specific transport remains unresolved. Project usage does not guarantee remaining capacity. |
| `groq` | Key; no OAuth adapter | I/M; passive rate headers, optionally authorized enterprise metrics. Neither an account-wide monetary balance nor general remaining quota promised. |
| `inception` | Key; no OAuth adapter | I/M and locally observed usage; no qualified autonomous remaining-quota read. |
| `kilo` | Implemented device-key flow | I/M/Q with personal access; wallet and Pass separate. The free-model filter is not a separate login product. |
| `kimi` | Implemented dedicated device OAuth owner | OAuth Q only; retain the coding key for I/M and the existing key reader; Moonshot Platform is a different scope. |
| `mistral` | Key; Vibe CLI adapter unavailable | I/M; Vibe plan/Studio and enterprise admin monitoring are separate access paths, no confirmed universal OAuth quota reader. |
| `nanogpt` | Implemented PKCE key bootstrap | I/M; current Q reader requires a separate `usage:read` management token without an account selector. Publicly evidenced same-key read not yet implemented. |
| `nebius` | Token Factory key; no OAuth adapter | I/M, passive rate values where connected; Cloud IAM/Dedicated observability is not a general serverless balance. |
| `nvidia-build` | Public-endpoint key; no OAuth adapter | I/M; no qualified account remaining-quota reader. NGC/enterprise login is not substitute evidence. |
| `opencode-go` | Workspace key; no lain OAuth adapter | I/M; Go/Plus/PAYG separate. MCP client OAuth does not evidence a Go quota tool or inference grant. |
| `openrouter` | Implemented public PKCE key bootstrap | I/M/Q; key cap/key usage are not an account wallet. Management rights separate. |
| `poe` | Implemented PKCE key adapter behind maintainer gate | I/M/Q through issued key; points, expiry and reset only according to the actual schema. |
| `requesty` | Product CLI unavailable; successful individual official login documented | I/M through adopted key, self-Q evidenced; do not infer organization balance/rights. |
| `sambanova` | Key; console method unavailable | I/M; bound console credits/session not qualified, portal SSO does not create a new OAuth mandate. |
| `stepfun` | PAYG key; Step Plan login is only a researched vendor path | I/M; existing Q reader covers only personal PAYG on a qualified basis, not Step Plan/organization/project. |
| `synthetic` | Key; no OAuth adapter | I/M and same-key subscription quota; PAYG monetary balance is not part of the same metric. |
| `upstage` | Key; console method unavailable | I/M; do not offer organization/console usage and credits as OAuth Q without a qualified session bridge. |
| `xai` | API key; no API-team OAuth adapter | I/M with team discovery; management/billing access separate. Grok consumer/Build session is a different product. |
| `xiaomi-mimo` | Key plus implemented dedicated console session, no OAuth | I/M by key; Q prepared for a bound individual session, real authenticated read unresolved. Do not equate team/PAYG access. |
| `zai` | Coding key; managed login unavailable | I/M/Q with existing international key; independent client/non-creating CLI contract missing. PAT projection/discovery are not a finished OAuth owner. |

## Our own registrations: specifically required or not?

The [maintainer procedure](oauth-maintainer-registration-2026-10-25.md) and
[`scripts/prepare-oauth-maintainer.py`](../scripts/prepare-oauth-maintainer.py)
are prepared offline; staging leaves all qualification gates false. No credentials
or maintainer configuration were read here.

| Path | Registration work and remaining gate |
| --- | --- |
| Google direct | Our own Google Auth Platform **Desktop** client, branding/consent, possibly production/scope review; verify S256/IPv4 loopback and real renewal. Current adapter requires `cloud-platform`, not a narrow monitoring grant. IAM/APIs/billing are not configured automatically. |
| Cloudflare direct | Our own account-bound public client (`none`, S256), real OAuth scope IDs and `offline_access`; fixed callback registration, possibly domain verification/public approval. Registration requires administrative rights; no anonymous DCR. Neuron data source remains a separate gate. |
| Poe | Our own app at `poe.com/api/clients`; register/verify the exact release callback. The localhost development exception does not qualify arbitrary release URIs. `apikey:create` returns a key, not a refresh grant. |
| OpenRouter / NanoGPT Shortcut / Kilo | Public key bootstrap without users having to obtain a client ID. NanoGPT standard OAuth also supports registration; the current shortcut does not require that manual preparation. |
| Kimi | The project accepts the published toolkit client with an honest lain host identity; do not introduce additional registration as an unevidenced requirement. No claim of individual vendor approval/npm publication. |
| Official gcloud/Requesty/Fireworks CLI | Original tool performs its own flow. No lain client registration is needed for that; isolation, private handoff and product/identity binding remain necessary. Do not repurpose another client's ID for our own flow. |
| Z.ai / Step Plan | No evidenced general lain registration instructions. Neither assert that registration is definitely required nor copy the official app identity. First evidence a supported external contract or real CLI handoff. |
| OpenAI SIWC OSS | Separate dynamic registration per user/workspace and persistent host ID in the documented flow; no reuse of the Codex first-party client. Assess only as a new integration. |

Z.ai remains specifically limited: the [source handoff](providers/zai-managed-login-handoff-2026-10-25.md)
evidences ZCode's own flow, but no independent lain client contract. The official
bootstrap can create a key before returning successfully; `--no-browser` does
not prevent this. A monitor that creates no key needs a different evidenced
contract. A finite session plus reconnect would be acceptable; invented permanent
OAuth refresh is not an additional minimum requirement.

## Current official web review and real additional opportunities

The following official pages were actually opened on 26.10.2026. They evidence
public contracts, not a working lain installation or live login. Search results
alone were not used as implementation evidence.

| Primary source | Verified statement / implication |
| --- | --- |
| [OpenRouter OAuth](https://openrouter.ai/docs/guides/overview/auth/oauth) | Public PKCE key bootstrap; existing adapter is the right foundation, no new refresh owner needed. |
| [NanoGPT PKCE](https://docs.nano-gpt.com/api-reference/miscellaneous/oauth-pkce) | Shortcut and standard OAuth return app-specific API keys; shortcut creates/reuses the callback client server-side. Existing adapter covers the shortcut. |
| [NanoGPT CLI Device Login](https://docs.nano-gpt.com/integrations/cli-login) | Public start/poll contract with our own `client_name`, secret device code and visible user code; result is an API key. **Additional device path that can concretely be implemented**, with no lain adapter yet. Do not infer a refresh grant from the name “Device”. |
| [NanoGPT Subscription Usage](https://docs.nano-gpt.com/api-reference/endpoint/subscription-usage) | Documented inference-key read at `/api/subscription/v1/usage`, separate from the management path. **Same-key monitoring is a real additional opportunity**, not covered by today's management-only reader. |
| [Google Desktop OAuth](https://developers.google.com/identity/protocols/oauth2/native-app) | Dedicated desktop client, PKCE and loopback are publicly documented; continue the existing registration package. |
| [Create a Cloudflare client](https://developers.cloudflare.com/fundamentals/oauth/create-an-oauth-client/) | Account role, client configuration and scopes required. Supported registration exists; this does not prove reporting rights. |
| [Poe Sign-in](https://creator.poe.com/docs/external-applications/sign-in-with-poe) | Dedicated client and S256 code exchange for an API key documented; no blanket refresh. |
| [Fireworks CLI Authentication](https://docs.fireworks.ai/tools-sdks/firectl/commands/authentication) | `firectl signin` for Google SSO, with account argument for custom SSO. Official path exists; this does not prove loopback isolation. |
| [Requesty CLI README, pinned source](https://github.com/requestyai/cli/blob/21809398e99d888565178d718186b637f3de4566/README.md) | Official login and `auth token` integration; result is a long-lived product key rather than lain-owned refresh logic. |
| [Kimi Toolkit README, pinned](https://github.com/MoonshotAI/kimi-code/blob/743f140978bddaf8b6aa524765fcd578f7c22fc9/packages/oauth/README.md) | Vendor provides an OAuth toolkit in the public MIT source tree. Details/accepted client decision are pinned in the local [Kimi evidence](providers/kimi-device-login-2026-10-25.md); the short README alone does not evidence live approval. |
| [OpenAI Codex Auth](https://developers.openai.com/codex/auth/) | Official CLI supports device-code login; this does not qualify an additional lain core device adapter. |
| [OpenAI app-server](https://developers.openai.com/codex/app-server/) | Documented account/rate-limit RPC permits a limited standalone monitor experiment; stable account scope and isolated renewal still require local evidence. |
| [OpenAI SIWC for OSS/local apps](https://developers.openai.com/siwc/token-sharing-open-source) | Dynamic user/workspace registration, stable dedicated host ID and ChatGPT-plan inference through Responses are documented. **New integration opportunity**, not an evidenced replacement for today's WHAM reader. |
| [OpenCode Console API](https://opencode.ai/v2/docs/console/api/) | Workspace-bound API keys and separate usage/budget permissions documented. General [MCP OAuth in the client](https://dev.opencode.ai/docs/mcp-servers/) does not prove a suitable console quota tool. |

**Research limit:** Step Code's specific documentation page could not be retrieved
in this round. The older [vendor-source report](account-connect-research/xai-stepfun-poe-synthetic.md)
remains dated candidate evidence, not newly confirmed deployment/third-party
client approval. No additional generally usable OAuth contract was confirmed for
other providers in this limited scope. This does not claim a complete renewed
review of all vendor websites. Portal SSO, MCP client capabilities, visible
first-party client IDs and private browser endpoints do not replace a product
contract.

## Specific next work packages

Proposals for later implementation, **not an active update/login instruction**.
Reuse existing work; produce a separate reviewable result for each package.

| Priority / package | Bounded result | Go/acceptance criterion and stop boundary |
| --- | --- | --- |
| 1 — Qualify existing public flows | Accept OpenRouter, Kilo, Kimi and NanoGPT individually using existing adapters | Each needs a later deliberately authorized login, identity, persistent adoption/restart, reconnect; Kimi additionally needs real renewal. No new route or inference as a login side effect. An unresolved provider result blocks only its method. |
| 1 — Productize Requesty | Integrate the existing isolated v0.1.16 operator procedure and targeted private `auth token` export as a general adapter | Pinned artifact, complete store/keyring isolation, key-creation intent/recovery, account/group binding and targeted reconnect. No general configuration wizard that changes other harness files; no organization balance promise. |
| 1 — NanoGPT same-key Q | Separate reader for the documented inference-key subscription path, retaining the existing management monitor | Schema/unit/reset/degradation fixtures; select credentials strictly by path. Then optionally one authorized read. No management tokens for inference; no automatic paid fallback route based on `routing` hints. |
| 2 — NanoGPT Device | New start/poll key adapter in the existing login-operation lifecycle | Timeout/interval, cancel, consume/replay and checkpoint before adoption; device code stays private, no duplicate key issuance after a lost response. Initially I/M; Q only after separate reader qualification. |
| 2 — Complete Poe registration | Complete the prepared maintainer plan with our own app and exact callback | Separately evidence actual registration/callback/key expiry and reconnect. An offline plan or flag alone is insufficient. |
| 2 — Google direct monitor | Register our own desktop client; use the existing cloud adapter and discovery | Consent/scopes, IAM/project binding, callback, restart, refresh and real monitoring read; no OAuth inference as an extra package. Alternatively qualify the existing gcloud path against the real provider first. |
| 2 — Fireworks callback isolation | Ensure the unchanged official sign-in listener is reachable only locally despite its wildcard binding | First test network/callback design offline using the real tool without login and an external reachability check; only then separately accept SSO/account/one-time key issuance. Remains hard-blocked without evidence. |
| 3 — Codex standalone Q | Isolated official app-server owns a new dedicated monitor grant, documented rate-limit RPC | Without core/route: stable identity, restart, expiry/renewal, detach and no other caches. Never refresh the existing core grant in parallel. No new private WHAM adapter. |
| 3 — OpenAI SIWC inference | Bounded protocol/transport experiment for Responses inference documented for OSS/local apps | Dedicated dynamic registration/host ID, token/scope validation, one exclusive owner; supported models/preview limits and later deliberate end-to-end acceptance. No quota/WHAM promise and no silent replacement of existing Codex accounts. |
| Lower priority — Cloudflare | Complete registration plan plus exact analytics scope/dataset mapping | First determine whether the real account has any usable neuron data source. Successful OAuth without a data source does not justify an available quota monitor. |
| Research package — Step Plan / OpenCode / Z.ai | Only identify and pin each public external login/read contract | Step: admission/region/deployment; OpenCode: specific authorized read tool; Z.ai: dedicated client or non-creating official handoff. Without this evidence, no private-endpoint implementation or browser automation. |

Alibaba, Command Code and Mistral remain separate CLI qualifications under
[AC-05](account-connect-work-packages/05-cli-connections.md), not automatic
extensions of the Requesty package. Console login for SambaNova/Upstage/Cohere
and further MiMo automation are outside these OAuth opportunities.

## Review scope of this document

Project rules and architecture index, current adapter wiring, method/monitor
catalog, relevant provider adapters and the implementation, maintainer, Account
Connect and qualification documents linked above were read. OpenAI statements
were checked using the OpenAI Docs skill and official documentation pages that
were opened. The web review used only public documentation/vendor sources; no
authenticated API or discovery calls.

Only this file was written for that research task. No credentials were read,
and no CLI login, live quota/inference, client registration, installation,
production change or commit was performed. Existing WIP and others' research
remain intact. Checks: local link targets, complete provider list and whitespace
in this file; no build/test run for documentation only.
