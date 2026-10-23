using System.Text.Json;
using Lain.Host.Quota;

namespace Lain.Host.Providers.Poe;

public sealed record PoeUsageEntry(string QueryId, string BotName, string UsageType, long CostPoints, DateTimeOffset CreatedAt);
public sealed record PoeUsagePage(IReadOnlyList<PoeUsageEntry> Entries, bool? HasMore, string? NextCursor, bool CompletePage);

public sealed class PoeUsageHistory(HttpClient client)
{
    public const string Endpoint = "https://api.poe.com/usage/points_history";

    // Deliberately one page at a time. This is an account-wide, 30-day history
    // containing API, Chat and Canvas App usage, not lain-only usage or totals.
    public async Task<PoeUsagePage> FetchPageAsync(QuotaCredential credential, int limit = 20, string? startingAfter = null, CancellationToken ct = default)
    {
        if (credential.BaseUrl?.TrimEnd('/') != "https://api.poe.com/v1")
            throw new InvalidOperationException("Poe history requires the official API base URL.");
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        var url = Endpoint + "?limit=" + limit.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (startingAfter is not null) url += "&starting_after=" + Uri.EscapeDataString(startingAfter);
        using var document = await QuotaHttp.GetAsync(client, url, credential, ct, userAgent: "lain/1.0");
        return Normalize(document.RootElement);
    }

    public static PoeUsagePage Normalize(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("error", out _) || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return new([], null, null, false);
        var entries = new List<PoeUsageEntry>();
        bool complete = true;
        string? lastQueryId = null;
        foreach (var item in data.EnumerateArray())
        {
            var id = JsonFields.String(item, "query_id");
            lastQueryId = string.IsNullOrWhiteSpace(id) ? null : id;
            var bot = JsonFields.String(item, "bot_name");
            var kind = JsonFields.String(item, "usage_type");
            var time = JsonFields.Object(item, "creation_time");
            var cost = JsonFields.Object(item, "cost_points");
            if (id is null || string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(bot)
                || kind is not ("API" or "Chat" or "Canvas App")
                || cost.ValueKind != JsonValueKind.Number || !cost.TryGetInt64(out var points) || points < 0
                || time.ValueKind != JsonValueKind.Number || !time.TryGetInt64(out var microseconds) || microseconds < 0)
            { complete = false; continue; }
            try
            {
                var created = DateTimeOffset.UnixEpoch.AddTicks(checked(microseconds * 10));
                entries.Add(new(id, bot, kind, points, created));
            }
            catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException) { complete = false; }
        }
        bool? more = root.TryGetProperty("has_more", out var hasMore) && hasMore.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? hasMore.GetBoolean() : null;
        // Never derive a cursor from the last successfully parsed row: a later
        // malformed row would then be fetched repeatedly or silently skipped.
        return new(entries, more, more == true ? lastQueryId : null, complete);
    }
}
