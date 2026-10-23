using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Operations;
using YamlDotNet.RepresentationModel;

namespace Lain.Host.Accounts;

/// <summary>Read-only snapshot import. Never calls legacy management, refresh, revoke, or queue endpoints.</summary>
public sealed class LegacyImportService(OwnStatePaths paths, ConfigurationRepository configuration, SecretStore secrets)
{
    private static readonly string[] ProviderSections = ["codex-api-key", "claude-api-key", "gemini-api-key", "vertex-api-key", "openai-compatibility"];
    private static readonly string[] ConfigSections = [.. ProviderSections, "routing", "oauth-model-alias", "oauth-excluded-models", "disabled"];
    public ImportResult Import(ImportRequest request)
    {
        paths.AssertExternalSource(request.ConfigPath);
        if (new FileInfo(request.ConfigPath).Length > 16 * 1024 * 1024) throw new InvalidDataException("Legacy config is too large.");
        var yaml = new YamlStream();
        try { using var reader = File.OpenText(request.ConfigPath); yaml.Load(reader); }
        catch (YamlDotNet.Core.YamlException) { throw new InvalidDataException("Legacy YAML could not be parsed."); }
        if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode root)
            throw new InvalidDataException("Expected one legacy YAML mapping.");
        var candidates = new List<(AccountDto Account, string Credential)>();
        var warnings = new List<string>();
        foreach (var section in ProviderSections)
        {
            if (Get(root, section) is not YamlSequenceNode providers) continue;
            foreach (var value in providers.Children)
            {
                if (value is not YamlMappingNode provider) continue;
                var providerName = section == "openai-compatibility" ? Text(provider, "name") ?? "openai-compatible" : section.Replace("-api-key", "");
                var endpoint = Text(provider, "base-url");
                if (endpoint is not null) AccountService.ValidateEndpoint(endpoint);
                var entries = Get(provider, "api-key-entries") as YamlSequenceNode;
                if (entries is not null)
                {
                    foreach (var entry in entries.Children.OfType<YamlMappingNode>())
                        AddApiCandidate(section, providerName, provider, entry, endpoint, candidates);
                }
                else AddApiCandidate(section, providerName, provider, provider, endpoint, candidates);
            }
        }
        var authDirectory = request.AuthDirectory ?? Text(root, "auth-dir");
        if (authDirectory is not null)
        {
            if (authDirectory.StartsWith("~/", StringComparison.Ordinal)) authDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), authDirectory[2..]);
            if (!Path.IsPathRooted(authDirectory)) authDirectory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(request.ConfigPath))!, authDirectory);
            paths.AssertExternalSource(authDirectory);
            if (Directory.Exists(authDirectory))
            foreach (var file in Directory.EnumerateFiles(authDirectory, "*.json").Order(StringComparer.Ordinal))
            {
                if (new FileInfo(file).Length > 1024 * 1024) { warnings.Add("An oversized legacy auth file was skipped."); continue; }
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                var auth = document.RootElement;
                if (JsonText(auth, "type") != "codex") { warnings.Add("A non-Codex OAuth credential was skipped (provider not qualified)."); continue; }
                var token = JsonText(auth, "access_token");
                if (string.IsNullOrWhiteSpace(token)) { warnings.Add("A Codex auth file without an access token was skipped."); continue; }
                var accountId = JsonText(auth, "account_id");
                var id = AccountService.CredentialId("codex", accountId ?? token);
                DateTimeOffset? expires = DateTimeOffset.TryParse(JsonText(auth, "expired"), out var parsed) ? parsed : null;
                var disabled = auth.TryGetProperty("disabled", out var disabledValue) && disabledValue.ValueKind == JsonValueKind.True;
                var credential = JsonSerializer.Serialize(new { kind = "codex-access-snapshot", provider = "codex", accessToken = token, accountId, expiresAt = expires });
                candidates.Add((new(id, "Codex snapshot", "codex", !disabled, "codex-access-snapshot", expires <= DateTimeOffset.UtcNow ? "expired" : "unverified", ExpiresAt: expires, SharedWithLegacy: true), credential));
            }
        }
        var selected = new YamlMappingNode();
        foreach (var key in ConfigSections) if (Get(root, key) is { } value) selected.Add(key, value);
        RemoveRefreshMaterial(selected);
        var output = new StringBuilder();
        using (var writer = new StringWriter(output)) new YamlStream(new YamlDocument(selected)).Save(writer, false);
        var snapshot = output.ToString();
        var snapshotId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot))).ToLowerInvariant();
        var added = 0; var updated = 0;
        var unique = candidates.GroupBy(c => c.Account.Id).Select(g => g.First()).ToArray();
        var current = configuration.Snapshot;
        if (current.Revision != request.Revision) throw new RevisionConflictException(request.Revision, current.Revision);
        var changed = unique.Where(candidate =>
        {
            var prior = current.Accounts.SingleOrDefault(a => a.Id == candidate.Account.Id);
            return prior is null || prior != candidate.Account || !secrets.HasAccount(candidate.Account.Id) || secrets.ReadAccount(candidate.Account.Id) != candidate.Credential;
        }).ToArray();
        var (importedRoutes, routeMetadata, routesChanged) = BuildImportedRoutes(current.Routes, unique, root);
        // A replay changes neither revision nor secrets. Snapshot is private and content-addressed.
        if (changed.Length == 0 && !routesChanged && File.Exists(Path.Combine(paths.Imports, snapshotId + ".yaml")))
            return new(current.Revision, 0, 0, warnings.Distinct().ToArray());
        var result = configuration.Update(request.Revision, state =>
        {
            var accounts = state.Accounts.ToDictionary(a => a.Id);
            foreach (var candidate in changed)
            {
                if (accounts.ContainsKey(candidate.Account.Id)) updated++; else added++;
                accounts[candidate.Account.Id] = candidate.Account;
                secrets.WriteAccount(candidate.Account.Id, candidate.Credential);
            }
            OwnStatePaths.WritePrivate(Path.Combine(paths.Imports, snapshotId + ".yaml"), snapshot);
            OwnStatePaths.WritePrivate(Path.Combine(paths.Imports, "routes.json"), routeMetadata);
            OwnStatePaths.WritePrivate(Path.Combine(paths.Imports, "latest.json"), JsonSerializer.Serialize(new { snapshot = snapshotId + ".yaml", importedAt = DateTimeOffset.UtcNow }));
            return state with { Accounts = accounts.Values.OrderBy(a => a.Id).ToArray(), Routes = importedRoutes };
        });
        return new(result.Revision, added, updated, warnings.Distinct().ToArray());
    }
    private sealed record ImportedRouteSource(string AccountId, string Alias, string Model);
    private (RouteDto[] Routes, string Metadata, bool Changed) BuildImportedRoutes(RouteDto[] existing, (AccountDto Account, string Credential)[] candidates, YamlMappingNode legacy)
    {
        var metadataPath = Path.Combine(paths.Imports, "routes.json");
        var previous = File.Exists(metadataPath)
            ? JsonSerializer.Deserialize<Dictionary<string, ImportedRouteSource>>(File.ReadAllText(metadataPath)) ?? [] : [];
        var metadata = new Dictionary<string, ImportedRouteSource>(previous);
        var routes = existing.ToList();
        var sources = new List<(AccountDto Account, YamlMappingNode Model)>();
        foreach (var candidate in candidates)
        {
            using var document = JsonDocument.Parse(candidate.Credential);
            if (document.RootElement.TryGetProperty("sourceConfig", out var source))
            {
                var yaml = new YamlStream(); using var reader = new StringReader(source.GetString()!); yaml.Load(reader);
                if (yaml.Documents[0].RootNode is YamlMappingNode provider && Get(provider, "models") is YamlSequenceNode models)
                    sources.AddRange(models.Children.OfType<YamlMappingNode>().Select(model => (candidate.Account, model)));
            }
            else if (candidate.Account.CredentialKind == "codex-access-snapshot" &&
                     Get(legacy, "oauth-model-alias") is YamlMappingNode oauth && Get(oauth, "codex") is YamlSequenceNode aliases)
                sources.AddRange(aliases.Children.OfType<YamlMappingNode>().Select(model => (candidate.Account, model)));
        }
        foreach (var source in sources)
        {
            var upstream = Text(source.Model, "name");
            if (string.IsNullOrWhiteSpace(upstream)) continue;
            var sourceAlias = Text(source.Model, "alias") ?? upstream;
            var prior = previous.FirstOrDefault(p => p.Value.AccountId == source.Account.Id && p.Value.Alias == sourceAlias);
            if (prior.Key is null) prior = previous.FirstOrDefault(p => p.Value.AccountId == source.Account.Id && p.Value.Model == upstream);
            var id = prior.Key ?? "route-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.Account.Id + "\0" + upstream + "\0" + sourceAlias))).ToLowerInvariant()[..32];
            var alias = sourceAlias;
            var occupied = routes.Where(r => r.Id != id).Select(r => r.Alias).ToHashSet(StringComparer.Ordinal);
            if (occupied.Contains(alias))
            {
                var baseAlias = alias + "-" + source.Account.Id[^8..]; alias = baseAlias; var suffix = 2;
                while (occupied.Contains(alias)) alias = baseAlias + "-" + suffix++;
            }
            var visible = !IsTrue(Text(source.Model, "hidden")) && !string.Equals(Text(source.Model, "visible"), "false", StringComparison.OrdinalIgnoreCase);
            var enabled = source.Account.Enabled && !IsTrue(Text(source.Model, "disabled"));
            var route = new RouteDto(id, alias, upstream, source.Account.Provider, [source.Account.Id], Visible: visible, Enabled: enabled);
            var index = routes.FindIndex(r => r.Id == id);
            if (index < 0) routes.Add(route); else routes[index] = route;
            metadata[id] = new(source.Account.Id, sourceAlias, upstream);
        }
        var result = routes.ToArray();
        return (result, JsonSerializer.Serialize(metadata), JsonSerializer.Serialize(existing) != JsonSerializer.Serialize(result));
    }
    private static void AddApiCandidate(string sourceSection, string providerName, YamlMappingNode provider, YamlMappingNode entry, string? endpoint, List<(AccountDto Account, string Credential)> candidates)
    {
        var key = Text(entry, "api-key");
        if (string.IsNullOrWhiteSpace(key)) return;
        var id = AccountService.CredentialId(providerName, key);
        var disabled = IsTrue(Text(entry, "disabled")) || IsTrue(Text(provider, "disabled"));
        RemoveRefreshMaterial(provider);
        var source = new StringBuilder();
        using (var writer = new StringWriter(source)) new YamlStream(new YamlDocument(provider)).Save(writer, false);
        var entrySource = new StringBuilder();
        using (var writer = new StringWriter(entrySource)) new YamlStream(new YamlDocument(entry)).Save(writer, false);
        var credential = JsonSerializer.Serialize(new { kind = "api-key", sourceSection, provider = providerName, apiKey = key, baseUrl = endpoint, sourceConfig = source.ToString(), entryConfig = entrySource.ToString() });
        candidates.Add((new(id, providerName, providerName, !disabled, "api-key", "unverified", SharedWithLegacy: true), credential));
    }
    private static void RemoveRefreshMaterial(YamlNode node, HashSet<YamlNode>? seen = null)
    {
        seen ??= new(ReferenceEqualityComparer.Instance);
        if (!seen.Add(node)) return;
        if (node is YamlMappingNode mapping)
        {
            foreach (var key in mapping.Children.Keys.OfType<YamlScalarNode>().ToArray())
            {
                var name = key.Value?.Replace("-", "_").ToLowerInvariant();
                if (name is "refresh_token" or "id_token") mapping.Children.Remove(key);
            }
            foreach (var child in mapping.Children.Values) RemoveRefreshMaterial(child, seen);
        }
        else if (node is YamlSequenceNode sequence) foreach (var child in sequence.Children) RemoveRefreshMaterial(child, seen);
    }
    private static bool IsTrue(string? value) => bool.TryParse(value, out var result) && result;
    internal static YamlNode? Get(YamlMappingNode node, string key) => node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;
    internal static string? Text(YamlMappingNode node, string key) => (Get(node, key) as YamlScalarNode)?.Value;
    private static string? JsonText(JsonElement node, string key) => node.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
