# Independent expansion review — 24 October 2026

Status: **installed expansion checkpoint accepted within the evidence scopes below; eligible for final scoped release packaging, with new-provider live access explicitly untested**.
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
| Dense quota tiles | Unequal tile heights do not create large empty grid cells. Closed tiles retain named windows, bars, remaining/reset/freshness; native expansion exposes balances/actions/provenance. | Actual Masonry fixture renders independently inspected at 1120×780 and 800×560; closed-tile density accepted. Installed Desktop follow-up also inspected and accepted for this UI-only checkpoint; expansion stays separate. |
| Own live streams | Owned core supplies request/attempt live lifecycle and content; UI stays useful while a stream is running, including cancellation/error. | Core/Host/Desktop source and final compact renders accepted; final exact-hash synthetic Core/Inspector qualification passes. Actual new-provider live traffic remains untested. |
| Conversation/turn/tool/worker grouping | Group only by supported identities and recorded evidence. Retries, continuations and tool calls do not become invented independent conversations. Missing parents/workers remain unknown. | Source, adversarial Core/Host/Desktop fixtures and final renders inspected. Conflicting or missing evidence remains separate/unknown; no heuristic grouping accepted. |
| Durable usage | Existing requests/tokens survive migration/restart/replay. Metadata additions do not change token/outcome facts or double-count a terminal event. | Additive migration and metadata-only completion source reviewed; data worktree suite passed 22 tests. Actual installed upgrade/restart preserves the fixed-window historical request/tokens/cursor; ten synthetic reopen/replays also pass. Actual old-generation rollback was not executed. |
| Useful dimensions | Route, model family, manufacturer, inference provider, gateway, billing account, reasoning/mode and supported grouping identities remain distinct and historically attributable. | DTOs, binding validation and analysis projections inspected with synthetic coverage. Historical missing dimensions remain unknown; no live provider attribution inferred. |
| Performance | First-output latency, full duration and output/full-duration rate have explicit definitions and valid sample counts. Quantiles use the whole filtered dataset, not 25 displayed attempts. | Whole-population SQL and explicit denominator/sample semantics inspected; 22-test data suite and final rendered analysis accepted, including the 600-attempt percentile fixture. |
| Token/cost semantics | Unknown is null, measured zero remains zero, cached/reasoning subsets are not added again. Costs require actual currency/unit/pricing basis; unknown pricing never becomes zero. | Synthetic exact-Core null/zero checks and rendered eligible/sample counts accepted. No cost aggregation promised where no source cost fact exists. |
| Declarative provider setup | Capabilities/protocol, account link, required credentials, billing kind/scope, limits and route matrix are available without a key. Missing credentials visibly block live verification, not documentation/mocks. | Catalog/onboarding source, mock adapters and final provider/setup renders inspected. Required-field and consent boundaries remain explicit; all 15 new providers live not_tested. |
| Strict free-only | Actual route execution and every retry/fallback reject paid or unknown price, incompatible scope/eligibility and expired/unverified trial assumptions. A display toggle alone is insufficient. | Initial deny-all boundary reviewed; narrow guarded OpenRouter execution is now wired and source-reviewed. Same-key zero-cap proof remains mandatory; live usability not tested. |
| Privacy/lifetime | Auth headers/keys/refresh tokens never enter public output or persistent Usage. Inspector content uses bounded local buffers, explicit redaction and TTL; gaps/evictions are visible. | Implementation inspected; final exact-Core privacy/restart cases and Host 32+80 regression assertions pass. Independent Core/Host TTL and redaction limits remain explicit. |
| Accessible UI | Keyboard activation/focus/expansion survive polling; statuses are text as well as color; narrow viewport and long content stay reachable without hidden data. | Final private-Xvfb run passes 380 checks/68 PNG; key renders accepted at 1120×780 and 800×560. Routed events are not OS pointer hit testing. |
| Safe delivery | Owned-core identity/durability qualification remains valid; controlled migration/install has rollback. No legacy proxy/queue mutation or normal Codex configuration rewrite. | Actual stopped-state backup, whole-app upgrade/restart and interval-scoped protection checks pass. Final archive must retain the qualified payload hashes and explicit evidence limits. |

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
The following integration questions were sent to owners at the initial checkpoint; later resolutions and remaining delivery checks are recorded below:

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
| Catalog identity could misstate actual inference provider | In policy `bfd8068`, cost validation compared descriptor model only; binding compilation attached descriptor dimensions even when the selected secret had a custom endpoint/protocol. A declared NVIDIA route could therefore claim NVIDIA inference through another gateway. | Inspected fix `aec3f78`: bindings require explicit qualified route IDs and writer calls `ProviderTransportValidation.ValidateCompiled`. Also inspected `3178c53` validator: exact endpoint/model/protocol/core identity and generated-header checks. Root wiring and synthetic transport/policy execution were subsequently inspected and passed. |
| Secret presence falsely cleared onboarding requirements | Initial onboarding endpoint used any provider account secret's existence to return an empty missing-fields list. It did not establish required account/region/workspace fields. | Inspected fix `aec3f78`: required fields remain unverified even with a secret; endpoint explicitly says presence is not validation. Static finding resolved. |
| Free-only execution is not yet a qualified usable path | Policy `bfd8068` always adds `upstream_cost_controls_unavailable`; both runtime writers reject it and explicit cross-route fallback execution. | Safe fail-closed boundary, not evidence that free-only requests can execute. Final product/evidence must disclose this limitation unless provider controls are qualified. Core `8fe73e3` startup defense inspected: missing/configured accepted, free_only and unknown policies rejected regardless of claimed free labels. |
| Content lifetime spans two buffers | W1 WIP has 15-minute Core ring retention, while public DTO defaults describe a 120-second Host buffer. | Inspected documentation fix `d1188ab`: both independent lifetimes and on-read/observation expiry are explicit; no secure-erasure timer claim. Static documentation finding resolved. |
| OpenCode Gemini Contributor needs explicit data-use permission | Transport `3178c53` prepared the route without a privacy permission setting despite the provider research documenting that requirement. | Inspected fix `941d0fc`: absent/false consent rejects the Contributor route in shared Prepare validation; manifest explicitly describes the non-secret opt-in. Subsequent mock transport fixtures passed; live consent/onboarding remains untested. |

Static W1 inspection found separate content-only RAM storage, 4 MiB/4,096-event
ring bounds, 256 pending-capture slots and 2 KiB line buffers. Redaction precedes
publication of complete lines, so patterns split across transport chunks are
reassembled before checking. No content property was added to the durable event.
Committed Core `8fe73e3` and its semantic document were subsequently inspected; this is still source inspection, not an executed privacy or durability test.
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
| StepFun direct / Step Plan | Subscription versus PAYG endpoint; credits are not tokens; supported coding-tool/general-proxy scope. | Independently retrieved official pricing and accounts markdown: confirms monthly credits/prices and separate GET /v1/accounts balance. Subsequent NVIDIA/Step mock harness passed 40 assertions; live not tested. |
| Poe | Points shared across models; official API/OAuth only; manufacturer bot versus third-party publisher explicit. | Research and OAuth source inspected: prepared PKCE/state/one-use token-request helper, no active callback or persistence. Points/publisher distinctions explicit; live not tested. |
| OpenCode Go / Go Plus | Shared weighted quota and time windows; route-specific protocols; model budgets not added. | Research and request adapter inspected: explicit protocol table/session header, no quota endpoint claim; live not tested. |
| Gemini API | API/project-based free tier versus paid models and consumer subscription. | Research/model metadata adapter inspected: fixed official endpoint, no redirect, bounded response, metadata is not inference entitlement; live not tested. |
| Inception Mercury | Once-only trial, expiry unknown if unverified; no permanent-free claim. | Research/config/request preparation inspected: exact endpoints, regional/plan boundaries and free-only rejection; live not tested. |
| OpenRouter | Explicit free model IDs versus random free router; eligibility/rate limits; no paid fallback. | Research/manifests and GatewayRouteValidation inspected; fixed model/account/paid-extra checks are preparation helpers, not dispatch qualification. Live not tested. |
| Requesty | Organization pool, pinned free provider route and disabled chargeable fallback. | Research/manifests and GatewayRouteValidation inspected; fixed model/account/paid-extra checks are preparation helpers, not dispatch qualification. Live not tested. |
| Cloudflare Workers AI | Neurons, account ID and token, direct API usage; paid-only routes separate. | Research/manifests and GatewayRouteValidation inspected; fixed model/account/paid-extra checks are preparation helpers, not dispatch qualification. Live not tested. |
| Groq | Distinct from xAI/Grok; token/rate limits separate from context capacity. | Official-source research and bounded model metadata adapter inspected; account limits/remaining unknown; live not tested. |
| Mistral | Free API limits, paid API and subscription benefits separate; shared scope only if proven. | Official-source research and bounded model metadata adapter inspected; account limits/remaining unknown; live not tested. |
| Alibaba Model Studio | Region/workspace/endpoints; expiring per-model trials; Free Quota Only enforcement; coding-plan restrictions. | Research/config/request preparation inspected: exact endpoints, regional/plan boundaries and free-only rejection; live not tested. |
| Nebius TokenFactory | PAYG/trial; optional Builder application is not guaranteed credit. | Research/config/request preparation inspected: exact endpoints, regional/plan boundaries and free-only rejection; live not tested. |
| Synthetic | Subscription model/limit eligibility verified; no universal consumer-budget assumption. | Research and endpoint/protocol validator inspected. Synthetic weighted pool and xAI separate management credentials explicit; live not tested. |
| xAI/Grok | Direct API; no unverified general permanent-free budget. | Research and endpoint/protocol validator inspected. Synthetic weighted pool and xAI separate management credentials explicit; live not tested. |

Gateway source inspection also confirms that OpenRouter, Requesty and Cloudflare
manifests distinguish fixed free IDs, random free routing, organizational/account
scope, paid overflow, Neurons and unknown live state. Their helper validation is
not itself a dispatch boundary. At that initial checkpoint Host policy denied all
free-only and Core independently rejected it during startup. The later narrow
OpenRouter execution boundary below supersedes that deny-all implementation; a
positive catalog/helper result alone still does not qualify dispatch.

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

Source fixes in Host `d94a7cf` have now been inspected: the five-source allowlist,
effective reasoning, newline discard after oversized/gapped content, and monotonic
pending-buffer lifetime are present. Focused execution remains pending.
Provider preparation source reviews likewise do not qualify Host transport wiring,
account entitlements, successful inference or usable free-only execution.

## Actual Masonry render acceptance

Independently viewed real native captures from
`/tmp/lain-masonry-ui-evidence/evidence.json` (private Xvfb, PASS, 250 checks,
48 PNGs). The fixture assembly SHA-256 is
`B6F0C960BA6DE734A1F2729D3E5602B6F97E10E45A7F71FDAA6B56869E91B96B`.
This evidence qualifies that exact fixture binary. The separately inspected
installed binary and its different hash are recorded below.

Viewed closed Overview and Accounts at both 1120×780 and 800×560. The left
Z.ai card starts just below the shorter Codex card while the taller right Command
card continues; the prior row-height whitespace is gone. Names, window labels,
bars, remaining amounts/percentages, local resets and freshness remain legible.
Unsupported MiMo has explanatory text instead of an invented bar; exhausted Kimi
has a text badge as well as color. The harness independently asserts the 12 px
column gap, positive bounds, no tile overlap and preserved keyboard focus through
passive refresh.

Also viewed expanded Command, Z.ai, Kimi and Codex captures, including narrow
Command's sixth window and Z.ai technical provenance/amounts. Expanded cards
intentionally span the page width, which can leave space above them next to the
last shorter closed card; this is distinct from synchronizing every closed row.
Details and actions remain readable and later windows are reachable by ordinary
page scrolling. No targeted Masonry visual blocker was found in these captures.
This is layout acceptance, not Inspector UI or whole-expansion release acceptance.

## Installed Masonry checkpoint

The completed installed run at
`/tmp/lain-quota-installed-gui-check/evidence.json` is PASS with 22 native PNGs,
using private Xvfb and installed Desktop SHA-256
`7ca4fb2032f37e7c1eeb88b137d48afa4d18c35f6028f5d073d8e04c572d245a`.
The root preserved its report as
`tests/acceptance/evidence/masonry-desktop-installed.json`.
Independently viewed installed Overview, closed Z.ai and Kimi Accounts tiles,
expanded Kimi, Z.ai technical details, and reopened Usage. Actual provider windows
remain readable (two Z.ai, three Kimi); bars, percentages, reset labels and sources
match the captured Host observations. The independent-column layout remains
visible with real account data.

The installed harness retained one request, 81,986 tokens and cursor 2 before,
during and after a same-process window close/reopen; Host and proxy remained
reachable. This demonstrates that window reopening does not discard the captured
persistent Usage. It is not a service restart/migration test. The harness performs
GET observations and native navigation/expansion only; it reports no inference,
quota refresh, credential check, configuration write or service operation.

The separate `masonry-ui-installed.json` reports Desktop/CLI installation with
Host/Core, services, credentials and current normal Codex config bytes preserved
for that update interval. Its component source commits are Desktop `850cdc3`,
CLI `d9cfc2f`, Host `de872b7`; it reports zero inference and service operations.
This does not broaden preservation to earlier session intervals.

**Scoped judgment:** the rendered and installed Masonry UI change at `850cdc3`
is accepted as a standalone checkpoint, subject to packaging retaining the
qualified component hashes. This does not qualify new Inspector/analysis/provider
implementations or executable free-only routing. Packaging may preserve the
already-qualified fixed Core; the UI evidence alone does not justify a broad
expansion `releaseQualified: true` claim.

## Integrated source and existing harness checkpoint

Inspected the root's completed outputs under `/tmp/lain-expansion-tests`, rather
than running competing builds. Client/CLI passed 63 assertions; provider policy
42; transport 63; independent policy integration 169; direct APIs 60; trial
providers 85; NVIDIA/Step 40; Poe/OpenCode 50; Synthetic/xAI 41; and three Python
manifest tests passed. Gateway's initial build failed because its standalone
project omitted additive Core contracts. After `cd45ec3`, the separate retest and
run passed 37 assertions; the initial failing log is retained and not described
as a first-pass success. These are synthetic checks, not provider access tests.

Desktop Inspector source at integrated `414a5fb` uses source generations, explicit
request/attempt IDs and evidenced conversation/parent/response-chain references.
Tool chunks remain within a selected request/attempt. No timestamp/content/model
heuristic creates conversations. Two concrete issues were reported and accepted
for a follow-up: actual terminal state `success` was missing from the view's
terminal-state list; per-attempt parent lookup could split a retried logical
request when its attempts asserted conflicting parent IDs. Inspected Desktop fix `bf540d4`: per-request consensus keeps ambiguous,
conflicting or cyclic links under the original request; actual success state,
five-source evidence checks and monotonic receipt TTL are present. Clear follow-up `e3530b7` was also inspected: its cursor never regresses to a
stale snapshot and incoming entries at/below the cleared cursor are skipped. Core fix
`25ae0ed` stores separate monotonic receipt timestamps, closing the ring-retention
clock issue while leaving UTC observation timestamps for display.

## Narrow OpenRouter free-only qualification boundary

Root accepted the design boundary of a dedicated numeric zero-USD hard cap,
including BYOK, checked afresh with the actual outgoing key for every dispatch,
plus exact model/endpoint and final-payload controls. Source preparation
`b879bbd`/`307a6ee` is consistent with that boundary: only two explicit free model
IDs qualify; arbitrary suffixes/random routers do not; recursive duplicate JSON
keys, provider/model overrides, server tools, paid extras and unqualified fields
are rejected; final provider prompt/completion/request/image prices are zero,
fallbacks disabled, redirects/cookies disabled. Numeric zero is inspected without
floating-point underflow; null, missing, positive or failed cap proofs reject.

The official hard-cap/null distinction supports the chosen no-spend prerequisite.
The documentation explicitly preserves the remaining evidence limit: no live
zero-cap key has demonstrated successful free-model inference or behavior under
administrator-enforced billable defaults, and a remote policy change cannot be
atomically bound to the following inference call. This is not a live-usability
claim. Core must carry the trusted route pin through every retry and invoke the
final guard after all body/header transformations, immediately before dispatch;
preparation helpers or an inbound-body check alone do not qualify execution.
Other provider free-only mechanisms remain unsupported. Inspected Core
`4030184` with root final-send hooks `73f1d46`/`755f2e8`: the private pin is installed
at each executor attempt before the durable dispatch gate; exact auth/provider/
protocol/base/model checks and a second pin check precede execution; every final
compatibility HTTP send uses the guard after translation/customization. The proof
uses an isolated client copy and is excluded from inference response tracking.
No further static boundary blocker was found; the final focused Go execution and exact-binary synthetic qualification below now pass.

Further integrated logs passed 27 focused Host Inspector assertions and 80
independent fake-HTTP collector boundary assertions. They cover split credentials,
unknown/conflicting provenance, cursor gaps/restarts, active-capacity overflow,
byte bounds, monotonic lifetime and null/zero token distinctions. The updated
provider-policy harness passes 50 assertions and independent integration remains
169. Global loopback/bearer authorization, `Cache-Control: no-store`, and actual
Inspector service/endpoint wiring were inspected in root Program.cs.

Route/Profile UI source `01fb567` preserves optional policy/provenance fields and
unchanged selection order by editing original records. The policy-check wording
correctly distinguishes saved-configuration eligibility from a fresh upstream cap
proof or live access. Final policy-editor renders and mutation fixtures are accepted within the run-3 scope below.
Preliminary provider renders at 1120×780 and 800×560 are readable with explicit
prepared/live-unknown distinctions and eight navigation labels. Their first suite
stopped on a provider-search assertion; the reported event-loop timing fix and full
successful rerun are required before accepting that UI suite.

Further follow-up source review found no new blocker in Host `540b2af` (root
`ad29d94`): normalized metadata is checked for secrets after control/invisible
character removal; canonical generation IDs are required; held characters keep
their original monotonic receipt age through later chunks and terminal flushing;
expiry handles delayed events whose ages are older than earlier cursors. The fresh 32-assertion and 80-boundary-assertion results below certify this follow-up within their synthetic scope; the earlier 27-assertion result alone did not. Core `fc916d7` applies the same
normalize-before-secret-matching order and adds split/hidden-marker/final-flush
fixtures. Its new binary receives the fresh exact-hash synthetic qualification recorded below.

The data worktree's `/tmp/lain-usage-next-tests.log` reports 22 tests passed, zero
failed/skipped, for the Usage store suite. The focused final Go log
`/tmp/lain-core-expanded-go-final.log` passes `internal/lain`, `internal/freeonly`,
`sdk/cliproxy/auth` and `internal/runtime/executor`. The preceding Go run failed
two newly added auth fixtures using invalid request UUIDs; `042e302` corrects
those fixtures, and the rerun passes. That run precedes `fc916d7` and is not the
final binary's qualification report.

Staging-script source `a174f6e` was independently inspected without execution.
It requires separate nonexistent outputs, frozen source/locks/resources, exact
patch-2 Core/build/source/license/qualification binding and post-copy hashes,
while keeping broad release/expansion gates false. No static packaging blocker
was found. `backendQualified` inherits only the supplied independent report's
scope: focused Go tests must not be substituted for full exact-binary Core
qualification. Final archive/installed hashes remain separate checks.

## Final rendered expansion checkpoint

`/tmp/lain-expansion-ui-evidence-3/evidence.json` is PASS: 380 checks and 68 PNGs
on private Xvfb `:2`. It uses production MainWindow, UsageView,
ProviderCatalogView, InspectorView and App theme with in-memory HTTP only.
Exact Desktop fixture SHA-256:
`360CFFEB234E10F4FA112FA084C8FF2BE29A2E5436F17AE0287CC6B25FCBF23D`.
This is an actual rendered fixture, with routed Button Click events, not OS
pointer hit testing or an installed Host/provider integration. No real credential,
auth browser, inference or service mutation was involved.

Independently viewed Inspector, Usage analysis/unobserved coverage, provider
catalog/capabilities/setup, route policy and profile editor captures at 1120×780
and 800×560, plus narrow tool/restart/disconnected, Usage details/error and route
editor captures. No blocking visual defect was found. Narrow Inspector output is
visible immediately; its 760×142 inner viewport scrolls to remaining output while
the outer viewport has zero offset and no excess extent. Restart clears old
relationships/output with an explicit notice; disconnection marks active state
unknown and retained text as context. Actual success is terminal. Known request
relationships group only with evidence; conflicting retry parents remain separate.
The fixture's tool-result text is mocked Host data and does not prove actual Core
captures tool arguments/results; actual Core/Host exclude those payloads.

Analysis exposes whole-range P50/P95, measured/eligible counts and unknown groups;
narrow expanded details and incomplete-coverage qualifications are readable.
Providers visibly distinguish prepared/mock-tested from live-unverified, missing
accounts and advertised free offers from runtime free-only controls. Setup terms
are collapsed and long forms use ordinary page scrolling. The explicit route
policy dropdown was viewed at 1120; at 800 its lower policy section is below the
captured fold, so this review does not claim every lower label was visually seen.
Profile policy controls are visible at both sizes. Mutation fixtures cover
preservation of original policy, provenance, fallback IDs, order and consent;
configuration eligibility never implies a fresh cap proof or live access.

The first full run stopped on provider-search timing before the queued text event
(fixed by `f3453ba`); the second stopped on an Inspector fixture locator
(`06d36d4`). Run 3 is the complete passing rerun, not a first-pass success.
The later visual-only Usage header/filter alignment `f09f407` is outside this
run-3 fixture hash. The final rerun after `f09f407` and `57db173` is recorded below.

## Final exact-Core synthetic checkpoint

Independently read `/tmp/lain-core-patch2-final-go.log`: all four packages pass
(`internal/lain`, `internal/freeonly`, `sdk/cliproxy/auth`,
`internal/runtime/executor`). Also read `/tmp/lain-inspector-age-regression.log`:
32 focused Host assertions and 80 independent fake-HTTP boundary assertions pass,
including normalized secret matching and original per-character receipt ages.

The following final reports all pass and bind to Core SHA-256
`bab5ff3fdce446f3bae4c4aa495ef6b5d42a8d14f40306734f79368e5bb92d87`
(root source `42e2c3d`), rather than the superseded candidate:

- `/tmp/lain-final-core-qualification.json`: 15 synthetic backend cases,
  including 200-request replay/restart equality, retries, protocol/tool
  continuation, null/zero usage, credential binding revisions, crash and journal
  failures. Real OAuth/display-name lifecycle and provider probes are excluded.
- `/tmp/lain-final-inspector-core.json`: 15 synthetic cases covering provenance,
  conversation/turn/worker boundaries, cancellation, secret/payload exclusion and
  new-process generation without payload replay or durable-Usage alteration.
- `/tmp/lain-final-core-acceptance.json`: management/inference auth separation
  and kill-after-dispatch restart; exact binary unchanged, no invented completion.

The earlier current-consumer replay report shows 200 requests/attempts, cursor600
and 3,600 tokens, but predates this final binary. The separately inspected final-hash-bound current-consumer result below closes
that binding requirement. Actual packaged/installed migration/restart remains a
separate requirement; this review does not silently promote the earlier report.

## Final delivery fixture and consumer binding

`/tmp/lain-expansion-ui-evidence-final/evidence.json` passes all 380 checks with
68 PNGs after the final alignment/test refinements. Exact Desktop fixture SHA-256:
`2EDC5F348B3A8C67A7974AC21B1033F6567DCB20E9D4368A5A311F8669F89727`.
Independently viewed final Usage analysis at both widths, narrow Inspector and
wide route policy. Header alignment is coherent; sample counts remain readable,
narrow Inspector output remains immediately visible, and the wide route policy
selector is explicit. No new visual blocker. The preceding fixture-only and
below-fold limitations remain; this is not installed-binary visual evidence.

`tests/acceptance/evidence/expansion-core-consumer-replay.json` is PASS and binds
Core `bab5ff3fdce446f3bae4c4aa495ef6b5d42a8d14f40306734f79368e5bb92d87`,
source manifest, input journal and consumer DLL by full hashes. Ten replays each
reopen the store: cursor600, complete=true, 200 requests/attempts, 3,600 total
tokens, zero errors/unknown-usage attempts. This closes the synthetic final-Core
consumer binding, not actual installed migration evidence.

## Controlled upgrade-helper source review

Independently read `tests/acceptance/upgrade_probe.py`, its synthetic-test source,
`docs/upgrade-verification.md` and the owned state-path implementation in the
contracts worktree. No helper was executed by this reviewer. No source blocker
was found for the proposed default-path controlled upgrade. Plan-only performs
no service/install mutation. Execution pins the complete package/Core hashes,
checks the owned service unit/executable, stages privately, stops only
`lain.service`, verifies inactive PID/cgroup, copies the stopped DB/WAL/SHM,
full proxy journal/auth/configuration and other owned state, retains the whole
old app, swaps once and starts once. Verification uses authenticated loopback
GETs against fixed historical usage and new schema endpoints, with no inference
or second live SQLite read. Normal background refresh behavior can resume and
is explicitly not represented as zero provider requests.

Failures preserve private phase evidence and do not automatically rewind a
migrated database or rotated auth. Explicit rollback first archives current
stopped state and refuses changed durable journal/source or credentials/config;
only an unchanged generation permits paired old-app/old-state restoration.
Legacy service/config/auth and normal Codex are observed for interval changes,
not modified. Legacy temporary auth files are explicitly excluded, as are logs,
queues and databases; preservation must not be generalized beyond that scope.
The quiesced-client flag records a prerequisite rather than enforcing exclusion
of all external clients. Root must keep owned inference/configuration mutations quiet; the user's open Desktop stays untouched and ordinary read-only polling is allowed.

The initial rollback fsync follow-up is closed in inspected `96a926d`:
`sync_part` flushes every restored DB/WAL/SHM, credential/import/configuration and
proxy part; displaced state and rename parent directories are flushed before
restart. Phase records distinguish restoring/restored/flushed, and the injected
flush-failure fixture verifies no old Host restart. This is source/fixture review,
not a simulated power-loss result. Legacy `oauth/logs/**` is now pruned before
reading, and `*.tmp` files excluded. No further helper source blocker was found.
Root reports eight synthetic helper tests passed; no helper test was run by this
reviewer. Synthetic helper tests do not establish an installed upgrade/rollback.

The staged package at `/tmp/lain-expansion-delivery/lain-linux-x64-0.1.0-expansion`
(source `f713478`) has independently checked `SHA256SUMS` hash
`eb425f60052969fdb16879e2cc88020c963515fdc6fe426d6e36d3ef038f7c79`.
Its qualification explicitly binds final Core/source hashes and distinguishes
historical live references, synthetic tests and new-provider live `not_tested`.
`/tmp/lain-expansion-package-probe.json` reports seven PASS cases for actual
packaged executables, private installation, synthetic inference/Usage, CLI from
an unrelated directory, update/restart, rollback/restart, offline failure and
staged uninstall. It reports one synthetic and zero real provider calls, no
systemd operations, and explicitly `sameArtifactUpdateAndRollback: true`.
That last boundary matters: this does not demonstrate old-generation schema
migration on the user's installation. Controlled installed upgrade and subsequent
GET-only native evidence remain the next independent acceptance checkpoint.

## Valid pending-configuration preflight correction

The first real preflight rejected a healthy existing desired/applied revision
pair 4/2 before service stop or application/state replacement; root observed the
same old Host PID and old application afterward. This was the helper's incorrect
readiness assumption, not failed product startup or a completed upgrade. Opening
the advisory upgrade lock is not included in an unchanged-all-files claim.

Inspected fix `6c86d55`: readiness accepts `proxyReady=true` with valid integer
`0 <= appliedRevision <= revision`. Initial status must agree with the captured
configuration; immediately before stopping, status/configuration/usage are read
again and must match the baseline. Upgrade and rollback require the exact original
desired/applied pair afterward. No apply endpoint or configuration mutation was
added. This matches product startup, which binds runtime/active-profile to the
applied revision independently of pending desired edits. The runtime and active
profile file guards remain. The owner reports eleven synthetic helper tests
passed, including actual readiness parsing, invalid pairs and preserved 4/2
upgrade/rollback fixtures. No source blocker remains for this correction.

## Actual installed expansion checkpoint

Independently read `/tmp/lain-expansion-upgrade-result-2.json` and selected
non-secret verification fields from the private backup record
`upgrade-backups/20261023T230528Z-686dddd8/upgrade.json`: phase `verified`, PASS,
manifest `eb425f60052969fdb16879e2cc88020c963515fdc6fe426d6e36d3ef038f7c79`,
Core `bab5ff3fdce446f3bae4c4aa495ef6b5d42a8d14f40306734f79368e5bb92d87`.
The actual old-to-new whole-application upgrade and owned Host/Core restart
retained desired/applied revisions 4/2, one historical request/attempt,
81,986 tokens and cursor2. New analysis/schema queries succeeded. Configuration,
owned credentials, normal Codex, launcher/unit and protected stable legacy files
and service state remained unchanged during this controlled interval. Legacy
logs/temp files are excluded, and normal background quota/auth activity is not
claimed to stop. The helper issued zero inference requests. A stopped paired
backup exists; actual old-generation rollback was not exercised.

The final installed GET-only Xvfb report at
`/tmp/lain-quota-installed-gui-check/evidence.json` is PASS with 30 PNGs.
This temporary path has been reused; the earlier Masonry checkpoint is its
separately retained historical report, not the current file. The new run loads
installed production Desktop/Client/Core assemblies and uses the real local
Host API. Independently verified installed payload hashes match the staged
package, rather than inferring that from source commits:

| Component | Installed SHA-256 |
| --- | --- |
| Desktop DLL | `f6e27878441b099645c9557d10fc921ec2c0022922f45b842c59f7c4c92ffb4d` |
| Host executable | `6812c2aac14c4a712574f345e500f2382481b7d535273fab47041bd8d6115595` |
| CLI executable | `2dfbb1bea1b96dd5d4d8262a3f16bc64d0ce303e96d88c881511de05c69ee095` |
| Core executable | `bab5ff3fdce446f3bae4c4aa495ef6b5d42a8d14f40306734f79368e5bb92d87` |

Personally viewed installed provider catalog, connected-empty Inspector and
Usage analysis at 1120×780 and 800×560, provider details at800, reopened Usage,
closed Z.ai and expanded Kimi. No blocking visual defect was found. All fifteen
actual catalog providers are represented; prepared versus live-unverified and
missing accounts remain clear. Inspector is connected with zero streams and no
invented identities/content. Historical usage has one eligible attempt but zero
owned measurement samples; P50/P95 remain unknown rather than borrowing old
legacy timing. The installed two Z.ai and three Kimi windows remain legible,
with amounts/percentages, reset and freshness matching the captured API data.

The harness's same-process window close/reopen preserved one request,
81,986 tokens and cursor2 while Host/Core remained available. That is separate
from the real service restart in the upgrade report. Harness operations were
GET-only observations, navigation/expansion and read-filter application: no
inference, credential validation, quota refresh, configuration mutation, login
or official-link/browser action. The user's existing Desktop was not closed;
these captures use private Xvfb. Active streaming/tool rendering remains covered
by the synthetic exact-Core/Host/UI fixtures, not fabricated installed traffic.

## Evidence ledger and final outcome

This reviewer ran no builds, tests, installation or service actions: execution
was serialized by root, and the evidence and actual renders were independently
inspected. The installed expansion checkpoint is accepted for the qualified
scope: exact-Core synthetic execution and durability, installed migration/restart
and historical Usage preservation, installed read-only UI, prepared onboarding
and mock-tested provider adapters. No remaining reviewed blocker prevents final
scoped release metadata/archive creation, provided final checksums retain the
qualified executable payloads and these explicit evidence limits. A broad claim
that every provider is live-tested or every workflow was exercised on the user's
installation is not accepted. Final archive identity/checksum verification is a
delivery step, separate from this installed acceptance.
All 15 new provider integrations remain live `not_tested`: documentation,
prepared setup and mock-tested execution are useful accepted scope without new
keys or live calls. Earlier Z.ai/Kimi/quota observations are historical evidence
for the existing integrations and Masonry checkpoint, not new expansion probes.
No live OpenRouter zero-cap usability claim is accepted. Release acceptance within this declared scope does not require inventing a live
test or acquiring new credentials. Actual zero-cap upstream usability, real
owned-OAuth rotation and actual old-generation rollback remain untested.

The preceding quota review's normal Codex preservation statement was separately
corrected in `2ebcf8f`: the 23:58 Desktop/CLI update preserved the bytes it started
with, while the earlier 20:50 baseline mismatch/current 21:13 modification remains
unattributed. Do not copy a whole-session unchanged-files claim into this release.
