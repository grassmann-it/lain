using System.Text.Json;

namespace Lain.Host.Providers.Poe;

public sealed record PoeModelMetadata(string Id, string? PublisherClaim, string[] InputModalities,
    string[] AdvertisedEndpoints, long? ContextWindow, long? MaximumOutput,
    bool? Tools, string PricingClass, string? Manufacturer = null, string? InferenceProvider = null,
    string PublisherVerification = "provider_reported");

public static class PoeModelCatalog
{
    public const string Endpoint = "https://api.poe.com/v1/models";

    public static IReadOnlyList<PoeModelMetadata> Normalize(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("error", out _) || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return [];
        var models = new List<PoeModelMetadata>();
        foreach (var model in data.EnumerateArray())
        {
            var id = String(model, "id");
            if (string.IsNullOrWhiteSpace(id)) continue;
            var features = Strings(model, "supported_features");
            var context = Object(model, "context_window");
            var pricing = Object(model, "pricing");
            // Null pricing is documented for free OR request-priced models.
            // It never qualifies a route for free-only dispatch.
            var priced = pricing.ValueKind == JsonValueKind.Object && pricing.EnumerateObject().Any(p => PositivePrice(p.Value));
            models.Add(new(id, String(model, "owned_by"), Strings(Object(model, "architecture"), "input_modalities"),
                Strings(model, "supported_endpoints"), PositiveInteger(context, "context_length") ?? PositiveInteger(model, "context_length"),
                PositiveInteger(context, "max_output_tokens"), features.Contains("tools") ? true : null, priced ? "paid" : "unknown"));
        }
        return models;
    }

    private static bool PositivePrice(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) && number > 0
        || value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var text) && text > 0;
    private static JsonElement Object(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) ? value : default;
    private static string? String(JsonElement root, string name) => Object(root, name) is var value && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string[] Strings(JsonElement root, string name) => Object(root, name) is var value && value.ValueKind == JsonValueKind.Array
        ? value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToArray() : [];
    private static long? PositiveInteger(JsonElement root, string name) => Object(root, name) is var value && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var count) && count > 0 ? count : null;
}
