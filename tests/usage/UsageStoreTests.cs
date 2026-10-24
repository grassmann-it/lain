using System.Net;
using System.Diagnostics;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Usage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lain.Usage.Tests;

public sealed partial class UsageStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "lain-usage-test-" + Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(directory, "usage.db");
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-03-28T23:30:00Z");
    private static UsageQuery Query(string zone = "UTC") => new(Start.AddDays(-1), Start.AddDays(3), zone);
    private static JournalEvent Started(long seq, string attempt = "a1", string request = "r1", string account = "account-a", DateTimeOffset? at = null) =>
        new(1, "source-a", seq, $"source-a:{seq}", "attempt_started", request, attempt, account, "credential-" + account, "route-a", at ?? Start, "fake", "alias", "real-model");
    private static JournalEvent Finished(long seq, JournalEvent started, bool known = true, string outcome = "success", int status = 200) => started with
    {
        Sequence = seq, EventId = $"source-a:{seq}", Kind = "usage", ObservedAt = started.ObservedAt.AddSeconds(2),
        Outcome = outcome, UsageReported = known, InputTokens = known ? 10 : null, OutputTokens = known ? 4 : null,
        TotalTokens = known ? 14 : null, CacheReadTokens = known ? 5 : null, ReasoningTokens = known ? 2 : null,
        StatusCode = status, LatencyMs = 2000, TtftMs = 250
    };
    private static JournalBatch Batch(params JournalEvent[] events) => new("source-a", events, events.LastOrDefault()?.Sequence ?? 0, events.LastOrDefault()?.Sequence ?? 0);

    [Fact]
    public async Task KilledProcessPreservesDurableDispatchAndDiscardsUncommittedCursorAndUsage()
    {
        Directory.CreateDirectory(directory);
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(typeof(CrashProcess).Assembly.Location);
        start.ArgumentList.Add("--crash-writer");
        start.ArgumentList.Add(Database);
        using var child = Process.Start(start)!;
        try
        {
            Assert.Equal("uncommitted", await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20)));
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
        }
        finally
        {
            if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); }
        }
        var store = new SqliteUsageStore(Database);
        Assert.Equal(1, await store.GetCursorAsync("source-a"));
        var summary = await store.SummaryAsync(Query());
        Assert.Equal(1, summary.Totals.Attempts);
        Assert.Null(summary.Totals.Tokens.Total);
        Assert.False(summary.Complete);
        await store.IngestAsync(Batch(Finished(2, Started(1))));
        Assert.Equal(14L, (await store.SummaryAsync(Query())).Totals.Tokens.Total);
    }

    [Fact]
    public async Task ReplayTenTimesAndReopenKeepsOneRequestTwoAttempts()
    {
        var a = Started(1);
        var b = Started(3, "a2", account: "account-b");
        var batch = Batch(a, Finished(2, a, outcome: "failed", status: 429), b, Finished(4, b));
        for (var i = 0; i < 10; i++) await new SqliteUsageStore(Database).IngestAsync(batch);
        var store = new SqliteUsageStore(Database);
        var summary = await store.SummaryAsync(Query());
        Assert.Equal(4, summary.Cursor);
        Assert.Equal(1, summary.Totals.Requests);
        Assert.Equal(2, summary.Totals.Attempts);
        Assert.Equal(1, summary.Totals.Errors);
        Assert.Equal(28L, summary.Totals.Tokens.Total);
        Assert.Equal(10L, summary.Totals.Tokens.Cached);
        Assert.True(summary.Complete);
        Assert.Equal(2, summary.Accounts.Length);
        var failures = await store.AttemptsAsync(Query() with { StatusCode = 429 });
        Assert.Single(failures.Items);
        Assert.Equal("account-a", failures.Items[0].AccountId);
    }

    [Fact]
    public async Task FailedBatchRollsBackEarlierRowsAndCursorThenReplaysAfterRestart()
    {
        var a = Started(1);
        var invalid = Finished(2, a) with { AccountId = "changed-account" };
        var store = new SqliteUsageStore(Database);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.IngestAsync(Batch(a, invalid)));
        var reopened = new SqliteUsageStore(Database);
        Assert.Equal(0, await reopened.GetCursorAsync("source-a"));
        Assert.Empty((await reopened.AttemptsAsync(Query())).Items);
        await reopened.IngestAsync(Batch(a, Finished(2, a)));
        Assert.Equal(14L, (await reopened.SummaryAsync(Query())).Totals.Tokens.Total);
    }

    [Fact]
    public async Task DurableDispatchWithoutTerminalRemainsUnknownAfterRestartAndLateUsageRepairsIt()
    {
        var a = Started(1);
        await new SqliteUsageStore(Database).IngestAsync(Batch(a));
        var store = new SqliteUsageStore(Database);
        var pending = await store.SummaryAsync(Query());
        Assert.False(pending.Complete);
        Assert.Equal(1, pending.Totals.UnknownUsageAttempts);
        Assert.Null(pending.Totals.Tokens.Total);
        Assert.Null((await store.AttemptsAsync(Query())).Items.Single().FinishedAt);
        await store.IngestAsync(Batch(Finished(2, a)));
        Assert.True((await store.SummaryAsync(Query())).Complete);
        Assert.Equal(14L, (await store.SummaryAsync(Query())).Totals.Tokens.Total);
    }

    [Fact]
    public async Task UnknownComponentsNeverBecomeZeroOrPartialTotals()
    {
        var a = Started(1);
        var b = Started(3, "a2", "r2");
        var store = new SqliteUsageStore(Database);
        await store.IngestAsync(Batch(a, Finished(2, a), b, Finished(4, b, known: false, outcome: "canceled", status: 499)));
        var summary = await store.SummaryAsync(Query());
        Assert.Null(summary.Totals.Tokens.Input);
        Assert.Null(summary.Totals.Tokens.Total);
        Assert.Equal(1, summary.Totals.UnknownUsageAttempts);
        Assert.Equal(1, summary.Totals.Errors);
        Assert.True(summary.Complete); // Complete capture can include unknown provider token usage.
        var page = await store.AttemptsAsync(Query() with { Limit = 1 });
        Assert.Equal(2, page.Total);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task BerlinDstDaysAndHalfOpenInstantFiltersAreCorrect()
    {
        var a = Started(1);
        var b = Started(3, "a2", "r2", at: DateTimeOffset.Parse("2026-03-29T22:30:00Z"));
        var store = new SqliteUsageStore(Database);
        await store.IngestAsync(Batch(a, Finished(2, a), b, Finished(4, b)));
        Assert.Equal(new[] { "2026-03-29", "2026-03-30" }, (await store.SummaryAsync(Query("Europe/Berlin"))).Daily.Select(d => d.Key));
        Assert.Equal(new[] { "2026-03-28", "2026-03-29" }, (await store.SummaryAsync(Query())).Daily.Select(d => d.Key));
        var day = await store.SummaryAsync(new(DateTimeOffset.Parse("2026-03-28T23:00:00Z"), DateTimeOffset.Parse("2026-03-29T22:00:00Z"), "Europe/Berlin"));
        Assert.Equal(1, day.Totals.Attempts);
        Assert.Empty((await store.AttemptsAsync(Query() with { To = a.ObservedAt })).Items);
    }

    [Fact]
    public async Task GapChangedReplayAndOversizedQueryAreRejected()
    {
        var a = Started(1);
        var store = new SqliteUsageStore(Database);
        await store.IngestAsync(Batch(a));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.IngestAsync(Batch(Finished(3, a))));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.IngestAsync(Batch(a with { Model = "changed" })));
        Assert.Equal(1, await store.GetCursorAsync("source-a"));
        await Assert.ThrowsAsync<ArgumentException>(() => store.AttemptsAsync(Query() with { Limit = 501 }));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SummaryAsync(Query() with { From = Start.AddYears(-2) }));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SummaryAsync(Query("No/SuchZone")));
    }

    [Fact]
    public async Task CursorLagAndCollectorFailureAreVisibleAndRecoverable()
    {
        var a = Started(1);
        var store = new SqliteUsageStore(Database);
        await store.IngestAsync(Batch(a, Finished(2, a)) with { HighWatermark = 3 });
        Assert.False((await store.SummaryAsync(Query())).Complete);
        await store.IngestAsync(Batch(Started(3, "a2", "r2"), Finished(4, Started(3, "a2", "r2"))));
        Assert.True((await store.SummaryAsync(Query())).Complete);
        await store.MarkCollectionFailureAsync();
        Assert.False((await new SqliteUsageStore(Database).SummaryAsync(Query())).Complete);
        await store.IngestAsync(new("source-a", [], 4, 4));
        Assert.True((await store.SummaryAsync(Query())).Complete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceReplacementBetweenCleanPollsPersistsOnlyItsUncertainInterval(bool emptyReplacement)
    {
        var old = Started(1);
        var handler = new JournalHandler([old, Finished(2, old)]);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:18418/") };
        var clock = new TestClock(Start.AddSeconds(10));
        var store = new SqliteUsageStore(Database, clock);
        var collector = new JournalUsageCollector(client, store, NullLogger<JournalUsageCollector>.Instance);
        Assert.True(await collector.PollAsync());
        Assert.True((await store.SummaryAsync(Query())).Complete);

        // Normal restart preserves source identity and does not invent a gap.
        clock.Now = Start.AddSeconds(20);
        store = new SqliteUsageStore(Database, clock);
        collector = new JournalUsageCollector(client, store, NullLogger<JournalUsageCollector>.Instance);
        Assert.True(await collector.PollAsync());
        Assert.True((await store.SummaryAsync(Query())).Complete);
        Assert.Equal(0, InspectLong("SELECT COUNT(*) FROM coverage_gaps"));

        // Both journal file and UUID are replaced between completed polls.
        var fresh = Started(1, "new-a", "new-r", at: Start.AddSeconds(35)) with { SourceId = "source-b", EventId = "source-b:1" };
        handler.SourceId = "source-b";
        handler.Events = emptyReplacement ? [] : [fresh, Finished(2, fresh) with { EventId = "source-b:2" }];
        clock.Now = Start.AddSeconds(40);
        Assert.True(await collector.PollAsync());
        Assert.Equal(emptyReplacement ? 0 : 2, await store.GetCursorAsync("source-b"));
        Assert.False((await store.SummaryAsync(Query())).Complete);
        Assert.False((await store.SummaryAsync(Query() with { AccountId = "not-observed", Model = "not-observed", StatusCode = 429 })).Complete);
        Assert.True((await store.SummaryAsync(new(Start.AddSeconds(-1), Start.AddSeconds(20)))).Complete);
        Assert.True((await store.SummaryAsync(new(Start.AddSeconds(40), Start.AddSeconds(60)))).Complete);
        Assert.False((await store.SummaryAsync(new(Start.AddSeconds(20), Start.AddSeconds(40)))).Complete);
        Assert.Equal(1, InspectLong("SELECT COUNT(*) FROM coverage_gaps"));

        clock.Now = Start.AddSeconds(50);
        store = new SqliteUsageStore(Database, clock);
        collector = new JournalUsageCollector(client, store, NullLogger<JournalUsageCollector>.Instance);
        Assert.True(await collector.PollAsync());
        Assert.False((await store.SummaryAsync(Query())).Complete);
        Assert.Equal(1, InspectLong("SELECT COUNT(*) FROM coverage_gaps"));
        Assert.Equal(emptyReplacement ? 1 : 2, (await store.SummaryAsync(Query())).Totals.Attempts);
    }

    [Fact]
    public async Task InvalidReplacementBatchRollsBackSourceGapAndActiveIdentityWithCursor()
    {
        var clock = new TestClock(Start.AddSeconds(10));
        var store = new SqliteUsageStore(Database, clock);
        var old = Started(1);
        await store.IngestAsync(Batch(old, Finished(2, old)));
        var fresh = Started(1, "new-a", "new-r") with { SourceId = "source-b", EventId = "source-b:1" };
        clock.Now = Start.AddSeconds(40);
        var invalid = Finished(2, fresh) with { EventId = "source-b:2", AccountId = "changed-account" };
        await Assert.ThrowsAsync<InvalidDataException>(() => store.IngestAsync(new("source-b", [fresh, invalid], 2, 2)));
        store = new SqliteUsageStore(Database, clock);
        Assert.Equal(0, await store.GetCursorAsync("source-b"));
        Assert.Equal(2, await store.GetCursorAsync("source-a"));
        Assert.Equal(0, InspectLong("SELECT COUNT(*) FROM coverage_gaps"));
        Assert.Equal(1, InspectLong("SELECT COUNT(*) FROM sources"));
        Assert.Equal(1, InspectLong("SELECT COUNT(*) FROM collector_state WHERE active_source_id='source-a'"));
        Assert.True((await store.SummaryAsync(Query())).Complete);
    }

    [Fact]
    public async Task ExistingVersionOneDatabaseMigratesWithoutChangingUsageOrCursor()
    {
        var store = new SqliteUsageStore(Database);
        var started = Started(1);
        await store.IngestAsync(Batch(started, Finished(2, started)));
        RestoreLegacySchema(1);
        store = new SqliteUsageStore(Database);
        Assert.Equal(4, InspectLong("PRAGMA user_version"));
        Assert.Equal(2, await store.GetCursorAsync("source-a"));
        Assert.Equal(0, InspectLong("SELECT COUNT(*) FROM coverage_gaps"));
        var summary = await store.SummaryAsync(Query());
        Assert.True(summary.Complete);
        Assert.Equal(14L, summary.Totals.Tokens.Total);
        await store.IngestAsync(Batch(started, Finished(2, started)));
        Assert.Equal(1, (await store.SummaryAsync(Query())).Totals.Attempts);
    }

    private long InspectLong(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={Database};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task CollectorReplaysOnlyBeyondCommittedCursor()
    {
        var a = Started(1);
        var events = new[] { a, Finished(2, a) };
        var handler = new JournalHandler(events);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:18418/") };
        var store = new SqliteUsageStore(Database);
        var collector = new JournalUsageCollector(client, store, NullLogger<JournalUsageCollector>.Instance);
        Assert.True(await collector.PollAsync());
        Assert.True(await collector.PollAsync());
        Assert.Contains("?after=2&limit=1000", handler.Queries);
        Assert.Equal(1, (await store.SummaryAsync(Query())).Totals.Attempts);
    }

    private sealed class JournalHandler(JournalEvent[] events) : HttpMessageHandler
    {
        public JournalEvent[] Events { get; set; } = events;
        public string SourceId { get; set; } = events[0].SourceId;
        public List<string> Queries { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Queries.Add(request.RequestUri!.Query);
            var values = request.RequestUri.Query.TrimStart('?').Split('&').Select(p => p.Split('=')).ToDictionary(p => p[0], p => p[1]);
            var after = long.Parse(values["after"]);
            var selected = Events.Where(e => e.Sequence > after).Take(int.Parse(values["limit"])).ToArray();
            var batch = new JournalBatch(SourceId, selected, selected.LastOrDefault()?.Sequence ?? after, Events.LastOrDefault()?.Sequence ?? 0);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(batch, JournalJson.Options)) });
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
