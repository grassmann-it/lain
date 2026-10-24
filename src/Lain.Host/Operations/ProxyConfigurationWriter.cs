using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Providers;
using Lain.Host.Models;
using YamlDotNet.RepresentationModel;

namespace Lain.Host.Operations;

/// <summary>Credential identities and transport provenance only; contains no credential material.</summary>
public sealed record ProxyTransportDescription(
    IReadOnlyDictionary<string, string> CredentialIds,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> RouteCredentials,
    IReadOnlySet<string> QualifiedProviderRouteIds,
    IReadOnlyDictionary<string, ModelCompatibilityTransportBinding> TransportBindings);

/// <summary>Compiles only private lain snapshots into the isolated proxy configuration.</summary>
public sealed class ProxyConfigurationWriter(OwnStatePaths paths, SecretStore secrets, ProviderCostPolicy? costPolicy = null)
{
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> RouteCredentials { get; private set; } = new Dictionary<string, IReadOnlyDictionary<string, string>>();
    public IReadOnlySet<string> QualifiedProviderRouteIds { get; private set; } = new HashSet<string>();
    public IReadOnlyDictionary<string, ModelCompatibilityTransportBinding> TransportBindings { get; private set; } = new Dictionary<string, ModelCompatibilityTransportBinding>();

    /// <summary>Uses exactly the runtime compiler, without creating tokens or touching generated runtime files.</summary>
    public ProxyTransportDescription Describe(ConfigurationDto state) => Compile(state).Description;

    public IReadOnlyDictionary<string, string> Write(ConfigurationDto state)
    {
        var compiled = Compile(state);
        // Compilation, credential resolution and binding validation all precede the first mutation.
        Set(compiled.Config, "api-keys", new YamlSequenceNode(new YamlScalarNode(secrets.GetOrCreateToken(paths.InferenceToken))));
        Set(compiled.Config, "remote-management", new YamlMappingNode { { "allow-remote", "false" },
            { "secret-key", secrets.GetOrCreateToken(paths.ManagementToken) }, { "disable-control-panel", "true" } });
        foreach (var filename in Directory.EnumerateFiles(paths.ProxyAuth, "acct-*.json"))
            if (!compiled.AuthFiles.ContainsKey(Path.GetFileName(filename))) File.Delete(filename);
        foreach (var pair in compiled.AuthFiles) OwnStatePaths.WritePrivate(Path.Combine(paths.ProxyAuth, pair.Key), pair.Value);
        var output = new StringBuilder();
        using (var writer = new StringWriter(output)) new YamlStream(new YamlDocument(compiled.Config)).Save(writer, false);
        OwnStatePaths.WritePrivate(paths.ProxyConfig, output.ToString());
        RouteCredentials = compiled.Description.RouteCredentials;
        QualifiedProviderRouteIds = compiled.Description.QualifiedProviderRouteIds;
        TransportBindings = compiled.Description.TransportBindings;
        return compiled.Description.CredentialIds;
    }

    private sealed record Compilation(YamlMappingNode Config, Dictionary<string, string> AuthFiles, ProxyTransportDescription Description);
    private Compilation Compile(ConfigurationDto state)
    {
        (costPolicy ?? ProviderCostPolicy.Embedded()).ValidateRuntime(state);
        var catalog = costPolicy?.Catalog ?? new ProviderCatalogRegistry();
        var qualifiedProviderRouteIds = new HashSet<string>(StringComparer.Ordinal);
        var transportBindings = new Dictionary<string, ModelCompatibilityTransportBinding>(StringComparer.Ordinal);
        var activeRoutes = state.Routes.Where(r => r.Enabled).ToArray();
        var referencedAccounts = activeRoutes.SelectMany(r => r.AccountIds).ToHashSet(StringComparer.Ordinal);
        foreach (var id in referencedAccounts)
        {
            var account = state.Accounts.SingleOrDefault(a => a.Id == id);
            if (account is null || !account.Enabled || AccountService.EffectiveCapabilities(account).LastOrDefault(c => c.Purpose == "inference")?.State is not ("unverified" or "available"))
                throw new InvalidOperationException("Active route requires an enabled account with inference capability.");
        }
        foreach (var route in activeRoutes)
            if (route.AccountIds.Any(id => !string.Equals(state.Accounts.Single(a => a.Id == id).Provider, route.Provider, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Route and inference account providers differ.");
        void BindTransport(RouteDto route, string protocol, string source)
        {
            var binding = new ModelCompatibilityTransportBinding(route.Provider, route.UpstreamModel, protocol, source);
            if (transportBindings.TryGetValue(route.Id, out var prior) && prior.UpstreamProtocol != protocol)
                throw new InvalidDataException("A route pool cannot mix upstream protocols.");
            transportBindings[route.Id] = binding;
        }
        var config = new YamlMappingNode
        {
            { "host", "127.0.0.1" }, { "port", OwnStatePaths.ProxyPort.ToString() }, { "auth-dir", paths.ProxyAuth },
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
        var identityProtocols = new Dictionary<string, string>(StringComparer.Ordinal);
        var authFiles = new Dictionary<string, string>();
        var routeCredentials = state.Routes.Where(r => r.Enabled).ToDictionary(r => r.Id, _ => new Dictionary<string, string>());
        var metadataPath = Path.Combine(paths.Imports, "routes.json");
        var routeMetadata = File.Exists(metadataPath)
            ? JsonSerializer.Deserialize<Dictionary<string, LegacyImportService.ImportedRouteSource>>(File.ReadAllText(metadataPath)) ?? [] : [];
        foreach (var account in state.Accounts.Where(a => a.Enabled && referencedAccounts.Contains(a.Id)))
        {
            using var credential = JsonDocument.Parse(ReadInferenceCredential(account));
            var secret = credential.RootElement;
            var kind = Read(secret, "kind");
            if (kind == "codex-owned")
            {
                var ownedFilename = Read(secret, "ownedAuthFile") ?? throw new InvalidDataException("Missing owned credential reference.");
                if (Path.GetFileName(ownedFilename) != ownedFilename || !ownedFilename.EndsWith(".json", StringComparison.Ordinal))
                    throw new InvalidDataException("Invalid owned credential reference.");
                if (string.IsNullOrWhiteSpace(Read(secret, "accessToken"))) throw new InvalidDataException("Missing inference access token.");
                foreach (var route in activeRoutes.Where(r => r.AccountIds.Contains(account.Id))) BindTransport(route, "responses", "compiled:codex-owned");
                identities.Add(account.Id, "owned/" + ownedFilename);
                foreach (var route in state.Routes.Where(r => r.Enabled && r.AccountIds.Contains(account.Id))) routeCredentials[route.Id][account.Id] = "owned/" + ownedFilename;
                continue;
            }
            if (kind == "codex-access-snapshot")
            {
                if (string.IsNullOrWhiteSpace(Read(secret, "accessToken"))) throw new InvalidDataException("Missing inference access token.");
                foreach (var route in activeRoutes.Where(r => r.AccountIds.Contains(account.Id))) BindTransport(route, "responses", "compiled:codex-access-snapshot");
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
                ProviderRouteIds = (Read(transport, "providerRouteId") is { } primary ? new[] { primary } : [])
                    .Concat(transport.TryGetProperty("providerRouteIds", out var routeIds) && routeIds.ValueKind == JsonValueKind.Array
                        ? routeIds.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()!) : [])
                    .ToHashSet(StringComparer.Ordinal),
                Models = Read(transport, "sourceConfig") is { } sourceConfig && LegacyImportService.Get(Parse(sourceConfig), "models") is YamlSequenceNode models
                    ? models.Children.OfType<YamlMappingNode>().Select(m => LegacyImportService.Text(m, "name")).Where(m => m is not null).ToHashSet() : []
            }).ToArray();
            if (descriptors.Any(d => string.IsNullOrWhiteSpace(d.Id)) || descriptors.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count() != descriptors.Length)
                throw new InvalidDataException("Private credential transport identities must be distinct and nonempty.");
            var selectedRoutes = new Dictionary<string, string>();
            foreach (var route in state.Routes.Where(r => r.Enabled && r.AccountIds.Contains(account.Id)))
            {
                var mapped = routeMetadata.TryGetValue(route.Id, out var metadata) && metadata.AccountId == account.Id ? metadata.TransportId : null;
                var matches = route.ProviderRouteId is not null ? descriptors.Where(d => d.ProviderRouteIds.Contains(route.ProviderRouteId)).ToArray()
                    : mapped is not null ? descriptors.Where(d => d.Id == mapped).ToArray()
                    : descriptors.Where(d => d.Models.Contains(route.UpstreamModel)).ToArray();
                if (matches.Length == 0 && route.ProviderRouteId is null && mapped is null && descriptors.Length == 1) matches = descriptors;
                if (matches.Length != 1) throw new InvalidOperationException("Route transport is missing or ambiguous; select an imported transport-specific route.");
                selectedRoutes[route.Id] = matches[0].Id;
            }
            var selectedTransports = descriptors.Where(d => selectedRoutes.ContainsValue(d.Id));
            foreach (var descriptor in selectedTransports)
            {
                var transport = descriptor.Secret;
                var transportId = descriptor.Id;
                var coreProvider = Read(transport, "coreProvider") ?? account.Provider;
                var section = Read(transport, "sourceSection") ?? "openai-compatibility";
                if (section is not ("openai-compatibility" or "codex-api-key" or "claude-api-key" or "gemini-api-key"))
                    throw new InvalidDataException("Unsupported source section.");
                var provider = Read(transport, "sourceConfig") is { } sourceConfig ? Parse(sourceConfig) : new YamlMappingNode();
                var key = Read(transport, "apiKey");
                if (string.IsNullOrWhiteSpace(key)) throw new InvalidDataException("Missing own API credential.");
                var endpoint = Read(transport, "baseUrl") ?? "";
                if (endpoint.Length > 0) { AccountService.ValidateEndpoint(endpoint); Set(provider, "base-url", new YamlScalarNode(endpoint)); }
                var qualifiedDirectHeaders = false;
                foreach (var route in selectedRoutes.Where(pair => pair.Value == transportId).Select(pair => state.Routes.Single(r => r.Id == pair.Key)))
                {
                    qualifiedDirectHeaders |= ProviderTransportValidation.ValidateCompiled(catalog, route, transport, section, endpoint, provider);
                    if (route.ProviderRouteId is not null) qualifiedProviderRouteIds.Add(route.Id);
                }
                var protocol = CompiledProtocol(section, provider);
                if (section == "openai-compatibility")
                    Set(provider, "upstream-protocol", new YamlScalarNode(protocol == "responses" ? "openai-responses" : "openai-chat"));
                foreach (var route in activeRoutes.Where(r => selectedRoutes.GetValueOrDefault(r.Id) == transportId))
                    BindTransport(route, protocol, "compiled:" + section);
                if (!qualifiedDirectHeaders && (section is "codex-api-key" or "claude-api-key" or "gemini-api-key") && LegacyImportService.Get(provider, "headers") is YamlMappingNode headers && headers.Children.Count > 0)
                    throw new InvalidDataException("Custom direct-provider headers require additional qualification.");
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
                var sortedHeaders = section != "openai-compatibility" && LegacyImportService.Get(provider, "headers") is YamlMappingNode generatedHeaders
                    ? string.Concat(generatedHeaders.Children.Select(pair => ((YamlScalarNode)pair.Key, (YamlScalarNode)pair.Value))
                        .OrderBy(pair => pair.Item1.Value, StringComparer.Ordinal)
                        .Select(pair => pair.Item1.Value + "\0" + pair.Item2.Value + "\0")) : "";
                var coreId = CoreCredentialId(section, coreProvider, key, endpoint, proxyUrl, LegacyImportService.Text(provider, "prefix") ?? "", sortedHeaders);
                if (identityProtocols.TryGetValue(coreId, out var priorProtocol) && priorProtocol != protocol)
                    throw new InvalidDataException("A compiled credential identity cannot select multiple upstream protocols.");
                identityProtocols[coreId] = protocol;
                identities.TryAdd(account.Id, coreId);
                foreach (var pair in selectedRoutes.Where(pair => pair.Value == transportId)) routeCredentials[pair.Key][account.Id] = coreId;
            }
        }
        foreach (var route in state.Routes.Where(r => r.Enabled))
            if (route.AccountIds.Any(id => !routeCredentials[route.Id].ContainsKey(id))) throw new InvalidOperationException("Route contains an unavailable account credential.");
        var description = new ProxyTransportDescription(identities,
            routeCredentials.ToDictionary(p => p.Key, p => (IReadOnlyDictionary<string, string>)p.Value),
            qualifiedProviderRouteIds, transportBindings);
        _ = RouteBindings.Compile(state, identities, description.RouteCredentials, catalog,
            qualifiedProviderRouteIds: qualifiedProviderRouteIds, transportBindings: transportBindings);
        return new(config, authFiles, description);
    }

    private string ReadInferenceCredential(AccountDto account)
    {
        var inference = AccountService.EffectiveCapabilities(account).LastOrDefault(c => c.Purpose == "inference");
        var handle = inference?.CredentialId is { } id ? account.Credentials?.SingleOrDefault(h => h.Id == id)
            : account.Credentials?.LastOrDefault(h => h.Purposes.Contains("inference", StringComparer.Ordinal));
        if (inference?.CredentialId is not null && handle is null)
            throw new InvalidDataException("Referenced inference credential is missing.");
        if (handle is null)
        {
            if (account.Credentials is { Length: > 0 }) throw new InvalidDataException("Inference credential generation is missing.");
            return secrets.ReadAccount(account.Id);
        }
        if (!handle.Purposes.Contains("inference", StringComparer.Ordinal))
            throw new InvalidDataException("Referenced credential does not grant inference.");
        if (!string.Equals(account.Provider, handle.Binding.Provider, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Inference credential provider does not match the account.");
        AccountService.EnsureCompatibleBinding(account.Binding, handle.Binding);
        if (secrets.Generations.IsRetired(handle)) throw new InvalidDataException("Credential owner recovery is required.");
        return secrets.ProjectAccessCredential(secrets.Generations.Read(account.Id, handle).PrivateJson);
    }

    private static string CompiledProtocol(string section, YamlMappingNode provider) => section switch
    {
        "codex-api-key" => "responses",
        "claude-api-key" => "anthropic",
        "gemini-api-key" => "gemini",
        // The compatibility executor defaults to Chat. Only its actual compiled switch selects Responses.
        "openai-compatibility" => ReadProtocol(provider) switch
        {
            null or "" or "openai-chat" => "openai",
            "openai-responses" => "responses",
            _ => throw new InvalidDataException("Unsupported compiled compatibility protocol.")
        },
        _ => throw new InvalidDataException("Unsupported compiled transport section.")
    };

    private static string? ReadProtocol(YamlMappingNode provider) => LegacyImportService.Get(provider, "upstream-protocol") switch
    {
        null => null,
        YamlScalarNode scalar => scalar.Value,
        _ => throw new InvalidDataException("Compiled compatibility protocol must be a scalar.")
    };

    public static string CoreCredentialId(string section, string provider, string key, string endpoint, string proxyUrl, string prefix = "", string sortedHeaders = "")
    {
        var kind = section == "openai-compatibility" ? "openai-compatibility:" + provider.Trim().ToLowerInvariant() : section.Replace("-api-key", "") + ":apikey";
        var input = kind + "\0" + key.Trim() + "\0" + endpoint.Trim() + "\0" + proxyUrl.Trim();
        if (section != "openai-compatibility") input += "\0" + prefix.Trim() + "\0" + sortedHeaders;
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
