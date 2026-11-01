using Lain.Core;
using Lain.Host.Recording.Storage;
using Xunit;

namespace Lain.Recording.Storage.Tests;

public sealed class StorageStartupRecoveryTests : IDisposable
{
    private readonly string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".cache", "lain-recording-startup-tests", Guid.NewGuid().ToString("N"));
    private static RecordingStoreOptions Options => new(MaximumDiskBytes: 32L * 1024 * 1024, MinimumFreeDiskBytes: 0);
    private static SanitizedObservation Event(long sequence) => new(
        "epoch", sequence, "event-" + sequence, "capture", "request", DateTimeOffset.UtcNow, "client_ingress", "responses", "request",
        1, 0, RecordingContract.RedactionVersion, "sanitized", BodyEncoding: "utf8_json", Body: "{\"message\":\"persisted before restart\"}");

    private async Task SeedAsync()
    {
        await using var store = new RecordingSourceStore(directory, new Gate(), Options);
        Assert.True((await store.CommitAsync(Event(1))).Committed);
    }

    [Fact]
    public async Task ExistingArchiveStartupAndHealthReturnBeforeRecoveryWhileReadersAndCommitsWait()
    {
        await SeedAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var construct = Task.Run(() => new RecordingSourceStore(directory, new Gate(), Options, null, ct => release.Task.WaitAsync(ct)));
        await using var store = await construct.WaitAsync(TimeSpan.FromSeconds(2));
        var health = await store.HealthAsync().WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal("recovering", health.State);
        var read = store.ReadCommittedAsync();
        var commit = store.CommitAsync(Event(2));
        Assert.False(read.IsCompleted);
        Assert.False(commit.IsCompleted);
        using var cancelled = new CancellationTokenSource();
        var cancelledRead = store.ReadCommittedAsync(ct: cancelled.Token);
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledRead);
        release.SetResult();
        Assert.Contains((await read.WaitAsync(TimeSpan.FromSeconds(5))).Records, r => r.Observation.Sequence == 1 && r.Observation.Body!.Contains("persisted before restart", StringComparison.Ordinal));
        Assert.True((await commit.WaitAsync(TimeSpan.FromSeconds(5))).Committed);
        Assert.Equal("ready", (await store.HealthAsync()).State);
    }

    [Fact]
    public async Task BackgroundRecoveryStillValidatesCompleteFrameChecksumAndExposesFixedCorruptionState()
    {
        await SeedAsync();
        var journal = System.IO.Directory.GetFiles(directory, "*.rec", SearchOption.AllDirectories).Single();
        var bytes = await File.ReadAllBytesAsync(journal); bytes[^1] ^= 0x71; await File.WriteAllBytesAsync(journal, bytes);
        await using var store = new RecordingSourceStore(directory, new Gate(), Options);
        var page = await store.ReadCommittedAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("source_corrupt", page.GapReason);
        Assert.Empty(page.Records);
        Assert.Equal("source_corrupt", (await store.HealthAsync()).ErrorCode);
        Assert.Equal(bytes.Length, new FileInfo(journal).Length);
    }

    [Fact]
    public async Task DisposalCancelsBlockedRecoveryAndDoesNotLeaveAnUnobservedRecoveryTask()
    {
        await SeedAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new RecordingSourceStore(directory, new Gate(), Options, null, async ct => {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var read = store.ReadCommittedAsync();
        await store.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("recovery_cancelled", (await read.WaitAsync(TimeSpan.FromSeconds(2))).GapReason);
        await using var reopened = new RecordingSourceStore(directory, new Gate(), Options);
        Assert.Single((await reopened.ReadCommittedAsync().WaitAsync(TimeSpan.FromSeconds(5))).Records);
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
