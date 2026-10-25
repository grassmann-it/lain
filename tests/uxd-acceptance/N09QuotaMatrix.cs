using System.Globalization;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Lain.Desktop.Presentation;

internal sealed partial class Acceptance
{
    private const string N09LongAccount = "C Research account for regional comparison with a deliberately long saved customer project name";

    private async Task RunN09QuotaMatrix()
    {
        Fixture? template = null;
        foreach (var monitor in new[] { false, true })
        foreach (var (width, height) in monitor
            ? new[] { (1120, 780), (800, 560), (640, 420) }
            : new[] { (1120, 780), (800, 560) })
        foreach (var information in Enum.GetValues<InformationLevel>())
        {
            var name = $"native-n09-quota-{(monitor ? "monitor" : "gateway")}-{information}-{width}x{height}";
            if (!Selected(name)) continue;
            if (template is null)
            {
                template = new Fixture();
                template.UseQuotaHierarchyInventory();
                template.Config = template.Config with { Accounts = template.Config.Accounts.Select(account => account.Id == "qh-command"
                    ? account with { Name = N09LongAccount } : account).ToArray() };
                await File.WriteAllTextAsync(Path.Combine(output, "n09-quota-input.json"), JsonSerializer.Serialize(new
                {
                    fixture = "quota-hierarchy-seven-accounts-long-custom-name", template.Config,
                    quotas = template.QuotaSnapshot(), scope = "Same synthetic snapshot for every matrix cell in this run; runtime mode varies explicitly."
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            await Open(information, ViewDensity.Normal, value =>
            {
                value.Config = template.Config;
                value.QuotaOverride = template.QuotaSnapshot();
                value.ActiveProfileId = null; value.ActiveProfileRevision = null;
                value.SetMonitor(monitor);
            }, waitForAccounts: false);
            await Case(name, async () =>
            {
                await Wait(() => Find<Border>("Account budgets for " + N09LongAccount) is not null);
                await Resize(width, height);
                await NativeN09QuotaCase(information, monitor, width, height);
            });
            await Close();
        }
    }

    private async Task NativeN09QuotaCase(InformationLevel information, bool monitor, int width, int height)
    {
        using var input = new NativeInput(); input.Focus(window);
        var original = JsonSerializer.Serialize(fixture.Config);
        var quotas = JsonSerializer.Serialize(fixture.QuotaSnapshot());
        var requested = double.Parse(Environment.GetEnvironmentVariable("AVALONIA_GLOBAL_SCALE_FACTOR") ?? "1", CultureInfo.InvariantCulture);
        Check((requested == 1 || requested == 1.25) && Math.Abs(window.RenderScaling - requested) < .001,
            "actual native RenderScaling matches requested 100/125 percent matrix cell");
        Check(window.ClientSize.Width >= width - 1 && window.ClientSize.Width <= width + 1
            && window.ClientSize.Height >= height - 1 && window.ClientSize.Height <= height + 1,
            "matrix cell uses actual requested client dimensions");
        Check(monitor == (Find<StackPanel>("Monitor quota table") is not null), "matrix cell uses the intended Host operating mode");
        Check(Need<ComboBox>("Information level").SelectedIndex == (int)information,
            "matrix cell uses the requested saved information level");
        CheckN09QuotaShell();

        // Real input changes only the filter; config IDs and original names are
        // checked again after all presentation operations.
        var search = Need<TextBox>("Search accounts");
        await NativeText(input, search, "regional comparison");
        await Wait(() => N09QuotaCards().Length == 1);
        Check(AutomationProperties.GetName(N09QuotaCards()[0]) == "Account budgets for " + N09LongAccount,
            "native search resolves the long saved name to its one stable account");
        input.Key(0xff09); await Task.Delay(60);
        var forward = window.FocusManager?.GetFocusedElement() as Control;
        Check(forward is not null && !ReferenceEquals(forward, search), "native Tab leaves the account search field");
        input.Key(0xff09, 0xffe1); await Task.Delay(60);
        Check(search.IsFocused, "native Shift+Tab restores account search focus");
        focusTrace.Add(currentCase + ": search Tab -> " + (AutomationProperties.GetName(forward!) ?? forward!.GetType().Name) + " -> Shift+Tab search");
        input.Key('a', 0xffe3); input.Key(0xff08);
        await Wait(() => search.Text == "" && N09QuotaCards().Length == 7);
        CheckN09QuotaGeometry("inventory");
        var command = Need<Border>("Account budgets for " + N09LongAccount);
        var labels = new[] { "5 hours", "7 hours", "7 days", "Monthly", "Purchased credits", "Free credits" };
        string[] Windows(Control root) => N09QuotaVisible<TextBlock>(root).Select(value => value.Text)
            .Where(value => value is not null && labels.Contains(value)).Cast<string>().ToArray();
        Check(Windows(command).SequenceEqual(labels.Take(2))
            && N09QuotaVisible<ProgressBar>(command).Select(value => value.Value).SequenceEqual(new[] { 15d, 25d }),
            "first look shows only genuine 5h/7h consumption in short-window order");
        Check(N09QuotaVisible<ProgressBar>(Need<Border>("Account budgets for E Monthly")).Single().Value == 60,
            "monthly-only measured account retains its genuine 60 percent budget");
        Check(N09QuotaVisible<ProgressBar>(Need<Border>("Account budgets for G Stale measured")).Single().Value == 70
            && N09QuotaVisible<TextBlock>(Need<Border>("Account budgets for G Stale measured"))
                .Any(value => value.Text?.Contains("Last known", StringComparison.OrdinalIgnoreCase) == true),
            "stale account preserves measured consumption with explicit last-known state");
        var openName = "Open account details for " + N09LongAccount;
        var open = Need<Button>(openName);
        open.BringIntoView(); await Task.Delay(80);
        Check(Fits(open), "long-name account detail action fits the actual viewport");
        Capture(FlowImage("long-name-inventory"));

        var expander = Need<Expander>("Additional budgets for " + N09LongAccount);
        var header = expander.GetVisualDescendants().OfType<ToggleButton>().First();
        await NativePress(input, header);
        await Wait(() => expander.IsExpanded);
        Check(Windows(command).SequenceEqual(labels.Take(5)),
            "native expansion exposes weekly/monthly/positive credits without inventing zero extras");
        input.Key(0x20); await Wait(() => !expander.IsExpanded);
        Check(header.IsFocused, "native Space collapses additional budgets with focus on the real header");
        await NativePress(input, Need<Button>(openName));
        var detailName = "Account detail content for " + N09LongAccount;
        var closeName = "Close account details for " + N09LongAccount;
        await Wait(() => Find<ScrollViewer>(detailName) is not null);
        var detail = Need<ScrollViewer>(detailName);
        Check(Windows(detail).SequenceEqual(labels), "selected long-name account retains all six original windows including zero credits");
        Check(Fits(Need<Button>(closeName)) && Fits(detail),
            "selected account detail viewport and Close remain reachable at the permitted window size");
        CheckN09QuotaGeometry("selected-detail");
        Capture(FlowImage("long-name-selected"));

        // The same selected identity must survive presentation changes. Menus
        // may legitimately be identical between levels; no fake nav differences.
        var alternate = information == InformationLevel.Advanced ? InformationLevel.Barebones : InformationLevel.Advanced;
        await N09NativeLevel(input, alternate);
        Check(Find<ScrollViewer>(detailName) is not null && Find<Button>(closeName) is not null,
            "native information-level change retains selected stable account identity");
        await N09NativeLevel(input, information);
        Check(Find<ScrollViewer>(detailName) is not null, "return to original level retains selected account");
        CheckN09QuotaShell();
        // Put focus in the real inspector with native traversal before Escape.
        var close = Need<Button>(closeName);
        close.BringIntoView(); await Task.Delay(60);
        for (var attempt = 0; attempt < 80 && !close.IsFocused; attempt++)
        { input.Key(0xff09); await Task.Delay(25); }
        Check(close.IsFocused, "native Tab reaches the selected account Close action");
        input.Key(0xff1b); await Task.Delay(120);
        Check(Find<ScrollViewer>(detailName) is null && Need<Button>(openName).IsFocused,
            "native Escape closes selected details and restores the original long-name account focus");

        var missing = Need<Border>("Account budgets for A Missing MiMo");
        var failed = Need<Border>("Account budgets for B Failed Cloudflare");
        Check(N09QuotaVisible<ProgressBar>(missing).Length == 0 && N09QuotaVisible<ProgressBar>(failed).Length == 0,
            "missing and failed reporting never become zero-percent budget bars");
        Check(N09QuotaVisible<TextBlock>(failed).Any(value => value.Text == "Synthetic reporting denied"),
            "reporting failure has account-local visible explanation");
        var missingOpen = Need<Button>("Open account details for A Missing MiMo");
        await NativePress(input, missingOpen);
        var missingDetail = Need<ScrollViewer>("Account detail content for A Missing MiMo");
        Check(N09QuotaVisible<ProgressBar>(missingDetail).Length == 0
            && N09QuotaVisible<TextBlock>(missingDetail).Any(value => value.Text?.Contains("Synthetic quota unavailable", StringComparison.Ordinal) == true),
            "missing reporting remains explainable in selected details without an invented measured value");
        Check(Fits(Need<Button>("Close account details for A Missing MiMo")), "missing-reporting details remain closable");
        Capture(FlowImage("missing-reporting-selected"));
        await NativePress(input, Need<Button>("Close account details for A Missing MiMo"));
        Check(JsonSerializer.Serialize(fixture.Config) == original && JsonSerializer.Serialize(fixture.QuotaSnapshot()) == quotas
            && Mutations == 0 && fixture.LoginBegins == 0 && fixture.ApplyCalls == 0,
            "quota matrix navigation, modes and keyboard leave stable IDs/config/snapshots unchanged with no provider refresh, login or Apply");
    }

    private async Task N09NativeLevel(NativeInput input, InformationLevel target)
    {
        await NativePress(input, Need<ComboBox>("Information level"));
        input.Key(0xff50);
        for (var index = 0; index < (int)target; index++) input.Key(0xff54);
        input.Key(0xff0d); await Task.Delay(120);
        Check(Need<ComboBox>("Information level").SelectedIndex == (int)target,
            "native mode selection commits " + target);
        level = target;
    }

    private void CheckN09QuotaShell()
    {
        var mode = Need<ComboBox>("Information level");
        var bounds = Bounds(mode);
        Check(Controls<ComboBox>().Count(value => AutomationProperties.GetName(value) == "Information level") == 1
            && Fits(mode) && bounds.Left > window.ClientSize.Width / 2 && bounds.Bottom > window.ClientSize.Height - 45,
            "one real information selector remains visible at bottom right");
        Check(Fits(Need<Button>("Back")) && Fits(Need<Button>("Settings")), "header Back and Settings remain within the actual viewport");
    }

    private Border[] N09QuotaCards() => Controls<Border>().Where(value => AutomationProperties.GetName(value)?.StartsWith("Account budgets for ", StringComparison.Ordinal) == true).ToArray();

    private void CheckN09QuotaGeometry(string stage)
    {
        var cards = N09QuotaCards();
        Check(cards.All(value => value.Bounds.Width > 0 && value.Bounds.Height > 0), "quota cards retain positive physical bounds: " + stage);
        Check(cards.SelectMany((left, index) => cards.Skip(index + 1).Select(right => (a: Bounds(left), b: Bounds(right))))
            .All(pair => Math.Min(pair.a.Right, pair.b.Right) - Math.Max(pair.a.Left, pair.b.Left) <= 1
                || Math.Min(pair.a.Bottom, pair.b.Bottom) - Math.Max(pair.a.Top, pair.b.Top) <= 1),
            "physical account budget cards do not overlap: " + stage);
        geometry.Add(new { currentCase, stage, mode = fixture.Config.RuntimeMode, information = level.ToString(),
            width = window.ClientSize.Width, height = window.ClientSize.Height, scale = window.RenderScaling,
            fixture = "quota-hierarchy-seven-accounts-long-custom-name", savedAccountIds = fixture.Config.Accounts.Select(value => value.Id).ToArray(),
            cards = cards.Select(value => new { name = AutomationProperties.GetName(value), bounds = Bounds(value).ToString(), visibleInViewport = IntersectsViewport(value) }).ToArray() });
    }

    private static T[] N09QuotaVisible<T>(Control root) where T : Control => root.GetVisualDescendants().OfType<T>()
        .Where(value => Shown(value) && value.GetVisualAncestors().OfType<Expander>().All(expander => expander.IsExpanded
            || expander.Content is not Control content || (!ReferenceEquals(value, content) && !value.GetVisualAncestors().Contains(content))))
        .ToArray();
}
