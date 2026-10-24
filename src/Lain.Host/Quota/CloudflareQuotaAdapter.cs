using System.Net;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts.Cloud;

namespace Lain.Host.Quota;

/// <summary>Live account-scoped Workers AI telemetry. Adaptive analytics is not an exact free allowance balance.</summary>
public sealed class CloudflareQuotaAdapter : IQuotaAdapter, IDisposable
{
    public const string Documentation = "https://developers.cloudflare.com/analytics/graphql-api/sampling/";
    public const string ConsoleUrl = "https://dash.cloudflare.com/?to=/:account/ai/workers-ai";
    public const string AnalyticsPermission = "Account > Account Analytics > Read";
    private readonly CloudProviderHttp http;
    private readonly CloudflareWorkersAiReader reader;
    private readonly TimeProvider clock;
    // Retain the old constructor shape for integrations. Source availability is now established by
    // the actual account-scoped response, not by a global maintainer qualification switch.
    public CloudflareQuotaAdapter(bool candidateQualified = false, HttpMessageHandler? handler = null, TimeProvider? clock = null)
    { http = new(handler); reader = new(http); this.clock = clock ?? TimeProvider.System; }
    public string Source => "cloudflare:workers-ai:adaptive-neurons";
    public bool Supports(string provider) => provider == CloudResourceDiscovery.CloudflareProvider;

    public async Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default)
    {
        var accountId = credential.Monitor?.ResourceId ?? credential.Settings.GetValueOrDefault("accountId") ?? account.Binding?.ResourceId;
        var access = credential.Monitor?.Secret ?? credential.BearerToken;
        var now = clock.GetUtcNow();
        if (!CloudResourceDiscovery.CloudflareAccount(accountId) || string.IsNullOrWhiteSpace(access)
            || access.Length > 32768 || access.Any(char.IsWhiteSpace) || access.Any(char.IsControl)
            || credential.Monitor is { Secret: null or "" }
            || credential.Monitor?.ExpiresAt is { } expiry && expiry <= now
            || credential.ExpiresAt is { } bearerExpiry && credential.Monitor is null && bearerExpiry <= now
            || account.Binding?.ResourceId is { } pinned && pinned != accountId
            || credential.Settings.GetValueOrDefault("accountId") is { } configured && configured != accountId)
            return Setup("Bind an unexpired monitoring grant to the same Cloudflare account as this connection. An existing API token with " + AnalyticsPermission + " can be used; a separate read token is optional.");
        try { return await reader.ReadAsync(access, accountId!, now, ct); }
        catch (CloudProviderException error) when (error.Status == HttpStatusCode.Unauthorized)
        { return Failure("Cloudflare rejected this analytics credential. Reconnect or replace the monitoring access for this account.", "setup-required"); }
        catch (CloudProviderException error) when (error.Status == HttpStatusCode.Forbidden)
        { return Failure("Cloudflare denied analytics access. Grant " + AnalyticsPermission + " for the selected account to the existing API token, or attach a separate read token.", "setup-required"); }
        catch (CloudProviderException error) when (error.Status == HttpStatusCode.NotFound)
        { return Unavailable("Workers AI analytics is not available for the selected account. Check its Workers AI dashboard and account selection."); }
        catch (CloudProviderException error) { throw new QuotaFetchException(error.Message); }
        catch (Exception error) when (error is JsonException or InvalidDataException or HttpRequestException)
        { throw new QuotaFetchException("Cloudflare analytics could not be read. Retry later; no usage or balance was inferred."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new QuotaFetchException("Cloudflare analytics timed out. Retry later."); }
    }

    public static QuotaFetchResult Normalize(JsonElement root, string accountId, DateTimeOffset start, DateTimeOffset observed)
    {
        if (!CloudResourceDiscovery.CloudflareAccount(accountId) || start != CloudflareWorkersAiReader.UtcDayStart(observed) || start >= observed)
            throw new ArgumentException("Invalid Cloudflare usage selection.");
        if (CloudflareWorkersAiReader.HasGraphQlErrors(root))
            return Failure("Cloudflare reported a GraphQL dataset or permission error. Check " + AnalyticsPermission + " for this account. Partial data is not complete usage.", "unavailable");
        var accounts = JsonFields.Object(JsonFields.Object(JsonFields.Object(root, "data"), "viewer"), "accounts");
        if (accounts.ValueKind != JsonValueKind.Array || accounts.GetArrayLength() != 1 || JsonFields.String(accounts[0], "accountTag") != accountId)
            return Failure("Workers AI analytics did not return the selected account. Verify account access and selection.", "unavailable");
        var groups = JsonFields.Object(accounts[0], CloudflareWorkersAiReader.Dataset);
        if (groups.ValueKind != JsonValueKind.Array || groups.GetArrayLength() != 1)
            return Failure("No single account-wide neuron aggregate was returned. Missing or grouped data does not prove zero usage.", "unavailable");
        // Dimension-bearing fixtures/responses cannot silently masquerade as an account aggregate.
        if (JsonFields.Object(groups[0], "dimensions").ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
            return Failure("Workers AI returned grouped data instead of the requested account aggregate.", "unavailable");
        var neurons = JsonFields.Number(JsonFields.Object(groups[0], "sum"), "totalNeurons");
        if (neurons is null || neurons > 9007199254740991d)
            return Failure("The Workers AI neuron aggregate was absent or invalid.", "unavailable");
        return new([new("cloudflare:workers-ai:adaptive-neurons", "neurons", Used: neurons,
            BudgetPoolId: "cloudflare-account:" + accountId, InheritAccountPool: false,
            WindowLabel: "Estimated neurons · UTC day · coverage/freshness unconfirmed", WindowId: "utc-day-adaptive-neurons",
            MeasurementKind: "usage-estimate", Scope: "account-workers-ai", ScopeId: accountId, SourceUrl: Documentation,
            ObservedAt: observed, WindowStart: start, WindowEnd: start.AddDays(1))],
            Support: new("api", "Workers AI neuron usage read for this account. Adaptive analytics may be sampled or delayed; billing coverage and plan are unconfirmed. The UTC reporting day ends at 00:00 UTC. No exact remaining allowance is derived.", ConsoleUrl));
    }

    internal static QuotaFetchResult Unavailable(string message) => new([], Support: new("unavailable", message, ConsoleUrl));
    private static QuotaFetchResult Setup(string message) => new([], Support: new("setup-required", message, ConsoleUrl, "quota"));
    internal static QuotaFetchResult Failure(string message, string kind) => new([], Error: message, Support: new(kind, message, ConsoleUrl, "quota"));
    public void Dispose() => http.Dispose();
}
