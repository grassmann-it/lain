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
public sealed class QuotaCredential(string bearerToken, string? providerAccountId = null, DateTimeOffset? expiresAt = null, string? baseUrl = null)
{
    public string BearerToken { get; } = bearerToken;
    public string? ProviderAccountId { get; } = providerAccountId;
    public DateTimeOffset? ExpiresAt { get; } = expiresAt;
    public string? BaseUrl { get; } = baseUrl;
    public override string ToString() => "QuotaCredential [redacted]";

    // Accepts the normalized private SecretStore schema, never the legacy store path.
    public static QuotaCredential? FromSecretJson(string json)
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
            JsonFields.Date(root, "expiresAt"), JsonFields.String(root, "baseUrl"));
    }
}

public record QuotaReading(string Source, string Unit, double? Used = null, double? Limit = null,
    double? Remaining = null, DateTimeOffset? ResetsAt = null, string? BudgetPoolId = null, bool InheritAccountPool = true, string? WindowLabel = null, double? UsedPercent = null);

// A partial response may contain usable readings and a sanitized diagnostic.
public record QuotaFetchResult(IReadOnlyList<QuotaReading> Readings, string? Error = null);

public interface IQuotaAdapter
{
    bool Supports(string provider);
    string Source { get; }
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
