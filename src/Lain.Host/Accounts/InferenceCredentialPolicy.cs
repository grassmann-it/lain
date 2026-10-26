using System.Text.Json;

namespace Lain.Host.Accounts;

/// <summary>Management-only secrets never cross the inference or model-access boundary.</summary>
public static class InferenceCredentialPolicy
{
    public static void RejectManagementToken(string? value)
    {
        if (value?.StartsWith("sk-nano-mgmt-", StringComparison.Ordinal) == true)
            throw new ArgumentException("NanoGPT management tokens are only for subscription monitoring. Use an inference API key for model access.");
    }

    public static void ValidatePrivateJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("kind", out var kind) && kind.GetString() == "monitor-access")
            throw new InvalidDataException("A monitor credential cannot provide inference or model access.");
        Check(document.RootElement);
        static void Check(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
                foreach (var property in value.EnumerateObject())
                {
                    if (property.Name is "apiKey" or "accessToken" or "access_token" or "bearerToken"
                        && property.Value.ValueKind == JsonValueKind.String)
                        RejectManagementToken(property.Value.GetString());
                    else if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array) Check(property.Value);
                }
            else if (value.ValueKind == JsonValueKind.Array)
                foreach (var item in value.EnumerateArray()) Check(item);
        }
    }
}
