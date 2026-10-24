using System.Text.Json;
using Lain.Core;

namespace Lain.Host.Quota;

public sealed class KiloQuotaAdapter(HttpClient client) : IQuotaAdapter
{
    public const string Product = "gateway";
    public const string ResourceType = "personal";
    public const string Endpoint = "https://api.kilo.ai/api/profile/balance";
    public string Source => Endpoint;
    public bool Supports(string provider) => provider.Equals("kilo", StringComparison.OrdinalIgnoreCase);

    internal static void ValidateCredential(QuotaCredential credential)
    {
        if (string.IsNullOrWhiteSpace(credential.BearerToken)) throw new QuotaFetchException("Kilo requires its own API key.");
        if (credential.BaseUrl is not null && credential.BaseUrl.TrimEnd('/') != "https://ai-gateway.kilo.ai/api/v1")
            throw new QuotaFetchException("Kilo requires its official Gateway API base URL.");
        if (credential.Settings.Any(p => p.Key.Contains("organization", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(p.Value)))
            throw new QuotaFetchException("Kilo organization quota is not qualified; personal scope required.");
    }

    public async Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default)
    {
        if (!Supports(account.Provider) || account.CredentialKind != "api-key") throw new QuotaFetchException("Kilo requires its own API key account.");
        ValidateCredential(credential);
        if (account.Binding is { } binding && (!binding.Provider.Equals("kilo", StringComparison.OrdinalIgnoreCase)
            || binding.Product is not null && binding.Product != Product
            || binding.ResourceType is not null && binding.ResourceType != ResourceType))
            throw new QuotaFetchException("Kilo quota requires its personal Gateway resource binding.");
        IReadOnlyList<QuotaReading> rows = [];
        KiloPassState? pass = null;
        var errors = new List<string>();
        try
        {
            using var doc = await QuotaHttp.GetAsync(client, Endpoint, credential, ct, userAgent: "lain/1.0");
            rows = Normalize(doc.RootElement);
            if (rows.Count == 0) errors.Add("Kilo wallet balance unknown.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { errors.Add("Kilo wallet request timed out."); }
        catch (Exception ex) when (ex is QuotaFetchException or HttpRequestException or JsonException or IOException or ArgumentException or FormatException)
        { errors.Add("Kilo wallet response unavailable or invalid."); }
        try { pass = await new KiloPassReader(client).ReadAsync(credential, ct); }
        catch (QuotaFetchException) { errors.Add("Kilo Pass response unavailable or invalid."); }
        // Independent failures preserve a valid wallet or Pass. No bonus addition or
        // subtraction of Pass usage from the independent wallet balance.
        return new(rows, errors.Count == 0 ? null : string.Join(" ", errors), KiloPass: pass);
    }

    public static IReadOnlyList<QuotaReading> Normalize(JsonElement root)
    {
        var balance = JsonFields.Number(root, "balance");
        return balance is null ? [] : [new(Endpoint, "USD", Remaining: balance,
            InheritAccountPool: false, WindowLabel: "Personal wallet balance", WindowId: "personal-wallet",
            MeasurementKind: "balance", Scope: "personal", SourceUrl: Endpoint)];
    }
}
