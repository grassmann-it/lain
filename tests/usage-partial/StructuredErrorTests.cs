using System.Text.Json;
using Lain.Core;
using Lain.Host.Usage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lain.Usage.Partial.Tests;

public sealed partial class PartialMeasurementTests
{
    [Theory]
    [InlineData(400, "invalid_request", "check_request")]
    [InlineData(401, "authentication", "reconnect_account")]
    [InlineData(403, "permission", "check_permissions")]
    [InlineData(429, "rate_limited", "retry_later")]
    [InlineData(503, "upstream_error", "retry_later")]
    [InlineData(404, "unknown", "inspect_attempt")]
    public async Task HistoricalHttpEvidenceSuppliesOnlyGenericSafeError(int status, string category, string action)
    {
        var a = Started(1); var store = new SqliteUsageStore(Database);
        var terminal = Finished(2, a) with { StatusCode = status, Outcome = "failed", UsageReported = false };
        Assert.DoesNotContain("errorCategory", JsonSerializer.Serialize(terminal, JournalJson.Options));
        await store.IngestAsync(Batch(a, terminal));
        var attempt = Assert.Single((await store.AttemptsAsync(Query)).Items);
        Assert.Equal(new AttemptErrorDto(category, "http_status", action), attempt.ErrorDetails);
        Assert.Equal(category, attempt.Error);
        Assert.Null(attempt.Tokens.Total);
        Component((await store.SummaryAsync(Query)).Totals.Measurements!.Total, null, 0, 1);
    }

    [Fact]
    public async Task CancellationAndWrapperFailureUseOnlyObservedOutcome()
    {
        var a = Started(1); var b = Started(3, "a2"); var store = new SqliteUsageStore(Database);
        var finished = b with
        {
            Sequence = 5, EventId = "source-a:5", Kind = "attempt_finished", ObservedAt = Start.AddSeconds(2),
            CompletionState = "canceled", ErrorCategory = "canceled"
        };
        await store.IngestAsync(Batch(a, Finished(2, a) with { Outcome = "failed", StatusCode = null }, b, Finished(4, b), finished));
        var attempts = (await store.AttemptsAsync(Query)).Items;
        Assert.Equal(new AttemptErrorDto("unknown", "reported_outcome", "inspect_attempt"), attempts.Single(x => x.Id == "a1").ErrorDetails);
        Assert.Equal(new AttemptErrorDto("canceled", "completion_state", "retry_if_needed"), attempts.Single(x => x.Id == "a2").ErrorDetails);
        await new SqliteUsageStore(Database).IngestAsync(Batch(a, Finished(2, a) with { Outcome = "failed", StatusCode = null }, b, Finished(4, b), finished));
        Assert.Equal(5, await store.GetCursorAsync("source-a"));
    }

    [Theory]
    [InlineData("unsupported_tool", "change_tool_configuration")]
    [InlineData("tool_limit", "reduce_tool_count")]
    public async Task QualifiedDispatchCategorySurvivesReplayWithoutRawError(string category, string action)
    {
        var a = Started(1);
        var batch = Batch(a, Finished(2, a) with { StatusCode = 400, Outcome = "failed", ErrorCategory = category });
        for (var replay = 0; replay < 3; replay++) await new SqliteUsageStore(Database).IngestAsync(batch);
        var store = new SqliteUsageStore(Database);
        Assert.Equal(2, await store.GetCursorAsync("source-a"));
        var attempt = Assert.Single((await store.AttemptsAsync(Query)).Items);
        Assert.Equal(new AttemptErrorDto(category, "dispatch_validation", action), attempt.ErrorDetails);
    }

    [Theory]
    [InlineData("quota_exhausted", "failed", 429)]
    [InlineData("provider body: prompt or credential content", "failed", 400)]
    [InlineData("invalid_request", "failed", 429)]
    [InlineData("unsupported_tool", "success", 200)]
    public async Task UnsupportedOrContradictoryErrorCategoryRollsBackWholeBatch(string category, string outcome, int status)
    {
        var a = Started(1); var store = new SqliteUsageStore(Database);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.IngestAsync(Batch(a,
            Finished(2, a) with { Outcome = outcome, StatusCode = status, ErrorCategory = category })));
        Assert.Equal(0, await store.GetCursorAsync("source-a"));
        Assert.Empty((await store.AttemptsAsync(Query)).Items);
    }

    [Fact]
    public async Task SchemaThreeUpgradePreservesCanonicalReplayAndKnownMeasurements()
    {
        var a = Started(1); var batch = Batch(a, Finished(2, a) with { StatusCode = 429, Outcome = "failed" });
        var store = new SqliteUsageStore(Database);
        await store.IngestAsync(batch);
        using (var connection = new SqliteConnection($"Data Source={Database};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE attempts DROP COLUMN error_category; PRAGMA user_version=3;";
            command.ExecuteNonQuery();
        }
        store = new SqliteUsageStore(Database);
        await store.IngestAsync(batch);
        Assert.Equal(2, await store.GetCursorAsync("source-a"));
        var attempt = Assert.Single((await store.AttemptsAsync(Query)).Items);
        Assert.Equal("rate_limited", attempt.ErrorDetails!.Category);
        Component((await store.SummaryAsync(Query)).Totals.Measurements!.Total, 14, 1, 1);
    }
}
