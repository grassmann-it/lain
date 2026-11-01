using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Recording.Normalization;

namespace Lain.Host.Recording.Projection;

/// <summary>Hard query-work caps. Scan truncation is explicit; list queries never retain message payloads.</summary>
public sealed record RecordingProjectionLimits(int MaxPageSize = 100, int SourcePageSize = 32,
    int MaxScannedRecords = 100000, int MaxConversations = 16384, int MaxRuns = 256,
    int MaxDetailBytes = 16 * 1024 * 1024);

/// <summary>
/// Rebuildable read facade over the store's durable journal. No independent persistence/checkpoint can
/// outlive deletion. Each bounded read replays a fixed source high-watermark and validates it before return.
/// </summary>
public sealed class RecordingConversationProjection(IRecordingStore store, IRecordingNormalizer normalizer,
    RecordingProjectionLimits? limits = null) : IRecordingProjection
{
    private readonly RecordingProjectionLimits _limits = limits ?? new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // The committed source already is the projection's durable authority. Apply never accepts RAM-only history.
    public Task ApplyAsync(CommittedObservation observation, CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    public async Task RebuildAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { normalizer.Reset(); }
        finally { _gate.Release(); }
    }

    public async Task<RecordingConversationPage> ListAsync(RecordingConversationQuery query, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var health = await store.HealthAsync(ct);
            var stamp = Stamp(health);
            var scope = "list:" + RecordingProtocolNormalizer.Identity(query.Model, query.RouteId, query.WorkerId);
            if (!Decode(query.Cursor, stamp, scope, out var cursor)) return new([], ErrorCode: "stale_cursor", Snapshot: health.HighWatermark);
            var summaries = new Dictionary<string, Summary>(StringComparer.Ordinal);
            var stopped = false;
            var scan = await ScanAsync(health, (committed, record) =>
            {
                if (record is null) return true;
                var id = ConversationId(record, committed);
                if (!summaries.TryGetValue(id, out var summary))
                {
                    if (summaries.Count >= _limits.MaxConversations) { stopped = true; return false; }
                    summary = new Summary(id, record.ObservedAt); summaries.Add(id, summary);
                }
                summary.Add(record, committed.Observation.Kind);
                return true;
            }, ct);
            if (!SameSnapshot(health, await store.HealthAsync(ct))) return new([], ErrorCode: "stale_cursor", Snapshot: health.HighWatermark);
            var limit = Math.Clamp(query.Limit, 1, _limits.MaxPageSize);
            var ordered = summaries.Values.Where(s => s.Matches(query)).Where(s => cursor?.Last is null || string.CompareOrdinal(s.Id, cursor.Last) > 0)
                .OrderBy(s => s.Id, StringComparer.Ordinal).Take(limit + 1).Select(s => s.Freeze()).ToArray();
            var more = ordered.Length > limit;
            var items = ordered.Take(limit).ToArray();
            return new(items, more ? Encode(new(stamp, scope, items[^1].Id)) : null, more,
                stopped ? "projection_conversation_limit" : scan, health.HighWatermark);
        }
        finally { _gate.Release(); }
    }

    public async Task<RecordingConversationDetail?> GetAsync(string conversationId, string? cursor = null,
        int limit = 100, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var health = await store.HealthAsync(ct);
            var stamp = Stamp(health);
            if (!Decode(cursor, stamp, conversationId, out var resume))
                return ErrorDetail(conversationId, "stale_cursor");
            var result = new List<RecordingNormalizedRecord>();
            var runs = new List<RecordingRunManifest>();
            Summary? summary = null;
            var size = 0L;
            var more = false;
            var pageBlocked = false;
            string? last = null;
            string? gap = null;
            var take = Math.Clamp(limit, 1, _limits.MaxPageSize);
            var scan = await ScanAsync(health, (committed, record) =>
            {
                if (record is null || ConversationId(record, committed) != conversationId) return true;
                summary ??= new Summary(conversationId, record.ObservedAt);
                summary.Add(record, committed.Observation.Kind);
                if (ReadManifest(committed.Observation) is { } manifest && runs.Count < _limits.MaxRuns) runs.Add(manifest);
                var key = committed.Cursor.ToString("D20", System.Globalization.CultureInfo.InvariantCulture) + ":" + record.Id;
                if (resume?.Last is not null && string.CompareOrdinal(key, resume.Last) <= 0) return true;
                if (result.Count >= take || pageBlocked) { more = true; return true; }
                var bytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(record, Json));
                if (bytes > _limits.MaxDetailBytes && result.Count == 0)
                {
                    gap = "detail_body_omitted";
                    record = record with { Messages = [], Complete = false, GapReason = gap };
                    bytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(record, Json));
                }
                if (size + bytes > _limits.MaxDetailBytes)
                { more = true; pageBlocked = true; gap = "detail_byte_limit"; return true; }
                size += bytes; result.Add(record); last = key;
                return true;
            }, ct);
            if (!SameSnapshot(health, await store.HealthAsync(ct))) return ErrorDetail(conversationId, "stale_cursor");
            if (summary is null) return null;
            return new(summary.Freeze(), result.ToArray(), more && last is not null ? Encode(new(stamp, conversationId, last)) : null,
                more, runs.Distinct().ToArray(), gap ?? scan);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Resolve only explicit call-ID matches within a proven conversation/branch scope. No execution assertion.</summary>
    public static RecordingToolRelation[] ToolRelations(IEnumerable<RecordingNormalizedRecord> records)
    {
        var requests = new Dictionary<(string Capture, string? Conversation, string? Worker, string Call), List<string>>();
        var results = new List<RecordingToolRelation>();
        var materialized = records.ToArray();
        foreach (var r in materialized)
            foreach (var m in r.Messages)
                foreach (var p in m.Parts.Where(p => p.Kind == "tool_request" && p.ToolCallId is not null))
                {
                    var key = (r.IdentityScope ?? r.CaptureId, r.ConversationId, r.WorkerId, p.ToolCallId!);
                    if (!requests.TryGetValue(key, out var ids)) requests[key] = ids = [];
                    ids.Add(m.Id);
                }
        foreach (var r in materialized)
            foreach (var m in r.Messages)
                foreach (var p in m.Parts.Where(p => p.Kind == "tool_result"))
                {
                    var provenScope = r.ConversationId is not null;
                    var found = p.ToolCallId is not null && provenScope && requests.TryGetValue((r.IdentityScope ?? r.CaptureId, r.ConversationId, r.WorkerId, p.ToolCallId), out var ids) ? ids : null;
                    results.Add(new(p.ToolCallId, found is { Count: 1 } ? found[0] : null, m.Id,
                        found is { Count: > 1 } ? "ambiguous" : found is { Count: 1 } ? "client_reported" : "unresolved", false));
                }
        return results.ToArray();
    }

    private async Task<string?> ScanAsync(RecordingStorageHealthDto snapshot,
        Func<CommittedObservation, RecordingNormalizedRecord?, bool> visit, CancellationToken ct)
    {
        normalizer.Reset();
        var after = 0L;
        var scanned = 0;
        string? gap = snapshot.RetainedFromCursor > 1 ? "retained_prefix_unavailable" : null;
        while (after < snapshot.HighWatermark)
        {
            ct.ThrowIfCancellationRequested();
            var page = await store.ReadCommittedAsync(after, Math.Clamp(_limits.SourcePageSize, 1, 100), ct);
            if (page.GapReason is not null) gap = page.GapReason;
            foreach (var item in page.Records)
            {
                if (item.Cursor > snapshot.HighWatermark) break;
                if (++scanned > _limits.MaxScannedRecords) return "projection_scan_limit";
                foreach (var record in normalizer.Normalize(item))
                    if (!visit(item, record)) return "projection_work_limit";
            }
            if (page.NextCursor <= after) return after < snapshot.HighWatermark ? "source_cursor_gap" : gap;
            after = Math.Min(page.NextCursor, snapshot.HighWatermark);
            if (!page.HasMore) break;
        }
        return gap;
    }

    public static string ConversationId(RecordingNormalizedRecord record, CommittedObservation source) =>
        record.ConversationId is { } id ? RecordingProtocolNormalizer.Identity("conversation", record.IdentityScope ?? record.CaptureId, id) :
        RecordingProtocolNormalizer.Identity("unassigned_request", record.CaptureId, source.Observation.SourceEpoch, record.RequestId);

    private static RecordingRunManifest? ReadManifest(SanitizedObservation o)
    {
        if (o.Kind is not ("run_manifest" or "run_annotation") || o.Body is null || o.RedactionState != "sanitized") return null;
        try
        {
            using var doc = JsonDocument.Parse(o.Body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("runManifest", out var manifest)) manifest = root;
            var supplied = manifest.Deserialize<RecordingRunManifest>(Json);
            if (supplied is null || string.IsNullOrWhiteSpace(supplied.Id) || supplied.Evidence is not ("configured" or "client_reported" or "observed" or "user_selected")) return null;
            return supplied;
        }
        catch (JsonException) { return null; }
    }
    private static string Stamp(RecordingStorageHealthDto h) => $"{h.HighWatermark}:{h.RecordCount}:{h.RetainedFromCursor}";
    private static bool SameSnapshot(RecordingStorageHealthDto a, RecordingStorageHealthDto b) => Stamp(a) == Stamp(b);
    private static string Encode(Cursor c) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(c, Json));
    private static bool Decode(string? encoded, string stamp, string scope, out Cursor? cursor)
    {
        cursor = null;
        if (encoded is null) return true;
        if (encoded.Length > 4096) return false;
        try { cursor = JsonSerializer.Deserialize<Cursor>(Convert.FromBase64String(encoded), Json); return cursor is not null && cursor.Stamp == stamp && cursor.Scope == scope; }
        catch (Exception e) when (e is FormatException or JsonException) { return false; }
    }
    private sealed record Cursor(string Stamp, string Scope, string? Last = null);
    private static RecordingConversationDetail ErrorDetail(string id, string gap) =>
        new(new(id, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 0, 0, [], Complete: false), [], GapReason: gap);

    internal sealed class Summary(string id, DateTimeOffset started)
    {
        public string Id => id;
        private DateTimeOffset _started = started, _updated = started;
        private readonly HashSet<string> _requests = new(StringComparer.Ordinal), _models = new(StringComparer.Ordinal), _routes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string?> _dimensions = new(StringComparer.Ordinal);
        private readonly HashSet<string> _conflicts = new(StringComparer.Ordinal);
        private int _messages;
        private bool _complete = true;
        private string? _title;
        private string? _finish;
        private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
        public void Add(RecordingNormalizedRecord r, string sourceKind, int? messageCount = null, string? title = null)
        {
            _started = r.ObservedAt < _started ? r.ObservedAt : _started;
            _updated = r.ObservedAt > _updated ? r.ObservedAt : _updated;
            if (sourceKind is not ("run_manifest" or "run_annotation"))
                _requests.Add(r.Sources.FirstOrDefault()?.SourceEpoch + ":" + r.RequestId);
            if (r.RouteId is not null) _routes.Add(r.RouteId);
            if (r.Model is not null) _models.Add(r.Model);
            _messages += messageCount ?? r.Messages.Length;
            var streamKey = r.RequestId + ":" + r.AttemptId;
            if (r.GapReason == "stream_content_deferred") _pending.Add(streamKey);
            else
            {
                _complete &= r.Complete;
                if (sourceKind == "normalized_stream_body") _pending.Remove(streamKey);
            }
            if (r.FinishReason is not null) _finish = r.FinishReason;
            Merge("parent", r.ParentConversationId is null ? null : RecordingProtocolNormalizer.Identity("conversation", r.IdentityScope ?? r.CaptureId, r.ParentConversationId)); Merge("worker", r.WorkerId); Merge("parentWorker", r.ParentWorkerId);
            _title ??= title ?? r.Messages.Where(m => m.Role == "user").SelectMany(m => m.Parts).FirstOrDefault(p => p.Kind == "text")?.Text;
            if (_title?.Length > 120) _title = _title[..120];
        }
        private void Merge(string field, string? value)
        {
            if (value is null || _conflicts.Contains(field)) return;
            if (_dimensions.TryGetValue(field, out var prior) && prior != value)
            { _dimensions[field] = null; _conflicts.Add(field); _complete = false; }
            else _dimensions[field] = value;
        }
        public bool Matches(RecordingConversationQuery q) => (q.Model is null || _models.Contains(q.Model)) &&
            (q.RouteId is null || _routes.Contains(q.RouteId)) && (q.WorkerId is null || _dimensions.GetValueOrDefault("worker") == q.WorkerId);
        public RecordingConversationSummary Freeze() => new(id, _started, _updated, _requests.Count, _messages,
            _models.Order(StringComparer.Ordinal).ToArray(), _finish ?? "partial", _complete && _pending.Count == 0,
            _dimensions.GetValueOrDefault("parent"), _dimensions.GetValueOrDefault("worker"), _dimensions.GetValueOrDefault("parentWorker"), _title);
    }
}

public sealed record RecordingToolRelation(string? ToolCallId, string? RequestMessageId,
    string ResultMessageId, string State, bool ExecutionProven = false);
