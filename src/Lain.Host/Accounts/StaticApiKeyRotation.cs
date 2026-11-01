using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.RepresentationModel;

namespace Lain.Host.Accounts;

/// <summary>Pure transport-template handoff. No storage lookup, generation fallback or old secret selection.</summary>
public static class StaticApiKeyRotation
{
    private const int MaximumJsonCharacters = 1024 * 1024;
    private static readonly HashSet<string> Fields = new(StringComparer.Ordinal)
    {
        "kind", "apiKey", "provider", "coreProvider", "sourceSection", "transportId", "baseUrl", "settings",
        "providerRouteId", "providerRouteIds", "providerConnectionId", "sourceConfig", "entryConfig",
        "displayNameIsExplicit", "sourceEnabled", "origin", "transports"
    };

    /// <summary>Issue a descriptor with the selected template's transports and only the intended new API key.</summary>
    public static string Rotate(string templateJson, string newKey)
    {
        ValidateKey(newKey);
        var root = Parse(templateJson);
        var previous = new HashSet<string>(StringComparer.Ordinal);
        CollectKeys(root, previous);
        previous.Remove(newKey);
        var clean = CleanTransport(root, newKey, previous, true);
        var result = clean.ToJsonString();
        if (result.Length > MaximumJsonCharacters) throw Invalid();
        AssertNoPreviousKey(clean, previous);
        return result;
    }

    /// <summary>Reconnect may preserve a supplied matching endpoint/settings; changing a transport is separate.</summary>
    public static void ValidateReconnectOptions(string templateJson, string? baseUrl,
        IReadOnlyDictionary<string, string>? settings)
    {
        var root = Parse(templateJson);
        var transports = root["transports"] is JsonArray variants
            ? variants.Select(v => v as JsonObject ?? throw Invalid()).Prepend(root).ToArray() : [root];
        if (baseUrl is not null && !transports.Any(t =>
            t["baseUrl"] is JsonValue endpoint && endpoint.TryGetValue<string>(out var value) &&
            StringComparer.Ordinal.Equals(value.TrimEnd('/'), baseUrl.TrimEnd('/')))) throw TransportChange();
        if (settings is not null)
            foreach (var setting in settings)
            {
                var configured = transports.Select(t => t["settings"] as JsonObject)
                    .Where(s => s?.ContainsKey(setting.Key) == true).Select(s => s![setting.Key]).ToArray();
                if (configured.Length == 0 || configured.Any(v => v is not JsonValue value ||
                    !value.TryGetValue<string>(out var text) || text != setting.Value)) throw TransportChange();
            }
    }

    private static JsonObject Parse(string json)
    {
        if (json.Length > MaximumJsonCharacters) throw Invalid();
        try { return JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = 64 }) as JsonObject ?? throw Invalid(); }
        catch (JsonException) { throw Invalid(); }
    }

    private static void CollectKeys(JsonObject source, HashSet<string> keys)
    {
        if (source["kind"]?.GetValue<string>() != "api-key") throw Invalid();
        if (source["apiKey"] is not JsonValue value || !value.TryGetValue<string>(out var key) || string.IsNullOrWhiteSpace(key)) throw Invalid();
        keys.Add(key);
        if (source["transports"] is not null)
        {
            if (source["transports"] is not JsonArray { Count: > 0 and <= 256 } variants) throw Invalid();
            foreach (var transport in variants)
            {
                if (transport is not JsonObject item || item["transports"] is not null) throw Invalid();
                CollectKeys(item, keys);
            }
        }
    }

    private static JsonObject CleanTransport(JsonObject source, string newKey, HashSet<string> previous, bool root)
    {
        if (source.Any(p => !Fields.Contains(p.Key))) throw Invalid();
        var clean = new JsonObject();
        foreach (var property in source)
        {
            if (property.Key is "apiKey" or "transports" or "origin") continue;
            if (property.Key is "sourceConfig" or "entryConfig")
            {
                if (property.Value is not JsonValue yaml || !yaml.TryGetValue<string>(out var text)) throw Invalid();
                clean[property.Key] = CleanYaml(text, previous);
            }
            else clean[property.Key] = property.Value?.DeepClone();
        }
        clean["apiKey"] = newKey;
        clean["origin"] = "api-key-rotation";
        if (root && source["transports"] is JsonArray transports)
            clean["transports"] = new JsonArray(transports.Select(t => (JsonNode)CleanTransport(t!.AsObject(), newKey, previous, false)).ToArray());
        return clean;
    }

    private static string CleanYaml(string text, HashSet<string> previous)
    {
        if (text.Length > MaximumJsonCharacters) throw Invalid();
        var yaml = new YamlStream();
        try { yaml.Load(new StringReader(text)); }
        catch (YamlDotNet.Core.YamlException) { throw Invalid(); }
        if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode mapping) throw Invalid();
        var seen = new HashSet<YamlNode>(ReferenceEqualityComparer.Instance);
        var nodes = 0;
        Clean(mapping, 0);
        using var output = new StringWriter();
        new YamlStream(new YamlDocument(mapping)).Save(output, false);
        return output.ToString();

        void Clean(YamlNode node, int depth)
        {
            if (depth > 64 || ++nodes > 100000 || !seen.Add(node)) throw Invalid();
            if (node is YamlMappingNode map)
            {
                foreach (var pair in map.Children.ToArray())
                {
                    if (pair.Key is not YamlScalarNode key || key.Value is null) throw Invalid();
                    var normalized = key.Value.Replace("-", "").Replace("_", "").ToLowerInvariant();
                    if (normalized is "apikey" or "apikeyentries" or "authorization" or "proxyauthorization" or
                        "xapikey" or "xgoogapikey" or "accesstoken" or "refreshtoken" or "bearertoken" or
                        "cookie" or "setcookie" or "clientsecret" or "password" or "secret")
                    { map.Children.Remove(pair.Key); continue; }
                    if (previous.Any(old => key.Value.Contains(old, StringComparison.Ordinal))) throw Invalid();
                    Clean(pair.Value, depth + 1);
                }
            }
            else if (node is YamlSequenceNode sequence)
                foreach (var child in sequence.Children) Clean(child, depth + 1);
            else if (node is YamlScalarNode scalar && scalar.Value is { } value &&
                previous.Any(old => value.Contains(old, StringComparison.Ordinal))) throw Invalid();
        }
    }

    private static void AssertNoPreviousKey(JsonNode node, HashSet<string> previous)
    {
        if (node is JsonObject obj)
            foreach (var property in obj)
            {
                if (previous.Any(old => property.Key.Contains(old, StringComparison.Ordinal))) throw Invalid();
                if (property.Value is { } child) AssertNoPreviousKey(child, previous);
            }
        else if (node is JsonArray array)
            foreach (var child in array.OfType<JsonNode>()) AssertNoPreviousKey(child, previous);
        else if (node is JsonValue value && value.TryGetValue<string>(out var text) &&
            previous.Any(old => text.Contains(old, StringComparison.Ordinal))) throw Invalid();
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 65536 || key.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)) ||
            key.StartsWith("sk-nano-mgmt-", StringComparison.Ordinal)) throw new ArgumentException("A valid inference API key is required.");
    }
    private static InvalidDataException Invalid() => new("Existing API transport metadata cannot be safely rotated; review the selected template.");
    private static ArgumentException TransportChange() => new("API-key reconnect preserves existing transports. Change endpoint or provider settings through an explicit transport-change workflow.");
}
