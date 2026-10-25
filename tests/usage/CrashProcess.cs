using Lain.Host.Usage;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lain.Usage.Tests;

internal static class CrashProcess
{
    public static async Task Main(string[] args)
    {
        if (args.Length == 4 && args[0] == "--collect-journal")
        {
            if (!Uri.TryCreate(args[1], UriKind.Absolute, out var address) || address.Scheme != "http" ||
                address.Host != "127.0.0.1" || address.Port is 18417 or 18418 || address.AbsolutePath != "/" ||
                address.Query.Length != 0 || address.Fragment.Length != 0 || address.UserInfo.Length != 0)
                throw new ArgumentException("Archive collector fixture requires its explicit private HTTP loopback URL.");
            var batch = JsonSerializer.Deserialize<JournalBatch>(await File.ReadAllTextAsync(args[2]), JournalJson.Options)!;
            if (batch.Events.Length == 0 || batch.Events.Length != batch.HighWatermark || batch.NextCursor != batch.HighWatermark ||
                !Path.IsPathFullyQualified(args[3])) throw new ArgumentException("Complete synthetic envelope and absolute database path required.");
            for (var replay = 0; replay < 3; replay++)
            {
                // Use the actual Host collector, reopening its real SQLite store
                // and resuming the durable per-source cursor every time.
                using var handler = new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false };
                using var client = new HttpClient(handler) { BaseAddress = address, Timeout = TimeSpan.FromSeconds(15) };
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "lain-synthetic-private-management-token");
                var store = new SqliteUsageStore(args[3]);
                using var collector = new JournalUsageCollector(client, store, NullLogger<JournalUsageCollector>.Instance);
                if (!await collector.PollAsync() || await store.GetCursorAsync(batch.SourceId) != batch.HighWatermark)
                    throw new InvalidDataException("Archive collector did not reach the expected unchanged source/cursor.");
            }
            var query = new Lain.Core.UsageQuery(batch.Events.Min(e => e.ObservedAt).AddSeconds(-1), batch.Events.Max(e => e.ObservedAt).AddSeconds(1));
            var summary = await new SqliteUsageStore(args[3]).SummaryAsync(query);
            Console.WriteLine(JsonSerializer.Serialize(new { summary.Cursor, summary.Complete, summary.Totals, Days = summary.Daily.Length, Accounts = summary.Accounts.Length, Models = summary.Models.Length }, JournalJson.Options));
            return;
        }
        if (args.Length == 3 && args[0] == "--ingest-journal")
        {
            var batch = JsonSerializer.Deserialize<JournalBatch>(await File.ReadAllTextAsync(args[1]), JournalJson.Options)!;
            for (var replay = 0; replay < 10; replay++) await new SqliteUsageStore(args[2]).IngestAsync(batch);
            var query = new Lain.Core.UsageQuery(batch.Events.Min(e => e.ObservedAt).AddSeconds(-1), batch.Events.Max(e => e.ObservedAt).AddSeconds(1));
            var summary = await new SqliteUsageStore(args[2]).SummaryAsync(query);
            Console.WriteLine(JsonSerializer.Serialize(new { summary.Cursor, summary.Complete, summary.Totals, Days = summary.Daily.Length, Accounts = summary.Accounts.Length, Models = summary.Models.Length }, JournalJson.Options));
            return;
        }
        if (args.Length != 2 || args[0] != "--crash-writer") return;
        var store = new SqliteUsageStore(args[1]);
        var started = new JournalEvent(1, "source-a", 1, "source-a:1", "attempt_started", "r1", "a1", "account-a", "credential-account-a",
            "route-a", DateTimeOffset.Parse("2026-03-28T23:30:00Z"), "fake", "alias", "real-model");
        await store.IngestAsync(new("source-a", [started], 1, 1));
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = args[1], Pooling = false }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE attempts SET state='success', usage_reported=1, total_tokens=999 WHERE attempt_id='a1';
            UPDATE sources SET cursor=2,high_watermark=2 WHERE source_id='source-a';
            INSERT INTO journal_events VALUES('source-a',2,'source-a:2','not-committed');
            """;
        command.ExecuteNonQuery();
        Console.WriteLine("uncommitted");
        await Console.Out.FlushAsync();
        await Task.Delay(Timeout.InfiniteTimeSpan);
    }
}
