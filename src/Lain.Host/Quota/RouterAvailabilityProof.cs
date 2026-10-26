using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;

namespace Lain.Host.Quota;

/// <summary>Nonsecret proof captured during the existing quota credential read, never a credential resolver.</summary>
public sealed record RouterAvailabilityQuotaProof(string AccountId, long AccountGeneration, string Provider,
    string Endpoint, string BindingVersion);

public interface IRouterAvailabilityQuotaProofSource
{
    RouterAvailabilityQuotaProof? GetRouterAvailabilityProof(string accountId, string? bindingVersion);
}

public sealed record RouterAvailabilityQuotaSnapshot(QuotaDto Reading, RouterAvailabilityQuotaProof? Proof);

public sealed class RouterAvailabilityProofStore
{
    public const int MaximumEntries = 4096;
    private readonly object gate = new();
    private readonly Dictionary<string, RouterAvailabilityQuotaProof> proofs = new(StringComparer.Ordinal);

    public void Remove(string accountId) { lock (gate) proofs.Remove(accountId); }
    public RouterAvailabilityQuotaProof? Get(string accountId, string? bindingVersion)
    {
        lock (gate) return proofs.TryGetValue(accountId, out var proof) && proof.BindingVersion == bindingVersion ? proof : null;
    }

    // Only the legacy primary/single transport is proven here. A normalized account may select
    // different inference and monitoring handles; it needs a separate qualified resource join.
    // The JSON is already loaded by HostQuotaSource. No private JSON or key is retained.
    public void CaptureLegacy(AccountDto account, string privateJson, string projectedJson, string bindingVersion)
    {
        Remove(account.Id);
        if (account.CredentialKind != "api-key" || account.Credentials is { Length: > 0 }) return;
        try
        {
            using var raw = JsonDocument.Parse(privateJson);
            using var projected = JsonDocument.Parse(projectedJson);
            var primary = raw.RootElement;
            var selected = primary;
            if (JsonFields.Object(primary, "transports") is { ValueKind: not JsonValueKind.Undefined } variants)
            {
                if (variants.ValueKind != JsonValueKind.Array || variants.GetArrayLength() != 1) return;
                selected = variants[0];
            }
            var key = JsonFields.String(primary, "apiKey");
            var provider = AccountService.CanonicalProviderId(account.Provider);
            var endpoint = CodingEndpoint(provider, JsonFields.String(selected, "baseUrl"));
            if (endpoint is null || JsonFields.String(primary, "kind") != "api-key" ||
                JsonFields.String(selected, "kind") != "api-key" || string.IsNullOrWhiteSpace(key) ||
                JsonFields.String(selected, "apiKey") != key || JsonFields.String(projected.RootElement, "apiKey") != key ||
                CodingEndpoint(provider, JsonFields.String(projected.RootElement, "baseUrl")) != endpoint ||
                JsonFields.Object(primary, "sourceEnabled").ValueKind == JsonValueKind.False ||
                JsonFields.Object(selected, "sourceEnabled").ValueKind == JsonValueKind.False ||
                JsonFields.String(selected, "provider") is { } selectedProvider && AccountService.CanonicalProviderId(selectedProvider) != provider)
                return;
            lock (gate)
            {
                // Current accounts can replace their own proof at capacity. New account
                // churn cannot grow the store or evict a retained current account's proof.
                if (!proofs.ContainsKey(account.Id) && proofs.Count >= MaximumEntries) return;
                proofs[account.Id] = new(account.Id, account.Generation, provider, endpoint, bindingVersion);
            }
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException)
        { /* Optional routing evidence must never turn a usable quota credential into a failure. */ }
    }

    public static string? CodingEndpoint(string provider, string? endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0) return null;
        var path = uri.AbsolutePath.TrimEnd('/');
        var host = uri.IdnHost.ToLowerInvariant();
        return (provider == "kimi" && host is "api.kimi.com" or "api.kimi.ai" && path == "/coding/v1" ||
            provider == "zai" && host == "api.z.ai" && path == "/api/coding/paas/v4")
            ? "https://" + host + path : null;
    }
}
