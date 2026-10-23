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
    private static readonly string[] ConfigSections = [.. ProviderSections, "api-keys", "routing", "oauth-model-alias", "oauth-excluded-models", "disabled"];
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
        var importedRoutes = BuildImportedRoutes(current.Routes, unique);
        // A replay changes neither revision nor secrets. Snapshot is private and content-addressed.
        if (changed.Length == 0 && importedRoutes.Length == 0 && File.Exists(Path.Combine(paths.Imports, snapshotId + ".yaml")))
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
            OwnStatePaths.WritePrivate(Path.Combine(paths.Imports, "latest.json"), JsonSerializer.Serialize(new { snapshot = snapshotId + ".yaml", importedAt = DateTimeOffset.UtcNow }));
            return state with { Accounts = accounts.Values.OrderBy(a => a.Id).ToArray(), Routes = [.. state.Routes, .. importedRoutes] };
        });
        return new(result.Revision, added, updated, warnings.Distinct().ToArray());
    }
    private static RouteDto[] BuildImportedRoutes(RouteDto[] existing, (AccountDto Account, string Credential)[] candidates)
    {
        var result = new List<RouteDto>();
        var ids = existing.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var aliases = existing.Select(r => r.Alias).ToHashSet(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            using var document = JsonDocument.Parse(candidate.Credential);
            if (!document.RootElement.TryGetProperty("sourceConfig", out var source)) continue;
            var yaml = new YamlStream(); using var reader = new StringReader(source.GetString()!); yaml.Load(reader);
            if (yaml.Documents[0].RootNode is not YamlMappingNode provider || Get(provider, "models") is not YamlSequenceNode models) continue;
            foreach (var model in models.Children.OfType<YamlMappingNode>())
            {
                var upstream = Text(model, "name");
                if (string.IsNullOrWhiteSpace(upstream)) continue;
                var alias = Text(model, "alias") ?? upstream;
                var id = "route-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(candidate.Account.Id + "\0" + upstream + "\0" + alias))).ToLowerInvariant()[..32];
                if (!ids.Add(id)) continue;
                if (!aliases.Add(alias))
                {
                    var baseAlias = alias + "-" + candidate.Account.Id[^8..]; alias = baseAlias; var suffix = 2;
                    while (!aliases.Add(alias)) alias = baseAlias + "-" + suffix++;
                }
                result.Add(new(id, alias, upstream, candidate.Account.Provider, [candidate.Account.Id], Enabled: candidate.Account.Enabled));
            }
        }
        return result.ToArray();
    }
    private static void AddApiCandidate(string sourceSection, string providerName, YamlMappingNode provider, YamlMappingNode entry, string? endpoint, List<(AccountDto Account, string Credential)> candidates)
    {
        var key = Text(entry, "api-key");
        if (string.IsNullOrWhiteSpace(key)) return;
        var id = AccountService.CredentialId(providerName, key);
        var disabled = Text(entry, "disabled") == "true" || Text(provider, "disabled") == "true";
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
    internal static YamlNode? Get(YamlMappingNode node, string key) => node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;
    internal static string? Text(YamlMappingNode node, string key) => (Get(node, key) as YamlScalarNode)?.Value;
    private static string? JsonText(JsonElement node, string key) => node.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
