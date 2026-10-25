---
name: lain-usage
description: Inspect lain service status, accounts, models, usage and quotas through its CLI, and perform explicitly requested lain configuration actions. Use for lain operations, not general Codex settings or management of another proxy.
---

Use the installed `lain` command (normally `~/.local/bin/lain`); it works from any
working directory. `LAIN_HOME` selects a separate installation/data directory.
Read `lain --help` for the installed command contract before using a command not
shown here. Use `--json` for structured inspection, stdout for results and stderr
for diagnostics. Never scrape the GUI, read the SQLite database directly, consume
a proxy usage queue or start an alternative collector.

The current source implements explicit Monitor/Gateway operation; the installed
version may be older. Inspect installed help first. `lain mode --json` reads the
configured operating intent when available; never infer Monitor solely from an
unready proxy. Monitor does not start Core/inference or apply/export profiles,
but can read available saved history. Missing Gateway features are not a Monitor
health failure. Keep operating mode separate from UI information level/density.

Supported read operations:

```sh
lain status --json
lain accounts --json
lain mode --json
lain accounts methods --json
lain models --json
lain profiles --json
lain usage --json
lain usage --full
lain quotas --json
lain history --today --timezone Europe/Berlin --json
lain history --from 2026-10-22T00:00:00Z --to 2026-10-23T00:00:00Z --timezone UTC --json
lain attempts --account ACCOUNT_ID --offset 0 --limit 100 --json
lain history analysis --today --timezone Europe/Berlin --group-by modelFamily,accountId,reasoningEffort --group-limit 100 --json
lain history performance --today --inference-provider PROVIDER --json
lain inspector snapshot --limit 100 --json
lain inspector events --limit 100 --json
lain inspector events --source SOURCE_ID --after CURSOR --request REQUEST_ID --limit 100 --json
lain providers catalog --json
lain providers onboarding PROVIDER_ID --json
lain providers routes --provider PROVIDER_ID --json
lain providers check-free CONFIGURED_ROUTE_ID --json
```

Bare `lain usage` shows current quota snapshots for every configured account,
including actual account names/providers, disabled accounts and unavailable
quotas. Accounts with readings lead. Its compact view shows at most two primary
readings per account, with shorter windows first (5 hours before 7 hours or
7 days). Monthly allowances are secondary; positive additional credits appear
as concise amounts and zero-only extras are hidden. A sole monthly allowance or
wallet and a real exhausted main balance remain visible. Unknown readings never
become zero; unknown secondary windows stay in `--full` when a main reading exists.
Native remaining units, reset times, shared-budget tags and refresh errors stay
visible. Displayed numbers older than five minutes since the last successful
check, or with unknown check time, stay last known. Use `lain usage --full` for
every window, including zero extras and missing readings, plus IDs, credential
diagnostics, source/check times and full quota details.
`lain usage --json` returns a CLI envelope with `version: 1`,
`kind: "account-usage"` and `accounts`, each containing the full `account` DTO
and its `quotas` array. Missing snapshots remain empty arrays. It reads accounts
and cached quotas through the Host; it does not refresh providers.
`--full --json` returns the same envelope and bytes in original Host account/window order; `--full` cannot be combined
with historical arguments or the `history` command.
`lain quotas --json` remains the raw quota DTO array from a single GET.
Quota `support` describes a capability separately from `error`. Missing quota
support, required monitor setup, or a passive source awaiting its first ordinary
request is neutral guidance, not a failed provider request. Preserve `windowId`,
measurement kind, model/scope and source timestamps. A usage reading without a
limit is not remaining budget; an approved ceiling is not the enforced limit.
Passive Groq polling never sends inference and never renews the observation age.
Use `lain history` for proxy request/token history and performance. Explicit
historical `usage` arguments, such as `usage --today`, `usage --from ...` and
`usage analysis`, remain supported aliases with their existing JSON contracts.

History and attempts also accept `--model ID` and `--status HTTP`; bound and paginate
attempt lists (limit 1–500). History intervals are at most 366 days. Extended exact
filters are `--route`, `--model-family`, `--manufacturer`, `--inference-provider`,
`--gateway`, `--billing-account`, `--reasoning`, `--mode`, `--conversation`, `--turn`,
`--worker`, `--harness`, `--profile` and `--effective-reasoning-effort`, each
followed by a value. `--reasoning` is the requested effort; effective reasoning
records the upstream translation only when observed. Use installed
help to check which commands the running version supports before inspecting the
expanded contract; development does not imply the installed service was upgraded.

`history analysis`, `history aggregate` and `history performance` return the same
server aggregate, including measured performance and grouped usage. They do not
accept attempt pagination. `--group-by` takes 1–3 distinct comma-separated keys:
`accountId`, `routeId`, `model`, `provider`, `modelFamily`, `manufacturer`,
`inferenceProvider`, `gateway`, `billingAccountId`, `reasoningEffort`, `mode`,
`effectiveReasoningEffort`, `conversationId`, `turnId`, `workerId`, `parentWorkerId`, `parentRequestId`,
`toolCallId`, `harness`, `profileId`, `responseId`, `previousResponseId`.
The default is `modelFamily,accountId,reasoningEffort`; `--group-limit` is 1–500.
Preserve null dimension keys, truncated groups, sample/eligible counts, metric
definitions and rate basis. Do not compute medians or totals from attempt pages.
For comparisons, reuse fixed `--from`/`--to` values and timezone across commands;
each requested dimension is a separate breakdown, not a combined grouping.

Inspector reads are bounded snapshots of the owned core's local live buffer.
Snapshot limits are 1–256; event page limits are 1–500. The Host may expose lower
configured limits. Reported Host display retention does not describe the
separately bounded source ring's lifetime.
Keep `sourceId` with every cursor; `--after` greater than zero requires `--source`.
Use the returned `nextCursor` even for a filtered empty page. On `source_changed`
or another gap, record the interruption, adopt the returned source/cursor pair
and refresh the snapshot; never silently join generations.
Report gaps, dropped events, incomplete coverage, truncation and retention times.
Use explicit dimension evidence for conversations, turns, workers and tool
continuations; missing identities stay unknown. Events are not independent chats.
Text output caps each retained event preview at 2,000 characters; JSON preserves
the bounded response. Do not persist payloads as usage history or start a second
collector.

Provider inspection is read-only. Onboarding shows account links, credential field
names and setup instructions without creating accounts. Keep model manufacturer,
inference provider, gateway and billing account distinct; route capabilities can
be unknown. Keep free, trial, subscription and PAYG budgets and their native units
separate. `providers check-free` evaluates a configured route without changing
policy: unknown-priced and paid routes must stay blocked under `free_only`.
Unverified live connections stay unverified until an authorized real check occurs.
The cost-policy response checks configuration; actual dispatch still requires
qualified provider-side controls. The narrow OpenRouter implementation requires
an exact allowlisted free model and a fresh same-key zero-USD cap check including
BYOK on every dispatch. A local boolean or cached quota does not satisfy it, and
a zero-cap key may deny free requests too. No live usability is implied.

Honor response coverage, observation time and unknown/null values.
Do not invent token totals, infer independent budgets from multiple credentials,
or present shared-with-legacy quotas as isolated budgets.

Exit codes: 0 success, 1 unexpected error, 2 invalid command/options, 3 host
unavailable, 4 authorization failure, 5 rejected operation/conflict. On an
unavailable host report that state; `lain service status --json` may help diagnose
it. Start/stop/restart the service only when requested via
`lain service start|stop|restart --json`. Opening the UI uses `lain ui`.

For an explicitly requested account action, use the CLI mutation path:

```sh
lain accounts add
lain accounts monitor ACCOUNT_ID
lain accounts login codex --revision N --json
lain accounts login-status LOGIN_ID --json
lain accounts login-cancel LOGIN_ID --json
lain accounts check ACCOUNT_ID --json
lain accounts enable ACCOUNT_ID --revision N --json
lain accounts disable ACCOUNT_ID --revision N --json
lain accounts remove ACCOUNT_ID --revision N --json
lain accounts add-api --name NAME --provider PROVIDER --base-url URL --key-env ENVNAME --revision N --json
```

`accounts add` and `accounts monitor` are interactive private TTY wizards for the
user. Do not ask for tokens in chat, read them from their store, or run an unseen
prompt. Account setup selects a connection independently from optional model
templates; an empty model selection saves only the account. Monitoring access
belongs to the same account and is never sent to inference. External monitoring
snapshots are not refreshed or revoked. Saving a token is not a live access test.

`add-api` also accepts `--key-stdin` for a single input line. Never place API keys
on argv, in generated command text, or reports. Prefer an already populated
secret environment variable; do not print its value. Own Codex login is a
separate operation from importing an external access-token snapshot. Poll only
the requested login operation, and honor cancellation.

Catalog-backed providers use `--provider-route CATALOG_ROUTE_ID`; retain the
exact endpoint and protocol shown by onboarding. Repeat `--setting NAME=VALUE`
for declared provider fields with `secret: false`, or nonsecret `accountId`,
`region`, `workspaceId`, `serviceDeploymentScope` or `billingPlan` values required
by the provider validator. Settings require a catalog route;
keys and other secret values must use the dedicated secret input path. Host
validation remains authoritative for supported protocols, routes and settings.
Preparing an account does not establish live provider verification.
Optional Contributor training routes require the declared nonsecret
`allowTrainingData=true` workspace privacy permission; never supply that opt-in
without the user's authorization. It does not authorize paid usage or bypass
free-only controls.

An explicitly requested operating-mode change uses `lain mode monitor --revision N
--json` or `lain mode gateway --revision N --json`, after reading status/current
configuration. It may stop/start Core or interrupt active requests; it preserves
accounts/history/routes and is not a profile Apply. Re-read/review on conflict,
never retry blindly using a newer revision. An inspection request authorizes no
mode change, provider login or purchase. These commands require installed help
support; the source contract alone is not installed-version evidence.

Backup/restore and package changes are maintenance tasks, not read operations.
Do not invoke historical upgrade scripts against default real user paths merely
to inspect usage. Full stopped-state restore creates a recovery epoch; rotating
grants may be spent and require explicit recovery/reconnect. Do not remove the
marker or resurrect a previous refresh token. Current static accounts/history
can remain usable. Ordinary Codex config and other proxy installations stay
outside the maintenance scope unless explicitly authorized.

Configuration mutations require a user request for that action. A request to
inspect usage does not authorize importing accounts or changing routes. Imports
use `lain import /absolute/config.yaml --auth-dir /absolute/auth --revision N
--json`; inspect current revision first and re-read state on conflict rather
than forcing a write. `lain profiles apply ID --revision N --json` applies a profile through the host.
`lain profiles install-codex ID --revision N --json` privately replaces the
separate `~/.codex/lain.config.toml` and `lain.models.json` using the absolute
`~/.local/bin/lain` auth helper, which must exist. It preserves the ordinary Codex
config; the two artifact replacements are not a single transaction. Use installed
help for other profile/route/client binding mutations. Keep the ordinary Codex configuration and other proxies unchanged;
lain's Codex integration uses its separate config/profile.

`lain auth token --audience codex` returns an inference credential in plaintext
for the authorized local harness helper. Do not call it for inspection, include
its output in reports or logs, or expose host-management credentials. Imported
external OAuth snapshots must not be refreshed or revoked by this skill.
