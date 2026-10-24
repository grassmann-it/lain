using System.Text.Json;

namespace Lain.Host.Accounts.Managed;

/// <summary>Reject ambiguous controls before any candidate protocol could use them.</summary>
internal static class ManagedJson
{
    public static JsonElement Field(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object) throw Invalid();
        JsonElement value = default;
        var seen = false;
        foreach (var property in parent.EnumerateObject())
        {
            if (property.Name != name) continue;
            if (seen) throw Invalid();
            seen = true; value = property.Value;
        }
        return value;
    }
    public static string? Text(JsonElement parent, string name) => Field(parent, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
    public static double? Number(JsonElement parent, string name)
    {
        var value = Field(parent, name);
        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;
    }
    public static bool Success(JsonElement root)
    {
        var code = Field(root, "code");
        return (code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var n) && n is 0 or 200) ||
            (code.ValueKind == JsonValueKind.String && code.GetString() is "0" or "200");
    }
    public static InvalidDataException Invalid() => new("Managed provider response is invalid or ambiguous.");
}
