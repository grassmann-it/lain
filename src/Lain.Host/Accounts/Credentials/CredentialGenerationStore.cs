using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using Lain.Core;
using Lain.Host.Operations;

namespace Lain.Host.Accounts.Credentials;

/// <summary>Immutable, durable secret preparation. The configuration file alone publishes a generation.</summary>
public sealed class CredentialGenerationStore(OwnStatePaths paths)
{
    private readonly object gate = new();
    public sealed record StoredGeneration(string AccountId, CredentialHandleDto Handle, string PrivateJson,
        bool PreserveForRecovery, string? OperationId = null, string? RemoteId = null, string? RestoreEpoch = null)
    {
        public override string ToString() => "StoredGeneration [redacted]";
    }

    public CredentialHandleDto Prepare(string accountId, PreparedLoginCredential credential, string? operationId = null,
        string? credentialId = null, long generation = 1)
    {
        ValidateId(accountId);
        ValidateCredential(credential);
        var handle = new CredentialHandleDto(credentialId ?? "cred-" + Guid.NewGuid().ToString("N"), generation,
            credential.Owner, credential.Purposes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            credential.Binding, credential.ExpiresAt);
        var path = GenerationPath(handle.Id, generation);
        lock (gate)
        {
            if (File.Exists(path)) throw new InvalidOperationException("Credential generation already exists.");
            var protectedJson = ProtectMaterial(accountId, credential.PrivateJson);
            OwnStatePaths.WritePrivate(path, JsonSerializer.Serialize(new StoredGeneration(accountId, handle,
                protectedJson, credential.PreserveForRecovery, operationId, credential.RemoteId, RestoreEpoch)));
        }
        return handle;
    }

    public StoredGeneration Read(string accountId, CredentialHandleDto handle)
    {
        var value = Read(handle.Id, handle.Generation);
        if (value.AccountId != accountId || value.Handle.Owner != handle.Owner || value.Handle.Binding != handle.Binding ||
            value.Handle.ExpiresAt != handle.ExpiresAt || !value.Handle.Purposes.SequenceEqual(handle.Purposes))
            throw new InvalidDataException("Credential reference does not match its immutable generation.");
        return value;
    }

    public StoredGeneration Read(string id, long generation)
    {
        var path = GenerationPath(id, generation);
        RefuseLink(path);
        if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("Credential generation is too large.");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var value = JsonSerializer.Deserialize<StoredGeneration>(File.ReadAllText(path)) ?? throw new InvalidDataException("Invalid credential generation.");
        if (value.Handle.Id != id || value.Handle.Generation != generation) throw new InvalidDataException("Credential generation identity mismatch.");
        if (value.Handle.Owner is not ("static-key" or "external-snapshot") && RestoreEpoch is { } epoch && value.RestoreEpoch != epoch)
            throw new InvalidDataException("Restored rotating credentials require reconnect before access or refresh.");
        return value with { PrivateJson = HydrateMaterial(value.AccountId, value.PrivateJson) };
    }

    internal string? RestoreEpoch
    {
        get
        {
            var marker = Path.Combine(paths.Root, "restore-requires-reconnect");
            RefuseLink(marker);
            return File.Exists(marker) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(marker))) : null;
        }
    }

    public void DiscardStaticPreparation(CredentialHandleDto handle)
    {
        lock (gate)
        {
            var stored = Read(handle.Id, handle.Generation);
            if (stored.PreserveForRecovery || stored.Handle.Owner is not ("static-key" or "external-snapshot")) return;
            File.Delete(GenerationPath(handle.Id, handle.Generation));
        }
    }

    public StoredGeneration? FindPrepared(string operationId, string accountId)
    {
        ValidateId(operationId); ValidateId(accountId);
        var root = Path.Combine(paths.Credentials, "generations");
        if (!Directory.Exists(root)) return null;
        var matches = new List<StoredGeneration>();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            OwnStatePaths.EnsurePrivateDirectory(directory);
            foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
            {
                RefuseLink(path);
                if (new FileInfo(path).Length > 2 * 1024 * 1024) continue;
                StoredGeneration? candidate;
                try { candidate = JsonSerializer.Deserialize<StoredGeneration>(File.ReadAllText(path)); }
                catch (JsonException) { continue; }
                if (candidate?.OperationId == operationId && candidate.AccountId == accountId)
                    matches.Add(Read(accountId, candidate.Handle));
            }
        }
        if (matches.Select(m => m.Handle.Id).Distinct(StringComparer.Ordinal).Count() > 1)
            throw new InvalidDataException("Recovery found multiple prepared credentials; explicit inspection is required.");
        return matches.OrderByDescending(m => m.Handle.Generation).FirstOrDefault();
    }

    public string RecoveryPath(string operationId)
    {
        ValidateId(operationId);
        return Path.Combine(paths.Credentials, "recovery", operationId + ".json");
    }

    internal string RemovalPath(string accountId)
    {
        ValidateId(accountId);
        var directory = Path.Combine(paths.Credentials, "removed-targets");
        OwnStatePaths.EnsurePrivateDirectory(directory);
        return Path.Combine(directory, accountId + ".json");
    }

    internal IEnumerable<string> RecoveryFiles()
    {
        var directory = Path.Combine(paths.Credentials, "recovery");
        OwnStatePaths.EnsurePrivateDirectory(directory);
        return Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
    }

    public bool IsRetired(CredentialHandleDto handle) => File.Exists(RetirementPath(handle));
    public void RetireForRotation(CredentialHandleDto handle, string operationId)
    {
        lock (gate)
        {
            var path = RetirementPath(handle);
            if (File.Exists(path)) throw new InvalidOperationException("Credential rotation requires recovery; old refresh material cannot be reused.");
            OwnStatePaths.WritePrivate(path, JsonSerializer.Serialize(new { operationId, handle.Id, handle.Generation }));
        }
    }

    private string RetirementPath(CredentialHandleDto handle) => GenerationPath(handle.Id, handle.Generation) + ".retired";
    private string ProtectMaterial(string accountId, string json)
    {
        var root = JsonNode.Parse(json)!;
        Visit(root);
        return root.ToJsonString();
        void Visit(JsonNode node)
        {
            if (node is JsonObject value)
                foreach (var property in value.ToArray())
                {
                    if (property.Key == "$lainMaterial") throw new ArgumentException("Private credential contains a reserved material marker.");
                    var key = property.Key.Replace("_", "").Replace("-", "").ToLowerInvariant();
                    if (key is "apikey" or "accesstoken" or "refreshtoken" or "clientsecret" or "secret" or "sessiontoken" or "cookie" &&
                        property.Value is JsonValue token && token.TryGetValue<string>(out var secret))
                    {
                        var id = MaterialId(accountId, secret);
                        var path = MaterialPath(accountId, id);
                        RefuseLink(path);
                        if (!File.Exists(path)) OwnStatePaths.WritePrivate(path, JsonSerializer.Serialize(secret));
                        value[property.Key] = new JsonObject { ["$lainMaterial"] = id };
                    }
                    else if (property.Value is { } child) Visit(child);
                }
            else if (node is JsonArray array) foreach (var child in array.OfType<JsonNode>()) Visit(child);
        }
    }
    private string HydrateMaterial(string accountId, string json)
    {
        var root = JsonNode.Parse(json)!;
        Visit(root);
        return root.ToJsonString();
        void Visit(JsonNode node)
        {
            if (node is JsonObject value)
                foreach (var property in value.ToArray())
                {
                    if (property.Value is JsonObject marker && marker.TryGetPropertyValue("$lainMaterial", out var material))
                    {
                        if (marker.Count != 1 || material is not JsonValue idNode || !idNode.TryGetValue<string>(out var id))
                            throw new InvalidDataException("Invalid private material reference.");
                        var path = MaterialPath(accountId, id);
                        RefuseLink(path);
                        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Private material is too large.");
                        var secret = JsonSerializer.Deserialize<string>(File.ReadAllText(path)) ?? throw new InvalidDataException("Invalid private material.");
                        if (MaterialId(accountId, secret) != id) throw new InvalidDataException("Private material identity mismatch.");
                        value[property.Key] = secret;
                    }
                    else if (property.Value is { } child) Visit(child);
                }
            else if (node is JsonArray array) foreach (var child in array.OfType<JsonNode>()) Visit(child);
        }
    }
    private static string MaterialId(string accountId, string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accountId + "\0" + secret))).ToLowerInvariant();
    private string MaterialPath(string accountId, string id)
    {
        ValidateId(accountId);
        if (id.Length != 64 || id.Any(c => !char.IsAsciiHexDigit(c))) throw new InvalidDataException("Invalid private material identity.");
        var directory = Path.Combine(paths.Credentials, "material", accountId);
        OwnStatePaths.EnsurePrivateDirectory(directory);
        return Path.Combine(directory, id + ".json");
    }
    private string GenerationPath(string id, long generation)
    {
        ValidateId(id);
        if (generation < 1) throw new ArgumentException("Invalid credential generation.");
        var directory = Path.Combine(paths.Credentials, "generations", id);
        OwnStatePaths.EnsurePrivateDirectory(directory);
        return Path.Combine(directory, generation.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".json");
    }
    internal static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new ArgumentException("Invalid private resource identity.");
    }
    internal static void RefuseLink(string path)
    {
        if (new FileInfo(path).LinkTarget is not null || File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Invalid private credential path.");
    }
    private static void ValidateCredential(PreparedLoginCredential credential)
    {
        if (credential.Purposes.Length == 0 && !credential.PreserveForRecovery || credential.Purposes.Any(p => p is not ("inference" or "quota" or "cost" or "models")))
            throw new ArgumentException("A credential requires explicit supported purposes.");
        if (string.IsNullOrWhiteSpace(credential.Owner) || credential.Owner.Length > 80 || credential.Owner.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("Unsupported credential owner.");
        if (credential.PrivateJson.Length > 1024 * 1024 || JsonNode.Parse(credential.PrivateJson) is not JsonObject json)
            throw new ArgumentException("Invalid private credential.");
        if (json["kind"]?.GetValue<string>() == "codex-owned" && credential.Owner != "core")
            throw new ArgumentException("Core-owned credentials require their qualified core owner.");
        if (credential.Owner is "static-key" or "external-snapshot" && ContainsRefresh(json))
            throw new ArgumentException("Static keys and external snapshots cannot contain refresh material.");
        static bool ContainsRefresh(JsonNode node) => node switch
        {
            JsonObject value => value.Any(p => p.Key.Contains("refresh", StringComparison.OrdinalIgnoreCase) ||
                p.Key is "id_token" or "idToken" || p.Value is not null && ContainsRefresh(p.Value)),
            JsonArray array => array.Any(child => child is not null && ContainsRefresh(child)),
            _ => false
        };
    }
}
