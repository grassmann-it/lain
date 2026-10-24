using System.Text.Json;

namespace Lain.Host.Accounts.Managed;

/// <summary>Short-lived validated result for an eventual owner; never a static API-key credential or refresh owner.</summary>
public sealed class ZaiProjectTokenProjection
{
    public ZaiExistingKeyReference Reference { get; }
    public string AccessToken { get; }
    public DateTimeOffset ExpiresAt { get; }
    private ZaiProjectTokenProjection(ZaiExistingKeyReference reference, string token, DateTimeOffset expiresAt)
    { Reference = reference; AccessToken = token; ExpiresAt = expiresAt; }
    public override string ToString() => "ZaiProjectTokenProjection [redacted]";

    public static ZaiProjectTokenProjection? Parse(JsonElement envelope, ZaiExistingKeyReference reference,
        DateTimeOffset requestStartedAt, DateTimeOffset now)
    {
        reference.Scope.Validate();
        if (string.IsNullOrWhiteSpace(reference.KeyId) || reference.KeyId.Length > 512 || reference.KeyId.Any(char.IsControl) || requestStartedAt > now || !ManagedJson.Success(envelope)) throw ManagedJson.Invalid();
        var data = ManagedJson.Field(envelope, "data");
        var enabled = ManagedJson.Field(data, "enable");
        if (enabled.ValueKind == JsonValueKind.False) return null;
        // Public source defaults an omitted enable to true; null/other values are not accepted.
        if (enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.Undefined)) throw ManagedJson.Invalid();
        var token = ManagedJson.Text(data, "accessToken")?.Trim();
        var expiresIn = ManagedJson.Number(data, "expiresIn"); var expiresAt = ManagedJson.Number(data, "expiresAt");
        if (ManagedJson.Text(data, "tokenType") != "Bearer" || string.IsNullOrWhiteSpace(token) || token == reference.KeyId || token.Length > 65536 || token.Any(char.IsWhiteSpace) ||
            expiresIn is not > 0 || expiresAt is not > 0) throw ManagedJson.Invalid();
        try
        {
            var absolute = DateTimeOffset.FromUnixTimeMilliseconds(checked((long)(expiresAt.Value * 1000)));
            var relative = requestStartedAt.AddSeconds(expiresIn.Value);
            var expiry = absolute < relative ? absolute : relative;
            if (expiry <= now) throw ManagedJson.Invalid();
            return new(reference, token, expiry);
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or OverflowException) { throw ManagedJson.Invalid(); }
    }
    public bool IsCurrent(ZaiExistingKeyReference reference, DateTimeOffset now) => Reference == reference && now < ExpiresAt;
}
