# Ordered collection routing v1

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
