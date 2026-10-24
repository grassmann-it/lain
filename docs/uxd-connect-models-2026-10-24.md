# UXD-03/04: Connect and Models

Source delivery extends the existing short Connect implementation. Validation of
this follow-up is pending the integrator's serialized build/runtime slot. Prior
26-check evidence in the stabilization artifact directory predates this follow-up.

Connect starts with the provider, then opens the preferred installed and currently
available method. Other methods are local alternatives with their actual host
requirements; unavailable methods cannot submit. Back retains only name and
nonsecret resource values; private input is cleared on method/provider changes.
Kilo key setup remains name plus private key without a model or plan requirement.
Discover and Add account use one inventory projected from provider documentation
and installed methods. Method-only providers gain no invented pricing, credential
or capability facts. The installed method endpoint decides which connection is executable.

Browser state uses POST accounts/login once, then GET accounts/login/{id} on the
same operation. Continue in browser starts that operation and opens its URL once;
Reopen browser only reopens that URL. DELETE accounts/login/{id} cancels local
polling before the request. Late responses from a cancelled/replaced polling owner
cannot turn the current page into a successful connection. Expiry, unsupported,
resource selection, failure and cancellation have separate local guidance. The
browser launcher is an injectable in-memory fixture seam; no real login is claimed.

| Method family | Local executable condition | Missing prerequisite stays local |
| --- | --- | --- |
| Existing API key | Installed method reports available and offers the requested purposes | No quota capability is inferred from inference-only keys |
| Dedicated monitor token | Installed quota method and its actual account/project fields | Unsupported scopes remain unavailable |
| Codex owned browser | Host advertises available plus Core login in Gateway | Monitor without Core uses an actual supported alternative, such as an explicit snapshot |
| Access-token snapshot | Host offers quota-only snapshot; future expiry and account identity supplied | No refresh, revoke or inference capability is added |
| OpenRouter issued key | Host exposes the qualified browser method | User authorization remains explicit; no purchase or inference |
| Poe browser | Host exposes available after maintainer client/callback qualification | Setup-required guidance and existing-key alternative remain visible |
| Google/Cloudflare browser monitoring | Host exposes available after its client/scopes/callback qualification | Existing supported token-based monitoring is separate |
| Managed/device/console paths | Only exact available methods reported by the installed host can start | Unsupported methods show their own qualification guidance; logos never enable them |

This table describes admission, not live qualification. The host method catalog
is authoritative when configuration or qualification changes.

The bounded completion card groups account identity, credential presence and only
observed capability/quota results. Credential completion is separate from quota
observations and inference success.
Done saves no models. Optional model setup and an account's Models context open
one template picker. GET providers supplies exact connection IDs, template IDs and
pricing facts. POST accounts/{id}/models uses RevisionRequest with account generation,
connection, selected template IDs and only accepted nonsecret settings. It sends
no key, account replacement, Apply, inference or purchase request. Single-plan
connections need no tariff control; multiple plans require existing-access choice.
Contributor training permission starts unchecked and exists only for the actual
Contributor route. Configured templates cannot be selected again. Manual model
access remains available.

Models is a bounded table with aligned native rows and direct Edit/actions.
Filter controls are local. Selection uses stable route IDs; wide view reserves an
independent detail pane, narrow view opens a separate detail task and restores the
inventory focus/offset on close. Detail links preserve distinct account, route and
profile identities. Saved Enabled and menu Visible remain independent: disable
requires global Apply; Hidden changes model menus and does not block direct calls.
Menus share commands for details, editing, access/menu state, Usage, profile uses,
accounts, copy and removal. Removal names profile/default and fallback references,
blocks referenced removal and preserves historical usage.

The host supplies global revision/applied revision, not a cold-start per-route
applied snapshot. Global pending is shown honestly. Per-row pending badges only use
successful local saves whose revision is newer than the applied revision.
Unknown prices/capabilities remain unsupplied. The route editor preserves upstream,
provider, provenance, strict/pool binding, enabled/visible and complete spending
policy. Explicit fallback positions and paid/unknown fallback permission are editable;
free-only clears and disables paid fallback permission. Host validation remains
final for cycles, removed IDs and spending controls. Existing defaults remain
profile-owned and are identified in the detail's named profile uses.

Shared helper signatures for Profiles remain unchanged: CompactRouteButton,
SpendingPolicyEditor construction/Control/Resolve, SpendingPolicyCheck,
ProfileHeader and ProfileWrap. Ui.Viewport and explicit ProfileId navigation are
provided by the shared shell owner. The authoritative narrow/long-name, lifecycle,
conflict and native input matrix is UXD-09; no physical-pointer, screenreader or
live-provider qualification is inferred from routed fixture actions.
