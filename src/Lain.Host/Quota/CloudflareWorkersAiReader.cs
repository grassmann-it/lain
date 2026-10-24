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
        // The successful usage response itself establishes schema and account access. Introspection is
        // diagnostic only: installations do not need a maintainer flag to read their own telemetry.
        if (HasGraphQlErrors(document.RootElement) && IsSchemaError(document.RootElement))
        {
            using var probe = Request(token, "{__type(name:\"Account\"){fields{name}}}");
            using var schema = await http.ReadAsync(probe, deadline.Token);
            var available = DatasetAvailable(schema.RootElement);
            return CloudflareQuotaAdapter.Failure(available == false
                ? "The current Cloudflare GraphQL account schema does not expose Workers AI neuron analytics. Open Workers AI usage in Cloudflare; no usage or balance was inferred."
                : "The Workers AI neuron query is not supported by the current GraphQL schema or account entitlement. Open Workers AI usage in Cloudflare; no usage or balance was inferred.", "unavailable");
        }
        return result;
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
        if (fields.ValueKind != JsonValueKind.Array) return null;
        return fields.EnumerateArray().Any(field => JsonFields.String(field, "name") == Dataset);
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
