using System.Text.Json;

namespace Lain.Host.Recording.Storage;

public sealed partial class RecordingSourceStore
{
    private bool recoveryUncertain;

    private void Recover(CancellationToken ct = default)
    {
        PrivateArchiveFiles.Directory(PayloadDirectory);
        if (File.Exists(ControlPath)) {
            using var stream = PrivateArchiveFiles.Open(ControlPath, FileMode.Open, FileAccess.Read);
            if (stream.Length > MaximumControlBytes) throw new InvalidDataException("Recording control state exceeds its bound.");
            control = JsonSerializer.Deserialize<ArchiveControl>(stream, RecordingFrames.Json)
                ?? throw new InvalidDataException("Recording control state is invalid.");
            if (control.Version is not (1 or ArchiveControlVersion)) throw new InvalidDataException("Recording control state version is unsupported; preserve the archive.");
            if (!Guid.TryParseExact(control.Generation, "N", out _) || control.HighWatermark < 0 || control.InvalidatedThrough < 0)
                throw new InvalidDataException("Recording control state is invalid.");
        }
        else {
            // Never invent a new generation over an existing archive with a missing manifest.
            if (System.IO.Directory.EnumerateDirectories(directory).Any(p => Path.GetFileName(p) != "payloads") || System.IO.Directory.EnumerateFiles(PayloadDirectory).Any())
                throw new InvalidDataException("Recording control state is missing; preserve the archive.");
            control = new(ArchiveControlVersion, Guid.NewGuid().ToString("N"));
            PrivateArchiveFiles.Directory(GenerationDirectory);
            SaveControl();
        }
        UpgradeDeletionControl();
        RefreshVisibility();
        PrivateArchiveFiles.Directory(GenerationDirectory);
        if (control.ClearPending) { CompleteClear(ct); return; }
        var files = System.IO.Directory.EnumerateFiles(GenerationDirectory, "journal-*.rec").Order(StringComparer.Ordinal).ToArray();
        for (var index = 0; index < files.Length; index++) {
            ct.ThrowIfCancellationRequested();
            var path = files[index];
            using var stream = PrivateArchiveFiles.Open(path, FileMode.Open, FileAccess.ReadWrite);
            try {
                while (stream.Position < stream.Length) {
                    var start = stream.Position;
                    var item = RecordingFrames.Read(stream, options.MaximumRecordBytes, out var torn, ct);
                    if (torn) {
                        if (index != files.Length - 1) throw new InvalidDataException("Sealed recording segment has an incomplete frame.");
                        QuarantineTail(stream, start, path);
                        recoveryUncertain = true;
                        break;
                    }
                    if (item is null) break;
                    if (records.Count >= options.MaximumRecords || (records.Count != 0 && item.Cursor <= records[^1].Record.Cursor))
                        throw new InvalidDataException("Recording source order or bound is invalid.");
                    var location = new StoredLocation(item, Path.GetFileName(path), start);
                    if (!identities.TryAdd((item.Metadata.SourceEpoch, item.Metadata.Sequence), location))
                        throw new InvalidDataException("Recording source identity is repeated.");
                    records.Add(location);
                    control = control with { HighWatermark = Math.Max(control.HighWatermark, item.Cursor) };
                    if (!IsHidden(item)) { visibleRecords.Add(location); _ = Materialize(location, ct); } // Validate referenced payload before exposing a committed prefix.
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ArgumentException) {
                error = "source_corrupt";
                // Preserve all damaged material, never silently truncate checksum failures/interior corruption.
                break;
            }
        }
        recoveryUncertain |= records.Count != 0;
        if (error is null) {
            SaveControl();
            if (control.PurgePending) CompactHidden(ct);
            if (!control.PurgePending) CleanupUnreferenced(ct);
        }
    }

    private void QuarantineTail(FileStream stream, long offset, string journalPath)
    {
        var length = stream.Length - offset;
        if (length > options.MaximumRecordBytes + 65536L || !HasSpace(length + 4096))
            throw new InvalidDataException("Recording recovery workspace is exhausted.");
        var recovery = Path.Combine(directory, "recovery");
        PrivateArchiveFiles.Directory(recovery);
        var targetPath = Path.Combine(recovery, Guid.NewGuid().ToString("N") + ".tail");
        TrackFileLength(targetPath, length);
        using (var target = PrivateArchiveFiles.Open(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
            stream.Position = offset;
            stream.CopyTo(target);
            target.Flush(true);
        }
        PrivateArchiveFiles.SyncDirectory(recovery);
        stream.SetLength(offset);
        TrackFileLength(journalPath, offset);
        stream.Flush(true);
    }

    private void CleanupUnreferenced(CancellationToken ct = default)
    {
        var reachable = records.Where(r => !IsHidden(r.Record)).Select(r => r.Record.PayloadFile).Where(p => p is not null).ToHashSet(StringComparer.Ordinal);
        foreach (var path in System.IO.Directory.EnumerateFiles(PayloadDirectory)) {
            ct.ThrowIfCancellationRequested();
            PrivateArchiveFiles.RejectLinks(path);
            if (!reachable.Contains(Path.GetFileName(path))) DeleteTrackedFile(path);
        }
        // A generation not named by durable control cannot become active after a crash or restore.
        foreach (var path in System.IO.Directory.EnumerateDirectories(directory)) {
            ct.ThrowIfCancellationRequested();
            PrivateArchiveFiles.RejectLinks(path);
            if (Guid.TryParseExact(Path.GetFileName(path), "N", out _) && path != GenerationDirectory) DeletePrivateTree(path, ct);
        }
        PrivateArchiveFiles.SyncDirectory(directory);
    }
}
