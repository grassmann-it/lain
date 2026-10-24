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
        // The pinned manufacturer client rewrites the legacy API-app gateway URL
        // to the same personal gateway (api/constants.ts, resolveKiloAiGatewayBase).
        if (credential.BaseUrl is not null && credential.BaseUrl.TrimEnd('/') is not
            ("https://ai-gateway.kilo.ai/api/v1" or "https://api.kilo.ai/api/gateway/v1"))
            throw new QuotaFetchException("Kilo requires its official Gateway API base URL.");
        if (credential.Settings.Any(p => p.Key.Contains("organization", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(p.Value)))
            throw new QuotaFetchException("Kilo organization quota is not qualified; personal scope required.");
    }

    public async Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default)
    {
        if (!Supports(account.Provider) || (credential.CredentialKind ?? account.CredentialKind) != "api-key") throw new QuotaFetchException("Kilo requires its own API key account.");
        ValidateCredential(credential);
        if (account.Binding is { } binding && (!binding.Provider.Equals("kilo", StringComparison.OrdinalIgnoreCase)
            || binding.Product is not null && binding.Product != Product
            || binding.ResourceType is not null && binding.ResourceType != ResourceType))
            throw new QuotaFetchException("Kilo quota requires its personal Gateway resource binding.");
        async Task<(IReadOnlyList<QuotaReading> Rows, string? Error)> Wallet()
        {
            try
            {
                using var doc = await QuotaHttp.GetAsync(client, Endpoint, credential, ct, userAgent: "lain/1.0");
                var rows = Normalize(doc.RootElement);
                return (rows, rows.Count == 0 ? "Kilo wallet balance unknown." : null);
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            { return ([], "Kilo wallet unavailable. " + QuotaFetchException.SafeDiagnostic(ex)); }
            catch (Exception ex) when (ex is QuotaFetchException or HttpRequestException or JsonException or IOException or ArgumentException or FormatException)
            { return ([], "Kilo wallet unavailable. " + QuotaFetchException.SafeDiagnostic(ex)); }
        }
        async Task<(KiloPassState? Value, string? Error)> Pass()
        {
            try { return (await new KiloPassReader(client).ReadAsync(credential, ct), null); }
            catch (QuotaFetchException ex) { return (null, "Kilo Pass unavailable. " + QuotaFetchException.SafeDiagnostic(ex)); }
        }
        // Independent fixed read endpoints run together, so a slow Pass cannot
        // consume a second full request timeout after the wallet finishes.
        var walletTask = Wallet();
        var passTask = Pass();
        await Task.WhenAll(walletTask, passTask);
        var wallet = await walletTask;
        var pass = await passTask;
        var errors = new[] { wallet.Error, pass.Error }.Where(error => error is not null).ToArray();
        // Never add Pass credits to the independent wallet balance.
        return new(wallet.Rows, errors.Length == 0 ? null : string.Join(" ", errors), KiloPass: pass.Value,
            KiloPassOutcome: new(pass.Error is null, pass.Error));
    }

    public static IReadOnlyList<QuotaReading> Normalize(JsonElement root)
    {
        var balance = JsonFields.Number(root, "balance");
        return balance is null ? [] : [new(Endpoint, "USD", Remaining: balance,
            InheritAccountPool: false, WindowLabel: "Personal wallet balance", WindowId: "personal-wallet",
            MeasurementKind: "balance", Scope: "personal", SourceUrl: Endpoint)];
    }
}
