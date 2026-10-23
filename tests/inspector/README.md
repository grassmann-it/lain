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
provider parser coverage or a working packaged UI.

The integrator rebuilt and reran follow-up `540b2af` (integrated as `ad29d94`) on
2026-10-24: zero warnings/errors, 32 focused assertions and 80 independent Host
fake-HTTP assertions passed. The combined log is
`/tmp/lain-inspector-age-regression.log`.
This includes normalized credential markers, slow pending-text trickles,
completion-flush receipt age, mixed old/new tails and canonical source identities.
Execution was coordinated in the integrator's serialized slot; this worker did
not run an additional build or test pass.
