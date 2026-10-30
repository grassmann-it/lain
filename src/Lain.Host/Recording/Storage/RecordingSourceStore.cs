using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Lain.Core;

namespace Lain.Host.Recording.Storage;

/// <summary>Independent private source archive. All disk work runs on one bounded writer.</summary>
public sealed partial class RecordingSourceStore : IRecordingStore, IAsyncDisposable
{
    private sealed record Pending(SanitizedObservation Observation, long Bytes, long Epoch, TaskCompletionSource<RecordingCommitResult> Completion);
    private readonly string directory;
    private readonly IRecordingPolicy policy;
    private readonly RecordingStoreOptions options;
    private readonly TimeProvider clock;
    private FileStream? writerLock;
    private bool initialized;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Channel<Pending> queue;
    private readonly Task writer;
    private readonly List<StoredLocation> records = [];
    private readonly Dictionary<(string Epoch, long Sequence), StoredLocation> identities = [];
    private ArchiveControl control = null!;
    private long queuedBytes, dropped, storeEpoch;
    private string? error;
    private bool disposed;
    private FileStream? active;
    private string? activeName;

    public RecordingSourceStore(string directory, IRecordingPolicy policy, RecordingStoreOptions? options = null, TimeProvider? clock = null)
    {
        this.directory = Path.GetFullPath(directory);
        this.policy = policy;
        this.options = options ?? new();
        this.clock = clock ?? TimeProvider.System;
        ValidateOptions(this.options);
        control = new(1, Guid.NewGuid().ToString("N"));
        // A disabled/new installation is inert: no journal, payload or lock creation on construction/health.
        if (System.IO.Directory.Exists(this.directory)) {
            try {
                PrivateArchiveFiles.RejectLinks(this.directory);
                if (System.IO.Directory.EnumerateFileSystemEntries(this.directory).Any()) Initialize();
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or PlatformNotSupportedException) {
                active?.Dispose(); active = null; writerLock?.Dispose(); writerLock = null;
                error = "archive_unavailable";
            }
        }
        queue = Channel.CreateBounded<Pending>(new BoundedChannelOptions(this.options.QueueCount) {
            SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
        writer = Task.Run(WriteLoopAsync);
    }

    private void Initialize()
    {
        PrivateArchiveFiles.Directory(directory);
        writerLock = PrivateArchiveFiles.Open(Path.Combine(directory, "writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try { Recover(); initialized = true; }
        catch { active?.Dispose(); writerLock.Dispose(); writerLock = null; throw; }
    }

    public Task<RecordingCommitResult> CommitAsync(SanitizedObservation observation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ct.ThrowIfCancellationRequested();
        if (disposed) return Task.FromResult(new RecordingCommitResult(false, ErrorCode: "store_closed"));
        // UTF8 counting is bounded by the already sanitized input boundary. Do not serialize raw content here.
        var bytes = (long)Encoding.UTF8.GetByteCount(observation.Body ?? "") + 4096;
        if (bytes > options.MaximumRecordBytes || !ValidMetadata(observation))
            return Task.FromResult(new RecordingCommitResult(false, ErrorCode: "invalid_or_oversize_record"));
        if (Interlocked.Add(ref queuedBytes, bytes) > options.QueueBytes) {
            Interlocked.Add(ref queuedBytes, -bytes); Interlocked.Increment(ref dropped);
            return Task.FromResult(new RecordingCommitResult(false, ErrorCode: "queue_full"));
        }
        var completion = new TaskCompletionSource<RecordingCommitResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!queue.Writer.TryWrite(new(observation, bytes, Interlocked.Read(ref storeEpoch), completion))) {
            Interlocked.Add(ref queuedBytes, -bytes); Interlocked.Increment(ref dropped);
            completion.SetResult(new(false, ErrorCode: "queue_full"));
        }
        // Cancellation stops waiting, but cannot undo a durable record already being written.
        return ct.CanBeCanceled ? completion.Task.WaitAsync(ct) : completion.Task;
    }

    private async Task WriteLoopAsync()
    {
        await foreach (var pending in queue.Reader.ReadAllAsync()) {
            await gate.WaitAsync();
            try {
                var result = pending.Epoch == Interlocked.Read(ref storeEpoch) ? Commit(pending.Observation) : new RecordingCommitResult(false, ErrorCode: "store_cleared");
                if (!result.Committed) Interlocked.Increment(ref dropped);
                pending.Completion.TrySetResult(result);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException or PlatformNotSupportedException) {
                error = "archive_write_failed";
                Interlocked.Increment(ref dropped);
                pending.Completion.TrySetResult(new(false, ErrorCode: error));
            }
            finally { Interlocked.Add(ref queuedBytes, -pending.Bytes); gate.Release(); }
        }
    }

    private RecordingCommitResult Commit(SanitizedObservation observation)
    {
        if (control.PurgePending) return new(false, ErrorCode: "purge_pending");
        if (error is not null) return new(false, ErrorCode: error);
        if (!policy.IsCurrent(observation.PolicyRevision, observation.StopGeneration)) return new(false, ErrorCode: "policy_stopped_or_changed");
        if (!initialized) Initialize();
        if ((control.HiddenCaptures ?? []).Contains(observation.CaptureId, StringComparer.Ordinal)) return new(false, ErrorCode: "capture_deleted");
        var serialized = JsonSerializer.SerializeToUtf8Bytes(observation, RecordingFrames.Json);
        if (serialized.Length > options.MaximumRecordBytes) return new(false, ErrorCode: "record_too_large");
        var hash = RecordingFrames.Hash(serialized);
        if (identities.TryGetValue((observation.SourceEpoch, observation.Sequence), out var duplicate)) {
            if (duplicate.Record.ContentHash != hash) return new(false, ErrorCode: "source_identity_conflict");
            return new(true, Materialize(duplicate));
        }
        if (records.Count >= options.MaximumRecords) return new(false, ErrorCode: "record_limit");
        var cursor = checked(control.HighWatermark + 1);
        var body = observation.Body is null ? null : Encoding.UTF8.GetBytes(observation.Body);
        var payload = body is null ? null : RecordingFrames.Compress(body);
        var payloadName = payload is null ? null : $"{cursor:D20}.br";
        var item = new StoredObservation(observation with { Body = null }, cursor, hash, payloadName,
            body?.Length ?? 0, body is null ? null : RecordingFrames.Hash(body), serialized.Length);
        var frame = RecordingFrames.Encode(item);
        var required = (payload?.Length ?? 0L) + frame.Length + 65536L;
        if (!HasSpace(required)) return new(false, ErrorCode: "disk_budget_exhausted");
        if (payload is not null) {
            PrivateArchiveFiles.AtomicWrite(Path.Combine(PayloadDirectory, payloadName!), payload);
        }
        // Recheck after compression/fsync and before source publication. Any rejected payload is an orphan, never an event.
        if (!policy.IsCurrent(observation.PolicyRevision, observation.StopGeneration)) {
            if (payloadName is not null) File.Delete(Path.Combine(PayloadDirectory, payloadName));
            return new(false, ErrorCode: "policy_stopped_or_changed");
        }
        EnsureSegment(frame.Length, cursor);
        var offset = active!.Position;
        active.Write(frame);
        active.Flush(true);
        var location = new StoredLocation(item, activeName!, offset);
        records.Add(location);
        identities.Add((observation.SourceEpoch, observation.Sequence), location);
        // Journal is authoritative; the cached manifest watermark may lag a crash and is repaired by replay.
        control = control with { HighWatermark = cursor };
        return new(true, new(observation, cursor, control.Generation + "/" + activeName, offset, hash));
    }

    public async Task<RecordingCommitPage> ReadCommittedAsync(long after = 0, int limit = 100, CancellationToken ct = default)
    {
        if (after < 0) throw new ArgumentOutOfRangeException(nameof(after));
        await gate.WaitAsync(ct);
        try {
            if (error is "source_corrupt" or "unsupported_archive_version") return new([], after, control.HighWatermark, false, error);
            if (after != 0 && after <= control.InvalidatedThrough) return new([], 0, control.HighWatermark, false, "cursor_invalidated");
            var pageSize = Math.Clamp(limit, 1, options.ReplayBatchSize);
            var selected = records.Where(r => r.Record.Cursor > after && !IsHidden(r.Record)).Take(pageSize + 1).ToArray();
            var page = selected.Take(pageSize).Select(Materialize).ToArray();
            return new(page, page.LastOrDefault()?.Cursor ?? Math.Max(after, control.InvalidatedThrough), control.HighWatermark, selected.Length > pageSize,
                recoveryUncertain ? "recovered_commit_uncertain" : null);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ArgumentException) { error = "source_corrupt"; return new([], after, control.HighWatermark, false, "source_corrupt"); }
        finally { gate.Release(); }
    }

    public async Task<RecordingStorageHealthDto> HealthAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try { return new(initialized ? DiskBytes() : 0, records.Count(r => !IsHidden(r.Record)), control.HighWatermark,
            records.FirstOrDefault(r => !IsHidden(r.Record))?.Record.Cursor ?? 0,
            control.PurgePending ? "purge_pending" : error is null ? "ready" : "degraded", error, Interlocked.Read(ref dropped)); }
        finally { gate.Release(); }
    }

    private CommittedObservation Materialize(StoredLocation location)
    {
        var item = location.Record;
        var observation = item.Metadata;
        if (item.PayloadFile is not null) {
            if (Path.GetFileName(item.PayloadFile) != item.PayloadFile) throw new InvalidDataException("Invalid recording payload locator.");
            using var file = PrivateArchiveFiles.Open(Path.Combine(PayloadDirectory, item.PayloadFile), FileMode.Open, FileAccess.Read);
            var bytes = RecordingFrames.Decompress(file, options.MaximumRecordBytes);
            if (bytes.Length != item.PayloadBytes || RecordingFrames.Hash(bytes) != item.PayloadHash) throw new InvalidDataException("Recording payload integrity failure.");
            observation = observation with { Body = new UTF8Encoding(false, true).GetString(bytes) };
        }
        return new(observation, item.Cursor, control.Generation + "/" + location.Journal, location.Offset, item.ContentHash);
    }

    private bool IsHidden(StoredObservation item) => (control.HiddenCaptures ?? []).Contains(item.Metadata.CaptureId, StringComparer.Ordinal);
    private string PayloadDirectory => Path.Combine(directory, "payloads");
    private string GenerationDirectory => Path.Combine(directory, control.Generation);
    private string ControlPath => Path.Combine(directory, "control.json");
    private void SaveControl()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(control, RecordingFrames.Json);
        if (bytes.Length > 1024 * 1024) throw new IOException("Recording control state exceeds its bound.");
        PrivateArchiveFiles.AtomicWrite(ControlPath, bytes);
    }

    private void EnsureSegment(int length, long cursor)
    {
        if (active is not null && active.Length + length <= options.SegmentBytes) return;
        active?.Flush(true); active?.Dispose();
        activeName = $"journal-{cursor:D20}.rec";
        active = PrivateArchiveFiles.Open(Path.Combine(GenerationDirectory, activeName), FileMode.CreateNew, FileAccess.ReadWrite);
        PrivateArchiveFiles.SyncDirectory(GenerationDirectory);
    }

    private bool HasSpace(long required)
    {
        if (DiskBytes() + required > options.MaximumDiskBytes) return false;
        var drive = new DriveInfo(Path.GetPathRoot(directory)!);
        // DriveInfo('/') is not correct for every separate mount; locate the longest containing mount on Linux.
        if (OperatingSystem.IsLinux()) {
            var candidate = DriveInfo.GetDrives().Where(d => directory == d.Name.TrimEnd('/') || directory.StartsWith(d.Name.TrimEnd('/') + "/", StringComparison.Ordinal))
                .OrderByDescending(d => d.Name.Length).FirstOrDefault();
            if (candidate is not null) drive = candidate;
        }
        return drive.AvailableFreeSpace - required >= options.MinimumFreeDiskBytes;
    }

    private long DiskBytes()
    {
        long bytes = 0;
        var pending = new Stack<string>(); pending.Push(directory);
        while (pending.TryPop(out var folder)) {
            foreach (var path in System.IO.Directory.EnumerateFileSystemEntries(folder)) {
                PrivateArchiveFiles.RejectLinks(path);
                if (System.IO.Directory.Exists(path)) pending.Push(path);
                else if (File.Exists(path)) bytes = checked(bytes + new FileInfo(path).Length);
            }
        }
        return bytes;
    }

    private static bool ValidMetadata(SanitizedObservation o) =>
        o.SchemaVersion == RecordingContract.Version && o.Sequence > 0 &&
        o.RedactionVersion == RecordingContract.RedactionVersion && (o.RedactionState is "sanitized" or "metadata_only" or "content_omitted" or "no_body" or "omitted_oversize" or "omitted_fragment" or "omitted_unsupported" or "omitted_invalid") && (o.Body is null || o.RedactionState == "sanitized") &&
        new[] { o.SourceEpoch, o.EventId, o.CaptureId, o.RequestId, o.Boundary, o.Protocol, o.Kind }.All(s => !string.IsNullOrWhiteSpace(s) && s.Length <= 1024);

    private static void ValidateOptions(RecordingStoreOptions o)
    {
        if (o.QueueCount <= 0 || o.MaximumRecordBytes < 4096 || o.MaximumRecordBytes > 64 * 1024 * 1024 || o.QueueBytes < 4096 ||
            o.MaximumRecords <= 0 || o.MaximumDiskBytes < 65536 || o.MinimumFreeDiskBytes < 0 || o.SegmentBytes < 4096 || o.ReplayBatchSize <= 0 ||
            o.ContentRetentionDays <= 0 || o.MetadataRetentionDays < o.ContentRetentionDays)
            throw new ArgumentOutOfRangeException(nameof(o));
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true; queue.Writer.TryComplete();
        await writer;
        await gate.WaitAsync();
        try { active?.Flush(true); active?.Dispose(); if (initialized) SaveControl(); writerLock?.Dispose(); }
        finally { gate.Release(); }
    }
}
