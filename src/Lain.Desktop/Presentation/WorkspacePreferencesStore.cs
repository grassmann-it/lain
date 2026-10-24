using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lain.Desktop.Presentation;

/// <summary>Local presentation state, independent of host configuration and revisions.</summary>
public sealed class WorkspacePreferencesStore
{
    private const int MaximumFileBytes = 256 * 1024;
    private const int MaximumPins = 512;
    private static readonly string[] Sections = ["Accounts", "Models", "Profiles", "Usage", "Live"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    private readonly object _gate = new();
    private readonly string? _path;
    private WorkspacePreferences _current = new();

    // Return snapshots: the shared contract contains mutable collections.
    public WorkspacePreferences Current { get { lock (_gate) return Copy(_current); } }
    public string? Error { get; private set; }

    public WorkspacePreferencesStore(string? path = null)
    {
        try
        {
            var root = Environment.GetEnvironmentVariable("LAIN_HOME") ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lain");
            _path = Path.GetFullPath(path ?? Path.Combine(root, "ui", "workspace-preferences.json"));
            _current = Read(_path);
        }
        catch (Exception ex) when (IsStorageError(ex))
        {
            Error = "Presentation preferences could not be loaded; using defaults. " + Describe(ex);
        }
    }

    /// <summary>
    /// Saves atomically. Independent edits from other windows are merged against the
    /// last loaded snapshot; simultaneous edits to the same setting use the last save.
    /// On failure the previously loaded snapshot and existing file are retained.
    /// </summary>
    public bool Save(WorkspacePreferences preferences)
    {
        lock (_gate)
        {
            try
            {
                Validate(preferences);
                var requested = Copy(preferences);
                if (_path is null) throw new IOException("Preference storage is unavailable.");
                EnsureDirectory(Path.GetDirectoryName(_path)!);
                using var fileLock = AcquireLock(_path + ".lock");
                WorkspacePreferences latest;
                try { latest = Read(_path); }
                catch (Exception ex) when (ex is JsonException or InvalidDataException)
                {
                    // An explicit successful save can repair a corrupt preference file.
                    latest = new();
                }
                var merged = Merge(_current, requested, latest);
                Validate(merged);
                WriteAtomic(_path, merged);
                _current = merged;
                Error = null;
                return true;
            }
            catch (Exception ex) when (IsStorageError(ex))
            {
                Error = "Presentation preferences could not be saved; previous settings were kept. " + Describe(ex);
                return false;
            }
        }
    }

    private static WorkspacePreferences Read(string path)
    {
        RejectLinks(path);
        if (!File.Exists(path)) return new();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > MaximumFileBytes) throw new InvalidDataException("Preference file is too large.");
        var preferences = JsonSerializer.Deserialize<WorkspacePreferences>(stream, JsonOptions);
        Validate(preferences);
        return Copy(preferences!);
    }

    private static void Validate(WorkspacePreferences? value)
    {
        if (value is null || value.Version != 1 || !Enum.IsDefined(value.Level))
            throw new InvalidDataException("Unsupported presentation preferences.");
        if (value.Densities is null || value.Densities.Any(pair =>
                !Sections.Contains(pair.Key, StringComparer.Ordinal) || !Enum.IsDefined(pair.Value)))
            throw new InvalidDataException("Unknown section or density.");
        if (value.AccountSort is not ("favorites" or "provider" or "name" or "five-hour"))
            throw new InvalidDataException("Unknown account sort order.");
        if (value.PinnedAccountIds is null || value.PinnedAccountIds.Length > MaximumPins ||
            value.PinnedAccountIds.Any(id => id is null || id.Length is < 1 or > 100 ||
                id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))) ||
            value.PinnedAccountIds.Distinct(StringComparer.Ordinal).Count() != value.PinnedAccountIds.Length)
            throw new InvalidDataException("Invalid or duplicate pinned account IDs.");
    }

    private static WorkspacePreferences Copy(WorkspacePreferences value) => value with
    {
        Densities = new(value.Densities, StringComparer.Ordinal),
        PinnedAccountIds = [.. value.PinnedAccountIds]
    };

    private static WorkspacePreferences Merge(WorkspacePreferences previous,
        WorkspacePreferences requested, WorkspacePreferences latest)
    {
        var densities = new Dictionary<string, ViewDensity>(latest.Densities, StringComparer.Ordinal);
        foreach (var section in Sections)
        {
            var hadPrevious = previous.Densities.TryGetValue(section, out var oldDensity);
            var hasRequested = requested.Densities.TryGetValue(section, out var newDensity);
            if (hadPrevious == hasRequested && (!hasRequested || oldDensity == newDensity)) continue;
            if (hasRequested) densities[section] = newDensity;
            else densities.Remove(section);
        }
        return latest with
        {
            Level = requested.Level == previous.Level ? latest.Level : requested.Level,
            Densities = densities,
            AccountSort = requested.AccountSort == previous.AccountSort ? latest.AccountSort : requested.AccountSort,
            GroupAccountsByProvider = requested.GroupAccountsByProvider == previous.GroupAccountsByProvider
                ? latest.GroupAccountsByProvider : requested.GroupAccountsByProvider,
            PinnedAccountIds = requested.PinnedAccountIds.SequenceEqual(previous.PinnedAccountIds, StringComparer.Ordinal)
                ? [.. latest.PinnedAccountIds] : [.. requested.PinnedAccountIds]
        };
    }

    private static void EnsureDirectory(string path)
    {
        RejectLinks(path);
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void RejectLinks(string path)
    {
        if (new FileInfo(path).LinkTarget is not null)
            throw new IOException("Preference paths must not contain symbolic links.");
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if (directory.LinkTarget is not null)
                throw new IOException("Preference paths must not contain symbolic links.");
    }

    private static FileStream AcquireLock(string path)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            RejectLinks(path);
            try
            {
                var options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                var stream = new FileStream(path, options);
                try
                {
                    // Existing lock files may predate the private creation mode.
                    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    return stream;
                }
                catch { stream.Dispose(); throw; }
            }
            catch (IOException) when (elapsed.ElapsedMilliseconds < 500) { Thread.Sleep(20); }
        }
    }

    private static void WriteAtomic(string path, WorkspacePreferences value)
    {
        RejectLinks(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temporary, options))
            {
                JsonSerializer.Serialize(stream, value, JsonOptions);
                stream.Flush(true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            // Cleanup failures must not report a successful rename as a failed save.
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static bool IsStorageError(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or
        JsonException or ArgumentException or NotSupportedException or System.Security.SecurityException;

    private static string Describe(Exception ex) => ex switch
    {
        JsonException or InvalidDataException => "The stored values are invalid.",
        UnauthorizedAccessException or System.Security.SecurityException => "Storage access was denied.",
        _ => "Check that the preference directory is writable and available."
    };
}
