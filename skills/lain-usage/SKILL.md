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

Supported read operations:

```sh
lain status --json
lain accounts --json
lain models --json
lain profiles --json
lain quotas --json
lain usage --today --timezone Europe/Berlin --json
lain usage --from 2026-10-22T00:00:00Z --to 2026-10-23T00:00:00Z --timezone UTC --json
lain attempts --account ACCOUNT_ID --offset 0 --limit 100 --json
lain usage analysis --today --timezone Europe/Berlin --group-by modelFamily,accountId,reasoningEffort --group-limit 100 --json
lain usage performance --today --inference-provider PROVIDER --json
lain inspector snapshot --limit 100 --json
lain inspector events --limit 100 --json
lain inspector events --source SOURCE_ID --after CURSOR --request REQUEST_ID --limit 100 --json
lain providers catalog --json
lain providers onboarding PROVIDER_ID --json
lain providers routes --provider PROVIDER_ID --json
lain providers check-free CONFIGURED_ROUTE_ID --json
```

Usage and attempts also accept `--model ID` and `--status HTTP`; bound and paginate
attempt lists (limit 1–500). Usage intervals are at most 366 days. Extended exact
filters are `--route`, `--model-family`, `--manufacturer`, `--inference-provider`,
`--gateway`, `--billing-account`, `--reasoning`, `--mode`, `--conversation`, `--turn`,
`--worker`, `--harness` and `--profile`, each followed by a value. Use installed
help to check which commands the running version supports before inspecting the
expanded contract; development does not imply the installed service was upgraded.

`usage analysis`, `usage aggregate` and `usage performance` return the same
server aggregate, including measured performance and grouped usage. They do not
accept attempt pagination. `--group-by` takes 1–3 distinct comma-separated keys:
`accountId`, `routeId`, `model`, `provider`, `modelFamily`, `manufacturer`,
`inferenceProvider`, `gateway`, `billingAccountId`, `reasoningEffort`, `mode`,
`conversationId`, `turnId`, `workerId`, `parentWorkerId`, `parentRequestId`,
`toolCallId`, `harness`, `profileId`, `responseId`, `previousResponseId`.
The default is `modelFamily,accountId,reasoningEffort`; `--group-limit` is 1–500.
Preserve null dimension keys, truncated groups, sample/eligible counts, metric
definitions and rate basis. Do not compute medians or totals from attempt pages.

Inspector reads are bounded snapshots of the owned core's local live buffer.
Keep `sourceId` with every cursor; `--after` greater than zero requires `--source`.
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
lain accounts login codex --revision N --json
lain accounts login-status LOGIN_ID --json
lain accounts login-cancel LOGIN_ID --json
lain accounts check ACCOUNT_ID --json
lain accounts enable ACCOUNT_ID --revision N --json
lain accounts disable ACCOUNT_ID --revision N --json
lain accounts remove ACCOUNT_ID --revision N --json
lain accounts add-api --name NAME --provider PROVIDER --base-url URL --key-env ENVNAME --revision N --json
```

`add-api` also accepts `--key-stdin` for a single input line. Never place API keys
on argv, in generated command text, or reports. Prefer an already populated
secret environment variable; do not print its value. Own Codex login is a
separate operation from importing an external access-token snapshot. Poll only
the requested login operation, and honor cancellation.

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
