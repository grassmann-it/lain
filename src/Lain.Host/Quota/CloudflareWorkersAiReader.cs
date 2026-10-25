using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Lain.Host.Accounts.Cloud;

namespace Lain.Host.Quota;

/// <summary>Account-bound, read-only Workers AI analytics. No inference requests or dashboard sessions.</summary>
public sealed class CloudflareWorkersAiReader(CloudProviderHttp http)
{
    public const string Endpoint = "https://api.cloudflare.com/client/v4/graphql";
    public const string Dataset = "aiInferenceAdaptiveGroups";

    public async Task<QuotaFetchResult> ReadAsync(string token, string accountId, DateTimeOffset now, CancellationToken ct)
    {
        var start = UtcDayStart(now);
        // At midnight there is no non-empty interval yet. An empty interval cannot establish measured zero.
        if (start == now) return CloudflareQuotaAdapter.Unavailable("The new UTC reporting day has just started. No usage interval is available yet.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = Request(token, UsageQuery(accountId, start, now));
        using var document = await http.ReadAsync(request, deadline.Token);
        var result = CloudflareQuotaAdapter.Normalize(document.RootElement, accountId, start, now);
        if (!HasGraphQlErrors(document.RootElement) || AnalyticsPermissionDenied(document.RootElement)) return result;
        // Diagnostic reads never qualify a failed usage read. Schema presence alone does not
        // establish this account's dataset grant; inspect its settings separately when available.
        if (IsSchemaError(document.RootElement))
        {
            using var schema = await DiagnosticAsync(token, "{__type(name:\"Account\"){fields{name}}}", deadline.Token, ct);
            if (schema is not null && DatasetAvailable(schema.RootElement) == false)
                return CloudflareQuotaAdapter.Failure("The current Cloudflare GraphQL account schema does not expose Workers AI neuron analytics. Check Workers AI usage in Cloudflare; schema absence does not establish a paid-plan requirement. No usage or balance was inferred.", "unavailable");
        }
        using var settings = await DiagnosticAsync(token, SettingsQuery(accountId), deadline.Token, ct);
        return settings is null ? result : DiagnoseSettings(settings.RootElement, accountId) ?? result;
    }

    public static DateTimeOffset UtcDayStart(DateTimeOffset at) => new(at.UtcDateTime.Date, TimeSpan.Zero);

    public static string UsageQuery(string accountId, DateTimeOffset start, DateTimeOffset end)
    {
        if (!CloudResourceDiscovery.CloudflareAccount(accountId) || start.Offset != TimeSpan.Zero || start != UtcDayStart(end)
            || end <= start || end > start.AddDays(1)) throw new ArgumentException("Invalid Cloudflare usage selection.");
        // With no dimensions there is one account-wide aggregate, not a truncated top-N model list.
        return "{viewer{accounts(filter:{accountTag:" + JsonSerializer.Serialize(accountId) + "}){accountTag " + Dataset + "(limit:1,filter:{datetime_geq:"
            + JsonSerializer.Serialize(start.ToString("O", CultureInfo.InvariantCulture)) + ",datetime_lt:"
            + JsonSerializer.Serialize(end.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)) + "}){sum{totalNeurons}}}}}";
    }

    public static bool HasGraphQlErrors(JsonElement root)
    {
        var errors = JsonFields.Object(root, "errors");
        return errors.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null)
            && (errors.ValueKind != JsonValueKind.Array || errors.GetArrayLength() > 0);
    }

    public static bool? DatasetAvailable(JsonElement root)
    {
        if (HasGraphQlErrors(root)) return null;
        var fields = JsonFields.Object(JsonFields.Object(JsonFields.Object(root, "data"), "__type"), "fields");
        if (fields.ValueKind != JsonValueKind.Array || fields.EnumerateArray().Any(field => JsonFields.String(field, "name") is null)) return null;
        return fields.EnumerateArray().Any(field => JsonFields.String(field, "name") == Dataset);
    }

    public static string SettingsQuery(string accountId)
    {
        if (!CloudResourceDiscovery.CloudflareAccount(accountId)) throw new ArgumentException("Invalid Cloudflare account selection.");
        return "{viewer{accounts(filter:{accountTag:" + JsonSerializer.Serialize(accountId)
            + "}){accountTag settings{" + Dataset + "{enabled availableFields}}}}}";
    }

    public static bool AnalyticsPermissionDenied(JsonElement root)
    {
        var errors = JsonFields.Object(root, "errors");
        if (errors.ValueKind != JsonValueKind.Array) return false;
        // Only an explicit named Analytics permission signal supports this diagnosis. Generic
        // FORBIDDEN/not-authorized responses also occur for resource/dataset restrictions.
        return errors.EnumerateArray().Any(error => JsonFields.String(error, "message") is { } message &&
            message.Contains("account analytics", StringComparison.OrdinalIgnoreCase)
            && message.Contains("permission", StringComparison.OrdinalIgnoreCase)
            && (message.Contains("denied", StringComparison.OrdinalIgnoreCase)
                || message.Contains("missing", StringComparison.OrdinalIgnoreCase)
                || message.Contains("required", StringComparison.OrdinalIgnoreCase)));
    }

    private static QuotaFetchResult? DiagnoseSettings(JsonElement root, string accountId)
    {
        if (HasGraphQlErrors(root)) return null;
        var accounts = JsonFields.Object(JsonFields.Object(JsonFields.Object(root, "data"), "viewer"), "accounts");
        if (accounts.ValueKind != JsonValueKind.Array || accounts.GetArrayLength() != 1) return null;
        var returned = JsonFields.String(accounts[0], "accountTag");
        if (returned is not null && returned != accountId)
            return CloudflareQuotaAdapter.Failure("Cloudflare dataset settings returned a different account. Verify the monitor's account binding before reading again; that account's data was discarded.", "setup-required");
        if (returned != accountId) return null;
        var dataset = JsonFields.Object(JsonFields.Object(accounts[0], "settings"), Dataset);
        var enabled = JsonFields.Object(dataset, "enabled");
        if (enabled.ValueKind == JsonValueKind.False)
            return CloudflareQuotaAdapter.Failure("Cloudflare dataset settings report Workers AI analytics disabled for this account and requester. Check the selected account's dataset access in Cloudflare. The response does not establish whether permissions or entitlement caused this; no paid-plan requirement was inferred.", "unavailable");
        var fields = JsonFields.Object(dataset, "availableFields");
        if (enabled.ValueKind == JsonValueKind.True && fields.ValueKind == JsonValueKind.Array
            && fields.EnumerateArray().All(field => field.ValueKind == JsonValueKind.String)
            && !fields.EnumerateArray().Any(field => field.GetString() == "sum.totalNeurons"))
            return CloudflareQuotaAdapter.Failure("Cloudflare dataset settings do not list the requested neuron field for this account and requester. Check Workers AI usage and current dataset fields; no usage, balance or paid-plan requirement was inferred.", "unavailable");
        return null;
    }

    private async Task<JsonDocument?> DiagnosticAsync(string token, string query, CancellationToken deadline, CancellationToken caller)
    {
        try
        {
            using var request = Request(token, query);
            return await http.ReadAsync(request, deadline);
        }
        catch (Exception error) when (error is CloudProviderException or JsonException or InvalidDataException or HttpRequestException)
        { return null; } // Failed optional diagnostics must not overwrite the original usage error.
        catch (OperationCanceledException) when (!caller.IsCancellationRequested) { return null; }
    }

    private static bool IsSchemaError(JsonElement root)
    {
        var errors = JsonFields.Object(root, "errors");
        if (errors.ValueKind != JsonValueKind.Array) return false;
        // Inspect only to choose a fixed safe diagnostic; provider text never leaves the parser.
        return errors.EnumerateArray().Any(error => JsonFields.String(error, "message") is { } message &&
            (message.Contains("cannot query field", StringComparison.OrdinalIgnoreCase)
             || message.Contains("unknown field", StringComparison.OrdinalIgnoreCase)));
    }

    private static HttpRequestMessage Request(string token, string query)
    {
        var request = CloudProviderHttp.Get(Endpoint, token);
        request.Method = HttpMethod.Post;
        request.Content = JsonContent.Create(new { query });
        return request;
    }
}
