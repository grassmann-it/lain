using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Recording.Storage;
using Xunit;

namespace Lain.Recording.Storage.Tests;

public sealed class StorageDeletionBudgetTests : IDisposable
{
    private readonly string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".cache", "lain-recording-deletion-budget-tests", Guid.NewGuid().ToString("N"));
    private static RecordingStoreOptions Options => new(MaximumDiskBytes: 64L * 1024 * 1024, MinimumFreeDiskBytes: 0);
    private static SanitizedObservation Event(long sequence, string capture, string? body = "{}") => new(
        "epoch", sequence, "event-" + sequence, capture, "request", DateTimeOffset.UtcNow, "client_ingress", "responses", "request",
        1, 0, RecordingContract.RedactionVersion, body is null ? "metadata_only" : "sanitized", BodyEncoding: body is null ? null : "utf8_json", Body: body);
    private string ControlPath => Path.Combine(directory, "control.json");

    [Fact]
    public async Task CompactionReservesEncodedMetadataRatherThanUncompressedRetainedPayload()
    {
        var body = "{\"text\":\"" + new string('a', 2 * 1024 * 1024) + "\"}";
        await using (var store = new RecordingSourceStore(directory, new Gate(), Options with { MaximumDiskBytes = 131072 })) {
            Assert.True((await store.CommitAsync(Event(1, "deleted"))).Committed);
            Assert.True((await store.CommitAsync(Event(2, "survivor", body))).Committed);
            await store.PurgeCaptureAsync("deleted");
            Assert.Equal("ready", (await store.HealthAsync()).State);
            Assert.Equal(body, Assert.Single((await store.ReadCommittedAsync()).Records).Observation.Body);
        }
        await using var reopened = new RecordingSourceStore(directory, new Gate(), Options with { MaximumDiskBytes = 131072 });
        Assert.Equal(body, Assert.Single((await reopened.ReadCommittedAsync()).Records).Observation.Body);
        Assert.Equal("capture_deleted", (await reopened.CommitAsync(Event(3, "deleted"))).ErrorCode);
    }

    [Fact]
    public async Task LegacyLongIdTombstonesMigrateAndRetentionCrossesTheOldOneMiBLimit()
    {
        static string Legacy(int i) => "prior-" + i.ToString("D6") + "-" + new string('x', 243);
        static string Expired(int i) => "expired-" + i.ToString("D4") + "-" + new string('y', 243);
        await using (var seed = new RecordingSourceStore(directory, new Gate(), Options)) {
            for (var i = 1; i <= 80; i++) Assert.True((await seed.CommitAsync(Event(i, Expired(i)) with { ObservedAt = DateTimeOffset.UtcNow.AddDays(-8) })).Committed);
        }
        var control = JsonSerializer.Deserialize<ArchiveControl>(await File.ReadAllBytesAsync(ControlPath), RecordingFrames.Json)!;
        var legacy = control with { Version = 1, HiddenCaptures = Enumerable.Range(1, 4010).Select(Legacy).ToArray(), HiddenCaptureHashes = null };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(legacy, RecordingFrames.Json);
        Assert.InRange(bytes.Length, 1_000_000, 1024 * 1024);
        await File.WriteAllBytesAsync(ControlPath, bytes);
        await using (var store = new RecordingSourceStore(directory, new Gate(), Options)) {
            Assert.Equal(80, (await store.ReadCommittedAsync()).Records.Length);
            await store.ApplyRetentionAsync();
            Assert.Equal("ready", (await store.HealthAsync()).State);
            Assert.Empty((await store.ReadCommittedAsync()).Records);
            Assert.Equal("capture_deleted", (await store.CommitAsync(Event(81, Legacy(1)))).ErrorCode);
            Assert.Equal("capture_deleted", (await store.CommitAsync(Event(82, Expired(1)))).ErrorCode);
            var migrated = JsonSerializer.Deserialize<ArchiveControl>(await File.ReadAllBytesAsync(ControlPath), RecordingFrames.Json)!;
            Assert.Equal(2, migrated.Version);
            Assert.Null(migrated.HiddenCaptures);
            Assert.Equal(4090, migrated.HiddenCaptureHashes!.Length);
            Assert.True(new FileInfo(ControlPath).Length < 512 * 1024);
        }
        await using var reopened = new RecordingSourceStore(directory, new Gate(), Options);
        Assert.Empty((await reopened.ReadCommittedAsync()).Records);
        Assert.Equal("capture_deleted", (await reopened.CommitAsync(Event(83, Legacy(1)))).ErrorCode);
    }

    [Fact]
    public async Task HundredThousandTombstonesFitAndCapacityRejectionDoesNotEnterPendingPurge()
    {
        await using (var seed = new RecordingSourceStore(directory, new Gate(), Options)) await seed.CommitAsync(Event(1, "retained"));
        var control = JsonSerializer.Deserialize<ArchiveControl>(await File.ReadAllBytesAsync(ControlPath), RecordingFrames.Json)!;
        var hashes = Enumerable.Range(0, 100000).Select(i => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("deleted-" + i)))).ToArray();
        var full = control with { Version = 2, HiddenCaptures = null, HiddenCaptureHashes = hashes };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(full, RecordingFrames.Json);
        Assert.True(bytes.Length < 8 * 1024 * 1024);
        await File.WriteAllBytesAsync(ControlPath, bytes);
        await using var store = new RecordingSourceStore(directory, new Gate(), Options);
        Assert.Single((await store.ReadCommittedAsync()).Records);
        var durableBefore = await File.ReadAllBytesAsync(ControlPath);
        var failure = await Assert.ThrowsAnyAsync<IOException>(() => store.PurgeCaptureAsync("one-more"));
        Assert.Equal("recording_deletion_capacity", failure.Message);
        Assert.Equal("ready", (await store.HealthAsync()).State);
        Assert.Equal(durableBefore, await File.ReadAllBytesAsync(ControlPath));
        Assert.Equal("deletion_capacity", (await store.CommitAsync(Event(2, "new-capture"))).ErrorCode);
        Assert.Equal("capture_deleted", (await store.CommitAsync(Event(3, "deleted-1"))).ErrorCode);
        await store.ClearAsync();
        Assert.True((await store.CommitAsync(Event(4, "new-capture"))).Committed);
    }

    public void Dispose() { if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true); }
    private sealed class Gate : IRecordingPolicy
    {
        public CapturePolicySnapshot Snapshot => new(1, true, RecordingContract.Content);
        public bool IsCurrent(long revision, long stopGeneration) => revision == 1 && stopGeneration == 0;
        public RecordingPolicyResult Update(RecordingPolicyUpdate update) => throw new NotSupportedException();
        public SanitizedObservation? Sanitize(SourceObservation observation, CapturePolicySnapshot policy) => throw new NotSupportedException();
    }
}
