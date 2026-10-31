using Lain.Core;

namespace Lain.Host.Recording.Storage;

public sealed partial class RecordingSourceStore
{
    public async Task ClearAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try {
            Interlocked.Increment(ref storeEpoch);
            if (!initialized) { if (error is not null) throw new IOException("Recording archive is unavailable."); return; }
            control = control with { InvalidatedThrough = control.HighWatermark, PurgePending = true, ClearPending = true };
            SaveControl();
            CompleteClear();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { error = "purge_pending"; ReconcileAfterFailure(); throw new IOException("recording_clear_failed"); }
        finally { gate.Release(); }
    }

    private void CompleteClear(CancellationToken ct = default)
    {
        active?.Flush(true); active?.Dispose(); active = null;
        // The durable clear control takes precedence even when the previous source is corrupt.
        // Remove only managed archive material; never follow links during recursive cleanup.
        foreach (var path in System.IO.Directory.EnumerateFileSystemEntries(directory)) {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(path);
            if (name is "writer.lock" or "control.json") continue;
            PrivateArchiveFiles.RejectLinks(path);
            if (System.IO.Directory.Exists(path)) DeletePrivateTree(path, ct);
            else DeleteTrackedFile(path);
        }
        control = control with { Generation = Guid.NewGuid().ToString("N"), Version = ArchiveControlVersion, HiddenCaptures = null, HiddenCaptureHashes = [], ClearPending = false, PurgePending = false };
        PrivateArchiveFiles.Directory(GenerationDirectory);
        PrivateArchiveFiles.Directory(PayloadDirectory);
        records.Clear(); identities.Clear(); RefreshVisibility(); error = null;
        PrivateArchiveFiles.SyncDirectory(directory);
        SaveControl();
    }

    private void DeletePrivateTree(string path, CancellationToken ct = default)
    {
        PrivateArchiveFiles.RejectLinks(path);
        foreach (var child in System.IO.Directory.EnumerateFileSystemEntries(path)) {
            ct.ThrowIfCancellationRequested();
            PrivateArchiveFiles.RejectLinks(child);
            if (System.IO.Directory.Exists(child)) DeletePrivateTree(child, ct); else DeleteTrackedFile(child);
        }
        System.IO.Directory.Delete(path);
    }

    /// <summary>Whole capture is the deletion unit. Logical denial persists before physical cleanup starts.</summary>
    public async Task PurgeCaptureAsync(string captureId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(captureId) || captureId.Length > 1024) throw new ArgumentException("Invalid recording capture identifier.");
        await gate.WaitAsync(ct);
        try { if (initialized) MarkAndPurge([captureId]); }
        catch (RecordingDeletionCapacityException) { throw; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { if (error is not ("source_corrupt" or "archive_unavailable")) error = "purge_pending"; ReconcileAfterFailure(); throw new IOException("recording_purge_failed"); }
        finally { gate.Release(); }
    }

    /// <summary>Whole captures expire at the earliest applicable retained-record deadline.</summary>
    public async Task ApplyRetentionAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try {
            if (!initialized || error is "source_corrupt" or "archive_unavailable") return;
            ReconcileDiskUsage(); // Scheduled maintenance is the deliberate reconciliation boundary.
            var now = clock.GetUtcNow();
            var limits = RetentionLimits();
            var expired = records.Where(r => r.Record.Metadata.ObservedAt <= now.AddDays(-limits.MetadataDays) ||
                (r.Record.PayloadFile is not null && r.Record.Metadata.ObservedAt <= now.AddDays(-limits.ContentDays)))
                .Select(r => r.Record.Metadata.CaptureId).Distinct().ToArray();
            if (expired.Length != 0 || control.PurgePending) MarkAndPurge(expired);
        }
        catch (RecordingDeletionCapacityException) { throw; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { if (error is not ("source_corrupt" or "archive_unavailable")) error = "purge_pending"; ReconcileAfterFailure(); throw new IOException("recording_retention_failed"); }
        finally { gate.Release(); }
    }

    private void MarkAndPurge(IEnumerable<string> captures)
    {
        if (error == "source_corrupt") throw new IOException("Recording source requires recovery before physical purge.");
        var hidden = new HashSet<string>(hiddenCaptures, StringComparer.Ordinal);
        foreach (var capture in captures) {
            var hash = CaptureTombstone(capture);
            if (!hidden.Contains(hash) && hidden.Count >= DeletionCapacity)
                throw new RecordingDeletionCapacityException();
            hidden.Add(hash);
        }
        // Capacity failure happens before changing visible state or PurgePending.
        control = control with { Version = ArchiveControlVersion, HiddenCaptures = null, HiddenCaptureHashes = hidden.ToArray(), InvalidatedThrough = control.HighWatermark, PurgePending = true };
        RefreshVisibility();
        SaveControl();
        try { CompactHidden(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { error = "purge_pending"; throw new IOException("Recording physical purge remains pending."); }
    }

    private void CompactHidden(CancellationToken ct = default)
    {
        active?.Flush(true); active?.Dispose(); active = null;
        var survivors = records.Where(r => !IsHidden(r.Record)).ToArray();
        var nextGeneration = Guid.NewGuid().ToString("N");
        // Only source metadata frames are rewritten. Payloads remain in place; their
        // uncompressed OriginalBytes are not compaction workspace. Measure each exact
        // remapped frame in a bounded first pass without retaining all encoded frames.
        long frameBytes = 0;
        var estimateCursor = control.HighWatermark;
        foreach (var survivor in survivors) {
            ct.ThrowIfCancellationRequested();
            var retained = survivor.Record with { Cursor = checked(++estimateCursor) };
            frameBytes = checked(frameBytes + RecordingFrames.Encode(retained).LongLength);
        }
        var publication = control with { Generation = nextGeneration, HighWatermark = estimateCursor, InvalidatedThrough = Math.Max(control.InvalidatedThrough, control.HighWatermark) };
        var controlBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(publication, RecordingFrames.Json).LongLength;
        if (!HasSpace(checked(frameBytes + controlBytes + 65536))) throw new IOException("Recording purge workspace is exhausted.");
        var destination = Path.Combine(directory, nextGeneration);
        PrivateArchiveFiles.Directory(destination);
        var updated = new List<StoredLocation>(survivors.Length);
        var nextCursor = control.HighWatermark;
        FileStream? segment = null;
        string? name = null;
        try {
            foreach (var survivor in survivors) {
                ct.ThrowIfCancellationRequested();
                // Reserve a new monotonic cursor range for this generation so old long cursors
                // invalidate explicitly while a restarted paged scan can progress through survivors.
                var retained = survivor.Record with { Cursor = checked(++nextCursor) };
                var frame = RecordingFrames.Encode(retained);
                if (segment is null || segment.Length + frame.Length > options.SegmentBytes) {
                    segment?.Flush(true); segment?.Dispose();
                    name = $"journal-{retained.Cursor:D20}.rec";
                    segment = PrivateArchiveFiles.Open(Path.Combine(destination, name), FileMode.CreateNew, FileAccess.Write);
                }
                var offset = segment.Position;
                TrackFileLength(Path.Combine(destination, name!), segment.Length + frame.Length);
                segment.Write(frame);
                updated.Add(new(retained, name!, offset));
            }
            segment?.Flush(true);
        }
        finally { segment?.Dispose(); }
        PrivateArchiveFiles.SyncDirectory(destination);
        // New generation is authoritative before removing old source material. Pending=true survives any following crash.
        control = control with { Generation = nextGeneration, HighWatermark = nextCursor, InvalidatedThrough = Math.Max(control.InvalidatedThrough, control.HighWatermark) };
        SaveControl();
        records.Clear(); records.AddRange(updated); RefreshVisibility();
        identities.Clear();
        foreach (var item in records) identities.Add((item.Record.Metadata.SourceEpoch, item.Record.Metadata.Sequence), item);
        CleanupUnreferenced(ct);
        // The disposable projection must be closed by its owner during purge. Failure stays pending and cannot claim deletion.
        foreach (var file in new[] { "projection.db", "projection.db-wal", "projection.db-shm", "projection.db-journal" }) {
            var path = Path.Combine(directory, file);
            PrivateArchiveFiles.RejectLinks(path);
            if (File.Exists(path)) DeleteTrackedFile(path);
        }
        var recovery = Path.Combine(directory, "recovery");
        if (System.IO.Directory.Exists(recovery)) DeletePrivateTree(recovery, ct);
        PrivateArchiveFiles.SyncDirectory(directory);
        control = control with { PurgePending = false };
        SaveControl();
        if (error == "purge_pending") error = null;
    }
}
