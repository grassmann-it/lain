# EC1 DeepSeek fixtures

This executable suite uses fake HTTP and a disposable private state directory
under the existing user cache. It never calls a real provider, reads an actual
credential, starts an inference runtime, changes HOME, or modifies a real account.

It covers exact balances, multiple currencies, malformed/partial/duplicate
responses, no invented money quota fields, 401 sanitization, invalid endpoints
and resource assertions before HTTP, caller cancellation, catalog/current model
IDs, account-only key connection, optional existing-transport model attachment,
stable local account/name/route preservation and quota credential generations.
NuGet dependencies inherit the host's pinned lockfile; there are no new packages.

Run only while holding the project's serial build/runtime slot:

```sh
DOTNET_PROCESSOR_COUNT=2 dotnet restore tests/deepseek/DeepSeek.Tests.csproj --locked-mode --disable-parallel
DOTNET_PROCESSOR_COUNT=2 dotnet build tests/deepseek/DeepSeek.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false
DOTNET_PROCESSOR_COUNT=2 dotnet run --project tests/deepseek/DeepSeek.Tests.csproj --no-build --no-restore
```

Account connection and attachment tests exercise real lain services against
synthetic local credentials. They establish local binding/generation behavior;
the upstream balance contract cannot verify a remote user's identity because
it returns no user identifier. Actual installation and real-key validation are
separate integration steps.
