# Account connect foundation offline qualification

Run `dotnet run --project tests/account-connect-foundation` in the coordinated build slot.
Fixtures use unique private temporary state, synthetic secrets, fake owners/adapters and an injected clock.
No provider requests, installed services, real credentials or GUI are used.

Covers monitor-only creation, later inference, immutable references/save failure, operation replay,
revision/expiry/cancel/target fences, recoverable results, rotation ownership, restore quarantine,
and quota generation invalidation. Provider production prerequisites remain separate gates.
