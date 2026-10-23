Client/CLI contract checks run without external providers, a database, a host process, or test package dependencies:

```sh
dotnet run --project tests/client-cli/ClientCli.Tests.csproj -p:BuildInParallel=false -m:1
```

The executable checks typed API deserialization, bearer auth, revision envelopes, mutation paths, escaped usage filters, explicit timezone boundaries, JSON-only stdout, inference-token-only stdout, authorization/revision/unavailable exit codes, and offline discovery. The fake transport never reads actual credentials.

`lain profiles apply ID [--revision N] [--json]` applies the selected profile through the Host. `lain profiles install-codex ID [--revision N] [--json]` requests generated artifacts from the Host and privately replaces only `~/.codex/lain.config.toml` and `~/.codex/lain.models.json`. It uses the absolute `~/.local/bin/lain` launcher for Codex authentication and preserves ordinary Codex configuration. Each file is flushed and atomically replaced; the catalog is published first. The two-file installation is not a single filesystem transaction.

CLI discovery uses `LAIN_HOME/credentials/host.token`, or `~/.local/share/lain/credentials/host.token` by default, and `http://127.0.0.1:18418`. The inference secret is obtained only through the authenticated `/api/v1/auth/token?audience=codex` endpoint. `CreateLocal()` can be constructed while the host is stopped; credential discovery occurs on each request.
