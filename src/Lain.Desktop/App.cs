using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Lain.Client;
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
            desktop.MainWindow = new MainWindow(LainApiClient.CreateLocal());
        base.OnFrameworkInitializationCompleted();
    }
}
