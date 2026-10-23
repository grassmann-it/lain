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

The integrator built and ran the original `d94a7cf` implementation (integrated
as `3b63bcb`) on 2026-10-24: zero warnings/errors and 27 passing assertions,
recorded in `/tmp/lain-expansion-tests/Inspector.Tests-integrated.log`.
The independent Host boundary suite passed 80 fake-HTTP assertions in
`/tmp/lain-expansion-tests/InspectorIntegration-integrated.log`.

Follow-up cases for normalized credential markers, slow pending-text trickles,
completion-flush receipt age, mixed old/new tails and canonical source identities
were added after that run. Their execution remains pending in the integrator's
serialized slot; this worker has not run a build or assertions for the follow-up.
