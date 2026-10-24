# Console connection fixtures

All JSON is synthetic. The harness exercises the actual private boundary and
pure MiMo normalization implementation. It performs no HTTP/browser operation,
uses no real state and creates no provider key.

Integrator only, within the serial .NET build slot, from checkout root:

```sh
dotnet run --project tests/console-connections/ConsoleConnections.Tests.csproj
```

See `docs/account-connect-qualification/console-connections.md` for source
evidence, browser options and each production NoGo. Test success qualifies only
local message/schema behavior. No transport is registered in Host and the
normalizer intentionally does not implement `IQuotaAdapter`.
