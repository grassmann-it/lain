using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Lain.Client.Lifecycle;

/// <summary>A user LaunchAgent. Only the login copy is placed in LaunchAgents.</summary>
public sealed class MacOSUserServiceManager : IUserServiceManager
{
    private readonly InstallationMetadata installation;
    private readonly ILifecycleProcessRunner runner;
    private readonly string loginPath;
    private string? domain;

    public MacOSUserServiceManager(InstallationMetadata installation, ILifecycleProcessRunner? runner = null)
    {
        this.installation = installation;
        this.runner = runner ?? new LifecycleProcessRunner();
        loginPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "LaunchAgents", installation.ServiceName + ".plist");
    }

    public async Task<UserServiceStatus> InspectAsync(CancellationToken ct = default)
    {
        try
        {
            NativeServiceFiles.Validate(installation);
            if (!File.Exists(installation.RegistrationPath))
                return new("launchd", "missing", null, "stopped", "The Lain LaunchAgent registration is missing. Run setup again.");
            ValidateAgent(installation.RegistrationPath);
            var login = File.Exists(loginPath);
            if (login)
            {
                NativeServiceFiles.ValidateOwnedFile(installation, loginPath);
                ValidateAgent(loginPath);
            }
            if (login != NativeServiceFiles.ReadLoginPreference(installation))
                return new("launchd", "modified", login, "unknown", "The Lain login registration differs from its saved preference.");
            var scope = await DomainAsync(ct).ConfigureAwait(false);
            var result = await runner.RunAsync("/bin/launchctl", ["print", scope + "/" + installation.ServiceName], ct).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                var available = await runner.RunAsync("/bin/launchctl", ["print", scope], ct).ConfigureAwait(false);
                return available.ExitCode == 0 ? new("launchd", "registered", login, "stopped")
                    : new("launchd", "unavailable", login, "unknown", "The current user's graphical launchd domain is unavailable.");
            }
            var path = Value(result.StandardOutput, "path");
            if (path != installation.RegistrationPath && path != loginPath)
                return new("launchd", "modified", login, "unknown", "A different LaunchAgent owns the Lain service name.");
            if (Value(result.StandardOutput, "program") != installation.HostPath)
                return new("launchd", "modified", login, "unknown", "The loaded Lain LaunchAgent executable has changed.");
            var state = Value(result.StandardOutput, "state") == "running" ? "running" : "stopped";
            return new("launchd", "registered", login, state);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Xml.XmlException or InvalidOperationException)
        { return new("launchd", "modified", null, "unknown", error.Message); }
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        var status = await RequireRegisteredAsync(ct).ConfigureAwait(false);
        if (status.State == "running") return;
        var scope = await DomainAsync(ct).ConfigureAwait(false);
        var loaded = await runner.RunAsync("/bin/launchctl", ["print", scope + "/" + installation.ServiceName], ct).ConfigureAwait(false);
        if (loaded.ExitCode != 0)
            await RequireSuccess(["bootstrap", scope, installation.RegistrationPath], "start", ct).ConfigureAwait(false);
        else
            await RequireSuccess(["kickstart", scope + "/" + installation.ServiceName], "start", ct).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        var status = await RequireRegisteredAsync(ct).ConfigureAwait(false);
        var scope = await DomainAsync(ct).ConfigureAwait(false);
        var loaded = await runner.RunAsync("/bin/launchctl", ["print", scope + "/" + installation.ServiceName], ct).ConfigureAwait(false);
        if (loaded.ExitCode == 0)
            await RequireSuccess(["bootout", scope + "/" + installation.ServiceName], "stop gracefully", ct).ConfigureAwait(false);
    }

    public async Task RestartAsync(CancellationToken ct = default)
    { await StopAsync(ct).ConfigureAwait(false); await StartAsync(ct).ConfigureAwait(false); }

    public async Task SetLoginAutostartAsync(bool enabled, CancellationToken ct = default)
    {
        await RequireRegisteredAsync(ct).ConfigureAwait(false);
        // The caller owns the shared lifecycle operation gate. Loading/unloading is
        // deliberately separate: changing the next login must not stop this session.
        NativeServiceFiles.ChangeLoginPreference(installation, enabled, () =>
        {
            if (enabled)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(loginPath)!);
                NativeServiceFiles.WriteAtomic(loginPath, File.ReadAllBytes(installation.RegistrationPath));
            }
            else File.Delete(loginPath);
        }, enabled ? new Dictionary<string, byte[]> { [loginPath] = File.ReadAllBytes(installation.RegistrationPath) }
            : new Dictionary<string, byte[]>(), enabled ? [] : [loginPath]);
    }

    private void ValidateAgent(string path)
    {
        NativeServiceFiles.ValidateOwnedFile(installation, path);
        var root = XDocument.Load(path).Root?.Element("dict") ?? throw new IOException("The Lain LaunchAgent plist is invalid.");
        var values = root.Elements().ToArray();
        var entries = new Dictionary<string, XElement>(StringComparer.Ordinal);
        for (var n = 0; n + 1 < values.Length; n += 2)
            if (!entries.TryAdd(values[n].Value, values[n + 1])) throw new IOException("The Lain LaunchAgent plist contains duplicate keys.");
        var args = entries.GetValueOrDefault("ProgramArguments")?.Elements("string").Select(x => x.Value).ToArray();
        var env = entries.GetValueOrDefault("EnvironmentVariables");
        var envValues = env?.Elements().ToArray() ?? [];
        var variables = new Dictionary<string, string>();
        for (var n = 0; n + 1 < envValues.Length; n += 2) variables.Add(envValues[n].Value, envValues[n + 1].Value);
        if (entries.GetValueOrDefault("Label")?.Value != installation.ServiceName || args is not { Length: 1 } ||
            args[0] != installation.HostPath || variables.GetValueOrDefault("LAIN_HOME") != Path.Combine(installation.InstallationRoot, "data") ||
            entries.GetValueOrDefault("WorkingDirectory")?.Value != Path.Combine(installation.InstallationRoot, "data"))
            throw new IOException("The Lain LaunchAgent registration does not match this installation.");
    }

    private async Task<UserServiceStatus> RequireRegisteredAsync(CancellationToken ct)
    {
        var status = await InspectAsync(ct).ConfigureAwait(false);
        if (status.RegistrationState != "registered") throw new IOException(status.Error ?? "The Lain LaunchAgent is unavailable.");
        return status;
    }
    private async Task<string> DomainAsync(CancellationToken ct)
    {
        if (domain is not null) return domain;
        var result = await runner.RunAsync("/usr/bin/id", ["-u"], ct).ConfigureAwait(false);
        if (result.ExitCode != 0 || !uint.TryParse(result.StandardOutput.Trim(), out var uid) || uid == 0)
            throw new IOException("Lain requires an ordinary graphical user session.");
        return domain = "gui/" + uid;
    }
    private async Task RequireSuccess(IReadOnlyList<string> args, string action, CancellationToken ct)
    {
        var result = await runner.RunAsync("/bin/launchctl", args, ct).ConfigureAwait(false);
        if (result.ExitCode != 0) throw new IOException($"launchd could not {action} Lain (exit {result.ExitCode}).");
    }
    private static string? Value(string output, string key) => output.Split('\n').Select(x => x.Trim())
        .FirstOrDefault(x => x.StartsWith(key + " = ", StringComparison.Ordinal))?[(key.Length + 3)..];
}

/// <summary>Owned native registrations are checksummed; incomplete changes fail closed.</summary>
internal static class NativeServiceFiles
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static string Ledger(InstallationMetadata value) => Path.Combine(value.InstallationRoot, "installer-owned.json");
    private static string Metadata(InstallationMetadata value) => Path.Combine(value.InstallationRoot, "data", "installation.json");
    internal static void Validate(InstallationMetadata value)
    {
        if (File.Exists(Path.Combine(value.InstallationRoot, "integration-pending.json")))
            throw new IOException("A Lain integration change was interrupted. Inspect integration-pending.json before retrying.");
        RefuseLinks(value.RegistrationPath);
    }
    internal static bool ReadLoginPreference(InstallationMetadata value)
    {
        RefuseLinks(Metadata(value));
        return JsonNode.Parse(File.ReadAllText(Metadata(value)))?["loginAutostart"]?.GetValue<bool>()
            ?? throw new IOException("The Lain startup preference is missing.");
    }
    internal static void ValidateOwnedFile(InstallationMetadata value, string path)
    {
        RefuseLinks(path);
        RefuseLinks(Ledger(value));
        var expected = JsonNode.Parse(File.ReadAllText(Ledger(value)))?["files"]?[path]?.GetValue<string>();
        if (!File.Exists(path) || expected is null || Hash(File.ReadAllBytes(path)) != expected)
            throw new IOException("The Lain integration is missing or customized. Run setup after resolving the change: " + path);
    }
    internal static void ChangeLoginPreference(InstallationMetadata value, bool enabled, Action mutation,
        Dictionary<string, byte[]> changedFiles, string[] removedFiles)
    {
        Validate(value);
        RefuseLinks(Ledger(value)); RefuseLinks(Metadata(value));
        var ledger = JsonNode.Parse(File.ReadAllText(Ledger(value)))!.AsObject();
        var metadata = JsonNode.Parse(File.ReadAllText(Metadata(value)))!.AsObject();
        var files = ledger["files"]!.AsObject();
        foreach (var path in changedFiles.Keys.Concat(removedFiles))
        {
            RefuseLinks(path);
            if (File.Exists(path)) ValidateOwnedFile(value, path);
        }
        var pending = Path.Combine(value.InstallationRoot, "integration-pending.json");
        using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(stream, new { operation = "set-login-autostart", enabled }, Json); stream.Flush(true); }
        mutation();
        foreach (var entry in changedFiles) files[entry.Key] = Hash(entry.Value);
        foreach (var path in removedFiles) files.Remove(path);
        ledger["loginAutostart"] = enabled;
        metadata["loginAutostart"] = enabled;
        WriteAtomic(Ledger(value), System.Text.Encoding.UTF8.GetBytes(ledger.ToJsonString(Json)));
        WriteAtomic(Metadata(value), System.Text.Encoding.UTF8.GetBytes(metadata.ToJsonString(Json)));
        File.Delete(pending);
    }
    internal static void WriteAtomic(string path, byte[] bytes)
    {
        RefuseLinks(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temporary, options)) { stream.Write(bytes); stream.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal static void RefuseLinks(string path)
    {
        for (var current = new FileInfo(Path.GetFullPath(path)) as FileSystemInfo; current is not null;
            current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Lain integration paths cannot contain symbolic links or reparse points.");
    }
}
