using System.Text.Json;
using System.Text.RegularExpressions;
using Lain.Core;

namespace Lain.Host.Accounts.CliConnections;

public static class FireworksCliResult
{
    public static string Identifier(string value)
    {
        if (value.Length is < 1 or > 100 || !value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            throw new InvalidDataException("Invalid Fireworks identifier.");
        return value;
    }
    public static string AuthorizationUrl(string candidate)
    {
        if (candidate.Length > 8192 || candidate.Any(char.IsControl) || !Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0
            || !Regex.IsMatch(uri.Host, @"^fireworks-[a-z0-9-]+\.auth\.us-west-2\.amazoncognito\.com$", RegexOptions.CultureInvariant)
            || uri.AbsolutePath is not ("/oauth2/authorize" or "/login" or "/logout")) throw new InvalidDataException("Unqualified Fireworks authorization URL.");
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split('=', 2); var name = Uri.UnescapeDataString(pieces[0]);
            if (!seen.Add(name) || name is not ("client_id" or "response_type" or "scope" or "redirect_uri" or "state"
                or "code_challenge" or "code_challenge_method" or "identity_provider" or "prompt"))
                throw new InvalidDataException("Unqualified Fireworks authorization parameter.");
            if (name == "redirect_uri")
            {
                if (pieces.Length != 2 || !Uri.TryCreate(Uri.UnescapeDataString(pieces[1]), UriKind.Absolute, out var callback)
                    || callback.Scheme != "http" || callback.UserInfo.Length != 0 || callback.Fragment.Length != 0
                    || callback.Host is not ("127.0.0.1" or "localhost" or "[::1]"))
                    throw new InvalidDataException("Fireworks authorization callback is not local.");
            }
        }
        if (!seen.Contains("client_id") || !seen.Contains("redirect_uri") || !seen.Contains("state")) throw new InvalidDataException("Incomplete Fireworks authorization URL.");
        return uri.AbsoluteUri;
    }
    public static ResourceBindingDto[] Accounts(ReadOnlyMemory<byte> output)
    {
        using var document = JsonDocument.Parse(output, new JsonDocumentOptions { MaxDepth = 16 });
        var root = document.RootElement;
        if (root.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object)) throw new InvalidDataException("Invalid Fireworks account response.");
        var list = root.ValueKind == JsonValueKind.Array ? root : root.TryGetProperty("accounts", out var found) ? found : default;
        if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is < 1 or > 200) throw new InvalidDataException("Invalid Fireworks account list.");
        // Refuse incomplete pagination instead of silently hiding accounts and selecting a default.
        if (root.ValueKind == JsonValueKind.Object && Text(root, "nextPageToken", "next_page_token") is { Length: > 0 })
            throw new InvalidDataException("Fireworks account list is incomplete.");
        var names = new HashSet<string>(StringComparer.Ordinal); var result = new List<ResourceBindingDto>();
        foreach (var node in list.EnumerateArray())
        {
            if (node.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid Fireworks account entry.");
            var name = Text(node, "name") ?? throw new InvalidDataException("Fireworks account name is missing.");
            if (!name.StartsWith("accounts/", StringComparison.Ordinal)) throw new InvalidDataException("Fireworks account resource differs.");
            var id = Identifier(name[9..]);
            if (!names.Add(id)) throw new InvalidDataException("Duplicate Fireworks account.");
            result.Add(new("fireworks", Product: "serverless", ResourceType: "account", ResourceId: id, Provenance: "official-cli-account-list"));
        }
        return result.ToArray();
    }
    public static PreparedLoginCredential Key(ReadOnlyMemory<byte> output, string accountId, string keyName, string operationId, string profileReference, string[] purposes)
    {
        Identifier(accountId); Identifier(keyName); Identifier(operationId); Identifier(profileReference);
        using var document = JsonDocument.Parse(output, new JsonDocumentOptions { MaxDepth = 12 }); var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid Fireworks key result.");
        var key = Text(root, "key"); var id = Identifier(Text(root, "keyId", "key_id") ?? "");
        if (Text(root, "displayName", "display_name") != keyName || key is not { Length: > 0 and <= 16384 }
            || key.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)) || key.StartsWith("fpk_", StringComparison.OrdinalIgnoreCase)
            || Boolean(root, "isFirepass", "is_firepass")) throw new InvalidDataException("Fireworks result is not the newly named ordinary API key.");
        var binding = new ResourceBindingDto("fireworks", Product: "serverless", ResourceType: "account", ResourceId: accountId, Provenance: "official-cli-key-create");
        var payload = JsonSerializer.Serialize(new { kind = "api-key", provider = "fireworks", coreProvider = "fireworks",
            sourceSection = "openai-compatibility", apiKey = key, baseUrl = "https://api.fireworks.ai/inference/v1", origin = "official-cli",
            cliRelease = FireworksCliQualification.Version, cliPublicationCommit = FireworksCliQualification.TapCommit,
            cliOperationId = operationId, cliProfileReference = profileReference, remoteKeyId = id,
            settings = new { accountId } });
        return new("static-key", purposes, binding, payload, PreserveForRecovery: true, RemoteId: id);
    }
    private static string? Text(JsonElement root, string name, string? alias = null)
    {
        var first = root.TryGetProperty(name, out var a); var second = alias is not null && root.TryGetProperty(alias, out _);
        if (first && second) throw new InvalidDataException("Ambiguous Fireworks result fields.");
        var field = first ? a : second ? root.GetProperty(alias!) : default;
        if (field.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return null;
        if (field.ValueKind != JsonValueKind.String) throw new InvalidDataException("Invalid Fireworks result field.");
        return field.GetString();
    }
    private static bool Boolean(JsonElement root, string name, string alias)
    {
        if (root.TryGetProperty(name, out var a) && root.TryGetProperty(alias, out _)) throw new InvalidDataException("Ambiguous Fireworks key type.");
        if (a.ValueKind == JsonValueKind.Undefined) root.TryGetProperty(alias, out a);
        return a.ValueKind switch { JsonValueKind.Undefined => false, JsonValueKind.False => false, JsonValueKind.True => true,
            _ => throw new InvalidDataException("Invalid Fireworks key type.") };
    }
}
