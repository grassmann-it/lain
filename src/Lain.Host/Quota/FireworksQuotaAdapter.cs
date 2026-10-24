using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lain.Core;

namespace Lain.Host.Quota;

/// <summary>Reads account constraints only; never infers prepaid balance or performs inference.</summary>
public sealed class FireworksQuotaAdapter(HttpClient client, TimeProvider? timeProvider = null) : IQuotaAdapter
{
    public const string ApiBaseUrl = "https://api.fireworks.ai/inference/v1";
    public const string QuotaBaseUrl = "https://api.fireworks.ai/v1/accounts/";
    public string Source => "https://docs.fireworks.ai/api-reference/list-quotas";
    public bool Supports(string provider) => provider.Equals("fireworks", StringComparison.OrdinalIgnoreCase);

    public async Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!Supports(account.Provider) || account.CredentialKind != "api-key" || string.IsNullOrWhiteSpace(credential.BearerToken))
            throw new QuotaFetchException("Fireworks quota requires an API-key account.");
        if (!IsOfficialBaseUrl(credential.BaseUrl))
            throw new QuotaFetchException("Fireworks quota requires its official serverless API base URL.");
        var monitor = credential.Monitor;
        if (monitor is not null && monitor.Kind is not ("inference-key" or "api-token"))
            return new([], Support: new("setup-required", "Fireworks quota requires an authorized Fireworks API key.", Source, "quota"));
        if (monitor?.ExpiresAt <= (timeProvider ?? TimeProvider.System).GetUtcNow())
            throw new QuotaFetchException("Fireworks monitor credential expired; reconnect the monitor.");
        var bearerToken = monitor?.Kind == "api-token" ? monitor.Secret : credential.BearerToken;
        if (string.IsNullOrWhiteSpace(bearerToken))
            return new([], Support: new("setup-required", "Fireworks monitor API key is missing.", Source, "quota"));
        var accountId = monitor?.ResourceId ?? credential.Settings.GetValueOrDefault("accountId");
        if (!ValidId(accountId))
            return new([], Support: new("setup-required", "Enter the Fireworks account ID to read account quotas.", Source, "quota"));
        var httpCredential = new QuotaCredential(bearerToken);
        var endpoint = QuotaBaseUrl + accountId + "/quotas?pageSize=200";
        var rows = new List<QuotaReading>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var pages = new HashSet<string>(StringComparer.Ordinal);
        string? pageToken = null;
        string? diagnostic = null;
        // Bounds total work independently of the provider's pagination metadata.
        for (var page = 0; page < 10; page++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var document = await QuotaHttp.GetAsync(client,
                    endpoint + (pageToken is null ? "" : "&pageToken=" + Uri.EscapeDataString(pageToken)), httpCredential, ct, userAgent: "lain/1.0");
                var root = document.RootElement;
                var quotas = JsonFields.Object(root, "quotas");
                if (quotas.ValueKind != JsonValueKind.Array)
                    throw new QuotaFetchException("Fireworks quota response has no quota list.");
                foreach (var quota in quotas.EnumerateArray())
                {
                    var name = JsonFields.String(quota, "name");
                    if (name is null || !name.StartsWith("accounts/" + accountId + "/quotas/", StringComparison.Ordinal)
                        || !ValidId(name[("accounts/" + accountId + "/quotas/").Length..])
                        || name.Contains(credential.BearerToken, StringComparison.Ordinal)
                        || name.Contains(bearerToken, StringComparison.Ordinal))
                    {
                        diagnostic = "Fireworks quota response contained an invalid or mismatched account scope.";
                        continue;
                    }
                    if (!names.Add(name))
                    {
                        diagnostic = "Fireworks quota response contained duplicate quota resources.";
                        continue;
                    }
                    var reading = Normalize(quota, accountId!);
                    if (reading is not null) rows.Add(reading);
                    else diagnostic = "Some Fireworks quotas contained no recognized quantities.";
                }
                var tokenField = JsonFields.Object(root, "nextPageToken");
                if (tokenField.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) break;
                if (tokenField.ValueKind != JsonValueKind.String)
                    throw new QuotaFetchException("Fireworks quota pagination is malformed.");
                pageToken = tokenField.GetString();
                if (string.IsNullOrEmpty(pageToken)) break;
                if (pageToken.Length > 4096 || !pages.Add(pageToken))
                    throw new QuotaFetchException("Fireworks quota pagination is invalid or repeated.");
                if (page == 9) diagnostic = "Fireworks quota pagination limit reached; results are partial.";
            }
            catch (QuotaFetchException ex) when (rows.Count > 0)
            {
                diagnostic = ex.Message;
                break;
            }
            catch (JsonException) when (rows.Count > 0)
            {
                diagnostic = "Fireworks quota response was malformed; results are partial.";
                break;
            }
            catch (HttpRequestException) when (rows.Count > 0)
            {
                diagnostic = "Fireworks quota transport failed; results are partial.";
                break;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && rows.Count > 0)
            {
                diagnostic = "Fireworks quota request timed out; results are partial.";
                break;
            }
        }
        return new(rows, diagnostic ?? (rows.Count == 0 ? "No Fireworks quota quantities were reported; remaining budget is unknown." : null),
            Support: new("api", "Official account quotas; prepaid credit balance and usage measurement time are unavailable.", Source, "quota"));
    }

    public static QuotaReading? Normalize(JsonElement quota, string accountId)
    {
        var name = JsonFields.String(quota, "name");
        var prefix = "accounts/" + accountId + "/quotas/";
        if (!ValidId(accountId) || name is null || !name.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var id = name[prefix.Length..];
        if (!ValidId(id)) return null;
        var limit = IntegerQuantity(quota, "value");
        var used = JsonFields.Number(quota, "usage");
        var approvedLimit = IntegerQuantity(quota, "maxValue");
        if (limit is null && used is null && approvedLimit is null) return null;
        // Only this exact ID has a documented denomination. Other constraints retain
        // their original identity and raw quantities without invented units or remainder.
        var monthlySpend = id == "monthly-spend-usd";
        return new("https://api.fireworks.ai/v1/" + name, monthlySpend ? "USD" : "unknown", used, limit,
            monthlySpend && limit.HasValue && used.HasValue ? limit.Value - used.Value : null,
            InheritAccountPool: false,
            WindowLabel: monthlySpend ? "Account monthly spend constraint · not credit balance" : "Account quota · " + id + " · unit unspecified",
            WindowId: id, MeasurementKind: monthlySpend ? "quota" : "limit", Scope: "account", ScopeId: accountId,
            SourceUrl: "https://docs.fireworks.ai/api-reference/get-quota", ProviderUpdatedAt: JsonFields.Date(quota, "updateTime"),
            ApprovedLimit: approvedLimit);
    }

    private static double? IntegerQuantity(JsonElement root, string key)
    {
        var value = JsonFields.Object(root, key);
        // JSON int64 strings are not doubles: reject fractional values, infinity and
        // values that cannot be represented exactly by the public numeric contract.
        long parsed = 0;
        var valid = value.ValueKind == JsonValueKind.String
            ? long.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out parsed)
            : value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out parsed);
        return valid && parsed >= 0 && parsed <= 9_007_199_254_740_991L ? parsed : null;
    }

    private static bool ValidId(string? value) => value is not null && Regex.IsMatch(value, "\\A[a-zA-Z0-9][a-zA-Z0-9_-]{0,127}\\z", RegexOptions.CultureInvariant);
    private static bool IsOfficialBaseUrl(string? value) => value is null ||
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals("api.fireworks.ai", StringComparison.OrdinalIgnoreCase) && uri.IsDefaultPort
        && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
        && uri.AbsolutePath.TrimEnd('/') == "/inference/v1";
}
