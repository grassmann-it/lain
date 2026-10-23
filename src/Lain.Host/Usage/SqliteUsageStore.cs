using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Lain.Core;
using Microsoft.Data.Sqlite;

namespace Lain.Host.Usage;

/// <summary>Single host-owned database; all journal mutations and acknowledgement commit together.</summary>
public sealed partial class SqliteUsageStore : IUsageStore, IUsageAnalysisStore
{
    public const int MaximumBatchSize = 1000;
    public const int MaximumPageSize = 500;
    public const int MaximumGroupCount = 1000;
    public static readonly TimeSpan MaximumRange = TimeSpan.FromDays(366);
    private readonly string connectionString;
    private readonly TimeProvider clock;

    public SqliteUsageStore(string databasePath, TimeProvider? clock = null)
    {
        this.clock = clock ?? TimeProvider.System;
        var path = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, DefaultTimeout = 10 }.ToString();
        using var connection = Open();
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        using var transaction = connection.BeginTransaction();
        var version = Convert.ToInt32(Command(connection, transaction, "PRAGMA user_version").ExecuteScalar(), CultureInfo.InvariantCulture);
        if (version > 3) throw new InvalidOperationException("Usage database schema is newer than this host.");
        foreach (var migration in new[] { (Version: 1, File: "001_usage.sql"), (Version: 2, File: "002_source_coverage.sql"), (Version: 3, File: "003_attempt_dimensions.sql") })
        {
            if (version >= migration.Version) continue;
            var assembly = typeof(SqliteUsageStore).Assembly;
            var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("Usage.Migrations." + migration.File, StringComparison.Ordinal));
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            Command(connection, transaction, reader.ReadToEnd()).ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA busy_timeout=10000;";
        command.ExecuteNonQuery();
        return connection;
    }

    public async Task<long> GetCursorAsync(string sourceId, CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = Command(connection, null, "SELECT cursor FROM sources WHERE source_id=$source", ("$source", sourceId));
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct) ?? 0, CultureInfo.InvariantCulture);
    }

    public async Task MarkCollectionFailureAsync(CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = Command(connection, null, "UPDATE sources SET faulted=1");
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task IngestAsync(JournalBatch batch, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (string.IsNullOrWhiteSpace(batch.SourceId) || batch.Events is null || batch.Events.Length > MaximumBatchSize || batch.NextCursor < 0 || batch.HighWatermark < batch.NextCursor)
            throw new InvalidDataException("Invalid journal envelope.");
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var observed = clock.GetUtcNow().ToUnixTimeMilliseconds();
        using (var create = Command(connection, transaction,
            "INSERT INTO sources(source_id,cursor,high_watermark,observed_ms) VALUES($source,0,0,$now) ON CONFLICT DO NOTHING",
            ("$source", batch.SourceId), ("$now", observed)))
            await create.ExecuteNonQueryAsync(ct);
        // A fresh UUID can conceal an unread tail of the old journal, even when
        // its last delivered watermark was fully ingested. Keep that uncertainty
        // independently of transient collection faults and atomically with data.
        using (var gap = Command(connection, transaction, """
            INSERT INTO coverage_gaps(from_ms,to_ms,previous_source_id,next_source_id,reason)
            SELECT MIN(s.observed_ms,$now),MAX(s.observed_ms+1,$now),s.source_id,$source,'source_changed'
            FROM collector_state c JOIN sources s ON s.source_id=c.active_source_id
            WHERE c.singleton=1 AND c.active_source_id <> $source
            """, ("$source", batch.SourceId), ("$now", observed)))
            await gap.ExecuteNonQueryAsync(ct);
        using (var active = Command(connection, transaction,
            "UPDATE collector_state SET active_source_id=$source WHERE singleton=1", ("$source", batch.SourceId)))
            await active.ExecuteNonQueryAsync(ct);
        long cursor;
        using (var read = Command(connection, transaction, "SELECT cursor FROM sources WHERE source_id=$source", ("$source", batch.SourceId)))
            cursor = Convert.ToInt64(await read.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        long previous = 0;
        foreach (var item in batch.Events)
        {
            ct.ThrowIfCancellationRequested();
            Validate(item, batch.SourceId);
            if (previous > 0 && item.Sequence != previous + 1) throw new InvalidDataException("Journal batch is not contiguous.");
            previous = item.Sequence;
            var digest = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(item, JournalJson.Options)));
            using var existing = Command(connection, transaction, "SELECT digest FROM journal_events WHERE source_id=$source AND sequence=$seq", ("$source", batch.SourceId), ("$seq", item.Sequence));
            var known = await existing.ExecuteScalarAsync(ct) as string;
            if (known is not null)
            {
                if (known != digest) throw new InvalidDataException("Previously ingested journal event changed.");
                continue;
            }
            if (item.Sequence != cursor + 1) throw new InvalidDataException("Journal gap: refusing to acknowledge missing events.");
            await ApplyAsync(connection, transaction, item, ct);
            using var insert = Command(connection, transaction,
                "INSERT INTO journal_events(source_id,sequence,event_id,digest) VALUES($source,$seq,$id,$digest)",
                ("$source", batch.SourceId), ("$seq", item.Sequence), ("$id", item.EventId), ("$digest", digest));
            await insert.ExecuteNonQueryAsync(ct);
            cursor = item.Sequence;
        }
        if ((batch.Events.Length > 0 && batch.NextCursor != batch.Events[^1].Sequence) || (batch.Events.Length == 0 && batch.NextCursor != cursor))
            throw new InvalidDataException("Journal nextCursor does not acknowledge the delivered batch.");
        using var update = Command(connection, transaction,
            "UPDATE sources SET cursor=$cursor,high_watermark=MAX(high_watermark,$high,$cursor),observed_ms=$now,faulted=0 WHERE source_id=$source",
            ("$cursor", cursor), ("$high", batch.HighWatermark), ("$now", observed), ("$source", batch.SourceId));
        await update.ExecuteNonQueryAsync(ct);
        ct.ThrowIfCancellationRequested();
        transaction.Commit();
    }

    private static void Validate(JournalEvent e, string source)
    {
        if (e.SchemaVersion != 1 || e.SourceId != source || e.Sequence <= 0 || e.EventId != $"{source}:{e.Sequence}" ||
            string.IsNullOrWhiteSpace(e.RequestId) || string.IsNullOrWhiteSpace(e.AttemptId) || string.IsNullOrWhiteSpace(e.AccountId) ||
            string.IsNullOrWhiteSpace(e.CredentialId) || string.IsNullOrWhiteSpace(e.Provider) || string.IsNullOrWhiteSpace(e.Model) ||
            e.ObservedAt == default || e.Kind is not ("attempt_started" or "usage" or "attempt_finished"))
            throw new InvalidDataException("Invalid journal event identity or schema.");
        if (new[] { e.InputTokens, e.OutputTokens, e.TotalTokens, e.CacheReadTokens, e.CacheCreationTokens, e.ReasoningTokens }.Any(n => n < 0) ||
            e.LatencyMs is double latency && (!double.IsFinite(latency) || latency < 0) ||
            e.TtftMs is double ttft && (!double.IsFinite(ttft) || ttft < 0) || e.StatusCode is < 100 or > 599)
            throw new InvalidDataException("Invalid usage quantity or timing.");
        if (e.Kind == "usage" && e.Outcome is not ("success" or "failed" or "canceled"))
            throw new InvalidDataException("Terminal usage requires an outcome.");
        if (e.Kind == "attempt_finished" && (e.Outcome is not null || e.StatusCode is not null || e.UsageReported ||
            new[] { e.InputTokens, e.OutputTokens, e.TotalTokens, e.CacheReadTokens, e.CacheCreationTokens, e.ReasoningTokens }.Any(n => n is not null)))
            throw new InvalidDataException("Attempt completion may only add observation metadata, not rewrite reported usage.");
        ValidateDimensions(e.Dimensions);
        ValidateMeasurements(e.Measurements);
        if (e.CompletionState is not null && (e.CompletionState is not ("success" or "failed" or "canceled") || e.Kind == "attempt_started"))
            throw new InvalidDataException("Invalid wrapper completion state.");
    }

    private static async Task ApplyAsync(SqliteConnection connection, SqliteTransaction transaction, JournalEvent e, CancellationToken ct)
    {
        if (e.Kind == "attempt_finished")
        {
            using var finish = Command(connection, transaction, """
                UPDATE attempts SET completion_recorded=1 WHERE source_id=$source AND attempt_id=$attempt
                    AND request_id=$request AND account_id=$account AND credential_id=$credential
                    AND route_id IS $route AND provider=$provider AND model=$model
                    AND state <> 'pending' AND completion_recorded=0
                """, ("$source", e.SourceId), ("$attempt", e.AttemptId), ("$request", e.RequestId),
                ("$account", e.AccountId), ("$credential", e.CredentialId), ("$route", e.RouteId),
                ("$provider", e.Provider), ("$model", e.Model));
            if (await finish.ExecuteNonQueryAsync(ct) != 1)
                throw new InvalidDataException("Attempt completion requires matching reported usage and may occur only once.");
            await SaveObservationsAsync(connection, transaction, e, ct);
            return;
        }
        if (e.Kind == "attempt_started")
        {
            using var insert = Command(connection, transaction, """
                INSERT INTO attempts(source_id,attempt_id,request_id,account_id,credential_id,route_id,provider,requested_model,model,started_ms,state,usage_reported)
                VALUES($source,$attempt,$request,$account,$credential,$route,$provider,$requested,$model,$started,'pending',0)
                """, ("$source", e.SourceId), ("$attempt", e.AttemptId), ("$request", e.RequestId), ("$account", e.AccountId),
                ("$credential", e.CredentialId), ("$route", e.RouteId), ("$provider", e.Provider), ("$requested", e.RequestedModel), ("$model", e.Model), ("$started", e.ObservedAt.ToUnixTimeMilliseconds()));
            await insert.ExecuteNonQueryAsync(ct);
            await SaveObservationsAsync(connection, transaction, e, ct);
            return;
        }
        using var update = Command(connection, transaction, """
            UPDATE attempts SET model=$model,finished_ms=$finished,status_code=$status,state=$state,usage_reported=$reported,
                input_tokens=$input,output_tokens=$output,cached_tokens=$cached,cache_creation_tokens=$creation,
                reasoning_tokens=$reasoning,total_tokens=$total,duration_ms=$duration,first_token_ms=$first
            WHERE source_id=$source AND attempt_id=$attempt AND request_id=$request AND account_id=$account
                AND credential_id=$credential AND provider=$provider AND route_id IS $route AND state='pending' AND started_ms <= $finished
            """, ("$source", e.SourceId), ("$attempt", e.AttemptId), ("$request", e.RequestId), ("$account", e.AccountId), ("$credential", e.CredentialId),
            ("$provider", e.Provider), ("$route", e.RouteId), ("$model", e.Model), ("$finished", e.ObservedAt.ToUnixTimeMilliseconds()), ("$status", e.StatusCode),
            ("$state", e.Outcome), ("$reported", e.UsageReported ? 1 : 0), ("$input", e.UsageReported ? e.InputTokens : null),
            ("$output", e.UsageReported ? e.OutputTokens : null), ("$cached", e.UsageReported ? e.CacheReadTokens : null),
            ("$creation", e.UsageReported ? e.CacheCreationTokens : null), ("$reasoning", e.UsageReported ? e.ReasoningTokens : null),
            ("$total", e.UsageReported ? e.TotalTokens : null), ("$duration", e.LatencyMs), ("$first", e.TtftMs));
        if (await update.ExecuteNonQueryAsync(ct) != 1) throw new InvalidDataException("Terminal event lacks a matching pending dispatch or changes its identity.");
        await SaveObservationsAsync(connection, transaction, e, ct);
    }

    private sealed record Window(DateTimeOffset From, DateTimeOffset To, TimeZoneInfo Zone);
    private Window ValidateQuery(UsageQuery query)
    {
        ValidateAnalysisQuery(query);
        if (query.Offset < 0 || query.Limit is < 1 or > MaximumPageSize || query.StatusCode is < 100 or > 599)
            throw new ArgumentException("Usage page limit must be 1–500, offset nonnegative and status 100–599.", nameof(query));
        var to = query.To ?? clock.GetUtcNow();
        var from = query.From ?? to.AddDays(-30);
        if (from >= to || to - from > MaximumRange) throw new ArgumentException("Usage interval must be positive and at most 366 days.", nameof(query));
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(query.TimeZone); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        { throw new ArgumentException("Unknown usage time zone.", nameof(query), ex); }
        return new(from, to, zone);
    }

    private static SqliteCommand Filtered(SqliteConnection connection, SqliteTransaction transaction, string select, UsageQuery query, Window window, string suffix = "")
    {
        var predicates = new List<string> { "started_ms >= $from", "started_ms < $to" };
        var parameters = new List<(string Name, object? Value)> { ("$from", window.From.ToUnixTimeMilliseconds()), ("$to", window.To.ToUnixTimeMilliseconds()) };
        void Add(string field, string? value)
        {
            if (value is null) return;
            var parameter = "$filter" + parameters.Count;
            predicates.Add(DimensionExpression(field) + "=" + parameter);
            parameters.Add((parameter, value));
        }
        Add("accountId", query.AccountId); Add("model", query.Model); Add("routeId", query.RouteId);
        Add("modelFamily", query.ModelFamily); Add("manufacturer", query.Manufacturer);
        Add("inferenceProvider", query.InferenceProvider); Add("gateway", query.Gateway);
        Add("billingAccountId", query.BillingAccountId); Add("reasoningEffort", query.ReasoningEffort);
        Add("effectiveReasoningEffort", query.EffectiveReasoningEffort);
        Add("mode", query.Mode); Add("conversationId", query.ConversationId); Add("turnId", query.TurnId);
        Add("workerId", query.WorkerId); Add("harness", query.Harness); Add("profileId", query.ProfileId);
        if (query.StatusCode is not null) { predicates.Add("status_code=$status"); parameters.Add(("$status", query.StatusCode)); }
        return Command(connection, transaction, select + " FROM attempts WHERE " + string.Join(" AND ", predicates) + " " + suffix, parameters.ToArray());
    }

    // A component is only a known total when every selected attempt supplies that component.
    // Cache/reasoning values are subsets: they are never added to provider-reported total.
    private const string Aggregate = """
        COUNT(DISTINCT source_id || ':' || request_id), COUNT(*),
        COALESCE(SUM(CASE WHEN state IN ('failed','canceled') OR status_code >= 400 THEN 1 ELSE 0 END),0),
        CASE WHEN COUNT(input_tokens)=COUNT(*) THEN SUM(input_tokens) END,
        CASE WHEN COUNT(output_tokens)=COUNT(*) THEN SUM(output_tokens) END,
        CASE WHEN COUNT(cached_tokens)=COUNT(*) THEN SUM(cached_tokens) END,
        CASE WHEN COUNT(reasoning_tokens)=COUNT(*) THEN SUM(reasoning_tokens) END,
        CASE WHEN COUNT(total_tokens)=COUNT(*) THEN SUM(total_tokens) END,
        COALESCE(SUM(CASE WHEN usage_reported=0 OR total_tokens IS NULL THEN 1 ELSE 0 END),0)
        """;

    public async Task<UsageSummaryDto> SummaryAsync(UsageQuery query, CancellationToken ct = default)
    {
        var window = ValidateQuery(query);
        using var connection = Open();
        connection.CreateFunction<long, string>("lain_day", value => TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(value), window.Zone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), isDeterministic: true);
        using var transaction = connection.BeginTransaction(deferred: true);
        UsageTotals totals;
        using (var command = Filtered(connection, transaction, "SELECT " + Aggregate, query, window))
        using (var reader = await command.ExecuteReaderAsync(ct)) { await reader.ReadAsync(ct); totals = ReadTotals(reader); }
        async Task<UsageGroup[]> Groups(string expression)
        {
            using var command = Filtered(connection, transaction, "SELECT " + expression + "," + Aggregate, query, window, $"GROUP BY {expression} ORDER BY {expression} LIMIT {MaximumGroupCount + 1}");
            using var reader = await command.ExecuteReaderAsync(ct);
            var groups = new List<UsageGroup>();
            while (await reader.ReadAsync(ct)) groups.Add(new(reader.GetString(0), ReadTotals(reader, 1)));
            return groups.ToArray();
        }
        var daily = await Groups("lain_day(started_ms)");
        var accounts = await Groups("account_id");
        var models = await Groups("model");
        var coverage = await ReadCoverageAsync(connection, transaction, query, window, ct);
        var complete = coverage.Complete && accounts.Length <= MaximumGroupCount && models.Length <= MaximumGroupCount;
        return new(Api.Version, window.From, window.To, window.Zone.Id, coverage.ObservedAt, coverage.Cursor, complete, totals, daily, accounts.Take(MaximumGroupCount).ToArray(), models.Take(MaximumGroupCount).ToArray());
    }

    public async Task<PageDto<AttemptDto>> AttemptsAsync(UsageQuery query, CancellationToken ct = default)
    {
        var window = ValidateQuery(query);
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        using var count = Filtered(connection, transaction, "SELECT COUNT(*)", query, window);
        var total = Convert.ToInt64(await count.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        using var command = Filtered(connection, transaction, """
            SELECT attempt_id,request_id,account_id,route_id,provider,model,started_ms,finished_ms,status_code,state,
                input_tokens,output_tokens,cached_tokens,reasoning_tokens,total_tokens,duration_ms,first_token_ms,
                dimensions_json,measurements_json,source_id,requested_model,completion_state
            """, query, window, "ORDER BY started_ms DESC, source_id, attempt_id LIMIT $limit OFFSET $offset");
        command.Parameters.AddWithValue("$limit", query.Limit);
        command.Parameters.AddWithValue("$offset", query.Offset);
        using var reader = await command.ExecuteReaderAsync(ct);
        var items = new List<AttemptDto>();
        while (await reader.ReadAsync(ct))
            items.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), reader.GetString(5),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(6)), reader.IsDBNull(7) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(7)),
                reader.IsDBNull(8) ? null : reader.GetInt32(8), reader.GetString(9), new(NullableLong(reader, 10), NullableLong(reader, 11), NullableLong(reader, 12), NullableLong(reader, 13), NullableLong(reader, 14)),
                reader.IsDBNull(15) ? null : reader.GetDouble(15), reader.IsDBNull(16) ? null : reader.GetDouble(16),
                Dimensions: reader.IsDBNull(17) ? null : JsonSerializer.Deserialize<AttemptDimensionsDto>(reader.GetString(17), JournalJson.Options),
                Measurements: reader.IsDBNull(18) ? null : JsonSerializer.Deserialize<AttemptMeasurementsDto>(reader.GetString(18), JournalJson.Options),
                SourceId: reader.GetString(19), RequestedModel: reader.GetString(20), CompletionState: reader.IsDBNull(21) ? null : reader.GetString(21)));
        return new(items.ToArray(), query.Offset, query.Limit, total);
    }

    private static long? NullableLong(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetInt64(index);
    private static UsageTotals ReadTotals(SqliteDataReader reader, int start = 0) => new(reader.GetInt64(start), reader.GetInt64(start + 1), reader.GetInt64(start + 2),
        new(NullableLong(reader, start + 3), NullableLong(reader, start + 4), NullableLong(reader, start + 5), NullableLong(reader, start + 6), NullableLong(reader, start + 7)), reader.GetInt64(start + 8));
    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }
}
