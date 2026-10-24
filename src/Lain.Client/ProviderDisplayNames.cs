namespace Lain.Client;

public static class ProviderDisplayNames
{
    public static string Get(string id) => id.ToLowerInvariant() switch
    {
        "alibaba-model-studio" => "Alibaba Model Studio", "cloudflare-workers-ai" => "Cloudflare Workers AI",
        "codex" => "Codex", "cohere" => "Cohere", "commandcode" or "command-code" => "Command Code",
        "fireworks" => "Fireworks", "gemini-api" => "Gemini API", "groq" => "Groq", "inception" => "Inception",
        "kilo" => "Kilo", "kimi" => "Kimi", "mistral" => "Mistral", "nebius" => "Nebius",
        "nvidia-build" => "NVIDIA Build", "opencode-go" => "OpenCode", "openrouter" => "OpenRouter", "poe" => "Poe",
        "requesty" => "Requesty", "sambanova" => "SambaNova", "stepfun" => "StepFun", "synthetic" => "Synthetic",
        "upstage" => "Upstage", "xai" => "xAI", "xiaomi-mimo" or "mimo" => "Xiaomi MiMo", "zai" => "Z.ai",
        _ => id.Length == 0 ? id : char.ToUpperInvariant(id[0]) + id[1..]
    };
}
