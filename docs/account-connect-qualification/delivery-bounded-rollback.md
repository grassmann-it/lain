# Bounded previous-binary rollback: source verdict

24 October 2026. Reviewed old Host source `413588c`, current frozen delivery
source `4a58d5b`. No runtime, existing credentials, user data, service or network
namespace was accessed. This is a source finding, not a completed rollback test.

## Concrete blocker for app-only rollback

The old `src/Lain.Host/Usage/SqliteUsageStore.cs` constructor explicitly rejects
`PRAGMA user_version > 3`. The current Host runs migration
`004_attempt_error.sql`, adds nullable `attempts.error_category` with an enumerated
CHECK constraint and sets `user_version = 4`. It migrates on startup, even before
any user configuration mutation. Therefore the exact old shipped Host cannot
open the new database after an app-only swap. Additive SQLite columns do not
override the application's explicit schema version guard.

Keeping the entire CURRENT credential/auth stores is necessary to avoid spent
refresh-token resurrection, but does not fix this database incompatibility.
A successful full current-package stopped restore is also not evidence that an
old binary opens a migrated database. No successful old-binary runtime stage was
added to `actual_packages.py`; its report now names this exact schema blocker.

## Narrow additional conversion candidate (not authorized/executed here)

A bounded rollback could potentially use an explicit, independently qualified
stopped schema conversion in addition to the app swap. This is a different action
from an app-only rollback and needs the integrator's concrete decision and proof:

1. Stop all writers and retain a verified full CURRENT app/state snapshot.
   Keep CURRENT credentials, auth-owned files and every credential generation;
   never restore old refresh material to make an old application work.
2. Admit only the reviewed legacy configuration and desired11/applied2 pair,
   with no new-style accounts, handles, connect operations, restore epoch,
   Apply generation, configuration changes or new traffic. Old Host does not
   implement the new restore-epoch contract.
3. Compare the exact database schema against reviewed v3 + migration004, and
   verify all `error_category` values are NULL. Confirm all legacy tables,
   source identities/cursors, journal-event digests and attempt/token data remain
   unchanged except for the reviewed migration. Unexpected extension data is a
   stop condition, not a value to drop silently.
4. On a stopped staged copy, remove exactly the empty `error_category` column
   and set `user_version = 3`, retaining current rows/cursors and all other
   schema. Do not simply relabel a database as v3 with the column present: the
   next current Host would try to add it again and fail.
5. Verify integrity and exact semantic data preservation before any controlled
   swap. Preserve the original current database and failure state; do not
   overwrite the only recovery copy.
6. Run the exact pinned old Host/Core in a dedicated loopback-only network
   namespace at its fixed 18418/18417 ports, with synthetic credentials only.
   Check accounts, retained current owned credential bytes, desired11/applied2,
   history/token components, source/cursor and no journal replay change. Stop it
   fully, restore the CURRENT application and prove forward migration works.

This candidate has NOT passed. New non-NULL error categories or new-schema
configuration/credential semantics require reconciliation rather than this
conversion. A schema header change or filesystem restore alone proves neither
compatibility nor preservation.

## Journal and credential boundaries

The new sparse journal index changes in-memory checkpoints; the flat file,
source ID and sequential event IDs remain the same. Both versions validate
source/sequence while opening. However the old Event DTO omits `errorCategory`;
old unmarshalling/re-encoding would strip unsupported event fields on replay.
The bounded candidate must therefore require unchanged legacy journal bytes,
source/cursor and digests, with no extension-bearing events. It must not assume
that arbitrary new journals are backward compatible merely because they parse.

The old SecretStore supports legacy descriptor + `ownedAuthFile` identity
projection and reads the current auth-owned file. Retaining that whole current
store can preserve a rotated current grant; restoring an older snapshot cannot.
None of this qualifies old code for new credential-generation owners, new
connect state or restored rotating grants. Those are outside the candidate.

## Network isolation and next gate

The upstream Core can perform incidental public metadata checks at startup even
without configured provider accounts. `actual_packages.py --execute` now refuses
execution unless its network namespace differs from process1's namespace and
contains only `lo`. The runner must bring up loopback inside an explicitly new
network namespace and must not create a new PID namespace (which would obscure
this guard's parent namespace reference). No default host port can be used in
the user's network namespace. A namespace failure aborts; it never falls back.

Root owns the build/runtime slot and any decision to qualify the extra conversion.
Until actual exact-binary evidence exists, old app-only rollback is **blocked by
schema4**. Full current-package snapshot/restore remains a separate supported
qualification target, with rotation quarantine and usable static/history access.
