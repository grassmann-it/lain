Client/CLI contract checks run without external providers, a database, a host process, or test package dependencies:

```sh
dotnet build tests/client-cli/ClientCli.Tests.csproj -m:1 -p:BuildInParallel=false -nr:false
dotnet tests/client-cli/bin/Debug/net10.0/ClientCli.Tests.dll
```

The executable checks typed API deserialization, bearer auth, revision envelopes, mutation paths, escaped usage filters, explicit timezone boundaries, JSON-only stdout, inference-token-only stdout, authorization/revision/unavailable exit codes, and offline discovery. The fake transport never reads actual credentials.

`HumanRenderingChecks.cs` captures wide, narrow and very narrow human tables. It checks numeric alignment, full command IDs, Unicode cell widths, ANSI/OSC/control sanitization, TTY/NO_COLOR colour policy, empty lists, provider readiness versus live verification, and stale/shared quota values. Each adapted list also has a byte-for-byte JSON regression check against the existing serializer. No terminal, service or provider is contacted.

Quota, status, usage, history and help fixtures additionally cover 60/80/120 columns. Quotas show actual consumed percentages as horizontal bars; unknown, balance-only and unsupported windows have no invented zero bar. Stale/overdue values retain their last-known status and absolute balances retain their native units. The all-account `usage` overview uses actual account names and providers; raw `quotas` snapshots contain account IDs. Full source and pool IDs remain in `--json`.

`AccountUsageChecks.cs` verifies that bare `usage` and `usage --json` compose only `GET /api/v1/accounts` and `GET /api/v1/quotas`, retaining every configured account, disabled states and empty quota arrays. `history` uses the original proxy-history contract; explicit historical `usage` arguments remain aliases. Checks preserve historical JSON bytes, aggregate endpoints, raw `quotas --json` and the existing failure exit/diagnostic path with empty stdout. See [CLI usage and history](../../docs/cli-usage.md).

Default `usage` is compact; `usage --full` retains the detailed account/quota view. Checks compare both at 60/80/120 columns, preserve native balances and stale/overdue/unknown states, mark successful observations older than five minutes as last known, retain real disabled/missing/expired states and put generic unverified credential details under `--full`. Sources, check times and complete support/error details remain in `--full`; compact warnings retain their meaning without repeated missing-value messages. `--full --json` must be byte-identical to the existing overview JSON; historical uses of `--full` fail before Host discovery.

After building, the test executable has a fixture-only preview entry point. It uses fake HTTP or local help and never discovers a real host:

```sh
dotnet tests/client-cli/bin/Debug/net10.0/ClientCli.Tests.dll --render-fixture quotas
dotnet tests/client-cli/bin/Debug/net10.0/ClientCli.Tests.dll --render-fixture usage
dotnet tests/client-cli/bin/Debug/net10.0/ClientCli.Tests.dll --render-fixture usage --full
dotnet tests/client-cli/bin/Debug/net10.0/ClientCli.Tests.dll --render-fixture history --json
dotnet tests/client-cli/bin/Debug/net10.0/ClientCli.Tests.dll --render-fixture providers
dotnet tests/client-cli/bin/Debug/net10.0/ClientCli.Tests.dll --render-fixture status --json
python3 tests/client-cli/render-pty.py \
  --dll tests/client-cli/bin/Debug/net10.0/ClientCli.Tests.dll \
  --output /tmp/lain-cli-private-pty
```

`UsageStyleChecks.cs` exercises compact usage at 20/40/60/80/120 columns, including long Unicode labels and terminal-control injection, selective metric colour, matching account/provider names, neutral support guidance, grouped missing readings and complete full-view errors. Explicit Europe/Berlin reset fixtures distinguish the repeated autumn hour by UTC offset while full output retains UTC timestamps.

`render-pty.py` creates its own private PTYs at 60/80/120 columns for quotas, providers, status, compact/full usage, history and help, adds compact usage at 20/40 columns, checks normal colour and `NO_COLOR`, and separately checks redirected and JSON output. It compares compact/full line counts and overview JSON bytes. It writes plain/ANSI captures and `checks.json` under the requested output directory. The harness removes only the known initial .NET keypad-mode prefix before checking application terminal controls. It does not build, use a real terminal session, call services or contact providers. This preview entry point belongs only to the test executable.

Expansion checks cover the typed usage analysis, inspector snapshot/events and provider APIs. They verify exact dimension filters, bounded groups and pages, server-owned statistics and sample counts, unknown measurements, retained null identities, source-bound cursors, gaps and evidence, keyless onboarding, route capability uncertainty and read-only free-only evaluation. Invalid grouping, pagination and inspector cursor options fail before host discovery. Text previews neutralize terminal control characters and preserve native quota units and shared-budget scope.

`ExpansionContractChecks.cs` supplies independent JSON response fixtures rather than serializing the consumer DTOs as the fake server. These checks retain actual camelCase wire fields, distinguish prepared adapters from live verification, preserve source resets and unknown performance, map a rejected strict-free profile to exit 5 without retry/fallback, and keep malformed error response bodies out of diagnostics. Optional training permission is forwarded only as explicitly supplied; the Host remains responsible for permission and cost-policy enforcement. These fake transport checks do not qualify real Host policy or provider connections.

`lain profiles apply ID [--revision N] [--json]` applies the selected profile through the Host. `lain profiles install-codex ID [--revision N] [--json]` requests generated artifacts from the Host and privately replaces only `~/.codex/lain.config.toml` and `~/.codex/lain.models.json`. It uses the absolute `~/.local/bin/lain` launcher for Codex authentication and preserves ordinary Codex configuration. Each file is flushed and atomically replaced; the catalog is published first. The two-file installation is not a single filesystem transaction.

Account management uses `accounts login codex`, `accounts login-status ID`, `accounts login-cancel ID`, `accounts enable|disable|remove|check ID`. Mutations accept `--revision N` or read the current revision through the Host. `accounts add-api --name NAME --provider PROVIDER --base-url URL --key-env ENVNAME` reads the key from the named environment variable; alternatively, `--key-stdin` reads one line from standard input. A plain API-key command argument is not accepted. `--json` applies to automation account commands; login without JSON prints its authorization URL.

CLI discovery uses `LAIN_HOME/credentials/host.token`, or `~/.local/share/lain/credentials/host.token` by default, and `http://127.0.0.1:18418`. The inference secret is obtained only through the authenticated `/api/v1/auth/token?audience=codex` endpoint. `CreateLocal()` can be constructed while the host is stopped; credential discovery occurs on each request.

`accounts add` is a private TTY wizard using the same Host catalog connections, billing plans, fields and model templates as Desktop. It saves one account and only the explicitly selected optional model routes. `accounts monitor ID` attaches separate read access to that same account; snapshot expiry and scopes stay explicit. Both refuse redirected terminals before Host discovery, hide tokens and support Esc/Ctrl-C/Ctrl-D cancellation before the final save. Existing `add-api --key-stdin/--key-env` automation is unchanged.

`AccountWizardChecks`, `MonitorWizardChecks` and `SetupPresentationChecks` use synthetic HTTP and terminal adapters for per-prompt cancellation, revision conflicts, custom/collision names, account-only and multiple-model saves, monitor expiry/removal, neutral unsupported states, usage/balance semantics and the five-provider presentation. After building, the actual console input boundary is checked in private PTYs:

```sh
python3 tests/client-cli/wizard-pty.py --dll tests/client-cli/bin/Debug/net10.0/ClientCli.Tests.dll
```
