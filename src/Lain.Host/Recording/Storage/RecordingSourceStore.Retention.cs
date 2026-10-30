using Lain.Core;

namespace Lain.Host.Recording.Storage;

public sealed partial class RecordingSourceStore
{
    public async Task ClearAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try {
            Interlocked.Increment(ref storeEpoch);
            control = control with { InvalidatedThrough = control.HighWatermark, PurgePending = true, ClearPending = true };
            SaveControl();
            CompleteClear();
        }
        finally { gate.Release(); }
    }

    private void CompleteClear()
    {
        active?.Flush(true); active?.Dispose(); active = null;
        // The durable clear control takes precedence even when the previous source is corrupt.
        // Remove only managed archive material; never follow links during recursive cleanup.
        foreach (var path in System.IO.Directory.EnumerateFileSystemEntries(directory)) {
            var name = Path.GetFileName(path);
            if (name is "writer.lock" or "control.json") continue;
            PrivateArchiveFiles.RejectLinks(path);
            if (System.IO.Directory.Exists(path)) DeletePrivateTree(path);
            else File.Delete(path);
        }
        control = control with { Generation = Guid.NewGuid().ToString("N"), HiddenCaptures = [], ClearPending = false, PurgePending = false };
        PrivateArchiveFiles.Directory(GenerationDirectory);
        PrivateArchiveFiles.Directory(PayloadDirectory);
        records.Clear(); identities.Clear(); error = null;
        PrivateArchiveFiles.SyncDirectory(directory);
        SaveControl();
    }

    private static void DeletePrivateTree(string path)
    {
        PrivateArchiveFiles.RejectLinks(path);
        foreach (var child in System.IO.Directory.EnumerateFileSystemEntries(path)) {
            PrivateArchiveFiles.RejectLinks(child);
            if (System.IO.Directory.Exists(child)) DeletePrivateTree(child); else File.Delete(child);
        }
        System.IO.Directory.Delete(path);
    }

    /// <summary>Whole capture is the deletion unit. Logical denial persists before physical cleanup starts.</summary>
    public async Task PurgeCaptureAsync(string captureId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(captureId) || captureId.Length > 1024) throw new ArgumentException("Invalid recording capture identifier.");
        await gate.WaitAsync(ct);
        try { MarkAndPurge([captureId]); }
        finally { gate.Release(); }
    }

    /// <summary>Whole captures expire at the earliest applicable retained-record deadline.</summary>
    public async Task ApplyRetentionAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try {
            var now = clock.GetUtcNow();
            var expired = records.Where(r => r.Record.Metadata.ObservedAt <= now.AddDays(-options.MetadataRetentionDays) ||
                (r.Record.PayloadFile is not null && r.Record.Metadata.ObservedAt <= now.AddDays(-options.ContentRetentionDays)))
                .Select(r => r.Record.Metadata.CaptureId).Distinct().ToArray();
            if (expired.Length != 0 || control.PurgePending) MarkAndPurge(expired);
        }
        finally { gate.Release(); }
    }

    private void MarkAndPurge(IEnumerable<string> captures)
    {
        if (error == "source_corrupt") throw new IOException("Recording source requires recovery before physical purge.");
        var hidden = (control.HiddenCaptures ?? []).Concat(captures).Distinct(StringComparer.Ordinal).ToArray();
        if (hidden.Length > options.MaximumRecords) throw new IOException("Recording deletion-control bound exceeded.");
        control = control with { HiddenCaptures = hidden, InvalidatedThrough = control.HighWatermark, PurgePending = true };
        SaveControl();
        try { CompactHidden(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { error = "purge_pending"; throw new IOException("Recording physical purge remains pending."); }
    }

    private void CompactHidden()
    {
        active?.Flush(true); active?.Dispose(); active = null;
        var survivors = records.Where(r => !IsHidden(r.Record)).ToArray();
        var estimated = survivors.Sum(r => r.Record.OriginalBytes + 128);
        if (!HasSpace(estimated + 65536)) throw new IOException("Recording purge workspace is exhausted.");
        var nextGeneration = Guid.NewGuid().ToString("N");
        var destination = Path.Combine(directory, nextGeneration);
        PrivateArchiveFiles.Directory(destination);
        var updated = new List<StoredLocation>(survivors.Length);
        var nextCursor = control.HighWatermark;
        FileStream? segment = null;
        string? name = null;
        try {
            foreach (var survivor in survivors) {
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
        records.Clear(); records.AddRange(updated);
        identities.Clear();
        foreach (var item in records) identities.Add((item.Record.Metadata.SourceEpoch, item.Record.Metadata.Sequence), item);
        CleanupUnreferenced();
        // The disposable projection must be closed by its owner during purge. Failure stays pending and cannot claim deletion.
        foreach (var file in new[] { "projection.db", "projection.db-wal", "projection.db-shm", "projection.db-journal" }) {
            var path = Path.Combine(directory, file);
            PrivateArchiveFiles.RejectLinks(path);
            if (File.Exists(path)) File.Delete(path);
        }
        var recovery = Path.Combine(directory, "recovery");
        if (System.IO.Directory.Exists(recovery)) DeletePrivateTree(recovery);
        PrivateArchiveFiles.SyncDirectory(directory);
        control = control with { PurgePending = false };
        SaveControl();
        if (error == "purge_pending") error = null;
    }
}
