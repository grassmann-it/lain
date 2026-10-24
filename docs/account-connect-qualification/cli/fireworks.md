# fireworks CLI qualification

Status on 2026-10-24: **disabled; no real client executed or qualified**.
The disabled manifest is [fireworks.json](fireworks.json). Unknown fields are explicit
nulls, not a runnable configuration or a user-facing enable flag.

Public source/reference: https://docs.fireworks.ai/tools/firectl-user-guide

Source-backed candidate commands: `signin (isolation unqualified)`, `account list -o json (after signin qualification)`, `api-key create --key-name lain-… -o json (requires separate authorization)`.
These strings document candidates, not runtime approval. Real adapters are
unregistered by this package. Missing CLI never triggers download or installation.

Isolation: UNQUALIFIED signin storage; a later --profile is insufficient.

Output boundary: UNQUALIFIED JSON secret field, principal/account identity, expiry and explicit ordinary non-Fire-Pass type.

Blocking evidence: No signin/profilestore isolation evidence or pinned artifact. Never touch ~/.fireworks/auth.ini, signout, --all-users or key creation. Separate F1 isolation and F2 one-key authorization are required; no SSO session is imported into the ordinary key owner.

Local qualification uses a separate synthetic executable and new temporary owned
profiles only. It exercises process arguments/environment, bounded private pipes,
deadlines/tree cleanup, journal/reference permissions, same-context replay,
resource mismatch, serialized reconnect and crash reconciliation. It does not
qualify this manufacturer's executable, login or inference rights. Local results
are recorded in [the shared evidence](README.md).

Next real qualification requires a separately authorized pinned-tool probe:
verify source/release/artifact/license and exact version, prove ordinary config
unchanged with bait entries, validate only the freshly created own profile/key,
confirm provider identity/resource and cancellation/reconnect behavior. No model
prompt, purchase, serviceaccount, global logout or hidden key creation is needed
or authorized by this report.
