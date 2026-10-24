using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lain.Client;

namespace Lain.Cli;

public sealed record CodexInstallResult(long Revision, string ConfigurationPath, string CatalogPath, string[] LaunchArguments,
    string? ProfileId = null, string? GenerationId = null);
public sealed record CodexInstallationStatus(string State, string? ProfileId = null, long? Revision = null,
    string? GenerationId = null, bool? Stale = null, string? RecoveryAction = null);

/// <summary>
/// A stable configuration is the single commit point and references an immutable generation's
/// catalog. The compatibility catalog is never referenced by new configurations. Interrupted
/// bookkeeping is detected on inspection; reinstalling the selected export repairs it.
/// </summary>
public static class CodexArtifactInstaller
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Regex CatalogSetting = new(@"(?m)^model_catalog_json\s*=\s*[^\r\n]*", RegexOptions.CultureInvariant);
    public static CodexInstallResult Install(CodexProfileArtifacts artifacts, string directory)
    {
        if (artifacts.ConfigFileName != "lain.config.toml" || artifacts.CatalogFileName != "lain.models.json")
            throw new InvalidDataException("lain host returned unexpected Codex artifact names.");
        var root = Path.GetFullPath(directory);
        var configuration = Path.Combine(root, "lain.config.toml");
        var compatibilityCatalog = Path.Combine(root, "lain.models.json");
        AssertRegularPath(root);
        CreateDirectory(root);
        var storage = Path.Combine(root, ".lain-artifacts");
        AssertRegularPath(storage);
        CreateDirectory(storage);
        var lockPath = Path.Combine(storage, "install.lock");
        AssertRegularPath(lockPath);
        using var installLock = new FileStream(lockPath, PrivateOptions(FileMode.OpenOrCreate));
        AssertRegularPath(configuration); AssertRegularPath(compatibilityCatalog);
        // Local generation identity is independent of server identity and never used as a path input.
        var generation = Guid.NewGuid().ToString("N");
        var target = Path.Combine(storage, generation);
        CreateDirectory(target);
        var catalog = Path.Combine(target, "lain.models.json");
        var rewritten = BindCatalog(artifacts.Configuration, catalog);
        var manifest = new InstallManifest(generation, artifacts.ProfileId, artifacts.Revision,
            Hash(rewritten), Hash(artifacts.Catalog), artifacts.GenerationId);
        var journal = Path.Combine(storage, "pending.json");
        WriteAtomic(journal, JsonSerializer.Serialize(manifest, Json));
        WriteAtomic(catalog, artifacts.Catalog);
        WriteAtomic(Path.Combine(target, "lain.config.toml"), rewritten);
        WriteAtomic(Path.Combine(target, "manifest.json"), JsonSerializer.Serialize(manifest, Json));
        // This compatibility copy is not part of the committed pair and can be safely replaced first.
        WriteAtomic(compatibilityCatalog, artifacts.Catalog);
        WriteAtomic(configuration, rewritten);
        WriteAtomic(Path.Combine(storage, "current.json"), JsonSerializer.Serialize(manifest, Json));
        File.Delete(journal);
        return new(artifacts.Revision, configuration, catalog, artifacts.LaunchArguments, artifacts.ProfileId, generation);
    }

    /// <summary>Checks identity and content, including an interruption after the config commit.</summary>
    public static CodexInstallationStatus Inspect(string directory, string? activeProfileId = null, long? activeRevision = null)
    {
        var root = Path.GetFullPath(directory);
        var storage = Path.Combine(root, ".lain-artifacts");
        try
        {
            AssertRegularPath(root); AssertRegularPath(storage);
            if (File.Exists(Path.Combine(storage, "pending.json"))) return Recovery();
            var current = Path.Combine(storage, "current.json");
            if (!File.Exists(current)) return new("unknown", RecoveryAction: "Export and install the selected profile to establish its identity.");
            AssertRegularPath(current);
            var manifest = JsonSerializer.Deserialize<InstallManifest>(ReadSmall(current), Json);
            if (manifest is null || !Guid.TryParseExact(manifest.GenerationId, "N", out _) || manifest.Revision < 0) return Recovery();
            var configuration = Path.Combine(root, "lain.config.toml");
            var catalog = Path.Combine(storage, manifest.GenerationId, "lain.models.json");
            AssertRegularPath(configuration); AssertRegularPath(catalog);
            if (Hash(ReadSmall(configuration)) != manifest.ConfigurationHash || Hash(ReadSmall(catalog)) != manifest.CatalogHash) return Recovery();
            bool? stale = activeProfileId is null || activeRevision is null || manifest.ProfileId is null ? null
                : activeProfileId != manifest.ProfileId || activeRevision != manifest.Revision;
            return new("installed", manifest.ProfileId, manifest.Revision, manifest.GenerationId, stale);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        { return Recovery(); }
    }

    private static string BindCatalog(string configuration, string catalog)
    {
        // Only the generated top-level setting is ours. A similarly named table key is not.
        var table = Regex.Match(configuration, @"(?m)^[ \t]*\[", RegexOptions.CultureInvariant);
        var topLevel = table.Success ? configuration[..table.Index] : configuration;
        if (CatalogSetting.Matches(topLevel).Count > 1)
            throw new InvalidDataException("Codex configuration has duplicate catalog settings.");
        return CatalogSetting.Replace(topLevel, _ => "model_catalog_json = " + JsonSerializer.Serialize(catalog)) +
            (table.Success ? configuration[table.Index..] : "");
    }

    private static CodexInstallationStatus Recovery() => new("recovery-required", RecoveryAction:
        "Export and install the selected profile again. Existing immutable catalogs are retained for running clients.");
    private sealed record InstallManifest(string GenerationId, string? ProfileId, long Revision,
        string ConfigurationHash, string CatalogHash, string? ExportGenerationId);
    private static string Hash(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    private static string ReadSmall(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > 16 * 1024 * 1024) throw new IOException("Codex artifact exceeds its size limit.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
    private static void CreateDirectory(string path)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    private static FileStreamOptions PrivateOptions(FileMode mode)
    {
        var options = new FileStreamOptions { Mode = mode, Access = FileAccess.ReadWrite, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return options;
    }
    private static void WriteAtomic(string target, string content)
    {
        AssertRegularPath(target);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, PrivateOptions(FileMode.CreateNew)))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            { writer.Write(content); writer.Flush(); stream.Flush(true); }
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
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
