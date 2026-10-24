using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Lain.Core;
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
                ("native-state-account-refresh", NativeAccountRefreshFlow),
                ("native-state-model-menu", NativeModelMenuFlow),
                ("native-state-apply", NativeApplyFlow),
                ("native-state-connect", NativeConnectPrerequisiteFlow),
                ("native-state-setup", NativeSetupFlow)
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
        Check(!Need<Button>("Save model access").IsEnabled, "stale editor save is disabled");
        Capture(FlowImage("stale"));
        await NativeGo(input, "Profiles");
        await NativePress(input, Need<Button>("Discard Models · gpt-lain-01 draft"));
        Check(Find<Button>(backName) is null, "explicit discard removes this draft notice");
        await NativeGo(input, "Models");
        await NativePress(input, Need<Button>("Edit model access gpt-lain-01"));
        Check(Need<CheckBox>("Model access enabled").IsChecked == true, "discard restores saved value on next edit");
        Check(Mutations == mutations && fixture.ApplyCalls == 0, "draft return, revision update and discard send no Save or Apply");
        Capture(FlowImage("discarded"));
    }

    private async Task NativeAccountRefreshFlow()
    {
        using var input = new NativeInput(); input.Focus(window);
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
        Capture(FlowImage("busy"));
        await NativePress(input, Need<Button>("Open account details for Command Code Pro"));
        var selected = Need<Button>("Close account details for Command Code Pro");
        held.Fail("Synthetic delayed Personal Codex failure");
        await Wait(() => Need<Button>("Refresh quota for Personal Codex").IsEnabled);
        Check(ReferenceEquals(selected, Need<Button>("Close account details for Command Code Pro")), "late A error does not replace selected B inspector");
        Check(fixture.Requests.Count(value => value == "POST /api/v1/quotas/refresh") == 1, "one account refresh request without retry");
        Check(fixture.RequestDetails.Any(value => System.Text.Json.JsonSerializer.Serialize(value).Contains("a1")), "refresh ledger includes requested a1 identity");
        Capture(FlowImage("late-error-b-selected"));
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
        Check(Mutations == 0, "all menu entry and Escape paths remain read-only");
    }

    private async Task NativeConnectPrerequisiteFlow()
    {
        using var input = new NativeInput(); input.Focus(window);
        fixture.SetMonitor(true);
        await NativePress(input, Need<Button>("Reload status"));
        await NativePress(input, Need<Button>("Add account"));
        await NativePress(input, Need<Button>("Connect provider codex"));
        var review = Find<Button>("Review requirements for Continue in browser") ?? Need<Button>("Review requirements");
        await NativePress(input, review);
        Check(Find<Control>("Connection prerequisites browser") is not null, "Core requirement is explained before browser login");
        Check(fixture.LoginBegins == 0 && fixture.Config.RuntimeMode == "monitor", "missing Core sends no login and preserves Monitor intent");
        Capture(FlowImage("monitor-prerequisite"));
        await NativePress(input, Need<Button>("Review Gateway setup"));
        Check(fixture.Config.RuntimeMode == "monitor" && Mutations == 0, "Gateway review navigation does not change operating mode");
        Capture(FlowImage("gateway-review"));
    }

    private async Task NativeText(NativeInput input, TextBox field, string value)
    {
        await NativePress(input, field);
        input.Key('a', 0xffe3); input.Type(value);
        await Wait(() => field.Text == value);
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
        Check(trigger.IsFocused, "native Cancel returns focus to review trigger");
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
        await NativePress(input, Need<Button>("Apply to proxy"));
        await Wait(() => fixture.ApplyCalls == 1 && Find<Button>("Check current status") is not null);
        Check(fixture.ActiveProfileId == "p1", "synthetic host committed p1 before response loss");
        Check(Controls<TextBlock>().Any(value => value.Text?.Contains("Last confirmed", StringComparison.OrdinalIgnoreCase) == true), "unknown outcome labels last confirmed status");
        Capture(FlowImage("unknown"));
        fixture.StatusFailure = false; fixture.LoseApplyResponse = false;
        await NativePress(input, Need<Button>("Check current status"));
        await Wait(() => !Controls<TextBlock>().Any(value => value.Text?.Contains("outcome unknown", StringComparison.OrdinalIgnoreCase) == true));
        Check(fixture.ApplyCalls == 1, "status clarification never repeats Apply");
        Capture(FlowImage("confirmed"));
    }
}
