using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lain.Host.Inspector;

/// <summary>One reader of the owned core's non-destructive RAM feed. Never reads legacy queues.</summary>
public sealed class InspectorCollector(HttpClient client, InspectorStore store, ILogger<InspectorCollector> logger) : BackgroundService
{
    private const int MaximumResponseBytes = 8 * 1024 * 1024;
    private const int BatchSize = 500;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 32 };
    private readonly SemaphoreSlim pollGate = new(1, 1);
    private string? source;
    private string? lastErrorType;
    private long after;

    /// <summary>Bounded catch-up pass. False requests another pass without a normal polling delay.</summary>
    public async Task<bool> PollAsync(CancellationToken ct = default)
    {
        await pollGate.WaitAsync(ct);
        try
        {
            for (var iteration = 0; iteration < 16; iteration++)
            {
                var requestedAfter = after;
                var batch = await ReadAsync(requestedAfter, ct);
                ValidateEnvelope(batch);
                if (source != batch.InstanceId)
                {
                    source = batch.InstanceId; after = 0; store.BeginSource(source);
                    // An old generation's cursor cannot select a tail in a new generation.
                    if (requestedAfter != 0) continue;
                }
                ValidatePage(batch, after);
                if (batch.Events.Length == 0 && after < batch.HighWatermark)
                {
                    // Every unseen event was evicted. Advancing is accompanied by an explicit gap.
                    store.MarkUpstreamGap(batch.HighWatermark); after = batch.HighWatermark;
                }
                else
                {
                    foreach (var item in batch.Events) store.Ingest(item);
                    after = batch.NextCursor;
                }
                store.MarkConnected(); lastErrorType = null;
                if (after >= batch.HighWatermark) return true;
            }
            return false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch
        {
            store.MarkUnavailable();
            throw;
        }
        finally { pollGate.Release(); }
    }

    private static void ValidateEnvelope(CoreInspectorBatch batch)
    {
        if (InspectorTextRedactor.Identity(batch.InstanceId) is not { } safeSource || safeSource != batch.InstanceId || batch.Events is null || batch.Events.Length > BatchSize ||
            batch.EarliestCursor < 0 || batch.HighWatermark < 0 || batch.NextCursor < 0 || batch.NextCursor > batch.HighWatermark || batch.DroppedCount < 0 ||
            batch.EarliestCursor > batch.HighWatermark && batch.EarliestCursor - batch.HighWatermark > 1)
            throw new InvalidDataException("Invalid owned inspector envelope.");
    }

    private static void ValidatePage(CoreInspectorBatch batch, long cursor)
    {
        if (cursor > batch.HighWatermark) throw new InvalidDataException("Inspector source moved behind its cursor without changing instance.");
        var previous = cursor;
        foreach (var item in batch.Events)
        {
            if (item is null || item.Sequence <= previous || item.Sequence > batch.HighWatermark || item.Sequence < batch.EarliestCursor)
                throw new InvalidDataException("Invalid owned inspector sequence.");
            previous = item.Sequence;
        }
        if (batch.NextCursor != previous || batch.Events.Length == 0 && cursor < batch.HighWatermark && batch.EarliestCursor <= batch.HighWatermark)
            throw new InvalidDataException("Owned inspector omitted a retained interval.");
    }

    private async Task<CoreInspectorBatch> ReadAsync(long cursor, CancellationToken ct)
    {
        using var response = await client.GetAsync($"lain/inspector/events?after={cursor.ToString(CultureInfo.InvariantCulture)}&limit={BatchSize}", HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumResponseBytes) throw new InvalidDataException("Inspector response exceeds its bound.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct)) != 0)
        {
            if (memory.Length + count > MaximumResponseBytes) throw new InvalidDataException("Inspector response exceeds its bound.");
            memory.Write(buffer, 0, count);
        }
        return JsonSerializer.Deserialize<CoreInspectorBatch>(memory.ToArray(), Json) ?? throw new InvalidDataException("Empty inspector response.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromMilliseconds(250);
            try { if (!await PollAsync(stoppingToken)) delay = TimeSpan.FromMilliseconds(10); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                var type = exception.GetType().Name;
                if (lastErrorType != type) logger.LogWarning("Owned inspector collection unavailable ({ErrorType}); retrying bounded cursor feed", type);
                lastErrorType = type; delay = TimeSpan.FromSeconds(2);
            }
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    public override void Dispose() { base.Dispose(); client.Dispose(); }
}
