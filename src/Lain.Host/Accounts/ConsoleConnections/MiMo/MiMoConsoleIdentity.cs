using System.Text.Json;

namespace Lain.Host.Accounts.ConsoleConnections.MiMo;

// Official 2026-10-24 public main bundle: refreshUserInfo /userProfile.
// Its unknown-idc fallback to SGP is deliberately NOT a region verification.
public static class MiMoConsoleIdentity
{
    public static bool IsIdentifier(string? value) => value is { Length: > 0 and <= 128 }
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    public static bool Matches(JsonElement payload, ConsoleResourceBinding expected)
    {
        if (!TrySuccessData(payload, out var data)
            || data.ValueKind != JsonValueKind.Object || expected.Product != "individual-token-plan") return false;
        var id = Identifier(data, "userId") ?? Identifier(data, "id");
        // Conflicting identity fields are not silently resolved using precedence.
        var alternate = Identifier(data, "id");
        if (data.TryGetProperty("userId", out _) && Identifier(data, "userId") is null
            || data.TryGetProperty("id", out _) && alternate is null) return false;
        if (id is null || alternate is not null && id != alternate || id != expected.Subject) return false;
        if (!data.TryGetProperty("idc", out var idc) || idc.ValueKind != JsonValueKind.Number || !idc.TryGetInt32(out var cluster)) return false;
        var region = cluster switch { 310 => "CN", 314 => "EU", _ => null };
        return region is not null && region == expected.Region;
    }
    public static bool TrySuccessData(JsonElement payload, out JsonElement data)
    {
        data = default;
        // Do not inherit the website's acceptance of application-code 429 as
        // successful quota data. HTTP 200 alone does not establish success.
        return payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("code", out var code)
            && code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var n) && n is 0 or 200
            && payload.TryGetProperty("data", out data) && data.ValueKind == JsonValueKind.Object;
    }
    private static string? Identifier(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var field)) return null;
        var id = field.ValueKind == JsonValueKind.String ? field.GetString()
            : field.ValueKind == JsonValueKind.Number && field.TryGetInt64(out var number) && number >= 0
                ? number.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        return IsIdentifier(id) ? id : null;
    }
}
