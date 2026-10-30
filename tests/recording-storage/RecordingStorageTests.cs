using System.Text;
using Lain.Core;
using Lain.Host.Recording.Storage;
using Xunit;

namespace Lain.Recording.Storage.Tests;

public sealed class RecordingStorageTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "lain-recording-storage-tests", Guid.NewGuid().ToString("N"));
    private readonly Gate policy = new();
    private RecordingStoreOptions Options => new(MaximumDiskBytes: 32 * 1024 * 1024, MinimumFreeDiskBytes: 0);
    private string PathFor(string name = "archive") => Path.Combine(root, name);
    private SanitizedObservation Event(long sequence = 1, string capture = "capture", string? body = "{\"message\":\"hello\"}", string epoch = "epoch") =>
        new(epoch, sequence, "event-" + sequence, capture, "request", DateTimeOffset.UtcNow, "client_ingress", "responses", "request", 1, 0,
            RecordingContract.RedactionVersion, body is null ? "metadata_only" : "sanitized", BodyEncoding: body is null ? null : "utf8_json", Body: body);

    [Fact]
    public async Task DisabledConstructionHealthAndClearCreateNoArchiveFiles()
    {
        policy.Enabled = false;
        await using (var store = new RecordingSourceStore(PathFor(), policy, Options)) {
            Assert.Equal(0, (await store.HealthAsync()).RecordCount);
            await store.ClearAsync();
            Assert.Empty((await store.ReadCommittedAsync()).Records);
            Assert.False(System.IO.Directory.Exists(PathFor()));
        }
        Assert.False(System.IO.Directory.Exists(PathFor()));
    }

    [Fact]
    public async Task DurableRestartPreservesOccurrencesAndRejectsConflictingIdentity()
    {
        await using (var store = new RecordingSourceStore(PathFor(), policy, Options)) {
            Assert.True((await store.CommitAsync(Event())).Committed);
            Assert.True((await store.CommitAsync(Event(2))).Committed);
            var duplicate = await store.CommitAsync(Event() with { Body = "{}" });
            Assert.False(duplicate.Committed);
        }
        await using (var store = new RecordingSourceStore(PathFor(), policy, Options)) {
            var page = await store.ReadCommittedAsync();
            Assert.Equal(2, page.Records.Length);
            Assert.Equal(new long[] { 1, 2 }, page.Records.Select(r => r.Cursor));
            Assert.All(page.Records, r => Assert.Equal("{\"message\":\"hello\"}", r.Observation.Body));
            Assert.Equal("recovered_commit_uncertain", page.GapReason);
            Assert.True((await store.CommitAsync(Event(3))).Committed);
        }
    }

    [Fact]
    public async Task ReplayIdentityDeduplicatesExactBytesOnly()
    {
        await using var store = new RecordingSourceStore(PathFor(), policy, Options);
        var observation = Event();
        var first = await store.CommitAsync(observation);
        var replay = await store.CommitAsync(observation);
        Assert.True(replay.Committed);
        Assert.Equal(first.Record!.Cursor, replay.Record!.Cursor);
        Assert.Equal("source_identity_conflict", (await store.CommitAsync(observation with { Body = "{}" })).ErrorCode);
        Assert.Single((await store.ReadCommittedAsync()).Records);
    }

    [Fact]
    public async Task TornActiveTailIsQuarantinedButChecksumDamageIsNotTruncated()
    {
        await using (var store = new RecordingSourceStore(PathFor(), policy, Options)) Assert.True((await store.CommitAsync(Event())).Committed);
        var journal = System.IO.Directory.GetFiles(PathFor(), "*.rec", SearchOption.AllDirectories).Single();
        await using (var append = new FileStream(journal, FileMode.Append)) await append.WriteAsync(new byte[] { 1, 2, 3 });
        await using (var store = new RecordingSourceStore(PathFor(), policy, Options)) {
            Assert.Single((await store.ReadCommittedAsync()).Records);
            Assert.Single(System.IO.Directory.GetFiles(Path.Combine(PathFor(), "recovery")));
        }
        var bytes = await File.ReadAllBytesAsync(journal); bytes[^1] ^= 0x11; await File.WriteAllBytesAsync(journal, bytes);
        await using (var store = new RecordingSourceStore(PathFor(), policy, Options)) {
            Assert.Equal("source_corrupt", (await store.HealthAsync()).ErrorCode);
            Assert.Empty((await store.ReadCommittedAsync()).Records);
            Assert.Equal(bytes.Length, new FileInfo(journal).Length);
            await store.ClearAsync();
            Assert.Equal("ready", (await store.HealthAsync()).State);
        }
    }

    [Fact]
    public async Task MissingPayloadCannotBecomeCompleteObservation()
    {
        await using (var store = new RecordingSourceStore(PathFor(), policy, Options)) await store.CommitAsync(Event());
        File.Delete(System.IO.Directory.GetFiles(Path.Combine(PathFor(), "payloads")).Single());
        await using var reopened = new RecordingSourceStore(PathFor(), policy, Options);
        Assert.Equal("source_corrupt", (await reopened.ReadCommittedAsync()).GapReason);
        Assert.False((await reopened.CommitAsync(Event(2))).Committed);
    }

    [Fact]
    public async Task CaptureDeletionPreservesOtherRepeatedOccurrenceAndInvalidatesCursor()
    {
        await using (var store = new RecordingSourceStore(PathFor(), policy, Options)) {
            await store.CommitAsync(Event(capture: "deleted"));
            await store.CommitAsync(Event(2, "survivor"));
            await store.PurgeCaptureAsync("deleted");
            Assert.Equal("cursor_invalidated", (await store.ReadCommittedAsync(1)).GapReason);
            var survivor = Assert.Single((await store.ReadCommittedAsync()).Records);
            Assert.Equal("survivor", survivor.Observation.CaptureId);
            Assert.Equal("capture_deleted", (await store.CommitAsync(Event(3, "deleted"))).ErrorCode);
            Assert.Single(System.IO.Directory.GetFiles(Path.Combine(PathFor(), "payloads")));
        }
        await using var reopened = new RecordingSourceStore(PathFor(), policy, Options);
        Assert.Equal("survivor", Assert.Single((await reopened.ReadCommittedAsync()).Records).Observation.CaptureId);
    }

    [Fact]
    public async Task NewGenerationPagedReplayCanPassPreviousDeletionWatermark()
    {
        await using var store = new RecordingSourceStore(PathFor(), policy, Options with { ReplayBatchSize = 1 });
        await store.CommitAsync(Event(1, "survivor-a"));
        await store.CommitAsync(Event(2, "deleted"));
        await store.CommitAsync(Event(3, "survivor-b"));
        await store.PurgeCaptureAsync("deleted");
        var first = await store.ReadCommittedAsync();
        Assert.True(first.HasMore);
        var second = await store.ReadCommittedAsync(first.NextCursor);
        Assert.Single(second.Records);
        Assert.False(second.HasMore);
        Assert.Null(second.GapReason);
    }

    [Fact]
    public async Task DiskBudgetIncludesManagedProjectionAndStopsBeforeAppend()
    {
        await using var store = new RecordingSourceStore(PathFor(), policy, Options with { MaximumDiskBytes = 131072 });
        await store.CommitAsync(Event(1, body: null));
        await File.WriteAllBytesAsync(Path.Combine(PathFor(), "projection.db"), new byte[100000]);
        var result = await store.CommitAsync(Event(2));
        Assert.Equal("disk_budget_exhausted", result.ErrorCode);
        Assert.Single((await store.ReadCommittedAsync()).Records);
    }

    [Fact]
    public async Task ClearPurgesSourceAndProjectionWhileCursorRemainsMonotonic()
    {
        await using var store = new RecordingSourceStore(PathFor(), policy, Options);
        await store.CommitAsync(Event());
        await File.WriteAllTextAsync(Path.Combine(PathFor(), "projection.db"), "secret derived text");
        await store.ClearAsync();
        Assert.Empty((await store.ReadCommittedAsync()).Records);
        Assert.False(File.Exists(Path.Combine(PathFor(), "projection.db")));
        var next = await store.CommitAsync(Event(2));
        Assert.Equal(2L, next.Record!.Cursor);
    }

    [Fact]
    public async Task RetentionUsesWholeCaptureAndIsResumableOnRestart()
    {
        await using var store = new RecordingSourceStore(PathFor(), policy, Options);
        await store.CommitAsync(Event() with { ObservedAt = DateTimeOffset.UtcNow.AddDays(-8) });
        await store.CommitAsync(Event(2, "fresh"));
        await store.ApplyRetentionAsync();
        Assert.Equal("fresh", Assert.Single((await store.ReadCommittedAsync()).Records).Observation.CaptureId);
    }

    [Fact]
    public async Task PolicyGateQueueAndDiskBoundsRejectWithoutInferenceException()
    {
        await using var store = new RecordingSourceStore(PathFor(), policy, Options with { QueueBytes = 4096, MaximumDiskBytes = 131072 });
        Assert.Equal("queue_full", (await store.CommitAsync(Event(body: new string('x', 4096)))).ErrorCode);
        policy.Enabled = false;
        Assert.Equal("policy_stopped_or_changed", (await store.CommitAsync(Event(body: null))).ErrorCode);
        policy.Enabled = true;
        Assert.False((await store.CommitAsync(Event(body: new string('x', 17 * 1024 * 1024)))).Committed);
    }

    [Fact]
    public async Task PrivateModesExclusiveWriterAndSymlinkRefusal()
    {
        if (!OperatingSystem.IsLinux()) return;
        await using var store = new RecordingSourceStore(PathFor(), policy, Options);
        await store.CommitAsync(Event());
        await using (var other = new RecordingSourceStore(PathFor(), policy, Options))
            Assert.Equal("archive_unavailable", (await other.HealthAsync()).ErrorCode);
        foreach (var dir in System.IO.Directory.GetDirectories(PathFor(), "*", SearchOption.AllDirectories).Prepend(PathFor()))
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(dir));
        foreach (var file in System.IO.Directory.GetFiles(PathFor(), "*", SearchOption.AllDirectories))
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        var link = PathFor("link"); System.IO.Directory.CreateSymbolicLink(link, PathFor());
        await using var linked = new RecordingSourceStore(link, policy, Options);
        Assert.Equal("archive_unavailable", (await linked.HealthAsync()).ErrorCode);
    }

    [Fact]
    public async Task SerializedSourceAndPayloadAreCompressedBeforePrivateWrite()
    {
        var marker = string.Concat(Enumerable.Repeat("ordinary retained content, not an encryption test; ", 200));
        var body = "{\"message\":\"" + marker + "\"}";
        await using var store = new RecordingSourceStore(PathFor(), policy, Options);
        var result = await store.CommitAsync(Event(body: body));
        Assert.True(result.Committed);
        var payload = Assert.Single(System.IO.Directory.GetFiles(PathFor(), "*.br", SearchOption.AllDirectories));
        Assert.True(new FileInfo(payload).Length < Encoding.UTF8.GetByteCount(body));
        using var input = File.OpenRead(payload);
        Assert.Equal(body, Encoding.UTF8.GetString(RecordingFrames.Decompress(input, 1024 * 1024)));
        Assert.Equal(body, (await store.ReadCommittedAsync()).Records.Single().Observation.Body);

    }

    public void Dispose() { if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true); }
    private sealed class Gate : IRecordingPolicy
    {
        public bool Enabled = true;
        public CapturePolicySnapshot Snapshot => new(1, Enabled, RecordingContract.Content);
        public bool IsCurrent(long revision, long stopGeneration) => Enabled && revision == 1 && stopGeneration == 0;
        public RecordingPolicyResult Update(RecordingPolicyUpdate update) => throw new NotSupportedException();
        public SanitizedObservation? Sanitize(SourceObservation observation, CapturePolicySnapshot snapshot) => throw new NotSupportedException();
    }
}
