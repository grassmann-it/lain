Client/CLI contract checks run without external providers, a database, a host process, or test package dependencies:

```sh
dotnet run --project tests/client-cli/ClientCli.Tests.csproj -p:BuildInParallel=false -m:1
```

The executable checks typed API deserialization, bearer auth, revision envelopes, mutation paths, escaped usage filters, explicit timezone boundaries, JSON-only stdout, inference-token-only stdout, authorization/revision/unavailable exit codes, and offline discovery. The fake transport never reads actual credentials.

`HumanRenderingChecks.cs` captures wide, narrow and very narrow human tables. It checks numeric alignment, full command IDs, Unicode cell widths, ANSI/OSC/control sanitization, TTY/NO_COLOR colour policy, empty lists, provider readiness versus live verification, and stale/shared quota values. Each adapted list also has a byte-for-byte JSON regression check against the existing serializer. No terminal, service or provider is contacted.

Quota, status, usage and help fixtures additionally cover 60/80/120 columns. Quotas show actual consumed percentages as horizontal bars; unknown, balance-only and unsupported windows have no invented zero bar. Stale/overdue values retain their last-known status, absolute balances retain their native units, and snapshots without account names honestly use account IDs. Full source and pool IDs remain in `--json`.

After building, the test executable has a fixture-only preview entry point. It uses fake HTTP or local help and never discovers a real host:

```sh
dotnet tests/client-cli/bin/Debug/net10.0/ClientCli.Tests.dll --render-fixture quotas
dotnet tests/client-cli/bin/Debug/net10.0/ClientCli.Tests.dll --render-fixture providers
dotnet tests/client-cli/bin/Debug/net10.0/ClientCli.Tests.dll --render-fixture status --json
python3 tests/client-cli/render-pty.py \
  --dll tests/client-cli/bin/Debug/net10.0/ClientCli.Tests.dll \
  --output /tmp/lain-cli-private-pty
```

`render-pty.py` creates its own private PTYs at 60/80/120 columns for quotas, providers, status, usage and help, checks normal colour and `NO_COLOR`, and separately checks redirected and JSON output. It writes plain/ANSI captures and `checks.json` under the requested output directory. It does not build, use a real terminal session, call services or contact providers. This entry point belongs only to the test executable; production commands and flags are unchanged.

Expansion checks cover the typed usage analysis, inspector snapshot/events and provider APIs. They verify exact dimension filters, bounded groups and pages, server-owned statistics and sample counts, unknown measurements, retained null identities, source-bound cursors, gaps and evidence, keyless onboarding, route capability uncertainty and read-only free-only evaluation. Invalid grouping, pagination and inspector cursor options fail before host discovery. Text previews neutralize terminal control characters and preserve native quota units and shared-budget scope.

`ExpansionContractChecks.cs` supplies independent JSON response fixtures rather than serializing the consumer DTOs as the fake server. These checks retain actual camelCase wire fields, distinguish prepared adapters from live verification, preserve source resets and unknown performance, map a rejected strict-free profile to exit 5 without retry/fallback, and keep malformed error response bodies out of diagnostics. Optional training permission is forwarded only as explicitly supplied; the Host remains responsible for permission and cost-policy enforcement. These fake transport checks do not qualify real Host policy or provider connections.

`lain profiles apply ID [--revision N] [--json]` applies the selected profile through the Host. `lain profiles install-codex ID [--revision N] [--json]` requests generated artifacts from the Host and privately replaces only `~/.codex/lain.config.toml` and `~/.codex/lain.models.json`. It uses the absolute `~/.local/bin/lain` launcher for Codex authentication and preserves ordinary Codex configuration. Each file is flushed and atomically replaced; the catalog is published first. The two-file installation is not a single filesystem transaction.

Account management uses `accounts login codex`, `accounts login-status ID`, `accounts login-cancel ID`, `accounts enable|disable|remove|check ID`. Mutations accept `--revision N` or read the current revision through the Host. `accounts add-api --name NAME --provider PROVIDER --base-url URL --key-env ENVNAME` reads the key from the named environment variable; alternatively, `--key-stdin` reads one line from standard input. A plain API-key command argument is not accepted. `--json` applies to all account commands; login without JSON prints its authorization URL.

CLI discovery uses `LAIN_HOME/credentials/host.token`, or `~/.local/share/lain/credentials/host.token` by default, and `http://127.0.0.1:18418`. The inference secret is obtained only through the authenticated `/api/v1/auth/token?audience=codex` endpoint. `CreateLocal()` can be constructed while the host is stopped; credential discovery occurs on each request.
