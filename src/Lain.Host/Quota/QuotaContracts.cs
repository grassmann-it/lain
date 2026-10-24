using System.Text.Json;
using Lain.Core;

namespace Lain.Host.Quota;

public interface IQuotaAccountSource
{
    Task<IReadOnlyList<AccountDto>> GetAccountsAsync(CancellationToken ct = default);
}

public interface IQuotaCredentialSource
{
    Task<QuotaCredential?> GetCredentialAsync(string accountId, CancellationToken ct = default);
}

// Deliberately contains no refresh token, token endpoint, or ability to rotate credentials.
public sealed class QuotaCredential(string bearerToken, string? providerAccountId = null, DateTimeOffset? expiresAt = null, string? baseUrl = null,
    IReadOnlyDictionary<string, string>? settings = null, QuotaMonitorCredential? monitor = null)
{
    public string BearerToken { get; } = bearerToken;
    public string? ProviderAccountId { get; } = providerAccountId;
    public DateTimeOffset? ExpiresAt { get; } = expiresAt;
    public string? BaseUrl { get; } = baseUrl;
    public IReadOnlyDictionary<string, string> Settings { get; } = settings is null
        ? new Dictionary<string, string>() : new Dictionary<string, string>(settings, StringComparer.Ordinal);
    public QuotaMonitorCredential? Monitor { get; } = monitor;
    public string? MonitorBearerToken => Monitor?.Secret;
    public override string ToString() => "QuotaCredential [redacted]";

    // Accepts the normalized private SecretStore schema, never the legacy store path.
    public static QuotaCredential? FromSecretJson(string json, QuotaMonitorCredential? monitor = null)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var kind = JsonFields.String(root, "kind");
        var token = kind switch
        {
            "codex-access-snapshot" or "codex-owned" => JsonFields.String(root, "accessToken"),
            "api-key" => JsonFields.String(root, "apiKey"),
            _ => null
        };
        return string.IsNullOrWhiteSpace(token) ? null : new(token,
            kind is "codex-access-snapshot" or "codex-owned" ? JsonFields.String(root, "accountId") : null,
            JsonFields.Date(root, "expiresAt"), JsonFields.String(root, "baseUrl"),
            JsonFields.Object(root, "settings") is { ValueKind: JsonValueKind.Object } values
                ? values.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String)
                    .ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal)
                : null, monitor);
    }
}

/// <summary>Private access projection. Owned refresh material must never enter this type.</summary>
public sealed class QuotaMonitorCredential(string kind, string? secret, string resourceId, DateTimeOffset? expiresAt = null)
{
    public string Kind { get; } = kind;
    public string? Secret { get; } = secret;
    public string ResourceId { get; } = resourceId;
    public DateTimeOffset? ExpiresAt { get; } = expiresAt;
    public override string ToString() => "QuotaMonitorCredential [redacted]";
}

public record QuotaReading(string Source, string Unit, double? Used = null, double? Limit = null,
    double? Remaining = null, DateTimeOffset? ResetsAt = null, string? BudgetPoolId = null, bool InheritAccountPool = true, string? WindowLabel = null, double? UsedPercent = null,
    string? WindowId = null, string? MeasurementKind = null, string? Scope = null, string? ScopeId = null,
    string? Model = null, string? SourceUrl = null, DateTimeOffset? ObservedAt = null, DateTimeOffset? DataAsOf = null,
    DateTimeOffset? WindowStart = null, DateTimeOffset? WindowEnd = null, DateTimeOffset? FreshUntil = null,
    DateTimeOffset? ProviderUpdatedAt = null, double? ApprovedLimit = null);

// A partial response may contain usable readings and a sanitized diagnostic.
public record QuotaFetchResult(IReadOnlyList<QuotaReading> Readings, string? Error = null, QuotaSupportDto? Support = null);

public interface IQuotaAdapter
{
    bool Supports(string provider);
    string Source { get; }
    bool RequiresCredential => true;
    Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default);
}

internal static class JsonFields
{
    public static JsonElement Object(JsonElement root, string key) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var value) ? value : default;
    public static string? String(JsonElement root, string key) => Object(root, key) is var value && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    public static double? Number(JsonElement root, string key)
    {
        var value = Object(root, key);
        double parsed;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out parsed) && double.IsFinite(parsed) && parsed >= 0) return parsed;
        if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out parsed) && double.IsFinite(parsed) && parsed >= 0) return parsed;
        return null;
    }
    public static DateTimeOffset? Date(JsonElement root, string key)
    {
        var value = Object(root, key);
        if (value.ValueKind == JsonValueKind.String && System.Text.RegularExpressions.Regex.IsMatch(value.GetString()!, @"(?:[zZ]|[+-]\d{2}:\d{2})$")
            && DateTimeOffset.TryParse(value.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var date)) return date.ToUniversalTime();
        var number = Number(root, key);
        if (number is null or <= 0) return null;
        try { return number > 1e11 ? DateTimeOffset.FromUnixTimeMilliseconds(checked((long)number.Value)) : DateTimeOffset.FromUnixTimeSeconds(checked((long)number.Value)); }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException) { return null; }
    }
}
