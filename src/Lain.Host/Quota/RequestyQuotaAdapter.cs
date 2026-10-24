using System.Globalization;
using System.Text.Json;
using Lain.Core;

namespace Lain.Host.Quota;

// requestyai/cli@2180939 internal/client/api_keys.go and usage.go.
public sealed class RequestyQuotaAdapter(HttpClient client) : IQuotaAdapter
{
    public const string Endpoint = "https://api-v2.requesty.ai/v1/manage/apikey/self";
    public string Source => Endpoint;
    public bool Supports(string provider) => provider.Equals("requesty", StringComparison.OrdinalIgnoreCase);

    public async Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default)
    {
        if (!Supports(account.Provider) || account.CredentialKind != "api-key" || string.IsNullOrWhiteSpace(credential.BearerToken))
            throw new QuotaFetchException("Requesty Self requires its own API key account.");
        if (credential.BaseUrl is not null && credential.BaseUrl.TrimEnd('/') != "https://router.requesty.ai/v1")
            throw new QuotaFetchException("Requesty Self requires its official router API base URL.");
        if (account.Binding is { } binding && (!binding.Provider.Equals("requesty", StringComparison.OrdinalIgnoreCase)
            || binding.ResourceType is not null && binding.ResourceType is not ("api_key" or "key" or "group")))
            throw new QuotaFetchException("Requesty Self requires its own key resource binding.");
        if (account.Binding is { ResourceId: not null, ResourceType: null })
            throw new QuotaFetchException("Requesty Self requires a key or group type for its bound resource.");
        var now = DateTimeOffset.UtcNow;
        var start = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var rows = new List<QuotaReading>();
        var errors = new List<string>();
        RequestyKeyMetadata? metadata = null;
        string? metadataError = null;
        var resourceMismatch = false;
        var resourceVerified = false;
        try
        {
            using var doc = await QuotaHttp.GetAsync(client, Endpoint, credential, ct, userAgent: "lain/1.0");
            metadata = NormalizeMetadata(doc.RootElement);
            if (metadata is not null && (metadata.KeyId == credential.BearerToken || metadata.GroupId == credential.BearerToken))
            { metadata = null; throw new QuotaFetchException("Requesty Self response invalid."); }
            if (account.Binding is { ResourceId: { } expected, ResourceType: { } resourceType }
                && (resourceType == "group" ? metadata?.GroupId : metadata?.KeyId) != expected)
            { metadata = null; resourceMismatch = true; throw new QuotaFetchException("Requesty Self resource does not match the bound account."); }
            resourceVerified = metadata is not null;
            rows.AddRange(Normalize(doc.RootElement, start));
            if (metadata is null) { metadataError = "Requesty key metadata unknown."; errors.Add(metadataError); }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { metadataError = "Requesty Self request timed out."; errors.Add(metadataError); }
        catch (Exception ex) when (ex is QuotaFetchException or HttpRequestException or JsonException or IOException or ArgumentException or FormatException)
        { metadataError = "Requesty Self response unavailable or invalid."; errors.Add(metadataError); }
        if (resourceMismatch) return new([], "Requesty Self resource does not match the bound account.",
            RequestyKeyOutcome: new(false, "Requesty Self resource does not match the bound account."));
        // A bound identity requires a successful Self read. Transport/schema failure
        // must not let independent usage bypass that identity check with a null ID.
        if (account.Binding?.ResourceId is not null && !resourceVerified)
            return new([], "Requesty Self identity could not be verified for the bound resource.",
                RequestyKeyOutcome: new(false, "Requesty Self identity could not be verified for the bound resource."));
        // Explicit month-to-observation bounds; no all-time spend compared with a monthly cap.
        var usageUrl = Endpoint + "/usage?start=" + Uri.EscapeDataString(start.ToString("O", CultureInfo.InvariantCulture))
            + "&end=" + Uri.EscapeDataString(now.ToString("O", CultureInfo.InvariantCulture));
        try
        {
            using var doc = await QuotaHttp.GetAsync(client, usageUrl, credential, ct, userAgent: "lain/1.0");
            rows.AddRange(NormalizeUsage(doc.RootElement, start, now, metadata?.KeyId));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { errors.Add("Requesty Self usage request timed out."); }
        catch (Exception ex) when (ex is QuotaFetchException or HttpRequestException or JsonException or IOException or ArgumentException or FormatException)
        { errors.Add("Requesty Self usage response unavailable or invalid."); }
        if (rows.Count == 0 && metadata is null && errors.Count == 0) errors.Add("Requesty Self quota unknown.");
        return new(rows, errors.Count == 0 ? null : string.Join(" ", errors), RequestyKey: metadata,
            RequestyKeyOutcome: new(metadata is not null, metadataError));
    }

    public static RequestyKeyMetadata? NormalizeMetadata(JsonElement root)
    {
        var id = SafeIdentifier(JsonFields.String(root, "id"));
        if (id is null) return null;
        var permissions = JsonFields.Object(root, "permissions");
        var cap = JsonFields.Number(root, "monthly_limit");
        return new(id, SafeIdentifier(JsonFields.String(JsonFields.Object(root, "group"), "id")),
            Permission(JsonFields.String(permissions, "manage")), Permission(JsonFields.String(permissions, "completions")),
            JsonFields.Date(root, "expires_at"), cap.HasValue ? cap == 0 : null);
    }

    public static IReadOnlyList<QuotaReading> Normalize(JsonElement root, DateTimeOffset monthStart)
    {
        var metadata = NormalizeMetadata(root);
        if (metadata is null) return [];
        var cap = JsonFields.Number(root, "monthly_limit");
        var spent = JsonFields.Number(root, "monthly_spend");
        if (cap is null && spent is null) return [];
        // Zero is the documented uncapped sentinel, not an exhausted zero-dollar cap.
        if (cap == 0) cap = null;
        double? remaining = cap.HasValue && spent.HasValue && spent <= cap ? cap - spent : null;
        return [new(Endpoint + "#monthly-cap", "USD", spent, cap, remaining,
            InheritAccountPool: false, WindowLabel: "API key monthly spend", WindowId: "key-monthly-cap",
            MeasurementKind: "spend-cap", Scope: "api_key", ScopeId: metadata.KeyId, SourceUrl: Endpoint,
            WindowStart: monthStart, WindowEnd: monthStart.AddMonths(1))];
    }

    public static IReadOnlyList<QuotaReading> NormalizeUsage(JsonElement root, DateTimeOffset start, DateTimeOffset end, string? keyId = null)
    {
        var usage = JsonFields.Object(root, "usage");
        if (usage.ValueKind != JsonValueKind.Object || end < start) return [];
        // Unknown/malformed buckets invalidate the total instead of undercounting it.
        double? spend = 0, requests = 0, tokens = 0;
        var count = 0;
        foreach (var bucket in usage.EnumerateObject())
        {
            if (++count > 4096) return [];
            spend = Add(spend, JsonFields.Number(bucket.Value, "spend"));
            requests = Add(requests, JsonFields.Number(bucket.Value, "total_requests"));
            tokens = Add(tokens, JsonFields.Number(bucket.Value, "total_tokens"));
        }
        if (count == 0) return [];
        var rows = new List<QuotaReading>();
        void Row(string component, string unit, double? value)
        {
            if (value is null) return;
            rows.Add(new(Endpoint + "#self-usage-" + component, unit, Used: value,
                InheritAccountPool: false, WindowLabel: "API key usage · requested period", WindowId: "self-usage-" + component,
                MeasurementKind: "usage", Scope: "api_key", ScopeId: keyId, SourceUrl: Endpoint + "/usage", WindowStart: start, WindowEnd: end));
        }
        Row("spend", "USD", spend); Row("requests", "requests", requests); Row("tokens", "tokens", tokens);
        return rows;
    }

    private static double? Add(double? a, double? b) => a.HasValue && b.HasValue && double.IsFinite(a.Value + b.Value) ? a + b : null;
    private static string? Permission(string? value) => value is "none" or "read" or "write" ? value : null;
    internal static string? SafeIdentifier(string? value) => value is { Length: > 0 and <= 200 }
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':') ? value : null;
}
