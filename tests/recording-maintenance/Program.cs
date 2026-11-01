using Lain.Core;
using Lain.Host.Recording.Collection;

var assertions = 0;
var store = new Store();
var index = new Index();
using var maintenance = new RecordingMaintenance(store, index);
await maintenance.RunOnceAsync();
Check(index.Recoveries == 1 && index.Rebuilds == 0, "startup reconciles possible interrupted purge using incremental recovery");
await maintenance.RunOnceAsync();
Check(index.Recoveries == 1 && index.Rebuilds == 0, "unchanged maintenance tick does not touch index");
store.Change = new(RecordCount: 1, HighWatermark: 3, RetainedFromCursor: 3);
await maintenance.RunOnceAsync();
Check(index.Recoveries == 2, "retention source-stamp change proactively synchronizes external index");
index.Fail = true;
store.Change = new(RecordCount: 0, HighWatermark: 3);
try { await maintenance.RunOnceAsync(); throw new Exception("expected index failure"); }
catch (IOException) { assertions++; }
Check(index.Recoveries == 3, "deletion reaching empty archive still synchronizes existing index");
index.Fail = false;
await maintenance.RunOnceAsync();
Check(index.Recoveries == 4, "index failure remains dirty and retries even when source is unchanged");
await maintenance.RunOnceAsync();
Check(index.Recoveries == 4, "successful retry clears dirty state");
store.FailAfterChange = true;
store.Change = new(RecordCount: 0, HighWatermark: 4);
try { await maintenance.RunOnceAsync(); throw new Exception("expected retention failure"); }
catch (IOException) { assertions++; }
Check(index.Recoveries == 5, "logical source change synchronizes despite later physical purge failure");
store.FailAfterChange = false;
store.Health = new(State: "recovering");
var passes = store.Passes;
await maintenance.RunOnceAsync();
Check(store.Passes == passes && index.Recoveries == 5, "startup recovery is not mistaken for an empty retained archive");
Console.WriteLine($"Recording maintenance: {assertions} assertions passed (pure fixtures).");
void Check(bool condition, string name) { assertions++; if (!condition) throw new Exception(name); }

sealed class Store : IRecordingStore, IRecordingMaintenance
{
    public RecordingStorageHealthDto Health = new(RecordCount: 2, HighWatermark: 2, RetainedFromCursor: 1);
    public RecordingStorageHealthDto? Change;
    public bool FailAfterChange;
    public int Passes;
    public Task ApplyRetentionAsync(CancellationToken ct = default)
    {
        Passes++;
        if (Change is { } changed) { Health = changed; Change = null; }
        if (FailAfterChange) throw new IOException("fixture");
        return Task.CompletedTask;
    }
    public Task<RecordingStorageHealthDto> HealthAsync(CancellationToken ct = default) => Task.FromResult(Health);
    public Task<RecordingCommitResult> CommitAsync(SanitizedObservation observation, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<RecordingCommitPage> ReadCommittedAsync(long after = 0, int limit = 100, CancellationToken ct = default) => throw new NotSupportedException();
    public Task ClearAsync(CancellationToken ct = default) => throw new NotSupportedException();
}
sealed class Index : IRecordingProjection, IRecordingProjectionRecovery
{
    public int Recoveries, Rebuilds;
    public bool Fail;
    public Task RecoverAsync(CancellationToken ct = default)
    { Recoveries++; if (Fail) throw new IOException("fixture"); return Task.CompletedTask; }
    public Task RebuildAsync(CancellationToken ct = default) { Rebuilds++; return Task.CompletedTask; }
    public Task ApplyAsync(CommittedObservation observation, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<RecordingConversationPage> ListAsync(RecordingConversationQuery query, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<RecordingConversationDetail?> GetAsync(string conversationId, string? cursor = null, int limit = 100, CancellationToken ct = default) => throw new NotSupportedException();
}
