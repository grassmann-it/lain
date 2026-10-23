using System.Security.Cryptography;
using System.Text;

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
    public string ProxyConfig => Path.Combine(Proxy, "config.yaml");
    public string Configuration => Path.Combine(Root, "configuration.json");
    public string ApiToken => Path.Combine(Credentials, "host.token");
    public string InferenceToken => Path.Combine(Credentials, "inference.token");
    public string ManagementToken => Path.Combine(Credentials, "management.token");

    public OwnStatePaths(string? root = null)
    {
        Root = Path.GetFullPath(root ?? Environment.GetEnvironmentVariable("LAIN_HOME") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lain"));
        foreach (var path in new[] { Root, Credentials, Imports, Proxy, ProxyAuth }) EnsurePrivateDirectory(path);
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
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
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
    public string ReadAccount(string accountId) => ReadPrivate(AccountPath(accountId));
    public void WriteAccount(string accountId, string json) => OwnStatePaths.WritePrivate(AccountPath(accountId), json);
    public void DeleteAccount(string accountId) { var path = AccountPath(accountId); if (File.Exists(path)) File.Delete(path); }
    public bool HasAccount(string accountId) => File.Exists(AccountPath(accountId));
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
