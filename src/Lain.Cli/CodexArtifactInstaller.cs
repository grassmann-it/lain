using System.Text;
using Lain.Client;

namespace Lain.Cli;

public sealed record CodexInstallResult(long Revision, string ConfigurationPath, string CatalogPath, string[] LaunchArguments);

/// <summary>Installs only the two explicitly lain-owned artifacts, never Codex's ordinary config.</summary>
public static class CodexArtifactInstaller
{
    public static CodexInstallResult Install(CodexProfileArtifacts artifacts, string directory)
    {
        if (artifacts.ConfigFileName != "lain.config.toml" || artifacts.CatalogFileName != "lain.models.json")
            throw new InvalidDataException("lain host returned unexpected Codex artifact names.");
        var root = Path.GetFullPath(directory);
        var configuration = Path.Combine(root, "lain.config.toml");
        var catalog = Path.Combine(root, "lain.models.json");
        AssertRegularPath(root);
        if (!Directory.Exists(root))
        {
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(root);
            else Directory.CreateDirectory(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        AssertRegularPath(configuration); AssertRegularPath(catalog);
        var configTemporary = WriteTemporary(configuration, artifacts.Configuration);
        string? catalogTemporary = null;
        try
        {
            catalogTemporary = WriteTemporary(catalog, artifacts.Catalog);
            // Publish the referenced catalog before publishing the new configuration.
            File.Move(catalogTemporary, catalog, true);
            File.Move(configTemporary, configuration, true);
        }
        finally
        {
            if (File.Exists(configTemporary)) File.Delete(configTemporary);
            if (catalogTemporary is not null && File.Exists(catalogTemporary)) File.Delete(catalogTemporary);
        }
        return new(artifacts.Revision, configuration, catalog, artifacts.LaunchArguments);
    }

    private static string WriteTemporary(string target, string content)
    {
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using var stream = new FileStream(temporary, options);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(content); writer.Flush(); stream.Flush(true);
            return temporary;
        }
        catch { if (File.Exists(temporary)) File.Delete(temporary); throw; }
    }

    private static void AssertRegularPath(string path)
    {
        for (var current = new FileInfo(Path.GetFullPath(path)); current is not null;
             current = current.Directory is { } parent ? new FileInfo(parent.FullName) : null)
        {
            if (current.LinkTarget is not null || ((File.Exists(current.FullName) || Directory.Exists(current.FullName)) &&
                (File.GetAttributes(current.FullName) & FileAttributes.ReparsePoint) != 0))
                throw new IOException("Codex artifact paths must not contain symbolic links.");
        }
    }
}
