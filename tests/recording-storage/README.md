Synthetic tests use `$HOME/.cache/lain-recording-storage-tests/<UUID>`, never RAM
or `/tmp`. No providers, cloud calls, real conversations or baseline Usage data.

Run only in the integrator's serial build lane:

```sh
TMPDIR=/home/tomka/.cache/lain-implementation-20261030/test-tmp dotnet test tests/recording-storage/Lain.Recording.Storage.Tests.csproj --no-restore -m:1
```

Restore this new test project first if needed (its package references match the
existing test infrastructure). Tests cover durable restart and uncertain receipt,
exact replay/conflicts, torn tail/checksum failure, missing payload, deletion and
cursor invalidation, full clear, retention, admission limits, Unix protections,
second writer, symlink refusal and compression before writing. They do not claim
measured inference overhead, hostile same-user protection, fault-injected every
fsync boundary or live SQLite-reader invalidation. Host integration must schedule
retention and serialize projection close/rebuild around deletion.
