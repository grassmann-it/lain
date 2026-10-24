using System.Globalization;
using System.Text.RegularExpressions;

namespace Lain.Desktop.Tray;

/// <summary>A real StatusNotifier host and a registered icon owned by this process.</summary>
public static class TrayAvailability
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(3);
    private const string Watcher = "org.kde.StatusNotifierWatcher";

    public static Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => ProbeAsync(
        new DesktopCommandRunner(), Environment.ProcessId,
        OperatingSystem.IsLinux() && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")),
        cancellationToken);

    internal static async Task<bool> ProbeAsync(IDesktopCommandRunner runner, int processId,
        bool hasSessionBus, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!hasSessionBus) return false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Deadline);
        try
        {
            var host = await PropertyAsync(runner, "IsStatusNotifierHostRegistered", deadline.Token).ConfigureAwait(false);
            if (!host.Succeeded || !IsRegisteredHost(host.Output)) return false;
            var items = await PropertyAsync(runner, "RegisteredStatusNotifierItems", deadline.Token).ConfigureAwait(false);
            if (!items.Succeeded) return false;
            // Avalonia 12.1.4 registers asynchronously. Host presence alone does
            // not prove its TrayIcon obtained a working native implementation.
            foreach (var service in RegisteredServices(items.Output))
            {
                var owner = await runner.RunAsync("/usr/bin/gdbus", ["call", "--session", "--timeout", "2",
                    "--dest", "org.freedesktop.DBus", "--object-path", "/org/freedesktop/DBus",
                    "--method", "org.freedesktop.DBus.GetConnectionUnixProcessID", service],
                    Deadline, deadline.Token).ConfigureAwait(false);
                if (owner.Succeeded && IsOwnedBy(owner.Output, processId)) return true;
            }
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
    }

    private static Task<DesktopCommandResult> PropertyAsync(IDesktopCommandRunner runner, string property, CancellationToken token) =>
        runner.RunAsync("/usr/bin/gdbus", ["call", "--session", "--timeout", "2", "--dest", Watcher,
            "--object-path", "/StatusNotifierWatcher", "--method", "org.freedesktop.DBus.Properties.Get",
            Watcher, property], Deadline, token);

    internal static bool IsRegisteredHost(string output) => Regex.IsMatch(output,
        @"^\(\s*<\s*true\s*>\s*,\s*\)\s*$", RegexOptions.CultureInvariant);

    internal static bool IsOwnedBy(string output, int processId)
    {
        var match = Regex.Match(output, @"^\(\s*uint32\s+([0-9]+)\s*,\s*\)\s*$", RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var owner)
            && owner == processId;
    }

    internal static string[] RegisteredServices(string output)
    {
        // gdbus prints a variant containing an array of service/path strings.
        // Service and object-path grammar is ASCII and needs no escape decoding.
        if (output.Length > 16384) return [];
        var match = Regex.Match(output, @"^\(\s*<\s*(?:@as\s+)?\[(.*?)\]\s*>\s*,\s*\)\s*$", RegexOptions.CultureInvariant);
        if (!match.Success) return [];
        var entries = match.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (entries.Length > 128) return [];
        var services = new List<string>();
        foreach (var entry in entries)
        {
            if (entry.Length < 3 || entry[0] is not ('\'' or '"') || entry[^1] != entry[0]) return [];
            var value = entry[1..^1];
            var slash = value.IndexOf('/');
            var service = slash < 0 ? value : value[..slash];
            if (slash >= 0 && value[slash..] != "/StatusNotifierItem") continue;
            if (!Regex.IsMatch(service, @"^(?::[0-9]+\.[0-9]+|[A-Za-z_][A-Za-z0-9_-]*(?:\.[A-Za-z_][A-Za-z0-9_-]*)+)$", RegexOptions.CultureInvariant)) return [];
            services.Add(service);
        }
        return services.Distinct(StringComparer.Ordinal).ToArray();
    }
}
