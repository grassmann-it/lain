using System.Net.Http;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Lain.Client;
using Lain.Core;
using Lain.Desktop;

internal sealed partial class Scenario
{
    public async Task RunMonitorTableSmoke()
    {
        Directory.CreateDirectory(Output);
        fixture.SetMonitor(true);
        fixture.Config = fixture.Config with { Routes = [], Profiles = [], Accounts = fixture.Config.Accounts.Select(account =>
            account.Id == "a5" ? account with { Name = "Very long monitor account name that wraps while refresh and account actions stay reachable" } : account).ToArray() };
        fixture.AddDensityAccounts(35);
        var observed = DateTimeOffset.UtcNow.AddHours(-1);
        QuotaDto Window(string id, string label, double? used, double? limit, double? remaining, string unit = "credits") =>
            new(id, null, "fixture#" + label, "ok", observed, observed, DateTimeOffset.UtcNow.AddDays(3), unit, used, limit, remaining, false, WindowLabel: label);
        fixture.QuotaOverride = [
            Window("a1", "7 days", 37, 100, 63, "percent"),
            Window("a2", "5 hours", 4, 20, 16),
            Window("a2", "Weekly", 22, 40, 18) with { State = "stale", Error = "Synthetic weekly refresh failed" },
            Window("a2", "Monthly", 20, 80, 60),
            Window("a2", "Purchased credits", null, null, 12) with { MeasurementKind = "balance", ResetsAt = null },
            Window("a2", "Free credits", null, null, 0) with { MeasurementKind = "balance", ResetsAt = null },
            Window("a2", "Unknown window", null, null, null) with { State = "unknown", ResetsAt = null },
            Window("a3", "Relative weekly", null, null, null) with { UsedPercent = 72 },
            Window("a4", "Monthly total", 20, 100, 80) with { Scope = "account", ScopeId = "synthetic-account", ResetsAt = DateTimeOffset.UtcNow.AddHours(-2) },
            Window("a4", "Monthly coding", 5, 100, 95) with { Scope = "model", Model = "synthetic-coding" }
        ];
        var http = new HttpClient(fixture, disposeHandler: false) { BaseAddress = new Uri("http://fixture.invalid") };
        transports.Add(http);
        window = new MainWindow(new LainApiClient(http), Path.Combine(Output, "uxd18-monitor-preferences.json")) { Width = 1120, Height = 780 };
        lifetime.MainWindow = window; window.Show();
        await Wait(() => Controls<StackPanel>().Any(control => AutomationProperties.GetName(control) == "Monitor quota table"), "Monitor selects its table inventory");
        Check(!Controls<Control>().Any(control => control.GetType().Name == "MasonryPanel"), "Monitor uses account groups and quota rows rather than Gateway masonry");
        var command = Named<Border>("Account budgets for Command Code Pro");
        var windowRows = QuotaHierarchyVisible<Grid>(command).Where(grid => AutomationProperties.GetName(grid)?.StartsWith("Monitor quota window for ", StringComparison.Ordinal) == true).ToArray();
        Check(windowRows.Length == 2 && QuotaHierarchyVisible<ProgressBar>(command).Length == 2,
            "Monitor prioritizes the two short Command Code windows while additional budgets are collapsed");
        Named<Expander>("Additional budgets for Command Code Pro").IsExpanded = true; await Task.Delay(100);
        Check(QuotaHierarchyVisible<Grid>(command).Count(grid => AutomationProperties.GetName(grid)?.StartsWith("Monitor quota window for ", StringComparison.Ordinal) == true) == 5
            && QuotaHierarchyVisible<ProgressBar>(command).Length == 3
            && QuotaHierarchyVisible<TextBlock>(command).Any(text => text.Text == "12 credits left")
            && !QuotaHierarchyVisible<TextBlock>(command).Any(text => text.Text == "Free credits"),
            "Monitor expansion preserves monthly, positive credit and unknown originals without a zero additional-credit row");
        await Click("Open account details for Command Code Pro");
        var originalDetails = Named<ScrollViewer>("Account detail content for Command Code Pro");
        Check(QuotaHierarchyVisible<TextBlock>(originalDetails).Any(text => text.Text == "Free credits")
            && QuotaHierarchyVisible<TextBlock>(originalDetails).Any(text => text.Text == "0 credits left"),
            "Monitor Inspector retains the zero free-credit original without inventing a percentage");
        await Click("Close account details for Command Code Pro");
        Named<Expander>("Additional budgets for Kimi Coding").IsExpanded = true; await Task.Delay(100);
        var relative = Named<Grid>("Monitor quota window for Z.ai Coding · Relative weekly");
        Check(relative.GetVisualDescendants().OfType<ProgressBar>().Single().Value == 72, "Monitor retains a valid reported percent without absolute amounts");
        Check(HasText("Reset due · refresh to check") && HasText("Scope: account · synthetic-account")
            && HasText("Scope: model · synthetic-coding") && HasText("Synthetic weekly refresh failed"),
            "Monitor retains unconfirmed reset, scope and account-local quota error facts");

        foreach (var (width, height) in new[] { (1120, 780), (800, 560) })
        {
            Check(await ResizeViewport(width, height), "Monitor requested viewport is available");
            command.BringIntoView(); await Task.Delay(80); window.UpdateLayout();
            var list = Named<ScrollViewer>("Account inventory");
            var listWidth = list.Bounds.Width; var listOffset = list.Offset; var rowBounds = command.Bounds;
            var bars = command.GetVisualDescendants().OfType<ProgressBar>().ToArray();
            Check(bars.All(bar => bar.Minimum == 0 && bar.Maximum == 100 && !bar.IsIndeterminate)
                && bars.Max(bar => bar.Bounds.Width) - bars.Min(bar => bar.Bounds.Width) < 1,
                "Monitor consumption bars keep equal widths and the same 0–100 scale at " + width);
            foreach (var bar in bars)
            {
                var consumption = (StackPanel)bar.Parent!;
                Check(bar.Bounds.Top >= consumption.Children[0].Bounds.Bottom && bar.Bounds.Width >= 150,
                    "Monitor bar stays long and below the numbers at " + width);
                if (width == 800) Check(Grid.GetRow(consumption) == 1,
                    "340px Monitor inventory stacks consumption and reset rather than shortening the bar inline");
            }
            await Click("Open account details for Command Code Pro");
            Check(list.Bounds.Width == listWidth && list.Offset == listOffset && command.Bounds == rowBounds,
                "Monitor detail opening preserves inventory geometry and its independent scroll at " + width);
            SaveWindowImage("uxd18-monitor-selected-" + width + "x" + height + ".png");
            await Click("Close account details for Command Code Pro");
            Check(list.Bounds.Width == listWidth && list.Offset == listOffset && command.Bounds == rowBounds,
                "Monitor detail closing preserves inventory geometry and scroll at " + width);
            SaveWindowImage("uxd18-monitor-table-" + width + "x" + height + ".png");
        }
        await ResizeViewport(800, 560);
        var quotaBars = command.GetVisualDescendants().OfType<ProgressBar>().ToArray();
        var pending = fixture.Defer("POST", "/api/v1/quotas/refresh");
        await Click("Refresh quota for Command Code Pro"); await pending.Entered.Task;
        Check(quotaBars.All(bar => TopLevel.GetTopLevel(bar) == window)
            && command.GetVisualDescendants().OfType<ProgressBar>().SequenceEqual(quotaBars),
            "Monitor local refresh keeps its quota controls and old values mounted");
        await Click("Open account details for Z.ai Coding");
        var inspectedB = Named<ScrollViewer>("Account detail content for Z.ai Coding");
        pending.Fail("Synthetic monitor refresh failure");
        await Wait(() => HasText("Refresh failed · showing last known values"), "Monitor refresh failure stays on requested account");
        Check(ReferenceEquals(inspectedB, Named<ScrollViewer>("Account detail content for Z.ai Coding"))
            && command.GetVisualDescendants().OfType<ProgressBar>().SequenceEqual(quotaBars),
            "Monitor A failure retains quota controls and the mounted B inspector");
        var longName = fixture.Config.Accounts.Single(account => account.Id == "a5").Name;
        var longRow = Named<Border>("Account budgets for " + longName); longRow.BringIntoView();
        await Task.Delay(80); window.UpdateLayout(); SaveWindowImage("uxd18-monitor-long-name-800x560.png");
        Check(FitsScrollViewports(Named<Button>("Open account details for " + longName))
            && FitsScrollViewports(Controls<Button>().First(button => AutomationProperties.GetName(button) == "Refresh quota for " + longName)),
            "Monitor long name and refresh remain inside the 35-account inventory viewport");
        Check(fixture.LoginBegins == 0 && fixture.ApplyCalls == 0 && fixture.Unexpected.Count == 0,
            "Monitor layout fixture uses synthetic observations without login or apply");
        await File.WriteAllTextAsync(Path.Combine(Output, "uxd18-monitor-evidence.json"), JsonSerializer.Serialize(new {
            checks, requests = fixture.Requests, requestDetails = fixture.RequestDetails,
            inputMethod = "Production command invocation; native input is UXD-22", visualResult = "NOT_REVIEWED"
        }, new JsonSerializerOptions { WriteIndented = true }));
        window.Close(); foreach (var transport in transports) transport.Dispose();
    }
}
