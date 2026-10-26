using System.Text.Json;
using Lain.Core;
using Lain.Host.Providers.NanoGpt;

namespace Lain.Host.Quota;

/// <summary>Personal subscription windows from a separate usage:read management token.</summary>
public sealed class NanoGptQuotaAdapter(HttpClient client, TimeProvider? clock = null) : IQuotaAdapter
{
    public const string Endpoint = "https://nano-gpt.com/api/management/v1/subscription/usage";
    public const string DocumentationUrl = "https://docs.nano-gpt.com/api-reference/management-api";
    public const string TokenSettingsUrl = "https://nano-gpt.com/settings#management-api-tokens";
    public string Source => Endpoint;
    public bool Supports(string provider) => provider.Equals("nanogpt", StringComparison.OrdinalIgnoreCase);
    public bool AllowsMissingCredential => true;

    public async Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!Supports(account.Provider)) throw new QuotaFetchException("NanoGPT subscription monitoring requires its own provider account.");
        var monitor = credential.Monitor;
        if (monitor is null || monitor.Kind != "management-token")
            return new([], Support: new("setup-required", "Add a separate NanoGPT Usage only management token (usage:read) to read subscription quotas.", TokenSettingsUrl, "quota"));
        if (!string.IsNullOrEmpty(monitor.ResourceId))
            throw new QuotaFetchException("NanoGPT personal subscription monitoring does not accept an account selector.");
        if (!NanoGptCredentialPolicy.IsManagementToken(monitor.Secret))
            throw new QuotaFetchException("NanoGPT subscription monitoring requires a management token, not an inference key.");
        if (monitor.ExpiresAt <= (clock ?? TimeProvider.System).GetUtcNow())
            throw new QuotaFetchException("NanoGPT management token expired; reconnect the monitor.");
        using var response = await QuotaHttp.GetAsync(client, Endpoint, new QuotaCredential(monitor.Secret!), ct, userAgent: "lain/1.0");
        return Normalize(response.RootElement);
    }

    public static QuotaFetchResult Normalize(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || JsonFields.Object(root, "active").ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || JsonFields.String(root, "state") is not ("active" or "grace" or "inactive"))
            throw new QuotaFetchException("NanoGPT subscription response is invalid.");
        var rows = new List<QuotaReading>();
        var unknown = false;
        var limits = JsonFields.Object(root, "limits");
        Add("dailyInputTokens", "input_tokens", "Daily input tokens", Quantity(limits, "dailyInputTokens"));
        Add("weeklyInputTokens", "input_tokens", "Weekly input tokens", Quantity(limits, "weeklyInputTokens"));
        Add("dailyImages", "images", "Daily images", Quantity(limits, "dailyImages"));
        if (JsonFields.String(root, "usageUnits") == "tokens")
            Add("tokens", "tokens", "Trial tokens", Quantity(JsonFields.Object(root, "tokenLimits"), "total"));
        var state = JsonFields.String(root, "state");
        var active = JsonFields.Object(root, "active").ValueKind == JsonValueKind.True;
        var status = active ? state == "grace" ? "Subscription grace period." : "Subscription active." : "Subscription inactive.";
        return new(rows, unknown ? "Some NanoGPT subscription counters are unknown or degraded." : null,
            new("api", status + " Personal subscription of the management token owner; association with an inference key is unverified. No cash balance or model entitlement is inferred.", DocumentationUrl, "quota"));

        void Add(string id, string unit, string label, double? limit)
        {
            var quota = JsonFields.Object(root, id);
            // Null means not configured/applicable. A configured window with unavailable
            // counters retains its known limit but never fabricates remaining allowance.
            if (quota.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                if (limit is not null) unknown = true;
                return;
            }
            if (quota.ValueKind != JsonValueKind.Object) { unknown = true; return; }
            var degradedField = JsonFields.Object(quota, "degraded");
            var degraded = degradedField.ValueKind is not (JsonValueKind.False or JsonValueKind.Undefined);
            var used = degraded ? null : Quantity(quota, "used");
            var remaining = degraded ? null : Quantity(quota, "remaining");
            var fraction = degraded ? null : Quantity(quota, "percentUsed");
            var percent = fraction * 100;
            if (percent is { } number && !double.IsFinite(number)) percent = null;
            if (degraded || used is null || remaining is null || percent is null) unknown = true;
            rows.Add(new(Endpoint + "#" + id, unit, used, limit, remaining, Reset(quota),
                InheritAccountPool: false, WindowLabel: label, UsedPercent: percent, WindowId: id,
                MeasurementKind: "quota", Scope: "account", SourceUrl: Endpoint));
        }
    }

    // The published contract uses JSON numbers, not numeric strings. Preserve unknown
    // fields individually; never derive a missing remainder/percentage from other values.
    private static double? Quantity(JsonElement node, string name)
    {
        var value = JsonFields.Object(node, name);
        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) && number >= 0 ? number : null;
    }
    private static DateTimeOffset? Reset(JsonElement quota)
    {
        var value = JsonFields.Object(quota, "resetAt");
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var milliseconds) || milliseconds <= 0) return null;
        try { return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds); }
        catch (ArgumentOutOfRangeException) { return null; }
    }
}
