using Lain.Core;
using Lain.Host.Recording.Normalization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lain.Host.Recording.Collection;

/// <summary>Single bounded consumer. No inference callbacks, Usage queues or Inspector state are involved.</summary>
public sealed class RecordingCollector(RecordingSourceClient source, IRecordingPolicy policy, IRecordingStore store,
    IRecordingProjection projection, ILogger<RecordingCollector> logger, Func<bool>? shouldCollect = null) : BackgroundService
{
    private RecordingStreamAssembler assembler = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private string? epoch;
    private long cursor;
    private long configuredRevision = -1;
    private bool projectionDirty = true;
    private bool configuredEnabled;
    public string State { get; private set; } = "off";
    public string? LastErrorCode { get; private set; }
    public long DroppedRecords { get; private set; }
    public bool UnknownLoss { get; private set; }
    public DateTimeOffset? LastDurableAt { get; private set; }

    public async Task<bool> PollAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var snapshot = policy.Snapshot;
            if (shouldCollect?.Invoke() == false)
            { State = snapshot.Enabled ? "source_unavailable" : "off"; LastErrorCode = snapshot.Enabled ? "source_unavailable" : null; return true; }
            if (!snapshot.Enabled)
            {
                if (configuredEnabled) { await source.ConfigureAsync(snapshot, ct); configuredEnabled = false; configuredRevision = snapshot.Revision; }
                assembler.Reset(); State = "off"; LastErrorCode = null; return true;
            }
            if (configuredRevision != snapshot.Revision)
            {
                await source.ConfigureAsync(snapshot, ct);
                configuredRevision = snapshot.Revision; configuredEnabled = true;
                assembler.Reset();
            }
            if (projectionDirty) { await projection.RebuildAsync(ct); projectionDirty = false; }
            State = "collecting";
            for (var pass = 0; pass < 8; pass++)
            {
                if (!policy.IsCurrent(snapshot.Revision, snapshot.StopGeneration)) { State = "stopping"; return true; }
                var batch = await source.ReadAsync(cursor, epoch, 32, ct);
                ValidateBatch(batch, snapshot);
                if (epoch != batch.SourceEpoch)
                {
                    UnknownLoss = true; // A process-local feed cannot prove absence of crash loss.
                    assembler.Reset();
                    epoch = batch.SourceEpoch;
                    cursor = await FindDurableCursorAsync(epoch, ct);
                    // The probe did not use the durable cursor of this source generation.
                    continue;
                }
                if (batch.Gap is { } sourceGap && sourceGap.ThroughSequence > cursor)
                {
                    assembler.Reset();
                    var gapObservation = new SourceObservation(epoch, sourceGap.ThroughSequence,
                        "gap:" + sourceGap.ThroughSequence, "gap:" + epoch, "source-gap", DateTimeOffset.UtcNow,
                        "source_handoff", "unknown", "source_gap", Complete: false, GapReason: "source_evicted_or_discarded",
                        PolicyRevision: snapshot.Revision, StopGeneration: snapshot.StopGeneration);
                    var gapSanitized = policy.Sanitize(gapObservation, snapshot) ?? throw new RecordingSourceException("source_policy_mismatch");
                    var gapCommit = await store.CommitAsync(gapSanitized, ct);
                    if (!gapCommit.Committed || gapCommit.Record is null) throw new RecordingSourceException("store_commit_failed");
                    cursor = sourceGap.ThroughSequence; UnknownLoss = true;
                    DroppedRecords += sourceGap.ThroughSequence - sourceGap.FromSequence + 1;
                    try { await projection.ApplyAsync(gapCommit.Record, ct); }
                    catch { projectionDirty = true; throw; }
                }
                long previous = cursor;
                foreach (var observation in batch.Observations)
                {
                    if (observation.Sequence <= cursor) continue;
                    if (!policy.IsCurrent(snapshot.Revision, snapshot.StopGeneration)) { State = "stopping"; return true; }
                    var gap = observation.Sequence != previous + 1 || batch.UnknownLoss || batch.DroppedCount > 0;
                    var candidate = gap ? observation with { Complete = false, GapReason = "source_gap" } : observation;
                    // The sanitizer rejects unassembled SSE/delta/fragment bodies. Only complete JSON or
                    // upstream assembled representations may cross the durable persistence boundary.
                    var prepared = assembler.Push(candidate);
                    var sanitized = policy.Sanitize(prepared, snapshot);
                    if (sanitized is null) throw new RecordingSourceException("source_policy_mismatch");
                    var result = await store.CommitAsync(sanitized, ct);
                    if (!result.Committed || result.Record is null)
                    {
                        if (!policy.IsCurrent(snapshot.Revision, snapshot.StopGeneration)) { State = "stopping"; return true; }
                        throw new RecordingSourceException("store_commit_failed");
                    }
                    // A committed journal record is the sole acknowledgement/checkpoint authority.
                    cursor = observation.Sequence; previous = cursor; LastDurableAt = DateTimeOffset.UtcNow;
                    try { await projection.ApplyAsync(result.Record, ct); }
                    catch { projectionDirty = true; throw; }
                }
                DroppedRecords = Math.Max(DroppedRecords, batch.DroppedCount);
                UnknownLoss |= batch.UnknownLoss || batch.GapReason is not null;
                if (batch.Observations.Length == 0 || batch.HighWatermark is null || cursor >= batch.HighWatermark)
                { State = UnknownLoss || DroppedRecords > 0 ? "partial" : "recording"; LastErrorCode = null; return true; }
            }
            return false;
        }
        finally { gate.Release(); }
    }

    private async Task<long> FindDurableCursorAsync(string sourceEpoch, CancellationToken ct)
    {
        long after = 0, found = 0;
        while (true)
        {
            var page = await store.ReadCommittedAsync(after, 100, ct);
            foreach (var item in page.Records)
                if (item.Observation.SourceEpoch == sourceEpoch) found = Math.Max(found, item.Observation.Sequence);
            if (!page.HasMore) return found;
            if (page.NextCursor <= after) throw new RecordingSourceException("store_cursor_invalid");
            after = page.NextCursor;
        }
    }
    private void ValidateBatch(RecordingSourceBatch batch, CapturePolicySnapshot snapshot)
    {
        if (batch.SchemaVersion != RecordingContract.Version || !SafeIdentity(batch.SourceEpoch) || batch.Observations is null || batch.Observations.Length > 100 || batch.DroppedCount < 0 ||
            batch.NextCursor < 0 || batch.HighWatermark < 0 ||
            batch.Gap is { } invalidGap && (invalidGap.FromSequence < 1 || invalidGap.ThroughSequence < invalidGap.FromSequence ||
                invalidGap.ThroughSequence >= (batch.Observations.FirstOrDefault()?.Sequence ?? long.MaxValue))) throw new RecordingSourceException("source_invalid");
        long last = -1, total = 0;
        foreach (var item in batch.Observations)
        {
            if (item is null || item.SourceEpoch != batch.SourceEpoch || item.Sequence < 1 || item.Sequence <= last ||
                item.SchemaVersion != RecordingContract.Version || item.PolicyRevision != snapshot.Revision ||
                item.StopGeneration != snapshot.StopGeneration || !SafeIdentity(item.EventId) || !SafeIdentity(item.RequestId) ||
                !SafeIdentity(item.CaptureId) || (item.Body?.LongLength ?? 0) > snapshot.EffectiveLimits.MaxRecordBytes)
                throw new RecordingSourceException("source_invalid");
            total += item.Body?.LongLength ?? 0;
            if (total > Math.Min(snapshot.EffectiveLimits.QueueBytes, RecordingSourceClient.MaximumResponseBytes))
                throw new RecordingSourceException("source_oversize");
            last = item.Sequence;
        }
        if (batch.HighWatermark is { } high && (last > high || epoch == batch.SourceEpoch && high < cursor))
            throw new RecordingSourceException("source_reset");
        if (batch.NextCursor is { } next && next != (last < 0 ? batch.Gap?.ThroughSequence ?? cursor : last))
            throw new RecordingSourceException("source_cursor_invalid");
        if (epoch == batch.SourceEpoch && batch.Observations.Length == 0 && batch.HighWatermark > cursor && batch.Gap is null)
            throw new RecordingSourceException("source_gap");
    }
    private static bool SafeIdentity(string? text) => text is { Length: > 0 and <= 256 } &&
        text.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or ':' or '/' or '@' or '+');

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var caughtUp = true;
            try { caughtUp = await PollAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                assembler.Reset();
                if (error is RecordingSourceException { Code: "source_reset" }) { epoch = null; cursor = 0; }
                LastErrorCode = error switch { RecordingSourceException e => e.Code,
                    UnauthorizedAccessException => "storage_unavailable", IOException => "storage_unavailable", _ => "collection_failed" };
                State = LastErrorCode.StartsWith("source_", StringComparison.Ordinal) ? "source_unavailable" : "paused";
                UnknownLoss = true;
                logger.LogWarning("Recording collection paused ({Code}); retrying from durable source cursor", LastErrorCode);
            }
            try { await Task.Delay(caughtUp ? TimeSpan.FromSeconds(1) : TimeSpan.FromMilliseconds(25), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
