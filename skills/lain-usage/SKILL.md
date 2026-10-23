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
```

Usage and attempts also accept `--model ID` and `--status HTTP`; bound and paginate
attempt lists. Honor response coverage, observation time and unknown/null values.
Do not invent token totals, infer independent budgets from multiple credentials,
or present shared-with-legacy quotas as isolated budgets.

Exit codes: 0 success, 1 unexpected error, 2 invalid command/options, 3 host
unavailable, 4 authorization failure, 5 rejected operation/conflict. On an
unavailable host report that state; `lain service status --json` may help diagnose
it. Start/stop/restart the service only when requested via
`lain service start|stop|restart --json`. Opening the UI uses `lain ui`.

Configuration mutations require a user request for that action. A request to
inspect usage does not authorize importing accounts or changing routes. Imports
use `lain import /absolute/config.yaml --auth-dir /absolute/auth --revision N
--json`; inspect current revision first and re-read state on conflict rather
than forcing a write. Use installed help for profile/route/client binding
mutations. Keep the ordinary Codex configuration and other proxies unchanged;
lain's Codex integration uses its separate config/profile.

`lain auth token --audience codex` returns an inference credential in plaintext
for the authorized local harness helper. Do not call it for inspection, include
its output in reports or logs, or expose host-management credentials. Imported
external OAuth snapshots must not be refreshed or revoked by this skill.
