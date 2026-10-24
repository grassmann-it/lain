# Partial usage measurement contract

Run `dotnet test tests/usage-partial/Lain.Usage.Partial.Tests.csproj`. These tests
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
