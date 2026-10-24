using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lain.Core;
using Lain.Host.Accounts.Credentials;

namespace Lain.Host.Operations;

/// <summary>Only lain-owned resources. No legacy paths are inferred here.</summary>
public sealed class OwnStatePaths
{
    public const int ProxyPort = 18417;
    public const int HostPort = 18418;
    public string Root { get; }
    public string Credentials => Path.Combine(Root, "credentials");
    public string Imports => Path.Combine(Root, "imports");
    public string Proxy => Path.Combine(Root, "proxy");
    public string ProxyAuth => Path.Combine(Proxy, "auth");
    public string OwnedAuth => Path.Combine(Proxy, "auth-owned");
    public string ProxyConfig => Path.Combine(Proxy, "config.yaml");
    public string Configuration => Path.Combine(Root, "configuration.json");
    public string ApiToken => Path.Combine(Credentials, "host.token");
    public string InferenceToken => Path.Combine(Credentials, "inference.token");
    public string ManagementToken => Path.Combine(Credentials, "management.token");

    public OwnStatePaths(string? root = null)
    {
        Root = Path.GetFullPath(root ?? Environment.GetEnvironmentVariable("LAIN_HOME") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lain"));
        foreach (var path in new[] { Root, Credentials, Imports, Proxy, ProxyAuth, OwnedAuth }) EnsurePrivateDirectory(path);
    }

    public static void EnsurePrivateDirectory(string path)
    {
        // Refuse an existing symlink at any ancestor, including the state root.
        for (var current = new DirectoryInfo(Path.GetFullPath(path)); current is not null; current = current.Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0 && current.Exists)
                throw new IOException("lain state paths cannot contain symbolic links.");
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public static void WritePrivate(string path, string content)
    {
        EnsurePrivateDirectory(Path.GetDirectoryName(path)!);
        if (new FileInfo(path).LinkTarget is not null || File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("lain refuses symbolic link files.");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var streamOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) streamOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temporary, streamOptions))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) { writer.Write(content); writer.Flush(); stream.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void AssertExternalSource(string source)
    {
        var full = Path.GetFullPath(source);
        if (full == Root || full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Legacy source must be outside lain state.");
    }
}

public sealed class SecretStore(OwnStatePaths paths)
{
    private readonly object gate = new();
    public CredentialGenerationStore Generations { get; } = new(paths);
    public string GetOrCreateToken(string tokenPath)
    {
        if (tokenPath != paths.ApiToken && tokenPath != paths.InferenceToken && tokenPath != paths.ManagementToken)
            throw new ArgumentException("Unknown token purpose.");
        lock (gate)
        {
            if (File.Exists(tokenPath)) return ReadPrivate(tokenPath);
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            OwnStatePaths.WritePrivate(tokenPath, token);
            return token;
        }
    }
    public string ReadAccount(string accountId)
    {
        var stored = ReadAccountDescriptor(accountId);
        return ProjectAccessCredential(stored);
    }
    internal string ProjectAccessCredential(string stored)
    {
        var descriptor = System.Text.Json.Nodes.JsonNode.Parse(stored)!.AsObject();
        if (descriptor["kind"]?.GetValue<string>() != "codex-owned") return AccessProjection(descriptor);
        var filename = descriptor["ownedAuthFile"]?.GetValue<string>();
        if (filename is null || Path.GetFileName(filename) != filename || !filename.EndsWith(".json", StringComparison.Ordinal))
            throw new InvalidDataException("Invalid owned credential reference.");
        var owned = System.Text.Json.Nodes.JsonNode.Parse(ReadPrivate(Path.Combine(paths.OwnedAuth, filename)))!.AsObject();
        if (owned["type"]?.GetValue<string>() != "codex" || owned["account_id"]?.GetValue<string>() != descriptor["accountId"]?.GetValue<string>())
            throw new InvalidDataException("Owned credential identity mismatch.");
        descriptor["accessToken"] = owned["access_token"]?.DeepClone();
        if (owned["expired"] is { } expires) descriptor["expiresAt"] = expires.DeepClone();
        // Refresh tokens never leave the core-owned credential file.
        return descriptor.ToJsonString();
    }
    internal string ReadAccountDescriptor(string accountId)
    {
        var account = CurrentAccount(accountId);
        if (account?.Capabilities is not null && !account.Capabilities.Any(c => c.Purpose == "inference"))
            throw new FileNotFoundException("Inference capability unavailable.");
        if (account?.Credentials is { Length: > 0 } || account?.Capabilities?.Any(c => c.Purpose == "inference" && c.CredentialId is not null) == true)
        {
            var credentialId = account.Capabilities?.LastOrDefault(c => c.Purpose == "inference")?.CredentialId;
            var handle = credentialId is null ? account.Credentials?.LastOrDefault(h => h.Purposes.Contains("inference"))
                : account.Credentials?.SingleOrDefault(h => h.Id == credentialId && h.Purposes.Contains("inference"));
            if (credentialId is not null && handle is null) throw new InvalidDataException("Referenced inference credential is missing.");
            if (handle is not null)
            {
                if (Generations.IsRetired(handle)) throw new InvalidDataException("Credential owner recovery is required.");
                return Generations.Read(accountId, handle).PrivateJson;
            }
            // A new monitor-only account has no inference credential. Never manufacture one.
            if (account.CredentialKind is not ("api-key" or "codex-owned" or "codex-access-snapshot") || !File.Exists(AccountPath(accountId)))
                throw new FileNotFoundException("Inference credential unavailable.");
        }
        var legacy = ReadPrivate(AccountPath(accountId));
        if (Generations.RestoreEpoch is not null && JsonNode.Parse(legacy)?["kind"]?.GetValue<string>() == "codex-owned")
            throw new InvalidDataException("Restored owned credentials require a new login.");
        return legacy;
    }
    public bool RequiresReconnect(string accountId)
    {
        try { ReadAccountDescriptor(accountId); return false; }
        catch (InvalidDataException) { return true; }
    }
    internal static string AccessProjection(JsonObject descriptor)
    {
        foreach (var property in descriptor.ToArray())
        {
            if (property.Key.Contains("refresh", StringComparison.OrdinalIgnoreCase) || property.Key is "id_token" or "idToken") descriptor.Remove(property.Key);
            else if (property.Value is JsonObject nested) AccessProjection(nested);
            else if (property.Value is JsonArray array)
                foreach (var child in array.OfType<JsonObject>()) AccessProjection(child);
        }
        return descriptor.ToJsonString();
    }
    private AccountDto? CurrentAccount(string accountId)
    {
        if (!File.Exists(paths.Configuration)) return null;
        CredentialGenerationStore.RefuseLink(paths.Configuration);
        return JsonSerializer.Deserialize<ConfigurationDto>(File.ReadAllText(paths.Configuration))?.Accounts.SingleOrDefault(a => a.Id == accountId);
    }
    public void WriteAccount(string accountId, string json) => OwnStatePaths.WritePrivate(AccountPath(accountId), json);
    public void DeleteAccount(string accountId) { var path = AccountPath(accountId); if (File.Exists(path)) File.Delete(path); }
    public bool HasAccount(string accountId)
    {
        var account = CurrentAccount(accountId);
        if (account?.Credentials?.Any(h => h.Purposes.Contains("inference")) == true) return true;
        return File.Exists(AccountPath(accountId));
    }
    private string AccountPath(string id)
    {
        if (id.Length is < 1 or > 100 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new ArgumentException("Invalid account ID.");
        return Path.Combine(paths.Credentials, id + ".json");
    }
    private static string ReadPrivate(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Invalid credential path.");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return File.ReadAllText(path);
    }
}
