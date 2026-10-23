using System.Globalization;
using System.Text.Json;
using Lain.Core;

namespace Lain.Host.Quota;

public sealed class ZaiQuotaAdapter(HttpClient client) : IQuotaAdapter
{
    public const string Endpoint = "https://api.z.ai/api/monitor/usage/quota/limit";
    public string Source => Endpoint;

    public bool Supports(string provider) => provider.Equals("zai", StringComparison.OrdinalIgnoreCase)
        || provider.Equals("z.ai", StringComparison.OrdinalIgnoreCase)
        || provider.Equals("glm", StringComparison.OrdinalIgnoreCase);

    public async Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default)
    {
        if (account.CredentialKind != "api-key" || string.IsNullOrWhiteSpace(credential.BearerToken))
            throw new QuotaFetchException("Z.ai quota requires a qualified API key.");
        if (!IsOfficialBaseUrl(credential.BaseUrl))
            throw new QuotaFetchException("Z.ai quota requires a recognized official API base URL.");

        // Official reference: zai-org/zai-coding-plugins, plugins/glm-plan-usage/
        // skills/usage-query-skill/scripts/query-usage.mjs (read-only, 2026-10-23).
        // The monitor endpoint uses the raw key, unlike a Bearer-authenticated API.
        using var doc = await QuotaHttp.GetAsync(client, Endpoint, credential, ct,
            rawAuthorization: true, userAgent: "lain/0.1.0");
        var rows = Normalize(doc.RootElement);
        return new(rows, rows.Count == 0 ? "No recognized Z.ai quota windows; remaining budget unknown." : null);
    }

    private static bool IsOfficialBaseUrl(string? baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("api.z.ai", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            return false;
        return uri.AbsolutePath.TrimEnd('/') is "/api/v1" or "/v1" or "/api/paas/v4"
            or "/api/coding/paas/v4" or "/api/anthropic";
    }

    public static IReadOnlyList<QuotaReading> Normalize(JsonElement root)
    {
        var rows = new List<QuotaReading>();
        if (root.ValueKind != JsonValueKind.Object || JsonFields.Object(root, "success").ValueKind == JsonValueKind.False)
            return rows;
        var code = JsonFields.Object(root, "code");
        if (code.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null)
            && JsonFields.Number(root, "code") is not (0 or 200))
            return rows;
        var limits = JsonFields.Object(JsonFields.Object(root, "data"), "limits");
        if (limits.ValueKind != JsonValueKind.Array) return rows;

        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var window in limits.EnumerateArray())
        {
            var type = JsonFields.String(window, "type");
            var unit = type switch
            {
                "CREDIT_LIMIT" => "credits",
                "TOKENS_LIMIT" => "tokens",
                "TIME_LIMIT" => "requests",
                _ => null
            };
            if (unit is null) continue;

            // Numeric unit/number describe the provider's window, not the count unit.
            // Counts and resets change over time and must not change cache identity.
            var windowUnit = JsonFields.Number(window, "unit")?.ToString("R", CultureInfo.InvariantCulture);
            var windowNumber = JsonFields.Number(window, "number")?.ToString("R", CultureInfo.InvariantCulture);
            var identity = $"{type}/unit/{windowUnit ?? "unknown"}/number/{windowNumber ?? "unknown"}";
            var occurrence = occurrences.GetValueOrDefault(identity) + 1;
            occurrences[identity] = occurrence;
            // Multiple constraints with the same enum tuple remain separate readings.
            var suffix = occurrence == 1 ? "" : "/duplicate/" + occurrence.ToString(CultureInfo.InvariantCulture);
            var label = type switch
            {
                "CREDIT_LIMIT" => "Credits",
                "TOKENS_LIMIT" => "Tokens",
                _ => "MCP requests"
            };
            // Verified official my-plan frontend, 2026-10-23:
            // static.bigmodel.cn/z-ai-website/_next/static/chunks/3i6jwetj169n2.js
            // getCreditUsageLimitsDefaults/getDefaultUsageLimits identify unit 3
            // as 5-hour and unit 6 as weekly for credits/tokens. Restrict friendly
            // durations to the observed number as well; retain other enums in Source.
            label += (type, windowUnit, windowNumber) switch
            {
                ("CREDIT_LIMIT" or "TOKENS_LIMIT", "3", "5") => " · 5 hours",
                ("CREDIT_LIMIT" or "TOKENS_LIMIT", "6", "1") => " · 7 days",
                _ => " · quota window"
            };
            if (occurrence > 1) label += " · " + occurrence.ToString(CultureInfo.InvariantCulture);

            var percent = JsonFields.Number(window, "percentage");
            if (percent > 100) percent = null;
            // Read-only schema reference: modelcoach/provider_status.py normalize_glm.
            // Preserve independent count fields (including zero and missing values).
            // Counts may differ slightly through rounding or concurrent updates;
            // percentage is the provider's separate signal, never a synthetic count.
            rows.Add(new(Endpoint + "#" + identity + suffix, unit,
                Used: JsonFields.Number(window, "currentValue"),
                Limit: JsonFields.Number(window, "usage"),
                Remaining: JsonFields.Number(window, "remaining"),
                ResetsAt: JsonFields.Date(window, "nextResetTime"),
                InheritAccountPool: false, WindowLabel: label, UsedPercent: percent));
        }
        return rows;
    }
}
