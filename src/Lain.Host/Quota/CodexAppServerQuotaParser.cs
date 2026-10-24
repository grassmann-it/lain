using System.Text.Json;
using Lain.Host.Accounts.Managed;

namespace Lain.Host.Quota;

public sealed record CodexAppServerCredits(string BucketId, bool? HasCredits, bool? Unlimited, string? Balance);
public sealed record CodexAppServerQuotaSnapshot(QuotaReading[] Readings, CodexAppServerCredits[] Credits);

/// <summary>Documented RPC result parser. It never reads WHAM, auth.json, global CLI state, or creates an owner.</summary>
public static class CodexAppServerQuotaParser
{
    public const string Source = "codex-app-server:account/rateLimits/read";
    public static QuotaReading[] Parse(JsonElement result) => ParseSnapshot(result).Readings;
    public static CodexAppServerQuotaSnapshot ParseSnapshot(JsonElement result)
    {
        var buckets = ManagedJson.Field(result, "rateLimitsByLimitId");
        var rows = new List<QuotaReading>();
        var creditRows = new List<CodexAppServerCredits>();
        void Bucket(JsonElement bucket, string id)
        {
            if (bucket.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(id) || id.Length > 256 || id.Any(char.IsControl)) throw ManagedJson.Invalid();
            var declared = ManagedJson.Text(bucket, "limitId");
            if (declared is not null && declared != id) throw ManagedJson.Invalid();
            foreach (var windowName in new[] { "primary", "secondary" })
            {
                var window = ManagedJson.Field(bucket, windowName);
                if (window.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
                var used = ManagedJson.Number(window, "usedPercent");
                if (used is not (>= 0 and <= 100)) used = null;
                var minutes = ManagedJson.Number(window, "windowDurationMins");
                _ = ManagedJson.Field(window, "resetsAt");
                var label = minutes is > 0 ? $"{minutes:0.##}-minute window" : windowName == "primary" ? "Primary window" : "Secondary window";
                rows.Add(new(Source + "#" + Uri.EscapeDataString(id) + "/" + windowName, "percent", used, used.HasValue ? 100 : null,
                    used.HasValue ? 100 - used : null, JsonFields.Date(window, "resetsAt"), InheritAccountPool: false,
                    WindowLabel: label, UsedPercent: used));
            }
            var credits = ManagedJson.Field(bucket, "credits");
            if (credits.ValueKind == JsonValueKind.Object)
            {
                // Balance/hasCredits/unlimited do not establish a currency or finite quota unit.
                bool? Flag(string name) => ManagedJson.Field(credits, name).ValueKind switch
                { JsonValueKind.True => true, JsonValueKind.False => false, JsonValueKind.Undefined or JsonValueKind.Null => null, _ => throw ManagedJson.Invalid() };
                var balance = ManagedJson.Text(credits, "balance");
                if (balance?.Length > 64 || balance?.Any(char.IsControl) == true) throw ManagedJson.Invalid();
                creditRows.Add(new(id, Flag("hasCredits"), Flag("unlimited"), balance));
            }
        }
        if (buckets.ValueKind == JsonValueKind.Object)
        {
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var bucket in buckets.EnumerateObject())
            {
                if (!identities.Add(bucket.Name) || identities.Count > 64) throw ManagedJson.Invalid();
                Bucket(bucket.Value, bucket.Name);
            }
            return new(rows.ToArray(), creditRows.ToArray()); // Explicit empty multibucket view does not duplicate/fall back to a stale single bucket.
        }
        if (buckets.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null)) throw ManagedJson.Invalid();
        var legacy = ManagedJson.Field(result, "rateLimits");
        if (legacy.ValueKind == JsonValueKind.Object) Bucket(legacy, ManagedJson.Text(legacy, "limitId") ?? "default");
        else if (legacy.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)) throw ManagedJson.Invalid();
        return new(rows.ToArray(), creditRows.ToArray());
    }
}
