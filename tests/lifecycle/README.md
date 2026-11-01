# Shared startup lifecycle checks

Run from the repository root:

```sh
dotnet run --project tests/lifecycle/Lifecycle.Tests.csproj
```

The fixture uses private temporary directories, an in-memory authenticated Host,
and fake OS-manager process execution. It does not contact providers or control
installed user services. It covers idempotent/concurrent opens, absent first-start
credentials, Monitor, applied versus pending revisions, explicit Stop suppression,
application recovery, foreign credentials/contracts, independent preferences,
manager-aware CLI status, native binary discovery, and modified Linux units.
