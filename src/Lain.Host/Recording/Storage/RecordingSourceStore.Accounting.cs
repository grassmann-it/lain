namespace Lain.Host.Recording.Storage;

/// <summary>Content-free work counters for qualification; querying them performs no archive I/O.</summary>
public sealed record RecordingStoreDiagnostics(long DiskReconciliations, long DirectoryEntriesScanned,
    long ReplaySearchSteps, long PayloadReads);

public sealed partial class RecordingSourceStore
{
    private readonly Dictionary<string, long> trackedFiles = new(StringComparer.Ordinal);
    private readonly HashSet<string> hiddenCaptures = new(StringComparer.Ordinal);
    private readonly List<StoredLocation> visibleRecords = [];
    private long cachedDiskBytes, diskReconciliations, directoryEntriesScanned, replaySearchSteps, payloadReads;
    private bool diskAccountingValid;
    private DriveInfo? archiveDrive;

    public RecordingStoreDiagnostics Diagnostics => new(Interlocked.Read(ref diskReconciliations),
        Interlocked.Read(ref directoryEntriesScanned), Interlocked.Read(ref replaySearchSteps), Interlocked.Read(ref payloadReads));

    private void RefreshVisibility()
    {
        hiddenCaptures.Clear();
        foreach (var capture in control.HiddenCaptures ?? []) hiddenCaptures.Add(capture);
        visibleRecords.Clear();
        foreach (var record in records) if (!IsHidden(record.Record)) visibleRecords.Add(record);
    }

    private int FirstVisibleAfter(long cursor)
    {
        var low = 0;
        var high = visibleRecords.Count;
        while (low < high) {
            Interlocked.Increment(ref replaySearchSteps);
            var middle = low + (high - low) / 2;
            if (visibleRecords[middle].Record.Cursor <= cursor) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    private long DiskBytes() => cachedDiskBytes;

    // Full reconciliation belongs to recovery, scheduled maintenance and failure recovery only.
    // Commit/health track owned writes and sample at most the four optional SQLite files.
    private void ReconcileDiskUsage()
    {
        Interlocked.Increment(ref diskReconciliations);
        var found = new Dictionary<string, long>(StringComparer.Ordinal);
        long bytes = 0;
        var pending = new Stack<string>(); pending.Push(directory);
        while (pending.TryPop(out var folder)) {
            foreach (var path in System.IO.Directory.EnumerateFileSystemEntries(folder)) {
                Interlocked.Increment(ref directoryEntriesScanned);
                PrivateArchiveFiles.RejectLinks(path);
                if (System.IO.Directory.Exists(path)) pending.Push(path);
                else if (File.Exists(path)) {
                    var length = new FileInfo(path).Length;
                    found.Add(path, length); bytes = checked(bytes + length);
                }
            }
        }
        trackedFiles.Clear();
        foreach (var item in found) trackedFiles.Add(item.Key, item.Value);
        cachedDiskBytes = bytes;
        diskAccountingValid = true;
    }

    private void TrackFileLength(string path, long length)
    {
        var previous = trackedFiles.GetValueOrDefault(path);
        cachedDiskBytes = checked(cachedDiskBytes - previous + length);
        trackedFiles[path] = length;
    }

    private void WriteTrackedAtomic(string path, ReadOnlySpan<byte> bytes)
    {
        // Account for the full temporary file alongside the old destination before writing.
        // On failure this remains a conservative upper bound until reconciliation.
        if (diskAccountingValid && !HasSpace(bytes.Length)) throw new IOException("Recording archive workspace is exhausted.");
        var temporary = path + ".new";
        TrackFileLength(temporary, bytes.Length);
        try {
            PrivateArchiveFiles.AtomicWrite(path, bytes);
            ForgetFile(temporary);
            TrackFileLength(path, bytes.Length);
        }
        catch { diskAccountingValid = false; throw; }
    }

    private void ForgetFile(string path)
    {
        if (trackedFiles.Remove(path, out var bytes)) cachedDiskBytes -= bytes;
    }

    private void DeleteTrackedFile(string path)
    {
        File.Delete(path);
        ForgetFile(path);
    }

    private void SampleProjectionFiles()
    {
        foreach (var name in new[] { "projection.db", "projection.db-wal", "projection.db-shm", "projection.db-journal" }) {
            var path = Path.Combine(directory, name);
            PrivateArchiveFiles.RejectLinks(path);
            if (File.Exists(path)) TrackFileLength(path, new FileInfo(path).Length);
            else ForgetFile(path);
        }
    }

    private DriveInfo FindArchiveDrive()
    {
        // Mount discovery is cold-path work. AvailableFreeSpace still samples the selected
        // shared filesystem before writes, without re-enumerating all mounts each time.
        var fallback = new DriveInfo(Path.GetPathRoot(directory)!);
        if (!OperatingSystem.IsLinux()) return fallback;
        return DriveInfo.GetDrives().Where(d => directory == d.Name.TrimEnd('/') || directory.StartsWith(d.Name.TrimEnd('/') + "/", StringComparison.Ordinal))
            .OrderByDescending(d => d.Name.Length).FirstOrDefault() ?? fallback;
    }

    private void ReconcileAfterFailure()
    {
        diskAccountingValid = false;
        try { if (initialized) ReconcileDiskUsage(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Keep conservative tracked bytes and fixed caller health error. */ }
    }
}
