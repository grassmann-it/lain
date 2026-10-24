using System.Text.Json;

namespace Lain.Host.Accounts.Managed;

/// <summary>Candidate monitor protocol boundary only; no process launcher or filesystem credential reader.</summary>
public static class CodexAppServerProtocol
{
    public static string Request(long id, string method, object? parameters = null)
    {
        if (id <= 0 || method is not ("account/read" or "account/rateLimits/read"))
            throw new ArgumentException("Only account and rate-limit reads are locally qualified.");
        if (parameters is not null) throw new ArgumentException("Caller-controlled read parameters are not supported.");
        return method == "account/read"
            ? JsonSerializer.Serialize(new { id, method, @params = new { refreshToken = false } })
            : JsonSerializer.Serialize(new { id, method });
    }
    public static JsonElement Result(JsonElement message, long expectedId)
    {
        var id = ManagedJson.Field(message, "id");
        if (id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var actual) || actual != expectedId || expectedId <= 0)
            throw ManagedJson.Invalid();
        if (ManagedJson.Field(message, "error").ValueKind != JsonValueKind.Undefined)
            throw new InvalidDataException("Codex owner request failed; reconnect the isolated owner.");
        if (ManagedJson.Field(message, "method").ValueKind != JsonValueKind.Undefined) throw ManagedJson.Invalid();
        var result = ManagedJson.Field(message, "result");
        if (result.ValueKind != JsonValueKind.Object) throw ManagedJson.Invalid();
        return result.Clone();
    }
    public static bool HasManagedChatGptAccount(JsonElement result)
    {
        var account = ManagedJson.Field(result, "account");
        return account.ValueKind == JsonValueKind.Object && ManagedJson.Text(account, "type") == "chatgpt";
    }
    // Email and planType are display hints only. They are deliberately never promoted into ResourceBindingDto.Subject.
}
