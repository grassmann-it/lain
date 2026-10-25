# Account connect foundation offline qualification

Run `dotnet run --project tests/account-connect-foundation` in the coordinated build slot.
Fixtures use unique private temporary state, synthetic secrets, fake owners/adapters and an injected clock.
No provider requests, installed services, real credentials or GUI are used.

Covers monitor-only creation, later inference, immutable references/save failure, operation replay,
revision/expiry/cancel/target fences, recoverable results, rotation ownership, restore quarantine,
and quota generation invalidation. Provider production prerequisites remain separate gates.

Validated 24 October 2026: Host and this executable build for `linux-x64` with zero warnings/errors;
107 assertions pass. The existing Accounts/operations executable also passed through a temporary
linked-source project adding `Accounts/Credentials/*.cs`; its repository project was not modified here.

Followup cases cover legacy source updates after local rotation, stable names/provider subjects,
no modern identity merge by key/email, deduplicated private key material, scope-incomplete grants,
provider-specific cloud owners, restore quarantine, checkpoint storage failures and exact-account
optional model attachment. A storage failure retains received material in the bounded operation until
local persistence can retry; durable success is never claimed while the filesystem remains unavailable.

Quota boundary regressions cover canonical reader/reconnect aliases without persisted identity changes,
same-key official transport selection with scope and purpose fences, selected monitor-kind metadata,
fixed HTTP status/category diagnostics without provider text, and optional passive fallback when direct
access is absent, expired or unreadable. These additions await the coordinated build slot.

The persisted target-removal follow-up is source-prepared, not yet executed. Its fixtures cover
pruned/restarted recovery, exact same-ID recreation, failed removal publication, both sides of the
removal-journal crash boundary, late generations, unrelated-account preservation and bounded fair
cleanup retries across restart. Private removal epochs are prepared before config publication and
confirmed afterward; an absent account completes a pending journal after a crash. Failed config
publication leaves the old epoch and existing credential selection intact.

`LoginOperationService.CleanupRemovedRecoveriesAsync` requires a startup and periodic Host hook.
Each pass examines at most 64 recovery entries and attempts at most eight disk-only retirements,
using a cooperative five-second deadline and a retained enumeration cursor. It never globally scans
credential generations, starts login, or revokes external grants. Completed retirement is stored in
the existing private recovery record; a late new generation makes retirement pending again. A
missing handle is passed only to its owner hook, which must identify its own persisted operation
resources or report uncertainty. Google verifies its bounded receipt inventory against both account
and operation IDs; malformed/ambiguous/busy profiles retain retry work. MiMo's separate startup
reconciliation handles browser profiles whose former host has exited.
