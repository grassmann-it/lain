using Lain.Core;
using Lain.Host.Recording.Storage;
using Xunit;

namespace Lain.Recording.Storage.Tests;

public sealed class StorageResourceBoundsTests : IDisposable
{
    private readonly string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".cache", "lain-recording-storage-resource-tests", Guid.NewGuid().ToString("N"));
    private static RecordingStoreOptions Options => new(MaximumDiskBytes: 32L * 1024 * 1024, MinimumFreeDiskBytes: 0);
    private static SanitizedObservation Event(long sequence, string capture = "survivor") => new(
        "epoch", sequence, "event-" + sequence, capture, "request", DateTimeOffset.UtcNow, "client_ingress", "responses", "request",
        1, 0, RecordingContract.RedactionVersion, "sanitized", BodyEncoding: "utf8_json", Body: "{\"text\":\"" + new string('a', 2048) + "\"}");

    [Fact]
    public async Task RepeatedHealthDoesNotWalkArchiveOrHydratePayloads()
    {
        await using var store = new RecordingSourceStore(directory, new Gate(), Options);
        for (var i = 1; i <= 32; i++) Assert.True((await store.CommitAsync(Event(i))).Committed);
        var before = store.Diagnostics;
        for (var i = 0; i < 40; i++) Assert.Equal(32, (await store.HealthAsync()).RecordCount);
        var after = store.Diagnostics;
        Assert.Equal(before.DiskReconciliations, after.DiskReconciliations);
        Assert.Equal(before.DirectoryEntriesScanned, after.DirectoryEntriesScanned);
        Assert.Equal(before.PayloadReads, after.PayloadReads);
    }

    [Fact]
    public async Task CommitAccountsIncrementallyAndReplaySeeksWithoutHydratingEarlierRecords()
    {
        await using var store = new RecordingSourceStore(directory, new Gate(), Options);
        for (var i = 1; i <= 64; i++) await store.CommitAsync(Event(i));
        var before = store.Diagnostics;
        Assert.True((await store.CommitAsync(Event(65))).Committed);
        Assert.Equal(before.DirectoryEntriesScanned, store.Diagnostics.DirectoryEntriesScanned);
        Assert.Equal(before.DiskReconciliations, store.Diagnostics.DiskReconciliations);
        var page = await store.ReadCommittedAsync(63, 1);
        Assert.Equal(64, Assert.Single(page.Records).Cursor);
        var after = store.Diagnostics;
        Assert.Equal(1, after.PayloadReads - before.PayloadReads);
        Assert.InRange(after.ReplaySearchSteps - before.ReplaySearchSteps, 1, 8);
        Assert.Equal(ActualBytes(), (await store.HealthAsync()).DiskBytes);
    }

    [Fact]
    public async Task MaintenanceReconcilesExternalFilesAndPurgeRefreshesVisibleAndDiskCaches()
    {
        await using var store = new RecordingSourceStore(directory, new Gate(), Options);
        for (var i = 1; i <= 12; i++) await store.CommitAsync(Event(i, i % 2 == 0 ? "deleted" : "survivor"));
        await File.WriteAllBytesAsync(Path.Combine(directory, "maintenance-owned.fixture"), new byte[5000]);
        var before = store.Diagnostics;
        await store.ApplyRetentionAsync();
        Assert.True(store.Diagnostics.DiskReconciliations > before.DiskReconciliations);
        Assert.Equal(ActualBytes(), (await store.HealthAsync()).DiskBytes);
        await store.PurgeCaptureAsync("deleted");
        Assert.Equal(6, (await store.HealthAsync()).RecordCount);
        Assert.Equal(ActualBytes(), (await store.HealthAsync()).DiskBytes);
        var first = await store.ReadCommittedAsync(0, 2);
        var next = await store.ReadCommittedAsync(first.NextCursor, 2);
        Assert.Equal(2, next.Records.Length);
        Assert.All(next.Records, x => Assert.Equal("survivor", x.Observation.CaptureId));
        await store.ClearAsync();
        Assert.Equal(0, (await store.HealthAsync()).RecordCount);
        Assert.Equal(ActualBytes(), (await store.HealthAsync()).DiskBytes);
    }

    private long ActualBytes() => System.IO.Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length);
    public void Dispose() { if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true); }
    private sealed class Gate : IRecordingPolicy
    {
        public CapturePolicySnapshot Snapshot => new(1, true, RecordingContract.Content);
        public bool IsCurrent(long revision, long stopGeneration) => revision == 1 && stopGeneration == 0;
        public RecordingPolicyResult Update(RecordingPolicyUpdate update) => throw new NotSupportedException();
        public SanitizedObservation? Sanitize(SourceObservation observation, CapturePolicySnapshot policy) => throw new NotSupportedException();
    }
}
