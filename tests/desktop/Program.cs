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
        return AppBuilder.Configure<SmokeApp>().UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime(args);
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
                await scenario.SaveFailureEvidence(error);
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
        await CheckQuotaOverview();
        await CaptureBoth("overview");
        await Click("Accounts");
        await CheckQuotaAccounts();
        await CaptureBoth("accounts");
        var accountList = Controls<ListBox>().Single(x => AutomationProperties.GetName(x) == "Accounts");
        var accountItem = accountList.GetVisualDescendants().OfType<ListBoxItem>().Single(x => x.IsSelected);
        accountItem.Focus();
        Check(accountItem.IsFocused, "selected account row accepts keyboard focus");
        var statusCalls = fixture.StatusCalls;
        await Task.Delay(10300);
        Check(fixture.StatusCalls > statusCalls && accountItem.IsFocused && Controls<ListBox>().Contains(accountList) && accountList.GetVisualDescendants().Contains(accountItem), "account list focus and identity survive passive refresh");
        fixture.Config = fixture.Config with { Revision = fixture.Config.Revision + 1,
            Accounts = fixture.Config.Accounts.Select(a => a.Id == "a1" ? a with { Name = "Personal Codex updated" } : a).ToArray() };
        await Click("Refresh");
        await Wait(() => HasText("Personal Codex updated"), "explicit refresh displays changed configuration");
        await Click("Connect Codex");
        await Wait(() => HasText("Waiting for authorization"), "login waiting");
        await CaptureBoth("login");
        await Wait(() => HasText("Codex account connected."), "login completion", 6000);
        Check(fixture.LoginPolls > 0, "login polled fake provider");
        await Click("Connect Codex"); await Click("Cancel sign-in");
        Check(fixture.LoginCancels == 1, "login cancellation");

        await Click("Add API account");
        var accountName = Input("My API account");
        accountName.Focus();
        Check(accountName.IsFocused, "account input accepts keyboard focus");
        accountName.Text = "Command account";
        Input("e.g. command-code").Text = "command-code";
        Input("https://…/v1").Text = "https://example.invalid/v1";
        Input("API key").Text = "fake-test-key-never-a-secret";
        await Click("Add account");
        await Wait(() => fixture.Config.Accounts.Length == Fixture.InitialAccounts + 1, "account added");
        await Click("Disable"); Check(fixture.EnableCalls == 1, "account disabled");
        await Click("Import setup…"); Input("/path/to/config.yaml").Text = "/fixture/config.yaml";
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
        await CaptureBoth("profile-editor");
        await Click("Save profile");
        await Wait(() => fixture.Config.Profiles.Length == Fixture.InitialProfiles + 1, "revisioned profile saved");
        await CaptureBoth("profiles"); await Click("Apply profile");
        Check(fixture.ApplyCalls == 1, "profile applied");
        await Click("Connect Codex…");
        Input("/absolute/path/to/lain").Text = "/usr/bin/true";
        var export = Path.Combine(Output, "export"); Directory.CreateDirectory(export);
        Input("Choose or enter an existing parent folder").Text = export;
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
        await Click("Details for attempt attempt-fixture");
        var details = Controls<Button>().Single(b => AutomationProperties.GetName(b) == "Details for attempt attempt-fixture");
        details.Focus();
        Check(details.IsFocused, "attempt details accepts keyboard focus");
        var attemptPollStart = fixture.UsageCalls;
        await Task.Delay(5300);
        Check(fixture.UsageCalls > attemptPollStart && Controls<Button>().Any(b => AutomationProperties.GetName(b) == "Details for attempt attempt-fixture" && b.IsFocused), "attempt detail focus survives polling");
        var daily = Controls<Expander>().Single(x => Equals(x.Header, "Daily totals"));
        daily.IsExpanded = true;
        await CaptureBoth("usage-details");
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
        await Wait(() => HasText("Local host unavailable", true), "offline host state visible");
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

    private async Task CheckQuotaOverview()
    {
        await Wait(() => Controls<ProgressBar>().Length >= 4, "overview displays known provider budgets");
        CheckQuotaBar("Personal Codex", "5 hours", 37);
        CheckQuotaBar("Command Code Pro", "Weekly", 55);
        CheckQuotaBar("Z.ai Coding", "Credits", 25);
        CheckQuotaBar("Kimi Coding", "5 hours", 100);
        Check(HasText("MiMo API") && HasText("Not available", true), "overview distinguishes unsupported MiMo from an empty balance");
        Check(!Controls<ProgressBar>().Any(x => AutomationProperties.GetName(x)?.Contains("MiMo", StringComparison.Ordinal) == true), "unsupported overview provider has no invented progress");
    }

    private async Task CheckQuotaAccounts()
    {
        await Wait(() => Controls<ListBox>().Any(x => AutomationProperties.GetName(x) == "Accounts"), "account list ready for quota inspection");
        var accounts = Controls<ListBox>().Single(x => AutomationProperties.GetName(x) == "Accounts");
        foreach (var (id, name, windows) in new (string, string, (string Label, double Percent)[])[]
        {
            ("a1", "Personal Codex", [("5 hours", 37)]),
            ("a2", "Command Code Pro", [("5 hours", 20), ("Weekly", 55), ("Monthly credits", 25)]),
            ("a3", "Z.ai Coding", [("Credits", 25)]),
            ("a4", "Kimi Coding", [("5 hours", 100), ("Monthly", 62)])
        })
        {
            var account = accounts.ItemsSource!.Cast<AccountDto>().Single(a => a.Id == id);
            accounts.SelectedItem = account;
            accounts.ScrollIntoView(account);
            await Task.Delay(120);
            foreach (var (label, percent) in windows) CheckQuotaBar(name, label, percent);
            if (id == "a2") Check(HasText("Last known"), "stale Command window is labelled as last known");
            if (id == "a4") Check(HasText("38% left"), "percentage-only Kimi window shows justified remaining percentage");
            if (id != "a1") await CaptureBoth($"accounts-{id}", () => accounts.ScrollIntoView(account));
        }
        accounts.SelectedItem = accounts.ItemsSource!.Cast<AccountDto>().Single(a => a.Id == "a3");
        await Task.Delay(120);
        Check(HasText("750", true) && HasText("credits", true), "Z.ai exposes its numeric remaining credit balance");
        var technical = Controls<Expander>().Single(x => Equals(x.Header, "Technical details"));
        technical.IsExpanded = true;
        await Task.Delay(120);
        var provenance = technical.GetVisualDescendants().OfType<SelectableTextBlock>().Single(x => x.Text?.Contains("observed", StringComparison.Ordinal) == true);
        Check(technical.GetVisualDescendants().OfType<SelectableTextBlock>().Any(x => x.Text?.Contains("https://api.z.ai/", StringComparison.Ordinal) == true)
            && provenance.Text!.Contains("last success", StringComparison.Ordinal), "expanded quota details expose source and snapshot freshness");
        await CaptureBoth("accounts-a3-technical", () => provenance.BringIntoView());
        technical.IsExpanded = false;
        var mimo = accounts.ItemsSource!.Cast<AccountDto>().Single(a => a.Id == "a5");
        accounts.SelectedItem = mimo;
        accounts.ScrollIntoView(mimo);
        await Task.Delay(120);
        await CaptureBoth("accounts-a5", () => accounts.ScrollIntoView(mimo));
        Check(HasText("Not available", true), "accounts explain unavailable MiMo quota");
        Check(!Controls<ProgressBar>().Any(x => AutomationProperties.GetName(x)?.Contains("MiMo", StringComparison.Ordinal) == true), "unsupported account has no zero-percent quota bar");
        // Keep the existing account/focus workflows on Personal Codex.
        var personal = accounts.ItemsSource!.Cast<AccountDto>().Single(a => a.Id == "a1");
        accounts.SelectedItem = personal;
        accounts.ScrollIntoView(personal);
        accounts.BringIntoView();
        await Task.Delay(120);
    }

    private void CheckQuotaBar(string account, string label, double percent)
    {
        var name = $"{account} · {label} · consumed";
        var bars = Controls<ProgressBar>().Where(x => AutomationProperties.GetName(x) == name).ToArray();
        Check(bars.Length > 0, $"semantic quota progress label: {name}");
        Check(bars.All(bar => Math.Abs(bar.Minimum) < .001 && Math.Abs(bar.Maximum - 100) < .001 && Math.Abs(bar.Value - percent) < .001 && !bar.IsIndeterminate),
            $"{account} {label} progress is {percent}% consumed");
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
    private TextBox Input(string watermark) => Controls<TextBox>().Single(x => Equals(x.PlaceholderText, watermark));
    private bool HasText(string value, bool contains = false) => Controls<TextBlock>().Any(t => contains ? t.Text?.Contains(value, StringComparison.OrdinalIgnoreCase) == true : t.Text == value);
    private bool UsageConnected() => HasText("Connected · source: lain host / persistent usage API");
    private Task ClickUsageApply() => Click("Apply");
    private async Task Click(string label)
    {
        // Selected-list pages may have several row actions. Select the first visible action.
        var button = Controls<Button>().FirstOrDefault(b => (Equals(b.Content, label) || AutomationProperties.GetName(b) == label) && b.IsEnabled)
            ?? throw new Exception($"Enabled button not found: {label}. Available: {string.Join(", ", Controls<Button>().Select(b => b.Content))}");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(180);
    }
    private async Task CaptureBoth(string page, Action? reveal = null)
    {
        foreach (var (width, height) in new[] { (1120, 780), (800, 560) })
        {
            var matches = await ResizeViewport(width, height);
            if (reveal is not null) { reveal(); await Task.Delay(80); window.UpdateLayout(); }
            // Capture before asserting: a constrained or late resize must leave an image and actual geometry.
            var actual = window.Bounds.Size;
            var suffix = matches ? "" : $"-actual-{Math.Ceiling(actual.Width)}x{Math.Ceiling(actual.Height)}";
            var file = $"{page}-{width}x{height}{suffix}.png";
            SaveWindowImage(file);
            screenshots.Add(new { page, width, height, actualWidth = actual.Width, actualHeight = actual.Height, file });
            if (!matches)
                await File.WriteAllTextAsync(Path.Combine(Output, $"{page}-{width}x{height}-layout.json"), JsonSerializer.Serialize(LayoutEvidence(), new JsonSerializerOptions { WriteIndented = true }));
            Check(matches, $"{page} viewport {width}x{height}; actual bounds {actual.Width:0.##}x{actual.Height:0.##}, client {window.ClientSize.Width:0.##}x{window.ClientSize.Height:0.##}; image {file}");
        }
        await ResizeViewport(1120, 780);
    }

    private async Task<bool> ResizeViewport(int width, int height)
    {
        window.Width = width; window.Height = height;
        var watch = Stopwatch.StartNew();
        var stable = 0;
        while (watch.ElapsedMilliseconds < 2000)
        {
            // X11 configure events and Avalonia layout run asynchronously after Width/Height change.
            await Task.Delay(40);
            window.UpdateLayout();
            var matches = Math.Abs(window.Bounds.Width - width) < 2 && Math.Abs(window.Bounds.Height - height) < 2;
            stable = matches ? stable + 1 : 0;
            if (stable >= 3) return true;
        }
        return false;
    }

    private object LayoutEvidence() => new
    {
        requestedWidth = window.Width, requestedHeight = window.Height,
        actualWidth = window.Bounds.Width, actualHeight = window.Bounds.Height,
        clientWidth = window.ClientSize.Width, clientHeight = window.ClientSize.Height,
        window.MinWidth, window.MinHeight,
        maxWidth = double.IsFinite(window.MaxWidth) ? window.MaxWidth : (double?)null,
        maxHeight = double.IsFinite(window.MaxHeight) ? window.MaxHeight : (double?)null,
        desiredWidth = window.DesiredSize.Width, desiredHeight = window.DesiredSize.Height,
        window.WindowState, window.RenderScaling, window.IsVisible
    };

    private void SaveWindowImage(string file)
    {
        var width = Math.Max(1, (int)Math.Ceiling(window.Bounds.Width));
        var height = Math.Max(1, (int)Math.Ceiling(window.Bounds.Height));
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        bitmap.Render(window); bitmap.Save(Path.Combine(Output, file));
    }

    public async Task SaveFailureEvidence(Exception error)
    {
        try
        {
            Directory.CreateDirectory(Output);
            if (window is null) return;
            window.UpdateLayout();
            SaveWindowImage("failure.png");
            await File.WriteAllTextAsync(Path.Combine(Output, "failure-evidence.json"), JsonSerializer.Serialize(new
            {
                error = error.ToString(), layout = LayoutEvidence(), checks, screenshots, requests = fixture.Requests
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception diagnosticError) { Console.Error.WriteLine("Failure evidence could not be saved: " + diagnosticError.Message); }
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
