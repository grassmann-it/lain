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
    private readonly Task recovery;
    private readonly CancellationTokenSource recoveryStop = new();
    private readonly List<StoredLocation> records = [];
    private readonly Dictionary<(string Epoch, long Sequence), StoredLocation> identities = [];
    private ArchiveControl control = null!;
    private long queuedBytes, dropped, storeEpoch;
    private string? error;
    private bool disposed;
    private FileStream? active;
    private string? activeName;

    public RecordingSourceStore(string directory, IRecordingPolicy policy, RecordingStoreOptions? options = null, TimeProvider? clock = null)
        : this(directory, policy, options, clock, null) { }

    // A deterministic qualification seam; production always uses the public constructor.
    internal RecordingSourceStore(string directory, IRecordingPolicy policy, RecordingStoreOptions? options,
        TimeProvider? clock, Func<CancellationToken, Task>? beforeRecovery)
    {
        this.directory = Path.GetFullPath(directory);
        this.policy = policy;
        this.options = options ?? new();
        this.clock = clock ?? TimeProvider.System;
        ValidateOptions(this.options);
        control = new(1, Guid.NewGuid().ToString("N"));
        var hasArchive = false;
        // A disabled/new installation is inert: no journal, payload or lock creation on construction/health.
        if (System.IO.Directory.Exists(this.directory)) {
            try {
                PrivateArchiveFiles.RejectLinks(this.directory);
                hasArchive = System.IO.Directory.EnumerateFileSystemEntries(this.directory).Any();
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
        // The fresh gate is acquired without blocking before either task can run. All source
        // access therefore follows recovery, while Health can return immediately without it.
        recovery = Task.CompletedTask;
        if (hasArchive) {
            if (!gate.Wait(0)) throw new InvalidOperationException("Recording recovery gate is unavailable.");
            recovery = Task.Run(async () => {
                try {
                    if (beforeRecovery is not null) await beforeRecovery(recoveryStop.Token);
                    recoveryStop.Token.ThrowIfCancellationRequested();
                    Initialize(recoveryStop.Token);
                }
                catch (OperationCanceledException) when (recoveryStop.IsCancellationRequested) { error = "recovery_cancelled"; CloseRecoveryFiles(); }
                catch (Exception) { error = "archive_unavailable"; CloseRecoveryFiles(); }
                finally { gate.Release(); }
            });
        }
        writer = Task.Run(WriteLoopAsync);
    }

    private void CloseRecoveryFiles()
    {
        try { active?.Dispose(); } catch (Exception) { /* Recovery owns and observes cleanup failures. */ }
        finally { active = null; }
        try { writerLock?.Dispose(); } catch (Exception) { /* Fixed recovery health code already records failure. */ }
        finally { writerLock = null; }
    }

    private void Initialize(CancellationToken ct = default)
    {
        PrivateArchiveFiles.Directory(directory);
        writerLock = PrivateArchiveFiles.Open(Path.Combine(directory, "writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try { ReconcileDiskUsage(ct); Recover(ct); initialized = true; }
        catch { active?.Dispose(); writerLock.Dispose(); writerLock = null; throw; }
    }

    public Task<RecordingCommitResult> CommitAsync(SanitizedObservation observation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ct.ThrowIfCancellationRequested();
        if (disposed) return Task.FromResult(new RecordingCommitResult(false, ErrorCode: "store_closed"));
        // UTF8 counting is bounded by the already sanitized input boundary. Do not serialize raw content here.
        var bodyBytes = Encoding.UTF8.GetByteCount(observation.Body ?? "");
        var bytes = (long)bodyBytes + 4096;
        if (bodyBytes > MaximumAcceptedRecordBytes || !ValidMetadata(observation))
            return Task.FromResult(new RecordingCommitResult(false, ErrorCode: "invalid_or_oversize_record"));
        if (Interlocked.Add(ref queuedBytes, bytes) > MaximumQueuedBytes) {
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
                var result = disposed && !initialized ? new RecordingCommitResult(false, ErrorCode: "store_closed") : pending.Epoch == Interlocked.Read(ref storeEpoch) ? Commit(pending.Observation) : new RecordingCommitResult(false, ErrorCode: "store_cleared");
                if (!result.Committed) Interlocked.Increment(ref dropped);
                pending.Completion.TrySetResult(result);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException or PlatformNotSupportedException) {
                error = "archive_write_failed"; ReconcileAfterFailure();
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
        if (hiddenCaptures.Contains(observation.CaptureId)) return new(false, ErrorCode: "capture_deleted");
        var serialized = JsonSerializer.SerializeToUtf8Bytes(observation, RecordingFrames.Json);
        if (serialized.Length > MaximumAcceptedRecordBytes) return new(false, ErrorCode: "record_too_large");
        var hash = RecordingFrames.Hash(serialized);
        if (identities.TryGetValue((observation.SourceEpoch, observation.Sequence), out var duplicate)) {
            if (duplicate.Record.ContentHash != hash) return new(false, ErrorCode: "source_identity_conflict");
            return new(true, Materialize(duplicate));
        }
        if (records.Count >= options.MaximumRecords) return new(false, ErrorCode: "record_limit");
        if (!initialized) Initialize();
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
            WriteTrackedAtomic(Path.Combine(PayloadDirectory, payloadName!), payload);
        }
        // Recheck after compression/fsync and before source publication. Any rejected payload is an orphan, never an event.
        if (!policy.IsCurrent(observation.PolicyRevision, observation.StopGeneration)) {
            if (payloadName is not null) DeleteTrackedFile(Path.Combine(PayloadDirectory, payloadName));
            return new(false, ErrorCode: "policy_stopped_or_changed");
        }
        EnsureSegment(frame.Length, cursor);
        var offset = active!.Position;
        TrackFileLength(Path.Combine(GenerationDirectory, activeName!), active.Length + frame.Length);
        active.Write(frame);
        active.Flush(true);
        var location = new StoredLocation(item, activeName!, offset);
        records.Add(location); visibleRecords.Add(location);
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
            if (error is not null && error != "purge_pending") return new([], after, control.HighWatermark, false, error);
            if (after != 0 && after <= control.InvalidatedThrough) return new([], 0, control.HighWatermark, false, "cursor_invalidated");
            var pageSize = Math.Clamp(limit, 1, options.ReplayBatchSize);
            var start = FirstVisibleAfter(after);
            var pageItems = new List<CommittedObservation>(Math.Min(pageSize, visibleRecords.Count - start));
            long retainedBytes = 0;
            var byteLimit = Math.Min(64L * 1024 * 1024, Math.Max(options.MaximumRecordBytes, MaximumQueuedBytes));
            var index = start;
            for (; index < visibleRecords.Count && pageItems.Count < pageSize; index++) {
                var item = visibleRecords[index];
                // Check the page byte budget before opening/decompressing the next payload.
                var nextBytes = item.Record.OriginalBytes;
                if (pageItems.Count > 0 && retainedBytes + nextBytes > byteLimit) break;
                pageItems.Add(Materialize(item, ct)); retainedBytes += nextBytes;
            }
            var page = pageItems.ToArray();
            return new(page, page.LastOrDefault()?.Cursor ?? Math.Max(after, control.InvalidatedThrough), control.HighWatermark, index < visibleRecords.Count,
                recoveryUncertain ? "recovered_commit_uncertain" : null);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ArgumentException) { error = "source_corrupt"; return new([], after, control.HighWatermark, false, "source_corrupt"); }
        finally { gate.Release(); }
    }

    public async Task<RecordingStorageHealthDto> HealthAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!recovery.IsCompleted) return new(State: "recovering");
        await gate.WaitAsync(ct);
        try { if (initialized) SampleProjectionFiles(); return new(initialized ? DiskBytes() : 0, visibleRecords.Count, control.HighWatermark,
            visibleRecords.Count > 0 ? visibleRecords[0].Record.Cursor : 0,
            control.PurgePending ? "purge_pending" : error is null ? "ready" : "degraded", error, Interlocked.Read(ref dropped)); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) {
            error = "archive_unavailable";
            return new(RecordCount: visibleRecords.Count, HighWatermark: control.HighWatermark, State: "degraded", ErrorCode: error, DroppedRecords: Interlocked.Read(ref dropped));
        }
        finally { gate.Release(); }
    }

    private CommittedObservation Materialize(StoredLocation location, CancellationToken ct = default)
    {
        var item = location.Record;
        var observation = item.Metadata;
        if (item.PayloadFile is not null) {
            if (Path.GetFileName(item.PayloadFile) != item.PayloadFile) throw new InvalidDataException("Invalid recording payload locator.");
            Interlocked.Increment(ref payloadReads);
            using var file = PrivateArchiveFiles.Open(Path.Combine(PayloadDirectory, item.PayloadFile), FileMode.Open, FileAccess.Read);
            var bytes = RecordingFrames.Decompress(file, options.MaximumRecordBytes, ct);
            if (bytes.Length != item.PayloadBytes || RecordingFrames.Hash(bytes) != item.PayloadHash) throw new InvalidDataException("Recording payload integrity failure.");
            observation = observation with { Body = new UTF8Encoding(false, true).GetString(bytes) };
        }
        return new(observation, item.Cursor, control.Generation + "/" + location.Journal, location.Offset, item.ContentHash);
    }

    private bool IsHidden(StoredObservation item) => hiddenCaptures.Contains(item.Metadata.CaptureId);
    private string PayloadDirectory => Path.Combine(directory, "payloads");
    private string GenerationDirectory => Path.Combine(directory, control.Generation);
    private string ControlPath => Path.Combine(directory, "control.json");
    private void SaveControl()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(control, RecordingFrames.Json);
        if (bytes.Length > 1024 * 1024) throw new IOException("Recording control state exceeds its bound.");
        WriteTrackedAtomic(ControlPath, bytes);
    }

    private void EnsureSegment(int length, long cursor)
    {
        if (active is not null && active.Length + length <= options.SegmentBytes) return;
        active?.Flush(true); active?.Dispose();
        activeName = $"journal-{cursor:D20}.rec";
        active = PrivateArchiveFiles.Open(Path.Combine(GenerationDirectory, activeName), FileMode.CreateNew, FileAccess.ReadWrite);
        TrackFileLength(Path.Combine(GenerationDirectory, activeName), 0);
        PrivateArchiveFiles.SyncDirectory(GenerationDirectory);
    }

    private bool HasSpace(long required)
    {
        if (!diskAccountingValid) return false; // Reconcile only at a recovery/maintenance/error boundary.
        SampleProjectionFiles();
        var maximum = SourceDiskBudget;
        var existing = DiskBytes();
        if (required < 0 || existing > maximum || required > maximum - existing) return false;
        archiveDrive ??= FindArchiveDrive();
        return archiveDrive.AvailableFreeSpace - required >= options.MinimumFreeDiskBytes;
    }

    private static bool ValidMetadata(SanitizedObservation o) =>
        o.SchemaVersion == RecordingContract.Version && o.Sequence > 0 &&
        o.RedactionVersion == RecordingContract.RedactionVersion && (o.RedactionState is "sanitized" or "metadata_only" or "content_omitted" or "no_body" or "omitted_oversize" or "omitted_fragment" or "omitted_unsupported" or "omitted_invalid") && (o.Body is null || o.RedactionState == "sanitized") &&
        new[] { o.SourceEpoch, o.EventId, o.CaptureId, o.RequestId, o.Boundary, o.Protocol, o.Kind }.All(s => !string.IsNullOrWhiteSpace(s) && s.Length <= 1024);

    private static void ValidateOptions(RecordingStoreOptions o)
    {
        if (o.QueueCount <= 0 || o.MaximumRecordBytes < 4096 || o.MaximumRecordBytes > 64 * 1024 * 1024 || o.QueueBytes < 4096 ||
            o.MaximumRecords <= 0 || o.MaximumDiskBytes < 65536 || o.MinimumFreeDiskBytes < 0 || o.SegmentBytes < 4096 || o.ReplayBatchSize <= 0 ||
            o.ContentRetentionDays <= 0 || o.MetadataRetentionDays < o.ContentRetentionDays || o.ExternalReservedBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(o));
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true; queue.Writer.TryComplete(); recoveryStop.Cancel();
        // Cooperative checks cover each frame, expansion chunk and filesystem entry.
        // A kernel filesystem operation itself cannot safely be force-aborted; cleanup
        // remains owned/observed if that operation exceeds the shutdown grace period.
        var cleanup = FinishDisposalAsync();
        try { await cleanup.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (TimeoutException) { /* FinishDisposalAsync catches its own failures and retains ownership. */ }
    }

    private async Task FinishDisposalAsync()
    {
        var entered = false;
        try {
            await recovery;
            await writer;
            await gate.WaitAsync(); entered = true;
            active?.Flush(true);
            if (initialized) SaveControl();
        }
        catch (Exception) { error = "archive_write_failed"; }
        finally {
            try { active?.Dispose(); writerLock?.Dispose(); recoveryStop.Dispose(); }
            catch (Exception) { error = "archive_write_failed"; }
            if (entered) gate.Release();
        }
    }
}
