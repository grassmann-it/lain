using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lain.Host.Usage;

/// <summary>Polls only the host's own non-destructive journal endpoint. Never consumes the legacy queue.</summary>
public sealed class JournalUsageCollector(HttpClient client, SqliteUsageStore store, ILogger<JournalUsageCollector> logger,
    Func<bool>? shouldCollect = null) : BackgroundService
{
    private const int MaximumResponseBytes = 8 * 1024 * 1024;
    public string? LastError { get; private set; }
    public DateTimeOffset? LastSuccessAt { get; private set; }

    /// <summary>One bounded catch-up pass. False means another pass is required.</summary>
    public async Task<bool> PollAsync(CancellationToken ct = default)
    {
        // Probe the source identity before using a persisted per-source cursor. A replaced
        // journal must not inherit the previous journal's acknowledgement.
        var probe = await ReadAsync(0, 1, ct);
        var source = probe.SourceId;
        var cursor = await store.GetCursorAsync(source, ct);
        for (var iteration = 0; iteration < 64; iteration++)
        {
            var batch = await ReadAsync(cursor, SqliteUsageStore.MaximumBatchSize, ct);
            if (batch.SourceId != source) throw new InvalidDataException("Journal source changed during collection.");
            if (batch.HighWatermark < cursor) throw new InvalidDataException("Journal source was truncated behind the durable cursor.");
            if (batch.Events.Length == 0 && batch.HighWatermark > cursor) throw new InvalidDataException("Journal omitted events before its high watermark.");
            await store.IngestAsync(batch, ct);
            cursor = batch.NextCursor;
            if (cursor >= batch.HighWatermark) return true;
        }
        return false;
    }

    private async Task<JournalBatch> ReadAsync(long cursor, int limit, CancellationToken ct)
    {
        using var response = await client.GetAsync($"lain/events?after={cursor.ToString(CultureInfo.InvariantCulture)}&limit={limit}", HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct)) != 0)
        {
            if (memory.Length + count > MaximumResponseBytes) throw new InvalidDataException("Journal response exceeds bounded collector size.");
            memory.Write(buffer, 0, count);
        }
        return JsonSerializer.Deserialize<JournalBatch>(memory.ToArray(), JournalJson.Options) ?? throw new InvalidDataException("Empty journal response.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var caughtUp = true;
            try
            {
                if (shouldCollect?.Invoke() != false)
                {
                    caughtUp = await PollAsync(stoppingToken);
                    LastSuccessAt = DateTimeOffset.UtcNow;
                    LastError = null;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                if (shouldCollect?.Invoke() == false) continue;
                LastError = error.GetType().Name;
                // Do not log upstream response text; provider metadata can contain sensitive data.
                logger.LogWarning("Usage journal collection failed ({ErrorType}); retrying from committed cursor", LastError);
                try { await store.MarkCollectionFailureAsync(stoppingToken); }
                catch (Exception persistenceError) when (persistenceError is not OperationCanceledException)
                { logger.LogError("Could not persist usage coverage failure ({ErrorType})", persistenceError.GetType().Name); }
            }
            try { await Task.Delay(caughtUp ? TimeSpan.FromSeconds(1) : TimeSpan.FromMilliseconds(10), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
