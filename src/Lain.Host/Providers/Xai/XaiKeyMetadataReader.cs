using System.Text.Json;
using Lain.Core;
using Lain.Host.Quota;

namespace Lain.Host.Providers.Xai;

public sealed record XaiKeyMetadata(string? TeamId, string? ApiKeyId, IReadOnlyList<string>? Acls,
    bool? ApiKeyDisabled, bool? ApiKeyBlocked, bool? TeamBlocked);

// https://docs.x.ai/developers/rest-api-reference/inference/other, checked 2026-10-24.
// Discovery only; this reader cannot provision a key or read management billing.
public sealed class XaiKeyMetadataReader(HttpClient client)
{
    public const string Endpoint = "https://api.x.ai/v1/api-key";

    public async Task<XaiKeyMetadata?> ReadAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default)
    {
        if (!account.Provider.Equals("xai", StringComparison.OrdinalIgnoreCase) || account.CredentialKind != "api-key"
            || string.IsNullOrWhiteSpace(credential.BearerToken)) throw new QuotaFetchException("xAI metadata requires its own API key account.");
        if (credential.BaseUrl is not null && credential.BaseUrl.TrimEnd('/') != "https://api.x.ai/v1")
            throw new QuotaFetchException("xAI metadata requires its official API base URL.");
        if (account.Binding is { } binding && (!binding.Provider.Equals("xai", StringComparison.OrdinalIgnoreCase)
            || binding.ResourceType is not null && binding.ResourceType != "team"))
            throw new QuotaFetchException("xAI metadata requires its API team resource binding.");
        try
        {
            using var doc = await QuotaHttp.GetAsync(client, Endpoint, credential, ct, userAgent: "lain/1.0");
            var result = Normalize(doc.RootElement);
            if (result is not null && (result.TeamId == credential.BearerToken || result.ApiKeyId == credential.BearerToken
                || result.Acls?.Any(x => x.Contains(credential.BearerToken, StringComparison.Ordinal)) == true))
                throw new QuotaFetchException("xAI metadata response invalid.");
            if (credential.ProviderAccountId is not null && result?.TeamId != credential.ProviderAccountId)
                throw new QuotaFetchException("xAI metadata team does not match the bound account.");
            if (account.Binding?.ResourceId is { } team && result?.TeamId != team)
                throw new QuotaFetchException("xAI metadata team does not match the bound account.");
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new QuotaFetchException("xAI metadata request timed out."); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or ArgumentException or FormatException)
        { throw new QuotaFetchException("xAI metadata response unavailable or invalid."); }
    }

    public static XaiKeyMetadata? Normalize(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        var team = Identifier(JsonFields.String(root, "team_id"));
        var key = Identifier(JsonFields.String(root, "api_key_id"));
        var acls = JsonFields.Object(root, "acls");
        IReadOnlyList<string>? permissions = null;
        if (acls.ValueKind == JsonValueKind.Array && acls.GetArrayLength() <= 256
            && acls.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String && x.GetString() is { Length: > 0 and <= 256 } s
                && s.All(c => char.IsAsciiLetterOrDigit(c) || c is ':' or '-' or '_' or '*' or '.' or '/')))
            permissions = acls.EnumerateArray().Select(x => x.GetString()!).ToArray();
        var disabled = Bool(root, "api_key_disabled");
        var blocked = Bool(root, "api_key_blocked");
        var teamBlocked = Bool(root, "team_blocked");
        if (team is null && key is null && permissions is null && disabled is null && blocked is null && teamBlocked is null) return null;
        return new(team, key, permissions, disabled, blocked, teamBlocked);
    }

    private static string? Identifier(string? value) => value is { Length: > 0 and <= 200 }
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') ? value : null;
    private static bool? Bool(JsonElement root, string field) => JsonFields.Object(root, field).ValueKind switch
    { JsonValueKind.True => true, JsonValueKind.False => false, _ => null };
}
