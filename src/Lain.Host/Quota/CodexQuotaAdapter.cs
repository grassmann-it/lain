using Lain.Core;

namespace Lain.Host.Quota;

public sealed class CodexQuotaAdapter(HttpClient client) : IQuotaAdapter
{
    public const string Endpoint = "https://chatgpt.com/backend-api/wham/usage";
    public string Source => Endpoint;
    public bool Supports(string provider) => provider.Equals("codex", StringComparison.OrdinalIgnoreCase)
        || provider.Equals("openai", StringComparison.OrdinalIgnoreCase) || provider.Equals("gpt", StringComparison.OrdinalIgnoreCase);

    public async Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default)
    {
        if (account.CredentialKind is not ("codex-access-snapshot" or "codex-owned"))
            throw new QuotaFetchException("Codex quota requires a qualified access token; API-key quota is unknown.");
        using var doc = await QuotaHttp.GetAsync(client, Endpoint, credential, ct, codex: true);
        var limits = JsonFields.Object(doc.RootElement, "rate_limit");
        var rows = new List<QuotaReading>();
        foreach (var key in new[] { "primary_window", "secondary_window" })
        {
            var window = JsonFields.Object(limits, key);
            if (window.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
            var used = JsonFields.Number(window, "used_percent");
            if (used > 100) used = null;
            rows.Add(new(Endpoint + "#" + key, "percent", used, used.HasValue ? 100 : null,
                used.HasValue ? 100 - used : null, JsonFields.Date(window, "reset_at"),
                WindowLabel: JsonFields.Number(window, "limit_window_seconds") switch
                {
                    18000 => "5 hours", 604800 => "7 days",
                    { } seconds => $"{TimeSpan.FromSeconds(seconds).TotalHours:0.##}-hour window",
                    _ => key == "primary_window" ? "Primary window" : "Secondary window"
                }, UsedPercent: used));
        }
        return new(rows, rows.Count == 0 ? "No recognized quota windows; remaining budget unknown." : null);
    }
}
