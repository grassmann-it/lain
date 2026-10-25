using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lain.Client;
using Lain.Desktop;
using Lain.Desktop.Presentation;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try { NativeInput.AssertPrivateDisplay(); }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 2; }
        return AppBuilder.Configure<AcceptanceApp>().UsePlatformDetect().WithInterFont()
            .StartWithClassicDesktopLifetime(args);
    }
}

internal sealed class AcceptanceApp : Application
{
    public override void Initialize()
    {
        var production = new App(); production.Initialize();
        RequestedThemeVariant = production.RequestedThemeVariant;
        var resources = production.Resources; production.Resources = new ResourceDictionary(); Resources = resources;
        foreach (var style in production.Styles.ToArray()) { production.Styles.Remove(style); Styles.Add(style); }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Dispatcher.UIThread.Post(async () =>
        {
            var acceptance = new Acceptance(lifetime);
            try { await acceptance.Run(); }
            catch (Exception error) { acceptance.RecordFatal(error); }
            await acceptance.SaveEvidence();
            lifetime.Shutdown(acceptance.Failed ? 1 : 0);
        });
        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed partial class Acceptance(IClassicDesktopStyleApplicationLifetime lifetime)
{
    private static readonly string[] Pages = ["Accounts", "Models", "Profiles", "Usage", "Live", "Settings"];
    private readonly string output = Environment.GetEnvironmentVariable("LAIN_TEST_OUTPUT")!;
    private readonly List<object> cases = [];
    private readonly List<object> images = [];
    private readonly List<object> geometry = [];
    private readonly List<string> assertions = [];
    private readonly List<string> requests = [];
    private readonly List<string> focusTrace = [];
    private readonly List<string> nativeInputTrace = [];
    private Fixture fixture = null!;
    private HttpClient? http;
    private MainWindow window = null!;
    private InformationLevel level;
    private ViewDensity density;
    private string page = "Accounts";
    private string currentCase = "startup";
    public bool Failed { get; private set; }

    public async Task Run()
    {
        Directory.CreateDirectory(output);
        NativeInput.Trace = value => nativeInputTrace.Add(currentCase + ": " + value);
        await RunStateFlows();
        // Matrix renders the same production pages, changing only local presentation state.
        foreach (var information in Enum.GetValues<InformationLevel>())
        foreach (var compactness in Enum.GetValues<ViewDensity>())
        {
            if (!Pages.Any(destination => Selected($"matrix-{destination}-{information}-{compactness}"))) continue;
            await Open(information, compactness);
            foreach (var destination in Pages)
            {
                if (!Selected($"matrix-{destination}-{information}-{compactness}")) continue;
                await Case($"matrix-{destination}-{information}-{compactness}", async () =>
                {
                    await Navigate(destination);
                    foreach (var (width, height) in new[] { (1120, 780), (800, 560) })
                    {
                        await Resize(width, height);
                        CheckShell();
                        CheckPage(destination);
                        Capture($"{destination}-{information}-{compactness}-{width}x{height}");
                    }
                });
            }
            Check(fixture.Requests.All(request => request.StartsWith("GET ", StringComparison.Ordinal)),
                "presentation matrix has no host mutations");
            await Close();
        }

        (string Name, Func<Task> Action)[] focused =
        [
            ("account-adjacent-and-back", AccountAnchors), ("native-pointer-keyboard-menus", NativeAccounts),
            ("connect-provider-start-wait-key-result", ConnectFlows), ("model-long-name-and-policies", ModelDetails),
            ("native-profile-viewing-applied-and-review", ProfileDetails), ("usage-partial-null-and-draft", UsageStates),
            ("live-output-and-source-states", LiveStates), ("settings-local-scope", SettingsScope),
            ("monitor-capabilities-and-offline-gateway", ModeStates)
        ];
        foreach (var (name, action) in focused)
        {
            if (!Selected(name)) continue;
            await Open(InformationLevel.Simple, ViewDensity.Normal);
            await Case(name, action);
            await Close();
        }
    }

    private static bool Selected(string name) => Environment.GetEnvironmentVariable("LAIN_UXD_ONLY") is not { Length: > 0 } filter
        || filter.Split(',', StringSplitOptions.RemoveEmptyEntries).Any(value => name.Contains(value, StringComparison.OrdinalIgnoreCase));

    private async Task Open(InformationLevel information, ViewDensity compactness, Action<Fixture>? configure = null, bool waitForAccounts = true)
    {
        fixture = new Fixture(); fixture.UseUxdInventory(); configure?.Invoke(fixture);
        level = information; density = compactness; page = "Accounts";
        var preferencePath = Path.Combine(output, $"preferences-{Guid.NewGuid():N}.json");
        var store = new WorkspacePreferencesStore(preferencePath);
        Check(store.Save(store.Current with { Level = level, Densities = Pages.Where(name => name != "Settings")
            .ToDictionary(name => name, _ => density) }), "synthetic presentation preferences saved");
        http = new HttpClient(fixture) { BaseAddress = new Uri("http://fixture.invalid/api/v1/") };
        window = new MainWindow(new LainApiClient(http), preferencePath);
        window.Show();
        if (waitForAccounts) await Wait(() => Find<Button>("Open account details for Personal Codex") is not null);
        await Resize(1120, 780);
    }

    private async Task Close()
    {
        requests.AddRange(fixture.Requests);
        requestDetails.AddRange(fixture.RequestDetails);
        Check(fixture.Unexpected.Count == 0, "requests stayed inside the explicit in-memory allowlist");
        foreach (var child in lifetime.Windows.Where(w => w != window).ToArray()) child.Close();
        window.Close(); await Task.Delay(50); http?.Dispose(); http = null;
    }

    private async Task Case(string name, Func<Task> action)
    {
        currentCase = name; fixture.Scenario = name;
        var first = assertions.Count;
        var timer = Stopwatch.StartNew();
        try
        {
            await action();
            cases.Add(new { name, fixtureSession = fixture.SessionId, functionalResult = "PASS", assertions = assertions.Count - first, elapsedMs = timer.ElapsedMilliseconds });
            Console.WriteLine("PASS " + name);
        }
        catch (Exception error)
        {
            Failed = true;
            cases.Add(new { name, fixtureSession = fixture.SessionId, functionalResult = "FAIL", error = error.ToString(), elapsedMs = timer.ElapsedMilliseconds });
            try { Capture(name + "-failure"); } catch { }
            Console.Error.WriteLine("FAIL " + name + ": " + error.Message);
        }
    }

    private async Task Navigate(string destination)
    {
        var direct = Controls<Button>().FirstOrDefault(button => button.Tag as string == destination)
            ?? Find<Button>(destination);
        if (direct is not null) Click(direct);
        else
        {
            var more = Need<Button>("More destinations");
            var item = more.ContextMenu?.Items.OfType<MenuItem>().SingleOrDefault(entry => Equals(entry.Header, destination))
                ?? throw new InvalidOperationException(destination + " not reachable in direct navigation or More");
            Check(item.IsEnabled, destination + " More command enabled");
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }
        page = destination;
        await Task.Delay(180);
        if (destination == "Usage") await Wait(() => fixture.UsageCalls > 0 && fixture.AnalysisCalls > 0);
        if (destination == "Live") await Wait(() => fixture.InspectorCalls > 0);
    }

    private void CheckShell()
    {
        var mode = Need<ComboBox>("Information level");
        Check(Controls<ComboBox>().Count(control => AutomationProperties.GetName(control) == "Information level") == 1,
            "one information dropdown");
        var bounds = Bounds(mode);
        Check(Fits(mode) && bounds.Left > window.Bounds.Width / 2 && bounds.Bottom > window.Bounds.Height - 45,
            "information dropdown remains at bottom right");
        var expected = fixture.MonitorMode
            ? fixture.MonitorHistory ? new[] { "Accounts", "Usage" } : ["Accounts"]
            : level switch
            {
                InformationLevel.Barebones => ["Accounts", "Profiles", "Usage"],
                InformationLevel.Simple => ["Accounts", "Models", "Profiles", "Usage", "Live"],
                _ => ["Accounts", "Models", "Profiles", "Usage", "Live"]
            };
        var direct = Controls<Button>().Where(control => control.Tag is string tag && Pages.Contains(tag))
            .Select(control => (string)control.Tag!).ToArray();
        Check(direct.SequenceEqual(expected), "capability and information-level navigation matrix");
        Check(Fits(Need<Image>("Application logo")) && window.Icon is not null, "original cat logo remains visible");
        Check(Fits(Need<Button>("Settings")) && Fits(Need<Button>("Back")), "header navigation remains within viewport");
    }

    private void CheckPage(string destination)
    {
        if (destination == "Accounts")
        {
            var tiles = Controls<Border>().Where(control => AutomationProperties.GetName(control)?.StartsWith("Account budgets for ") == true).ToArray();
            Check(tiles.Length == Fixture.InitialAccounts, "all account summaries remain in the inventory");
            var command = Need<Border>("Account budgets for Command Code Pro");
            var text = command.GetVisualDescendants().OfType<TextBlock>().Select(control => control.Text).ToArray();
            Check(new[] { "5 hours", "Weekly", "Monthly credits", "Purchased credits", "Free credits", "Model allowance" }.All(text.Contains),
                "closed Command summary contains every reported window including unknown and balance");
            Check(!Need<Border>("Account budgets for MiMo API").GetVisualDescendants().OfType<ProgressBar>().Any(),
                "unsupported quota has no invented zero bar");
        }
        if (destination == "Models")
        {
            var edits = Controls<Button>().Where(control => AutomationProperties.GetName(control)?.StartsWith("Edit model access ") == true).ToArray();
            Check(edits.Length == Fixture.InitialRoutes, "all45 models retain a direct edit action");
            Check(edits.Count(Fits) >= (window.Bounds.Width >= 1000 ? 6 : 3), "compact model inventory exposes useful first-viewport rows");
            Check((Find<Border>("Selected model details") is not null) == (window.Bounds.Width >= 1000),
                "model inspector follows the actual resized viewport; no stale wide pane at800px");
        }
        if (destination == "Profiles")
        {
            Check(Find<Button>("Select profile Daily") is not null || Find<TextBlock>("Selected profile status") is not null,
                "profile inventory or selected detail is reachable");
            if (window.Bounds.Width <= 800) Check(Find<Button>("Select profile Daily") is not null && Find<TextBlock>("Selected profile status") is null,
                "initial narrow Profiles uses one inventory pane in both densities");
        }
        if (destination == "Usage")
        {
            var rows = Controls<Expander>().Where(control => AutomationProperties.GetName(control)?.StartsWith("Analysis group ") == true).ToArray();
            Check(rows.Length > 0 && rows.SelectMany(row => row.GetVisualDescendants().OfType<TextBlock>())
                .Any(text => text.Text == "Personal Codex" && Fits(text)),
                "first Usage account name is fully readable in initial viewport, not merely an intersecting row border");
        }
        if (destination == "Live") Check(Controls<Button>().Any(control => AutomationProperties.GetName(control)?.StartsWith("Inspect group ") == true)
            || Controls<SelectableTextBlock>().Any(control => control.Text?.Contains("First answer") == true), "Live inventory or selected output remains visible");
        if (destination == "Settings")
        {
            Check(Controls<CheckBox>().Count(control => new[] { "Minimize to tray", "Close to tray", "Usage and rate-limit alerts" }.Contains(AutomationProperties.GetName(control))) == 3,
                "all native preference checkboxes remain reachable");
            Check(Find<Button>("About lain") is not null && Find<Button>("Getting started") is not null, "both help destinations retained");
            if (window.Bounds.Width >= 1000) Check(Fits(Need<Button>("About lain")), "Settings help starts within wide first viewport");
        }
    }

    private async Task AccountAnchors()
    {
        await Navigate("Accounts"); await Resize(1120, 780);
        var scroller = Need<ScrollViewer>("Account inventory");
        // Frame 2/3 compares a fully visible lower account, not only its title at the fold.
        scroller.Offset = new Vector(0, Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height));
        await Task.Delay(100);
        var origin = Need<Button>("Open account details for Z.ai Coding");
        Check(Fits(Need<Border>("Account budgets for Z.ai Coding")), "lower reference account is fully visible before selection");
        var before = AccountGeometry(); var offset = scroller.Offset;
        Capture("accounts-lower-before"); Click(origin); await Task.Delay(100);
        Check(AccountGeometry().SequenceEqual(before) && scroller.Offset == offset, "adjacent inspector preserves every inventory rectangle and scroll offset");
        var inspector = Need<Border>("Account inspector for Z.ai Coding");
        Check(Bounds(Need<Border>("Account budgets for Z.ai Coding")).Right <= Bounds(inspector).Left + 1, "inspector never covers selected account");
        Capture("accounts-lower-adjacent");
        geometry.Add(new { currentCase, offset = new { offset.X, offset.Y }, before, after = AccountGeometry() });
        var detailScroll = Need<ScrollViewer>("Account detail content for Z.ai Coding");
        detailScroll.Offset = new Vector(0, Math.Min(50, detailScroll.Extent.Height - detailScroll.Viewport.Height));
        var detailOffset = detailScroll.Offset;
        Click(Need<Button>("Settings")); await Task.Delay(70); Click(Need<Button>("Back")); await Task.Delay(100);
        Check(Find<Border>("Account inspector for Z.ai Coding") is not null && Need<ScrollViewer>("Account inventory").Offset == offset,
            "Settings and Back preserve account selection and inventory scroll");
        Check(Need<ScrollViewer>("Account detail content for Z.ai Coding").Offset == detailOffset, "Settings and Back preserve inspector scroll");
        await Resize(800, 560);
        Check(Find<Border>("Account budgets for Z.ai Coding") is null, "narrow account detail replaces inventory");
        Capture("accounts-narrow-detail");
        Click(Need<Button>("Close account details for Z.ai Coding")); await Task.Delay(100);
        Check(Need<Button>("Open account details for Z.ai Coding").IsFocused, "close restores the originating account action focus");
        Capture("accounts-narrow-return");
    }

    private async Task NativeAccounts()
    {
        await Navigate("Accounts"); await Resize(1120, 780);
        using var input = new NativeInput(); input.Focus(window);
        var search = Need<TextBox>("Search accounts"); search.BringIntoView(); await Task.Delay(50);
        input.Click(search); await Wait(() => search.IsFocused);
        foreach (var letter in "codex") { input.Key(letter); await Task.Delay(60); }
        await Wait(() => search.Text == "codex");
        Check(search.Text == "codex" && search.IsFocused, "native pointer hit-testing and native keyboard input reach Search accounts");
        Capture("native-search-focus");
        input.Key('a', 0xffe3); input.Key(0xff08); await Task.Delay(100);
        Check(search.Text == "", "native Ctrl+A Backspace clears search");
        var seenDetail = false;
        for (var n = 0; n < 90; n++)
        {
            input.Key(0xff09); await Task.Delay(12);
            var focused = TopLevel.GetTopLevel(window)?.FocusManager?.GetFocusedElement() as Control;
            var name = focused is null ? "<none>" : AutomationProperties.GetName(focused) ?? focused.GetType().Name;
            focusTrace.Add(name);
            if (name == "Open account details for Personal Codex") { seenDetail = true; break; }
        }
        Check(seenDetail, "native Tab traversal reaches account Details without programmatic focus");
        input.Key(0xff0d); await Task.Delay(100);
        Check(Find<Button>("Close account details for Personal Codex") is not null, "native Enter opens focused account");
        Capture("native-inspector-focus"); input.Key(0xff1b); await Task.Delay(100);
        Check(Need<Button>("Open account details for Personal Codex").IsFocused, "native Escape closes inspector and restores trigger");
        var tile = Need<Border>("Account budgets for Personal Codex"); tile.BringIntoView(); await Task.Delay(60);
        var menu = tile.ContextMenu ?? throw new InvalidOperationException("Account lacks context menu");
        input.Click(tile, 3); await Task.Delay(100);
        Check(menu.IsOpen, "native right click opens account command menu");
        var contextCommands = MenuCommands(menu); Capture("native-account-context-menu");
        input.Key(0xff1b); await Task.Delay(50);
        var more = tile.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "⋯") || Equals(button.Content, "…"));
        input.Click(more); await Task.Delay(100);
        Check(more.ContextMenu?.IsOpen == true && MenuCommands(more.ContextMenu).SequenceEqual(contextCommands), "visible menu and native context menu expose identical commands");
        input.Key(0xff1b); await Task.Delay(50);
        input.Click(Need<Button>("Open account details for Personal Codex")); await Task.Delay(50); input.Key(0xff1b); await Task.Delay(50);
        input.Key(0xffc7, 0xffe1); await Task.Delay(100);
        Check(tile.ContextMenu?.IsOpen == true || more.ContextMenu?.IsOpen == true, "native Shift+F10 reaches the same account commands");
        input.Key(0xff1b);
    }

    private async Task ModelDetails()
    {
        await Navigate("Models"); await Resize(800, 560);
        var longEdit = Need<Button>("Edit model access " + fixture.Config.Routes.Single(route => route.Id == "r45").Alias);
        longEdit.BringIntoView(); await Task.Delay(80); Check(Fits(longEdit), "long alias retains visible narrow Edit action");
        Capture("models-long-name-narrow");
        Click(Need<Button>("Edit model access gpt-lain-01")); await Task.Delay(100);
        var fields = Controls<TextBox>().Select(control => AutomationProperties.GetName(control) ?? control.PlaceholderText).ToArray();
        Check(fields.Any(name => name?.Contains("client", StringComparison.OrdinalIgnoreCase) == true), "full model editor remains reachable");
        Capture("models-policy-editor-narrow");
        Click(Need<Button>("Cancel")); await Task.Delay(80);
    }

    private async Task ConnectFlows()
    {
        var browserCalls = 0;
        var browserField = typeof(MainWindow).GetField("_connectionBrowser", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Synthetic browser seam missing; refusing external browser action");
        browserField.SetValue(window, (Func<Uri, Task<bool>>)(uri =>
        {
            if (uri.Host != "example.invalid") throw new InvalidOperationException("Unexpected browser fixture URL");
            browserCalls++; return Task.FromResult(true);
        }));
        Click(Need<Button>("Add account")); await Wait(() => Find<Button>("Connect provider codex") is not null);
        Capture("connect-provider-chooser-wide"); await Resize(800, 560); Capture("connect-provider-chooser-narrow");
        Click(Need<Button>("Connect provider codex")); await Task.Delay(80);
        if (Find<Button>("Connection method browser") is { } browser) { Click(browser); await Task.Delay(60); }
        await Resize(1120, 780); Capture("connect-codex-start-wide");
        fixture.HoldLogin = true; Click(Need<Button>("Continue in browser"));
        await Wait(() => Find<Button>("Reopen browser") is not null);
        Check(browserCalls == 1 && fixture.LoginBegins == 1, "initial browser start creates one operation through synthetic launcher");
        Capture("connect-codex-waiting-wide"); await Resize(800, 560); Capture("connect-codex-waiting-narrow");
        Check(!Controls<TextBlock>().Any(text => text.Text == "Connection saved."), "pending authorization never claims a saved connection");
        var cancel = Controls<Button>().FirstOrDefault(button => button.Content is string text && text.StartsWith("Cancel", StringComparison.Ordinal));
        Check(cancel is not null && Fits(cancel), "Cancel sign-in remains visible in narrow waiting view");
        Click(Need<Button>("Reopen browser")); await Task.Delay(70);
        Check(browserCalls == 2 && fixture.LoginBegins == 1, "Reopen uses the existing login operation");
        fixture.HoldLogin = false;
        await Wait(() => Find<Button>("Done") is not null);
        Capture("connect-codex-result-narrow"); await Resize(1120, 780); Capture("connect-codex-result-wide");
        Click(Need<Button>("Done")); await Task.Delay(70);
        Click(Need<Button>("Add account")); await Wait(() => Find<Button>("Connect provider kilo") is not null);
        Click(Need<Button>("Connect provider kilo")); await Task.Delay(70);
        if (Find<Button>("Connection method api-key") is { } key) { Click(key); await Task.Delay(70); }
        var secret = Controls<TextBox>().Single(control => control.PasswordChar != '\0');
        var name = Need<TextBox>("Account name"); name.Text = "Fixture Kilo";
        Check(!Controls<TextBlock>().Any(text => text.Text == "Codex credential connected."), "new provider form clears the previous connection notice");
        Check(!Controls<ComboBox>().Any(control => AutomationProperties.GetName(control)?.Contains("plan", StringComparison.OrdinalIgnoreCase) == true),
            "key connection requires no artificial plan selector");
        Capture("connect-kilo-key-wide"); await Resize(800, 560); Capture("connect-kilo-key-narrow");
        secret.Text = "SYNTHETIC-NONCREDENTIAL"; Click(Need<Button>("Connect account"));
        await Wait(() => Find<Button>("Done") is not null);
        Check(secret.Text == "" && fixture.ConnectCalls == 1 && fixture.LastConnect?.Connection.Name == "Fixture Kilo",
            "successful private handoff clears input and preserves supplied account name");
        Check(fixture.ApplyCalls == 0 && fixture.ExportCalls == 0, "connection completion does not apply or export");
        Capture("connect-kilo-result-narrow");
    }

    private async Task ProfileDetails()
    {
        await Navigate("Profiles");
        Click(Need<Button>("Select profile Daily")); await Task.Delay(80);
        using var input = new NativeInput(); input.Focus(window);
        foreach (var (width, height) in new[] { (1120, 780), (800, 560) })
        {
            await Resize(width, height);
            var apply = Need<Button>("Apply to proxy…");
            await NativePress(input, apply);
            await Wait(() => Find<Control>("Profile apply review") is not null);
            Check(!lifetime.Windows.Any(candidate => candidate != window && candidate.IsVisible), "confirmed Apply review remains in-place");
            Check(fixture.PreviewCalls > 0 && fixture.ApplyCalls == 0, "review loads preview without applying");
            Capture($"profiles-inplace-review-{width}x{height}");
            await NativePress(input, Need<Button>("Cancel review"));
            Check(Need<Button>("Apply to proxy…").IsFocused && fixture.ApplyCalls == 0, "native Cancel restores review trigger without mutation");
        }
    }

    private async Task UsageStates()
    {
        await Navigate("Usage"); await Resize(800, 560);
        fixture.NullTokens = true; fixture.IncompleteCoverage = true;
        if (Find<Button>("Apply") is null && Find<Button>("Usage additional filters") is { } filters)
        { Click(filters); await Task.Delay(70); }
        var refresh = Controls<Button>().FirstOrDefault(button => Equals(button.Content, "Apply"))
            ?? throw new InvalidOperationException("Usage Apply action unavailable");
        fixture.NullTokens = false; fixture.PartialTokens = true; fixture.IncompleteCoverage = false;
        Click(refresh); await Task.Delay(200);
        Check(Controls<TextBlock>().Any(control => control.Text?.Contains("Partial · full total unknown") == true),
            "known token subtotal is explicitly partial while full total stays unknown");
        Check(Need<TextBlock>("Usage token measurement coverage").Text?.Contains("110") == true,
            "partial measured samples stay visible independently of complete journal coverage");
        Capture("usage-partial-known-subtotal-narrow");
        fixture.PartialTokens = false; fixture.NullTokens = true; fixture.IncompleteCoverage = true;
        Click(refresh); await Task.Delay(200);
        Check(Controls<TextBlock>().Any(control => control.Text == "—" || control.Text?.Contains("unknown", StringComparison.OrdinalIgnoreCase) == true),
            "missing token totals retain an unknown representation");
        Capture("usage-partial-null-narrow");
        fixture.UsageFailure = true; Click(refresh); await Task.Delay(200); Capture("usage-source-error-narrow");
        fixture.UsageFailure = false; fixture.NullTokens = false; Click(refresh); await Task.Delay(200);
        fixture.IncompleteCoverage = false;
    }

    private async Task LiveStates()
    {
        await Navigate("Live"); await Resize(800, 560);
        if (Find<Button>("Back to live requests") is { } back) { Click(back); await Task.Delay(80); }
        var group = Need<Button>("Inspect group conversation:conversation-ui"); group.BringIntoView(); await Task.Delay(40);
        using var input = new NativeInput(); input.Focus(window); input.Click(group); await Task.Delay(120);
        Check(Controls<SelectableTextBlock>().Any(control => control.Text?.Contains("First answer") == true && IntersectsViewport(control)),
            "selected Live output appears within narrow initial viewport");
        Capture("live-selected-output-narrow");
        var before = fixture.Requests.Count(request => !request.StartsWith("GET ", StringComparison.Ordinal));
        Click(Need<Button>("Toggle inspector updates")); await Task.Delay(60);
        Click(Need<Button>("Clear displayed events")); await Task.Delay(80);
        Check(fixture.Requests.Count(request => !request.StartsWith("GET ", StringComparison.Ordinal)) == before,
            "Pause and Clear stay local without inference or persistent Usage writes");
        Capture("live-paused-cleared-narrow");
        fixture.InspectorRestarted = true; Click(Need<Button>("Refresh inspector")); await Task.Delay(150);
        Capture("live-source-restarted-narrow");
        fixture.InspectorFailure = true; Click(Need<Button>("Refresh inspector")); await Task.Delay(150);
        Capture("live-source-error-narrow"); fixture.InspectorFailure = false;
    }

    private async Task SettingsScope()
    {
        await Navigate("Settings"); await Resize(800, 560);
        var before = fixture.Requests.Count(request => !request.StartsWith("GET ", StringComparison.Ordinal));
        var oldLevel = Need<ComboBox>("Information level").SelectedIndex;
        var oldMode = fixture.Config.RuntimeMode;
        var toggle = Need<CheckBox>("Usage and rate-limit alerts"); toggle.IsChecked = !toggle.IsChecked;
        var densityControl = Controls<ComboBox>().Single(control => AutomationProperties.GetName(control)?.StartsWith("Density for ") == true);
        densityControl.SelectedIndex = 1;
        Check(fixture.Config.RuntimeMode == oldMode && Need<ComboBox>("Information level").SelectedIndex == oldLevel,
            "density and notifications leave host intent and information level independent");
        Check(fixture.Requests.Count(request => !request.StartsWith("GET ", StringComparison.Ordinal)) == before,
            "presentation preferences send no host mutations");
        Capture("settings-narrow-local-preferences");
    }

    private async Task ModeStates()
    {
        await Navigate("Accounts");
        foreach (var history in new[] { false, true })
        {
            fixture.SetMonitor(true, history); Click(Need<Button>("Reload status")); await Task.Delay(160);
            await Resize(800, 560); CheckShell();
            Capture(history ? "monitor-with-history" : "monitor-no-history");
            foreach (var value in Enum.GetValues<InformationLevel>())
            {
                var size = window.ClientSize;
                Need<ComboBox>("Information level").SelectedIndex = (int)value; level = value; await Task.Delay(70);
                Check(window.ClientSize == size, "information level preserves monitor window size"); CheckShell();
            }
        }
        fixture.SetMonitor(false); Click(Need<Button>("Reload status")); await Task.Delay(160);
        fixture.Offline = true; Click(Need<Button>("Reload status")); await Task.Delay(160);
        CheckShell(); Capture("gateway-offline-retained-inventory"); fixture.Offline = false;
    }

    private async Task Resize(int width, int height)
    {
        window.Width = width; window.Height = height;
        await Wait(() => Math.Abs(window.ClientSize.Width - width) < 2 && Math.Abs(window.ClientSize.Height - height) < 2);
        await Task.Delay(90); window.UpdateLayout();
        Check(Math.Abs(window.Bounds.Width - width) < 2 && Math.Abs(window.Bounds.Height - height) < 2,
            $"actual client viewport {width}x{height}");
    }

    private void Capture(string name, Window? target = null)
    {
        target ??= window; target.UpdateLayout();
        var file = name.ToLowerInvariant() + ".png";
        using var bitmap = new RenderTargetBitmap(new PixelSize(Math.Max(1, (int)target.Bounds.Width), Math.Max(1, (int)target.Bounds.Height)), new Vector(96, 96));
        bitmap.Render(target); bitmap.Save(Path.Combine(output, file), new PngBitmapEncoderOptions());
        string? nativeScreen = null;
        if (currentCase.StartsWith("native-", StringComparison.Ordinal) || target != window)
        {
            NativeInput.AssertPrivateDisplay();
            nativeScreen = name.ToLowerInvariant() + "-x11.png";
            var start = new ProcessStartInfo("/usr/bin/import") { UseShellExecute = false, RedirectStandardError = true };
            foreach (var argument in new[] { "-display", Environment.GetEnvironmentVariable("LAIN_TEST_DISPLAY")!, "-window", "root", Path.Combine(output, nativeScreen) })
                start.ArgumentList.Add(argument);
            using var capture = Process.Start(start) ?? throw new InvalidOperationException("Could not capture owned X11 display");
            if (!capture.WaitForExit(3000)) { capture.Kill(); throw new TimeoutException("Private X11 screenshot"); }
            if (capture.ExitCode != 0) throw new InvalidOperationException("Private X11 screenshot failed: " + capture.StandardError.ReadToEnd());
        }
        var actualPage = typeof(MainWindow).GetField("_section", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as string ?? page;
        var preferences = ((WorkspacePreferencesStore)typeof(MainWindow).GetField("_preferencesStore", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!).Current;
        images.Add(new { file, currentCase, fixtureSession = fixture.SessionId, page = actualPage, runtimeMode = fixture.Config.RuntimeMode, informationLevel = preferences.Level.ToString(),
            density = preferences.Densities.GetValueOrDefault(actualPage, ViewDensity.Normal).ToString(), width = target.ClientSize.Width, height = target.ClientSize.Height,
            fixture = "five-accounts-45-routes-three-profiles", visualReview = "REQUIRED", nativeScreen, nativeInput = nativeInputTrace.Any(value => value.StartsWith(currentCase + ": ", StringComparison.Ordinal)),
            renderScaling = target.RenderScaling, scaleMethod = "AVALONIA_GLOBAL_SCALE_FACTOR",
            requestedScale = Environment.GetEnvironmentVariable("AVALONIA_GLOBAL_SCALE_FACTOR") });
    }

    private static string[] MenuCommands(ContextMenu menu) => menu.Items.OfType<MenuItem>()
        .Select(item => $"{item.Header}|{item.IsEnabled}").ToArray();
    private string[] AccountGeometry() => Controls<Border>().Where(control => AutomationProperties.GetName(control)?.StartsWith("Account budgets for ") == true)
        .Select(control => $"{AutomationProperties.GetName(control)}|{Bounds(control)}").ToArray();
    private T? Find<T>(string name) where T : Control => Controls<T>().FirstOrDefault(control => AutomationProperties.GetName(control) == name
        || control is Button button && Equals(button.Content, name));
    private T Need<T>(string name) where T : Control => Find<T>(name) ?? throw new InvalidOperationException("Missing reachable " + typeof(T).Name + ": " + name);
    private T[] Controls<T>() where T : Control => window.GetVisualDescendants().OfType<T>().Where(Shown).ToArray();
    private static bool Shown(Control control) => control.IsVisible && control.GetVisualAncestors().OfType<Control>().All(parent => parent.IsVisible);
    private Rect Bounds(Control control) => new(control.TranslatePoint(default, window) ?? throw new InvalidOperationException("Detached control"), control.Bounds.Size);
    private bool Fits(Control control)
    {
        var bounds = Bounds(control);
        return Shown(control) && bounds.Width > 0 && bounds.Height > 0 && Contains(new Rect(window.ClientSize), bounds)
            && control.GetVisualAncestors().OfType<ScrollViewer>().All(scroll => Contains(Bounds(scroll), bounds));
    }
    private bool IntersectsViewport(Control control) => Shown(control) && Bounds(control).Intersects(new Rect(window.ClientSize))
        && control.GetVisualAncestors().OfType<ScrollViewer>().All(scroll => Bounds(scroll).Intersects(Bounds(control)));
    private static bool Contains(Rect outer, Rect inner) => inner.Left >= outer.Left - 1 && inner.Top >= outer.Top - 1
        && inner.Right <= outer.Right + 1 && inner.Bottom <= outer.Bottom + 1;
    private static void Click(Button button)
    {
        if (!button.IsEnabled) throw new InvalidOperationException("Disabled action: " + AutomationProperties.GetName(button));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private static async Task Wait(Func<bool> ready)
    {
        for (var n = 0; n < 120; n++) { if (ready()) return; await Task.Delay(25); }
        throw new TimeoutException("Production UI fixture readiness");
    }
    private void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        assertions.Add(currentCase + ": " + description);
    }
    public void RecordFatal(Exception error)
    {
        Failed = true; cases.Add(new { name = currentCase, functionalResult = "FATAL", error = error.ToString() });
        Console.Error.WriteLine(error);
    }
    public async Task SaveEvidence()
    {
        Directory.CreateDirectory(output);
        var assembly = typeof(MainWindow).Assembly;
        await File.WriteAllTextAsync(Path.Combine(output, "evidence.json"), JsonSerializer.Serialize(new
        {
            functionalResult = Failed ? "FAIL" : "PASS", visualResult = "NOT_REVIEWED_BY_HARNESS", accessibilityResult = "NOT_QUALIFIED",
            requestedFilter = Environment.GetEnvironmentVariable("LAIN_UXD_ONLY"),
            commit = Environment.GetEnvironmentVariable("LAIN_TEST_COMMIT"), assembly = assembly.Location,
            assemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))),
            privateXvfb = true, cases, assertions, images, geometry, focusTrace, nativeInputTrace, requests, requestDetails,
            limitations = new[] { "Images require independent visual review", "XTEST is synthetic OS input, not a screenreader audit",
                "No real host, providers, credentials, installed app, external browser, clipboard, or tray qualification" }
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"UXD fixture { (Failed ? "FAIL" : "PASS") }: {cases.Count} cases, {assertions.Count} assertions, {images.Count} images");
    }
}
