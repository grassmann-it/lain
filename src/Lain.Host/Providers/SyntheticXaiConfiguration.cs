namespace Lain.Host.Providers;

public static class SyntheticXaiConfiguration
{
    // No keys in validation inputs or diagnostics. Known vendor routes cannot
    // quietly become arbitrary gateways or a different provider's credentials.
    public static string[] ValidateConfiguration(string provider, string baseUrl, string protocol, string model)
    {
        if (provider is not ("synthetic" or "xai")) return ["Unknown provider; configure xAI and Groq separately."];
        var bases = provider == "synthetic"
            ? new[] { "https://api.synthetic.new/openai/v1", "https://api.synthetic.new/v1" }
            : new[] { "https://api.x.ai/v1" };
        if (!bases.Contains(baseUrl.TrimEnd('/'), StringComparer.Ordinal)) return ["Use an official HTTPS API base URL for this route."];
        if (protocol != "openai-chat" && !(provider == "xai" && protocol == "openai-responses")) return ["Protocol not supported for this preconfigured route."];
        if (string.IsNullOrWhiteSpace(model)) return ["Model ID is missing."];
        if (provider == "synthetic" && !model.StartsWith("hf:", StringComparison.Ordinal) && !model.StartsWith("syn:", StringComparison.Ordinal))
            return ["Synthetic requires an explicit hf: model ID or a deliberately selected syn: alias."];
        if (provider == "xai" && !model.StartsWith("grok-", StringComparison.Ordinal)) return ["xAI requires a matching Grok model ID; Groq is a different provider."];
        return [];
    }
}
