using Avalonia;
using Lain.Client;
using Lain.Desktop.Tray;

namespace Lain.Desktop;

internal static class Program
{
    internal static UiInstanceOwner? InstanceOwner { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        using var owner = new UiInstanceOwner(LocalDiscovery.StateDirectory);
        if (!owner.IsOwner)
        {
            if (!owner.RequestReopenAsync().GetAwaiter().GetResult())
            {
                Console.Error.WriteLine("lain UI is already running, but the window could not be reopened. Try again.");
                Environment.ExitCode = 1;
            }
            return;
        }
        InstanceOwner = owner;
        try { BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        finally { InstanceOwner = null; }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
