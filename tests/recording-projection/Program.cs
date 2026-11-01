using System.Text.Json;
using Lain.Core;
using Lain.Host.Recording.Normalization;
using Lain.Host.Recording.Projection;
using Lain.Host.Recording.Storage;

var checks = 0;
void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks++; Console.WriteLine("PASS " + name); }
var store = new MemoryStore();
CommittedObservation Record(long seq, string conversation, string model, string? worker = null,
    string? parentWorker = null, string? parentRequest = null, string? body = null, string kind = "body", string? gap = null) =>
    new(new("epoch", seq, "event-" + seq, "capture", "request-" + seq, DateTimeOffset.UnixEpoch.AddSeconds(seq), "client_ingress", "responses", kind,
        1, 0, RecordingContract.RedactionVersion, "sanitized", AttemptId: "attempt-" + seq, Model: model,
        Dimensions: new(ConversationId: conversation, WorkerId: worker, ParentWorkerId: parentWorker, ParentRequestId: parentRequest,
            Evidence: [new("conversationId", "harness_header"), new("workerId", "harness_header"), new("parentWorkerId", "harness_header"), new("parentRequestId", "harness_header")]),
        BodyEncoding: "utf8_json", Body: body ?? "{\"input\":\"hello\"}", GapReason: gap), seq, "journal", seq, "hash-" + seq);
store.Add(Record(1, "root", "a", worker: "root"));
store.Add(Record(2, "root", "b", worker: "root", body: "{\"output\":[{\"type\":\"function_call\",\"call_id\":\"call\",\"name\":\"lookup\",\"arguments\":\"{}\"}],\"status\":\"completed\"}"));
store.Add(Record(3, "worker-one", "a", "one", "root", "request-1"));
store.Add(Record(4, "worker-two", "b", "two", "root", "request-1"));
store.Add(Record(5, "root", "b", worker: "root", body: "{\"input\":[{\"type\":\"function_call_output\",\"call_id\":\"call\",\"output\":\"client result\"}]}"));
store.Add(Record(6, "root", "b", worker: "root", kind: "run_manifest", body: "{\"id\":\"run-a\",\"variant\":\"skill-v1\",\"skillHash\":\"explicit-hash\",\"evidence\":\"configured\"}"));
var projection = new RecordingConversationProjection(store, new RecordingProtocolNormalizer());
var page = await projection.ListAsync(new(Limit: 2));
Check(page.Items.Length == 2 && page.HasMore && page.NextCursor is not null, "bounded conversation page and cursor");
var second = await projection.ListAsync(new(Limit: 2, Cursor: page.NextCursor));
Check(second.Items.Length == 1 && !second.HasMore && !page.Items.Select(x => x.Id).Intersect(second.Items.Select(x => x.Id)).Any(), "keyset continuation never repeats summaries");
var all = await projection.ListAsync(new(Limit: 100));
Check(all.Items.Length == 3 && all.Items.Single(c => c.WorkerId == "root").Models.SequenceEqual(["a", "b"]), "model switch stays one evidenced conversation");
Check(all.Items.Count(c => c.ParentWorkerId == "root") == 2, "root and two supplied worker links remain visible");
var root = all.Items.Single(c => c.WorkerId == "root");
var detail = await projection.GetAsync(root.Id, limit: 2);
Check(detail is { Records.Length: 2, HasMore: true, NextCursor: not null }, "selected timeline has bounded detail pages");
var detail2 = await projection.GetAsync(root.Id, detail!.NextCursor, 2);
Check(detail2 is { Records.Length: 2, HasMore: false } && !detail.Records.Select(r => r.Id).Intersect(detail2.Records.Select(r => r.Id)).Any(), "selected timeline resumes by committed occurrence");
var full = await projection.GetAsync(root.Id);
Check(full!.Runs is [{ Id: "run-a", Variant: "skill-v1", Evidence: "configured", ObservedInstructionHash: null }], "run variant configured provenance never promotes to observed");
var relations = RecordingConversationProjection.ToolRelations(full.Records);
Check(relations is [{ ToolCallId: "call", State: "client_reported", ExecutionProven: false, RequestMessageId: not null }], "explicit tool match retains client evidence without execution");
var before = JsonSerializer.Serialize(await projection.GetAsync(root.Id));
await projection.RebuildAsync();
Check(before == JsonSerializer.Serialize(await projection.GetAsync(root.Id)), "rebuild produces equivalent source-linked view");
store.Add(Record(7, "late", "c"));
Check((await projection.ListAsync(new(Cursor: page.NextCursor))).ErrorCode == "stale_cursor", "source mutation invalidates prior cursor");
store.RemoveCapture();
Check((await projection.ListAsync(new())).Items.Length == 0 && await projection.GetAsync(root.Id) is null, "source deletion never resurrects cached projections");
var limited = new MemoryStore();
for (var i = 1; i <= 200; i++) limited.Add(Record(i, "load-" + (i % 3), i % 2 == 0 ? "a" : "b",
    body: i <= 100 ? "{\"output\":[{\"type\":\"function_call\",\"call_id\":\"load-" + i + "\",\"name\":\"lookup\",\"arguments\":\"{}\"}]}" : null));
var load = new RecordingConversationProjection(limited, new RecordingProtocolNormalizer());
Check((await load.ListAsync(new())).Items.Sum(c => c.RequestCount) == 200, "synthetic 200 request and 100 tool workload");
Check((await new RecordingConversationProjection(limited, new RecordingProtocolNormalizer(), new(MaxScannedRecords: 5)).ListAsync(new())).ErrorCode == "projection_scan_limit", "query work limit is explicit coverage");
var external = Record(1, "unknown", "a") with { Observation = Record(1, "unknown", "a").Observation with { Dimensions = new(ConversationId: "unproven") } };
Check(new RecordingProtocolNormalizer().Normalize(external).Single().ConversationId is null, "unproven identity cannot fabricate joins");
using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
var canceled = false;
try { await load.ListAsync(new(), cancellation.Token); } catch (OperationCanceledException) { canceled = true; }
Check(canceled, "canceled query stops before reading source");
var forkStore = new MemoryStore();
var fork = Record(1, "fork", "a", body: "{\"metadata\":{\"parent_conversation_id\":\"parent\"},\"input\":\"fork branch\"}");
fork = fork with { Observation = fork.Observation with { Dimensions = fork.Observation.Dimensions! with {
    Evidence = [new("conversationId", "harness_header"), new("parentConversationId", "request_body", "metadata.parent_conversation_id")] } } };
forkStore.Add(fork); forkStore.Add(Record(2, "parent", "a"));
var forkProjection = new RecordingConversationProjection(forkStore, new RecordingProtocolNormalizer());
var forkPage = await forkProjection.ListAsync(new());
Check(forkPage.Items.Single(x => x.Title == "fork branch").ParentId == forkPage.Items.Single(x => x.Title == "hello").Id, "explicit supplied fork points to scoped parent conversation");
var firstFilter = await load.ListAsync(new(Limit: 1, Model: "a"));
Check((await load.ListAsync(new(Limit: 1, Cursor: firstFilter.NextCursor, Model: "b"))).ErrorCode == "stale_cursor", "cursor cannot be reused with a different filter");
Check((await load.ListAsync(new(Model: "a"))).Items.Sum(c => c.RequestCount) == 200, "model filter preserves whole matching conversation counts");
var conflictStore = new MemoryStore();
conflictStore.Add(Record(1, "conflict", "a", "worker-a")); conflictStore.Add(Record(2, "conflict", "a", "worker-b"));
var conflicted = (await new RecordingConversationProjection(conflictStore, new RecordingProtocolNormalizer()).ListAsync(new())).Items.Single();
Check(conflicted.WorkerId is null && !conflicted.Complete, "conflicting supplied worker identity stays unknown");
var indexDirectory = Path.Combine(Path.GetTempPath(), "lain-recording-index-fixture-" + Guid.NewGuid().ToString("N"));
try
{
    var databasePath = Path.Combine(indexDirectory, "projection.db");
    using (var indexed = new RecordingIndexedProjection(limited, new RecordingProtocolNormalizer(), databasePath, RecordingSchema.Sql))
    {
        await indexed.RebuildAsync();
        var beforeReads = limited.ReadCalls;
        var indexedPage = await indexed.ListAsync(new(Limit: 1));
        Check(indexedPage.Items.Length == 1 && indexedPage.HasMore, "SQLite summaries page from persisted metadata");
        Check(limited.ReadCalls == beforeReads, "repeated indexed list performs zero source payload reads");
        var indexedDetails = await indexed.GetAsync(indexedPage.Items[0].Id, limit: 2);
        Check(indexedDetails!.Records.Length == 2 && limited.ReadCalls - beforeReads <= 2, "indexed detail hydrates only selected source records");
        var sqliteCount = (await indexed.ListAsync(new(Limit: 100))).Items.Sum(i => i.RequestCount);
        Check(sqliteCount == 200, "SQLite index preserves synthetic 200 request graph");
    }
    using (var reopened = new RecordingIndexedProjection(limited, new RecordingProtocolNormalizer(), databasePath, RecordingSchema.Sql))
    {
        var beforeReads = limited.ReadCalls;
        Check((await reopened.ListAsync(new())).Items.Length == 3 && limited.ReadCalls == beforeReads, "SQLite restart reuses durable index without archive replay");
        limited.Add(Record(201, "new-conversation", "new-model"));
        await reopened.ApplyAsync(Record(201, "new-conversation", "new-model"));
        Check((await reopened.ListAsync(new())).Items.Sum(i => i.RequestCount) == 201, "committed incremental source indexes transactionally");
        limited.RemoveCapture();
        Check((await reopened.ListAsync(new())).Items.Length == 0, "deletion invalidates and securely rebuilds SQLite derivatives");
    }
    if (!OperatingSystem.IsWindows()) Check(File.GetUnixFileMode(databasePath) == (UnixFileMode.UserRead | UnixFileMode.UserWrite), "SQLite index file remains 0600");
}
finally { if (Directory.Exists(indexDirectory)) Directory.Delete(indexDirectory, true); }
var performanceDirectory = Path.Combine(Path.GetTempPath(), "lain-recording-summary-cache-" + Guid.NewGuid().ToString("N"));
try
{
    var conversationStore = new MemoryStore();
    for (var i = 1; i <= 200; i++) conversationStore.Add(Record(i, "long-conversation", "same-model"));
    var path = Path.Combine(performanceDirectory, "long.db");
    using (var indexed = new RecordingIndexedProjection(conversationStore, new RecordingProtocolNormalizer(), path, RecordingSchema.Sql))
    {
        await indexed.RebuildAsync();
        Check(indexed.SummaryMetadataRowsRead == 0 && indexed.SummaryMetadataScans == 1,
            "rebuild constructs cached summary incrementally without rescan of inserted metadata");
        var scans = indexed.SummaryMetadataScans;
        var rows = indexed.SummaryMetadataRowsRead;
        for (var i = 201; i <= 350; i++)
        {
            var appended = Record(i, "long-conversation", "same-model");
            conversationStore.Add(appended); await indexed.ApplyAsync(appended);
        }
        Check(indexed.SummaryMetadataScans == scans && indexed.SummaryMetadataRowsRead == rows,
            "150 repeated appends perform zero conversation history metadata scans");
        Check((await indexed.ListAsync(new())).Items.Single().RequestCount == 350,
            "cached summary retains exact request counts after repeated append");
    }
    using (var reopened = new RecordingIndexedProjection(conversationStore, new RecordingProtocolNormalizer(), path, RecordingSchema.Sql))
    {
        var first = Record(351, "long-conversation", "same-model");
        conversationStore.Add(first); await reopened.ApplyAsync(first);
        Check(reopened.SummaryMetadataScans == 1 && reopened.SummaryMetadataRowsRead == 350,
            "first append after restart rehydrates prior metadata once");
        var next = Record(352, "long-conversation", "same-model");
        conversationStore.Add(next); await reopened.ApplyAsync(next);
        Check(reopened.SummaryMetadataScans == 1 && reopened.SummaryMetadataRowsRead == 350,
            "append after cold summary load does not reread history");
        var duplicate = Record(353, "long-conversation", "same-model");
        conversationStore.Add(duplicate);
        conversationStore.AfterNextRead = () => conversationStore.Add(Record(354, "long-conversation", "same-model"));
        var invalidated = await reopened.ListAsync(new());
        Check(invalidated.ErrorCode == "stale_cursor" && reopened.CachedSummaryCount == 0,
            "source mutation rolls back transaction and invalidates modified summary cache");
        await reopened.ApplyAsync(Record(354, "long-conversation", "same-model"));
        Check((await reopened.ListAsync(new())).Items.Single().RequestCount == 354,
            "retry after rollback cannot double count aborted summary updates");
        await reopened.RebuildAsync();
        Check((await reopened.ListAsync(new())).Items.Single().RequestCount == 354,
            "full rebuild discards old cache before adding source rows");
        var rowsBeforeVersionReset = reopened.SummaryMetadataRowsRead;
        var scansBeforeVersionReset = reopened.SummaryMetadataScans;
        using (var schemaConnection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path))
        {
            schemaConnection.Open();
            using var oldVersion = schemaConnection.CreateCommand();
            oldVersion.CommandText = "UPDATE recording_entities SET native_id='old-projector-version' WHERE kind='snapshot'";
            oldVersion.ExecuteNonQuery();
        }
        Check((await reopened.ListAsync(new())).Items.Single().RequestCount == 354 &&
            reopened.SummaryMetadataScans == scansBeforeVersionReset + 1 && reopened.SummaryMetadataRowsRead == rowsBeforeVersionReset,
            "projector version invalidation discards cached state before deterministic replay");
        conversationStore.RemoveCapture(); await reopened.RebuildAsync();
        Check(reopened.CachedSummaryCount == 0 && (await reopened.ListAsync(new())).Items.Length == 0,
            "source deletion clears cached summaries and persisted conversation rows");
    }
    var evictingStore = new MemoryStore();
    using (var bounded = new RecordingIndexedProjection(evictingStore, new RecordingProtocolNormalizer(),
        Path.Combine(performanceDirectory, "bounded.db"), RecordingSchema.Sql, new(MaxConversations: 1)))
    {
        var a = Record(1, "a", "a"); evictingStore.Add(a); await bounded.ApplyAsync(a);
        var b = Record(2, "b", "b"); evictingStore.Add(b); await bounded.ApplyAsync(b);
        var again = Record(3, "a", "a"); evictingStore.Add(again); await bounded.ApplyAsync(again);
        Check(bounded.CachedSummaryCount == 1 && bounded.SummaryCacheEvictions == 2 && bounded.SummaryMetadataRowsRead == 1,
            "metadata LRU stays within conversation cache cap and loads evicted history once");
        Check((await bounded.ListAsync(new())).Items.Sum(i => i.RequestCount) == 3,
            "summary cache eviction preserves persistent counts");
    }
}
finally { if (Directory.Exists(performanceDirectory)) Directory.Delete(performanceDirectory, true); }
var namespaces = new MemoryStore();
var nativeOne = Record(901, "stable-native-conversation", "model-a");
var nativeTwo = Record(902, "stable-native-conversation", "model-b");
namespaces.Add(nativeOne with { Observation = nativeOne.Observation with { CaptureId = "request-one", IdentityScope = "owned-instance-a" } });
namespaces.Add(nativeTwo with { Observation = nativeTwo.Observation with { CaptureId = "request-two", IdentityScope = "owned-instance-a" } });
var sameNativeDifferentOwner = Record(903, "stable-native-conversation", "model-c");
namespaces.Add(sameNativeDifferentOwner with { Observation = sameNativeDifferentOwner.Observation with { CaptureId = "request-three", IdentityScope = "owned-instance-b" } });
var scopeProjection = new RecordingConversationProjection(namespaces, new RecordingProtocolNormalizer());
var namespaceList = await scopeProjection.ListAsync(new());
Check(namespaceList.Items.Length == 2 && namespaceList.Items.Single(c => c.Models.Contains("model-a")).RequestCount == 2,
    "stable trusted identity scope joins native conversations across per-request capture IDs and model switches");
Check(namespaceList.Items.Single(c => c.Models.Contains("model-c")).RequestCount == 1,
    "same native identifier in another identity scope cannot merge with the first conversation");
Console.WriteLine($"{checks} recording projection checks passed");

sealed class MemoryStore : IRecordingStore
{
    private readonly List<CommittedObservation> _records = [];
    private long _high;
    public int ReadCalls;
    public Action? AfterNextRead;
    public void Add(CommittedObservation record) { _records.Add(record); _high = Math.Max(_high, record.Cursor); }
    public void RemoveCapture() { _records.Clear(); }
    public Task<RecordingCommitResult> CommitAsync(SanitizedObservation observation, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<RecordingCommitPage> ReadCommittedAsync(long after = 0, int limit = 100, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); ReadCalls++;
        var page = _records.Where(r => r.Cursor > after).OrderBy(r => r.Cursor).Take(limit).ToArray();
        var next = page.LastOrDefault()?.Cursor ?? _high;
        var callback = AfterNextRead; AfterNextRead = null; callback?.Invoke();
        return Task.FromResult(new RecordingCommitPage(page, next, _high, _records.Any(r => r.Cursor > next)));
    }
    public Task<RecordingStorageHealthDto> HealthAsync(CancellationToken ct = default) => Task.FromResult(new RecordingStorageHealthDto(RecordCount: _records.Count, HighWatermark: _high));
    public Task ClearAsync(CancellationToken ct = default) { _records.Clear(); return Task.CompletedTask; }
}
