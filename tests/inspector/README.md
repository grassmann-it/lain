# Focused transient inspector checks

The console project links the production Inspector files and shared DTOs.
It uses synthetic events and a controllable monotonic clock, with no native GUI,
provider, credentials, database or live service.

After the integrator allocates the build slot:

```sh
dotnet run --project tests/inspector/Inspector.Tests.csproj --no-restore
```

Restore/build first if this is a new checkout. Checks cover explicit identities
and provenance, requested/effective reasoning, conflicting assertions, duplicate
replay, token zero/null, source loss/restart, global filtered cursors, byte and
event/stream bounds, monotonic expiry and pending-buffer expiry, split credential
redaction, oversized continuation and complete exclusion of tool arguments.

Collector/wire and launched-core checks are independently owned by
`tests/inspection`. These focused tests alone do not qualify actual transports,
provider parser coverage or a working packaged UI. The preparing worker has not
run the build or assertions; execution is coordinated by the integrator.
