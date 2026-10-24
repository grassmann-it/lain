# Bounded legacy rollback source review

Source-only review of previous package source `413588c` (`providers-r2`) against
the current Host migration. No old binary was launched, no database changed and
no credentials read. This assesses only the proposed unchanged legacy
desired/applied revision `11/2` verification window, with no modern account,
login, profile Apply or new inference operations.

**A prior-app-only swap is blocked.** At `413588c`, `SqliteUsageStore` rejects
`PRAGMA user_version > 3`. Current migration `004_attempt_error.sql` adds nullable
`attempts.error_category` and sets version 4. Additive SQL compatibility does not
bypass the old constructor's explicit version gate.

The delivery worker independently confirmed this blocker. A narrowly guarded
candidate, requiring an explicit implementation/qualification decision, is:

1. Stop every writer and retain the current state intact. Verify the configuration
   is byte-identical to the legacy checkpoint, still desired/applied `11/2`, with
   no modern credential references or new application transaction state.
2. Make a consistent staged copy of the **current** SQLite database. Accept only
   the exact reviewed v4 schema, with every `error_category` value NULL, and
   unchanged historical row/source/cursor/event-digest data. Refuse unexpected
   schema objects or data changes.
3. In the staged copy, transactionally remove that empty additive column and set
   `user_version=3`; validate schema, integrity, foreign keys and retained rows.
   Lowering the version while leaving the column is not a safe shortcut: the
   next upgrade would try to add the existing column again.
4. Keep the **current** complete credential/auth stores, configuration, applied
   runtime and flat journal. Never copy credential generations or owned grants
   back from the checkpoint. A consumed token must not be reactivated.
5. Qualify the exact prior app/Core against this candidate in an isolated state,
   then qualify re-upgrade. Preserve the untouched current database for failure
   recovery and record hashes, source/cursor/history equality and artifact IDs.

The journal boundary matters independently of SQL: the previous Host's event
DTO has no `ErrorCategory`. Non-null extension events can serialize differently
under the old reader and conflict with canonical event digests. Require unchanged
legacy journal bytes/source/cursors for this bounded window; do not claim support
for arbitrary new-version activity or replay by the old app.

The old Host also hardcodes ports 18417/18418 and predates the new guarded port
overrides. Its process qualification therefore needs a private loopback-only
network namespace; setting the new reference-port variables alone is insufficient.
Provider quota activity must be disabled using the old supported reference flag,
with no real credential fixtures or external network route.

## Helper implementation follow-up

Root authorized the delivery worker to implement
`scripts/prepare-legacy-rollback.py`. The independent review inspected that helper
and the amended package fixture in the delivery-qualification worktree without
running them. The implementation takes a private full snapshot of CURRENT state,
revalidates the copied v4 database and frozen legacy files, and converts only the
staged database. It verifies pinned schema resources, unchanged original rows and
digests, integrity/foreign keys, and absence of error-category data before dropping
the column. Copies of checkpoint credentials never replace current grants.

The review caught and the owner corrected a validation/copy race: validating the
live source before backup alone could accept a change captured just before copying
began. Validation now runs again on the completed staged copy before conversion.
The checkpoint likewise validates its copied v3 database and exact old application
before publication. Other reviewed corrections reject pending recovery storage and
torn journal tails, use immutable read-only SQLite connections with sidecar refusal,
recheck package hashes after copying, and compare the complete retained state.

Copied YAML needs a deliberate path translation when its `auth-dir` names the
original home. Both reviewed old/new launchers use their own `paths.Proxy` as the
working directory. Relative `auth` already stays local; an absolute original-owned
path requires explicit `--rebase-auth-dir`. Only that scalar changes to the final
destination's owned auth directory. The original/current snapshot YAML remains
unchanged, and every other retained state file must compare exactly. External,
ambiguous and unsupported auth-path syntax must be rejected.

The final parser condition is now closed in source. After taking over the delivery
worktree, Root added rejection of top-level quoted/explicit keys, aliases, tags
and merge syntax before counting the single bare `auth-dir` key. The reviewer
inspected this guard and five ambiguous-mapping subtests; it did not execute them.
No remaining source blocker was found within this exact legacy rollback boundary.

Writer exclusion combines mandatory `--stopped`, process inspection and repeated
source/copy inventories. It does not prove that a future or noncooperative writer
cannot start. All original and staged state writers must remain mutually stopped.
After an old Core runs and may rotate credentials, the archived current snapshot's
grants can themselves be stale: re-upgrade copies only application binaries and
keeps the active staged state's latest auth files. The amended fixture follows
this direction and checks old→new→old→new schema 3→4→3→4 with real artifacts only
inside a private loopback network namespace.

The final Main fixture follow-up also captures expected credential/auth inventories
from the original CURRENT home before preparation. Its synthetic current grant
differs from the checkpoint grant, and the staged copy must match CURRENT before
either old or new runtime starts. Comparing only the prepared copy to itself
would not independently detect accidental checkpoint-grant restoration.

Root selected and the reviewer source-checked the concrete original-home return
path. The fixture retains pristine helper-produced old app/v3 database before
the staged forward run, archives the original current app/v4 database, and
promotes only the prepared old app plus converted database into the original
paths while writers are stopped. Original YAML, configuration, credentials,
both auth directories and journal remain in place. Their bytes/inventories and
canonical `journal_events` digests are checked after the old runtime and after
the app-only forward upgrade. The latter must migrate back to schema 4 without
copying archived credentials or the staged rebased YAML into the original home.
Existing service/launcher paths therefore remain valid. This is a reviewed
fixture/procedure, not evidence that an actual user installation was changed.

Source review does not establish the pending exact-artifact process result.
Without the guarded conversion and passing old/new process evidence, historical
rollback remains unqualified. A current-package backup/restore test and retained
`app.previous` do not establish it.
