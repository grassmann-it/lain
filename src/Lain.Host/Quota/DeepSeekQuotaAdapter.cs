using System.Text.Json;
using Lain.Core;

namespace Lain.Host.Quota;

/// <summary>Same-key user balance; native currencies and exact decimal strings stay independent.</summary>
public sealed class DeepSeekQuotaAdapter(HttpClient client) : IQuotaAdapter
{
    public const string ApiBaseUrl = "https://api.deepseek.com";
    public const string Endpoint = ApiBaseUrl + "/user/balance";
    public string Source => Endpoint;
    public bool Supports(string provider) => provider.Equals("deepseek", StringComparison.OrdinalIgnoreCase);

    public async Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default)
    {
        if (!Supports(account.Provider) || (credential.CredentialKind ?? account.CredentialKind) != "api-key"
            || string.IsNullOrWhiteSpace(credential.BearerToken))
            throw new QuotaFetchException("DeepSeek balance requires its own API key account.");
        // Never send an arbitrary compatible gateway's key to DeepSeek's balance service.
        if (credential.BaseUrl?.TrimEnd('/') != ApiBaseUrl)
            throw new QuotaFetchException("DeepSeek balance requires the official DeepSeek API base URL.");
        // /user/balance selects the user through the key and returns no subject/resource ID.
        // An asserted identity cannot be verified by this endpoint.
        if (account.Binding is { } binding && (!Supports(binding.Provider) || binding.Subject is not null
            || binding.ResourceId is not null || binding.Product is not null || binding.Region is not null
            || binding.ResourceType is not null)
            || credential.ProviderAccountId is not null
            || credential.Settings.Any(setting => setting.Key != "billingPlan" || setting.Value != "payg"))
            throw new QuotaFetchException("DeepSeek balance does not support a selected account, product, region or resource binding.");
        try
        {
            using var doc = await QuotaHttp.GetAsync(client, Endpoint, credential, ct, userAgent: "lain/1.0");
            return Parse(doc.RootElement);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        { throw new QuotaFetchException(QuotaFetchException.SafeDiagnostic(ex)); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or ArgumentException or FormatException)
        { throw new QuotaFetchException(QuotaFetchException.SafeDiagnostic(ex)); }
    }

    public static IReadOnlyList<QuotaReading> Normalize(JsonElement root) => Parse(root).Readings;

    private static QuotaFetchResult Parse(JsonElement root)
    {
        var balances = JsonFields.Object(root, "balance_infos");
        if (balances.ValueKind != JsonValueKind.Array)
            return new([], "DeepSeek user balance unknown (invalid-response).");
        var rows = new List<QuotaReading>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = new HashSet<string>(StringComparer.Ordinal);
        var invalid = false;
        foreach (var balance in balances.EnumerateArray())
        {
            var currency = JsonFields.String(balance, "currency");
            if (currency is not ("CNY" or "USD")) { invalid = true; continue; }
            if (!seen.Add(currency)) { duplicates.Add(currency); invalid = true; }
            var exact = ExactDecimal(JsonFields.Object(balance, "total_balance"));
            if (exact is null) { invalid = true; continue; }
            // total_balance already includes granted and topped-up funds. Do not add its
            // components again, infer a spending limit, or round money through double.
            rows.Add(new(Endpoint, currency, InheritAccountPool: false,
                WindowLabel: "User balance", WindowId: "user-balance-" + currency.ToLowerInvariant(),
                MeasurementKind: "balance", Scope: "account", SourceUrl: Endpoint,
                ExactRemaining: exact));
        }
        rows.RemoveAll(row => duplicates.Contains(row.Unit));
        return new(rows, invalid ? "DeepSeek user balance partially unknown (invalid-response)."
            : rows.Count == 0 ? "DeepSeek user balance unknown (no reported balances)." : null);
    }

    private static string? ExactDecimal(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 and <= 256 } text)
            return null;
        var index = text[0] == '-' ? 1 : 0;
        var integerStart = index;
        while (index < text.Length && char.IsAsciiDigit(text[index])) index++;
        if (index == integerStart) return null;
        if (index == text.Length) return text;
        if (text[index++] != '.') return null;
        var fractionStart = index;
        while (index < text.Length && char.IsAsciiDigit(text[index])) index++;
        return index == text.Length && index > fractionStart ? text : null;
    }
}
