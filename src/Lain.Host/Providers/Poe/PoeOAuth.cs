using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lain.Host.Providers.Poe;

// Preparation only: host-owned callback lifetime, consent UI and SecretStore
// persistence must be supplied by the integrator before exposing this flow.
public sealed class PoeOAuthAttempt
{
    public const string AuthorizationEndpoint = "https://poe.com/oauth/authorize";
    public const string TokenEndpoint = "https://api.poe.com/token";
    private readonly string clientId;
    private readonly Uri redirectUri;
    private readonly string verifier;
    private readonly string state;
    private bool consumed;
    public Uri AuthorizationUri { get; }

    private PoeOAuthAttempt(string clientId, Uri redirectUri)
    {
        this.clientId = clientId;
        this.redirectUri = redirectUri;
        verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var values = new Dictionary<string, string>
        {
            ["client_id"] = clientId, ["redirect_uri"] = redirectUri.AbsoluteUri,
            ["response_type"] = "code", ["scope"] = "apikey:create",
            ["code_challenge"] = challenge, ["code_challenge_method"] = "S256", ["state"] = state
        };
        AuthorizationUri = new(AuthorizationEndpoint + "?" + string.Join("&", values.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value))));
    }

    public static PoeOAuthAttempt Create(string clientId, Uri redirectUri)
    {
        if (string.IsNullOrWhiteSpace(clientId)) throw new ArgumentException("Poe client ID required.", nameof(clientId));
        if (!redirectUri.IsAbsoluteUri || !(redirectUri.Scheme == "https" || redirectUri.Scheme == "http" && redirectUri.IsLoopback)
            || redirectUri.UserInfo.Length != 0 || redirectUri.Fragment.Length != 0)
            throw new ArgumentException("Poe redirect must use HTTPS or loopback HTTP.", nameof(redirectUri));
        return new(clientId, redirectUri);
    }

    // The callback must be received by the host for this exact registered URI.
    // Validate state locally even though the upstream docs mark it optional.
    public HttpRequestMessage CreateTokenRequest(string authorizationCode, string callbackState)
    {
        lock (this)
        {
            if (consumed) throw new InvalidOperationException("Poe authorization attempt already consumed.");
            if (string.IsNullOrWhiteSpace(authorizationCode) || callbackState is null
                || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(callbackState)))
                throw new InvalidOperationException("Invalid Poe authorization callback.");
            consumed = true;
            return new(HttpMethod.Post, TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code", ["client_id"] = clientId,
                    ["code"] = authorizationCode, ["redirect_uri"] = redirectUri.AbsoluteUri, ["code_verifier"] = verifier
                })
            };
        }
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public override string ToString() => "PoeOAuthAttempt [redacted]";

    public static PoeIssuedKey? ParseTokenResponse(JsonElement root, DateTimeOffset receivedAt)
    {
        if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("error", out _)
            || !root.TryGetProperty("api_key", out var value) || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()) || !root.TryGetProperty("api_key_expires_in", out var expiry)) return null;
        if (expiry.ValueKind == JsonValueKind.Null) return new(value.GetString()!, null);
        if (expiry.ValueKind != JsonValueKind.Number || !expiry.TryGetDouble(out var seconds) || !double.IsFinite(seconds) || seconds <= 0) return null;
        try { return new(value.GetString()!, receivedAt.AddSeconds(seconds)); }
        catch (ArgumentOutOfRangeException) { return null; }
    }
}

public sealed class PoeIssuedKey(string apiKey, DateTimeOffset? expiresAt)
{
    public string ApiKey { get; } = apiKey;
    public DateTimeOffset? ExpiresAt { get; } = expiresAt;
    public override string ToString() => "PoeIssuedKey [redacted]";
}
