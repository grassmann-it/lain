using System.Text.Json;

namespace Lain.Client.Lifecycle;

/// <summary>Resolves installer-owned binaries without reading runtime configuration or credentials.</summary>
public static class InstallationDiscovery
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static InstallationMetadata Discover(string? stateDirectory = null)
    {
        var state = Path.GetFullPath(stateDirectory ?? LocalDiscovery.StateDirectory);
        var path = Path.Combine(state, "installation.json");
        InstallationMetadata metadata;
        if (File.Exists(path))
        {
            RequirePrivatePath(path);
            try
            {
                metadata = JsonSerializer.Deserialize<InstallationMetadata>(File.ReadAllText(path), Json)
                    ?? throw new IOException("The lain installation metadata is empty. Run the installer again.");
            }
            catch (JsonException) { throw new IOException("The lain installation metadata is invalid. Run the installer again."); }
        }
        else
        {
            if (!OperatingSystem.IsLinux())
                throw new IOException("The lain installation metadata is missing. Run the installer first.");
            var app = Path.Combine(state, "app");
            var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg
                ? xdg : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            metadata = new(1, "linux", state, app, Path.Combine(app, "host", "Lain.Host"),
                Path.Combine(app, "cli", "Lain.Cli"), Path.Combine(app, "desktop", "Lain.Desktop"),
                Path.Combine(app, "proxy", "lain-proxy"), "systemd", "lain.service",
                Path.Combine(config, "systemd", "user", "lain.service"), false);
            if (!File.Exists(Path.Combine(app, ".lain-package")))
                throw new IOException("The lain installation is missing. Run the installer first.");
        }
        Validate(metadata, state);
        return metadata;
    }

    private static void Validate(InstallationMetadata value, string state)
    {
        if (value.SchemaVersion != 1 || value.Platform is not ("linux" or "macos" or "windows"))
            throw new IOException("The lain installation metadata has an unsupported format.");
        var root = FullPath(value.InstallationRoot);
        var expectedState = value.Platform == "linux" ? root : Path.Combine(root, "data");
        if (!SamePath(expectedState, state)) throw new IOException("The lain installation belongs to a different data directory.");
        var binaries = FullPath(value.BinaryRoot);
        if (value.Platform == "linux" ? !SamePath(binaries, Path.Combine(root, "app")) : !IsBelow(binaries, Path.Combine(root, "versions")))
            throw new IOException("The lain binary directory is outside its owned installation.");
        var suffix = value.Platform == "windows" ? ".exe" : "";
        var entries = new[] { (value.HostPath, "host", "Lain.Host"), (value.CliPath, "cli", "Lain.Cli"), (value.DesktopPath, "desktop", "Lain.Desktop") };
        foreach (var (path, folder, name) in entries)
        {
            if (!SamePath(FullPath(path), Path.Combine(binaries, folder, name + suffix)))
                throw new IOException("The lain installation contains an unexpected executable path.");
            RequirePrivatePath(path);
            if (!File.Exists(path)) throw new IOException("A required lain executable is missing. Run the installer again.");
        }
        if (value.ProxyPath is { } proxy && !SamePath(FullPath(proxy), Path.Combine(binaries, "proxy", "lain-proxy" + suffix)))
            throw new IOException("The lain installation contains an unexpected Core path.");
        _ = FullPath(value.RegistrationPath);
        if (string.IsNullOrWhiteSpace(value.ServiceName) || value.ServiceName.Any(c => char.IsControl(c) || c is '/' or '\\'))
            throw new IOException("The lain user service name is invalid.");
    }

    internal static string FullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.Any(char.IsControl))
            throw new IOException("The lain installation requires absolute, single-line paths.");
        return Path.GetFullPath(path);
    }

    internal static bool SamePath(string first, string second) => string.Equals(Path.GetFullPath(first), Path.GetFullPath(second),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool IsBelow(string path, string directory) => path.StartsWith(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    internal static void RequirePrivatePath(string path)
    {
        for (var entry = new FileInfo(Path.GetFullPath(path)) as FileSystemInfo; entry is not null;
             entry = entry is FileInfo file ? file.Directory : ((DirectoryInfo)entry).Parent)
            if (entry.Exists && (entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The lain installation paths must not use symbolic links or reparse points.");
    }
}
