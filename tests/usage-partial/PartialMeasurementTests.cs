using Lain.Core;
using Lain.Host.Usage;
using Xunit;

namespace Lain.Usage.Partial.Tests;

public sealed partial class PartialMeasurementTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "lain-usage-partial-" + Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(directory, "usage.db");
    // Berlin changes to daylight saving time during this interval.
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-03-28T23:30:00Z");
    private static UsageQuery Query => new(Start.AddDays(-1), Start.AddDays(2), "Europe/Berlin");
    private static JournalEvent Started(long seq, string attempt = "a1", string account = "account-a", DateTimeOffset? at = null) =>
        new(1, "source-a", seq, $"source-a:{seq}", "attempt_started", "r-" + attempt, attempt, account,
            "credential-" + account, "route-a", at ?? Start, "fixture", "alias", "model");
    private static JournalEvent Finished(long seq, JournalEvent start) => start with
    {
        Sequence = seq, EventId = $"{start.SourceId}:{seq}", Kind = "usage", ObservedAt = start.ObservedAt.AddSeconds(1),
        Outcome = "success", UsageReported = true, InputTokens = 10, OutputTokens = 4, TotalTokens = 14, StatusCode = 200
    };
    private static JournalBatch Batch(params JournalEvent[] events) =>
        new(events[0].SourceId, events, events[^1].Sequence, events[^1].Sequence);
    private static void Component(TokenComponentMeasurementDto value, long? known, long samples, long eligible)
    {
        Assert.Equal(known, value.KnownReported);
        Assert.Equal(samples, value.Samples);
        Assert.Equal(eligible, value.Eligible);
    }
    private static void SameMeasurements(UsageTotals expected, UsageTotals actual)
    {
        Assert.Equal(expected.Tokens, actual.Tokens);
        Assert.Equal(expected.Measurements, actual.Measurements);
        Assert.Equal(expected.UnknownUsageAttempts, actual.UnknownUsageAttempts);
    }

    [Fact]
    public async Task CapturedUnmeasuredAttemptKeepsMeasuredTotalsAndIndependentComponentCoverage()
    {
        var a = Started(1); var b = Started(3, "a2"); var c = Started(5, "a3");
        var store = new SqliteUsageStore(Database);
        await store.IngestAsync(Batch(a, Finished(2, a) with { CacheReadTokens = 3, CacheCreationTokens = 2 },
            b, Finished(4, b) with { InputTokens = 0, OutputTokens = null, TotalTokens = null, CacheReadTokens = 0, ReasoningTokens = 0 },
            c, Finished(6, c) with { UsageReported = false }));
        var summary = await store.SummaryAsync(Query);
        Assert.True(summary.Complete); // Journal coverage is complete, token measurement is not.
        Assert.Equal(3, summary.Totals.Attempts);
        Assert.Equal(2, summary.Totals.UnknownUsageAttempts);
        Assert.Equal(new TokenCounts(null, null, null, null, null), summary.Totals.Tokens);
        var measured = Assert.IsType<TokenMeasurementsDto>(summary.Totals.Measurements);
        Component(measured.Input, 10, 2, 3);
        Component(measured.Output, 4, 1, 3);
        Component(measured.Total, 14, 1, 3);
        Component(measured.Cached, 3, 2, 3);
        Component(measured.CacheCreation, 2, 1, 3);
        Component(measured.Reasoning, 0, 1, 3);
        SameMeasurements(summary.Totals, Assert.Single(summary.Daily).Totals);
        SameMeasurements(summary.Totals, Assert.Single(summary.Accounts).Totals);
        SameMeasurements(summary.Totals, Assert.Single(summary.Models).Totals);
        var analysis = await store.AnalysisAsync(Query with { GroupBy = "accountId,model,provider" });
        SameMeasurements(summary.Totals, analysis.Totals);
        foreach (var dimension in analysis.Dimensions) SameMeasurements(summary.Totals, Assert.Single(dimension.Groups).Totals);
        var rows = await store.AttemptsAsync(Query);
        Assert.Equal(2, rows.Items.Single(x => x.Id == "a1").Tokens.CacheCreation);
        Assert.Null(rows.Items.Single(x => x.Id == "a3").Tokens.Total);
    }

    [Fact]
    public async Task EmptyAndEntirelyUnknownSelectionsDoNotClaimMeasuredZero()
    {
        var store = new SqliteUsageStore(Database);
        var empty = await store.SummaryAsync(Query);
        var analysis = await store.AnalysisAsync(Query);
        Component(empty.Totals.Measurements!.Input, null, 0, 0);
        SameMeasurements(empty.Totals, analysis.Totals);
        var start = Started(1);
        await store.IngestAsync(Batch(start, Finished(2, start) with { UsageReported = false }));
        var unknown = await store.SummaryAsync(Query);
        Component(unknown.Totals.Measurements!.Input, null, 0, 1);
        Component(unknown.Totals.Measurements.Total, null, 0, 1);
        Assert.Null(unknown.Totals.Tokens.Total);
    }

    [Fact]
    public async Task ReportedZerosAreKnownAndOptionalCacheAndReasoningStayFieldwise()
    {
        var start = Started(1); var store = new SqliteUsageStore(Database);
        await store.IngestAsync(Batch(start, Finished(2, start) with { InputTokens = 0, OutputTokens = 0, TotalTokens = 0, CacheCreationTokens = 0 }));
        var totals = (await store.SummaryAsync(Query)).Totals;
        Assert.Equal(0, totals.Tokens.Total);
        Assert.Equal(0, totals.Tokens.CacheCreation);
        Assert.Null(totals.Tokens.Cached);
        Assert.Null(totals.Tokens.Reasoning);
        Component(totals.Measurements!.Total, 0, 1, 1);
        Component(totals.Measurements.CacheCreation, 0, 1, 1);
        Component(totals.Measurements.Cached, null, 0, 1);
        Component(totals.Measurements.Reasoning, null, 0, 1);
    }

    [Fact]
    public async Task ReplayReopenAndSourceIdentityPreservePartialMeasurementCounts()
    {
        var a = Started(1); var b = Started(3, "a2");
        var batch = Batch(a, Finished(2, a), b, Finished(4, b) with { OutputTokens = null, TotalTokens = null });
        for (var replay = 0; replay < 10; replay++) await new SqliteUsageStore(Database).IngestAsync(batch);
        var store = new SqliteUsageStore(Database);
        var before = await store.SummaryAsync(Query);
        Assert.Equal(4, before.Cursor);
        Component(before.Totals.Measurements!.Input, 20, 2, 2);
        Component(before.Totals.Measurements.Total, 14, 1, 2);
        var other = Started(1) with { SourceId = "source-b", EventId = "source-b:1" };
        await store.IngestAsync(Batch(other, Finished(2, other)));
        var after = await new SqliteUsageStore(Database).SummaryAsync(Query);
        Assert.Equal(3, after.Totals.Requests);
        Assert.Equal(3, after.Totals.Attempts);
        Component(after.Totals.Measurements!.Total, 28, 2, 3);
    }

    [Fact]
    public async Task AllFiltersAndTimezoneBucketsApplyBeforeMeasurementAndIgnorePageSize()
    {
        var a = Started(1); var b = Started(3, "a2", at: Start.AddDays(1)); var other = Started(5, "a3", "account-b");
        var store = new SqliteUsageStore(Database);
        await store.IngestAsync(Batch(a, Finished(2, a) with { Outcome = "failed", StatusCode = 429 },
            b, Finished(4, b) with { InputTokens = null, TotalTokens = null, Outcome = "failed", StatusCode = 429 },
            other, Finished(6, other)));
        var query = Query with { AccountId = "account-a", Model = "model", RouteId = "route-a", StatusCode = 429, Limit = 1, Offset = 1, GroupBy = "accountId,model" };
        var summary = await store.SummaryAsync(query);
        Assert.Equal(2, summary.Totals.Attempts);
        Assert.Null(summary.Totals.Tokens.Input);
        Component(summary.Totals.Measurements!.Input, 10, 1, 2);
        Component(summary.Totals.Measurements.Output, 8, 2, 2);
        Assert.Equal(new[] { "2026-03-29", "2026-03-30" }, summary.Daily.Select(x => x.Key));
        SameMeasurements(summary.Totals, (await store.AnalysisAsync(query)).Totals);
        SameMeasurements(summary.Totals, Assert.Single(summary.Accounts).Totals);
        var page = await store.AttemptsAsync(query);
        Assert.Single(page.Items);
        Assert.Equal(2, page.Total);
        var firstDay = await store.SummaryAsync(query with { From = Start, To = Start.AddDays(1) });
        Component(firstDay.Totals.Measurements!.Total, 14, 1, 1);
    }

    [Fact]
    public async Task PendingDispatchCountsAsEligibleUntilLateUsageCompletesItsMeasurement()
    {
        var a = Started(1); var b = Started(3, "a2");
        var store = new SqliteUsageStore(Database);
        await store.IngestAsync(Batch(a, Finished(2, a), b));
        var pending = await store.SummaryAsync(Query);
        Assert.False(pending.Complete);
        Component(pending.Totals.Measurements!.Total, 14, 1, 2);
        await store.IngestAsync(Batch(Finished(4, b)));
        var completed = await store.SummaryAsync(Query);
        Assert.True(completed.Complete);
        Assert.Equal(28, completed.Totals.Tokens.Total);
        Component(completed.Totals.Measurements!.Total, 28, 2, 2);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
