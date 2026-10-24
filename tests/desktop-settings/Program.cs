using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lain.Client;
using Lain.Desktop;
using Lain.Desktop.Presentation;
using Lain.Desktop.Tray;
using System.Reflection;
using System.Text.Json;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var pid = Environment.GetEnvironmentVariable("LAIN_TEST_XVFB_PID");
        var display = Environment.GetEnvironmentVariable("DISPLAY");
        var home = Environment.GetEnvironmentVariable("LAIN_HOME");
        if (!int.TryParse(pid, out _) || display != Environment.GetEnvironmentVariable("LAIN_TEST_DISPLAY")
            || string.IsNullOrEmpty(display) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
            || !File.Exists($"/proc/{pid}/cmdline") || Path.GetFileName(File.ReadAllText($"/proc/{pid}/cmdline").Split('\0')[0]) != "Xvfb"
            || home is null || !home.StartsWith("/tmp/lain-settings-", StringComparison.Ordinal))
        { Console.Error.WriteLine("Private Settings Xvfb runner and synthetic state required."); return 2; }
        return AppBuilder.Configure<SettingsApp>().UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime(args);
    }
}
internal sealed class SettingsApp : Application
{
    public override void Initialize()
    {
        var production = new App(); production.Initialize();
        RequestedThemeVariant = production.RequestedThemeVariant;
        var resources = production.Resources; production.Resources = new ResourceDictionary(); Resources = resources;
        var styles = production.Styles.ToArray(); production.Styles.Clear(); foreach (var style in styles) Styles.Add(style);
    }
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Dispatcher.UIThread.Post(async () => {
            try { await new SettingsScenario(lifetime).Run(); lifetime.Shutdown(0); }
            catch (Exception error) { Console.Error.WriteLine(error); lifetime.Shutdown(1); }
        });
        base.OnFrameworkInitializationCompleted();
    }
}
internal sealed class SettingsScenario(IClassicDesktopStyleApplicationLifetime lifetime)
{
    private MainWindow window = null!;
    private readonly Fixture fixture = new();
    private readonly List<string> checks = [];
    private readonly string output = Environment.GetEnvironmentVariable("LAIN_TEST_OUTPUT")!;
    private void Check(bool value, string text) { if (!value) throw new Exception(text); checks.Add(text); }
    private T Named<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(x => AutomationProperties.GetName(x) == name);
    private async Task Click(string name)
    {
        var button = window.GetVisualDescendants().OfType<Button>().First(b => AutomationProperties.GetName(b) == name || Equals(b.Content, name));
        Check(button.IsEnabled, name + " enabled"); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(180);
    }
    private async Task Open()
    {
        window = new MainWindow(new LainApiClient(new HttpClient(fixture, false) { BaseAddress = new Uri("http://fixture.invalid") })) { Width = 1120, Height = 780 };
        lifetime.MainWindow = window; window.Show(); await Task.Delay(500);
        await Click("Settings");
    }
    private void Capture(string name)
    {
        window.UpdateLayout();
        using var image = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(window.Bounds.Width), (int)Math.Ceiling(window.Bounds.Height)), new Vector(96, 96));
        image.Render(window); image.Save(Path.Combine(output, name + ".png"));
    }
    public async Task Run()
    {
        Directory.CreateDirectory(output); await Open();
        var initialHost = JsonSerializer.Serialize(fixture.Config);
        var mutations = fixture.Requests.Count(x => !x.StartsWith("GET ", StringComparison.Ordinal));
        foreach (var group in new[] { "View density settings", "Window and notification settings", "Help and application settings" })
        {
            var control = Named<Control>(group); var p = control.TranslatePoint(default, window)!.Value;
            Check(p.Y >= 0 && p.Y + control.Bounds.Height < window.Bounds.Height - 35, group + " visible in wide first viewport");
        }
        Check(window.GetVisualDescendants().OfType<ComboBox>().Count(c => c.Items.Cast<object>().Select(x => x.ToString()).SequenceEqual(Enum.GetNames<InformationLevel>())) == 1,
            "one footer information level selector");
        Named<ComboBox>("Density section").SelectedItem = "Usage";
        Named<ComboBox>("Density for Usage").SelectedIndex = (int)ViewDensity.Compact;
        Check(new WorkspacePreferencesStore().Current.Densities["Usage"] == ViewDensity.Compact, "density persisted locally");
        Named<CheckBox>("Minimize to tray").IsChecked = false;
        Named<CheckBox>("Close to tray").IsChecked = true;
        Named<CheckBox>("Usage and rate-limit alerts").IsChecked = false;
        var saved = new WorkspacePreferencesStore().Current;
        Check(!saved.MinimizeToTray && saved.CloseToTray && !saved.NotificationsEnabled, "three independent flags persisted");
        Check(Named<TextBlock>("Tray availability").Text!.Contains("unavailable"), "unavailable tray behavior explained");
        Capture("settings-wide-tray-unavailable");
        using (var tray = new TrayWorkspaceController(window, () => { }, (_, _) => Task.FromResult(new TrayStatusSnapshot("fixture", [], [], true)), new SilentSink(), _ => Task.FromResult(true)))
        {
            window.AttachTrayController(tray); tray.Start(); await Task.Delay(200);
            Check(Named<TextBlock>("Tray availability").Text!.StartsWith("Tray available"), "available tray remains independent from flags");
            Capture("settings-wide-tray-available");
        }
        window.Width = 800; window.Height = 560; await Task.Delay(200);
        Check(Math.Abs(window.Bounds.Width - 800) < 2 && Math.Abs(window.Bounds.Height - 560) < 2, "narrow viewport allocated");
        Named<ComboBox>("Density for Usage").BringIntoView(); Capture("settings-narrow-top");
        var about = Named<Button>("About lain"); about.BringIntoView(); await Task.Delay(80);
        var position = about.TranslatePoint(default, window)!.Value;
        Check(position.Y > 0 && position.Y + about.Bounds.Height < window.Bounds.Height - 30, "narrow help reachable by scrolling");
        Capture("settings-narrow-help");
        await Click("About lain");
        var version = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Check(Named<TextBlock>("Application version").Text == version, "About shows exact desktop assembly version");
        Check(!string.IsNullOrWhiteSpace(Named<TextBlock>("Reported host version").Text), "About separates reported host version");
        Capture("about-narrow");
        await Click("Back"); await Click("Host operating mode…");
        Check(!window.GetVisualDescendants().OfType<ComboBox>().Any(c => AutomationProperties.GetName(c) == "Host operating mode"), "host mode has no ambiguous dropdown");
        await Click("Review host mode change"); await Click("Cancel");
        Check(JsonSerializer.Serialize(fixture.Config) == initialHost && fixture.Requests.Count(x => !x.StartsWith("GET ", StringComparison.Ordinal)) == mutations,
            "local settings and cancelled host review make no host mutation");
        // A real filesystem error against this runner's synthetic state only.
        var file = Path.Combine(Environment.GetEnvironmentVariable("LAIN_HOME")!, "ui/workspace-preferences.json");
        File.Move(file, file + ".saved"); Directory.CreateDirectory(file);
        try {
            Named<CheckBox>("Usage and rate-limit alerts").IsChecked = true;
            Check(Named<CheckBox>("Usage and rate-limit alerts").IsChecked == false, "failed save restores authoritative checkbox value");
            Check(window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("could not be saved") == true), "save failure visible");
            Capture("settings-save-failure");
        }
        finally { Directory.Delete(file); File.Move(file + ".saved", file); }
        window.Close(); await Open();
        Check(Named<CheckBox>("Close to tray").IsChecked == true && Named<CheckBox>("Minimize to tray").IsChecked == false
            && Named<CheckBox>("Usage and rate-limit alerts").IsChecked == false, "fresh window reloads independent flags");
        Check(new WorkspacePreferencesStore().Current.Densities["Usage"] == ViewDensity.Compact, "fresh window preserves section density");
        await File.WriteAllTextAsync(Path.Combine(output, "settings-evidence.json"), JsonSerializer.Serialize(new { checks,
            assembly = typeof(MainWindow).Assembly.Location, fixture.Requests,
            scope = "Private Xvfb, synthetic preferences and in-memory HTTP. No provider/service/user display. Host mode review cancelled." }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS {checks.Count} Settings checks"); window.Close();
    }
    private sealed class SilentSink : IDesktopNotificationSink { public Task<bool> SendAsync(DesktopAlert alert, CancellationToken cancellationToken = default) => Task.FromResult(true); }
}
