using System.Text.Json.Nodes;
using Lain.Host.Accounts.Credentials;
using Lain.Host.Operations;
using Lain.Host.Providers;
using YamlDotNet.RepresentationModel;

namespace Lain.Host.Accounts.Kilo;

/// <summary>Transfers qualified routing metadata, never an earlier token or organization header.</summary>
internal sealed class KiloTransportHandoff(ConfigurationRepository configuration, SecretStore secrets)
{
    public string? Capture(LoginOperationContext context)
    {
        var before = configuration.Snapshot;
        var account = before.Accounts.SingleOrDefault(a => a.Id == context.AccountId);
        if (before.Revision != context.Revision || account is null || account.Generation != context.AccountGeneration
            || account.Provider != "kilo") throw Invalid();
        var resolver = new CredentialResolver(configuration, secrets);
        var access = resolver.Resolve(account.Id, "inference");
        // Attaching inference to a quota-only account has no transport to inherit.
        if (access is null) return null;
        if (access.Handle is { Owner: not "static-key" }) throw Invalid();
        var original = JsonNode.Parse(access.PrivateJson)?.AsObject() ?? throw Invalid();
        if (original["kind"]?.GetValue<string>() != "api-key") throw Invalid();
        var clean = Transport(original);
        if (original["transports"] is JsonArray variants)
        {
            if (variants.Count is < 1 or > 16) throw Invalid();
            clean["transports"] = new JsonArray(variants.Select(v => (JsonNode)Transport(v?.AsObject() ?? throw Invalid())).ToArray());
        }
        if (!resolver.IsCurrent(access) || configuration.Snapshot.Revision != context.Revision
            || configuration.Snapshot.Accounts.SingleOrDefault(a => a.Id == account.Id)?.Generation != context.AccountGeneration) throw Invalid();
        return clean.ToJsonString();
    }

    public static string Issue(string? template, string token)
    {
        var credential = template is null ? new JsonObject { ["kind"] = "api-key", ["provider"] = "kilo",
            ["coreProvider"] = "kilo", ["sourceSection"] = "openai-compatibility", ["baseUrl"] = KiloDeviceLoginAdapter.DefaultBaseUrl }
            : JsonNode.Parse(template)!.AsObject();
        credential["apiKey"] = token;
        credential["origin"] = "kilo-device";
        if (credential["transports"] is JsonArray transports)
            foreach (var transport in transports.OfType<JsonObject>()) { transport["apiKey"] = token; transport["origin"] = "kilo-device"; }
        return credential.ToJsonString();
    }

    private static JsonObject Transport(JsonObject source)
    {
        var endpoint = source["baseUrl"]?.GetValue<string>()?.TrimEnd('/');
        var core = source["coreProvider"]?.GetValue<string>();
        if (endpoint != KiloDeviceLoginAdapter.DefaultBaseUrl || source["provider"]?.GetValue<string>() is not (null or "kilo")
            || core is not (null or "kilo" or "kilo-openai-chat")
            || source["sourceSection"]?.GetValue<string>() is not (null or "openai-compatibility")) throw Invalid();
        var clean = new JsonObject { ["kind"] = "api-key", ["provider"] = "kilo", ["coreProvider"] = core ?? "kilo",
            ["sourceSection"] = "openai-compatibility", ["baseUrl"] = endpoint };
        var registry = new ProviderCatalogRegistry();
        foreach (var name in new[] { "providerRouteId", "providerConnectionId", "transportId" })
            if (source[name] is { } value)
            {
                var text = value.GetValue<string>();
                if (string.IsNullOrWhiteSpace(text) || text.Length > 256 || text.Any(char.IsControl)) throw Invalid();
                if (name == "providerRouteId" && registry.FindRoute("kilo", text) is null
                    || name == "providerConnectionId" && registry.FindConnection("kilo", text) is null) throw Invalid();
                clean[name] = text;
            }
        if (source["providerRouteIds"] is JsonArray routes)
        {
            if (routes.Count > 100 || routes.Any(r => r is null || registry.FindRoute("kilo", r.GetValue<string>()) is null)) throw Invalid();
            clean["providerRouteIds"] = routes.DeepClone();
        }
        if (source["settings"] is JsonObject settings)
        {
            // Current personal Kilo transport needs only its existing billing choice.
            if (settings.Any(p => p.Key != "billingPlan" || p.Value?.GetValue<string>() != "free-models")) throw Invalid();
            clean["settings"] = settings.DeepClone();
        }
        // Unknown private transport additions must be reviewed, not forwarded to a new token.
        foreach (var property in source)
            if (property.Key is not ("kind" or "provider" or "coreProvider" or "sourceSection" or "baseUrl" or "apiKey"
                or "origin" or "providerRouteId" or "providerRouteIds" or "providerConnectionId" or "transportId"
                or "settings" or "transports" or "sourceConfig" or "entryConfig")) throw Invalid();
        foreach (var name in new[] { "sourceConfig", "entryConfig" })
            if (source[name] is { } yaml) clean[name] = SafeYaml(yaml.GetValue<string>(), name == "entryConfig");
        return clean;
    }

    private static string SafeYaml(string text, bool entry)
    {
        if (text.Length > 65536) throw Invalid();
        var yaml = new YamlStream();
        try { yaml.Load(new StringReader(text)); }
        catch (YamlDotNet.Core.YamlException) { throw Invalid(); }
        if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode mapping) throw Invalid();
        var clean = new YamlMappingNode();
        foreach (var pair in mapping.Children)
        {
            if (pair.Key is not YamlScalarNode key) throw Invalid();
            if (key.Value is "api-key" or "api-key-entries" or "disabled") continue; // old secrets are never carried forward
            if (key.Value is "headers" && pair.Value is YamlMappingNode { Children.Count: 0 }) continue;
            if (key.Value is "proxy-url" && pair.Value is YamlScalarNode { Value: null or "" }) continue;
            if (key.Value is "prefix" or "priority" || !entry && key.Value is "name" or "base-url" or "upstream-protocol")
            {
                if (pair.Value is not YamlScalarNode value || value.Value?.Any(char.IsControl) == true) throw Invalid();
                if (key.Value == "base-url" && value.Value?.TrimEnd('/') != KiloDeviceLoginAdapter.DefaultBaseUrl
                    || key.Value == "upstream-protocol" && value.Value != "openai-chat") throw Invalid();
                clean.Add(key, value); continue;
            }
            if (!entry && key.Value == "models" && pair.Value is YamlSequenceNode models)
            {
                foreach (var model in models.Children)
                    if (model is not YamlMappingNode row || row.Children.Any(p => p.Key is not YamlScalarNode { Value: "name" or "alias" }
                        || p.Value is not YamlScalarNode scalar || scalar.Value is null || scalar.Value.Length > 512 || scalar.Value.Any(char.IsControl))) throw Invalid();
                clean.Add(key, models); continue;
            }
            if (!entry && key.Value == "excluded-models" && pair.Value is YamlSequenceNode exclusions
                && exclusions.Children.All(v => v is YamlScalarNode { Value: not null } scalar && scalar.Value.Length <= 512 && !scalar.Value.Any(char.IsControl)))
            { clean.Add(key, exclusions); continue; }
            throw Invalid();
        }
        using var output = new StringWriter(); new YamlStream(new YamlDocument(clean)).Save(output, false); return output.ToString();
    }

    private static InvalidOperationException Invalid() => new("Existing Kilo transport requires local review before browser reconnect. No authorization was started.");
}
