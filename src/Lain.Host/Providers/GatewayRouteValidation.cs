namespace Lain.Host.Providers;

/// <summary>Provider-specific prerequisites for an eventual route writer.
/// These checks do not contact providers or establish live billing posture.</summary>
public static class GatewayRouteValidation
{
    public static string CloudflareBaseUrl(string accountId)
    {
        if (accountId.Length != 32 || !accountId.All(Uri.IsHexDigit))
            throw new ArgumentException("A Cloudflare account ID must contain 32 hexadecimal characters.", nameof(accountId));
        return $"https://api.cloudflare.com/client/v4/accounts/{accountId}/ai/v1";
    }

    public static bool IsOfficialEndpoint(string providerId, string baseUrl, string? cloudflareAccountId = null)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            return false;
        var path = uri.AbsolutePath.TrimEnd('/');
        return providerId switch
        {
            "openrouter" => uri.Host.Equals("openrouter.ai", StringComparison.OrdinalIgnoreCase) && path == "/api/v1",
            "requesty" => uri.Host.Equals("router.requesty.ai", StringComparison.OrdinalIgnoreCase) && path == "/v1",
            "cloudflare-workers-ai" => cloudflareAccountId is { Length: 32 } && cloudflareAccountId.All(Uri.IsHexDigit)
                && uri.Host.Equals("api.cloudflare.com", StringComparison.OrdinalIgnoreCase)
                && path == $"/client/v4/accounts/{cloudflareAccountId}/ai/v1",
            _ => false
        };
    }

    /// <summary>verifiedFreeModels must come from qualified current endpoint metadata,
    /// never a suffix guess or a model-maker price. Paid and unknown fallbacks fail closed.</summary>
    public static string[] FreeOnlyRejections(string providerId, string model,
        IReadOnlySet<string> verifiedFreeModels, bool? accountEnforcesFreeStop,
        IEnumerable<string>? fallbackModels = null, bool usesGatewayPolicy = false, bool usesPaidExtras = false)
    {
        var reasons = new List<string>();
        if (accountEnforcesFreeStop != true) reasons.Add("Account billing posture does not establish a hard free stop.");
        if (usesGatewayPolicy) reasons.Add("An unqualified gateway routing policy may select paid or unknown routes.");
        if (usesPaidExtras) reasons.Add("Paid or unqualified gateway extras are excluded from free-only requests.");
        var fallbacks = fallbackModels?.ToArray() ?? [];
        if (providerId is not ("openrouter" or "requesty" or "cloudflare-workers-ai"))
            reasons.Add("Gateway free-only enforcement is not qualified.");
        if (model == "openrouter/free")
        {
            if (providerId != "openrouter" || fallbacks.Length != 0)
                reasons.Add("The random free router must be an OpenRouter route used alone.");
        }
        else if (!IsConcreteFreeModel(providerId, model, verifiedFreeModels))
            reasons.Add("Primary model is not a qualified concrete free route.");
        if (fallbacks.Any(modelId => !IsConcreteFreeModel(providerId, modelId, verifiedFreeModels)))
            reasons.Add("Every fallback must be a qualified concrete free route.");
        return reasons.ToArray();
    }

    private static bool IsConcreteFreeModel(string providerId, string model, IReadOnlySet<string> verifiedFreeModels)
    {
        if (!verifiedFreeModels.Any(value => string.Equals(value, model, StringComparison.Ordinal))) return false;
        return providerId switch
        {
            "openrouter" => model.Contains('/') && model.EndsWith(":free", StringComparison.Ordinal),
            "requesty" => model.Contains('/') && !model.StartsWith("policy/", StringComparison.Ordinal)
                && !model.StartsWith("smart/", StringComparison.Ordinal),
            "cloudflare-workers-ai" => model.StartsWith("@cf/", StringComparison.Ordinal),
            _ => false
        };
    }
}
