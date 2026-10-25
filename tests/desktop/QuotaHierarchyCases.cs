using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Lain.Client;
using Lain.Desktop;

internal sealed partial class Scenario
{
    // Native input is confined by the shared XTEST helper to runner-owned Xvfb and D-Bus.
    public async Task RunQuotaHierarchySmoke()
    {
        Directory.CreateDirectory(Output);
        using var native = new NativeInput();
        var input = new List<string>();
        NativeInput.Trace = input.Add;
        fixture.UseQuotaHierarchyInventory();
        fixture.SetMonitor(false);
        var initialConfiguration = JsonSerializer.Serialize(fixture.Config);
        var originals = fixture.QuotaSnapshot();
        var labels = new[] { "5 hours", "7 hours", "7 days", "Monthly", "Purchased credits", "Free credits" };

        async Task Activate(Control control)
        {
            control.BringIntoView(); await Task.Delay(100); window.UpdateLayout();
            Check(FitsScrollViewports(control), "native target is inside its scroll viewport: " + (AutomationProperties.GetName(control) ?? control.GetType().Name));
            native.Focus(window); native.Click(control); await Task.Delay(160);
        }
        Task Press(string name) => Activate(Controls<Button>().First(button =>
            (AutomationProperties.GetName(button) == name || Equals(button.Content, name)) && button.IsEnabled));
        Border Card(string name) => Named<Border>("Account budgets for " + name);
        Expander More() => Named<Expander>("Additional budgets for C Command Code");
        ToggleButton MoreHeader() => More().GetVisualDescendants().OfType<ToggleButton>().First();
        string[] Windows(Control control) => QuotaHierarchyVisible<TextBlock>(control)
            .Select(text => text.Text).Where(text => text is not null && labels.Contains(text)).Cast<string>().ToArray();
        Border[] Cards() => Controls<Border>().Where(card => AutomationProperties.GetName(card)?.StartsWith("Account budgets for ", StringComparison.Ordinal) == true).ToArray();

        foreach (var monitor in new[] { false, true })
        {
            fixture.SetMonitor(monitor);
            fixture.Scenario = "quota-hierarchy-" + (monitor ? "monitor" : "gateway");
            var mode = monitor ? "monitor" : "gateway";
            var http = new HttpClient(fixture, disposeHandler: false) { BaseAddress = new Uri("http://fixture.invalid") };
            transports.Add(http);
            window = new MainWindow(new LainApiClient(http), Path.Combine(Output, "quota-hierarchy-" + mode + "-preferences.json"))
                { Width = 1120, Height = 780 };
            lifetime.MainWindow = window; window.Show();
            await Wait(() => Cards().Length == 7, mode + " loads all synthetic accounts");
            Check(monitor == Controls<StackPanel>().Any(panel => AutomationProperties.GetName(panel) == "Monitor quota table"), mode + " uses its intended inventory presentation");

            foreach (var (width, height) in new[] { (1120, 780), (800, 560) })
            {
                Check(await ResizeViewport(width, height), mode + " viewport " + width);
                var command = Card("C Command Code");
                Check(!More().IsExpanded && Windows(command).SequenceEqual(new[] { "5 hours", "7 hours" }),
                    mode + " " + width + " first look prioritizes 5h then 7h despite reversed API input");
                Check(QuotaHierarchyVisible<ProgressBar>(command).Select(bar => bar.Value).SequenceEqual(new[] { 15d, 25d }),
                    mode + " " + width + " first look has exactly two genuine consumption bars");
                Check(Windows(Card("E Monthly")).SequenceEqual(new[] { "Monthly" })
                    && QuotaHierarchyVisible<ProgressBar>(Card("E Monthly")).Single().Value == 60,
                    mode + " " + width + " monthly-only account keeps its main reading");
                Check(QuotaHierarchyVisible<TextBlock>(Card("F Empty wallet")).Any(text => text.Text == "0 credits left")
                    && QuotaHierarchyVisible<ProgressBar>(Card("F Empty wallet")).Length == 0,
                    mode + " " + width + " balance-only real zero is retained without an invented percent");
                Check(QuotaHierarchyVisible<ProgressBar>(Card("G Stale measured")).Single().Value == 70
                    && QuotaHierarchyVisible<TextBlock>(Card("G Stale measured")).Any(text => text.Text?.Contains("Last known", StringComparison.OrdinalIgnoreCase) == true),
                    mode + " " + width + " stale measured reading retains value and state");
                var measured = new[] { "C Command Code", "D Weekly", "E Monthly", "F Empty wallet", "G Stale measured" }.Select(Card).ToArray();
                var missing = new[] { "A Missing MiMo", "B Failed Cloudflare" }.Select(Card).ToArray();
                var inventory = Named<ScrollViewer>("Account inventory");
                Check(measured.Max(card => card.TranslatePoint(new Point(0, card.Bounds.Height), inventory)!.Value.Y)
                    <= missing.Min(card => card.TranslatePoint(default, inventory)!.Value.Y),
                    mode + " " + width + " unavailable accounts occupy a physical block below every measured account");
                Check(HasText("Quota not available") && missing.All(card => QuotaHierarchyVisible<ProgressBar>(card).Length == 0)
                    && QuotaHierarchyVisible<TextBlock>(Card("B Failed Cloudflare")).Any(text => text.Text == "Synthetic reporting denied"),
                    mode + " " + width + " unknown and failed reads remain available without being represented as zero");
                command.BringIntoView(); await Task.Delay(100); window.UpdateLayout();
                SaveWindowImage("quota-hierarchy-" + mode + "-collapsed-" + width + ".png");
                await Activate(MoreHeader());
                await Wait(() => More().IsExpanded, mode + " native pointer expands additional budgets");
                Check(Windows(command).SequenceEqual(labels.Take(5)) && !Windows(command).Contains("Free credits"),
                    mode + " expanded additional windows retain 7d, monthly and positive credits in order; zero extras stay in Inspector");
                Check(QuotaHierarchyVisible<TextBlock>(command).Any(text => text.Text == "12 credits left"), mode + " positive purchased balance remains accessible");
                SaveWindowImage("quota-hierarchy-" + mode + "-expanded-" + width + ".png");
                // Pointer focuses the real header; Space now exercises native keyboard activation.
                native.Key(0x20); await Wait(() => !More().IsExpanded, mode + " native Space collapses additional budgets");
            }
            await ResizeViewport(1120, 780);
            await Activate(MoreHeader());
            await Wait(() => More().IsExpanded, mode + " expands before refresh/navigation");
            var commandBefore = Card("C Command Code");
            var orderBefore = Cards().Select(AutomationProperties.GetName).ToArray();
            var pending = fixture.Defer("POST", "/api/v1/quotas/refresh");
            await Press("Refresh quota for C Command Code");
            await Wait(() => pending.Entered.Task.IsCompleted, mode + " account-local refresh reaches fixture");
            Check(More().IsExpanded && ReferenceEquals(commandBefore, Card("C Command Code")), mode + " busy retains expansion and card identity");
            await Press("Open account details for D Weekly");
            var weeklyInspector = Named<ScrollViewer>("Account detail content for D Weekly");
            var inventoryBefore = Named<ScrollViewer>("Account inventory");
            var offsetBefore = inventoryBefore.Offset;
            pending.Complete(originals.Select(quota => quota.AccountId == "qh-command" && quota.WindowLabel == "5 hours"
                ? quota with { Used = 16, Remaining = 84, UsedPercent = 16 } : quota).ToArray());
            await Wait(() => QuotaHierarchyVisible<ProgressBar>(Card("C Command Code")).First().Value == 16, mode + " completed refresh updates requested account");
            Check(More().IsExpanded && ReferenceEquals(commandBefore, Card("C Command Code"))
                && Cards().Select(AutomationProperties.GetName).SequenceEqual(orderBefore)
                && ReferenceEquals(weeklyInspector, Named<ScrollViewer>("Account detail content for D Weekly"))
                && inventoryBefore.Offset == offsetBefore,
                mode + " refresh preserves expanded state, inventory order, selected other Inspector and scroll");
            await Press("Close account details for D Weekly");
            await Press("Settings"); await Press("Accounts");
            await Wait(() => Cards().Length == 7, mode + " returns to accounts");
            Check(More().IsExpanded && !Controls<Expander>().Any(expander => AutomationProperties.GetName(expander) == "Additional budgets for D Weekly"),
                mode + " expansion persists across navigation independently for this account");
            await Press("Open account details for C Command Code");
            var inspector = Named<ScrollViewer>("Account detail content for C Command Code");
            Check(Windows(inspector).SequenceEqual(labels) && QuotaHierarchyVisible<TextBlock>(inspector).Any(text => text.Text == "0 credits left"),
                mode + " Inspector retains all six originals including zero credits in short-to-long order");
            var technical = inspector.GetVisualDescendants().OfType<Expander>().Single(expander => Equals(expander.Header, "Technical details"));
            await Activate(technical.GetVisualDescendants().OfType<ToggleButton>().First());
            await Wait(() => technical.IsExpanded, mode + " native pointer opens technical originals");
            var sources = QuotaHierarchyVisible<SelectableTextBlock>(technical).Select(text => text.Text).ToArray();
            Check(originals.Where(quota => quota.AccountId == "qh-command").All(quota => sources.Contains(quota.Source))
                && sources.Any(text => text?.Contains("remaining: 0 credits", StringComparison.Ordinal) == true),
                mode + " technical details preserve each source and the original zero amount");
            SaveWindowImage("quota-hierarchy-" + mode + "-technical-originals.png");
            window.Close();
        }
        fixture.SetMonitor(false);
        Check(JsonSerializer.Serialize(fixture.Config) == initialConfiguration && fixture.QuotaSnapshot().SequenceEqual(originals),
            "presentation does not mutate account configuration or original quota snapshots");
        Check(fixture.Unexpected.Count == 0 && fixture.Requests.Where(request => !request.StartsWith("GET ", StringComparison.Ordinal))
            .SequenceEqual(new[] { "POST /api/v1/quotas/refresh", "POST /api/v1/quotas/refresh" }),
            "only the two explicitly requested synthetic quota reads use POST; no account, login, export or apply mutations");
        await File.WriteAllTextAsync(Path.Combine(Output, "quota-hierarchy-evidence.json"), JsonSerializer.Serialize(new {
            result = "PASS", checks, input, screenshots, requests = fixture.Requests, requestDetails = fixture.RequestDetails,
            scope = "Production MainWindow with synthetic HttpMessageHandler; native XTEST pointer/Space on private Xvfb and D-Bus. Rendered screenshots require visual review. No provider or user-state access."
        }, new JsonSerializerOptions { WriteIndented = true }));
        NativeInput.Trace = null;
        foreach (var transport in transports) transport.Dispose();
    }

    // Expander template content can remain in the visual tree while collapsed.
    // Count displayed quota data, not merely descendants or offscreen viewport pixels.
    private static T[] QuotaHierarchyVisible<T>(Control root) where T : Control => root.GetVisualDescendants().OfType<T>()
        .Where(control => IsShown(control) && control.GetVisualAncestors().OfType<Expander>().All(expander =>
            expander.IsExpanded || expander.Content is not Control content ||
            (!ReferenceEquals(control, content) && !control.GetVisualAncestors().Contains(content))))
        .ToArray();
}
