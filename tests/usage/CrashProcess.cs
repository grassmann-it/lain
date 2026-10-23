using Lain.Host.Usage;
using Microsoft.Data.Sqlite;

namespace Lain.Usage.Tests;

internal static class CrashProcess
{
    public static async Task Main(string[] args)
    {
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
