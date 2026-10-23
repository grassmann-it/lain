using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Simple;
using Lain.Client;

namespace Lain.Desktop;

public sealed class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new SimpleTheme());
        Styles.Add(Ui.CreateStyles());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow(LainApiClient.CreateLocal());
        base.OnFrameworkInitializationCompleted();
    }
}
