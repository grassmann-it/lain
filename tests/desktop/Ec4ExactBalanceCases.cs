using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Lain.Client;
using Lain.Core;
using Lain.Desktop;

internal sealed partial class Scenario
{
    public async Task RunEc4ExactBalanceCases()
    {
        Directory.CreateDirectory(Output);
        var source = fixture.Config.Accounts[0];
        var accounts = new[] { "A DeepSeek balances", "B Exact zero", "C Tiny positive", "D Retained balance" }
            .Select((name, index) => source with { Id = "exact-" + index, Name = name, Provider = "deepseek", SharedWithLegacy = false }).ToArray();
        fixture.Config = fixture.Config with { Accounts = accounts, Routes = [], Profiles = [] };
        var now = DateTimeOffset.UtcNow;
        const string usd = "12345678901234567890.12345678901234567890";
        const string cny = "7.00000000000000000001";
        const string tiny = "0.0000000000000000000000000000000000000001";
        QuotaDto Balance(int index, string exact, string unit, string label) => new(accounts[index].Id, null,
            "fixture#" + unit, "ok", now, now, null, unit, null, null, null, false,
            WindowLabel: label, MeasurementKind: "balance", ExactRemaining: exact);
        fixture.QuotaOverride =
        [
            Balance(0, usd, "USD", "USD balance"), Balance(0, cny, "CNY", "CNY balance"),
            Balance(1, "0.0000", "USD", "Balance"),
            Balance(2, tiny, "USD", "Balance") with { Remaining = 0 },
            Balance(3, "19.00000000000000000001", "CNY", "Balance") with
            {
                State = "unavailable", LastSuccessAt = now.AddHours(-1),
                Support = new("unavailable", "Synthetic retained observation")
            }
        ];
        var originalConfiguration = JsonSerializer.Serialize(fixture.Config);
        var requestedScale = double.Parse(Environment.GetEnvironmentVariable("LAIN_TEST_SCALE") ?? "1", CultureInfo.InvariantCulture);
        string[] Texts(Control control) => control.GetVisualDescendants().OfType<TextBlock>().Where(IsShown)
            .Select(block => block.Text ?? "").ToArray();
        Border Card(int index) => Named<Border>("Account budgets for " + accounts[index].Name);
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic;
        var observedScales = new List<double>();
        foreach (var monitor in new[] { false, true })
        {
            fixture.SetMonitor(monitor);
            var mode = monitor ? "monitor" : "gateway";
            var http = new HttpClient(fixture, disposeHandler: false) { BaseAddress = new("http://fixture.invalid") };
            transports.Add(http);
            window = new MainWindow(new LainApiClient(http), Path.Combine(Output, "exact-balance-" + mode + ".json")) { Width = 1120, Height = 780 };
            lifetime.MainWindow = window; window.Show();
            await Wait(() => AccountTileNames().Length == accounts.Length, mode + " exact-balance accounts mounted");
            Grid MonitorWindow(int index, string label) => Card(index).GetVisualDescendants().OfType<Grid>().Single(grid =>
                AutomationProperties.GetName(grid) == "Monitor quota window for " + accounts[index].Name + " · " + label);
            Control Consumption(int index, string label) => monitor ? MonitorWindow(index, label).Children[1] : Card(index);
            var overage = Balance(0, "0", "percent", "Subscription") with
            { MeasurementKind = null, ExactRemaining = null, UsedPercent = 125 };
            Check((double?)typeof(MainWindow).GetMethod("QuotaUsedPercent", statics)!.Invoke(null, [overage]) == 125,
                mode + " provider-reported subscription overage remains visible");
            var overageRow = (Control)typeof(MainWindow).GetMethod("BudgetWindowRow", instance)!.Invoke(window, [accounts[0], overage, null])!;
            Check(overageRow.GetLogicalDescendants().OfType<ProgressBar>().Single().Value == 100
                && overageRow.GetLogicalDescendants().OfType<TextBlock>().Any(text => text.Text?.Contains("125") == true)
                && !overageRow.GetLogicalDescendants().OfType<TextBlock>().Any(text => text.Text?.Contains("-25") == true),
                mode + " overage bar stays bounded and never invents negative remaining quota");
            observedScales.Add(window.RenderScaling);
            Check(Math.Abs(window.RenderScaling - requestedScale) < .001, mode + " actual exact-balance render scale matches request");
            // Monitor rows have a separate reset column. Its honest unknown-reset
            // text must not be mistaken for a missing consumption/balance value.
            Check(Texts(Consumption(0, "USD balance")).Contains(usd + " USD left") && !Texts(Consumption(0, "USD balance")).Any(text =>
                text.Contains("not supplied", StringComparison.OrdinalIgnoreCase) || text.Contains("not available", StringComparison.OrdinalIgnoreCase)),
                mode + " exact-only main balance is visible on first look without contradictory missing-value text");
            if (monitor)
                Check(Texts(MonitorWindow(0, "USD balance").Children[2]).Contains("Not supplied"),
                    "monitor unknown reset remains separate from the measured exact balance");
            var zero = fixture.QuotaOverride!.Single(quota => quota.AccountId == accounts[1].Id);
            Check(Texts(Consumption(1, "Balance")).Contains("0.0000 USD left")
                && (bool)typeof(MainWindow).GetMethod("QuotaExhausted", statics)!.Invoke(null, [zero])!
                && (monitor || Texts(Card(1)).Any(text => text.Contains("Balance empty", StringComparison.Ordinal))),
                mode + " real exact zero remains visible and classified empty; the Gateway also shows its empty badge");
            Check(Texts(Card(2)).Contains(tiny + " USD left") && !Texts(Card(2)).Any(text => text.Contains("empty", StringComparison.OrdinalIgnoreCase)),
                mode + " positive native decimal takes precedence over a binary zero without false exhaustion");
            Check(Texts(Card(3)).Contains("19.00000000000000000001 CNY left")
                && Texts(Card(3)).Any(text => text.Contains("Last known", StringComparison.OrdinalIgnoreCase))
                && !Texts(Card(3)).Contains("Synthetic retained observation"),
                mode + " retained exact-only value keeps its currency and freshness instead of becoming support-only");
            Check(Controls<ProgressBar>().Length == 0, mode + " exact monetary balances create no percentages or synthetic limits");
            var more = Named<Expander>("Additional budgets for " + accounts[0].Name);
            more.IsExpanded = true;
            await Wait(() => Texts(Card(0)).Contains(cny + " CNY left"), mode + " second native currency remains separate in additional budgets");
            Check(Texts(Card(0)).Contains(usd + " USD left") && Texts(Card(0)).Contains(cny + " CNY left"),
                mode + " currencies are displayed independently without conversion or aggregation");
            await CaptureBoth("ec4-exact-balance-" + mode, () => Card(0).BringIntoView());

            var tableRow = (Control)typeof(MainWindow).GetMethod("BudgetWindowRow", instance)!.Invoke(window,
                [accounts[0], fixture.QuotaOverride![0], null])!;
            var tableTexts = tableRow.GetLogicalDescendants().OfType<TextBlock>().Select(block => block.Text).ToArray();
            Check(tableTexts.Contains(usd + " USD left") && !tableTexts.Contains("Usage not supplied"),
                mode + " table row uses the same exact native amount without an approximate numeric fallback");
            var details = (Control)typeof(MainWindow).GetMethod("QuotaDetails", statics)!.Invoke(null, [fixture.QuotaOverride![0]])!;
            Check(details.GetLogicalDescendants().OfType<SelectableTextBlock>().Any(block =>
                block.Text?.Contains("remaining: " + usd + " USD", StringComparison.Ordinal) == true),
                mode + " technical amount details agree with the displayed exact balance");
            var invalid = Balance(0, "1\n", "USD", "Invalid exact input") with
            { Support = new("setup-required", "Synthetic setup guidance") };
            Check(!QuotaPresentation.HasReading(invalid)
                && (bool)typeof(MainWindow).GetMethod("QuotaUnavailable", statics)!.Invoke(null, [invalid])!,
                mode + " invalid whitespace-containing exact text cannot manufacture a valid balance reading");
            var negative = Balance(0, "-0.00000000000000000001", "USD", "Negative balance");
            Check((bool)typeof(MainWindow).GetMethod("QuotaExhausted", statics)!.Invoke(null, [negative])!,
                mode + " exact negative balance is classified without losing its sign or precision");
            window.Close();
        }
        Check(fixture.Config.Accounts.SequenceEqual(accounts) && fixture.Config.Routes.Length == 0 && fixture.Config.Profiles.Length == 0
            && fixture.Requests.All(request => request.StartsWith("GET ", StringComparison.Ordinal)) && fixture.Unexpected.Count == 0,
            "exact-balance display performs no mutation, login, quota refresh, inference or profile action");
        await File.WriteAllTextAsync(Path.Combine(Output, "ec4-exact-balance-evidence.json"), JsonSerializer.Serialize(new
        {
            result = "PASS", checks, screenshots, requestedScale, observedScales, requests = fixture.Requests,
            originalConfiguration,
            loadedAssemblies = new[] { typeof(MainWindow).Assembly, typeof(LainApiClient).Assembly, typeof(QuotaDto).Assembly }
                .Select(assembly => new { name = assembly.GetName().Name, path = assembly.Location,
                    sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant() }),
            scope = "Synthetic production Desktop Gateway/Monitor balance presentation; no external provider, real host, credentials or user desktop. Exact decimal strings are never converted to binary values."
        }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var transport in transports) transport.Dispose();
    }
}
