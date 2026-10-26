# Ordered collection routing v1

## Accepted extension, 26 October 2026 — source tested, deployment pending

The user explicitly revises the previous no-fallback rule below: automatic
groups should skip members with a fresh, applicable exhausted-quota snapshot
and move to the next configured member after a quota rejection before any
client-visible answer/tool output. GPT-6 Luna is requested as a reserve in IT Sec.
This is an implementation requirement, not an installed-runtime claim.

Cached provider quota snapshots remain distinct from local request/token history.
Selection consumes a small derived availability snapshot, not inferred remaining
tokens from local history. Snapshots must retain account/model applicability,
observation freshness and reset boundaries; stale or unsupported information is
unknown, never a permanent exclusion. No provider request is sent just to choose
a route. Refresh uses the existing quota-monitoring cadence.

The Core/Host bridge is management-authenticated, bounded and tied to the exact
running Core generation; complete replacement updates use monotonic sequence
numbers. Route fingerprints bind exclusions to compiled members. Old or delayed
snapshots cannot override new state. Each exclusion expires no later than its
original freshness/reset deadline. A restarted or unavailable bridge starts
unknown, without blocking direct inference or usage monitoring.

Fallback is bounded to eligible configured members. It preserves explicit account
selection, transport/tool compatibility and each route's effective cost policy.
It never buys extra usage or silently enables a paid replacement. No route change
after visible output/tool events. Attempt records must identify each actually
contacted model/account separately from the requested group alias. Exhaustion
must return an explicit failure instead of looping. Conversation pin migration,
concurrency and durable restart behavior require targeted qualification.

Implementation rule for conversation migration: successful turns retain their
durable pin. A later complete-history turn may move forward in group order for
quota exhaustion before any executor payload, client byte or flush. Stored
references and unpinned continuations remain rejected. Changed, removed or
credential-unavailable pins fail closed rather than being reinterpreted as quota.
Same-thread requests serialize through HTTP completion; moving the pin uses a
durable compare-and-swap before another dispatch. A new conversation can choose
earlier members again after their quotas recover.

Later pin migration supports full user/assistant message history and paired
function_call/function_call_output records with matching call IDs. A latest-user
fragment, tool-output-only continuation, opaque reasoning or native/custom tool
record cannot migrate: HTTP409 `router_replay_required` explains the supported
replay format and asks for a new thread. Records are never silently stripped.
Ordinary continuation on the existing route is unchanged. The gateway can
validate supplied records, but cannot prove that a caller omitted no messages.

The Host republishes derived availability every15 seconds, without refreshing
providers. Initial supported quota evidence is limited to proven legacy Kimi
Coding/Z.ai Coding credentials using the same quota/inference key. Other
providers, ambiguous bindings and stale readings stay unknown. Original
observation age is capped at5 minutes and the reset deadline; repeated publication
does not renew it. Monitor mode and a stopped Core make no availability HTTP
calls. The bridge and inference-error cooldowns are memory-only and start unknown
after Core restart. Existing quota monitoring supplies the cached observations.

Fresh cache exclusions are route-specific. Generic429 rejection cooldowns are
model-specific; only recognized account-wide quota codes can suppress sibling
aliases of that account. Rejection cooldowns default to30 seconds, honor bounded
`Retry-After` and never exceed5 minutes; replacement credentials do not inherit
them. Each real dispatch has its own attempt UUID under the parent request.

Live evidence before implementation: one 569-byte, zero-tool IT-Sec request at
19:34 UTC selected Kimi `k3` and returned HTTP429, `exceeded_current_quota_error`,
5-hour window exhausted. The public attempts API independently identifies the
same request, route and account. Both Kimi and Z.ai reported exhausted 5-hour
windows; remaining longer windows do not remove the short-window constraint.
IT Sec now saves `k3, kimi-for-coding, glm-5.3, gpt-6-luna` at revision58;
runtime remains revision57 pending qualified activation.

The historical installed contract below describes the preceding implementation;
its no-quota-probe/no-cross-route-retry statements are superseded only by this
explicitly requested bounded extension.

**Current status, 25 October 2026:** this contract is implemented, synthetically
qualified and installed as recorded in the
[model-group delivery report](codex-model-groups-delivery-2026-10-25.md).
Free automation remains inactive. The introductory preparation-stage description
below is historical; the routing rules remain the contract.

This source adds an explicit, profile-bound `ordered_sticky_v1` selector. It is
not release qualification: the new Host/Go fixtures and built runtime still need
the serial synthetic qualification lane before deployment. No existing user
configuration, provider, installed binary or normal Codex profile is changed.

The user explicitly confirmed selection in their model order and keeping the
selected model for the conversation on 25 October 2026. Collection.RouteIds
provide custom order; Profile.RouteIds provide provider-group order. This is the
agreed rule, not an assumed classifier or error-based model fallback.

`ConfigurationDto.CollectionRouters` is an optional additive array. Each record
has `id`, `alias`, `groupId`, `profileId`, `costPolicy` (`configured` or `free_only`)
and `enabled` (default true). GET `/api/v1/collection-routers`, PUT
`/api/v1/collection-routers/{id}` with `{revision,value}`, and DELETE the same
path with `?revision=N` use the shared configuration revision gate. Writes are
saved only; applying a profile and regenerating its Codex export remain separate.

Groups are `collection:<id>`, `provider:<canonical-lowercase-provider>`,
`account:<stableAccountId>` or `policy:free`. Automatic provider views appear first,
sorted by their readable provider names, followed by custom collections in saved
order (for example `1M` and `ITSec`); collection member order is retained. With one
exported account, the provider label stays short. An explicitly configured active
account router retains its account group when the profile shrinks to one account,
so its menu action remains available. With multiple active exported
accounts, strict single-account routes are split into `Provider (Accountname)`
groups. Pooled routes remain visible in a separate `Provider (Pool)` view and are
never attributed to one account. Individual model labels also identify the account
in this case; inference aliases stay unchanged. Disabled, hidden, internal-review
and foreign-profile routes do not create extra exported account groups.

Account groups are revisionless derived views over existing route IDs. A saved
provider router becomes ambiguous when a second eligible account enters its
profile: compile, apply and grouped export fail closed with an account-group
selection error, while account creation and ordinary export remain available.
Explicit custom collections may span providers or accounts. No automatic provider
router silently joins private and work accounts.

Collection order comes from its existing RouteIds; provider, account and
Free order comes from the profile. A free group requires `free_only`. A router
may reference only enabled, visible, single-account strict routes in its own
profile with matching compiled transport provenance and OpenAI Chat or Responses
transport. Internal approval-review models are excluded. Empty eligible sets
reject application before drain. Existing route aliases and accounts are shared,
never copied. Collection RouterTargetId may reference the matching explicit router.

Compiled `bindings.routers[alias]` contains `id`, `profileId`, `selection`,
`costPolicy`, ordered `routeIds` and `memberFingerprints`. The opt-in grouped
Codex export adds one virtual model and references it from the group's
`router_model`. A pure export without compiled router proof refuses to advertise
an executable router. The in-process export proof comes from the real
RouteBindings compiler and binds the resolved profile, revision, effective routes,
collection order, router policy and logical accounts. A stale proof is rejected;
callers cannot substitute an arbitrary router dictionary. Ordinary exports do
not compile routers and remain available even if a saved router is not executable.

Configured routers accept actual compiled native Codex and imported Chat/Responses
transports without inventing a catalog model identity. Missing, mismatched or
unknown transport evidence is insufficient. Anthropic and Gemini remain outside
v1. Free routers additionally require qualified catalog identity and the existing
runtime cost enforcement; imported or native configured routes never borrow it.

The gateway authenticates a router request before reading or creating a pin.
Only `/v1/responses`, complete history and one canonical UUID `Thread-Id` are
accepted; Session-Id and payload-derived identities are not substitutes. Stored
responses, conversation IDs, item references and store:true are rejected. A new
router selection is supported only for a new conversation: assistant/tool or
other continuation history without a durable pin is refused. Selecting an
autorouter in the middle of an existing unpinned conversation requires starting
a new thread. There is no classifier, quota probe or implicit history migration.

Before the first dispatch, selection walks the compiled order and checks loaded
credentials, registered models, explicit account pins and existing negative
tool/input constraints. Unknown capabilities remain unknown; this does not claim
provider acceptance. The first eligible route is fixed before inference. A
provider error, quota rejection or zero-cap failure is returned; no second member
is tried, before or after output. Follow-up requests must retain the same route,
account and semantic binding fingerprint; removed, changed or unavailable pins
fail closed. Final outgoing model verification and redirect rejection prevent
payload rules from changing the pinned model.

Free routing requires each actual selected route's effective policy already to
be `free_only`. It cannot strengthen a configured route merely by relabeling it.
The existing qualified OpenRouter credential, fresh same-key zero-cap proof and
final model/endpoint/cost guards remain in force. Unknown or paid routes never
enter a Free router.

The journal and usage schema remain unchanged: RequestedModel is the router
alias, RouteID and AccountID identify the selected real route/account, and normal
request/attempt identities and tool payloads pass through unchanged. Pins are a
private sidecar `<journal>.router-pins.json` with schemaVersion, sourceId and only
router/profile/thread/route/account IDs plus a binding hash. It is bounded at
4096 entries and 4 MiB, serialized under a mutex, fsynced and atomically renamed.
There is no eviction or automatic reset. The existing exclusive journal process
lock owns the sidecar lifecycle. An initialized marker and historical router
attempt check prevent silent recreation after state loss. Corruption or missing
state disables routing; individual model routes remain available. Recovery is
an explicit operator operation, not another Apply or automatic deletion.

Synthetic coverage is in `tests/models/RouterCases.cs` and
`proxy/adapter/internal/lain/collection_router_test.go`. Run existing profile
application, free-only, tool continuation, durable usage and stream-boundary
suites as regressions. Source formatting and manifest verification alone do not
establish runtime qualification or authorize installation.
