using System.Text.Json;
using Lain.Core;

namespace Lain.Host.Quota;

/// <summary>
/// Documented PAYG account balance, never Step Plan credits. No private console API.
/// Source: https://platform.stepfun.com/docs/zh/api-reference/accounts/get.md
/// </summary>
public sealed class StepFunQuotaAdapter(HttpClient client) : IQuotaAdapter
{
    public const string ApiBaseUrl = "https://api.stepfun.com/v1";
    public const string Endpoint = ApiBaseUrl + "/accounts";
    public string Source => Endpoint;
    public bool Supports(string provider) => provider.Equals("stepfun", StringComparison.OrdinalIgnoreCase);

    public async Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default)
    {
        if (account.CredentialKind != "api-key")
            throw new QuotaFetchException("StepFun PAYG balance requires an API key.");
        // A key may be shared by official API products, but their budgets are not.
        // Exact endpoint qualification prevents Plan and compatible gateway keys
        // from ever being sent to the PAYG account endpoint by this adapter.
        if (credential.BaseUrl?.TrimEnd('/') != ApiBaseUrl)
            throw new QuotaFetchException("StepFun balance is available only for the official PAYG API base URL; Step Plan credits are unsupported.");
        using var doc = await QuotaHttp.GetAsync(client, Endpoint, credential, ct, userAgent: "lain/1.0");
        var readings = Normalize(doc.RootElement);
        return new(readings, readings.Count == 0 ? "No recognized PAYG account balance; remaining balance unknown." : null);
    }

    public static IReadOnlyList<QuotaReading> Normalize(JsonElement root)
    {
        if (JsonFields.String(root, "object") != "account" || JsonFields.String(root, "type") is not ("prepaid" or "postpaid"))
            return [];
        var balance = JsonFields.Number(root, "balance");
        if (!balance.HasValue) return [];
        // The documented total_cash_balance and total_voucher_balance are not
        // spend limits; do not derive a percentage or duplicate the account pool.
        return [new(Endpoint, "CNY", Remaining: balance,
            WindowLabel: "PAYG account balance (not a spending limit)")];
    }
}
