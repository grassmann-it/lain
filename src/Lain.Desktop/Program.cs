using Avalonia;
using Lain.Client;
using Lain.Core;
using Lain.Desktop.Tray;

namespace Lain.Desktop;

internal static class Program
{
    internal static UiInstanceOwner? InstanceOwner { get; private set; }
    private static IDisposable? _initializationAdmission;

    internal static void ReleaseInitializationAdmission() => Interlocked.Exchange(ref _initializationAdmission, null)?.Dispose();

    [STAThread]
    public static void Main(string[] args)
    {
        try { args = ConfigureStateDirectory(args); }
        catch (ArgumentException error) { Console.Error.WriteLine(error.Message); Environment.ExitCode = 2; return; }
        try { _initializationAdmission = LifecycleCoordination.AcquireOperation(LocalDiscovery.StateDirectory); }
        catch (IOException error) { Console.Error.WriteLine("Lain startup blocked: " + error.Message); Environment.ExitCode = 1; return; }
        using var initializationAdmission = _initializationAdmission;
        using var owner = new UiInstanceOwner(LocalDiscovery.StateDirectory);
        if (!owner.IsOwner)
        {
            ReleaseInitializationAdmission();
            if (!owner.RequestReopenAsync().GetAwaiter().GetResult())
            {
                Console.Error.WriteLine("lain UI is already running, but the window could not be reopened. Try again.");
                Environment.ExitCode = 1;
            }
            return;
        }
        InstanceOwner = owner;
        try { BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        finally { ReleaseInitializationAdmission(); InstanceOwner = null; }
    }

    // Native shortcuts can start Desktop directly, including an installation whose
    // versioned binaries are separate from its private data directory.
    internal static string[] ConfigureStateDirectory(string[] args)
    {
        var remaining = new List<string>();
        string? stateDirectory = null;
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] != "--lain-home") { remaining.Add(args[index]); continue; }
            if (stateDirectory is not null || ++index == args.Length || string.IsNullOrWhiteSpace(args[index])
                || !Path.IsPathFullyQualified(args[index]))
                throw new ArgumentException("--lain-home requires one absolute Lain data-directory path.");
            stateDirectory = Path.GetFullPath(args[index]);
        }
        if (stateDirectory is not null) Environment.SetEnvironmentVariable("LAIN_HOME", stateDirectory);
        return [.. remaining];
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
