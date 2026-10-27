# Host/Core startup isolation

Run from the repository root:

```sh
dotnet run --project tests/core-startup/CoreStartup.Tests.csproj
dotnet run --project tests/profile-application/ProfileApplication.Tests.csproj
dotnet run --project tests/Lain.Accounts.Tests/Lain.Accounts.Tests.csproj
```

The new suite uses temporary state and a synthetic Core implemented by its own
apphost on random private loopback ports. It covers persisted manual Stop,
invalid Start retaining suppression, concurrent idempotent ensure, Monitor,
credential/application recovery and login-only refusal, preserved pending
revisions, bounded redacted health observations, early child exit, health timeout
with the observed unmet condition, healthy idempotence and unexpected child exit.
Core output is deliberately secret-shaped and never becomes a public diagnostic.
No installed service, provider, production port, real credential or GUI is used.

The default readiness deadline remains 20 seconds. The fake Core deadline is
shortened through constructor-injected timing. No automatic retry is qualified
for an unclassified startup failure; an explicit Start or Restart is required.

This is source acceptance coverage. It does not diagnose the previous installed
cold-start incident or establish its root cause.
