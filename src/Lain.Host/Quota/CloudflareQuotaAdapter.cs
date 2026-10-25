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
        var usesInference = credential.Monitor is null || credential.Monitor.Kind == "inference-key";
        // An inference-key monitor is a reference, not a second secret. Independent monitor
        // credentials must never fall back to the saved inference credential.
        var access = usesInference ? credential.BearerToken : credential.Monitor!.Secret;
        var now = clock.GetUtcNow();
        if (account.Binding?.ResourceId is { } pinned && pinned != accountId
            || credential.Settings.GetValueOrDefault("accountId") is { } configured && configured != accountId)
            return Setup("Cloudflare monitoring selects a different account from this connection. Select the same Cloudflare account ID in monitor access; no request was sent and no credential was replaced.");
        if (!CloudResourceDiscovery.CloudflareAccount(accountId) || string.IsNullOrWhiteSpace(access)
            || access.Length > 32768 || access.Any(char.IsWhiteSpace) || access.Any(char.IsControl)
            || credential.Monitor is { Kind: "inference-key", Secret: not null }
            || credential.Monitor?.ExpiresAt is { } expiry && expiry <= now
            || credential.ExpiresAt is { } bearerExpiry && usesInference && bearerExpiry <= now)
            return Setup("Bind an unexpired monitoring grant to the same Cloudflare account as this connection. An existing API token with " + AnalyticsPermission + " can be used; a separate read token is optional.");
        try { return await reader.ReadAsync(access, accountId!, now, ct); }
        catch (CloudProviderException error) when (error.Status == HttpStatusCode.Unauthorized)
        { return Failure("Cloudflare rejected this analytics credential. Reconnect or replace the monitoring access for this account.", "setup-required"); }
        catch (CloudProviderException error) when (error.Status == HttpStatusCode.Forbidden)
        { return Failure("Cloudflare denied this analytics request (HTTP 403). Check the selected account and the token's " + AnalyticsPermission + "; if both are correct, check Workers AI dataset access. This response alone does not identify a missing permission, an unavailable dataset or a required plan. Reconnect only if the monitoring grant changed.", "unavailable"); }
        catch (CloudProviderException error) when (error.Status == HttpStatusCode.NotFound)
        { return Unavailable("Cloudflare did not find this analytics endpoint (HTTP 404). Check Cloudflare's GraphQL service status and Workers AI dashboard; account entitlement and usage remain unknown."); }
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
            return CloudflareWorkersAiReader.AnalyticsPermissionDenied(root)
                ? Failure("Cloudflare explicitly reported missing or denied Account Analytics permission. Check " + AnalyticsPermission + " on the existing monitoring token for this account; reconnect only if its grant changed. Partial data is not complete usage.", "setup-required")
                : Failure("Cloudflare rejected the Workers AI analytics query. Account access, dataset availability and query support are not distinguished by this response. Check the account ID and " + AnalyticsPermission + ", then the account's Workers AI dataset settings. Partial data is not complete usage; no plan requirement was inferred.", "unavailable");
        var accounts = JsonFields.Object(JsonFields.Object(JsonFields.Object(root, "data"), "viewer"), "accounts");
        if (accounts.ValueKind == JsonValueKind.Array && accounts.GetArrayLength() == 1
            && JsonFields.String(accounts[0], "accountTag") is { } returned && returned != accountId)
            return Failure("Cloudflare analytics returned a different account. Verify the monitor's account binding before reading again; that account's data was discarded.", "setup-required");
        if (accounts.ValueKind != JsonValueKind.Array || accounts.GetArrayLength() != 1 || JsonFields.String(accounts[0], "accountTag") != accountId)
            return Failure("Cloudflare did not return one identifiable selected account. Verify the account ID and token access; an empty account selection does not distinguish a wrong ID from denied access.", "unavailable");
        var groups = JsonFields.Object(accounts[0], CloudflareWorkersAiReader.Dataset);
        if (groups.ValueKind == JsonValueKind.Array && groups.GetArrayLength() == 0)
            return Unavailable("Cloudflare accepted the analytics query for the selected account but returned no neuron samples for this UTC interval. Usage remains unknown, not zero. Compare the same interval in Workers AI usage and retry after reporting catches up; no login or plan change is implied.");
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
