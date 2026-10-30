using System.Text;
using System.Text.Json;
using Lain.Core;
using Microsoft.Data.Sqlite;
using Lain.Host.Recording.Normalization;

namespace Lain.Host.Recording.Projection;

/// <summary>
/// Disposable SQLite index of committed source. Schema comes exclusively from RecordingSchema.Sql
/// (storage owner); body data stays in the source journal. Place this database outside the source
/// archive directory so source retention never unlinks an open SQLite database.
/// </summary>
public sealed class RecordingIndexedProjection : IRecordingProjection, IRecordingProjectionRecovery, IDisposable
{
    private readonly IRecordingStore _store;
    private readonly IRecordingNormalizer _normalizer;
    private readonly RecordingProjectionLimits _limits;
    private readonly string _path, _schema;
    private SqliteConnection? _connection;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string SnapshotScope = "__recording_projection";
    private const long MaximumIndexBytes = 128L * 1024 * 1024;

    public RecordingIndexedProjection(IRecordingStore store, IRecordingNormalizer normalizer,
        string databasePath, string schemaSql, RecordingProjectionLimits? limits = null)
    {
        _store = store; _normalizer = normalizer; _limits = limits ?? new();
        _path = Path.GetFullPath(databasePath); _schema = schemaSql;
    }
    public void Dispose() { _connection?.Dispose(); _gate.Dispose(); }

    public async Task ApplyAsync(CommittedObservation observation, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { await SynchronizeAsync(false, ct); }
        finally { _gate.Release(); }
    }
    public async Task RecoverAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { if ((await _store.HealthAsync(ct)).RecordCount != 0 || File.Exists(_path)) await SynchronizeAsync(false, ct); }
        finally { _gate.Release(); }
    }
    public async Task RebuildAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { if ((await _store.HealthAsync(ct)).RecordCount != 0 || File.Exists(_path)) await SynchronizeAsync(true, ct); }
        finally { _gate.Release(); }
    }

    public async Task<RecordingConversationPage> ListAsync(RecordingConversationQuery query, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if ((await _store.HealthAsync(ct)).RecordCount == 0 && !File.Exists(_path)) return new([]);
            var health = await SynchronizeAsync(false, ct);
            var stamp = Stamp(health);
            var scope = "list:" + RecordingProtocolNormalizer.Identity(query.Model, query.RouteId, query.WorkerId);
            if (!ReadCursor(query.Cursor, stamp, scope, out var cursor)) return new([], ErrorCode: "stale_cursor", Snapshot: health.HighWatermark);
            var count = Math.Clamp(query.Limit, 1, _limits.MaxPageSize);
            using var cmd = Command("""
                SELECT e.provenance FROM recording_entities e
                WHERE e.kind='conversation' AND e.entity_id > $after
                  AND ($model IS NULL OR EXISTS(SELECT 1 FROM recording_records r WHERE r.conversation_id=e.entity_id AND r.model=$model))
                  AND ($route IS NULL OR EXISTS(SELECT 1 FROM recording_records r WHERE r.conversation_id=e.entity_id AND r.route_id=$route))
                  AND ($worker IS NULL OR json_extract(e.provenance,'$.workerId')=$worker)
                ORDER BY e.entity_id LIMIT $limit
                """, null, ("$after", cursor?.Last ?? ""), ("$model", query.Model), ("$route", query.RouteId), ("$worker", query.WorkerId), ("$limit", count + 1));
            var items = new List<RecordingConversationSummary>();
            using (var reader = cmd.ExecuteReader())
                while (reader.Read()) { ct.ThrowIfCancellationRequested(); items.Add(JsonSerializer.Deserialize<RecordingConversationSummary>(reader.GetString(0), Json)!); }
            if (Stamp(await _store.HealthAsync(ct)) != stamp) return new([], ErrorCode: "stale_cursor", Snapshot: health.HighWatermark);
            var more = items.Count > count;
            var selected = items.Take(count).ToArray();
            return new(selected, more ? WriteCursor(new(stamp, scope, selected[^1].Id)) : null, more, Snapshot: health.HighWatermark);
        }
        catch (SourceChangedException) { return new([], ErrorCode: "stale_cursor"); }
        finally { _gate.Release(); }
    }

    public async Task<RecordingConversationDetail?> GetAsync(string conversationId, string? cursor = null,
        int limit = 100, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if ((await _store.HealthAsync(ct)).RecordCount == 0 && !File.Exists(_path)) return null;
            var health = await SynchronizeAsync(false, ct);
            var stamp = Stamp(health);
            if (!ReadCursor(cursor, stamp, conversationId, out var after)) return ErrorDetail(conversationId, "stale_cursor");
            using var summaryCommand = Command("SELECT provenance FROM recording_entities WHERE kind='conversation' AND entity_id=$id", null, ("$id", conversationId));
            var summaryText = summaryCommand.ExecuteScalar() as string;
            if (summaryText is null) return null;
            var summary = JsonSerializer.Deserialize<RecordingConversationSummary>(summaryText, Json)!;
            var count = Math.Clamp(limit, 1, _limits.MaxPageSize);
            var afterCursor = 0L;
            var afterId = "";
            if (after?.Last is { } position)
            {
                var separator = position.IndexOf(':');
                if (separator < 1 || !long.TryParse(position[..separator], out afterCursor)) return ErrorDetail(conversationId, "stale_cursor");
                afterId = position[(separator + 1)..];
            }
            using var command = Command("""
                SELECT r.record_json FROM recording_records r
                JOIN recording_evidence e ON e.record_id=r.id
                JOIN recording_occurrences o ON o.source_epoch=e.source_epoch AND o.sequence=e.sequence
                WHERE r.conversation_id=$id AND (o.cursor > $after OR (o.cursor=$after AND r.id>$afterId))
                ORDER BY o.cursor,r.id LIMIT $limit
                """, null, ("$id", conversationId), ("$after", afterCursor), ("$afterId", afterId), ("$limit", count + 1));
            // Read metadata only. The selected page resolves exact journal cursors; no archive scan.
            var candidates = new List<IndexRecord>();
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    var item = JsonSerializer.Deserialize<IndexRecord>(reader.GetString(0), Json)!;
                    candidates.Add(item);
                    if (candidates.Count > count) break;
                }
            var records = new List<RecordingNormalizedRecord>();
            var bytes = 0L;
            string? lastKey = null, gap = null;
            var more = candidates.Count > count;
            foreach (var indexed in candidates.Take(count))
            {
                ct.ThrowIfCancellationRequested();
                var sourcePage = await _store.ReadCommittedAsync(indexed.Cursor - 1, 1, ct);
                var source = sourcePage.Records.FirstOrDefault(r => r.Cursor == indexed.Cursor);
                if (source is null) { gap = "source_unavailable"; continue; }
                var record = _normalizer.Normalize(source).FirstOrDefault(r => r.Id == indexed.Record.Id);
                if (record is null) { gap = "source_unavailable"; continue; }
                var size = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(record, Json));
                if (size > _limits.MaxDetailBytes && records.Count == 0)
                { gap = "detail_body_omitted"; record = record with { Messages = [], Complete = false, GapReason = gap }; size = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(record, Json)); }
                if (size + bytes > _limits.MaxDetailBytes) { gap = "detail_byte_limit"; more = true; break; }
                records.Add(record); bytes += size; lastKey = Key(indexed);
            }
            var runs = new List<RecordingRunManifest>();
            using var runCommand = Command("SELECT manifest_json FROM recording_runs WHERE conversation_id=$id ORDER BY id LIMIT $limit", null,
                ("$id", conversationId), ("$limit", _limits.MaxRuns));
            using (var reader = runCommand.ExecuteReader()) while (reader.Read()) runs.Add(JsonSerializer.Deserialize<RecordingRunManifest>(reader.GetString(0), Json)!);
            if (Stamp(await _store.HealthAsync(ct)) != stamp) return ErrorDetail(conversationId, "stale_cursor");
            return new(summary, records.ToArray(), more && lastKey is not null ? WriteCursor(new(stamp, conversationId, lastKey)) : null,
                more, runs.ToArray(), gap);
        }
        catch (SourceChangedException) { return ErrorDetail(conversationId, "stale_cursor"); }
        finally { _gate.Release(); }
    }

    private async Task<RecordingStorageHealthDto> SynchronizeAsync(bool rebuild, CancellationToken ct)
    {
        var health = await _store.HealthAsync(ct);
        Open();
        var saved = SavedSnapshot();
        if (!rebuild && saved is not null && Stamp(saved) == Stamp(health)) return health;
        var incremental = !rebuild && saved is not null && saved.HighWatermark <= health.HighWatermark &&
            saved.RetainedFromCursor == health.RetainedFromCursor &&
            health.RecordCount - saved.RecordCount == health.HighWatermark - saved.HighWatermark;
        using var transaction = _connection!.BeginTransaction();
        if (!incremental)
        {
            Execute("DELETE FROM recording_runs; DELETE FROM recording_evidence; DELETE FROM recording_records; DELETE FROM recording_occurrences; DELETE FROM recording_entities; DELETE FROM recording_edges;", transaction);
            _normalizer.Reset();
        }
        var after = incremental ? saved!.HighWatermark : 0L;
        var scanned = 0;
        var touched = new HashSet<string>(StringComparer.Ordinal);
        while (after < health.HighWatermark)
        {
            ct.ThrowIfCancellationRequested();
            var page = await _store.ReadCommittedAsync(after, Math.Clamp(_limits.SourcePageSize, 1, 100), ct);
            foreach (var source in page.Records.Where(r => r.Cursor <= health.HighWatermark))
            {
                if (++scanned > _limits.MaxScannedRecords) throw new InvalidOperationException("projection_scan_limit");
                Add(source, transaction, touched, ct);
            }
            if (page.NextCursor <= after) throw new SourceChangedException();
            after = Math.Min(page.NextCursor, health.HighWatermark);
            if (!page.HasMore) break;
        }
        if (Stamp(await _store.HealthAsync(ct)) != Stamp(health)) throw new SourceChangedException();
        foreach (var id in touched) Summarize(id, transaction, ct);
        Execute("""
            INSERT INTO recording_entities(scope,kind,entity_id,native_id,provenance,state)
            VALUES($scope,'snapshot','source',$versionText,$json,'ready')
            ON CONFLICT(scope,kind,entity_id) DO UPDATE SET provenance=excluded.provenance,native_id=excluded.native_id,state='ready';
            UPDATE recording_projection_state SET cursor=$cursor,projection_version=$version,rebuilding=0 WHERE singleton=1;
            """, transaction, ("$scope", SnapshotScope), ("$json", JsonSerializer.Serialize(health, Json)), ("$cursor", health.HighWatermark), ("$version", RecordingProtocolNormalizer.Version), ("$versionText", RecordingProtocolNormalizer.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        transaction.Commit();
        PrivateFiles();
        if (!incremental) Execute("VACUUM", null); // Deletion/rebuild does not leave old indexed previews in free pages.
        return health;
    }

    private void Add(CommittedObservation source, SqliteTransaction transaction, HashSet<string> touched, CancellationToken ct)
    {
        var o = source.Observation;
        using var check = Command("SELECT content_hash FROM recording_occurrences WHERE source_epoch=$epoch AND sequence=$sequence", transaction,
            ("$epoch", o.SourceEpoch), ("$sequence", o.Sequence));
        if (check.ExecuteScalar() is string hash)
        { if (hash != source.ContentHash) throw new InvalidOperationException("source_identity_conflict"); return; }
        Execute("""
            INSERT INTO recording_occurrences(source_epoch,sequence,event_id,cursor,journal_id,journal_offset,content_hash,capture_id,request_id,attempt_id,observed_ms,boundary,protocol,kind,complete,gap_reason)
            VALUES($epoch,$sequence,$event,$cursor,$journal,$offset,$hash,$capture,$request,$attempt,$time,$boundary,$protocol,$kind,$complete,$gap)
            """, transaction, ("$epoch", o.SourceEpoch), ("$sequence", o.Sequence), ("$event", o.EventId), ("$cursor", source.Cursor), ("$journal", source.JournalId), ("$offset", source.JournalOffset),
            ("$hash", source.ContentHash), ("$capture", o.CaptureId), ("$request", o.RequestId), ("$attempt", o.AttemptId), ("$time", o.ObservedAt.ToUnixTimeMilliseconds()),
            ("$boundary", o.Boundary), ("$protocol", o.Protocol), ("$kind", o.Kind), ("$complete", o.Complete), ("$gap", o.GapReason));
        foreach (var r in _normalizer.Normalize(source))
        {
            ct.ThrowIfCancellationRequested();
            var conversation = RecordingConversationProjection.ConversationId(r, source);
            touched.Add(conversation);
            if (touched.Count > _limits.MaxConversations) throw new InvalidOperationException("projection_conversation_limit");
            var title = r.Messages.Where(m => m.Role == "user").SelectMany(m => m.Parts).FirstOrDefault(p => p.Kind == "text")?.Text;
            if (title?.Length > 120) title = title[..120];
            var metadata = r with { Messages = [], Sources = [new(o.SourceEpoch, o.Sequence, o.EventId)] };
            var index = new IndexRecord(metadata, source.Cursor, o.Kind, r.Messages.Length, title);
            Execute("""
                INSERT INTO recording_records(id,capture_id,request_id,attempt_id,conversation_id,parent_conversation_id,worker_id,parent_worker_id,parent_request_id,observed_ms,model,requested_model,route_id,provider,account_id,response_id,previous_response_id,complete,gap_reason,projection_version,record_json)
                VALUES($id,$capture,$request,$attempt,$conversation,$parent,$worker,$parentworker,$parentrequest,$time,$model,$requested,$route,$provider,$account,$response,$previous,$complete,$gap,$version,$json)
                """, transaction, ("$id", r.Id), ("$capture", r.CaptureId), ("$request", r.RequestId), ("$attempt", r.AttemptId), ("$conversation", conversation), ("$parent", r.ParentConversationId),
                ("$worker", r.WorkerId), ("$parentworker", r.ParentWorkerId), ("$parentrequest", r.ParentRequestId), ("$time", r.ObservedAt.ToUnixTimeMilliseconds()), ("$model", r.Model),
                ("$requested", r.RequestedModel), ("$route", r.RouteId), ("$provider", r.Provider), ("$account", r.AccountId), ("$response", r.ResponseId), ("$previous", r.PreviousResponseId),
                ("$complete", r.Complete), ("$gap", r.GapReason), ("$version", r.ProjectionVersion), ("$json", JsonSerializer.Serialize(index, Json)));
            Execute("INSERT INTO recording_evidence(record_id,source_epoch,sequence,event_id) VALUES($id,$epoch,$sequence,$event)", transaction,
                ("$id", r.Id), ("$epoch", o.SourceEpoch), ("$sequence", o.Sequence), ("$event", o.EventId));
            SaveRun(o, conversation, source.Cursor, transaction);
            if (r.WorkerId is not null && r.ParentWorkerId is not null)
                SaveEdge(r.IdentityScope ?? r.CaptureId, r.WorkerId, r.ParentWorkerId, "worker_parent", o, transaction);
            if (r.ParentConversationId is not null)
                SaveEdge(r.IdentityScope ?? r.CaptureId, conversation, RecordingProtocolNormalizer.Identity("conversation", r.IdentityScope ?? r.CaptureId, r.ParentConversationId), "conversation_parent", o, transaction);
            if (r.ParentRequestId is not null) SaveEdge(r.IdentityScope ?? r.CaptureId, r.RequestId, r.ParentRequestId, "request_parent", o, transaction);
        }
    }

    private void Summarize(string id, SqliteTransaction transaction, CancellationToken ct)
    {
        RecordingConversationProjection.Summary? summary = null;
        using var command = Command("SELECT record_json FROM recording_records WHERE conversation_id=$id ORDER BY observed_ms,id", transaction, ("$id", id));
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                var item = JsonSerializer.Deserialize<IndexRecord>(reader.GetString(0), Json)!;
                summary ??= new(id, item.Record.ObservedAt);
                summary.Add(item.Record, item.Kind, item.MessageCount, item.Title);
            }
        if (summary is null) return;
        var value = summary.Freeze();
        Execute("""
            INSERT INTO recording_entities(scope,kind,entity_id,native_id,provenance,state)
            VALUES($scope,'conversation',$id,NULL,$json,$state)
            ON CONFLICT(scope,kind,entity_id) DO UPDATE SET provenance=excluded.provenance,state=excluded.state
            """, transaction, ("$scope", SnapshotScope), ("$id", id), ("$json", JsonSerializer.Serialize(value, Json)), ("$state", value.State));
    }
    private void SaveRun(SanitizedObservation observation, string conversation, long cursor, SqliteTransaction transaction)
    {
        if (observation.Kind is not ("run_manifest" or "run_annotation") || observation.Body is null) return;
        try
        {
            using var document = JsonDocument.Parse(observation.Body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            if (root.TryGetProperty("runManifest", out var nested)) root = nested;
            var run = root.Deserialize<RecordingRunManifest>(Json);
            if (run is null || string.IsNullOrWhiteSpace(run.Id) || run.Evidence is not ("configured" or "client_reported" or "observed" or "user_selected")) return;
            var id = RecordingProtocolNormalizer.Identity(observation.CaptureId, run.Id);
            Execute("""
                INSERT INTO recording_runs(id,capture_id,conversation_id,variant,configured_revision,prompt_hash,skill_hash,observed_instruction_hash,evidence,source_cursor,manifest_json)
                VALUES($id,$capture,$conversation,$variant,$revision,$prompt,$skill,$observed,$evidence,$cursor,$json)
                ON CONFLICT(id) DO UPDATE SET variant=excluded.variant,manifest_json=excluded.manifest_json,source_cursor=excluded.source_cursor
                """, transaction, ("$id", id), ("$capture", observation.CaptureId), ("$conversation", conversation), ("$variant", run.Variant), ("$revision", run.ConfiguredRevision),
                ("$prompt", run.PromptHash), ("$skill", run.SkillHash), ("$observed", run.ObservedInstructionHash), ("$evidence", run.Evidence), ("$cursor", cursor), ("$json", JsonSerializer.Serialize(run, Json)));
        }
        catch (JsonException) { }
    }

    private void SaveEdge(string scope, string from, string to, string kind, SanitizedObservation source, SqliteTransaction transaction) =>
        Execute("INSERT OR IGNORE INTO recording_edges(scope,from_id,to_id,kind,source_epoch,sequence,evidence,state) VALUES($scope,$from,$to,$kind,$epoch,$sequence,'supplied_dimension','observed')", transaction,
            ("$scope", scope), ("$from", from), ("$to", to), ("$kind", kind), ("$epoch", source.SourceEpoch), ("$sequence", source.Sequence));

    private void Open()
    {
        if (_connection is not null) return;
        var directory = Path.GetDirectoryName(_path)!;
        for (var part = new DirectoryInfo(directory); part is not null; part = part.Parent) RejectLink(part.FullName);
        Directory.CreateDirectory(directory); RejectLink(_path);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (!File.Exists(_path))
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using var created = new FileStream(_path, options);
        }
        _connection = new(new SqliteConnectionStringBuilder { DataSource = _path, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
        _connection.Open();
        Execute("PRAGMA journal_mode=DELETE; PRAGMA temp_store=MEMORY; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=2500; PRAGMA secure_delete=ON;", null);
        using var pageSizeCommand = Command("PRAGMA page_size", null);
        var pageSize = Convert.ToInt64(pageSizeCommand.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        Execute("PRAGMA max_page_count=" + (MaximumIndexBytes / pageSize).ToString(System.Globalization.CultureInfo.InvariantCulture), null);
        Execute(_schema, null); PrivateFiles();
    }
    private void PrivateFiles()
    {
        if (OperatingSystem.IsWindows()) return;
        foreach (var name in new[] { _path, _path + "-journal", _path + "-wal", _path + "-shm" })
            if (File.Exists(name)) File.SetUnixFileMode(name, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
    private static void RejectLink(string path)
    {
        try { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("projection_path_link"); }
        catch (FileNotFoundException) { } catch (DirectoryNotFoundException) { }
    }
    private RecordingStorageHealthDto? SavedSnapshot()
    {
        using var command = Command("SELECT provenance FROM recording_entities WHERE scope=$scope AND kind='snapshot' AND entity_id='source' AND native_id=$version", null,
            ("$scope", SnapshotScope), ("$version", RecordingProtocolNormalizer.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return command.ExecuteScalar() is string json ? JsonSerializer.Deserialize<RecordingStorageHealthDto>(json, Json) : null;
    }
    private SqliteCommand Command(string sql, SqliteTransaction? transaction, params (string Name, object? Value)[] parameters)
    {
        var command = _connection!.CreateCommand(); command.CommandText = sql; command.Transaction = transaction;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }
    private void Execute(string sql, SqliteTransaction? transaction, params (string Name, object? Value)[] parameters)
    { using var command = Command(sql, transaction, parameters); command.ExecuteNonQuery(); }
    private static string Stamp(RecordingStorageHealthDto h) => $"{h.HighWatermark}:{h.RecordCount}:{h.RetainedFromCursor}";
    private static string Key(IndexRecord record) => record.Cursor.ToString("D20", System.Globalization.CultureInfo.InvariantCulture) + ":" + record.Record.Id;
    private sealed record IndexRecord(RecordingNormalizedRecord Record, long Cursor, string Kind, int MessageCount, string? Title);
    private sealed record Cursor(string Stamp, string Scope, string? Last = null);
    private static string WriteCursor(Cursor cursor) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor, Json));
    private static bool ReadCursor(string? text, string stamp, string scope, out Cursor? cursor)
    {
        cursor = null; if (text is null) return true; if (text.Length > 4096) return false;
        try { cursor = JsonSerializer.Deserialize<Cursor>(Convert.FromBase64String(text), Json); return cursor is not null && cursor.Stamp == stamp && cursor.Scope == scope; }
        catch (Exception e) when (e is JsonException or FormatException) { return false; }
    }
    private static RecordingConversationDetail ErrorDetail(string id, string gap) => new(new(id, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 0, 0, []), [], GapReason: gap);
    private sealed class SourceChangedException : Exception;
}
