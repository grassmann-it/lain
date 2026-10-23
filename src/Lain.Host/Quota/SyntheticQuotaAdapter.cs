using System.Text.Json;
using Lain.Core;

namespace Lain.Host.Quota;

// Official schema: https://dev.synthetic.new/docs/synthetic/quotas (2026-10-24).
public sealed class SyntheticQuotaAdapter(HttpClient client) : IQuotaAdapter
{
    public const string Endpoint = "https://api.synthetic.new/v2/quotas";
    public string Source => Endpoint;
    public bool Supports(string provider) => provider.Equals("synthetic", StringComparison.OrdinalIgnoreCase);

    public async Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default)
    {
        if (!Supports(account.Provider) || account.CredentialKind != "api-key")
            throw new QuotaFetchException("Synthetic quota requires its own API key account.");
        // Custom gateway credentials must never leave their configured origin.
        if (credential.BaseUrl?.TrimEnd('/') is not ("https://api.synthetic.new/openai/v1" or
            "https://api.synthetic.new/anthropic/v1" or "https://api.synthetic.new/v1"))
            throw new QuotaFetchException("Synthetic quota requires an official API base URL.");
        using var doc = await QuotaHttp.GetAsync(client, Endpoint, credential, ct, userAgent: "lain/1.0");
        var rows = Normalize(doc.RootElement);
        return new(rows, rows.Count == 0 ? "No recognized subscription quota; remaining budget unknown." : null);
    }

    public static IReadOnlyList<QuotaReading> Normalize(JsonElement root)
    {
        var subscription = JsonFields.Object(root, "subscription");
        if (subscription.ValueKind != JsonValueKind.Object) return [];
        var limit = JsonFields.Number(subscription, "limit");
        var used = JsonFields.Number(subscription, "requests");
        // The endpoint calls this field requests, while current pricing describes
        // weighted credits. Preserve provider units; never label these as tokens
        // or promise a fixed number of ordinary requests.
        double? remaining = limit.HasValue && used.HasValue && used <= limit ? limit - used : null;
        double? percent = limit is > 0 && remaining.HasValue ? used / limit * 100 : null;
        if (!limit.HasValue && !used.HasValue) return [];
        return [new(Endpoint + "#subscription", "units", used, limit, remaining,
            JsonFields.Date(subscription, "renewsAt"), WindowLabel: "Subscription (provider units)", UsedPercent: percent)];
    }
}
