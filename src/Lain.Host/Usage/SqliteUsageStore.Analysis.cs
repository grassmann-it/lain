using System.Globalization;
using Lain.Core;
using Microsoft.Data.Sqlite;

namespace Lain.Host.Usage;

public sealed partial class SqliteUsageStore
{
    // The analysis endpoint can report the actual wrapper outcome. The v1
    // summary keeps its historical reported-usage error definition unchanged.
    private static readonly string AnalysisAggregate = Aggregate.Replace(
        "CASE WHEN state IN ('failed','canceled') OR status_code >= 400 THEN 1 ELSE 0 END",
        "CASE WHEN completion_state IN ('failed','canceled') THEN 1 WHEN completion_state='success' THEN 0 WHEN state IN ('failed','canceled') OR status_code >= 400 THEN 1 ELSE 0 END", StringComparison.Ordinal);
    private static string DimensionExpression(string dimension) => dimension switch
    {
        "accountId" => "account_id", "routeId" => "route_id", "model" => "model", "provider" => "provider",
        _ when DimensionFields.Contains(dimension, StringComparer.Ordinal) => "json_extract(dimensions_json,'$." + dimension + "')",
        _ => throw new ArgumentException("Unsupported usage grouping dimension.", nameof(dimension))
    };

    private static string[] GroupDimensions(UsageQuery query) =>
        (query.GroupBy ?? "modelFamily,accountId,reasoningEffort").Split(',', StringSplitOptions.TrimEntries);

    private static void ValidateAnalysisQuery(UsageQuery query)
    {
        if (query.GroupLimit is < 1 or > 500) throw new ArgumentException("Group limit must be 1–500.", nameof(query));
        var dimensions = GroupDimensions(query);
        if (dimensions.Length is < 1 or > 3 || dimensions.Distinct(StringComparer.Ordinal).Count() != dimensions.Length)
            throw new ArgumentException("Choose one to three distinct grouping dimensions.", nameof(query));
        foreach (var dimension in dimensions) _ = DimensionExpression(dimension);
    }

    private readonly record struct GroupIdentity(string? Key);
    private sealed record GroupTotals(string? Key, UsageTotals Totals);
    private sealed record MetricValues(long Samples, double? Minimum, double? Mean, double? P50, double? P95, double? Maximum)
    {
        public MetricStatisticsDto For(long eligible) => new(Samples, eligible, Minimum, Mean, P50, P95, Maximum);
    }
    private static MetricStatisticsDto UnknownMetric(long eligible) => new(0, eligible, null, null, null, null, null);

    private static async Task<Dictionary<GroupIdentity, MetricValues>> MetricsAsync(
        SqliteConnection connection, SqliteTransaction transaction, UsageQuery query, Window window,
        string groupExpression, string metricExpression, int maximumGroups, CancellationToken ct)
    {
        using var command = Filtered(connection, transaction, $"SELECT {groupExpression} AS group_key, {metricExpression} AS value", query, window);
        var selection = command.CommandText;
        // Continuous quantiles over every qualified value in the filtered group:
        // rank = 1 + (N-1)*p, linearly interpolated between adjacent ordered ranks.
        // Neither attempt pagination nor the number of displayed groups changes N.
        static string Quantile(string p)
        {
            var offset = $"((n-1)*{p})";
            var lower = $"CAST({offset} AS INTEGER)";
            var fraction = $"({offset}-{lower})";
            return $"SUM(CASE WHEN rn=1+{lower} THEN value*(1-{fraction}) WHEN rn=2+{lower} THEN value*{fraction} ELSE 0 END)";
        }
        command.CommandText = $"""
            WITH selected AS ({selection}), ranked AS (
                SELECT group_key,value,ROW_NUMBER() OVER(PARTITION BY group_key ORDER BY value) AS rn,
                    COUNT(*) OVER(PARTITION BY group_key) AS n,
                    MAX(value) OVER(PARTITION BY group_key) AS scale
                FROM selected WHERE value IS NOT NULL AND value >= 0 AND value <= 1.7976931348623157e308
            )
            SELECT group_key,COUNT(*),MIN(value),
                CASE WHEN MAX(scale)=0 THEN 0 ELSE MAX(scale)*AVG(value/scale) END,
                {Quantile("0.5")},{Quantile("0.95")},MAX(value)
            FROM ranked GROUP BY group_key ORDER BY group_key LIMIT $groupLimit
            """;
        command.Parameters.AddWithValue("$groupLimit", maximumGroups);
        using var reader = await command.ExecuteReaderAsync(ct);
        var result = new Dictionary<GroupIdentity, MetricValues>();
        while (await reader.ReadAsync(ct))
        {
            var key = new GroupIdentity(reader.IsDBNull(0) ? null : reader.GetString(0));
            double? Number(int column) => reader.IsDBNull(column) || !double.IsFinite(reader.GetDouble(column)) ? null : reader.GetDouble(column);
            result.Add(key, new(reader.GetInt64(1), Number(2), Number(3), Number(4), Number(5), Number(6)));
        }
        return result;
    }

    private static async Task<UsageDimensionGroupDto[]> AnalyzeGroupsAsync(SqliteConnection connection, SqliteTransaction transaction,
        UsageQuery query, Window window, string groupExpression, int maximumGroups, CancellationToken ct)
    {
        var groups = new List<GroupTotals>();
        using (var command = Filtered(connection, transaction, $"SELECT {groupExpression}," + AnalysisAggregate, query, window,
            $"GROUP BY {groupExpression} ORDER BY {groupExpression} LIMIT $groupLimit"))
        {
            command.Parameters.AddWithValue("$groupLimit", maximumGroups);
            using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) groups.Add(new(reader.IsDBNull(0) ? null : reader.GetString(0), ReadTotals(reader, 1)));
        }
        if (groups.Count == 0) return [];
        var first = await MetricsAsync(connection, transaction, query, window, groupExpression, "first_output_latency_ms", maximumGroups, ct);
        var duration = await MetricsAsync(connection, transaction, query, window, groupExpression, "total_duration_ms", maximumGroups, ct);
        var rate = await MetricsAsync(connection, transaction, query, window, groupExpression,
            "CASE WHEN usage_reported=1 AND output_tokens IS NOT NULL AND total_duration_ms > 0 THEN output_tokens*1000.0/total_duration_ms END", maximumGroups, ct);
        return groups.Select(group =>
        {
            var key = new GroupIdentity(group.Key);
            var eligible = group.Totals.Attempts;
            MetricStatisticsDto Find(Dictionary<GroupIdentity, MetricValues> source) => source.TryGetValue(key, out var metric) ? metric.For(eligible) : UnknownMetric(eligible);
            return new UsageDimensionGroupDto(group.Key, group.Totals, new(Find(first), Find(duration), Find(rate)));
        }).ToArray();
    }

    public async Task<UsageAnalysisDto> AnalysisAsync(UsageQuery query, CancellationToken ct = default)
    {
        var window = ValidateQuery(query);
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        // A constant string expression gives one group for the whole filtered set.
        var whole = await AnalyzeGroupsAsync(connection, transaction, query, window, "'all'", 1, ct);
        UsageTotals totals;
        PerformanceSummaryDto performance;
        if (whole.Length == 1) { totals = whole[0].Totals; performance = whole[0].Performance; }
        else
        {
            totals = new(0, 0, 0, new(null, null, null, null, null), 0);
            performance = new(UnknownMetric(0), UnknownMetric(0), UnknownMetric(0));
        }
        var dimensions = new List<UsageDimensionDto>();
        foreach (var dimension in GroupDimensions(query))
        {
            var groups = await AnalyzeGroupsAsync(connection, transaction, query, window, DimensionExpression(dimension), query.GroupLimit + 1, ct);
            dimensions.Add(new(dimension, groups.Take(query.GroupLimit).ToArray(), groups.Length > query.GroupLimit));
        }
        var coverage = await ReadCoverageAsync(connection, transaction, query, window, ct);
        return new(Api.Version, window.From, window.To, window.Zone.Id, coverage.ObservedAt, coverage.Cursor,
            coverage.Complete && !dimensions.Any(d => d.Truncated), totals, performance, dimensions.ToArray());
    }

    private sealed record CaptureCoverage(long Cursor, DateTimeOffset ObservedAt, bool Complete);
    private static async Task<CaptureCoverage> ReadCoverageAsync(SqliteConnection connection, SqliteTransaction transaction, UsageQuery query, Window window, CancellationToken ct)
    {
        using var metadata = Command(connection, transaction, "SELECT COALESCE(SUM(cursor),0),MIN(observed_ms),COALESCE(SUM(high_watermark-cursor+faulted),0) FROM sources");
        using var state = await metadata.ExecuteReaderAsync(ct);
        await state.ReadAsync(ct);
        var cursor = state.GetInt64(0);
        var observed = state.IsDBNull(1) ? DateTimeOffset.UnixEpoch : DateTimeOffset.FromUnixTimeMilliseconds(state.GetInt64(1));
        var caughtUp = !state.IsDBNull(1) && state.GetInt64(2) == 0;
        state.Close();
        using var pending = Filtered(connection, transaction, "SELECT COUNT(*)", query, window, "AND state='pending'");
        var noPending = Convert.ToInt64(await pending.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) == 0;
        using var gaps = Command(connection, transaction,
            "SELECT EXISTS(SELECT 1 FROM coverage_gaps WHERE from_ms < $to AND to_ms > $from)",
            ("$from", window.From.ToUnixTimeMilliseconds()), ("$to", window.To.ToUnixTimeMilliseconds()));
        var noGaps = Convert.ToInt64(await gaps.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) == 0;
        return new(cursor, observed, caughtUp && noPending && noGaps);
    }
}
