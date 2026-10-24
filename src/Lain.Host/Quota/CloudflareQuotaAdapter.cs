using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts.Cloud;

namespace Lain.Host.Quota;

/// <summary>Opt-in qualified candidate telemetry. Adaptive analytics never implies an exact free allowance balance.</summary>
public sealed class CloudflareQuotaAdapter : IQuotaAdapter, IDisposable
{
    public const string Documentation = "https://developers.cloudflare.com/analytics/graphql-api/sampling/";
    public const string ConsoleUrl = "https://dash.cloudflare.com/";
    private readonly CloudProviderHttp http;
    private readonly TimeProvider clock;
    private readonly bool candidateQualified;
    public CloudflareQuotaAdapter(bool candidateQualified = false, HttpMessageHandler? handler = null, TimeProvider? clock = null)
    { this.candidateQualified = candidateQualified; http = new(handler); this.clock = clock ?? TimeProvider.System; }
    public string Source => "cloudflare:workers-ai:adaptive-neurons";
    public bool Supports(string provider) => provider == CloudResourceDiscovery.CloudflareProvider;

    public async Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default)
    {
        if (!candidateQualified) return Unavailable("Workers AI adaptive-neuron schema, account entitlement and source coverage have not yet been qualified. Existing credentials remain usable for their separately qualified purposes.");
        var accountId = credential.Monitor?.ResourceId ?? credential.Settings.GetValueOrDefault("accountId");
        var access = credential.Monitor?.Secret ?? credential.BearerToken;
        var now = clock.GetUtcNow();
        if (!CloudResourceDiscovery.CloudflareAccount(accountId) || string.IsNullOrWhiteSpace(access)
            || credential.Monitor?.ExpiresAt is { } expiry && expiry <= now
            || credential.ExpiresAt is { } bearerExpiry && credential.Monitor is null && bearerExpiry <= now
            || account.Binding?.ResourceId is { } pinned && pinned != accountId
            || credential.Settings.GetValueOrDefault("accountId") is { } configured && configured != accountId)
            return new([], Support: new("setup-required", "Bind a valid monitoring grant to the same Cloudflare account as this connection.", ConsoleUrl, "quota"));
        var start = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        // No dimensions: one aggregate, not a truncated top-N model list.
        var query = "{viewer{accounts(filter:{accountTag:" + JsonSerializer.Serialize(accountId) + "}){accountTag aiInferenceAdaptiveGroups(limit:1,filter:{datetime_geq:"
            + JsonSerializer.Serialize(start.ToString("O", CultureInfo.InvariantCulture)) + ",datetime_lt:" + JsonSerializer.Serialize(now.ToString("O", CultureInfo.InvariantCulture)) + "}){sum{totalNeurons}}}}}";
        using var request = CloudProviderHttp.Get("https://api.cloudflare.com/client/v4/graphql", access);
        request.Method = HttpMethod.Post; request.Content = JsonContent.Create(new { query });
        try
        {
            using var document = await http.ReadAsync(request, ct);
            return Normalize(document.RootElement, accountId!, start, now);
        }
        catch (CloudProviderException error) when (error.Status is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        { return Unavailable("Workers AI analytics is not permitted or available for this account. Review the dataset grant; no new token or broader permission is requested automatically."); }
        catch (CloudProviderException error) { throw new QuotaFetchException(error.Message); }
    }
    public static QuotaFetchResult Normalize(JsonElement root, string accountId, DateTimeOffset start, DateTimeOffset observed)
    {
        if (!CloudResourceDiscovery.CloudflareAccount(accountId) || start >= observed) throw new ArgumentException("Invalid Cloudflare usage selection.");
        var errors = JsonFields.Object(root, "errors");
        if (errors.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null) && (errors.ValueKind != JsonValueKind.Array || errors.GetArrayLength() > 0))
            return Unavailable("Workers AI analytics reported a dataset or permission error. Partial GraphQL data is not treated as complete usage.");
        var accounts = JsonFields.Object(JsonFields.Object(JsonFields.Object(root, "data"), "viewer"), "accounts");
        if (accounts.ValueKind != JsonValueKind.Array || accounts.GetArrayLength() != 1 || JsonFields.String(accounts[0], "accountTag") != accountId)
            return Unavailable("Workers AI analytics did not return the selected account.");
        var groups = JsonFields.Object(accounts[0], "aiInferenceAdaptiveGroups");
        if (groups.ValueKind != JsonValueKind.Array || groups.GetArrayLength() != 1)
            return Unavailable("No single account-wide neuron aggregate was returned. Missing or grouped data does not prove zero usage.");
        var neurons = JsonFields.Number(JsonFields.Object(groups[0], "sum"), "totalNeurons");
        if (neurons is null || neurons > 9007199254740991d) return Unavailable("The neuron aggregate was absent or invalid.");
        return new([new("cloudflare:workers-ai:adaptive-neurons", "neurons", Used: neurons,
            BudgetPoolId: "cloudflare-account:" + accountId, InheritAccountPool: false,
            WindowLabel: "Reported/estimated neurons · UTC day · coverage and freshness unconfirmed", WindowId: "utc-day-adaptive-neurons",
            MeasurementKind: "usage-estimate", Scope: "account-workers-ai", ScopeId: accountId, SourceUrl: Documentation,
            ObservedAt: observed, WindowStart: start, WindowEnd: observed)],
            Support: new("api", "Adaptive analytics can be sampled. Source coverage and freshness are not established; no exact remaining free allowance or account balance is derived.", ConsoleUrl));
    }
    private static QuotaFetchResult Unavailable(string message) => new([], Support: new("unavailable", message, ConsoleUrl));
    public void Dispose() => http.Dispose();
}
