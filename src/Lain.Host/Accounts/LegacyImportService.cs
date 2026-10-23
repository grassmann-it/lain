using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lain.Core;
using Lain.Host.Operations;
using YamlDotNet.RepresentationModel;

namespace Lain.Host.Accounts;

/// <summary>Read-only snapshot import. Never calls legacy management, refresh, revoke, or queue endpoints.</summary>
public sealed class LegacyImportService(OwnStatePaths paths, ConfigurationRepository configuration, SecretStore secrets, IReadOnlyList<string>? codexModelCatalog = null)
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
        var current = configuration.Snapshot;
        var remappedAccounts = NormalizeImportedIdentities(candidates, current);
        var unique = candidates.GroupBy(c => c.Account.Id).Select(MergeCredentialTransports).Select(candidate =>
        {
            var own = current.Accounts.SingleOrDefault(a => a.Id == candidate.Account.Id && a.CredentialKind == "codex-owned");
            return candidate.Account.CredentialKind == "codex-access-snapshot" && own is not null
                ? (Account: own with { SharedWithLegacy = true }, Credential: secrets.ReadAccountDescriptor(own.Id)) : candidate;
        }).ToArray();
        if (current.Revision != request.Revision) throw new RevisionConflictException(request.Revision, current.Revision);
        var changed = unique.Where(candidate =>
        {
            var prior = current.Accounts.SingleOrDefault(a => a.Id == candidate.Account.Id);
            return prior is null || prior != candidate.Account || !secrets.HasAccount(candidate.Account.Id) || secrets.ReadAccount(candidate.Account.Id) != candidate.Credential;
        }).ToArray();
        var (importedRoutes, routeMetadata, routesChanged) = BuildImportedRoutes(current.Routes.Select(r => r with { AccountIds = r.AccountIds.Select(id => remappedAccounts.GetValueOrDefault(id, id)).Distinct().ToArray() }).ToArray(), unique, root, remappedAccounts);
        // A replay changes neither revision nor secrets. Snapshot is private and content-addressed.
        if (changed.Length == 0 && remappedAccounts.Count == 0 && !routesChanged && File.Exists(Path.Combine(paths.Imports, snapshotId + ".yaml")))
            return new(current.Revision, 0, 0, warnings.Distinct().ToArray());
        var result = configuration.Update(request.Revision, state =>
        {
            var accounts = state.Accounts.Where(a => !remappedAccounts.ContainsKey(a.Id)).ToDictionary(a => a.Id);
            foreach (var candidate in changed)
            {
                if (accounts.ContainsKey(candidate.Account.Id)) updated++; else added++;
                accounts[candidate.Account.Id] = candidate.Account;
                secrets.WriteAccount(candidate.Account.Id, candidate.Credential);
            }
            OwnStatePaths.WritePrivate(Path.Combine(paths.Imports, snapshotId + ".yaml"), snapshot);
            OwnStatePaths.WritePrivate(Path.Combine(paths.Imports, "routes.json"), routeMetadata);
            if (remappedAccounts.Count > 0)
            {
                var aliasesPath = Path.Combine(paths.Imports, "account-aliases.json");
                var aliases = File.Exists(aliasesPath) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(aliasesPath)) ?? [] : [];
                foreach (var pair in remappedAccounts) aliases[pair.Key] = pair.Value;
                OwnStatePaths.WritePrivate(aliasesPath, JsonSerializer.Serialize(aliases));
            }
            OwnStatePaths.WritePrivate(Path.Combine(paths.Imports, "latest.json"), JsonSerializer.Serialize(new { snapshot = snapshotId + ".yaml", importedAt = DateTimeOffset.UtcNow }));
            return state with { Accounts = accounts.Values.OrderBy(a => a.Id).ToArray(), Routes = importedRoutes };
        });
        return new(result.Revision, added, updated, warnings.Distinct().ToArray());
    }
    internal sealed record ImportedRouteSource(string AccountId, string Alias, string Model, string? TransportId = null);
    private (RouteDto[] Routes, string Metadata, bool Changed) BuildImportedRoutes(RouteDto[] existing, (AccountDto Account, string Credential)[] candidates, YamlMappingNode legacy, IReadOnlyDictionary<string, string> remappedAccounts)
    {
        var metadataPath = Path.Combine(paths.Imports, "routes.json");
        var previous = File.Exists(metadataPath)
            ? JsonSerializer.Deserialize<Dictionary<string, ImportedRouteSource>>(File.ReadAllText(metadataPath)) ?? [] : [];
        previous = previous.ToDictionary(p => p.Key, p => p.Value with { AccountId = remappedAccounts.GetValueOrDefault(p.Value.AccountId, p.Value.AccountId) });
        var metadata = new Dictionary<string, ImportedRouteSource>(previous);
        var used = new HashSet<string>();
        var routes = existing.ToList();
        var sources = new List<(AccountDto Account, YamlMappingNode Model, string? TransportId, bool SourceEnabled)>();
        foreach (var candidate in candidates)
        {
            using var document = JsonDocument.Parse(candidate.Credential);
            var secret = document.RootElement;
            var transports = secret.TryGetProperty("transports", out var variants) ? variants.EnumerateArray().ToArray() : [secret];
            foreach (var transport in transports)
            if (transport.TryGetProperty("sourceConfig", out var source))
            {
                var yaml = new YamlStream(); using var reader = new StringReader(source.GetString()!); yaml.Load(reader);
                var transportId = JsonText(transport, "transportId");
                if (yaml.Documents[0].RootNode is YamlMappingNode provider && Get(provider, "models") is YamlSequenceNode models)
                    sources.AddRange(models.Children.OfType<YamlMappingNode>().Select(model => (candidate.Account, model, transportId, !transport.TryGetProperty("sourceEnabled", out var enabled) || enabled.GetBoolean())));
            }
            if (candidate.Account.CredentialKind is "codex-access-snapshot" or "codex-owned")
            {
                var aliases = Get(legacy, "oauth-model-alias") is YamlMappingNode oauth ? Get(oauth, "codex") as YamlSequenceNode : null;
                if (aliases is { Children.Count: > 0 })
                    sources.AddRange(aliases.Children.OfType<YamlMappingNode>().Select(model => (candidate.Account, model, (string?)null, true)));
                else
                {
                    var excluded = Get(legacy, "oauth-excluded-models") is YamlMappingNode exclusions && Get(exclusions, "codex") is YamlSequenceNode patterns
                        ? patterns.Children.OfType<YamlScalarNode>().Select(p => p.Value ?? "").ToArray() : [];
                    foreach (var model in (codexModelCatalog ?? []).Distinct(StringComparer.Ordinal))
                    {
                        if (string.IsNullOrWhiteSpace(model) || model.Length > 200 || excluded.Any(pattern => System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, model, ignoreCase: false))) continue;
                        sources.Add((candidate.Account, new YamlMappingNode { { "name", model }, { "alias", model } }, null, true));
                    }
                }
            }
        }
        foreach (var source in sources)
        {
            var upstream = Text(source.Model, "name");
            if (string.IsNullOrWhiteSpace(upstream)) continue;
            var sourceAlias = Text(source.Model, "alias") ?? upstream;
            var prior = previous.FirstOrDefault(p => !used.Contains(p.Key) && p.Value.AccountId == source.Account.Id && p.Value.TransportId == source.TransportId && p.Value.Alias == sourceAlias);
            if (prior.Key is null) prior = previous.FirstOrDefault(p => !used.Contains(p.Key) && p.Value.AccountId == source.Account.Id && p.Value.TransportId == source.TransportId && p.Value.Model == upstream);
            if (prior.Key is null) prior = previous.FirstOrDefault(p => !used.Contains(p.Key) && p.Value.AccountId == source.Account.Id && p.Value.TransportId is null && (p.Value.Alias == sourceAlias || p.Value.Model == upstream));
            var id = prior.Key ?? "route-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.Account.Id + "\0" + upstream + "\0" + sourceAlias + "\0" + source.TransportId))).ToLowerInvariant()[..32];
            used.Add(id);
            var alias = sourceAlias;
            var occupied = routes.Where(r => r.Id != id).Select(r => r.Alias).ToHashSet(StringComparer.Ordinal);
            if (occupied.Contains(alias))
            {
                var baseAlias = alias + "-" + source.Account.Id[^8..]; alias = baseAlias; var suffix = 2;
                while (occupied.Contains(alias)) alias = baseAlias + "-" + suffix++;
            }
            var visible = !IsTrue(Text(source.Model, "hidden")) && !string.Equals(Text(source.Model, "visible"), "false", StringComparison.OrdinalIgnoreCase);
            var enabled = source.Account.Enabled && source.SourceEnabled && !IsTrue(Text(source.Model, "disabled"));
            var route = new RouteDto(id, alias, upstream, source.Account.Provider, [source.Account.Id], Visible: visible, Enabled: enabled);
            var index = routes.FindIndex(r => r.Id == id);
            if (index < 0) routes.Add(route); else routes[index] = route;
            metadata[id] = new(source.Account.Id, sourceAlias, upstream, source.TransportId);
        }
        var result = routes.ToArray();
        return (result, JsonSerializer.Serialize(metadata), JsonSerializer.Serialize(existing) != JsonSerializer.Serialize(result));
    }
    private static void AddApiCandidate(string sourceSection, string providerName, YamlMappingNode provider, YamlMappingNode entry, string? endpoint, List<(AccountDto Account, string Credential)> candidates)
    {
        var key = Text(entry, "api-key");
        if (string.IsNullOrWhiteSpace(key)) return;
        var canonicalProvider = AccountService.CanonicalProvider(providerName, endpoint);
        var id = AccountService.CredentialId(canonicalProvider, key);
        var transportId = "transport-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceSection + "\0" + providerName + "\0" + endpoint + "\0" + Text(entry, "proxy-url") + "\0" + Text(provider, "prefix")))).ToLowerInvariant()[..24];
        var disabled = IsTrue(Text(entry, "disabled")) || IsTrue(Text(provider, "disabled"));
        RemoveRefreshMaterial(provider);
        var source = new StringBuilder();
        using (var writer = new StringWriter(source)) new YamlStream(new YamlDocument(provider)).Save(writer, false);
        var entrySource = new StringBuilder();
        using (var writer = new StringWriter(entrySource)) new YamlStream(new YamlDocument(entry)).Save(writer, false);
        var credential = JsonSerializer.Serialize(new { kind = "api-key", sourceSection, provider = canonicalProvider, coreProvider = providerName, transportId, sourceEnabled = !disabled, apiKey = key, baseUrl = endpoint, sourceConfig = source.ToString(), entryConfig = entrySource.ToString() });
        candidates.Add((new(id, canonicalProvider, canonicalProvider, !disabled, "api-key", "unverified", SharedWithLegacy: true), credential));
    }
    private Dictionary<string, string> NormalizeImportedIdentities(List<(AccountDto Account, string Credential)> candidates, ConfigurationDto state)
    {
        var remapped = new Dictionary<string, string>();
        foreach (var group in candidates.Where(c => c.Account.CredentialKind == "api-key").GroupBy(c => c.Account.Id).ToArray())
        {
            using var input = JsonDocument.Parse(group.First().Credential);
            var key = JsonText(input.RootElement, "apiKey");
            var matches = state.Accounts.Where(a => a.CredentialKind == "api-key" && secrets.HasAccount(a.Id)).Where(a =>
            {
                using var prior = JsonDocument.Parse(secrets.ReadAccount(a.Id));
                return JsonText(prior.RootElement, "apiKey") == key && AccountService.CanonicalProvider(a.Provider, JsonText(prior.RootElement, "baseUrl")) == group.First().Account.Provider;
            }).ToArray();
            if (matches.Length == 0) continue;
            var chosen = matches.FirstOrDefault(a => a.Id == group.Key) ?? matches.FirstOrDefault(a => a.Id == AccountService.CredentialId("codex", key!)) ?? matches[0];
            foreach (var match in matches.Where(a => a.Id != chosen.Id)) remapped[match.Id] = chosen.Id;
            for (var index = 0; index < candidates.Count; index++)
                if (candidates[index].Account.Id == group.Key) candidates[index] = (candidates[index].Account with { Id = chosen.Id }, candidates[index].Credential);
        }
        return remapped;
    }
    private static (AccountDto Account, string Credential) MergeCredentialTransports(IGrouping<string, (AccountDto Account, string Credential)> group)
    {
        var first = group.First();
        if (first.Account.CredentialKind != "api-key") return first;
        var json = JsonNode.Parse(first.Credential)!.AsObject();
        var transports = new JsonArray();
        foreach (var item in group.GroupBy(c => JsonNode.Parse(c.Credential)!["transportId"]!.GetValue<string>()).Select(g => g.First()))
            transports.Add(JsonNode.Parse(item.Credential));
        json["transports"] = transports;
        return (first.Account with { Enabled = group.Any(c => c.Account.Enabled) }, json.ToJsonString());
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
