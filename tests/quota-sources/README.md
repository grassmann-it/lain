# Quota source integration fixtures

Run in Main's serialized .NET build slot:

```sh
dotnet run --project tests/quota-sources/QuotaSources.Tests.csproj
```

Only synthetic HTTP handlers and credentials are used. Tests exercise the real
adapters and `QuotaService`, including the private Groq wire contract, account and
model isolation, retained observation timestamps, expired passive windows, neutral
unsupported/setup states, Google Monitoring project validation and pagination,
zero/missing data, last-good values on 401/403/429, and cancellation.

This suite is the Host half of the bridge. Main's Go tests must additionally show
that ordinary inference captures the allowlisted upstream headers with its actual
account/model/request, persists them in a bounded store, and serves them only on
the authenticated management endpoint. Static header parser tests alone do not
prove this runtime path. Installation/live account verification remain separate.
