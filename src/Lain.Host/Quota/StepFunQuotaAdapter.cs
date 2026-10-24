using System.Text.Json;
using Lain.Core;

namespace Lain.Host.Quota;

/// <summary>
/// Documented PAYG account balance, never Step Plan credits. No private console API.
/// Source: https://platform.stepfun.com/docs/zh/api-reference/accounts/get.md
/// </summary>
public sealed class StepFunQuotaAdapter(HttpClient client) : IQuotaAdapter
{
    public const string Product = "payg";
    public const string ResourceType = "personal";
    public const string Region = "com";
    public const string ApiBaseUrl = "https://api.stepfun.com/v1";
    public const string Endpoint = ApiBaseUrl + "/accounts";
    public string Source => Endpoint;
    public bool Supports(string provider) => provider.Equals("stepfun", StringComparison.OrdinalIgnoreCase);

    public async Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default)
    {
        if (!Supports(account.Provider) || account.CredentialKind != "api-key" || string.IsNullOrWhiteSpace(credential.BearerToken))
            throw new QuotaFetchException("StepFun PAYG balance requires an API key.");
        // A key may be shared by official API products, but their budgets are not.
        // Exact endpoint qualification prevents Plan and compatible gateway keys
        // from ever being sent to the PAYG account endpoint by this adapter.
        if (credential.BaseUrl?.TrimEnd('/') != ApiBaseUrl)
            throw new QuotaFetchException("StepFun balance is available only for the official PAYG API base URL; Step Plan credits are unsupported.");
        if (account.Binding is { } binding && (!binding.Provider.Equals("stepfun", StringComparison.OrdinalIgnoreCase)
            || binding.Product is not null && binding.Product != Product
            || binding.ResourceType is not null && binding.ResourceType != ResourceType
            || binding.Region is not null && binding.Region != Region))
            throw new QuotaFetchException("StepFun balance requires its personal PAYG resource binding.");
        if (credential.Settings.Any(p => (p.Key.Contains("organization", StringComparison.OrdinalIgnoreCase)
            || p.Key.Contains("project", StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrWhiteSpace(p.Value)))
            throw new QuotaFetchException("StepFun organization or project balance is not qualified; personal PAYG scope required.");
        try
        {
            using var doc = await QuotaHttp.GetAsync(client, Endpoint, credential, ct, userAgent: "lain/1.0");
            var readings = Normalize(doc.RootElement);
            return new(readings, readings.Count == 0 ? "No recognized PAYG account balance; remaining balance unknown." : null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new QuotaFetchException("StepFun balance request timed out."); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or ArgumentException or FormatException)
        { throw new QuotaFetchException("StepFun balance response unavailable or invalid."); }
    }

    public static IReadOnlyList<QuotaReading> Normalize(JsonElement root)
    {
        if (JsonFields.String(root, "object") != "account" || JsonFields.String(root, "type") is not ("prepaid" or "postpaid"))
            return [];
        // Never relabel a declared organization/project response as personal cash.
        if (JsonFields.Object(root, "organization_id").ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null)
            || JsonFields.Object(root, "project_id").ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null)
            || JsonFields.String(root, "scope") is { } scope && scope is not ("personal" or "account")) return [];
        var balance = JsonFields.Number(root, "balance");
        if (!balance.HasValue) return [];
        // The documented total_cash_balance and total_voucher_balance are not
        // spend limits; do not derive a percentage or duplicate the account pool.
        return [new(Endpoint, "CNY", Remaining: balance,
            InheritAccountPool: false, WindowLabel: "Personal PAYG " + JsonFields.String(root, "type") + " balance (not a spending limit)",
            WindowId: "personal-payg-" + JsonFields.String(root, "type"), MeasurementKind: "balance", Scope: "personal", SourceUrl: Endpoint)];
    }
}
