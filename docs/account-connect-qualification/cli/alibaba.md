# alibaba CLI qualification

Status on 2026-10-24: **disabled; no real client executed or qualified**.
The disabled manifest is [alibaba.json](alibaba.json). Unknown fields are explicit
nulls, not a runnable configuration or a user-facing enable flag.

Public source/reference: https://github.com/modelstudioai/cli/tree/3c8031f4d994c30820eb4d145d9404ba4a0acd39

Source-backed candidate commands: `auth login --console --console-site international --config lain`, `console call --api <fixed-read> --data <nonsecret-payload> --config lain --output json`.
These strings document candidates, not runtime approval. Real adapters are
unregistered by this package. Missing CLI never triggers download or installation.

Isolation: Source-supported BAILIAN_CONFIG_DIR plus explicit --config; binary/runtime identity and callback isolation still unqualified.

Output boundary: owned config.json active_config=lain; exact named block api_key/access_token/base_url/workspace_id/console_site/console_region; raw Console envelopes.

Blocking evidence: No verified published artifact/hash/version or authorized callback/isolation result. Full shared publication needs independent static inference key and delegated Console reference. Mixed/quota-only login is disabled before bootstrap. Grant active-status vocabulary is unqualified; default status allowlist is empty, so no numeric allowance is claimed.

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
