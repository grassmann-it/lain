using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Lain.Client;
using Lain.Desktop.Tray;
using Semi.Avalonia;
using System.Globalization;
using Ursa.Themes.Semi;

namespace Lain.Desktop;

public sealed class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new SemiTheme { Locale = CultureInfo.GetCultureInfo("en-US") });
        Styles.Add(new UrsaSemiTheme { Locale = CultureInfo.GetCultureInfo("en-US") });
        Styles.Add(Ui.CreateStyles());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow(LainApiClient.CreateLocal());
            desktop.MainWindow = window;
            // Hiding preserves the main window; a real close always terminates the UI.
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
            var monitorClient = LainApiClient.CreateLocal();
            var monitor = new DesktopSignalMonitor(monitorClient);
            var tray = new TrayWorkspaceController(window, () => desktop.Shutdown(), monitor.PollAsync,
                new LinuxDesktopNotificationSink(), TrayAvailability.IsAvailableAsync);
            window.AttachTrayController(tray);
            window.Opened += (_, _) => tray.Start();
            Program.InstanceOwner?.StartListening(tray.Reopen);
            desktop.Exit += (_, _) => { tray.Dispose(); monitorClient.Dispose(); };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
