using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Providers;
using YamlDotNet.RepresentationModel;

namespace Lain.Host.Operations;

/// <summary>Compiles only private lain snapshots into the isolated proxy configuration.</summary>
public sealed class ProxyConfigurationWriter(OwnStatePaths paths, SecretStore secrets, ProviderCostPolicy? costPolicy = null)
{
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> RouteCredentials { get; private set; } = new Dictionary<string, IReadOnlyDictionary<string, string>>();
    public IReadOnlyDictionary<string, string> Write(ConfigurationDto state)
    {
        (costPolicy ?? ProviderCostPolicy.Embedded()).ValidateRuntime(state);
        var config = new YamlMappingNode
        {
            { "host", "127.0.0.1" }, { "port", OwnStatePaths.ProxyPort.ToString() }, { "auth-dir", paths.ProxyAuth },
            { "api-keys", new YamlSequenceNode(new YamlScalarNode(secrets.GetOrCreateToken(paths.InferenceToken))) },
            { "remote-management", new YamlMappingNode { { "allow-remote", "false" }, { "secret-key", secrets.GetOrCreateToken(paths.ManagementToken) }, { "disable-control-panel", "true" } } },
            { "debug", "false" }, { "logging-to-file", "false" }, { "request-log", "false" },
            { "usage-statistics-enabled", "false" }
        };
        // These are data-only routing sections. Never copy source paths, plugins, listener, logging or management settings.
        var latest = Path.Combine(paths.Imports, "latest.json");
        if (File.Exists(latest))
        {
            using var metadata = JsonDocument.Parse(File.ReadAllText(latest));
            var name = metadata.RootElement.GetProperty("snapshot").GetString()!;
            if (Path.GetFileName(name) != name) throw new InvalidDataException("Invalid own import metadata.");
            var source = Parse(File.ReadAllText(Path.Combine(paths.Imports, name)));
            foreach (var key in new[] { "routing", "oauth-model-alias", "oauth-excluded-models", "payload", "request-retry", "disable-image-generation" })
                if (LegacyImportService.Get(source, key) is { } value) config.Add(key, value);
            if (LegacyImportService.Get(config, "oauth-model-alias") is YamlMappingNode oauth)
                foreach (var aliases in oauth.Children.Values.OfType<YamlSequenceNode>()) NormalizeModelAliases(aliases);
        }
        var identities = new Dictionary<string, string>();
        var authFiles = new Dictionary<string, string>();
        var routeCredentials = state.Routes.Where(r => r.Enabled).ToDictionary(r => r.Id, _ => new Dictionary<string, string>());
        var metadataPath = Path.Combine(paths.Imports, "routes.json");
        var routeMetadata = File.Exists(metadataPath)
            ? JsonSerializer.Deserialize<Dictionary<string, LegacyImportService.ImportedRouteSource>>(File.ReadAllText(metadataPath)) ?? [] : [];
        foreach (var account in state.Accounts.Where(a => a.Enabled))
        {
            using var credential = JsonDocument.Parse(secrets.ReadAccount(account.Id));
            var secret = credential.RootElement;
            var kind = Read(secret, "kind");
            if (kind == "codex-owned")
            {
                var ownedFilename = Read(secret, "ownedAuthFile") ?? throw new InvalidDataException("Missing owned credential reference.");
                identities.Add(account.Id, "owned/" + ownedFilename);
                foreach (var route in state.Routes.Where(r => r.Enabled && r.AccountIds.Contains(account.Id))) routeCredentials[route.Id][account.Id] = "owned/" + ownedFilename;
                continue;
            }
            if (kind == "codex-access-snapshot")
            {
                var filename = account.Id + ".json";
                var auth = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["type"] = "codex", ["access_token"] = Read(secret, "accessToken"),
                    ["account_id"] = Read(secret, "accountId"), ["expired"] = Read(secret, "expiresAt"), ["disabled"] = false
                });
                authFiles[filename] = auth;
                identities.Add(account.Id, filename);
                foreach (var route in state.Routes.Where(r => r.Enabled && r.AccountIds.Contains(account.Id))) routeCredentials[route.Id][account.Id] = filename;
                continue;
            }
            if (kind != "api-key") throw new InvalidDataException("Unsupported private credential kind.");
            var transports = secret.TryGetProperty("transports", out var variants) ? variants.EnumerateArray().ToArray() : [secret];
            var descriptors = transports.Select((transport, index) => new
            {
                Secret = transport,
                Id = Read(transport, "transportId") ?? "default-" + index,
                Models = Read(transport, "sourceConfig") is { } sourceConfig && LegacyImportService.Get(Parse(sourceConfig), "models") is YamlSequenceNode models
                    ? models.Children.OfType<YamlMappingNode>().Select(m => LegacyImportService.Text(m, "name")).Where(m => m is not null).ToHashSet() : []
            }).ToArray();
            var selectedRoutes = new Dictionary<string, string>();
            foreach (var route in state.Routes.Where(r => r.Enabled && r.AccountIds.Contains(account.Id)))
            {
                var mapped = routeMetadata.TryGetValue(route.Id, out var metadata) && metadata.AccountId == account.Id ? metadata.TransportId : null;
                var matches = mapped is not null ? descriptors.Where(d => d.Id == mapped).ToArray()
                    : descriptors.Where(d => d.Models.Contains(route.UpstreamModel)).ToArray();
                if (matches.Length == 0 && mapped is null && descriptors.Length == 1) matches = descriptors;
                if (matches.Length != 1) throw new InvalidOperationException("Route transport is missing or ambiguous; select an imported transport-specific route.");
                selectedRoutes[route.Id] = matches[0].Id;
            }
            var selectedTransports = selectedRoutes.Count > 0 ? descriptors.Where(d => selectedRoutes.ContainsValue(d.Id)) : descriptors.Take(1);
            foreach (var descriptor in selectedTransports)
            {
                var transport = descriptor.Secret;
                var transportId = descriptor.Id;
                var coreProvider = Read(transport, "coreProvider") ?? account.Provider;
                var section = Read(transport, "sourceSection") ?? "openai-compatibility";
                if (section is not ("openai-compatibility" or "codex-api-key" or "claude-api-key"))
                    throw new InvalidDataException("Unsupported source section.");
                var provider = Read(transport, "sourceConfig") is { } sourceConfig ? Parse(sourceConfig) : new YamlMappingNode();
                if ((section is "codex-api-key" or "claude-api-key") && LegacyImportService.Get(provider, "headers") is YamlMappingNode headers && headers.Children.Count > 0)
                    throw new InvalidDataException("Custom direct-provider headers require additional qualification.");
                var key = Read(transport, "apiKey") ?? throw new InvalidDataException("Missing own API credential.");
                var endpoint = Read(transport, "baseUrl") ?? "";
                if (endpoint.Length > 0) { AccountService.ValidateEndpoint(endpoint); Set(provider, "base-url", new YamlScalarNode(endpoint)); }
                provider.Children.Remove(new YamlScalarNode("disabled"));
                // Public lain aliases are resolved by bindings. Core-private aliases use actual model names.
                var models = LegacyImportService.Get(provider, "models") as YamlSequenceNode ?? new YamlSequenceNode();
                NormalizeModelAliases(models);
                foreach (var route in selectedRoutes.Where(pair => pair.Value == transportId).Select(pair => state.Routes.Single(r => r.Id == pair.Key)))
                    if (!models.Children.OfType<YamlMappingNode>().Any(m => LegacyImportService.Text(m, "name") == route.UpstreamModel))
                        models.Add(new YamlMappingNode { { "name", route.UpstreamModel }, { "alias", route.UpstreamModel } });
                Set(provider, "models", models);
                var proxyUrl = LegacyImportService.Text(provider, "proxy-url") ?? "";
                if (section == "openai-compatibility")
                {
                    Set(provider, "name", new YamlScalarNode(coreProvider));
                    var selectedEntry = Read(transport, "entryConfig") is { } entryConfig ? Parse(entryConfig) : new YamlMappingNode();
                    Set(selectedEntry, "api-key", new YamlScalarNode(key));
                    selectedEntry.Children.Remove(new YamlScalarNode("disabled"));
                    proxyUrl = LegacyImportService.Text(selectedEntry, "proxy-url") ?? "";
                    Set(provider, "api-key-entries", new YamlSequenceNode(selectedEntry));
                    provider.Children.Remove(new YamlScalarNode("api-key"));
                }
                else Set(provider, "api-key", new YamlScalarNode(key));
                if (LegacyImportService.Get(config, section) is not YamlSequenceNode entries) { entries = new(); config.Add(section, entries); }
                entries.Add(provider);
                var coreId = CoreCredentialId(section, coreProvider, key, endpoint, proxyUrl, LegacyImportService.Text(provider, "prefix") ?? "");
                identities.TryAdd(account.Id, coreId);
                foreach (var pair in selectedRoutes.Where(pair => pair.Value == transportId)) routeCredentials[pair.Key][account.Id] = coreId;
            }
        }
        foreach (var route in state.Routes.Where(r => r.Enabled))
            if (route.AccountIds.Any(id => !routeCredentials[route.Id].ContainsKey(id))) throw new InvalidOperationException("Route contains an unavailable account credential.");
        // Only own generated auth files are ever deleted. External source directories are never consulted here.
        foreach (var filename in Directory.EnumerateFiles(paths.ProxyAuth, "acct-*.json"))
            if (!authFiles.ContainsKey(Path.GetFileName(filename))) File.Delete(filename);
        foreach (var pair in authFiles) OwnStatePaths.WritePrivate(Path.Combine(paths.ProxyAuth, pair.Key), pair.Value);
        var output = new StringBuilder();
        using (var writer = new StringWriter(output)) new YamlStream(new YamlDocument(config)).Save(writer, false);
        OwnStatePaths.WritePrivate(paths.ProxyConfig, output.ToString());
        RouteCredentials = routeCredentials.ToDictionary(p => p.Key, p => (IReadOnlyDictionary<string, string>)p.Value);
        return identities;
    }
    public static string CoreCredentialId(string section, string provider, string key, string endpoint, string proxyUrl, string prefix = "")
    {
        var kind = section == "openai-compatibility" ? "openai-compatibility:" + provider.Trim().ToLowerInvariant() : section.Replace("-api-key", "") + ":apikey";
        var input = kind + "\0" + key.Trim() + "\0" + endpoint.Trim() + "\0" + proxyUrl.Trim();
        if (section != "openai-compatibility") input += "\0" + prefix.Trim() + "\0";
        return kind + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant()[..12];
    }
    private static void NormalizeModelAliases(YamlSequenceNode models)
    {
        foreach (var model in models.Children.OfType<YamlMappingNode>())
            if (LegacyImportService.Text(model, "name") is { } modelName) Set(model, "alias", new YamlScalarNode(modelName));
    }
    private static string? Read(JsonElement node, string name) => node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static void Set(YamlMappingNode mapping, string key, YamlNode value) => mapping.Children[new YamlScalarNode(key)] = value;
    private static YamlMappingNode Parse(string text)
    {
        var yaml = new YamlStream(); using var reader = new StringReader(text); yaml.Load(reader);
        return yaml.Documents.Single().RootNode as YamlMappingNode ?? throw new InvalidDataException("Invalid private provider configuration.");
    }
}
