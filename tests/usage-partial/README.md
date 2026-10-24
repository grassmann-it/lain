# Partial usage measurement contract

Run `dotnet test tests/usage-partial/Lain.Usage.Partial.Tests.csproj -r linux-x64 -p:RestoreLockedMode=true`. These tests
use synthetic journals and temporary SQLite databases, with no live providers.

`UsageTotals.Measurements` reports known sums and measured/eligible attempts for
each token component, independently. `KnownReported` is null with no measured
samples, and zero when the provider reported zero. `Tokens` remains the full
sum and stays null whenever an eligible attempt lacks the component. Cache and
reasoning are never added to the provider total. Cache creation is a separate
component. `Complete` still describes journal coverage, not token measurement.

Summary totals, daily/account/model groups and analysis dimensions share SQL
aggregation and the same query filters. Attempt pagination does not affect them.
The suite covers missing and zero values, optional components, unreported values,
late usage, replay/reopen/source identities, DST, filtering and pagination.

Structured attempt errors use a closed category allowlist plus fixed evidence
and action codes. HTTP 400 alone means `invalid_request`; HTTP 429 means
`rate_limited`, never exhausted subscription quota. `unsupported_tool` and
`tool_limit` require an explicit qualified dispatch category. Failed or canceled
outcomes can establish only generic failure/cancellation without an HTTP status.
No raw errors, request/response bodies or provider strings are stored. The
optional journal field is omitted when absent, preserving old replay digests.
Schema 4 adds a nullable category without rewriting historical events.

Validation on 2026-10-24: 20 focused cases and 22 existing usage cases passed
with the additive shared DTOs and schema 4. Existing usage tests also verify
old canonical digests, version 1/2 migrations, source transitions and crash replay.
