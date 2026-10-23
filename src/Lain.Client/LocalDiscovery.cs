namespace Lain.Client;

/// <summary>Reads only lain's local API credential; never opens application databases.</summary>
public static class LocalDiscovery
{
    public static Uri ApiAddress { get; } = new("http://127.0.0.1:18418/");
    public static string StateDirectory => Path.GetFullPath(Environment.GetEnvironmentVariable("LAIN_HOME") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lain"));

    public static string ReadHostToken()
    {
        var path = Path.Combine(StateDirectory, "credentials", "host.token");
        if (!File.Exists(path)) throw new IOException("lain host credential is missing. Start the lain service first.");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("lain host credential must be a regular private file.");
        var token = File.ReadAllText(path).Trim();
        if (token.Length == 0 || token.Any(char.IsWhiteSpace)) throw new IOException("lain host credential is invalid.");
        return token;
    }
}
