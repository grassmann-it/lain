Client/CLI contract checks run without external providers, a database, a host process, or test package dependencies:

```sh
dotnet run --project tests/client-cli/ClientCli.Tests.csproj -p:BuildInParallel=false -m:1
```

The executable checks typed API deserialization, bearer auth, revision envelopes, mutation paths, escaped usage filters, explicit timezone boundaries, JSON-only stdout, inference-token-only stdout, authorization/revision/unavailable exit codes, and offline discovery. The fake transport never reads actual credentials.

CLI discovery uses `LAIN_HOME/credentials/host.token`, or `~/.local/share/lain/credentials/host.token` by default, and `http://127.0.0.1:18418`. The inference secret is obtained only through the authenticated `/api/v1/auth/token?audience=codex` endpoint. `CreateLocal()` can be constructed while the host is stopped; credential discovery occurs on each request.
