# Profile application and Codex artifact recovery

Run `dotnet run --project tests/profile-application/ProfileApplication.Tests.csproj`.
The fixtures use an isolated temporary state directory and a synthetic Core lifecycle;
they do not read credentials, call providers, start the installed service or modify
ordinary Codex configuration. Actual stream-drain qualification belongs to the Core
quiescing tests, not this synthetic delegate fixture.

The suite covers separate save/apply, all pending effective route/account changes,
profile-only changes, disabled defaults, stale revisions, serialized application,
queued cancellation, a refused stream drain, metadata write failure, interrupted
commit, explicit retry, exact runtime generation and stale/interrupted client export.
It checks retained old catalogs, content tampering and symlink rejection.

## Recovery contract

A successful apply requires a matching profile, revision and generation in the
runtime and active metadata, the applied repository revision, and a completed
application journal. Startup must call `RequireCompletedRuntime()` before starting
the Core. An incomplete journal or mismatched metadata never proves active success.

The application first drains requests through the lifecycle delegate. A refused
or canceled drain leaves the old runtime files and their commit proof intact.
After drain/stop, request cancellation no longer cancels publication. A later
failure stops the new Core and records `recovery-required`. The user recovers by
previewing the current saved profile and explicitly applying that revision again.
This writes a fresh runtime from current credentials; it never restores older
rotated credentials. Configuration saves are not rolled back. No route toggle
implicitly invokes apply. All gateway clients still share one applied profile.

Codex installation atomically replaces `lain.config.toml` with a configuration
referencing a private immutable catalog generation under `.lain-artifacts`.
Existing generations are retained so already running clients can continue to read
their catalog. `lain.models.json` is only a compatibility copy. New generated
configurations do not reference it. The private journal and content hashes detect
interruption before or after the configuration commit. `Inspect` reports
`recovery-required` until explicit re-export/install repairs bookkeeping. Compare
its installed profile and revision with the host's applied identity to detect a
stale export; an absent identity is unknown, never implicitly current.

A crash can leave unreferenced private generation files. There is deliberately no
automatic retention/deletion policy. File contents are flushed before atomic
rename; sudden storage-device or filesystem failure still requires filesystem
recovery, and the checks do not claim protection against a malicious local user
who can replace both artifacts and all integrity metadata.
