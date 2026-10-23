using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
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
        CheckBundledFonts();
        if (Environment.GetEnvironmentVariable("LAIN_DESKTOP_FONT_PROBE_ONLY") == "1") { window.Close(); return; }
        await CaptureBoth("overview");
        await Click("Accounts");
        await CheckClosedQuotaTiles("accounts");
        await CaptureBoth("accounts-closed");
        await CheckQuotaAccounts();
        await CaptureBoth("accounts");
        var accountTile = QuotaTile("Personal Codex");
        var accountHeader = TileHeader(accountTile);
        accountHeader.BringIntoView();
        accountHeader.Focus();
        Check(accountHeader.IsFocused, "account tile header accepts keyboard focus");
        var statusCalls = fixture.StatusCalls;
        await Task.Delay(10300);
        Check(fixture.StatusCalls > statusCalls && accountHeader.IsFocused && accountTile.IsExpanded && Controls<Expander>().Contains(accountTile)
            && accountTile.GetVisualDescendants().Contains(accountHeader), "account tile focus expansion and identity survive passive refresh");
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
        await CheckUsageControls();
        var usageInput = Controls<UsageView>().Single().GetVisualDescendants().OfType<TextBox>().First();
        usageInput.Focus();
        Check(usageInput.IsFocused, "usage input accepts keyboard focus");
        var unsubmittedFilter = usageInput.Text;
        var pollStart = fixture.UsageCalls;
        await Task.Delay(5300);
        Check(fixture.UsageCalls > pollStart && UsageConnected(), "usage refreshes after five seconds");
        Check(usageInput.IsFocused && usageInput.Text == unsubmittedFilter, "usage input focus and filter survive polling");
        Check(Controls<Button>().Any(b => AutomationProperties.GetName(b) == "Filter to account a1" && b.GetVisualAncestors().OfType<Control>().All(parent => parent.IsVisible))
            && Controls<Button>().Any(b => AutomationProperties.GetName(b) == "Filter to model gpt-model" && b.GetVisualAncestors().OfType<Control>().All(parent => parent.IsVisible)), "account and model detail expansion survive polling");
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

    private async Task CheckUsageControls()
    {
        T Named<T>(string name) where T : Control => Controls<T>().Single(x => AutomationProperties.GetName(x) == name);
        var daily = Controls<Expander>().Single(x => Equals(x.Header, "Daily totals"));
        var measure = Named<ComboBox>("Daily measure");
        daily.IsExpanded = true;
        var reads = fixture.UsageCalls;
        measure.SelectedIndex = 2;
        Check(fixture.UsageCalls == reads, "switching daily measure reuses the loaded snapshot");
        var output = 9986L.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
        Check(Equals(measure.SelectedItem, "Output tokens") && daily.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text == output), "daily output measure exposes the exact host output total");
        await CaptureBoth("usage-daily-output", () => daily.BringIntoView());
        measure.SelectedIndex = 0;
        Check(Equals(measure.SelectedItem, "Attempts"), "daily measure supports attempts");
        measure.SelectedIndex = 1;
        daily.IsExpanded = false;

        Named<TextBox>("Time zone").Text = "UTC";
        Named<TextBox>("HTTP status").Text = "200";
        var today = DateTimeOffset.UtcNow.UtcDateTime.Date;
        foreach (var (label, start) in new[]
        {
            ("Today", today), ("Last 7 days", today.AddDays(-6)),
            ("Last 30 days", today.AddDays(-29)), ("This month", new DateTime(today.Year, today.Month, 1))
        })
        {
            await Click(label);
            var query = fixture.UsageQueries.Last();
            Check(DateTimeOffset.Parse(query["from"]).UtcDateTime == start && DateTimeOffset.Parse(query["to"]).UtcDateTime == today.AddDays(1)
                && query["timeZone"] == "UTC" && query["offset"] == "0" && query["statusCode"] == "200", $"{label} applies its UTC calendar range and preserves the status filter");
        }
        await Click("Details for account a1");
        await Click("Details for model gpt-model");
        var errorRate = (400d / 132).ToString("N1", System.Globalization.CultureInfo.CurrentCulture) + "% error rate";
        Check(HasText(errorRate, true) && HasText("Tokens:", true), "account and model details show token fields and errors per attempt");
        await Click("Filter to account a1");
        Check(fixture.UsageQueries.Last().GetValueOrDefault("accountId") == "a1", "account drilldown applies its stable account ID");
        await Click("Filter to model gpt-model");
        var scoped = fixture.UsageQueries.Last();
        Check(scoped.GetValueOrDefault("accountId") == "a1" && scoped.GetValueOrDefault("model") == "gpt-model" && scoped.GetValueOrDefault("statusCode") == "200", "model drilldown preserves account and status filters");
        Check(HasText("Applied filters:", true) && HasText("gpt-model", true) && HasText("HTTP 200", true), "loaded usage scope is visible");
        await CaptureBoth("usage-filters", () => Named<Button>("Filter to model gpt-model").BringIntoView());

        Named<TextBox>("From · inclusive").Text = "2020-01-01";
        Named<TextBox>("To · exclusive").Text = "2024-01-01";
        await ClickUsageApply();
        Check(HasText("Choose a range of at most 366 days."), "oversized date range reports a useful validation error");
        Check(!fixture.UsageQueries.Any(query => DateTimeOffset.Parse(query["from"]).Year == 2020), "oversized date range never reaches the usage API");
        Named<ComboBox>("Account").SelectedIndex = 0;
        Named<ComboBox>("Actual model").SelectedIndex = 0;
        Named<TextBox>("HTTP status").Text = "";
        await Click("Last 7 days");
        Named<TextBox>("From · inclusive").BringIntoView();
    }

    private static readonly (string Account, (string Label, double Percent)[] Windows)[] ExpectedProviderWindows =
    [
        ("Personal Codex", [("7 days", 37)]),
        ("Command Code Pro", [("5 hours", 20), ("Weekly", 55), ("Monthly credits", 25)]),
        ("Z.ai Coding", [("Credits · 5 hours", 25), ("Credits · 7 days", 72)]),
        ("Kimi Coding", [("5 hours", 100), ("Monthly total", 62), ("Monthly coding", 18)])
    ];

    private Expander QuotaTile(string account) => Controls<Expander>().Single(x => AutomationProperties.GetName(x) == $"Account budgets for {account}");
    private static ToggleButton TileHeader(Expander tile) => tile.GetVisualDescendants().OfType<ToggleButton>()
        .Single(x => AutomationProperties.GetName(x) == AutomationProperties.GetName(tile)!.Replace("Account budgets for ", "Toggle account budgets for ", StringComparison.Ordinal));
    private async Task ExpandTile(Expander tile, bool expanded)
    {
        // Exercise the native Expander header's two-way toggle binding without an OS pointer event.
        var header = TileHeader(tile);
        Check(header.IsEnabled, $"{AutomationProperties.GetName(tile)} header is interactive");
        header.SetCurrentValue(ToggleButton.IsCheckedProperty, expanded);
        await Task.Delay(100);
        Check(tile.IsExpanded == expanded, $"{AutomationProperties.GetName(tile)} {(expanded ? "expands" : "closes")} through its header");
    }

    private async Task CheckClosedQuotaTiles(string page)
    {
        await Wait(() => Controls<Expander>().Count(x => AutomationProperties.GetName(x)?.StartsWith("Account budgets for ", StringComparison.Ordinal) == true) == Fixture.InitialAccounts,
            $"{page} displays one quota tile per account");
        await Wait(() => Controls<ProgressBar>().Length == ExpectedProviderWindows.Sum(x => x.Windows.Length),
            $"{page} native tile headers realize every quota bar");
        foreach (var (account, windows) in ExpectedProviderWindows)
        {
            var tile = QuotaTile(account);
            Check(!tile.IsExpanded, $"{page} {account} first-look tile is closed");
            foreach (var (label, percent) in windows)
            {
                CheckQuotaBar(account, label, percent);
                var observed = DateTimeOffset.Parse("2026-10-23T12:00:00Z");
                var resets = label.Contains("7 days", StringComparison.Ordinal) || label == "Weekly" ? observed.AddDays(7)
                    : label.StartsWith("Monthly", StringComparison.Ordinal) ? DateTimeOffset.Parse("2026-11-01T00:00:00Z") : observed.AddHours(5);
                var resetText = resets.ToLocalTime().ToString("dd MMM · HH:mm", System.Globalization.CultureInfo.CurrentCulture);
                var bar = tile.GetVisualDescendants().OfType<ProgressBar>().Single(x => AutomationProperties.GetName(x) == $"{account} · {label} · consumed");
                var header = (Control)tile.Header!;
                Check(AutomationProperties.GetHelpText(bar)?.Contains(resetText, StringComparison.Ordinal) == true
                    && header.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text?.Contains(resetText, StringComparison.Ordinal) == true),
                    $"{page} closed {account} {label} exposes its actual local reset");
            }
            Check(tile.GetVisualDescendants().OfType<ProgressBar>().Count() == windows.Length, $"{page} {account} closed tile includes every known percentage window exactly once");
        }
        var mimo = QuotaTile("MiMo API");
        Check(!mimo.IsExpanded && HasText("Not available", true), $"{page} explains unsupported MiMo quota in its closed tile");
        Check(!mimo.GetVisualDescendants().OfType<ProgressBar>().Any(), $"{page} unsupported provider has no invented progress");
    }

    private Task CheckQuotaOverview() => CheckClosedQuotaTiles("overview");

    private async Task CheckQuotaAccounts()
    {
        foreach (var (name, windows) in ExpectedProviderWindows)
        {
            var tile = QuotaTile(name);
            await ExpandTile(tile, true);
            foreach (var (label, percent) in windows) CheckQuotaBar(name, label, percent);
            if (name == "Command Code Pro")
            {
                Check(HasText("Last known"), "stale Command window is labelled as last known");
                var lastWindow = tile.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Text == "Model allowance" && IsShown(x));
                Check(!Controls<ProgressBar>().Any(x => AutomationProperties.GetName(x) == "Command Code Pro · Model allowance · consumed"), "unknown model allowance has no invented progress bar");
                await CaptureBoth("accounts-command", () => TileHeader(tile).BringIntoView());
                await CaptureBoth("accounts-command-last-window", () => lastWindow.BringIntoView());
                await ResizeViewport(800, 560);
                lastWindow.BringIntoView();
                await Task.Delay(100); window.UpdateLayout();
                Check(FitsScrollViewports(lastWindow), "sixth Command window is visibly reachable by ordinary page scroll at 800x560");
                await ResizeViewport(1120, 780);
            }
            if (name == "Z.ai Coding")
            {
                Check(HasText("750", true) && HasText("credits", true), "Z.ai exposes its numeric remaining credit balance");
                await CaptureBoth("accounts-zai", () => TileHeader(tile).BringIntoView());
                var technical = tile.GetVisualDescendants().OfType<Expander>().Single(x => Equals(x.Header, "Technical details"));
                technical.IsExpanded = true;
                await Task.Delay(120);
                var values = technical.GetVisualDescendants().OfType<SelectableTextBlock>().ToArray();
                Check(values.Any(x => x.Text?.Contains("https://api.z.ai/", StringComparison.Ordinal) == true)
                    && values.Any(x => x.Text?.Contains("observed", StringComparison.Ordinal) == true && x.Text.Contains("last success", StringComparison.Ordinal)), "expanded quota details expose source and snapshot freshness");
                var percentOnly = values.Single(x => x.Text == "Used: Not supplied · limit: Not supplied · remaining: Not supplied");
                Check(percentOnly is not null, "percentage-only Z.ai quota does not invent absolute counts");
                await CaptureBoth("accounts-zai-technical", () => percentOnly.BringIntoView());
                technical.IsExpanded = false;
            }
            if (name == "Kimi Coding")
            {
                Check(HasText("38% left"), "percentage-only Kimi window shows justified remaining percentage");
                await CaptureBoth("accounts-kimi", () => TileHeader(tile).BringIntoView());
            }
            await ExpandTile(tile, false);
        }
        var mimo = QuotaTile("MiMo API");
        await ExpandTile(mimo, true);
        await CaptureBoth("accounts-mimo", () => TileHeader(mimo).BringIntoView());
        Check(HasText("Not available", true), "expanded MiMo tile explains unsupported quota");
        Check(!mimo.GetVisualDescendants().OfType<ProgressBar>().Any(), "expanded unsupported account has no zero-percent quota bar");
        await ExpandTile(mimo, false);
        var personal = QuotaTile("Personal Codex");
        await ExpandTile(personal, true);
        TileHeader(personal).BringIntoView();
        await Task.Delay(120);
    }

    private bool FitsScrollViewports(Control control)
    {
        if (control.GetVisualAncestors().OfType<Control>().Any(parent => !parent.IsVisible)) return false;
        var origin = control.TranslatePoint(default, window);
        if (origin is null || control.Bounds.Width <= 0 || control.Bounds.Height <= 0) return false;
        var target = new Rect(origin.Value, control.Bounds.Size);
        bool Contains(Rect viewport) => target.Left >= viewport.Left - 1 && target.Top >= viewport.Top - 1
            && target.Right <= viewport.Right + 1 && target.Bottom <= viewport.Bottom + 1;
        if (!Contains(new Rect(window.Bounds.Size))) return false;
        return control.GetVisualAncestors().OfType<ScrollViewer>().All(scroller =>
        {
            var point = scroller.TranslatePoint(default, window);
            return point is not null && Contains(new Rect(point.Value, scroller.Bounds.Size));
        });
    }

    private void CheckQuotaBar(string account, string label, double percent)
    {
        var name = $"{account} · {label} · consumed";
        var bars = Controls<ProgressBar>().Where(x => AutomationProperties.GetName(x) == name).ToArray();
        Check(bars.Length == 1, $"one semantic quota progress label: {name}");
        Check(bars.All(bar => Math.Abs(bar.Minimum) < .001 && Math.Abs(bar.Maximum - 100) < .001 && Math.Abs(bar.Value - percent) < .001 && !bar.IsIndeterminate),
            $"{account} {label} progress is {percent}% consumed");
    }

    private void CheckBundledFonts()
    {
        foreach (var label in new[] { "Overview", "Provider budgets", "Personal Codex", "lain" })
        {
            var block = Controls<TextBlock>().Where(x => x.Text == label).OrderByDescending(x => x.FontSize).First();
            var found = FontManager.Current.TryGetGlyphTypeface(new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch), out var actual);
            Console.WriteLine($"FONT displayed {label}: requested={block.FontFamily}, source={block.FontFamily.Key}, resolved={found}, actual={actual?.FamilyName}, weight={actual?.Weight}");
        }
        foreach (var (uri, expected) in new[]
        {
            ("avares://Lain.Desktop/Assets/Fonts/IBMPlexSansCondensed#IBM Plex Sans Condensed", "IBM Plex Sans Condensed"),
            ("avares://Lain.Desktop/Assets/Fonts/Oxanium#Oxanium", "Oxanium")
        })
        foreach (var weight in new[] { FontWeight.Normal, FontWeight.SemiBold })
        {
            var found = FontManager.Current.TryGetGlyphTypeface(new Typeface(new FontFamily(uri), FontStyle.Normal, weight), out var glyph);
            Check(found && glyph?.FamilyName.StartsWith(expected, StringComparison.Ordinal) == true,
                $"bundled {expected} {weight} resolves to its real font (actual: {glyph?.FamilyName ?? "missing"})");
        }
    }

    private async Task Open()
    {
        var http = new HttpClient(fixture, disposeHandler: false) { BaseAddress = new Uri("http://fixture.invalid") };
        transports.Add(http);
        window = new MainWindow(new LainApiClient(http)) { Width = 1120, Height = 780 };
        lifetime.MainWindow = window; window.Show();
        await Wait(() => HasText("ready", true), "main window ready");
    }
    private static bool IsShown(Control control) => control.IsVisible && control.GetVisualAncestors().OfType<Control>().All(parent => parent.IsVisible);
    private T[] Controls<T>() where T : Control => window.GetVisualDescendants().OfType<T>().Where(IsShown).ToArray();
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
