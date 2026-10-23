using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Lain.Core;

namespace Lain.Host.Quota;

/// <summary>Reads the current key; never purchases credits or performs inference.</summary>
public sealed class OpenRouterQuotaAdapter(HttpClient client) : IQuotaAdapter
{
    public const string Endpoint = "https://openrouter.ai/api/v1/key";
    public string Source => Endpoint;
    public bool Supports(string provider) => provider.Equals("openrouter", StringComparison.OrdinalIgnoreCase);

    public async Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default)
    {
        if (account.CredentialKind != "api-key" || string.IsNullOrWhiteSpace(credential.BearerToken))
            throw new QuotaFetchException("OpenRouter quota requires an API key.");
        if (!IsOfficialBaseUrl(credential.BaseUrl))
            throw new QuotaFetchException("OpenRouter quota requires its official API base URL.");
        using var doc = await QuotaHttp.GetAsync(client, Endpoint, credential, ct, userAgent: "lain/0.1.0");
        var rows = Normalize(doc.RootElement);
        return new(rows, rows.Count == 0 ? "No recognized OpenRouter budgets; remaining budget unknown." : null);
    }

    private static bool IsOfficialBaseUrl(string? value) => value is null ||
        (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals("openrouter.ai", StringComparison.OrdinalIgnoreCase) && uri.IsDefaultPort
        && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
        && uri.AbsolutePath.TrimEnd('/') == "/api/v1");

    // Official key/limits documentation, checked 2026-10-24. No tier-to-balance inference:
    // is_free_tier does not identify the purchased-credit threshold or requests left.
    public static IReadOnlyList<QuotaReading> Normalize(JsonElement root)
    {
        var rows = new List<QuotaReading>();
        var data = JsonFields.Object(root, "data");
        if (data.ValueKind != JsonValueKind.Object) return rows;
        var daily = JsonFields.Object(data, "free_model_daily_requests");
        if (daily.ValueKind == JsonValueKind.Object)
            rows.Add(new(Endpoint + "#free-model-daily-requests", "requests", JsonFields.Number(daily, "used"),
                JsonFields.Number(daily, "limit"), JsonFields.Number(daily, "remaining"),
                InheritAccountPool: false, WindowLabel: "Free models · account · UTC day"));

        var reset = JsonFields.String(data, "limit_reset");
        var usageKey = reset switch { "daily" => "usage_daily", "weekly" => "usage_weekly", "monthly" => "usage_monthly", null => "usage", _ => null };
        double? capUsed = usageKey is null ? null : JsonFields.Number(data, usageKey);
        var includeByok = JsonFields.Object(data, "include_byok_in_limit");
        if (includeByok.ValueKind == JsonValueKind.True)
        {
            var externalUsed = usageKey is null ? null : JsonFields.Number(data, "byok_" + usageKey);
            capUsed = capUsed is not null && externalUsed is not null ? capUsed + externalUsed : null;
        }
        else if (includeByok.ValueKind != JsonValueKind.False) capUsed = null;
        // This cap belongs to the key, not its billing account. Null means uncapped,
        // never a zero balance; all-time spend is not current-window spend.
        if (JsonFields.Number(data, "limit") is not null || SignedNumber(data, "limit_remaining") is not null)
            rows.Add(new(Endpoint + "#key-spend-cap", "usd", capUsed,
                JsonFields.Number(data, "limit"), SignedNumber(data, "limit_remaining"),
                InheritAccountPool: false, WindowLabel: "API key spend cap"));

        var budgets = JsonFields.Object(data, "budgets");
        if (budgets.ValueKind == JsonValueKind.Array)
        {
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var budget in budgets.EnumerateArray())
            {
                // Keep every overlapping constraint separate. Provider identifiers and
                // labels are not copied into display strings or used to invent pools.
                if (budget.ValueKind != JsonValueKind.Object) continue;
                var scope = JsonFields.String(budget, "scope");
                if (scope is not ("workspace_default_member" or "workspace_default_key" or "member" or "api_key" or "workspace")) continue;
                var identity = string.Join("|", scope, JsonFields.String(budget, "guardrail_id"),
                    JsonFields.String(budget, "budget_id"), JsonFields.String(budget, "reset_interval"));
                var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
                var occurrence = occurrences.GetValueOrDefault(hash) + 1;
                occurrences[hash] = occurrence;
                rows.Add(new(Endpoint + "#budget/" + hash + "/" + occurrence, "usd", JsonFields.Number(budget, "spend_usd"),
                    JsonFields.Number(budget, "limit_usd"), SignedNumber(budget, "remaining_usd"),
                    JsonFields.Date(budget, "resets_at"), InheritAccountPool: false,
                    WindowLabel: scope switch
                    {
                        "workspace_default_member" => "Workspace default · member spend cap",
                        "workspace_default_key" => "Workspace default · key spend cap",
                        "member" => "Member spend cap",
                        "api_key" => "API key guardrail spend cap",
                        _ => "Workspace spend cap"
                    }));
            }
        }
        // effective_budget duplicates one of budgets; do not present it as extra money.
        return rows;
    }

    private static double? SignedNumber(JsonElement root, string key)
    {
        var field = JsonFields.Object(root, key);
        return field.ValueKind == JsonValueKind.Number && field.TryGetDouble(out var value) && double.IsFinite(value) ? value : null;
    }
}
