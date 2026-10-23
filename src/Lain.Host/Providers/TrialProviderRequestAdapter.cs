using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Lain.Host.Providers;

/// <summary>
/// Isolated OpenAI-chat preparation for documented trial/PAYG APIs. No network,
/// credential persistence, quota probing, fallback, billing setup or console mutation.
/// </summary>
public static class TrialProviderRequestAdapter
{
    private static readonly HashSet<string> MercuryReasoning = new(StringComparer.Ordinal) { "instant", "low", "medium", "high" };

    public static HttpRequestMessage CreateRequest(TrialProviderConfiguration configuration, string apiKey, JsonElement body)
    {
        var endpoint = TrialProviderConfigurationValidator.Validate(configuration);
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Any(char.IsControl) || apiKey.Any(char.IsWhiteSpace))
            throw new ArgumentException("A valid provider API key is required.");
        if (body.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("The chat request must be an object.");
        var properties = body.EnumerateObject().ToArray();
        if (properties.GroupBy(p => p.Name, StringComparer.Ordinal).Any(g => g.Count() > 1))
            throw new ArgumentException("Duplicate chat request fields are not supported.");
        if (!body.TryGetProperty("model", out var model) || model.ValueKind != JsonValueKind.String ||
            model.GetString() != configuration.Model)
            throw new ArgumentException("Request model must match the configured concrete route.");
        if (!body.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("The chat request requires a messages array.");
        // Secrets must be passed as headers, not accepted as request/config payload fields.
        if (properties.Any(p => p.Name.Equals("api_key", StringComparison.OrdinalIgnoreCase) ||
                                p.Name.Equals("authorization", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Credentials must not be included in the chat body.");
        if (configuration.ProviderId == "inception") ValidateMercury(body);

        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, "chat/completions"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json");
        return request;
    }

    private static void ValidateMercury(JsonElement body)
    {
        if (body.TryGetProperty("reasoning_effort", out var effort) &&
            (effort.ValueKind != JsonValueKind.String || !MercuryReasoning.Contains(effort.GetString()!)))
            throw new ArgumentException("Mercury 3 reasoning effort must be instant, low, medium or high.");
        if (body.TryGetProperty("temperature", out var temperature) &&
            (temperature.ValueKind != JsonValueKind.Number || !temperature.TryGetDouble(out var value) ||
             !double.IsFinite(value) || value < 0.5 || value > 1.0))
            throw new ArgumentException("Mercury 3 temperature must be between 0.5 and 1.0.");
        foreach (var name in new[] { "max_tokens", "max_completion_tokens" })
            if (body.TryGetProperty(name, out var maximum) &&
                (maximum.ValueKind != JsonValueKind.Number || !maximum.TryGetInt64(out var tokens) || tokens < 1 || tokens > 131072))
                throw new ArgumentException("Mercury 3 maximum output must be between 1 and 131072 tokens.");
    }
}
