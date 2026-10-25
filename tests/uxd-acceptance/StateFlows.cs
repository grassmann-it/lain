using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Lain.Core;
using System.Reflection;
using Lain.Desktop;
using Lain.Desktop.Presentation;

internal sealed partial class Acceptance
{
    private readonly List<object> requestDetails = [];

    private async Task RunStateFlows()
    {
        await FirstAccountSnapshots();
        foreach (var (width, height) in new[] { (1120, 780), (800, 560) })
        {
            (string Name, Func<Task> Action)[] flows =
            [
                ("native-state-draft", NativeDraftFlow),
                ("native-state-retained-model-command", NativeRetainedModelCommandFlow),
                ("native-state-account-refresh", NativeAccountRefreshFlow),
                ("native-state-model-menu", NativeModelMenuFlow),
                ("native-n09-model-identity", NativeN09ModelIdentityFlow),
                ("native-n09-prepared-cancel-target", NativeN09PreparedCancelTargetFlow),
                ("native-n09-prepared-review", NativeN09PreparedReviewFlow),
                ("native-state-apply", NativeApplyFlow),
                ("native-state-apply-late-preview", NativeLatePreviewFlow),
                ("native-state-apply-rejected", NativeRejectedApplyFlow),
                ("native-state-connect", NativeConnectPrerequisiteFlow),
                ("native-state-connect-late", NativeLateConnectionFlow),
                ("native-state-status-late", NativeLateStatusFlow),
                ("native-state-setup", NativeSetupFlow),
                ("native-state-browse", NativeBrowseFlow),
                ("native-layout-monitor-table", NativeMonitorTableFlow),
                ("native-layout-live-left", NativeLiveLeftFlow)
            ];
            foreach (var (name, action) in flows)
            {
                var fullName = $"{name}-{width}x{height}";
                if (!Selected(fullName)) continue;
                await Open(InformationLevel.Simple, ViewDensity.Normal);
                await Resize(width, height);
                await Case(fullName, action);
                await Close();
            }
        }
    }

    private async Task FirstAccountSnapshots()
    {
        foreach (var (width, height) in new[] { (1120, 780), (800, 560) })
        foreach (var outcome in new[] { "data", "empty", "error" })
        {
            var name = $"native-state-first-snapshot-{outcome}-{width}x{height}";
            if (!Selected(name)) continue;
            Fixture.PendingResponse? held = null;
            await Open(InformationLevel.Simple, ViewDensity.Normal,
                value => held = value.Defer("GET", "/api/v1/configuration"), waitForAccounts: false);
            await Resize(width, height);
            await Case(name, async () =>
            {
                await Wait(() => held!.Entered.Task.IsCompleted);
                Check(Find<Control>("Account inventory loading") is not null, "first unanswered snapshot is visibly loading");
                Check(!Controls<TextBlock>().Any(value => value.Text == "No accounts configured"), "pending first read never claims empty inventory");
                Capture(FlowImage("pending"));
                if (outcome == "error") held!.Fail("Synthetic first configuration failure");
                else held!.Complete(outcome == "empty" ? fixture.Config with { Accounts = [] } : fixture.Config);
                if (outcome == "data") await Wait(() => Find<Button>("Open account details for Personal Codex") is not null);
                else await Wait(() => Find<Control>(outcome == "empty" ? "Account inventory empty" : "Account inventory load failed") is not null);
                Check(Mutations == 0, "first snapshot states perform no mutations");
                Capture(FlowImage(outcome));
            });
            // Release even if an assertion failed, allowing the private window to close.
            held!.Complete(fixture.Config);
            await Close();
        }
    }

    private async Task NativeMonitorTableFlow()
    {
        using var input = new NativeInput(); input.Focus(window);
        fixture.SetMonitor(true);
        await NativePress(input, Need<Button>("Reload status"));
        await Wait(() => Find<StackPanel>("Monitor quota table") is not null);
        var table = Need<StackPanel>("Monitor quota table");
        var rows = table.GetVisualDescendants().OfType<Grid>().Where(value =>
            AutomationProperties.GetName(value)?.StartsWith("Monitor quota window for ") == true).ToArray();
        Check(rows.Length == fixture.QuotaSnapshot().Length, "Monitor table includes every reported quota window");
        var bar = table.GetVisualDescendants().OfType<ProgressBar>().First();
        Check(bar.Bounds.Width >= 250, "Monitor uses long consumption bar below numeric values");
        Capture(FlowImage("inventory"));
        await NativePress(input, Need<Button>("Open account details for Personal Codex"));
        var detail = Need<ScrollViewer>("Account detail content for Personal Codex");
        Check(Fits(Need<Button>("Open account details for Personal Codex")) && Fits(Need<Button>("Close account details for Personal Codex")),
            "Monitor inventory and selected detail remain simultaneously reachable");
        Check(Bounds(table).Right <= Bounds(detail).Left && detail.Bounds.Width >= 300, "Monitor selection preserves adjacent independent panes");
        Capture(FlowImage("selected"));
        input.Key(0xff1b); await Task.Delay(100);
        Check(Need<Button>("Open account details for Personal Codex").IsFocused, "native Escape returns Monitor focus to selected account");
        Check(Mutations == 0, "Monitor table browsing is read-only");
    }

    private async Task NativeLiveLeftFlow()
    {
        using var input = new NativeInput(); input.Focus(window);
        await NativeGo(input, "Live");
        await Wait(() => Find<Button>("Inspect group conversation:conversation-ui") is not null);
        var inventory = Need<ScrollViewer>("Live request inventory");
        Check(Fits(inventory), "Live inventory is visible in initial layout");
        Capture(FlowImage("initial"));
        var group = Need<Button>("Inspect group conversation:conversation-ui");
        await NativePress(input, group);
        var outputView = Need<ScrollViewer>("Live output viewport");
        Check(Fits(inventory) && Fits(outputView) && Bounds(inventory).Right < Bounds(outputView).Left,
            "Live keeps persistent narrow left list beside selected output");
        Check(outputView.Bounds.Height >= 64 && outputView.Bounds.Width >= 300, "Live output has useful initial viewport");
        foreach (var name in new[] { "Toggle inspector updates", "Refresh inspector", "Clear displayed events" })
            Check(Fits(Need<Button>(name)), "Live action fits: " + name);
        Check(Fits(Need<CheckBox>("Follow live output")) && Fits(Need<ComboBox>("Information level")), "Follow and footer remain reachable");
        Capture(FlowImage("selected"));
        await NativePress(input, Need<Button>("Toggle inspector updates"));
        await NativePress(input, Need<CheckBox>("Follow live output"));
        fixture.InspectorAdditionalEvent = true;
        var reads = fixture.InspectorCalls;
        await NativePress(input, Need<Button>("Refresh inspector"));
        await Wait(() => fixture.InspectorCalls > reads);
        Check(ReferenceEquals(inventory, Need<ScrollViewer>("Live request inventory")) && ReferenceEquals(outputView, Need<ScrollViewer>("Live output viewport")),
            "manual Live poll preserves both scroll controls");
        await NativePress(input, Need<ComboBox>("Information level")); input.Key(0xff57); input.Key(0xff0d); await Task.Delay(100); level = InformationLevel.Advanced;
        Check(ReferenceEquals(outputView, Need<ScrollViewer>("Live output viewport")) && Fits(inventory), "information-level change preserves Live list and output context");
        input.Key(0xff09); await Task.Delay(80);
        var focused = Avalonia.Controls.TopLevel.GetTopLevel(window)?.FocusManager?.GetFocusedElement() as Control;
        Check(focused is not null, "native Tab remains in reachable Live window controls");
        focusTrace.Add("Live after level change: " + (focused is null ? "none" : AutomationProperties.GetName(focused) ?? focused.GetType().Name));
        Capture(FlowImage("polled-level-change"));
        await NativePress(input, Need<Button>("Clear displayed events"));
        Check(Mutations == 0, "Live Pause, Follow, Refresh and Clear send no mutations or inference");
        Check(Fits(Need<ScrollViewer>("Live request inventory")), "Clear retains persistent left list surface");
        Capture(FlowImage("cleared"));
    }

    private async Task NativePress(NativeInput input, Control control)
    {
        control.BringIntoView(); await Task.Delay(60);
        Check(Fits(control), "native input target is within visible viewport: " + AutomationProperties.GetName(control));
        input.Click(control); await Task.Delay(100);
    }

    private async Task NativeGo(NativeInput input, string destination)
    {
        var button = Controls<Button>().FirstOrDefault(value => value.Tag as string == destination) ?? Need<Button>(destination);
        await NativePress(input, button); page = destination;
    }

    private string FlowImage(string state) => currentCase + "-" + state;
    private int Mutations => fixture.Requests.Count(value => !value.StartsWith("GET ", StringComparison.Ordinal));

    private async Task NativeDraftFlow()
    {
        using var input = new NativeInput(); input.Focus(window);
        CheckShell();
        await NativeGo(input, "Models");
        await NativePress(input, Need<Button>("Edit model access gpt-lain-01"));
        var enabled = Need<CheckBox>("Model access enabled");
        await NativePress(input, enabled);
        Check(enabled.IsChecked == false && enabled.IsFocused, "native toggle creates focused local disabled draft");
        var mutations = Mutations;
        var size = window.ClientSize;
        foreach (var selected in new[] { InformationLevel.Advanced, InformationLevel.Barebones, InformationLevel.Simple })
        {
            await NativePress(input, Need<ComboBox>("Information level"));
            input.Key(0xff50);
            for (var i = 0; i < (int)selected; i++) input.Key(0xff54);
            input.Key(0xff0d); await Task.Delay(100); level = selected;
            Check(ReferenceEquals(enabled, Need<CheckBox>("Model access enabled")) && !enabled.IsChecked.GetValueOrDefault(),
                "native information-level change keeps mounted draft");
            Check(window.ClientSize == size, "information-level selection preserves window size");
        }
        await NativeGo(input, "Settings");
        var densityControl = Need<ComboBox>("Density for Models");
        await NativePress(input, densityControl); input.Key(0xff57); input.Key(0xff0d); await Task.Delay(100);
        await NativePress(input, Need<Button>("Back")); page = "Models";
        Check(ReferenceEquals(enabled, Need<CheckBox>("Model access enabled")) && !enabled.IsChecked.GetValueOrDefault(),
            "native Settings density change and Back restore the same draft");
        Check(window.ClientSize == size, "density selection preserves window size");
        await NativeGo(input, "Profiles");
        var backName = "Return to Models · gpt-lain-01 draft";
        Check(Fits(Need<Button>(backName)), "chosen Draft B provides persistent visible return outside page scrolling");
        if (Find<Button>("Dismiss message") is { } dismiss) await NativePress(input, dismiss);
        Check(Fits(Need<Button>(backName)), "dismissing transient notice retains draft return");
        Capture(FlowImage("retained"));
        await NativePress(input, Need<Button>(backName)); page = "Models";
        Check(ReferenceEquals(enabled, Need<CheckBox>("Model access enabled")) && !enabled.IsChecked.GetValueOrDefault(),
            "return restores identical mounted editor and unsaved input");
        Check(enabled.IsFocused, "return restores the editor's native focus anchor");
        fixture.Config = fixture.Config with { Revision = fixture.Config.Revision + 1 };
        await NativePress(input, Need<Button>("Reload status"));
        Check(!enabled.IsChecked.GetValueOrDefault(), "new configuration revision retains the local field");
        await NativePress(input, Need<Button>("Save model access"));
        Check(Mutations == mutations && !enabled.IsChecked.GetValueOrDefault(), "stale editor Save is rejected locally without request or input loss");
        Capture(FlowImage("stale"));
        await NativeGo(input, "Profiles");
        await NativePress(input, Need<Button>("Discard Models · gpt-lain-01 draft"));
        Check(Find<Button>(backName) is null, "explicit discard removes this draft notice");
        await NativeGo(input, "Models");
        await NativePress(input, Find<Button>("Edit model access gpt-lain-01") ?? Need<Button>("Edit selected model access gpt-lain-01"));
        Check(Need<CheckBox>("Model access enabled").IsChecked == true, "discard restores saved value on next edit");
        Check(Mutations == mutations && fixture.ApplyCalls == 0, "draft return, revision update and discard send no Save or Apply");
        Capture(FlowImage("discarded"));
    }

    private async Task NativeRetainedModelCommandFlow()
    {
        using var input = new NativeInput(); input.Focus(window);
        await NativeGo(input, "Models");
        await NativePress(input, Need<Button>("Edit model access gpt-lain-01"));
        var draft = Need<CheckBox>("Model access enabled");
        await NativePress(input, draft);
        await NativeGo(input, "Profiles");
        await NativePress(input, Need<Button>("Select profile Daily"));
        await NativePress(input, Need<Button>("Open model access gpt-lain-02")); page = "Models";
        await NativePress(input, Need<Button>("Edit selected model access gpt-lain-02"));
        Check(Find<CheckBox>("Model access enabled") is null && Find<Button>("Return to Models · gpt-lain-01 draft") is not null,
            "editing context B is blocked while retained A remains available");
        Check(Mutations == 0, "context navigation and blocked Edit B never save A");
        Capture(FlowImage("blocked-edit-b"));
        var more = Need<Button>("Selected model actions gpt-lain-02");
        await NativePress(input, more);
        await Wait(() => more.ContextMenu?.Items.OfType<MenuItem>().Any() == true);
        var enable = more.ContextMenu!.Items.OfType<MenuItem>().Single(value => Equals(value.Header, "Enable at next Apply"));
        input.Click(enable);
        await Wait(() => fixture.Config.Routes.Single(value => value.Id == "r2").Enabled);
        Check(Find<Button>("Return to Models · gpt-lain-01 draft") is not null, "successful B command does not delete retained A");
        await NativeGo(input, "Accounts");
        await NativePress(input, Need<Button>("Return to Models · gpt-lain-01 draft")); page = "Models";
        Check(ReferenceEquals(draft, Need<CheckBox>("Model access enabled")) && draft.IsChecked == false,
            "return after B mutation preserves exact A editor and input");
        Check(fixture.Requests.Count(value => value == "PUT /api/v1/routes/r2") == 1 && Mutations == 1,
            "only explicitly chosen B action sends mutation with stable r2 identity");
        Capture(FlowImage("returned-a-after-b-save"));
    }

    private async Task NativeAccountRefreshFlow()
    {
        using var input = new NativeInput(); input.Focus(window);
        var quotas = fixture.QuotaSnapshot();
        foreach (var fail in new[] { false, true })
        {
            var held = fixture.Defer("POST", "/api/v1/quotas/refresh");
            var refresh = Need<Button>("Refresh quota for Personal Codex");
            var summary = Need<Border>("Account budgets for Personal Codex");
            var values = summary.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray();
            await NativePress(input, refresh);
            await Wait(() => held.Entered.Task.IsCompleted);
            Check(!Need<Button>("Refresh quota for Personal Codex").IsEnabled, "only requested refresh is busy");
            Check(values.Where(text => text?.Contains('%') == true).All(value =>
                Need<Border>("Account budgets for Personal Codex").GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == value)),
                "last known quota percentages remain during held request");
            Capture(FlowImage(fail ? "error-busy" : "success-busy"));
            if (Find<Button>("Close account details for Command Code Pro") is null)
                await NativePress(input, Need<Button>("Open account details for Command Code Pro"));
            var selected = Need<Button>("Close account details for Command Code Pro");
            var b = Need<Border>("Account budgets for Command Code Pro").GetVisualDescendants().OfType<TextBlock>().Select(value => value.Text).ToArray();
            if (fail) held.Fail("Synthetic delayed Personal Codex failure");
            else held.Complete(quotas.Select(value => value.AccountId == "a1" ? value with { Used = 42, Remaining = 58, UsedPercent = 42 }
                : value.AccountId == "a2" ? value with { Used = 99, Remaining = 1, UsedPercent = 99 } : value).ToArray());
            await Wait(() => Need<Button>("Refresh quota for Personal Codex").IsEnabled);
            Check(ReferenceEquals(selected, Need<Button>("Close account details for Command Code Pro")), "late A response does not replace selected B inspector");
            Check(Need<Border>("Account budgets for Command Code Pro").GetVisualDescendants().OfType<TextBlock>().Select(value => value.Text).SequenceEqual(b),
                "late A full response does not merge stale B values");
            Capture(FlowImage(fail ? "late-error-b-selected" : "late-success-b-selected"));
        }
        Check(fixture.Requests.Count(value => value == "POST /api/v1/quotas/refresh") == 2, "one request per explicit account refresh without retry");
        Check(fixture.RequestDetails.Count(value => System.Text.Json.JsonSerializer.Serialize(value).Contains("quotas/refresh")) == 2, "request ledger preserves both refresh bodies");
    }

    private async Task NativeModelMenuFlow()
    {
        using var input = new NativeInput(); input.Focus(window);
        await NativeGo(input, "Models");
        var row = Need<Border>("Model row r1");
        var more = Need<Button>("Actions for model access gpt-lain-01");
        await NativePress(input, more);
        await Wait(() => more.ContextMenu?.IsOpen == true);
        var commands = MenuCommands(more.ContextMenu!);
        Capture(FlowImage("pointer-menu"));
        input.Key(0xff1b); await Task.Delay(100);
        Check(more.IsFocused, "Escape returns focus to model menu trigger");
        row.BringIntoView(); await Task.Delay(50); input.Click(row, 3); await Task.Delay(100);
        Check(row.ContextMenu?.IsOpen == true && MenuCommands(row.ContextMenu).SequenceEqual(commands), "native right click has same stable command set");
        input.Key(0xff1b); await Task.Delay(70);
        await NativePress(input, more); input.Key(0xff1b); await Task.Delay(70);
        input.Key(0xffc7, 0xffe1); await Task.Delay(100);
        Check(more.ContextMenu?.IsOpen == true || row.ContextMenu?.IsOpen == true, "native Shift+F10 opens model commands");
        Capture(FlowImage("keyboard-menu")); input.Key(0xff1b);
        var search = Need<TextBox>("Search configured model access");
        await NativeText(input, search, "no-match-demo");
        Check(Find<Button>("Clear filters") is not null && search.Text == "no-match-demo", "no-match retains native query with explicit reset");
        Capture(FlowImage("no-match"));
        await NativePress(input, Need<Button>("Clear filters"));
        Check(Need<TextBox>("Search configured model access").Text == "" && Find<Border>("Model row r1") is not null, "native filter reset restores actual inventory");
        Check(Mutations == 0, "menu, Escape and filter reset paths remain read-only");
    }

    private StatusDto CurrentFixtureStatus() => new(1, "test", "ready", true, fixture.Config.Revision, fixture.Config.AppliedRevision,
        DateTimeOffset.UtcNow, ActiveProfileId: fixture.ActiveProfileId, ActiveProfileRevision: fixture.ActiveProfileRevision,
        Capabilities: new(true, true, true, true, true, true), Application: fixture.ApplicationStatus);

    private async Task NativeLateConnectionFlow()
    {
        using var input = new NativeInput(); input.Focus(window);
        var heldStatus = fixture.Defer("GET", "/api/v1/status");
        var heldMethods = fixture.Defer("GET", "/api/v1/connection-methods");
        var tile = Need<Border>("Account budgets for Personal Codex");
        var more = tile.GetVisualDescendants().OfType<Button>().Single(value => Equals(value.Content, "⋯") || Equals(value.Content, "…"));
        await NativePress(input, more);
        var reconnect = more.ContextMenu!.Items.OfType<MenuItem>().Single(value => Equals(value.Header, "Reconnect account…"));
        input.Click(reconnect);
        await Wait(() => heldStatus.Entered.Task.IsCompleted && heldMethods.Entered.Task.IsCompleted);
        await NativeGo(input, "Models");
        await NativePress(input, Need<Button>("Edit model access gpt-lain-01"));
        var draft = Need<CheckBox>("Model access enabled"); await NativePress(input, draft);
        heldStatus.Complete(CurrentFixtureStatus()); heldMethods.Complete(fixture.ConnectionMethodSnapshot());
        await Task.Delay(180);
        Check(ReferenceEquals(draft, Need<CheckBox>("Model access enabled")) && draft.IsChecked == false,
            "late reconnect responses cannot replace a newer Models editor");
        Capture(FlowImage("reconnect-response-discarded"));
        await NativeGo(input, "Accounts");
        var failedMethods = fixture.Defer("GET", "/api/v1/connection-methods");
        await NativePress(input, Need<Button>("Add account"));
        await Wait(() => failedMethods.Entered.Task.IsCompleted);
        await NativeGo(input, "Models");
        failedMethods.Fail("Synthetic obsolete chooser failure"); await Task.Delay(180);
        Check(ReferenceEquals(draft, Need<CheckBox>("Model access enabled")) && draft.IsChecked == false,
            "late chooser failure cannot replace restored Models draft");
        Check(!Controls<TextBlock>().Any(value => value.Text == "Connections unavailable") && fixture.LoginBegins == 0 && Mutations == 0,
            "obsolete chooser and reconnect never start login or publish an error on another page");
        Capture(FlowImage("chooser-error-discarded"));
    }

    private async Task NativeLateStatusFlow()
    {
        using var input = new NativeInput(); input.Focus(window);
        await NativeGo(input, "Profiles");
        await NativePress(input, Need<Button>("Select profile Daily"));
        await NativePress(input, Need<Button>("Apply to proxy…"));
        await Wait(() => Find<Button>("Apply to proxy")?.IsEnabled == true);
        fixture.LoseApplyResponse = true;
        await NativePress(input, Need<Button>("Apply to proxy"));
        await Wait(() => fixture.ApplyCalls == 1 && Find<Button>("Check current status")?.IsEnabled == true);
        var held = fixture.Defer("GET", "/api/v1/status");
        await NativePress(input, Need<Button>("Check current status"));
        await Wait(() => held.Entered.Task.IsCompleted);
        await NativePress(input, Need<Button>("Cancel review"));
        if (Find<Button>("Select profile Workspace 3") is null) await NativePress(input, Need<Button>("Back to profiles"));
        await NativePress(input, Need<Button>("Select profile Workspace 3"));
        held.Complete(CurrentFixtureStatus());
        await Wait(() => Need<TextBlock>("Applied proxy profile status").Text?.Contains("Applied to proxy: Daily") == true);
        Check(Need<TextBlock>("Selected profile status").Text == "Viewing Workspace 3", "late status confirmation preserves newer profile selection");
        Check(!Controls<TextBlock>().Any(value => value.Text?.Contains("outcome unknown", StringComparison.OrdinalIgnoreCase) == true),
            "late global status clarification updates mounted summary and list");
        Check(fixture.ApplyCalls == 1, "delayed status clarification never repeats Apply");
        Capture(FlowImage("confirmed-after-navigation"));
    }

    private async Task NativeConnectPrerequisiteFlow()
    {
        using var input = new NativeInput(); input.Focus(window);
        foreach (var monitor in new[] { true, false })
        {
            await NativeGo(input, "Accounts");
            if (Find<Button>("Back to accounts") is { } backToAccounts) await NativePress(input, backToAccounts);
            fixture.SetMonitor(monitor); fixture.CoreLoginOverride = false;
            await NativePress(input, Need<Button>("Reload status"));
            await NativePress(input, Need<Button>("Add account"));
            await NativePress(input, Need<Button>("Connect provider codex"));
            var review = Find<Button>("Review requirements for Continue in browser") ?? Need<Button>("Review requirements");
            await NativePress(input, review);
            Check(Find<Control>("Connection prerequisites browser") is not null, "Core requirement is explained before browser login");
            Check(fixture.LoginBegins == 0 && fixture.Config.RuntimeMode == (monitor ? "monitor" : "gateway"), "missing Core sends no login and preserves configured intent");
            Capture(FlowImage(monitor ? "monitor-prerequisite" : "gateway-prerequisite"));
            if (monitor)
            {
                await NativePress(input, Need<Button>("Review Gateway setup"));
                Check(fixture.Config.RuntimeMode == "monitor" && Mutations == 0, "Gateway review navigation does not change operating mode");
                Capture(FlowImage("gateway-review"));
            }
            else
            {
                await NativePress(input, Need<Button>("Review provider sources"));
                Check(fixture.Config.RuntimeMode == "gateway" && Mutations == 0, "provider source navigation does not change operating mode");
                Capture(FlowImage("provider-sources"));
            }
        }
    }

    private async Task NativeText(NativeInput input, TextBox field, string value)
    {
        await NativePress(input, field);
        input.Key('a', 0xffe3); input.Type(value);
        await Wait(() => field.Text == value);
    }

    private async Task NativeBrowseFlow()
    {
        using var input = new NativeInput(); input.Focus(window);
        await NativeGo(input, "Profiles");
        await NativePress(input, Need<Button>("Select profile Daily"));
        await NativePress(input, Need<Button>("Set up Codex client…"));
        var folder = Need<TextBox>("Destination parent folder");
        var before = input.VisibleTopLevels();
        await NativePress(input, Need<Button>("Browse…"));
        await Wait(() => input.VisibleTopLevels().Except(before).Any());
        await Task.Delay(180);
        Capture(FlowImage("picker-open"));
        input.Key(0xff1b);
        await Wait(() => folder.IsFocused);
        Check(fixture.ExportCalls == 0 && fixture.ApplyCalls == 0, "native Browse Escape restores destination focus without export or Apply");
        Capture(FlowImage("escape-focus"));
    }

    private async Task NativeSetupFlow()
    {
        using var input = new NativeInput(); input.Focus(window);
        await NativeGo(input, "Profiles");
        await NativePress(input, Need<Button>("Select profile Daily"));
        await NativePress(input, Need<Button>("Set up Codex client…"));
        var parent = Path.Combine(output, currentCase + "-private-install");
        Directory.CreateDirectory(parent);
        var executable = Path.Combine(parent, "synthetic-lain");
        await File.WriteAllTextAsync(executable, "#!/bin/sh\nexit 99\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var sentinelDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        Directory.CreateDirectory(sentinelDirectory);
        var sentinel = Path.Combine(sentinelDirectory, "config.toml");
        await File.WriteAllTextAsync(sentinel, "# isolated ordinary Codex configuration sentinel\n");
        var sentinelBytes = await File.ReadAllBytesAsync(sentinel);
        var executableField = Need<TextBox>("lain command-line executable");
        var folder = Need<TextBox>("Destination parent folder");
        await NativeText(input, executableField, Path.Combine(parent, "missing-executable"));
        await NativeText(input, folder, parent);
        Check(!Need<Button>("Prepare Codex files").IsEnabled && fixture.ExportCalls == 0, "invalid executable prevents export");
        Capture(FlowImage("invalid-executable"));
        await NativeText(input, executableField, executable);
        await NativeText(input, folder, Path.Combine(parent, "missing"));
        Check(!Need<Button>("Prepare Codex files").IsEnabled && fixture.ExportCalls == 0,
            "missing parent disables Prepare before any export request");
        Check(Find<Control>("Client setup validation") is not null, "invalid destination has inline validation");
        Capture(FlowImage("invalid-parent"));
        await NativeText(input, folder, parent);
        await Wait(() => Need<Button>("Prepare Codex files").IsEnabled);
        await NativePress(input, Need<Button>("Prepare Codex files"));
        await Wait(() => Find<Control>("Prepared Codex artifact") is not null);
        Check(fixture.ExportCalls == 1 && Directory.GetDirectories(parent).Length == 0, "Prepare produces an identified artifact without writing destination files");
        Check(fixture.ApplyCalls == 0 && fixture.ActiveProfileId == "p2", "Prepared Daily leaves Workspace 2 applied");
        Capture(FlowImage("prepared"));
        await NativePress(input, Need<Button>("Install client files"));
        await Wait(() => Directory.GetDirectories(parent, "lain-codex-*").Length == 1);
        var installed = Directory.GetDirectories(parent, "lain-codex-*").Single();
        Check(File.Exists(Path.Combine(installed, "lain.config.toml")) &&
            Directory.GetFiles(Path.Combine(installed, ".lain-artifacts"), "lain.models.json", SearchOption.AllDirectories).Length == 1,
            "explicit Install writes separate configuration and immutable generation catalog");
        Check((await File.ReadAllBytesAsync(sentinel)).SequenceEqual(sentinelBytes), "isolated ordinary Codex configuration is byte-for-byte preserved");
        Check(fixture.ExportCalls == 1 && fixture.ApplyCalls == 0 && fixture.ActiveProfileId == "p2", "Install uses prepared artifact without export retry or Apply");
        Capture(FlowImage("installed"));
    }

    private async Task NativeRejectedApplyFlow()
    {
        using var input = new NativeInput(); input.Focus(window);
        fixture.ApplicationStatus = new("completed", "previous-operation", "p2", 1);
        await NativePress(input, Need<Button>("Reload status"));
        await NativeGo(input, "Profiles");
        await NativePress(input, Need<Button>("Select profile Daily"));
        await NativePress(input, Need<Button>("Apply to proxy…"));
        await Wait(() => Find<Button>("Apply to proxy")?.IsEnabled == true);
        fixture.ApplyRejectionCode = "runtime_busy";
        await NativePress(input, Need<Button>("Apply to proxy"));
        await Wait(() => Need<TextBlock>("Profile apply review message").Text?.StartsWith("Apply rejected:") == true);
        Check(fixture.ActiveProfileId == "p2" && fixture.ApplyCalls == 0, "confirmed runtime_busy rejection preserves previous runtime");
        Check(!Need<Button>("Apply to proxy").IsEnabled && Need<Button>("Refresh preview").IsEnabled,
            "confirmed rejection invalidates diff and permits manual preview");
        Check(!Controls<TextBlock>().Any(value => value.Text?.Contains("outcome unknown", StringComparison.OrdinalIgnoreCase) == true),
            "confirmed runtime_busy rejection is not mislabeled unknown");
        Capture(FlowImage("rejected"));
        await NativePress(input, Need<Button>("Check current status"));
        Check(Need<TextBlock>("Profile apply confirmed status").Text?.Contains("Applied to proxy: Workspace 2") == true,
            "unchanged previous completed operation confirms preserved Workspace 2");
        await NativePress(input, Need<Button>("Refresh preview"));
        await Wait(() => Need<Button>("Apply to proxy").IsEnabled);
        Check(fixture.Requests.Count(value => value == "POST /api/v1/profiles/p1/apply") == 1,
            "status and preview refresh do not retry rejected Apply");
        Capture(FlowImage("manual-new-review"));
    }

    private async Task NativeLatePreviewFlow()
    {
        using var input = new NativeInput(); input.Focus(window);
        await NativeGo(input, "Profiles");
        await NativePress(input, Need<Button>("Select profile Daily"));
        var held = fixture.Defer("GET", "/api/v1/profiles/p1/preview");
        await NativePress(input, Need<Button>("Apply to proxy…"));
        await Wait(() => held.Entered.Task.IsCompleted);
        await NativePress(input, Need<Button>("Select profile Workspace 3"));
        held.Complete(new ProfileApplyPreviewDto(1, "p1", fixture.Config.Revision, "p2", 1,
            ["r1"], ["r2"], [], ["a1"], ["all connected clients"], "global proxy restart", true));
        await Task.Delay(180);
        Check(Need<TextBlock>("Selected profile status").Text?.Contains("Workspace 3") == true, "late Daily preview preserves newly selected Workspace 3");
        Check(Find<Control>("Profile apply review") is null && fixture.ApplyCalls == 0, "late preview cannot mount or commit against a different target");
        Capture(FlowImage("late-preview-discarded"));
    }

    private void CheckAppliedSurfaces(bool unknown)
    {
        var summary = (TextBlock)typeof(MainWindow).GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var review = Need<TextBlock>("Profile apply confirmed status").Text ?? "";
        var inventory = Need<Button>(unknown ? "Select profile Workspace 2" : "Select profile Daily")
            .GetVisualDescendants().OfType<TextBlock>().Select(value => value.Text).ToArray();
        if (unknown)
        {
            Check(summary.Text?.Contains("Last confirmed: Workspace 2") == true && summary.Text.Contains("unknown", StringComparison.OrdinalIgnoreCase),
                "shell retains explicitly last-confirmed Workspace 2 during unknown outcome");
            Check(review.Contains("Last confirmed: Workspace 2") && review.Contains("Requested p1"), "review separates requested Daily from last-confirmed Workspace 2");
            Check(inventory.Any(value => value?.Contains("Last confirmed") == true), "inventory labels Workspace 2 as last confirmed");
        }
        else
        {
            Check(summary.Text?.Contains("Daily") == true && !summary.Text.Contains("unknown", StringComparison.OrdinalIgnoreCase), "shell confirms Daily after fresh terminal status");
            Check(review.Contains("Applied to proxy: Daily") && !review.Contains("unknown", StringComparison.OrdinalIgnoreCase), "review confirms the same Daily state");
            Check(review.Contains($"saved {fixture.Config.Revision} / proxy {fixture.Config.AppliedRevision}") &&
                !Controls<TextBlock>().Any(value => value.Text?.StartsWith("Saved changes pending") == true),
                "confirmed status aligns saved/proxy revision and clears stale pending label");
            Check(inventory.Any(value => value?.Contains("Applied · revision " + fixture.Config.Revision) == true), "inventory confirms Daily at matching revision");
        }
    }

    private async Task NativeApplyFlow()
    {
        using var input = new NativeInput(); input.Focus(window);
        await NativeGo(input, "Profiles");
        await NativePress(input, Need<Button>("Select profile Daily"));
        var trigger = Need<Button>("Apply to proxy…");
        await NativePress(input, trigger);
        await Wait(() => Find<Border>("Profile apply review") is not null || Find<Control>("Profile apply review") is not null);
        Check(!lifetime.Windows.Any(value => value != window && value.IsVisible), "Apply review stays in main window");
        Check(fixture.PreviewCalls > 0 && fixture.ApplyCalls == 0, "preview reads without commit");
        Capture(FlowImage("review"));
        await NativePress(input, Need<Button>("Cancel review"));
        Check(Need<Button>("Apply to proxy…").IsFocused, "native Cancel returns focus to review trigger");
        Check(fixture.ApplyCalls == 0, "cancel does not commit");
        await NativePress(input, Need<Button>("Apply to proxy…"));
        await Wait(() => Find<Button>("Apply to proxy") is not null);
        fixture.Config = fixture.Config with { Revision = fixture.Config.Revision + 1 };
        await NativePress(input, Need<Button>("Reload status"));
        Check(!Need<Button>("Apply to proxy").IsEnabled, "revision change disables previous diff commit");
        Check(Fits(Need<Button>("Refresh preview")), "stale diff offers reachable refresh");
        Capture(FlowImage("stale"));
        await NativePress(input, Need<Button>("Refresh preview"));
        await Wait(() => Find<Button>("Apply to proxy")?.IsEnabled == true);
        fixture.LoseApplyResponse = true; fixture.StatusFailure = true;
        var confirm = Need<Button>("Apply to proxy");
        confirm.BringIntoView(); await Task.Delay(50); input.Click(confirm); input.Click(confirm);
        await Wait(() => fixture.ApplyCalls == 1 && Find<Button>("Check current status")?.IsEnabled == true);
        Check(fixture.ActiveProfileId == "p1", "synthetic host committed p1 before response loss");
        Check(Controls<TextBlock>().Any(value => value.Text?.Contains("Last confirmed", StringComparison.OrdinalIgnoreCase) == true), "unknown outcome labels last confirmed status");
        CheckAppliedSurfaces(true);
        Capture(FlowImage("unknown"));
        fixture.StatusFailure = false; fixture.LoseApplyResponse = false;
        var completed = fixture.ApplicationStatus;
        fixture.StatusObservedAtOverride = DateTimeOffset.UtcNow.AddHours(-1);
        await NativePress(input, Need<Button>("Check current status"));
        await Wait(() => Need<Button>("Check current status").IsEnabled);
        CheckAppliedSurfaces(true);
        Capture(FlowImage("old-status-retains-unknown"));
        fixture.StatusObservedAtOverride = null; fixture.ApplicationStatus = null;
        await NativePress(input, Need<Button>("Check current status"));
        await Wait(() => Need<Button>("Check current status").IsEnabled);
        CheckAppliedSurfaces(true);
        Capture(FlowImage("incomplete-status-retains-unknown"));
        fixture.ApplicationStatus = completed;
        await NativePress(input, Need<Button>("Check current status"));
        await Wait(() => !Controls<TextBlock>().Any(value => value.Text?.Contains("outcome unknown", StringComparison.OrdinalIgnoreCase) == true));
        Check(fixture.ApplyCalls == 1, "double-click and status clarification never repeat Apply");
        CheckAppliedSurfaces(false);
        Capture(FlowImage("confirmed"));
    }
}
