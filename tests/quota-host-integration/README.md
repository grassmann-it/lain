# Host quota integration fixtures

Run `dotnet run --project tests/quota-host-integration` in the coordinated build slot.
All state, browser operations, HTTP responses and credentials are synthetic; no browser process,
provider service, installed state or real access material is used.

Coverage: local MiMo method availability, explicit quota-only login, reader reuse without browser
startup, late publication conflict and recovery, monitor/account removal, detached session retries,
same-profile generation changes, generic cleanup callbacks, Groq project-bound Enterprise reads
without Core, passive behavior without opt-in, and dated NVIDIA guidance without measured values.

Source prepared on 25 October 2026; runtime validation remains with the coordinated integration run.

Target-removal regressions cover pending, in-flight late-prepared, and already released recovery
sessions. Only the removed explicit account target is invalidated. The private `target-removed`
recovery state is irreversible, including late checkpoints and later reuse of the same account ID.
Ordinary revision conflicts with an existing target remain recoverable; external grants are never
revoked by the default target-removal hook.

Cleanup uses one cooperative five-second budget for the entire pass and at most eight entries.
Retries are ordered by their last attempt so failing entries cannot starve newly healthy work.
The retry dictionary remains in-memory and has no silent eviction cap: dropping a cleanup target
after publication would lose owned-session retirement. Persistent cleanup failures can therefore
grow this dictionary. A hard cap requires durable retirement/admission control before publication;
callbacks must honor cancellation. Pending login target-cleanup occupies the existing active-operation
limit and is not pruned until cleanup succeeds.

The persisted-removal follow-up adds actual MiMo adapter coverage with a synthetic browser after
terminal pruning and service restart. Its selected quota remains absent; the private disk descriptor
must still close exactly the removed target's profile and store completed cleanup. These additions
await the coordinated integration run.
