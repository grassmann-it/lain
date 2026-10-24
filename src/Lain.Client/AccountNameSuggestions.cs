namespace Lain.Client;

/// <summary>Suggests names only for new accounts; never rewrites saved names.</summary>
public static class AccountNameSuggestions
{
    public static string Suggest(string providerId, string providerName, IEnumerable<string> existingNames)
    {
        var name = providerId.ToLowerInvariant() switch
        {
            "nvidia" or "nvidia-build" => "NVIDIA Build",
            "cloudflare" or "cloudflare-workers-ai" => "Cloudflare Workers AI",
            "google" or "google-gemini" or "gemini-api" or "gemini" => "Gemini",
            "groq" => "Groq",
            "openrouter" => "OpenRouter",
            _ => providerName.Trim()
        };
        if (name.Length == 0) name = providerId;
        var used = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(name)) return name;
        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{name} {suffix}";
            if (!used.Contains(candidate)) return candidate;
        }
    }
}
