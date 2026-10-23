# Independent expansion review — 24 October 2026

Status: **in progress; no integrated release acceptance yet**.
Review baseline: `7b3a990` on `next-review`. This reviewer owns this document only.
No product edits, legacy writes, provider requests with credentials, service
operations or builds have been performed for this expansion review. Integrated
build/test execution is assigned and serialized by the root.

## Accepted scope and evidence boundaries

Requirements come from [expansion-2026-10-24](expansion-2026-10-24.md), the complete
user attachment `pasted-text-1.txt` referenced there, and the existing
[service contracts](service-contracts.md). The user authorizes implementation
without new confirmation, including mock-tested adapters without credentials.
The attachment's model names, prices and advertised quotas are leads to verify,
not established provider facts.

The architecture-sparring review distinguishes confirmed requirements from
implementation choices. Existing working quota tiles/Usage remain available
while isolated worktrees develop this expansion. A screenshot, a source review,
a fake-upstream test and a live provider response are different evidence classes.
None substitutes for the others.

| Requirement | Independent acceptance check | Evidence/status |
| --- | --- | --- |
| Dense quota tiles | Unequal tile heights do not create large empty grid cells. Closed tiles retain named windows, bars, remaining/reset/freshness; native expansion exposes balances/actions/provenance. | Final renders pending. Prior quota acceptance does not qualify new layout. |
| Own live streams | Owned core supplies request/attempt live lifecycle and content; UI stays useful while a stream is running, including cancellation/error. | Core/Inspector implementation pending review. |
| Conversation/turn/tool/worker grouping | Group only by supported identities and recorded evidence. Retries, continuations and tool calls do not become invented independent conversations. Missing parents/workers remain unknown. | Shared contract inspected; source and adversarial grouping fixtures pending. |
| Durable usage | Existing requests/tokens survive migration/restart/replay. Metadata additions do not change token/outcome facts or double-count a terminal event. | Migration/collector source review pending. |
| Useful dimensions | Route, model family, manufacturer, inference provider, gateway, billing account, reasoning/mode and supported grouping identities remain distinct and historically attributable. | Additive DTOs inspected; end-to-end projection pending. |
| Performance | First-output latency, full duration and output/full-duration rate have explicit definitions and valid sample counts. Quantiles use the whole filtered dataset, not 25 displayed attempts. | DTO definitions inspected; SQL and execution evidence pending. |
| Token/cost semantics | Unknown is null, measured zero remains zero, cached/reasoning subsets are not added again. Costs require actual currency/unit/pricing basis; unknown pricing never becomes zero. | No cost aggregation promised where no source cost fact exists. Integration pending. |
| Declarative provider setup | Capabilities/protocol, account link, required credentials, billing kind/scope, limits and route matrix are available without a key. Missing credentials visibly block live verification, not documentation/mocks. | Catalog/onboarding implementation pending. |
| Strict free-only | Actual route execution and every retry/fallback reject paid or unknown price, incompatible scope/eligibility and expired/unverified trial assumptions. A display toggle alone is insufficient. | Policy implementation pending review. |
| Privacy/lifetime | Auth headers/keys/refresh tokens never enter public output or persistent Usage. Inspector content uses bounded local buffers, explicit redaction and TTL; gaps/evictions are visible. | Contract bounds inspected; implementation/negative tests pending. |
| Accessible UI | Keyboard activation/focus/expansion survive polling; statuses are text as well as color; narrow viewport and long content stay reachable without hidden data. | Private-Xvfb actual renders and functional checks pending. |
| Safe delivery | Owned-core identity/durability qualification remains valid; controlled migration/install has rollback. No legacy proxy/queue mutation or normal Codex configuration rewrite. | Final root-owned integration and interval-scoped isolation evidence pending. |

## Initial contract inspection

Inspected contract series `3d94392`, `cd32d46`, `5f0529e` in the contracts
worktree, including normative `observability-contracts.md`, without building:

- Existing positional DTO fields remain unchanged; new route, attempt and query
  fields are optional. Old readers are not thereby proven to retain new fields.
- `AttemptDimensionsDto` separates provider roles and carries per-field evidence;
  identifiers are nullable. Consumers must enforce that documented evidence rule,
  not merely accept a non-null arbitrary string as a verified relationship.
- New monotonic measurements are separate from legacy provider timing. Statistics
  expose samples and eligible counts, with an explicit output/full-duration basis.
- Inspector contracts include source ID, cursors, earliest cursor, dropped events,
  gap/truncation fields and declared memory/page/TTL limits. Defaults in a DTO are
  not proof that any component enforces them.
- Provider routes have nullable capabilities, sources/check dates, billing plans,
  separate manufacturer/inference/gateway/publisher fields and explicit free-only
  eligibility/enforcement metadata. Unknown capabilities/prices remain unknown.

No blocking contract incompatibility was found in that limited static inspection.
The following integration questions were sent to owners and remain open:

1. The initial account creation request accepted name/provider/API key/base URL,
   while catalogs can require an account ID, region/workspace or OAuth. Each
   required field needs an executable normalized setup path or an honest manual/
   unsupported status; a catalog entry alone must not imply completed onboarding.
   n0 now documents optional non-secret settings/providerRouteId and n_transport
   owns that concrete path; implementation acceptance remains pending.
2. Inspector redaction must operate correctly across streaming boundaries. Tests
   must include secret-like material split across chunks, transport/error envelope
   exclusion, bounded text sizes and expiration. This is an implementation check,
   not a prohibition on the user viewing their authorized stream contents.
3. Terminal measurement events must enrich an existing attempt without changing
   its already-recorded token/outcome facts or duplicate-counting a retry.

## Concrete findings and implementation follow-ups

| Finding | Inspected evidence and consequence | Disposition |
| --- | --- | --- |
| Catalog identity could misstate actual inference provider | In policy `bfd8068`, cost validation compared descriptor model only; binding compilation attached descriptor dimensions even when the selected secret had a custom endpoint/protocol. A declared NVIDIA route could therefore claim NVIDIA inference through another gateway. | Inspected fix `aec3f78`: bindings require explicit qualified route IDs and writer calls `ProviderTransportValidation.ValidateCompiled`. Final validator and root wiring still pending; no execution claim. |
| Secret presence falsely cleared onboarding requirements | Initial onboarding endpoint used any provider account secret's existence to return an empty missing-fields list. It did not establish required account/region/workspace fields. | Inspected fix `aec3f78`: required fields remain unverified even with a secret; endpoint explicitly says presence is not validation. Static finding resolved. |
| Free-only execution is not yet a qualified usable path | Policy `bfd8068` always adds `upstream_cost_controls_unavailable`; both runtime writers reject it and explicit cross-route fallback execution. | Safe fail-closed boundary, not evidence that free-only requests can execute. Final product/evidence must disclose this limitation unless provider controls are qualified. Core startup defense pending. |
| Content lifetime spans two buffers | W1 WIP has 15-minute Core ring retention, while public DTO defaults describe a 120-second Host buffer. | Inspected documentation fix `d1188ab`: both independent lifetimes and on-read/observation expiry are explicit; no secure-erasure timer claim. Static documentation finding resolved. |

Static W1 inspection found separate content-only RAM storage, 4 MiB/4,096-event
ring bounds, 256 pending-capture slots and 2 KiB line buffers. Redaction precedes
publication of complete lines, so patterns split across transport chunks are
reassembled before checking. No content property was added to the durable event.
This is WIP source inspection, not an executed privacy or durability test.
Declared limitations must remain explicit: partial lines appear at newline or
completion; overlong lines are omitted; arbitrary short unmarked secrets cannot
be exhaustively recognized; expiry is currently applied on observation/read.

## Provider evidence checklist

For every provider below, verify official source URL and check date per concrete
claim, route protocol/model capability, declared billing unit/scope/eligibility,
mock transport coverage and explicit live verification state. No new credentials
exist for these integrations; `not_tested` is the expected truthful live state.
Published consumer subscriptions are not presumed to fund arbitrary proxy calls.

| Provider | Specific boundary from the full request | Review status |
| --- | --- | --- |
| NVIDIA Build / Step Preview | Development/evaluation eligibility and real limits; official route supports the claimed streaming/tool/reasoning behavior. | Manifest/config/research doc statically reviewed. Independent FAQ fetch failed; no independent live verification or inference. |
| StepFun direct / Step Plan | Subscription versus PAYG endpoint; credits are not tokens; supported coding-tool/general-proxy scope. | Independently retrieved official pricing and accounts markdown: confirms monthly credits/prices and separate GET /v1/accounts balance. Transport/quota test execution pending. |
| Poe | Points shared across models; official API/OAuth only; manufacturer bot versus third-party publisher explicit. | Research and OAuth source inspected: prepared PKCE/state/one-use token-request helper, no active callback or persistence. Points/publisher distinctions explicit; live not tested. |
| OpenCode Go / Go Plus | Shared weighted quota and time windows; route-specific protocols; model budgets not added. | Research and request adapter inspected: explicit protocol table/session header, no quota endpoint claim; live not tested. |
| Gemini API | API/project-based free tier versus paid models and consumer subscription. | Research/model metadata adapter inspected: fixed official endpoint, no redirect, bounded response, metadata is not inference entitlement; live not tested. |
| Inception Mercury | Once-only trial, expiry unknown if unverified; no permanent-free claim. | Research/config/request preparation inspected: exact endpoints, regional/plan boundaries and free-only rejection; live not tested. |
| OpenRouter | Explicit free model IDs versus random free router; eligibility/rate limits; no paid fallback. | Pending source/adapter review. |
| Requesty | Organization pool, pinned free provider route and disabled chargeable fallback. | Pending source/adapter review. |
| Cloudflare Workers AI | Neurons, account ID and token, direct API usage; paid-only routes separate. | Pending source/adapter review. |
| Groq | Distinct from xAI/Grok; token/rate limits separate from context capacity. | Official-source research and bounded model metadata adapter inspected; account limits/remaining unknown; live not tested. |
| Mistral | Free API limits, paid API and subscription benefits separate; shared scope only if proven. | Official-source research and bounded model metadata adapter inspected; account limits/remaining unknown; live not tested. |
| Alibaba Model Studio | Region/workspace/endpoints; expiring per-model trials; Free Quota Only enforcement; coding-plan restrictions. | Research/config/request preparation inspected: exact endpoints, regional/plan boundaries and free-only rejection; live not tested. |
| Nebius TokenFactory | PAYG/trial; optional Builder application is not guaranteed credit. | Research/config/request preparation inspected: exact endpoints, regional/plan boundaries and free-only rejection; live not tested. |
| Synthetic | Subscription model/limit eligibility verified; no universal consumer-budget assumption. | Research and endpoint/protocol validator inspected. Synthetic weighted pool and xAI separate management credentials explicit; live not tested. |
| xAI/Grok | Direct API; no unverified general permanent-free budget. | Research and endpoint/protocol validator inspected. Synthetic weighted pool and xAI separate management credentials explicit; live not tested. |

Gateway source inspection also confirms that OpenRouter, Requesty and Cloudflare
manifests distinguish fixed free IDs, random free routing, organizational/account
scope, paid overflow, Neurons and unknown live state. Their helper validation is
not itself a dispatch boundary; final writer/Core enforcement still needs review.

## Additional data and Inspector source inspection

Data WIP `SqliteUsageStore.Analysis.cs` computes continuous p50/p95 over each
whole filtered metric population with SQLite window functions. Attempt/page
limits do not change the population. Missing measurements produce zero samples
and null statistics, separately from all eligible attempts. Output rate requires
reported output tokens and positive full duration. Unknown dimension values stay
null; group truncation affects completeness. Source review does not establish
migration/replay execution or numerical fixture results.

Host Inspector WIP retains a bounded transient projection and strips tool
arguments/results, prompts and raw frames from the display path. Source changes,
sequence gaps, evictions and unobserved outcomes have explicit states. Identity
conflicts clear grouping fields instead of silently moving a request between
workers/conversations. These implementation follow-ups were sent to its owner:

- Restrict provenance source kinds to the normative allowlist; current WIP accepts
  any sanitized source string.
- Preserve newly added effective reasoning in the Host projection. Completion
  state is already taken from actual Core terminal outcome; the shared measurement
  DTO has no completion field, so no measurement-field omission is alleged.
- Consider discard-until-delimiter behavior after oversized input in the Host
  redactor; upstream Core already omits overlong lines, but clearing state alone
  can expose a subsequent continuation in an isolated Host fixture.

These are unexecuted WIP findings, pending owner updates and negative fixtures.
Provider preparation source reviews likewise do not qualify Host transport wiring,
account entitlements, successful inference or usable free-only execution.

## Evidence ledger and final outcome

No integrated builds or tests have been assigned to this reviewer yet. Code/test
source inspection does not imply execution. No expansion feature is accepted for
release by this document at this stage.

The preceding quota review's normal Codex preservation statement was separately
corrected in `2ebcf8f`: the 23:58 Desktop/CLI update preserved the bytes it started
with, while the earlier 20:50 baseline mismatch/current 21:13 modification remains
unattributed. Do not copy a whole-session unchanged-files claim into this release.
