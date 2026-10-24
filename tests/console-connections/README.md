# Console connection fixtures

All JSON is synthetic. The harness exercises the actual private boundary and
pure MiMo normalization implementation, plus the actual account/login lifecycle
through a synthetic isolated-browser interface. It performs no HTTP/browser operation,
uses no real state and creates no provider key.

Integrator only, within the serial .NET build slot, from checkout root:

```sh
dotnet run --project tests/console-connections/ConsoleConnections.Tests.csproj
```

See `docs/account-connect-qualification/mimo.md` for the implemented browser
connection, registration contract, lifecycle and remaining live qualification.
Test success qualifies local account/session/schema behavior; it does not imply
a successful Xiaomi sign-in or genuine provider quota read.
