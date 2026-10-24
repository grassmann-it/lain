namespace Lain.Client;

/// <summary>Reads only lain's local API credential; never opens application databases.</summary>
public static class LocalDiscovery
{
    public static Uri ApiAddress
    {
        get
        {
            var port = 18418;
            if (Environment.GetEnvironmentVariable("LAIN_REFERENCE_QUALIFICATION") == "1" &&
                StateDirectory.StartsWith(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                Environment.GetEnvironmentVariable("LAIN_REFERENCE_HOST_PORT") is { } referencePort)
            {
                if (!int.TryParse(referencePort, out port) || port is < 1024 or > 65535)
                    throw new InvalidOperationException("Invalid isolated reference Host port.");
            }
            return new Uri($"http://127.0.0.1:{port}/");
        }
    }
    public static string StateDirectory => Path.GetFullPath(Environment.GetEnvironmentVariable("LAIN_HOME") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lain"));

    public static string ReadHostToken()
    {
        var path = Path.Combine(StateDirectory, "credentials", "host.token");
        if (!File.Exists(path)) throw new IOException("lain host credential is missing. Start the lain service first.");
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(path)!); directory is not null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("lain host credential directories must not be symbolic links.");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("lain host credential must be a regular private file.");
        var token = File.ReadAllText(path).Trim();
        if (token.Length == 0 || token.Any(char.IsWhiteSpace)) throw new IOException("lain host credential is invalid.");
        return token;
    }
}
