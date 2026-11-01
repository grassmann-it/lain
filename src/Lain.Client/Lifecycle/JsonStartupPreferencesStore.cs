using System.Text.Json;
using Lain.Core;

namespace Lain.Client.Lifecycle;

public sealed class JsonStartupPreferencesStore(string? stateDirectory = null) : IStartupPreferencesStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string state = Path.GetFullPath(stateDirectory ?? LocalDiscovery.StateDirectory);

    public async Task<StartupPreferences> ReadAsync(CancellationToken ct = default)
    {
        var path = Path.Combine(state, "startup-preferences.json");
        if (!File.Exists(path)) return new();
        InstallationDiscovery.RequirePrivatePath(path);
        try
        {
            return JsonSerializer.Deserialize<StartupPreferences>(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false), Json)
                ?? throw new IOException("The lain startup preferences are empty.");
        }
        catch (JsonException) { throw new IOException("The lain startup preferences are invalid. Review startup-preferences.json."); }
    }

    public async Task WriteAsync(StartupPreferences preferences, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var admission = LifecycleCoordination.AcquireOperation(state);
        Directory.CreateDirectory(state);
        var path = Path.Combine(state, "startup-preferences.json");
        InstallationDiscovery.RequirePrivatePath(path);
        var temp = Path.Combine(state, ".startup-preferences-" + Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(preferences, Json) + "\n", ct).ConfigureAwait(false);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temp, path, overwrite: true);
        }
        finally { File.Delete(temp); }
    }
}
