using System.Text.RegularExpressions;

namespace Lain.Client.Lifecycle;

/// <summary>Controls only the installed per-user unit; never starts a detached Host.</summary>
public sealed class LinuxUserServiceManager(InstallationMetadata installation, ILifecycleProcessRunner? runner = null) : IUserServiceManager
{
    private readonly ILifecycleProcessRunner processRunner = runner ?? new LifecycleProcessRunner();

    public async Task<UserServiceStatus> InspectAsync(CancellationToken ct = default)
    {
        var ownership = CheckUnit();
        if (ownership is not null) return new("systemd", ownership.Value.State, null, "unknown", ownership.Value.Error);
        LifecycleProcessResult result;
        try
        {
            result = await Run(["show", installation.ServiceName, "--no-pager",
                "--property=LoadState,UnitFileState,ActiveState,FragmentPath,DropInPaths,ExecStart,NeedDaemonReload"], ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        { return new("systemd", "unavailable", null, "unknown", "The systemd user manager is unavailable. Check that this session has a user bus."); }
        var properties = Properties(result.StandardOutput);
        if (result.ExitCode != 0 && !properties.ContainsKey("LoadState"))
            return new("systemd", "unavailable", null, "unknown", "The systemd user manager is unavailable. Check that this session has a user bus.");
        var unitState = properties.GetValueOrDefault("UnitFileState");
        bool? enabled = unitState is "enabled" or "enabled-runtime" ? true : unitState is "disabled" or "static" or "indirect" or "masked" or "masked-runtime" ? false : null;
        if (properties.GetValueOrDefault("LoadState") == "not-found")
            return new("systemd", "missing", enabled, "stopped", "The owned user unit has not been loaded. Start will reload the user manager.");
        if (properties.GetValueOrDefault("NeedDaemonReload") == "yes")
            return new("systemd", "missing", enabled, "unknown", "The owned user unit changed on disk. Start will reload the user manager before continuing.");
        if (properties.GetValueOrDefault("LoadState") != "loaded")
            return new("systemd", "modified", enabled, "unknown", "The lain user service is masked or has an invalid registration.");
        if (!InstallationDiscovery.SamePath(properties.GetValueOrDefault("FragmentPath") ?? "/", installation.RegistrationPath)
            || !string.IsNullOrWhiteSpace(properties.GetValueOrDefault("DropInPaths"))
            || !OwnsLoadedCommand(properties.GetValueOrDefault("ExecStart")))
            return new("systemd", "modified", enabled, "unknown", "The lain user service has a foreign registration or custom overrides. Review it before continuing.");
        var state = properties.GetValueOrDefault("ActiveState") switch {
            "active" or "activating" or "reloading" => "running", "inactive" or "deactivating" => "stopped", "failed" => "failed", _ => "unknown"
        };
        return new("systemd", "registered", enabled, state);
    }

    public Task StartAsync(CancellationToken ct = default) => Mutate("start", ct);
    public Task StopAsync(CancellationToken ct = default) => Mutate("stop", ct);
    public Task RestartAsync(CancellationToken ct = default) => Mutate("restart", ct);
    public Task SetLoginAutostartAsync(bool enabled, CancellationToken ct = default) => Mutate(enabled ? "enable" : "disable", ct);

    private async Task Mutate(string action, CancellationToken ct)
    {
        var status = await InspectAsync(ct).ConfigureAwait(false);
        if (status.RegistrationState == "missing" && CheckUnit() is null)
        {
            await RequireSuccess(["daemon-reload"], ct).ConfigureAwait(false);
            status = await InspectAsync(ct).ConfigureAwait(false);
        }
        if (status.RegistrationState != "registered") throw new IOException(status.Error ?? "The owned lain user service is not registered.");
        await RequireSuccess([action, installation.ServiceName], ct).ConfigureAwait(false);
    }

    private (string State, string Error)? CheckUnit()
    {
        if (installation.Manager != "systemd" || installation.ServiceName != "lain.service")
            return ("modified", "The lain installation does not identify the supported systemd user service.");
        var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg
            ? xdg : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        if (!InstallationDiscovery.SamePath(installation.RegistrationPath, Path.Combine(config, "systemd", "user", "lain.service")))
            return ("modified", "The lain user service registration is outside the expected user manager directory.");
        if (!File.Exists(installation.RegistrationPath)) return ("missing", "The lain user service registration is missing. Run the installer again.");
        try
        {
            InstallationDiscovery.RequirePrivatePath(installation.RegistrationPath);
            var templatePath = Path.Combine(installation.BinaryRoot, "packaging", "lain.service.in");
            InstallationDiscovery.RequirePrivatePath(templatePath);
            if (!File.Exists(templatePath)) return ("modified", "The installed lain service template is missing. Run the installer again.");
            var escaped = installation.InstallationRoot.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal).Replace("%", "%%", StringComparison.Ordinal);
            var expected = File.ReadAllText(templatePath).Replace("@HOME@", escaped, StringComparison.Ordinal)
                .Replace("@WORKING_DIRECTORY@", installation.InstallationRoot.Replace("%", "%%", StringComparison.Ordinal), StringComparison.Ordinal);
            if (!string.Equals(File.ReadAllText(installation.RegistrationPath), expected, StringComparison.Ordinal))
                return ("modified", "The lain user service was customized. Preserve it and resolve the differences before continuing.");
        }
        catch (IOException) { return ("modified", "The lain user service registration could not be verified."); }
        return null;
    }

    private Task<LifecycleProcessResult> Run(IReadOnlyList<string> arguments, CancellationToken ct) =>
        processRunner.RunAsync("systemctl", new[] { "--user" }.Concat(arguments).ToArray(), ct);

    private async Task RequireSuccess(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var result = await Run(arguments, ct).ConfigureAwait(false);
        if (result.ExitCode != 0) throw new IOException("The systemd user service action failed. Check the user bus and the lain service journal.");
    }

    private bool OwnsLoadedCommand(string? command)
    {
        if (command is null) return false;
        var matches = Regex.Matches(command, @"\{ path=(.*?) ; argv\[\]=(.*?) ;");
        if (matches.Count != 1) return false;
        try
        {
            return InstallationDiscovery.SamePath(Regex.Unescape(matches[0].Groups[1].Value), installation.HostPath)
                && string.Equals(Regex.Unescape(matches[0].Groups[2].Value), installation.HostPath, StringComparison.Ordinal);
        }
        catch (ArgumentException) { return false; }
    }

    private static Dictionary<string, string> Properties(string output) => output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.TrimEnd('\r').Split('=', 2)).Where(parts => parts.Length == 2)
        .GroupBy(parts => parts[0], StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Last()[1], StringComparer.Ordinal);
}
