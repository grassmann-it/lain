using System.Text.Json;

namespace Lain.Host.Accounts.Managed;

public sealed record ZaiPersonalProjectScope(string AccountId, string Subject, string OrganizationId,
    string ProjectId, string LoginGeneration, string Origin = "https://api.z.ai", string Family = "zai")
{
    internal void Validate()
    {
        if (Origin != "https://api.z.ai" || Family != "zai" || new[] { AccountId, Subject, OrganizationId, ProjectId, LoginGeneration }
            .Any(s => string.IsNullOrWhiteSpace(s) || s is "." or ".." || s.Length > 256 || s.Any(char.IsControl)))
            throw new ArgumentException("A bound international personal Z.ai scope is required.");
    }
}
public sealed record ZaiExistingKeyReference(ZaiPersonalProjectScope Scope, string KeyId);

/// <summary>Noncreating discovery only. It cannot issue PATs, create keys or silently choose a default project.</summary>
public static class ZaiKeyDiscovery
{
    public static HttpRequestMessage CreateReadRequest(ZaiPersonalProjectScope scope)
    {
        scope.Validate();
        return new(HttpMethod.Get, scope.Origin + "/api/biz/v1/organization/" + Uri.EscapeDataString(scope.OrganizationId) +
            "/projects/" + Uri.EscapeDataString(scope.ProjectId) + "/api_keys");
    }
    public static ZaiExistingKeyReference? ParseExisting(JsonElement envelope, ZaiPersonalProjectScope scope, string requiredKeyId)
    {
        scope.Validate();
        if (string.IsNullOrWhiteSpace(requiredKeyId) || requiredKeyId.Length > 512 || requiredKeyId.Any(char.IsControl))
            throw new ArgumentException("Select an existing nonsecret key ID explicitly.");
        if (!ManagedJson.Success(envelope)) throw ManagedJson.Invalid();
        var data = ManagedJson.Field(envelope, "data");
        if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() > 512) throw ManagedJson.Invalid();
        ZaiExistingKeyReference? selected = null;
        foreach (var item in data.EnumerateArray())
        {
            var keyId = ManagedJson.Text(item, "apiKey")?.Trim();
            if (keyId != requiredKeyId) continue;
            if (selected is not null) throw ManagedJson.Invalid();
            if (ManagedJson.Number(item, "keyType") == 2) throw new InvalidDataException("Team Z.ai credentials require separate qualification.");
            selected = new(scope, keyId);
        }
        return selected; // Missing ID is a data-phase requirement; there is deliberately no create/retry fallback.
    }
}
