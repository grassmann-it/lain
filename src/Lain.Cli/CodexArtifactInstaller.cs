using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
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
    private static readonly Regex CatalogSetting = new(@"(?m)^[ \t]*model_catalog_json[ \t]*=[^\r\n]*", RegexOptions.CultureInvariant);
    public static CodexInstallResult Install(CodexProfileArtifacts artifacts, string directory,
        string? expectedProfileId = null, long? expectedRevision = null)
    {
        if (artifacts.ConfigFileName != "lain.config.toml" || artifacts.CatalogFileName != "lain.models.json")
            throw new InvalidDataException("lain host returned unexpected Codex artifact names.");
        if ((expectedProfileId is not null && expectedProfileId != artifacts.ProfileId) ||
            (expectedRevision is not null && expectedRevision != artifacts.Revision))
            throw new InvalidDataException("Codex export is stale or belongs to another profile. Prepare the selected current revision again.");
        Validate(artifacts);
        var root = Path.GetFullPath(directory);
        var configuration = Path.Combine(root, "lain.config.toml");
        var compatibilityCatalog = Path.Combine(root, "lain.models.json");
        AssertRegularPath(root);
        // TOML has no null/reset value for an inherited effort. An unqualified
        // route therefore cannot safely clear a base override through this overlay.
        // Refuse before writing, leaving the user's choice available for explicit review.
        var baseConfiguration = Path.Combine(root, "config.toml");
        AssertRegularPath(baseConfiguration);
        if (Setting(artifacts.Configuration, "model_reasoning_effort") is null && File.Exists(baseConfiguration) &&
            Regex.IsMatch(TopLevel(ReadSmall(baseConfiguration)), @"(?m)^[ \t]*model_reasoning_effort[ \t]*=", RegexOptions.CultureInvariant))
            throw new InvalidDataException("Codex base configuration has an inherited reasoning effort, but this route has no qualified effort controls. Select a source-qualified route or explicitly remove the base override before installing this profile.");
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
            var content = ReadSmall(configuration);
            if (Hash(content) != manifest.ConfigurationHash || Hash(ReadSmall(catalog)) != manifest.CatalogHash ||
                !string.Equals(Setting(content, "model_catalog_json", required: true), catalog,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return Recovery();
            bool? stale = activeProfileId is null || activeRevision is null || manifest.ProfileId is null ? null
                : activeProfileId != manifest.ProfileId || activeRevision != manifest.Revision;
            return new("installed", manifest.ProfileId, manifest.Revision, manifest.GenerationId, stale);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        { return Recovery(); }
    }

    private static string BindCatalog(string configuration, string catalog)
    {
        // Only the generated top-level setting is ours. A similarly named table key is not.
        var topLevel = TopLevel(configuration);
        if (CatalogSetting.Matches(topLevel).Count != 1)
            throw new InvalidDataException("Codex configuration must have exactly one top-level catalog setting.");
        return CatalogSetting.Replace(topLevel, _ => "model_catalog_json = " + JsonSerializer.Serialize(catalog)) +
            configuration[topLevel.Length..];
    }

    /// <summary>Validate the returned pair before creating files or an installation journal.</summary>
    private static void Validate(CodexProfileArtifacts artifacts)
    {
        if (artifacts.Revision < 0) throw new InvalidDataException("Codex export revision must be nonnegative.");
        if (artifacts.GenerationId is not null && (string.IsNullOrWhiteSpace(artifacts.ProfileId) ||
            artifacts.GenerationId != Hash(artifacts.ProfileId + "\n" + artifacts.Revision.ToString(CultureInfo.InvariantCulture) + "\n" +
                artifacts.Configuration + "\n" + artifacts.Catalog)))
            throw new InvalidDataException("Codex export generation does not match its profile, revision, configuration and catalog. Prepare the export again.");
        var selected = Setting(artifacts.Configuration, "model", required: true)!;
        Setting(artifacts.Configuration, "model_catalog_json", required: true);
        var effort = Setting(artifacts.Configuration, "model_reasoning_effort");
        try
        {
            using var catalog = JsonDocument.Parse(artifacts.Catalog);
            var models = Property(catalog.RootElement, "models");
            if (models is not { ValueKind: JsonValueKind.Array } || models.Value.GetArrayLength() == 0)
                throw new InvalidDataException("Codex catalog must contain models.");
            var slugs = new HashSet<string>(StringComparer.Ordinal);
            var found = false;
            foreach (var model in models.Value.EnumerateArray())
            {
                var slug = Text(Property(model, "slug"), "model slug");
                if (!slugs.Add(slug)) throw new InvalidDataException("Codex catalog has duplicate model slugs.");
                var levels = Property(model, "supported_reasoning_levels");
                var supported = new HashSet<string>(StringComparer.Ordinal);
                if (levels is not null)
                {
                    if (levels.Value.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Codex reasoning levels must be an array.");
                    foreach (var level in levels.Value.EnumerateArray())
                        if (!supported.Add(Text(Property(level, "effort"), "reasoning effort")))
                            throw new InvalidDataException("Codex catalog has duplicate reasoning efforts.");
                }
                var defaultValue = Property(model, "default_reasoning_level");
                var defaultEffort = defaultValue is null or { ValueKind: JsonValueKind.Null } ? null : Text(defaultValue, "default reasoning effort");
                if ((defaultEffort is not null && !supported.Contains(defaultEffort)) || (defaultEffort is null && supported.Count > 0))
                    throw new InvalidDataException($"Codex model '{slug}' must advertise its default reasoning effort in supported_reasoning_levels. Export a current catalog.");
                if (slug != selected) continue;
                found = true;
                if ((Property(model, "visibility") is { ValueKind: JsonValueKind.String } visibility && visibility.GetString() == "hide") ||
                    Property(model, "supported_in_api") is { ValueKind: JsonValueKind.False })
                    throw new InvalidDataException("Codex default model is hidden or unavailable.");
                if (effort is not null && !supported.Contains(effort))
                    throw new InvalidDataException($"Reasoning effort '{effort}' is not advertised by Codex model '{slug}'. Select an advertised effort explicitly or export its supported default.");
                if (effort is null && supported.Count > 0)
                    throw new InvalidDataException("Codex configuration must bind a supported default reasoning effort explicitly; inherited values are ambiguous. Export the selected model again.");
            }
            if (!found) throw new InvalidDataException("Codex default model is absent from the returned catalog. Prepare the selected profile again.");
        }
        catch (JsonException error) { throw new InvalidDataException("Codex export catalog is invalid JSON.", error); }
    }

    // The host emits JSON-escaped TOML basic strings. Parse only that generated
    // top-level contract, never a user's general TOML configuration or table keys.
    private static string TopLevel(string configuration)
    {
        var table = Regex.Match(configuration, @"(?m)^[ \t]*\[", RegexOptions.CultureInvariant);
        return table.Success ? configuration[..table.Index] : configuration;
    }
    private static string? Setting(string configuration, string name, bool required = false)
    {
        var matches = Regex.Matches(TopLevel(configuration), @"(?m)^[ \t]*" + Regex.Escape(name) + @"[ \t]*=([^\r\n]*)", RegexOptions.CultureInvariant);
        if (matches.Count == 0 && !required) return null;
        if (matches.Count != 1) throw new InvalidDataException($"Codex configuration must have exactly one top-level {name} setting.");
        try
        {
            using var value = JsonDocument.Parse(matches[0].Groups[1].Value);
            return Text(value.RootElement, name);
        }
        catch (JsonException error) { throw new InvalidDataException($"Codex generated {name} must be a quoted basic string.", error); }
    }
    private static string Text(JsonElement? value, string label)
    {
        if (value is not { ValueKind: JsonValueKind.String } text || string.IsNullOrWhiteSpace(text.GetString()) || text.GetString()!.Any(char.IsControl))
            throw new InvalidDataException($"Codex {label} must be a nonempty string without control characters.");
        return text.GetString()!;
    }
    private static JsonElement? Property(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Codex catalog controls must be objects.");
        JsonElement? found = null;
        foreach (var property in parent.EnumerateObject().Where(property => property.Name == name))
        {
            if (found is not null) throw new InvalidDataException($"Codex catalog has duplicate {name} controls.");
            found = property.Value;
        }
        return found;
    }

    private static CodexInstallationStatus Recovery() => new("recovery-required", RecoveryAction:
        "Export and install the selected profile again. Existing immutable catalogs are retained for running clients.");
    private sealed record InstallManifest(string GenerationId, string? ProfileId, long Revision,
        string ConfigurationHash, string CatalogHash, string? ExportGenerationId);
    private static string Hash(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    private static string ReadSmall(string path)
    {
        using var stream = WorkerProjectFiles.OpenRead(path);
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
