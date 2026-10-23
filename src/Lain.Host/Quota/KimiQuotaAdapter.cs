using System.Text.Json;
using Lain.Core;

namespace Lain.Host.Quota;

public sealed class KimiQuotaAdapter(HttpClient client) : IQuotaAdapter
{
    public const string Endpoint = "https://api.kimi.com/coding/v1/usages";
    public string Source => Endpoint;
    public bool Supports(string provider) => provider.Equals("kimi", StringComparison.OrdinalIgnoreCase);

    public async Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default)
    {
        if (account.CredentialKind != "api-key")
            throw new QuotaFetchException("Kimi quota requires a qualified API key.");
        // Only the account's own normalized secret qualifies its endpoint. A custom
        // Kimi-compatible gateway key must never be sent to the official service.
        var endpoint = credential.BaseUrl?.TrimEnd('/') switch
        {
            "https://api.kimi.com/coding/v1" => Endpoint,
            "https://api.kimi.ai/coding/v1" => "https://api.kimi.ai/coding/v1/usages",
            _ => throw new QuotaFetchException("Kimi quota requires an official coding API base URL.")
        };
        using var doc = await QuotaHttp.GetAsync(client, endpoint, credential, ct, userAgent: "lain/1.0");
        var rows = Normalize(doc.RootElement);
        return new(rows, rows.Count == 0 ? "No recognized quota windows; remaining budget unknown." : null);
    }

    // Read-only reference: modelcoach/provider_status.py normalize_kimi.
    // Canonical sources keep window identity stable across official host aliases
    // and old month_* / new limit_month_* response spellings.
    public static IReadOnlyList<QuotaReading> Normalize(JsonElement root)
    {
        var rows = new List<QuotaReading>();
        var usages = JsonFields.Object(root, "usages");
        if (usages.ValueKind == JsonValueKind.Object)
        {
            foreach (var (key, alias, label) in new (string, string?, string)[]
            {
                ("limit_5h", null, "5 hours"),
                ("limit_7d", null, "7 days"),
                ("limit_month_total", "month_total", "Monthly total"),
                ("limit_month_code", "month_code", "Monthly coding")
            })
            {
                var window = JsonFields.Object(usages, key);
                if (window.ValueKind != JsonValueKind.Object && alias is not null)
                    window = JsonFields.Object(usages, alias);
                if (window.ValueKind != JsonValueKind.Object) continue;
                var ratio = JsonFields.Number(window, "used_ratio");
                var percent = ratio is >= 0 and <= 1 ? ratio * 100 : null;
                rows.Add(new(Endpoint + "#" + key, "percent", percent, percent.HasValue ? 100 : null,
                    percent.HasValue ? 100 - percent : null, JsonFields.Date(window, "reset_time"),
                    WindowLabel: label, UsedPercent: percent));
            }
            // An unknown modern schema must stay unknown, never fall back to an
            // additional set of legacy limits that could describe the same pool.
            return rows;
        }
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("usages", out _)) return rows;

        var limits = JsonFields.Object(root, "limits");
        // No stable legacy window identifier has been verified. More than one
        // row could be reordered, so refuse to attach its history by position.
        if (limits.ValueKind != JsonValueKind.Array || limits.GetArrayLength() != 1) return rows;
        foreach (var limit in limits.EnumerateArray())
        {
            var source = Endpoint + "#legacy_0";
            var detail = JsonFields.Object(limit, "detail");
            if (detail.ValueKind != JsonValueKind.Object) continue;
            var total = JsonFields.Number(detail, "limit");
            var used = JsonFields.Number(detail, "used");
            double? percent = total is > 0 && used.HasValue ? used / total * 100 : null;
            if (percent > 100 || percent.HasValue && !double.IsFinite(percent.Value)) percent = null;
            rows.Add(new(source, "units", used, total, JsonFields.Number(detail, "remaining"),
                JsonFields.Date(detail, "resetTime"), WindowLabel: "Plan window", UsedPercent: percent));
        }
        return rows;
    }
}
