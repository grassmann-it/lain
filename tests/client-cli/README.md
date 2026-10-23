Client/CLI contract checks run without external providers, a database, a host process, or test package dependencies:

```sh
dotnet run --project tests/client-cli/ClientCli.Tests.csproj -p:BuildInParallel=false -m:1
```

The executable checks typed API deserialization, bearer auth, revision envelopes, mutation paths, escaped usage filters, explicit timezone boundaries, JSON-only stdout, inference-token-only stdout, authorization/revision/unavailable exit codes, and offline discovery. The fake transport never reads actual credentials.

Expansion checks cover the typed usage analysis, inspector snapshot/events and provider APIs. They verify exact dimension filters, bounded groups and pages, server-owned statistics and sample counts, unknown measurements, retained null identities, source-bound cursors, gaps and evidence, keyless onboarding, route capability uncertainty and read-only free-only evaluation. Invalid grouping, pagination and inspector cursor options fail before host discovery. Text previews neutralize terminal control characters and preserve native quota units and shared-budget scope.

`lain profiles apply ID [--revision N] [--json]` applies the selected profile through the Host. `lain profiles install-codex ID [--revision N] [--json]` requests generated artifacts from the Host and privately replaces only `~/.codex/lain.config.toml` and `~/.codex/lain.models.json`. It uses the absolute `~/.local/bin/lain` launcher for Codex authentication and preserves ordinary Codex configuration. Each file is flushed and atomically replaced; the catalog is published first. The two-file installation is not a single filesystem transaction.

Account management uses `accounts login codex`, `accounts login-status ID`, `accounts login-cancel ID`, `accounts enable|disable|remove|check ID`. Mutations accept `--revision N` or read the current revision through the Host. `accounts add-api --name NAME --provider PROVIDER --base-url URL --key-env ENVNAME` reads the key from the named environment variable; alternatively, `--key-stdin` reads one line from standard input. A plain API-key command argument is not accepted. `--json` applies to all account commands; login without JSON prints its authorization URL.

CLI discovery uses `LAIN_HOME/credentials/host.token`, or `~/.local/share/lain/credentials/host.token` by default, and `http://127.0.0.1:18418`. The inference secret is obtained only through the authenticated `/api/v1/auth/token?audience=codex` endpoint. `CreateLocal()` can be constructed while the host is stopped; credential discovery occurs on each request.
