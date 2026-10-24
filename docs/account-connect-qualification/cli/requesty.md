# requesty CLI qualification

Status on 2026-10-24: **disabled; no real client executed or qualified**.
The disabled manifest is [requesty.json](requesty.json). Unknown fields are explicit
nulls, not a runnable configuration or a user-facing enable flag.

Public source/reference: https://github.com/requestyai/cli/tree/21809398e99d888565178d718186b637f3de4566

Source-backed candidate commands: `login --profile <owned>`, `login --profile <owned> --force`, `auth token --profile <owned>`.
These strings document candidates, not runtime approval. Real adapters are
unregistered by this package. Missing CLI never triggers download or installation.

Isolation: UNQUALIFIED: configPath uses os.UserHomeDir; --profile names a block in shared settings, not a separate store.

Output boundary: one API key followed by newline; no banner or extra JSON.

Blocking evidence: A pinned Linux artifact/hash/version plus supported complete private storage are missing. Source config.go exposes no dedicated config directory. Do not redirect HOME or execute against the ordinary Requesty store. Confirm same key/group with Self metadata before same-account reconnect.

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
