using System.Text.Json;
using Lain.Core;
using Lain.Host.Operations;

namespace Lain.Host.Accounts.Monitoring;

/// <summary>A private capability of an existing account, never an inference credential or OAuth session.</summary>
public sealed class StoredMonitorAccess(string accountId, string provider, string kind, string? secret,
    string resourceId, DateTimeOffset? expiresAt)
{
    public string AccountId { get; } = accountId;
    public string Provider { get; } = provider;
    public string Kind { get; } = kind;
    public string? Secret { get; } = secret;
    public string ResourceId { get; } = resourceId;
    public DateTimeOffset? ExpiresAt { get; } = expiresAt;
    public override string ToString() => "StoredMonitorAccess [redacted]";
}

/// <summary>
/// Kept apart from SecretStore account files, so legacy import and inference-key replacement
/// cannot overwrite this capability. No refresh token, rotation endpoint or revocation operation.
/// </summary>
public sealed class MonitorCredentialStore
{
    private readonly string directory;
    private readonly object gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = false };

    public MonitorCredentialStore(OwnStatePaths paths)
    {
        directory = Path.Combine(paths.Credentials, "monitors");
        OwnStatePaths.EnsurePrivateDirectory(directory);
    }

    public StoredMonitorAccess? Read(AccountDto account)
    {
        lock (gate)
        {
            var path = AccountPath(account.Id);
            OwnStatePaths.EnsurePrivateDirectory(directory);
            RefuseLink(path);
            if (!File.Exists(path)) return null;
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            try
            {
                if (new FileInfo(path).Length > 128 * 1024) throw new InvalidDataException("Invalid monitor credential.");
                var value = JsonSerializer.Deserialize<StoredMonitorAccess>(File.ReadAllText(path), JsonOptions);
                if (value is null || value.AccountId != account.Id || value.Provider != account.Provider)
                    throw new InvalidDataException("Monitor credential identity does not match the account.");
                ValidateShape(value);
                return value;
            }
            catch (JsonException) { throw new InvalidDataException("Invalid monitor credential."); }
        }
    }

    public void Write(AccountDto account, StoredMonitorAccess value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.AccountId != account.Id || value.Provider != account.Provider)
            throw new ArgumentException("Monitor credential identity does not match the account.");
        ValidateShape(value);
        lock (gate)
        {
            var path = AccountPath(account.Id);
            OwnStatePaths.EnsurePrivateDirectory(directory);
            RefuseLink(path);
            OwnStatePaths.WritePrivate(path, JsonSerializer.Serialize(value, JsonOptions));
        }
    }

    public void Delete(string accountId)
    {
        lock (gate)
        {
            var path = AccountPath(accountId);
            OwnStatePaths.EnsurePrivateDirectory(directory);
            RefuseLink(path);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private string AccountPath(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 100 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new ArgumentException("Invalid account ID.");
        return Path.Combine(directory, id + ".json");
    }

    private static void RefuseLink(string path)
    {
        // LinkTarget also catches a dangling symlink, for which File.Exists is false.
        if (new FileInfo(path).LinkTarget is not null ||
            (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0))
            throw new IOException("Invalid monitor credential path.");
    }

    private static void ValidateShape(StoredMonitorAccess value)
    {
        if (string.IsNullOrWhiteSpace(value.Provider) || string.IsNullOrWhiteSpace(value.ResourceId) || value.ResourceId.Length > 128 ||
            value.ResourceId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new InvalidDataException("Invalid monitor resource.");
        if (value.Kind == "inference-key")
        {
            if (value.Secret is not null || value.ExpiresAt is not null)
                throw new InvalidDataException("Inference-key monitoring cannot contain a separate token or expiry.");
        }
        else if (value.Kind is "api-token" or "access-token-snapshot")
        {
            if (string.IsNullOrWhiteSpace(value.Secret) || value.Secret.Length > 65536 || value.Secret.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
                throw new InvalidDataException("A valid monitor token is required.");
            if (value.Kind == "access-token-snapshot" && value.ExpiresAt is null)
                throw new InvalidDataException("An access-token snapshot requires its expiry.");
        }
        else throw new InvalidDataException("Unsupported monitor credential kind.");
    }
}
