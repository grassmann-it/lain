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
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lain.Client;
using Lain.Core;
using Lain.Desktop;
using Lain.Desktop.Usage;
using Lain.Desktop.Providers;
using Lain.Desktop.Presentation;

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
    private readonly List<object> tileLayouts = [];
    private readonly List<object> inspectorLayouts = [];
    private MainWindow window = null!;
    private readonly List<HttpClient> transports = [];

    public async Task Run()
    {
        Directory.CreateDirectory(Output);
        await Open();
        Check(new WorkspacePreferencesStore().Current.Level == InformationLevel.Simple, "fresh workspace defaults to Simple presentation");
        await CheckClosedQuotaTiles("accounts-initial");
        CheckPrimaryNavigation();
        CheckBundledFonts();
        if (Environment.GetEnvironmentVariable("LAIN_DESKTOP_FONT_PROBE_ONLY") == "1") { window.Close(); return; }
        await CaptureBoth("accounts-initial");
        await Click("Accounts");
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
        await Click("Reload status");
        await Wait(() => HasText("Personal Codex updated"), "explicit refresh displays changed configuration");
        await Click("Accounts"); await Click("Add account"); await Click("Sign in to Codex (browser)");
        await Wait(() => HasText("Waiting for authorization"), "login waiting");
        await CaptureBoth("login");
        await Wait(() => HasText("Codex account connected."), "login completion", 6000);
        Check(fixture.LoginPolls > 0, "login polled fake provider");
        await Click("Accounts"); await Click("Add account"); await Click("Sign in to Codex (browser)"); await Click("Cancel sign-in");
        Check(fixture.LoginCancels == 1, "login cancellation");

        await Click("Accounts"); await Click("Add account"); await Click("Manual API account");
        var accountName = Input("My API account");
        accountName.Focus();
        Check(accountName.IsFocused, "account input accepts keyboard focus");
        accountName.Text = "Command account";
        var revisionBeforePresentation = fixture.Config.Revision;
        var mutationCountBeforePresentation = MutatingRequestCount();
        await Click("Reload status");
        await SelectPresentation(InformationLevel.Advanced);
        Check(ReferenceEquals(accountName, Input("My API account")) && accountName.Text == "Command account" && accountName.IsFocused,
            "account form values control identity and focus survive reload and presentation changes");
        Check(fixture.Config.Revision == revisionBeforePresentation && MutatingRequestCount() == mutationCountBeforePresentation,
            "presentation changes never mutate host configuration or start provider operations");
        await SelectPresentation(InformationLevel.Simple);
        Input("e.g. command-code").Text = "command-code";
        Input("https://…/v1").Text = "https://example.invalid/v1";
        Input("API key").Text = "fake-test-key-never-a-secret";
        var draftMutations = MutatingRequestCount();
        fixture.Config = fixture.Config with { Revision = fixture.Config.Revision + 1 };
        await Click("Reload status");
        Check(Input("My API account").Text == "Command account" && Input("API key").Text == "fake-test-key-never-a-secret"
            && HasText("Configuration changed while this draft was open.", true),
            "external revision changes retain the account draft and visibly request review");
        await Click("Review latest configuration");
        Check(Input("My API account").Text == "Command account" && Input("e.g. command-code").Text == "command-code"
            && Input("https://…/v1").Text == "https://example.invalid/v1" && Input("API key").Text == "fake-test-key-never-a-secret"
            && Input("API key").PasswordChar == '●' && MutatingRequestCount() == draftMutations,
            "explicit latest-configuration review restores account fields and masked credentials without saving them");
        await CaptureBoth("account-draft-review");
        await Click("Add account");
        await Wait(() => fixture.Config.Accounts.Length == Fixture.InitialAccounts + 1, "account added");
        await Click("Disable"); Check(fixture.EnableCalls == 1, "account disabled");
        await Click("Accounts"); await Click("Add account"); await Click("Import setup"); Input("/path/to/config.yaml").Text = "/fixture/config.yaml";
        await Click("Import");
        Check(fixture.Imports == 1 && HasText("snapshot warning", true), "import and warning preserved");

        await CheckProviderCatalog();
        await CheckInspector();

        await Click("Models"); await CaptureBoth("routes"); await Click("Add model access");
        var suggestions = Controls<Expander>().Single(x => Equals(x.Header, "Known model suggestions (optional)"));
        suggestions.IsExpanded = true; await Task.Delay(80);
        suggestions.GetVisualDescendants().OfType<ComboBox>().Single().SelectedIndex = 1;
        Input("client-facing-model").Text = "test-route";
        await CheckEditorPreservation("client-facing-model", "test-route");
        Input("Upstream model ID").Text = "test-model";
        Input("Provider identifier").Text = "openai";
        Controls<CheckBox>().First(x => x.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("Personal", StringComparison.Ordinal) == true)).IsChecked = true;
        await CaptureBoth("route-editor"); await Click("Save model access");
        await Wait(() => fixture.Config.Routes.Length == Fixture.InitialRoutes + 1, "revisioned route saved");

        await CheckPolicyPreservation();

        await Click("Profiles");
        await CheckAppliedProfileAuthority();
        await Click("Create profile");
        Input("Profile name").Text = "Focus";
        await CheckEditorPreservation("Profile name", "Focus");
        Controls<CheckBox>().First().IsChecked = true;
        await CaptureBoth("profile-editor");
        await Click("Save profile");
        await Wait(() => fixture.Config.Profiles.Length == Fixture.InitialProfiles + 1, "revisioned profile saved");
        await CaptureBoth("profiles"); await Click("Apply to proxy…"); await Click("Apply to proxy");
        Check(fixture.ApplyCalls == 1, "profile applied");
        await Click("Set up Codex client…");
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
        await CheckUsageAnalysis();
        await CheckUsageDisclosurePreservation();
        var usageInput = Controls<UsageView>().Single().GetVisualDescendants().OfType<TextBox>().First();
        usageInput.Focus();
        Check(usageInput.IsFocused, "usage input accepts keyboard focus");
        usageInput.Text = "2026-10-20";
        var unsubmittedFilter = usageInput.Text;
        var appliedFromBeforePoll = fixture.UsageQueries.Last()["from"];
        var pollStart = fixture.UsageCalls;
        await Task.Delay(5300);
        Check(fixture.UsageCalls > pollStart && UsageConnected(), "usage refreshes after five seconds");
        Check(usageInput.IsFocused && usageInput.Text == unsubmittedFilter && fixture.UsageQueries.Last()["from"] == appliedFromBeforePoll,
            "usage input focus and unapplied date draft survive polling without silently applying the draft");
        Check(Controls<Expander>().Any(b => AutomationProperties.GetName(b) == "Analysis group modelFamily fixture-family" && b.IsExpanded),
            "selected usage breakdown detail expansion survives polling");
        await Click("Details for attempt attempt-fixture");
        var details = Controls<Button>().Single(b => AutomationProperties.GetName(b) == "Details for attempt attempt-fixture");
        details.Focus();
        Check(details.IsFocused, "attempt details accepts keyboard focus");
        var attemptPollStart = fixture.UsageCalls;
        await Task.Delay(5300);
        Check(fixture.UsageCalls > attemptPollStart && Controls<Button>().Any(b => AutomationProperties.GetName(b) == "Details for attempt attempt-fixture" && b.IsFocused), "attempt detail focus survives polling");
        var accountScopeBeforeLink = fixture.UsageQueries.Last().GetValueOrDefault("accountId");
        var appliedStatusBeforeLink = fixture.UsageQueries.Last().GetValueOrDefault("statusCode");
        Named<TextBox>("HTTP status").Text = "503";
        await Click("Open account for attempt attempt-fixture");
        await Wait(() => Controls<Expander>().Any(x => AutomationProperties.GetName(x) == "Account budgets for Personal Codex updated"), "usage attempt links to its configured account");
        await Click("Back");
        await Wait(UsageConnected, "Back restores loaded Usage");
        Check(fixture.UsageQueries.Last().GetValueOrDefault("accountId") == accountScopeBeforeLink
            && fixture.UsageQueries.Last().GetValueOrDefault("statusCode") == appliedStatusBeforeLink
            && Named<TextBox>("HTTP status").Text == "503" && Named<TextBox>("From · inclusive").Text == unsubmittedFilter,
            "Usage Back restores the applied query separately from unapplied status and date drafts");
        await Click("Open model for attempt attempt-fixture");
        await Wait(() => Controls<Expander>().Any(x => AutomationProperties.GetName(x) == "Details for model access gpt-lain-01"),
            "Usage attempt links to its evidenced configured model access");
        await Click("Back"); await Wait(UsageConnected, "model link Back restores Usage");
        Check(Named<TextBox>("HTTP status").Text == "503" && fixture.UsageQueries.Last().GetValueOrDefault("statusCode") == appliedStatusBeforeLink,
            "Usage model navigation also preserves applied scope separately from draft filters");
        Named<TextBox>("HTTP status").Text = "";
        var daily = Controls<Expander>().Single(x => Equals(x.Header, "Daily totals"));
        daily.IsExpanded = true;
        await CaptureBoth("usage-details");
        fixture.NullTokens = true;
        await ClickUsageApply();
        Check(Controls<UsageView>().Single().GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "—"), "null tokens render unknown");
        Check(HasText("0 / 132 measured") && HasText("P50 —"), "missing output usage has zero measured samples and no invented rate median");
        await CaptureBoth("usage-null");
        fixture.UsageFailure = true; await ClickUsageApply();
        await Wait(() => HasText("Usage could not be refreshed:", true), "usage API error visible");
        await CaptureBoth("usage-error");
        fixture.UsageFailure = false; fixture.NullTokens = false;
        await ClickUsageApply(); await Wait(UsageConnected, "usage recovers");
        await Click("Local service");
        Check(new[] { "Start proxy", "Restart…", "Stop…" }.All(label => Controls<Button>().Any(x => Equals(x.Content, label))),
            "global Local service keeps all existing proxy actions reachable");
        await CaptureBoth("local-service");
        var detachedCalls = fixture.UsageCalls;
        await Task.Delay(5400);
        Check(fixture.UsageCalls == detachedCalls, "usage polling stops when detached");

        fixture.Offline = true; await Click("Reload status");
        await Wait(() => HasText("Local host unavailable", true), "offline host state visible");
        await CaptureBoth("offline");
        fixture.Offline = false; await Click("Reload status");
        await Click("Usage"); await Wait(UsageConnected, "usage reattaches");
        window.Close();
        var closedCalls = fixture.UsageCalls;
        await Task.Delay(5400);
        Check(!window.IsVisible && fixture.UsageCalls == closedCalls, "window close stops usage polling");
        await Open(); await Click("Usage"); await Wait(UsageConnected, "reopened window gets usage");
        await CaptureBoth("reopened-usage");
        await CheckPresentationAndInventoryPersistence();
        window.Close();
        Check(fixture.Unexpected.Count == 0, "all requests matched fixture allowlist");
        foreach (var transport in transports) transport.Dispose();
        var assembly = typeof(MainWindow).Assembly;
        await File.WriteAllTextAsync(Path.Combine(Output, "evidence.json"), JsonSerializer.Serialize(new
        {
            result = "PASS", display = Environment.GetEnvironmentVariable("DISPLAY"), privateXvfb = true,
            assembly = assembly.Location, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))),
            checks, screenshots, tileLayouts, inspectorLayouts, requests = fixture.Requests,
            scope = "Production MainWindow, UsageView, ProviderCatalogView, InspectorView and App theme initialization; in-memory HTTP only. Button Click routed events exercise handlers, not OS pointer hit-testing. No real credentials, host, authorization browser, inference or service mutation."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS desktop smoke: {checks.Count} checks, {screenshots.Count} screenshots. {Output}/evidence.json");
    }

    private int MutatingRequestCount() => fixture.Requests.Count(x => !x.StartsWith("GET ", StringComparison.Ordinal));

    private void CheckPrimaryNavigation()
    {
        var expected = new[] { "Accounts", "Models", "Profiles", "Usage", "Live" };
        var navigation = Controls<Button>().Where(b => b.Tag is string tag && expected.Contains(tag))
            .Select(AutomationProperties.GetName).ToArray();
        Check(navigation.SequenceEqual(expected), "exactly five primary destinations remain directly reachable in the agreed order");
        Check(!Controls<Button>().Any(b => b.Tag is "Overview" or "Routes" or "Providers" or "Inspector" or "Setup"),
            "provider discovery and help are contextual rather than duplicate primary inventories");
        Check(Controls<Expander>().Any(x => AutomationProperties.GetName(x)?.StartsWith("Account budgets for ", StringComparison.Ordinal) == true),
            "Accounts is the initial workspace");
        Check(window.Icon is not null && Controls<Image>().Any(x => x.Source is not null), "application window and shell show bundled cat branding");
    }

    private async Task SelectPresentation(InformationLevel level, ViewDensity? density = null)
    {
        if (!Controls<ComboBox>().Any(x => AutomationProperties.GetName(x) == "Information level")) await Click("View");
        var mode = Named<ComboBox>("Information level");
        mode.SelectedIndex = (int)level;
        await Task.Delay(100);
        if (density is { } value)
        {
            if (!Controls<ComboBox>().Any(x => AutomationProperties.GetName(x) == "View density")) await Click("View");
            Named<ComboBox>("View density").SelectedIndex = (int)value;
            await Task.Delay(100);
        }
        // The View panel is a toggle; leave the workspace ready for ordinary interaction.
        if (Controls<ComboBox>().Any(x => AutomationProperties.GetName(x) == "Information level")) await Click("View");
    }

    private async Task CheckEditorPreservation(string placeholder, string value)
    {
        var input = Input(placeholder);
        input.Focus();
        var config = JsonSerializer.Serialize(fixture.Config);
        var mutations = MutatingRequestCount();
        await Click("Reload status");
        await SelectPresentation(InformationLevel.Barebones, ViewDensity.Compact);
        Check(ReferenceEquals(input, Input(placeholder)) && input.Text == value && input.IsFocused,
            $"{placeholder} draft and focused control survive status reload and mode/density changes");
        Check(JsonSerializer.Serialize(fixture.Config) == config && MutatingRequestCount() == mutations,
            $"{placeholder} view changes neither save configuration nor apply a profile");
        await SelectPresentation(InformationLevel.Simple, ViewDensity.Normal);
    }

    private async Task SelectUsageBreakdown(string label, string dimension, string key)
    {
        var compare = Named<ComboBox>("Compare by");
        var choices = compare.Items.Cast<object>().ToArray();
        compare.SelectedIndex = Array.FindIndex(choices, x => x.ToString() == label);
        Check(compare.SelectedIndex >= 0, $"usage offers {label} in its single breakdown selector");
        await Wait(() => Controls<Expander>().Any(x => AutomationProperties.GetName(x) == $"Analysis group {dimension} {key}"),
            $"usage loads the {label} breakdown");
        Check(Controls<ComboBox>().Count(x => AutomationProperties.GetName(x) == "Compare by") == 1,
            "usage exposes one main configurable breakdown");
        Check(!Controls<Button>().Any(x => AutomationProperties.GetName(x) is "Details for account a1" or "Details for model gpt-model"),
            "usage no longer repeats account/model summary tables alongside the selected breakdown");
    }

    private async Task CheckPresentationAndInventoryPersistence()
    {
        await Click("Accounts");
        await SelectPresentation(InformationLevel.Simple, ViewDensity.Normal);
        var store = new WorkspacePreferencesStore();
        Check(store.Current.Level == InformationLevel.Simple, "Simple presentation is the persisted working default");
        var config = JsonSerializer.Serialize(fixture.Config);
        var mutations = MutatingRequestCount();
        foreach (var level in Enum.GetValues<InformationLevel>())
        {
            await SelectPresentation(level);
            CheckPrimaryNavigation();
            await CaptureBoth($"accounts-{level.ToString().ToLowerInvariant()}-normal");
            var commandHeader = (Control)QuotaTile("Command Code Pro").Header!;
            Check(new[] { "Purchased credits", "Free credits", "Model allowance" }.All(label => commandHeader.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == label)),
                $"{level} preserves mixed percentage remaining-only and unknown windows in the closed inventory");
            foreach (var (account, windows) in ExpectedProviderWindows)
                foreach (var (label, percent) in windows)
                {
                    var name = account == "Personal Codex" ? "Personal Codex updated" : account;
                    CheckQuotaBar(name, label, percent);
                    Check(((Control)QuotaTile(name).Header!).GetVisualDescendants().OfType<TextBlock>()
                        .Any(t => t.Text == percent.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) + "% used"),
                        $"{level} preserves {name} {label} consumed percentage text");
                }
        }
        Check(JsonSerializer.Serialize(fixture.Config) == config && MutatingRequestCount() == mutations,
            "all three presentation presets preserve host configuration and never invoke provider mutations");
        foreach (var section in new[] { "Models", "Profiles", "Usage", "Live" })
        {
            await Click(section);
            await SelectPresentation(InformationLevel.Advanced, ViewDensity.Normal);
            if (section == "Usage") await Wait(UsageConnected, "Advanced Usage retains its applied range");
            if (section == "Live") await Wait(() => HasSelectable("New source output only."), "Advanced Live retains readable output");
            await CaptureBoth(section.ToLowerInvariant() + "-advanced-normal");
        }
        await Click("Accounts");
        await SelectPresentation(InformationLevel.Advanced, ViewDensity.Compact);
        await ExpandTile(QuotaTile("Kimi Coding"), true);
        await Click("Pin account Kimi Coding");
        await ExpandTile(QuotaTile("Command Code Pro"), true);
        await Click("Pin account Command Code Pro");
        await Click("Move pinned account Command Code Pro earlier");
        await OpenAccountOptions();
        var grouping = Named<CheckBox>("Group accounts by provider");
        grouping.IsChecked = true;
        await Task.Delay(120);
        var saved = new WorkspacePreferencesStore().Current;
        Check(saved.PinnedAccountIds.Take(2).SequenceEqual(new[] { "a2", "a4" }) && saved.GroupAccountsByProvider,
            "pin actions reorder favorites and persist explicit grouping independently of quota values");
        var search = Named<TextBox>("Search accounts");
        search.Text = "Kimi";
        Named<ComboBox>("Filter accounts by state").SelectedIndex = 1;
        await Task.Delay(100);
        var filteredTiles = Controls<Expander>().Where(x => AutomationProperties.GetName(x)?.StartsWith("Account budgets for ", StringComparison.Ordinal) == true).ToArray();
        Check(filteredTiles.Length == 1 && AutomationProperties.GetName(filteredTiles[0]) == "Account budgets for Kimi Coding",
            "account search and explicit enabled filter combine without changing account enablement");
        await SelectPresentation(InformationLevel.Barebones);
        await Click("Reload status"); await Click("Models"); await Click("Accounts");
        Check(Named<TextBox>("Search accounts").Text == "Kimi" && Named<ComboBox>("Filter accounts by state").SelectedIndex == 1,
            "search and status filters survive presentation reload and workspace navigation");
        await ExpandTile(QuotaTile("Kimi Coding"), true);
        await Click("Recorded usage");
        await Wait(() => UsageConnected() && fixture.UsageQueries.Last().GetValueOrDefault("accountId") == "a4", "account drillthrough loads Usage with its stable account ID");
        await Click("Back");
        Check(Named<TextBox>("Search accounts").Text == "Kimi" && QuotaTile("Kimi Coding").IsExpanded,
            "contextual Back restores account search and expanded source tile");
        await Click("Models for this account");
        await Wait(() => Controls<Expander>().Any(x => AutomationProperties.GetName(x)?.StartsWith("Details for model access ", StringComparison.Ordinal) == true),
            "account drillthrough reaches the configured model-access inventory");
        await Click("Back");
        Check(Named<TextBox>("Search accounts").Text == "Kimi", "model drillthrough Back restores account filter context");
        await Click("Clear filters");
        Named<ComboBox>("Account order").SelectedIndex = 2;
        await Task.Delay(80);
        Check(new WorkspacePreferencesStore().Current.AccountSort == "name", "explicit account sort is stored independently from presentation");
        var beforePollOrder = AccountTileNames();
        fixture.QuotaAvailabilityChanged = true;
        await Click("Reload status");
        Check(AccountTileNames().SequenceEqual(beforePollOrder), "polling refresh preserves explicit pinned/provider/name account order");
        await Click("Models");
        await SelectPresentation(InformationLevel.Barebones, ViewDensity.Normal);
        await CaptureBoth("models-barebones-normal");
        await Click("Profiles"); await CaptureBoth("profiles-barebones-normal");
        await Click("Usage"); await Wait(UsageConnected, "usage survives compact presentation navigation");
        await CaptureBoth("usage-barebones-normal");
        await Click("Live"); await Wait(() => HasSelectable("New source output only."), "live survives presentation navigation");
        await CaptureBoth("live-barebones-normal");
        Check(JsonSerializer.Serialize(fixture.Config) == config && MutatingRequestCount() == mutations,
            "pins order grouping density and context navigation never mutate the host configuration");
        window.Close(); await Open();
        var restarted = new WorkspacePreferencesStore().Current;
        Check(restarted.Level == InformationLevel.Barebones && restarted.Densities.GetValueOrDefault("Accounts") == ViewDensity.Compact
            && restarted.Densities.GetValueOrDefault("Models") == ViewDensity.Normal,
            "information level and independent per-section densities survive a new window");
        Check(restarted.PinnedAccountIds.Take(2).SequenceEqual(new[] { "a2", "a4" }) && restarted.GroupAccountsByProvider && restarted.AccountSort == "name",
            "pins pin order explicit sort and grouping survive a new window");
        await OpenAccountOptions();
        Named<ComboBox>("Account order").SelectedIndex = 0;
        Check(Named<TextBox>("Search accounts").Text is null or "", "fresh window reveals accounts without a stale search filter");
        fixture.AddDensityAccounts(35);
        await Click("Reload status");
        await Wait(() => AccountTileNames().Length >= 35, "synthetic inventory exercises at least 35 accounts");
        await CaptureBoth("accounts-35-compact", verify: CheckAllAccountTileBounds);
        await SelectPresentation(InformationLevel.Simple, ViewDensity.Normal);
        await CaptureBoth("accounts-35-simple-normal", verify: CheckAllAccountTileBounds);
        Check(fixture.Config.Accounts.Count(x => !x.Enabled) > 0 && AccountTileNames().Contains("Account budgets for Density account 30"),
            "disabled accounts remain included by default in a large inventory");
        var savedInventory = fixture.Config;
        fixture.Config = fixture.Config with { Accounts = [] };
        await Click("Reload status");
        Check(Controls<Button>().Any(x => Equals(x.Content, "Discover providers")), "provider discovery is reachable in Accounts without credentials");
        await CaptureBoth("accounts-empty-onboarding");
        await Click("Discover providers");
        await Wait(() => HasText("NVIDIA Build"), "credential-free discovery uses the same provider catalog");
        fixture.Config = savedInventory;
        await Click("Accounts"); await Click("Reload status");
    }

    private async Task OpenAccountOptions()
    {
        if (Controls<ComboBox>().Any(x => AutomationProperties.GetName(x) == "Account order")) return;
        var options = Controls<ToggleButton>().SingleOrDefault(x => AutomationProperties.GetName(x) == "Account filters and order");
        if (options is not null) options.IsChecked = true;
        else Controls<Expander>().Single(x => Equals(x.Header, "Filters and order")).IsExpanded = true;
        await Task.Delay(100);
    }

    private string[] AccountTileNames() => Controls<Expander>().Select(AutomationProperties.GetName)
        .Where(x => x?.StartsWith("Account budgets for ", StringComparison.Ordinal) == true).Cast<string>().ToArray();

    private void CheckAllAccountTileBounds()
    {
        var tiles = Controls<Expander>().Where(x => AutomationProperties.GetName(x)?.StartsWith("Account budgets for ", StringComparison.Ordinal) == true)
            .Select(x => new Rect(x.TranslatePoint(default, window)!.Value, x.Bounds.Size)).ToArray();
        Check(tiles.Length >= 35 && tiles.All(x => x.Width >= 200 && x.Height > 0), "large inventory retains readable realized account tiles");
        Check(!tiles.SelectMany((x, i) => tiles.Skip(i + 1).Select(y => (x, y)))
            .Any(p => Math.Min(p.x.Right, p.y.Right) - Math.Max(p.x.Left, p.y.Left) > 1 && Math.Min(p.x.Bottom, p.y.Bottom) - Math.Max(p.x.Top, p.y.Top) > 1),
            "large inventory account tiles never overlap");
    }

    private async Task CheckAppliedProfileAuthority()
    {
        var before = fixture.ApplyCalls;
        Check(HasText("Applied to proxy: Workspace 2", true), "applied profile identity comes from Host status rather than saved or selected profile");
        await Click("Select profile Daily");
        Check(HasText("Selected profile: Daily") && HasText("Applied to proxy: Workspace 2", true) && fixture.ApplyCalls == before,
            "selecting a saved profile leaves the authoritative applied profile and global proxy unchanged");
        fixture.ActiveProfileId = null; fixture.ActiveProfileRevision = null;
        await Click("Reload status");
        Check(HasText("Applied profile not reported by the host.", true) && !HasText("Applied to proxy: Daily", true),
            "missing applied metadata remains unknown instead of being inferred from selection");
        fixture.ActiveProfileId = "p2"; fixture.ActiveProfileRevision = 1;
        await Click("Reload status");
    }

    private async Task CheckUsageDisclosurePreservation()
    {
        string[] headings = ["Filters & range", "More filters", "Source & freshness", "Token details",
            "Performance · whole filtered range", "Daily totals", "Attempt history & errors"];
        static Expander[] Find(UsageView view, string[] names) => names.Select(name => view.GetLogicalDescendants().OfType<Expander>()
            .Single(x => Equals(x.Header, name))).ToArray();
        var disclosures = Find(Controls<UsageView>().Single(), headings);
        var original = disclosures.Select(x => x.IsExpanded).ToArray();
        foreach (var disclosure in disclosures) disclosure.IsExpanded = true;
        Named<ComboBox>("Compare by").Focus();
        await SelectPresentation(InformationLevel.Barebones);
        Check(disclosures.All(x => x.IsExpanded), "Barebones preserves explicitly opened Usage details with focus outside them");
        foreach (var disclosure in disclosures) disclosure.IsExpanded = false;
        Named<ComboBox>("Compare by").Focus();
        await SelectPresentation(InformationLevel.Advanced);
        Check(disclosures.All(x => !x.IsExpanded), "Advanced preserves explicitly closed Usage details with focus outside them");
        await SelectPresentation(InformationLevel.Simple);
        Check(disclosures.All(x => !x.IsExpanded), "Simple also preserves explicit Usage disclosure choices");
        foreach (var (disclosure, expanded) in disclosures.Zip(original)) disclosure.IsExpanded = expanded;
        using var isolatedHttp = new HttpClient(fixture, disposeHandler: false) { BaseAddress = new Uri("http://fixture.invalid") };
        var untouched = new UsageView(new LainApiClient(isolatedHttp));
        untouched.SetPresentation(new(InformationLevel.Advanced));
        Check(Find(untouched, headings).All(x => x.IsExpanded), "untouched Usage details follow the Advanced preset defaults");
        untouched.SetPresentation(new(InformationLevel.Barebones));
        Check(Find(untouched, headings).All(x => !x.IsExpanded), "untouched Usage details follow the Barebones preset defaults");
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
        await SelectUsageBreakdown("Account", "accountId", "Personal Codex updated");
        Named<Expander>("Analysis group accountId Personal Codex updated").IsExpanded = true;
        await Task.Delay(80);
        var errorRate = (400d / 132).ToString("N1", System.Globalization.CultureInfo.CurrentCulture) + "% error rate";
        Check(HasText(errorRate, true) && HasText("Tokens:", true), "account and model details show token fields and errors per attempt");
        await Click("Filter to account a1");
        Check(fixture.UsageQueries.Last().GetValueOrDefault("accountId") == "a1", "account drilldown applies its stable account ID");
        await SelectUsageBreakdown("Actual model", "model", "gpt-model");
        Named<Expander>("Analysis group model gpt-model").IsExpanded = true;
        await Task.Delay(80);
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
        await SelectUsageBreakdown("Model family", "modelFamily", "fixture-family");
    }

    private async Task CheckUsageAnalysis()
    {
        T Named<T>(string name) where T : Control => Controls<T>().Single(x => AutomationProperties.GetName(x) == name);
        await Wait(() => Controls<StackPanel>().Any(x => AutomationProperties.GetName(x) == "Usage performance"), "whole-range usage performance is available");
        var performance = Named<StackPanel>("Usage performance");
        var texts = performance.GetVisualDescendants().OfType<TextBlock>().Select(x => x.Text).ToArray();
        Check(new[] { "120 / 132 measured", "129 / 132 measured", "100 / 132 measured" }.All(texts.Contains),
            "performance exposes measured and eligible counts from the whole range rather than the one-attempt page");
        Check(texts.Contains("P50 240 ms") && texts.Contains("P95 510 ms")
            && texts.Contains("P50 " + 10d.ToString("N1", System.Globalization.CultureInfo.CurrentCulture) + " tok/s"),
            "performance exposes the host's measured percentiles and rate basis");
        Check(HasText("P50/P95 use all matching stored measurements", true) && HasText("Costs are not reported by this source.", true),
            "performance defines its population and does not invent cost estimates");
        var known = Named<Expander>("Analysis group modelFamily fixture-family");
        known.IsExpanded = true;
        await Task.Delay(100);
        Check(known.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text?.Contains("80 of 88 eligible measured", StringComparison.Ordinal) == true),
            "expanded model family group retains its own measurement denominator");
        var unknown = Named<Expander>("Analysis group modelFamily Unknown");
        unknown.IsExpanded = true;
        await Task.Delay(100);
        Check(!unknown.GetVisualDescendants().OfType<Button>().Any(x => AutomationProperties.GetName(x)?.StartsWith("Filter ", StringComparison.Ordinal) == true),
            "unknown group is not given an invented filter identity");
        var order = Named<ComboBox>("Group order");
        var reads = fixture.AnalysisCalls;
        order.SelectedIndex = 1;
        Check(fixture.AnalysisCalls == reads
            && Controls<Expander>().First(x => AutomationProperties.GetName(x)?.StartsWith("Analysis group modelFamily ", StringComparison.Ordinal) == true) is { } first
            && AutomationProperties.GetName(first) == "Analysis group modelFamily Unknown",
            "least-used ordering reorders loaded groups without a new analysis request");
        order.SelectedIndex = 0;
        Check(new[] { "a2", "a3", "a4", "a5", "a-new" }.All(id => Controls<Button>().Any(button => AutomationProperties.GetName(button) == $"Inspect inactive account {id}")),
            "configured accounts with no matching observed activity remain discoverable");

        await CaptureBoth("usage-analysis", () => Named<StackPanel>("Usage performance").BringIntoView());
        var compare = Named<ComboBox>("Compare by");
        compare.SelectedIndex = 2;
        await Wait(() => Controls<Expander>().Any(x => AutomationProperties.GetName(x) == "Analysis group manufacturer fixture-manufacturer"), "usage can compare manufacturer separately from route and inference provider");
        var manufacturer = Named<Expander>("Analysis group manufacturer fixture-manufacturer");
        manufacturer.IsExpanded = true;
        await Task.Delay(100);
        await Click("Filter manufacturer fixture-manufacturer");
        Check(fixture.UsageQueries.Last().GetValueOrDefault("manufacturer") == "fixture-manufacturer"
            && HasText("manufacturer: fixture-manufacturer", true), "group drilldown applies its evidenced dimension and shows the loaded scope");

        fixture.IncompleteCoverage = true;
        await ClickUsageApply();
        Check(HasText("Configured accounts without matching observed activity · coverage is incomplete", true),
            "unobserved accounts are qualified when historical coverage is incomplete");
        await CaptureBoth("usage-unobserved", () => Named<Button>("Inspect inactive account a2").BringIntoView());
        fixture.IncompleteCoverage = false;
        fixture.AnalysisFailure = true;
        await ClickUsageApply();
        Check(UsageConnected() && HasText("Analysis unavailable", true) && HasText("showing the last analysis snapshot", true),
            "analysis failure leaves usage connected and labels the retained snapshot");
        fixture.AnalysisFailure = false;
        var more = Controls<Expander>().Single(x => Equals(x.Header, "More filters"));
        more.IsExpanded = true;
        await Task.Delay(80);
        Named<TextBox>("Manufacturer").Text = "";
        await Click("Last 7 days");
        more.IsExpanded = false;
        compare.SelectedIndex = 0;
        await Wait(() => Controls<Expander>().Any(x => AutomationProperties.GetName(x) == "Analysis group modelFamily fixture-family"), "usage analysis recovers its initial grouping");
        Named<Expander>("Analysis group modelFamily fixture-family").IsExpanded = true;
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
        var commandHeader = (Control)QuotaTile("Command Code Pro").Header!;
        var namedWindows = commandHeader.GetVisualDescendants().OfType<TextBlock>().Select(x => x.Text).ToArray();
        Check(new[] { "Purchased credits", "Free credits", "Model allowance" }.All(namedWindows.Contains),
            $"{page} closed tile exposes pure remaining balances and unknown windows alongside percentages");
        Check(commandHeader.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text?.Contains("12", StringComparison.Ordinal) == true),
            $"{page} closed purchased-credit window includes its remaining balance");
        var mimo = QuotaTile("MiMo API");
        Check(!mimo.IsExpanded && HasText("Not available", true), $"{page} explains unsupported MiMo quota in its closed tile");
        Check(!mimo.GetVisualDescendants().OfType<ProgressBar>().Any(), $"{page} unsupported provider has no invented progress");
        foreach (var (width, height) in new[] { (1120, 780), (800, 560) })
        {
            await ResizeViewport(width, height);
            CheckCompactTileLayout(page);
        }
        await ResizeViewport(1120, 780);
    }


    private async Task CheckQuotaAccounts()
    {
        foreach (var (name, windows) in ExpectedProviderWindows)
        {
            var tile = QuotaTile(name);
            await ExpandTile(tile, true);
            CheckTileBoundsDoNotOverlap($"expanded {name}");
            foreach (var (label, percent) in windows) CheckQuotaBar(name, label, percent);
            if (name == "Command Code Pro")
            {
                var weeklyLabel = ((Control)tile.Header!).GetVisualDescendants().OfType<TextBlock>().Single(x => x.Text == "Weekly");
                var weeklyWindow = weeklyLabel.GetVisualAncestors().OfType<StackPanel>().First();
                Check(weeklyWindow.GetVisualDescendants().OfType<TextBlock>().Any(x => IsShown(x)
                    && x.Text?.StartsWith("Last known · Checked ", StringComparison.Ordinal) == true),
                    "stale Command weekly window visibly retains last-known status and observation provenance in its header");
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
                var percentOnly = values.SingleOrDefault(x => x.Text == "Used: Not supplied · limit: Not supplied · remaining: Not supplied")
                    ?? throw new Exception("Percentage-only Z.ai quota must not invent absolute counts");
                Check(percentOnly.Text == "Used: Not supplied · limit: Not supplied · remaining: Not supplied", "percentage-only Z.ai quota does not invent absolute counts");
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

    private Rect TileBounds(string account)
    {
        var tile = QuotaTile(account);
        var point = tile.TranslatePoint(default, window) ?? throw new Exception($"Tile is not attached: {account}");
        return new Rect(point, tile.Bounds.Size);
    }

    private void CheckCompactTileLayout(string page)
    {
        var tiles = ExpectedProviderWindows.Select(x => TileBounds(x.Account)).Append(TileBounds("MiMo API")).ToArray();
        var columns = tiles.GroupBy(x => Math.Round(x.Left)).OrderBy(x => x.Key).ToArray();
        Check(columns.Length == (window.Bounds.Width < 900 ? 1 : 2), $"{page} uses readable responsive quota columns at {window.Bounds.Width:0}px");
        foreach (var column in columns)
        {
            var ordered = column.OrderBy(x => x.Top).ToArray();
            Check(ordered.Zip(ordered.Skip(1)).All(pair => pair.Second.Top >= pair.First.Bottom + 7),
                $"{page} cards have independent nonoverlapping column spacing at {window.Bounds.Width:0}px");
        }
        tileLayouts.Add(new { page, width = window.Bounds.Width, height = window.Bounds.Height,
            tiles = tiles.Select(x => new { x.X, x.Y, x.Width, x.Height }).ToArray() });
        CheckTileBoundsDoNotOverlap($"{page} closed {window.Bounds.Width:0}px");
    }

    private void CheckTileBoundsDoNotOverlap(string context)
    {
        var names = ExpectedProviderWindows.Select(row => row.Account).Append("MiMo API").ToArray();
        var tiles = names.Select(name => (Name: name, Bounds: TileBounds(name))).ToArray();
        Check(tiles.All(tile => tile.Bounds.Width > 0 && tile.Bounds.Height > 0), $"{context} quota tiles retain positive bounds");
        var overlapping = tiles.SelectMany((a, index) => tiles.Skip(index + 1).Select(b => (a, b)))
            .Where(pair => Math.Min(pair.a.Bounds.Right, pair.b.Bounds.Right) - Math.Max(pair.a.Bounds.Left, pair.b.Bounds.Left) > 1
                && Math.Min(pair.a.Bounds.Bottom, pair.b.Bounds.Bottom) - Math.Max(pair.a.Bounds.Top, pair.b.Bounds.Top) > 1)
            .Select(pair => $"{pair.a.Name} / {pair.b.Name}").ToArray();
        Check(overlapping.Length == 0, $"{context} quota tile bounds do not overlap ({string.Join(", ", overlapping)})");
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


    private T Named<T>(string name) where T : Control => Controls<T>().Single(x => AutomationProperties.GetName(x) == name);
    private bool HasSelectable(string text) => Controls<SelectableTextBlock>().Any(block => block.Text?.Contains(text, StringComparison.Ordinal) == true);
    private bool HasGroup(string key) => Controls<Button>().Any(button => AutomationProperties.GetName(button) == "Inspect group " + key);


    private async Task OpenModelDetails(string alias)
    {
        Named<Expander>("Details for model access " + alias).IsExpanded = true;
        await Task.Delay(80);
    }

    private async Task CheckPolicyPreservation()
    {
        var original = fixture.Config.Routes.Single(route => route.Id == "r1");
        await OpenModelDetails(original.Alias);
        await Click("Edit model access " + original.Alias);
        Input("client-facing-model").Text = "policy-preservation-fixture";
        await Click("Save model access");
        await Wait(() => fixture.Config.Routes.Single(route => route.Id == "r1").Alias == "policy-preservation-fixture", "existing route rename saves through the production editor");
        var renamed = fixture.Config.Routes.Single(route => route.Id == "r1");
        Check(renamed.ProviderRouteId == original.ProviderRouteId && JsonSerializer.Serialize(renamed.Policy) == JsonSerializer.Serialize(original.Policy),
            "unrelated route edits preserve concrete catalog provenance fallback targets and paid fallback consent");
        await OpenModelDetails(renamed.Alias);
        await Click("Edit model access " + renamed.Alias);
        Input("client-facing-model").Text = original.Alias;
        Named<ComboBox>("Spending policy").SelectedIndex = 1;
        await Click("Check spending policy");
        await Wait(() => HasText("This route has no qualified runtime spending controls", true), "policy prerequisite check explains missing runtime cost controls");
        Check(!HasText("Configuration is eligible for guarded free-only routing", true) && fixture.CostPolicyCalls == 1,
            "a failed policy check cannot claim a fulfilled spending cap or live access");
        await CaptureBoth("route-spending-policy");
        await Click("Save model access");
        await Wait(() => fixture.Config.Routes.Single(route => route.Id == "r1").Policy?.CostPolicy == "free_only", "explicit free-only route selection is saved");
        var strictRoute = fixture.Config.Routes.Single(route => route.Id == "r1");
        Check(strictRoute.ProviderRouteId == original.ProviderRouteId && strictRoute.Policy?.AllowPaidFallback == false
            && strictRoute.Policy.FallbackRouteIds!.SequenceEqual(original.Policy!.FallbackRouteIds!),
            "explicit strict route mode clears paid opt-in and retains fallback targets for Host validation");

        await Click("Profiles");
        var profile = fixture.Config.Profiles.Single(item => item.Id == "p1");
        await Click($"Select profile {profile.Name}");
        await Click("Edit selection");
        Input("Profile name").Text = "Profile policy preservation fixture";
        await Click("Save profile");
        await Wait(() => fixture.Config.Profiles.Single(item => item.Id == "p1").Name == "Profile policy preservation fixture", "existing profile rename saves through the production editor");
        Check(JsonSerializer.Serialize(fixture.Config.Profiles.Single(item => item.Id == "p1").Policy) == JsonSerializer.Serialize(profile.Policy),
            "unrelated profile edits retain fallback policy and explicit consent");
        await Click("Edit selection");
        Input("Profile name").Text = profile.Name;
        Named<ComboBox>("Spending policy").SelectedIndex = 1;
        await Click("Save profile");
        await Wait(() => fixture.Config.Profiles.Single(item => item.Id == "p1").Policy?.CostPolicy == "free_only", "explicit strict profile selection is saved");
        var strictProfile = fixture.Config.Profiles.Single(item => item.Id == "p1");
        Check(strictProfile.Policy?.AllowPaidFallback == false && strictProfile.Policy.FallbackRouteIds!.SequenceEqual(profile.Policy!.FallbackRouteIds!),
            "strict profile choice clears paid opt-in while preserving fallback targets");
    }

    private async Task CheckProviderCatalog()
    {
        var before = fixture.ProviderCalls;
        await Click("Accounts"); await Click("Discover providers");
        await Wait(() => fixture.ProviderCalls > before && HasText("NVIDIA Build"), "Providers loads its local descriptor endpoint on navigation");
        Check(HasText("No account configured") && !HasText("Connection verified"), "catalog is useful with no account and does not invent verification");
        await CaptureBoth("providers");
        Named<ComboBox>("Filter provider access type").SelectedIndex = 1;
        await Wait(() => HasText("1 of 2 providers", true) && HasText("NVIDIA Build") && !HasText("Inception Mercury"), "free access filter excludes one-time trial and paid Inception access");
        Named<ComboBox>("Filter provider access type").SelectedIndex = 0;
        await Wait(() => HasText("2 of 2 providers", true) && HasText("Inception Mercury"), "clearing provider access filter restores all cards");
        Named<TextBox>("Search providers").Text = "step-5";
        await Wait(() => HasText("1 of 2 providers", true) && HasText("NVIDIA Build") && !HasText("Inception Mercury"), "provider search includes concrete model identities");
        Named<TextBox>("Search providers").Text = "";
        await Wait(() => HasText("2 of 2 providers", true) && HasText("Inception Mercury"), "clearing provider search restores all cards");
        var card = Named<Expander>("Provider NVIDIA Build"); card.IsExpanded = true;
        await Task.Delay(100);
        Check(HasText("Unknown — no automatic budget source is available.") && HasText("Unknown requests/minute", true), "unobserved budgets and account limits remain unknown");
        var route = Named<Expander>("NVIDIA Build: Step 5 Preview via NVIDIA capabilities"); route.IsExpanded = true;
        await Task.Delay(100);
        Check(HasText("1,000,000 tokens / Unknown") && HasText("Strict free-only access requires qualified runtime controls", true), "capabilities preserve unknown maximum output and explain qualified free-only controls");
        await CaptureBoth("provider-capabilities", () => route.BringIntoView());
        var header = card.GetVisualDescendants().OfType<ToggleButton>().Single(control => AutomationProperties.GetName(control) == "NVIDIA Build setup and capabilities");
        header.BringIntoView(); header.Focus();
        await Click("Reload status");
        Check(card.IsExpanded && route.IsExpanded && header.IsFocused && Controls<Expander>().Contains(card), "identical catalog refresh preserves card identity expansion and focus");
        await Click("Add NVIDIA Build account");
        Check(HasText("Set up NVIDIA Build") && Named<ComboBox>("Connection / API endpoint").SelectedIndex == -1 && Named<ComboBox>("Billing plan").SelectedIndex == -1,
            "assisted provider setup requires an explicit route and billing choice");
        Named<ComboBox>("Connection / API endpoint").SelectedIndex = 0;
        Check(Named<ComboBox>("Billing plan").SelectedIndex == -1 && Named<TextBox>("Base URL").IsReadOnly
            && Named<TextBox>("Base URL").Text == "https://integrate.api.nvidia.com/v1" && Named<TextBox>("Own API key").PasswordChar == '●',
            "setup derives a read-only official endpoint and masks the private credential");
        await CaptureBoth("provider-setup");
        await Click("Cancel");

        // Explicit synthetic callbacks prove links are inert until clicked; never invoke the shell browser launcher.
        var opened = new List<Uri>(); var setups = new List<string>();
        var view = new ProviderCatalogView(opened.Add, setups.Add);
        var probe = new Window { Width = 800, Height = 560, Content = new ScrollViewer { Content = view } };
        view.SetCatalog(Fixture.ProviderCatalog, []); probe.Show(); await Task.Delay(100);
        Check(opened.Count == 0 && setups.Count == 0, "rendering provider guidance never opens links or starts account setup");
        var buttons = view.GetVisualDescendants().OfType<Button>().ToArray();
        buttons.Single(button => AutomationProperties.GetName(button) == "NVIDIA Build: Official account / keys").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        buttons.Single(button => AutomationProperties.GetName(button) == "Add NVIDIA Build account").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(opened.Count == 1 && opened[0].AbsoluteUri == "https://build.nvidia.com/stepfun-ai/step-5-preview" && setups.SequenceEqual(["nvidia-build"]), "explicit catalog actions invoke only their supplied callbacks");
        view.ShowUnavailable("Fixture catalog outage");
        Check(view.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text == "Provider catalog unavailable")
            && !view.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text?.Contains("invalid credential", StringComparison.OrdinalIgnoreCase) == true),
            "catalog outages do not mislabel credentials as invalid");
        probe.Close(); window.Activate();
    }

    private async Task CheckInspector()
    {
        await Click("Live");
        await Wait(() => HasText("Connected to the live inspector", true) && HasSelectable("First answer continued."), "Inspector loads and assembles live output chunks");
        Check(HasGroup("conversation:conversation-ui") && HasGroup("request:request-unknown") && HasGroup("request:request-error")
            && HasGroup("request:request-conflict") && !HasGroup("request:request-a") && !HasGroup("request:request-b"),
            "only evidenced identities group requests; unsupported claims and conflicting retry parents remain separate");
        Check(HasText("Retained result", true), "Host success state renders a retained result");
        var identities = Controls<Expander>().Single(control => Equals(control.Header, "Identities & measurement sources")); identities.IsExpanded = true;
        await Task.Delay(100);
        Check(HasSelectable("Conversation: conversation-ui") && HasSelectable("Worker: worker-ui") && HasText("conversationId · harness_header", true),
            "Inspector exposes identities together with their evidence sources");
        identities.IsExpanded = false;
        var requestPicker = Named<ComboBox>("Request within this group");
        var groupButton = Named<Button>("Inspect group conversation:conversation-ui");
        var selectedRequest = requestPicker.SelectedItem?.ToString();
        await CaptureBoth("inspector", verify: () =>
        {
            CheckInspectorViewport("inspector");
            var descendants = window.GetVisualDescendants().ToArray();
            Check(descendants.Contains(requestPicker) && descendants.Contains(groupButton)
                && ReferenceEquals(Named<ComboBox>("Request within this group"), requestPicker)
                && requestPicker.SelectedItem?.ToString() == selectedRequest,
                $"Inspector resize preserves native picker group control and selected request at {window.Bounds.Width:0}px");
            if (window.Bounds.Width < 900)
                Check(FitsScrollViewports(Named<ComboBox>("Conversation / linked requests")) && FitsScrollViewports(requestPicker),
                    "narrow Inspector conversation and request pickers fit within the initial viewport");
        });
        var eventDetail = Named<StackPanel>("Inspector events").GetVisualDescendants().OfType<Expander>().First(x => Equals(x.Header, "Event details"));
        eventDetail.IsExpanded = true;
        await SelectPresentation(InformationLevel.Barebones);
        Check(eventDetail.IsExpanded, "manually opened live event details stay open when changing to Barebones");
        eventDetail.IsExpanded = false;
        await SelectPresentation(InformationLevel.Advanced);
        Check(!eventDetail.IsExpanded, "manually closed live event details stay closed when changing to Advanced");
        fixture.InspectorAdditionalEvent = true;
        await Click("Refresh inspector");
        await Wait(() => HasSelectable("Follow-up event after presentation change."), "live appends new output after a presentation change");
        var regeneratedDetail = Named<StackPanel>("Inspector events").GetVisualDescendants().OfType<Expander>().First(x => Equals(x.Header, "Event details"));
        Check(!regeneratedDetail.IsExpanded, "new live events preserve the explicit detail choice for retained source events");
        await SelectPresentation(InformationLevel.Simple);
        Check(!regeneratedDetail.IsExpanded, "returning to Simple preserves the explicit live detail choice");
        requestPicker.SelectedIndex = 1;
        await Wait(() => HasSelectable("Fixture tool result.") && HasSelectable("Answer after tool. Still streaming."), "tool continuation stays within its selected request");
        Check(!HasSelectable("First answer continued.") && HasText("Tool · fixture_tool"), "switching request clears preceding output and labels tool content");
        await CaptureBoth("inspector-tool", verify: () => CheckInspectorViewport("inspector-tool"));
        Named<TextBox>("Search live requests").Text = "request-unknown";
        await Wait(() => HasGroup("request:request-unknown") && !HasGroup("conversation:conversation-ui"), "Inspector search filters observed groups locally");
        Check(!Controls<ComboBox>().Any(x => AutomationProperties.GetName(x) == "Request within this group"),
            "singleton live group hides its redundant request picker");
        Named<TextBox>("Search live requests").Text = "no-fixture-request-matches";
        await Wait(() => HasText("No requests match these filters.", true), "live filters have a distinct empty result state");
        Check(!HasSelectable("First answer") && !HasSelectable("Independent output."), "filtered-out retained output is never displayed as a current match");
        await CaptureBoth("live-filter-empty");
        Named<TextBox>("Search live requests").Text = "";
        await Wait(() => HasGroup("request:request-unknown") && HasGroup("conversation:conversation-ui"), "clearing Inspector search restores observed groups");
        Named<ComboBox>("Inspector activity").SelectedIndex = 2;
        Check(HasGroup("request:request-error") && !HasGroup("conversation:conversation-ui"), "Inspector error filter uses observed failed status");
        Named<ComboBox>("Inspector activity").SelectedIndex = 0;
        await Click("Inspect group conversation:conversation-ui");
        Named<ComboBox>("Request within this group").SelectedIndex = 1;
        await Wait(() => HasSelectable("Answer after tool."), "returning from filters restores the selected observed request output");
        await Click("Toggle inspector updates"); await Task.Delay(300);
        var paused = fixture.InspectorCalls + fixture.InspectorEventCalls;
        await Task.Delay(550);
        Check(fixture.InspectorCalls + fixture.InspectorEventCalls == paused && HasText("Updates paused", true), "pause stops automatic live fetches");
        var snapshots = fixture.InspectorCalls;
        await Click("Refresh inspector");
        await Wait(() => fixture.InspectorCalls == snapshots + 1, "manual inspector refresh works once while paused");
        await Task.Delay(300);
        Check(fixture.InspectorCalls == snapshots + 1, "manual refresh preserves paused state");
        await Click("Clear displayed events");
        Check(!HasSelectable("Answer after tool.") && HasText("Displayed events cleared.", true), "clear removes displayed content with an explicit explanation");
        var beforeClearRefresh = fixture.InspectorEventCalls;
        await Click("Refresh inspector");
        await Wait(() => fixture.InspectorEventCalls > beforeClearRefresh, "cleared Inspector can refresh while its snapshot cursor trails events");
        Check(fixture.InspectorEventAfters[^1] == fixture.InspectorHighWater && !HasSelectable("Ahead-of-snapshot output.") && HasText("Displayed events cleared.", true),
            "clear never regresses the event cursor or replays output ahead of the snapshot");
        fixture.InspectorGap = true;
        await Click("Refresh inspector");
        await Wait(() => HasText("Content gap · retention_limit", true), "live retention gaps stay visible even when no new output arrives");
        fixture.InspectorGap = false;
        fixture.InspectorRestarted = true;
        await Click("Refresh inspector");
        await Wait(() => HasSelectable("New source output only."), "Inspector attaches to the new live source");
        Check(!HasGroup("conversation:conversation-ui") && !HasSelectable("First answer") && !HasSelectable("Fixture tool result.")
            && HasText("Live source restarted; previous content and relationships were cleared.", true), "source restart purges prior content and relationships");
        await CaptureBoth("inspector-source-restarted", verify: () => CheckInspectorViewport("inspector-source-restarted"));
        Check(!Controls<ComboBox>().Any(x => AutomationProperties.GetName(x) == "Request within this group"), "restarted singleton live source uses only one picker");
        fixture.InspectorEmpty = true;
        await Click("Refresh inspector");
        await Wait(() => HasText("No observed streams yet.", true), "empty live source explains how to get an observed stream");
        await CaptureBoth("live-source-empty");
        fixture.InspectorEmpty = false;
        await Click("Refresh inspector");
        await Wait(() => HasSelectable("New source output only."), "manual refresh restores the nonempty source before outage testing");
        fixture.InspectorFailure = true;
        await Click("Refresh inspector");
        await Wait(() => HasText("Inspector disconnected · active state is unknown", true), "Inspector failures report unknown active state");
        Check(HasText("Active state unknown", true), "connection loss cannot leave a falsely active request");
        await CaptureBoth("inspector-disconnected", verify: () => CheckInspectorViewport("inspector-disconnected"));
        fixture.InspectorFailure = false;
        await Click("Toggle inspector updates");
        await Wait(() => HasText("Connected to the live inspector", true), "Inspector recovers from a local source outage");
        var contextDetails = Controls<Expander>().Single(x => Equals(x.Header, "Identities & measurement sources"));
        contextDetails.IsExpanded = true; await Task.Delay(80);
        await Click("Account a1");
        await Wait(() => Controls<Expander>().Any(x => AutomationProperties.GetName(x) == "Account budgets for Personal Codex updated"),
            "Live links only its observed account identity to Accounts");
        await Click("Back");
        await Wait(() => HasSelectable("New source output only."), "Live Back reloads only current bounded source output");
        Controls<Expander>().Single(x => Equals(x.Header, "Identities & measurement sources")).IsExpanded = true;
        await Task.Delay(80); await Click("Model access r1");
        await Wait(() => Controls<Expander>().Any(x => AutomationProperties.GetName(x) == "Details for model access gpt-lain-01"),
            "Live links only its observed configured route identity to Models");
        await Click("Back");
        await Wait(() => HasSelectable("New source output only."), "Live model Back reattaches to current source without fabricating history");
        await Click("Models");
        var detached = fixture.InspectorCalls + fixture.InspectorEventCalls;
        await Task.Delay(550);
        Check(fixture.InspectorCalls + fixture.InspectorEventCalls == detached, "Inspector polling stops when detached");
    }

    private void CheckInspectorViewport(string page)
    {
        var events = Named<StackPanel>("Inspector events");
        var output = events.GetVisualDescendants().OfType<SelectableTextBlock>().Where(IsShown).ToArray();
        Check(output.Length > 0, $"{page} has retained output to inspect at {window.Bounds.Width:0}px");
        var inner = events.GetVisualAncestors().OfType<ScrollViewer>().First();
        var outer = inner.GetVisualAncestors().OfType<ScrollViewer>().First();
        inspectorLayouts.Add(new
            {
                page, width = window.Bounds.Width, height = window.Bounds.Height,
                outputCount = output.Length,
                innerViewport = new { inner.Viewport.Width, inner.Viewport.Height },
                innerExtent = new { inner.Extent.Width, inner.Extent.Height },
                outerViewport = new { outer.Viewport.Width, outer.Viewport.Height },
                outerExtent = new { outer.Extent.Width, outer.Extent.Height },
                outerOffsetY = outer.Offset.Y
            });
        Check(inner.Viewport.Height >= 64 && inner.Viewport.Width >= 160,
            $"{page} provides a usable native output viewport at {window.Bounds.Width:0}px ({inner.Viewport.Width:0}×{inner.Viewport.Height:0})");
        Check(outer.Extent.Height <= outer.Viewport.Height + 2 && Math.Abs(outer.Offset.Y) <= 1,
            $"{page} does not require a second outer scroll at {window.Bounds.Width:0}px (extent {outer.Extent.Height:0}, viewport {outer.Viewport.Height:0}, offset {outer.Offset.Y:0})");
        Check(output.Any(FitsScrollViewports), $"{page} displays actual output within the initial inner and outer viewports at {window.Bounds.Width:0}px");
        var previous = inner.Offset;
        try
        {
            // Scroll only the native output pane. BringIntoView could hide a broken outer layout by moving both scrollers.
            var firstY = output[0].TranslatePoint(default, inner)?.Y ?? throw new Exception("Inspector output detached");
            var firstOffset = Math.Clamp(inner.Offset.Y + firstY - 4, 0, Math.Max(0, inner.Extent.Height - inner.Viewport.Height));
            inner.Offset = new Vector(previous.X, firstOffset); window.UpdateLayout();
            Check(FitsScrollViewports(output[0]), $"{page} first output is reachable within inner and outer viewports at {window.Bounds.Width:0}px");
            inner.Offset = new Vector(previous.X, Math.Max(0, inner.Extent.Height - inner.Viewport.Height)); window.UpdateLayout();
            Check(FitsScrollViewports(output[^1]), $"{page} last output is reachable with the native output scroller at {window.Bounds.Width:0}px");
        }
        finally { inner.Offset = previous; window.UpdateLayout(); }
    }

    private void CheckBundledFonts()
    {
        foreach (var label in new[] { "Accounts", "Personal Codex", "lain" })
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
    private bool UsageConnected() => Controls<UsageView>().Any(view => view.GetLogicalDescendants().OfType<TextBlock>()
        .Any(x => x.Text == "Connected · source: lain host / persistent usage API"));
    private Task ClickUsageApply() => Click("Apply");
    private async Task Click(string label)
    {
        // Selected-list pages may have several row actions. Select the first visible action.
        var button = Controls<Button>().FirstOrDefault(b => (Equals(b.Content, label) || AutomationProperties.GetName(b) == label) && b.IsEnabled)
            ?? throw new Exception($"Enabled button not found: {label}. Available: {string.Join(", ", Controls<Button>().Select(b => b.Content))}");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(180);
    }
    private async Task CaptureBoth(string page, Action? reveal = null, Action? verify = null)
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
            verify?.Invoke();
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
        bitmap.Render(window); bitmap.Save(Path.Combine(Output, file), new PngBitmapEncoderOptions());
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
                error = error.ToString(), layout = LayoutEvidence(), checks, screenshots, inspectorLayouts, requests = fixture.Requests
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
