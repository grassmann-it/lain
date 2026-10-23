namespace Lain.Host.Providers;

public static class SyntheticXaiConfiguration
{
    // No keys in validation inputs or diagnostics. Known vendor routes cannot
    // quietly become arbitrary gateways or a different provider's credentials.
    public static string[] ValidateConfiguration(string provider, string baseUrl, string protocol, string model)
    {
        if (provider is not ("synthetic" or "xai")) return ["Unbekannter Anbieter; xAI und Groq getrennt konfigurieren."];
        var bases = provider == "synthetic"
            ? new[] { "https://api.synthetic.new/openai/v1", "https://api.synthetic.new/v1" }
            : new[] { "https://api.x.ai/v1" };
        if (!bases.Contains(baseUrl.TrimEnd('/'), StringComparer.Ordinal)) return ["Für diese Route eine offizielle HTTPS-API-Basis verwenden."];
        if (protocol != "openai-chat" && !(provider == "xai" && protocol == "openai-responses")) return ["Protokoll für diese vorbereitete Route nicht unterstützt."];
        if (string.IsNullOrWhiteSpace(model)) return ["Modell-ID fehlt."];
        if (provider == "synthetic" && !model.StartsWith("hf:", StringComparison.Ordinal) && !model.StartsWith("syn:", StringComparison.Ordinal))
            return ["Synthetic benötigt eine explizite hf:-Modell-ID oder einen bewusst gewählten syn:-Alias."];
        if (provider == "xai" && !model.StartsWith("grok-", StringComparison.Ordinal)) return ["xAI benötigt eine passende Grok-Modell-ID; Groq ist ein anderer Anbieter."];
        return [];
    }
}
