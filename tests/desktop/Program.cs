using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lain.Client;
using Lain.Core;
using Lain.Desktop;
using Lain.Desktop.Usage;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Fail closed before Avalonia initializes a platform. The runner owns this PID/display.
        var pid = Environment.GetEnvironmentVariable("LAIN_TEST_XVFB_PID");
        var display = Environment.GetEnvironmentVariable("DISPLAY");
        if (!int.TryParse(pid, out _) || display is null || display != Environment.GetEnvironmentVariable("LAIN_TEST_DISPLAY") ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) ||
            !File.Exists($"/proc/{pid}/cmdline") ||
            !Path.GetFileName(File.ReadAllText($"/proc/{pid}/cmdline").Split('\0')[0]).Equals("Xvfb", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Refusing GUI startup: use tests/desktop/run-private-xvfb.sh (private Xvfb required).");
            return 2;
        }
        return AppBuilder.Configure<SmokeApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}

internal sealed class SmokeApp : Application
{
    public override void Initialize()
    {
        // Reuse production initialization (Semi/Ursa resources included), not a test theme.
        // Do not invoke production framework startup: it creates a real local API client.
        var production = new App();
        production.Initialize();
        RequestedThemeVariant = production.RequestedThemeVariant;
        var resources = production.Resources;
        production.Resources = new ResourceDictionary();
        Resources = resources;
        var styles = production.Styles.ToArray();
        production.Styles.Clear();
        foreach (var style in styles) Styles.Add(style);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Dispatcher.UIThread.Post(async () =>
        {
            var scenario = new Scenario(lifetime);
            try { await scenario.Run(); lifetime.Shutdown(0); }
            catch (Exception error)
            {
                await File.WriteAllTextAsync(Path.Combine(Scenario.Output, "failure.txt"), error.ToString());
                Console.Error.WriteLine("FAIL desktop smoke: " + error.Message);
                lifetime.Shutdown(1);
            }
        });
        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class Scenario(IClassicDesktopStyleApplicationLifetime lifetime)
{
    public static readonly string Output = Environment.GetEnvironmentVariable("LAIN_TEST_OUTPUT") ?? throw new InvalidOperationException("Runner output missing");
    private readonly Fixture fixture = new();
    private readonly List<string> checks = [];
    private readonly List<object> screenshots = [];
    private MainWindow window = null!;
    private readonly List<HttpClient> transports = [];

    public async Task Run()
    {
        Directory.CreateDirectory(Output);
        await Open();
        await CaptureBoth("overview");
        await Click("Accounts"); await CaptureBoth("accounts");
        await Click("Connect Codex");
        await Wait(() => HasText("Sign-in · waiting"), "login waiting");
        await CaptureBoth("login");
        await Wait(() => HasText("Codex account connected."), "login completion", 6000);
        Check(fixture.LoginPolls > 0, "login polled fake provider");
        await Click("Connect Codex"); await Click("Cancel login");
        Check(fixture.LoginCancels == 1, "login cancellation");

        await Click("Add API account");
        var accountName = Input("My API account");
        accountName.Focus();
        Check(accountName.IsFocused, "account input accepts keyboard focus");
        accountName.Text = "Command account";
        Input("Provider identifier, e.g. command-code").Text = "command-code";
        Input("https://…/v1").Text = "https://example.invalid/v1";
        Input("API key").Text = "fake-test-key-never-a-secret";
        await Click("Add account");
        await Wait(() => fixture.Config.Accounts.Length == Fixture.InitialAccounts + 1, "account added");
        await Click("Disable"); Check(fixture.EnableCalls == 1, "account disabled");
        await Click("Import existing setup"); Input("/path/to/config.yaml").Text = "/fixture/config.yaml";
        await Click("Import");
        Check(fixture.Imports == 1 && HasText("snapshot warning", true), "import and warning preserved");

        await Click("Routes"); await CaptureBoth("routes"); await Click("Create route");
        Controls<ComboBox>().First().SelectedIndex = 1;
        Input("client-facing-model").Text = "test-route";
        Input("Upstream model ID").Text = "test-model";
        Input("Provider identifier").Text = "openai";
        Controls<CheckBox>().First(x => x.Content?.ToString()?.StartsWith("Personal") == true).IsChecked = true;
        await CaptureBoth("route-editor"); await Click("Save route");
        await Wait(() => fixture.Config.Routes.Length == Fixture.InitialRoutes + 1, "revisioned route saved");

        await Click("Profiles"); await Click("Create profile");
        Input("Profile name").Text = "Focus";
        Controls<CheckBox>().First().IsChecked = true;
        await Click("Save profile");
        await Wait(() => fixture.Config.Profiles.Length == Fixture.InitialProfiles + 1, "revisioned profile saved");
        await CaptureBoth("profiles"); await Click("Apply");
        Check(fixture.ApplyCalls == 1, "profile applied");
        await Click("Connect Codex…");
        Input("/absolute/path/to/lain").Text = "/usr/bin/true";
        var export = Path.Combine(Output, "export"); Directory.CreateDirectory(export);
        Input("Choose or enter an export destination").Text = export;
        await Click("Export Codex connection");
        await Wait(() => HasText("Ready for Codex"), "Codex export ready");
        Check(fixture.ExportCalls == 1 && Directory.GetFiles(export, "lain.config.toml", SearchOption.AllDirectories).Length == 1, "fake Codex artifacts exported");
        await CaptureBoth("codex-export"); await Click("Back to profiles");

        await Click("Usage"); await Wait(UsageConnected, "usage connected");
        await CaptureBoth("usage");
        var usageInput = Controls<UsageView>().Single().GetVisualDescendants().OfType<TextBox>().First();
        usageInput.Focus();
        Check(usageInput.IsFocused, "usage input accepts keyboard focus");
        var unsubmittedFilter = usageInput.Text;
        var pollStart = fixture.UsageCalls;
        await Task.Delay(5300);
        Check(fixture.UsageCalls > pollStart && UsageConnected(), "usage refreshes after five seconds");
        Check(usageInput.IsFocused && usageInput.Text == unsubmittedFilter, "usage input focus and filter survive polling");
        fixture.NullTokens = true;
        await ClickUsageApply();
        Check(Controls<UsageView>().Single().GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "—"), "null tokens render unknown");
        await CaptureBoth("usage-null");
        fixture.UsageFailure = true; await ClickUsageApply();
        await Wait(() => HasText("Usage could not be refreshed:", true), "usage API error visible");
        await CaptureBoth("usage-error");
        fixture.UsageFailure = false; fixture.NullTokens = false;
        await ClickUsageApply(); await Wait(UsageConnected, "usage recovers");
        await Click("Setup"); await CaptureBoth("setup");
        var detachedCalls = fixture.UsageCalls;
        await Task.Delay(5400);
        Check(fixture.UsageCalls == detachedCalls, "usage polling stops when detached");

        fixture.Offline = true; await Click("Refresh");
        await Wait(() => HasText("stale", true), "stale host snapshot");
        await CaptureBoth("offline");
        fixture.Offline = false; await Click("Refresh");
        await Click("Usage"); await Wait(UsageConnected, "usage reattaches");
        window.Close();
        var closedCalls = fixture.UsageCalls;
        await Task.Delay(5400);
        Check(!window.IsVisible && fixture.UsageCalls == closedCalls, "window close stops usage polling");
        await Open(); await Click("Usage"); await Wait(UsageConnected, "reopened window gets usage");
        await CaptureBoth("reopened-usage");
        window.Close();
        Check(fixture.Unexpected.Count == 0, "all requests matched fixture allowlist");
        foreach (var transport in transports) transport.Dispose();
        var assembly = typeof(MainWindow).Assembly;
        await File.WriteAllTextAsync(Path.Combine(Output, "evidence.json"), JsonSerializer.Serialize(new
        {
            result = "PASS", display = Environment.GetEnvironmentVariable("DISPLAY"), privateXvfb = true,
            assembly = assembly.Location, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))),
            checks, screenshots, requests = fixture.Requests,
            scope = "Production MainWindow, UsageView and App theme initialization; in-memory HTTP only. Button Click routed events exercise handlers, not OS pointer hit-testing. No real credentials, host, authorization browser, inference or service mutation."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS desktop smoke: {checks.Count} checks, {screenshots.Count} screenshots. {Output}/evidence.json");
    }

    private async Task Open()
    {
        var http = new HttpClient(fixture, disposeHandler: false) { BaseAddress = new Uri("http://fixture.invalid") };
        transports.Add(http);
        window = new MainWindow(new LainApiClient(http)) { Width = 1120, Height = 780 };
        lifetime.MainWindow = window; window.Show();
        await Wait(() => HasText("ready", true), "main window ready");
    }
    private T[] Controls<T>() where T : Control => window.GetVisualDescendants().OfType<T>().Where(c => c.IsVisible).ToArray();
    private TextBox Input(string watermark) => Controls<TextBox>().Single(x => Equals(x.Watermark, watermark));
    private bool HasText(string value, bool contains = false) => Controls<TextBlock>().Any(t => contains ? t.Text?.Contains(value, StringComparison.OrdinalIgnoreCase) == true : t.Text == value);
    private bool UsageConnected() => HasText("Connected · source: lain host / persistent usage API");
    private Task ClickUsageApply() => Click(Controls<Button>().Any(b => Equals(b.Content, "Apply filters")) ? "Apply filters" : "Apply");
    private async Task Click(string label)
    {
        // Selected-list pages may have several row actions. Select the first visible action.
        var button = Controls<Button>().FirstOrDefault(b => (Equals(b.Content, label) || AutomationProperties.GetName(b) == label) && b.IsEnabled)
            ?? throw new Exception($"Enabled button not found: {label}. Available: {string.Join(", ", Controls<Button>().Select(b => b.Content))}");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(180);
    }
    private async Task CaptureBoth(string page)
    {
        foreach (var (width, height) in new[] { (1120, 780), (800, 560) })
        {
            window.Width = width; window.Height = height;
            await Task.Delay(130); window.UpdateLayout();
            Check(Math.Abs(window.Bounds.Width - width) < 2 && Math.Abs(window.Bounds.Height - height) < 2, $"{page} viewport {width}x{height}");
            var file = $"{page}-{width}x{height}.png";
            using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
            bitmap.Render(window); bitmap.Save(Path.Combine(Output, file));
            screenshots.Add(new { page, width, height, file });
        }
        window.Width = 1120; window.Height = 780;
        await Task.Delay(100);
    }
    private void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("Failed: " + name);
        checks.Add(name); Console.WriteLine("PASS " + name);
    }
    private async Task Wait(Func<bool> condition, string name, int milliseconds = 5000)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds >= milliseconds) throw new Exception("Timed out: " + name);
            await Task.Delay(50);
        }
        Check(true, name);
    }
}
