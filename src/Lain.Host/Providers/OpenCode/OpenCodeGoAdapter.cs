using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Lain.Host.Providers.OpenCode;

public sealed record OpenCodeGoEndpoint(string Model, string Protocol, Uri Endpoint);

// Explicit per-route protocols from the official Go endpoint table. Neither
// an OpenAI-shaped model list nor a model's manufacturer determines its API.
public static class OpenCodeGoAdapter
{
    public const string ModelsEndpoint = "https://opencode.ai/inference/go/v1/models";
    public const string QuotaState = "unsupported";
    public const string QuotaExplanation = "No verified public Go usage endpoint; current allowance and reset are unknown. View current usage in the OpenCode Console.";
    private static readonly Dictionary<string, (string Protocol, string Path)> Routes = new(StringComparer.Ordinal)
    {
        ["step-5-preview"] = ("openai-chat", "openai/v1/chat/completions"),
        ["glm-5.3"] = ("openai-chat", "openai/v1/chat/completions"),
        ["glm-5.3-flash"] = ("openai-chat", "openai/v1/chat/completions"),
        ["kimi-k3"] = ("openai-chat", "openai/v1/chat/completions"),
        ["kimi-k3.1-pro"] = ("openai-chat", "openai/v1/chat/completions"),
        ["gpt-6-luna"] = ("openai-responses", "openai/v1/responses"),
        ["claude-haiku-5-5"] = ("anthropic-messages", "anthropic/v1/messages"),
        ["qwen3.8-max"] = ("anthropic-messages", "anthropic/v1/messages"),
        ["gemini-4-argon-contributor"] = ("gemini", "google/v1beta/models/gemini-4-argon-contributor")
    };

    public static OpenCodeGoEndpoint? Resolve(string model, bool stream = false)
    {
        if (!Routes.TryGetValue(model, out var route)) return null;
        var suffix = route.Protocol == "gemini" ? stream ? ":streamGenerateContent?alt=sse" : ":generateContent" : "";
        return new(model, route.Protocol, new("https://opencode.ai/inference/go/" + route.Path + suffix));
    }

    // Payload must already use the selected protocol. This never translates a
    // Responses body into Chat, Anthropic or Gemini syntax implicitly.
    public static HttpRequestMessage CreateRequest(string model, string protocol, string payload, string apiKey, string conversationId, bool stream = false)
    {
        var endpoint = Resolve(model, stream) ?? throw new ArgumentException("OpenCode Go route is not qualified.", nameof(model));
        if (endpoint.Protocol != protocol) throw new ArgumentException("Payload protocol does not match the OpenCode Go route.", nameof(protocol));
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("OpenCode API key required.", nameof(apiKey));
        if (string.IsNullOrWhiteSpace(conversationId) || conversationId.Any(c => c < 0x21 || c > 0x7e) || conversationId.Length > 256)
            throw new ArgumentException("A stable, opaque conversation ID is required.", nameof(conversationId));
        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Protocol payload must be an object.", nameof(payload));
        if (protocol != "gemini" && (!document.RootElement.TryGetProperty("model", out var namedModel)
            || namedModel.ValueKind != JsonValueKind.String || namedModel.GetString() != model))
            throw new ArgumentException("Payload model must match the selected route.", nameof(payload));
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Endpoint) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.UserAgent.ParseAdd("lain/1.0");
        request.Headers.Add("x-opencode-session", conversationId);
        return request;
    }

    // The public endpoint currently returns OpenAI's minimal list shape with
    // owned_by=opencode. It proves availability, not manufacturer or protocol.
    public static IReadOnlyList<string> NormalizeModels(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("error", out _) || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return [];
        return data.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object
            && item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString()))
            .Select(item => item.GetProperty("id").GetString()!).Distinct(StringComparer.Ordinal).ToArray();
    }
}
