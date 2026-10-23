using System.Text.Json;
using Lain.Core;

namespace Lain.Host.Quota;

// Official user API, not the private Poe web application's endpoints.
public sealed class PoeQuotaAdapter(HttpClient client) : IQuotaAdapter
{
    public const string Endpoint = "https://api.poe.com/usage/current_balance";
    public string Source => Endpoint;
    public bool Supports(string provider) => provider.Equals("poe", StringComparison.OrdinalIgnoreCase);

    public async Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default)
    {
        if (account.CredentialKind != "api-key" || credential.BaseUrl?.TrimEnd('/') != "https://api.poe.com/v1")
            throw new QuotaFetchException("Poe balance requires a Poe API key with the official API base URL.");
        using var document = await QuotaHttp.GetAsync(client, Endpoint, credential, ct, userAgent: "lain/1.0");
        var readings = Normalize(document.RootElement);
        return new(readings, readings.Count == 0 ? "No recognized point balance; remaining points unknown." : null);
    }

    public static IReadOnlyList<QuotaReading> Normalize(JsonElement root)
    {
        // The documented balance includes both plan and add-on points. It does
        // not expose allocation, reset, expiry, or a split between those pools.
        if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("error", out _) || !root.TryGetProperty("current_point_balance", out var balance)
            || balance.ValueKind != JsonValueKind.Number || !balance.TryGetInt64(out var points) || points < 0 || points > 9_007_199_254_740_991)
            return [];
        return [new(Endpoint, "points", Remaining: points, WindowLabel: "Available points (plan and add-on)")];
    }
}
