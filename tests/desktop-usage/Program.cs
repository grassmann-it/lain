using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lain.Client;
using Lain.Core;
using Lain.Desktop;
using Lain.Desktop.Presentation;
using Lain.Desktop.Usage;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        NativeInput.AssertPrivateDisplay();
        return AppBuilder.Configure<UsageTestApp>().UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime(args);
    }
}
internal sealed class UsageTestApp : Application
{
    public override void Initialize()
    {
        var production = new App(); production.Initialize(); RequestedThemeVariant = production.RequestedThemeVariant;
        var resources = production.Resources; production.Resources = new ResourceDictionary(); Resources = resources;
        var styles = production.Styles.ToArray(); production.Styles.Clear(); foreach (var style in styles) Styles.Add(style);
    }
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Dispatcher.UIThread.Post(async () =>
        {
            try { await Scenario.Run(lifetime); lifetime.Shutdown(0); }
            catch (Exception error) { Console.Error.WriteLine(error); lifetime.Shutdown(1); }
        });
        base.OnFrameworkInitializationCompleted();
    }
}
internal static class Scenario
{
    private static int checks;
    private static string Output => Environment.GetEnvironmentVariable("LAIN_TEST_OUTPUT")!;
    private static IEnumerable<T> Controls<T>(Visual root) where T : Control => root.GetVisualDescendants().OfType<T>();
    private static T Named<T>(Visual root, string name) where T : Control => Controls<T>(root).Single(x => AutomationProperties.GetName(x) == name);
    private static string Text(Visual root) => string.Join("\n", Controls<TextBlock>(root).Select(x => x.Text));
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("FAIL: " + message);
        checks++; Console.WriteLine("PASS: " + message);
    }
    private static async Task Wait(Func<bool> condition, string message)
    {
        for (var i = 0; i < 150; i++) { if (condition()) return; await Task.Delay(50); }
        throw new TimeoutException(message);
    }
    private static async Task Click(NativeInput input, Control control)
    {
        control.BringIntoView(); await Task.Delay(100); input.Click(control); await Task.Delay(160);
    }
    private static async Task Apply(UsageView view, UsageFixture fixture)
    {
        var reads = fixture.Data.AnalysisCalls;
        Controls<Button>(view).Single(x => Equals(x.Content, "Apply")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Wait(() => fixture.Data.AnalysisCalls > reads, "analysis refresh"); await Task.Delay(120);
    }
    private static string Selected(UsageView view) => Named<TextBlock>(view, "Usage selected group name").Text!;
    private static void Capture(Window window, string file)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height), new Vector(96, 96));
        bitmap.Render(window); bitmap.Save(Path.Combine(Output, file));
    }
    private static void CheckColumns(UsageView view, Window window, string label)
    {
        var table = Named<ScrollViewer>(view, "Usage table scroll");
        var detail = Named<ScrollViewer>(view, "Usage detail scroll");
        var left = table.TranslatePoint(default, window)!.Value;
        var right = detail.TranslatePoint(default, window)!.Value;
        Check(right.X >= left.X + table.Bounds.Width && Math.Abs(left.Y - right.Y) < 2,
            label + " table and side detail remain adjacent without overlap");
        Check(table.Bounds.Height >= 230 && detail.Bounds.Height >= 230 && right.X + detail.Bounds.Width <= window.Bounds.Width,
            label + " bounded independent panes fit the viewport");
        Check(!Controls<Expander>(view).Any(x => (AutomationProperties.GetName(x) ?? "").StartsWith("Analysis group ")),
            label + " breakdown contains no inline group expanders");
    }
    public static async Task Run(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        using var fixture = new UsageFixture();
        using var http = new HttpClient(fixture) { BaseAddress = new Uri("http://fixture.invalid") };
        var view = new UsageView(new LainApiClient(http));
        var window = new Window { Width = 1120, Height = 780, Content = view, Padding = new Thickness(20), Title = "Private Usage qualification" };
        lifetime.MainWindow = window; window.Show();
        using var input = new NativeInput(); input.Focus(window);
        NativeInput.Trace = value => Console.WriteLine("INPUT: " + value);
        await Wait(() => Controls<TextBlock>(view).Any(x => AutomationProperties.GetName(x) == "Usage selected group name"), "initial selected group");
        await Task.Delay(200);
        CheckColumns(view, window, "1120×780");
        var row = Named<Grid>(view, "Analysis group accountId Kimi");
        var rowHeight = row.Bounds.Height;
        await Click(input, Named<Button>(view, "Select analysis group accountId Kimi"));
        Check(Selected(view) == "Kimi" && Math.Abs(row.Bounds.Height - rowHeight) < 1,
            "native pointer selects Kimi in the side detail without expanding its row");
        Check(Named<StackPanel>(view, "Usage selected group detail").GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text == "accountId: a4"),
            "side detail is bound to the evidenced stable account identity");
        var query = JsonSerializer.Serialize(fixture.Data.UsageQueries.Last());
        var command = Named<Button>(view, "Select analysis group accountId Command Code");
        command.Focus(); input.Key(0x20); await Task.Delay(160);
        Check(Selected(view) == "Command Code" && JsonSerializer.Serialize(fixture.Data.UsageQueries.Last()) == query,
            "native Space selects a row without applying a query");
        Check(Text(Named<StackPanel>(view, "Usage performance")).Contains("120 / 132 measured")
            && Text(Named<StackPanel>(view, "Usage performance")).Contains("P50 240 ms"),
            "whole-range first output keeps its 120/132 population after selection");
        Capture(window, "usage-wide.png");

        window.Width = 800; window.Height = 560; await Task.Delay(200);
        foreach (var density in new[] { ViewDensity.Normal, ViewDensity.Compact })
        {
            view.SetPresentation(new(InformationLevel.Simple, density)); await Task.Delay(160);
            CheckColumns(view, window, "800×560 " + density);
            Check(Selected(view) == "Command Code", "density changes keep the selected identity");
        }
        var table = Named<ScrollViewer>(view, "Usage table scroll");
        var detail = Named<ScrollViewer>(view, "Usage detail scroll");
        var tableHorizontal = Named<StackPanel>(view, "Usage breakdown table").GetVisualAncestors().OfType<ScrollViewer>().First();
        tableHorizontal.Offset = new Vector(1000, 0); await Task.Delay(100);
        Check(tableHorizontal.Offset.X > 0 && Controls<TextBlock>(view).Any(x => x.Text == "Completion errors")
            && Controls<TextBlock>(view).Any(x => x.Text == "Reported tokens"), "narrow table preserves all core columns behind horizontal scrolling");
        tableHorizontal.Offset = default;
        Capture(window, "usage-narrow.png");

        fixture.Partial = true; await Apply(view, fixture);
        Check(Text(view).Contains("64,200") || Text(view).Contains("64.200"), "partial fixture reports exactly 64,200 known tokens");
        Check(!Text(view).Contains("81,986") && !Text(view).Contains("81.986"), "partial fixture does not retain the complete 81,986 total");
        await Click(input, Named<Button>(view, "Select analysis group accountId Kimi"));
        Check(Text(Named<StackPanel>(view, "Usage selected group detail")).Contains("—"), "Kimi missing measurement remains unknown");
        Capture(window, "usage-partial-narrow.png");
        await Click(input, Named<Button>(view, "Usage additional filters"));
        var status = Named<TextBox>(view, "HTTP status"); status.BringIntoView(); await Task.Delay(100); input.Click(status); input.Type("503");
        await Wait(() => status.Text == "503", "native draft typing");
        var reads = fixture.Data.UsageCalls;
        await Wait(() => fixture.Data.UsageCalls > reads, "poll while draft focused");
        Check(status.IsFocused && status.Text == "503" && !fixture.Data.UsageQueries.Last().ContainsKey("statusCode") && Selected(view) == "Kimi",
            "polling preserves native input focus, unapplied draft, applied query and selected group");
        await Click(input, Named<Button>(view, "Usage additional filters"));
        detail.Offset = new Vector(0, 80); table.Offset = new Vector(0, 50); await Task.Delay(100);
        var detailOffset = detail.Offset; var tableOffset = table.Offset;
        Check(detailOffset.Y > 0 && tableOffset.Y > 0, "both result panes expose independent scroll ranges");
        var restore = view.CaptureNavigationState();
        window.Content = new TextBlock { Text = "Synthetic account destination" }; await Task.Delay(80);
        window.Content = view; restore();
        await Wait(() => Controls<TextBlock>(view).Any(x => AutomationProperties.GetName(x) == "Usage selected group name"), "navigation restored");
        await Task.Delay(180);
        Check(Selected(view) == "Kimi" && status.Text == "503" && !fixture.Data.UsageQueries.Last().ContainsKey("statusCode"),
            "navigation Back restores selection and keeps draft separate from applied query");
        Check(Math.Abs(detail.Offset.Y - detailOffset.Y) < 2 && Math.Abs(table.Offset.Y - tableOffset.Y) < 2,
            "navigation Back restores both independent scroll offsets");
        fixture.Data.AnalysisFailure = true;
        reads = fixture.Data.UsageCalls; await Wait(() => fixture.Data.UsageCalls > reads, "analysis failure poll"); await Task.Delay(120);
        Check(Text(view).Contains("Last known group measurements") && Selected(view) == "Kimi", "same-scope analysis failure labels retained side detail as last known");
        Capture(window, "usage-source-error.png");
        fixture.Data.AnalysisFailure = false; fixture.OmitKimi = true;
        reads = fixture.Data.UsageCalls; await Wait(() => fixture.Data.UsageCalls > reads, "missing selected group poll"); await Task.Delay(120);
        Check(Text(view).Contains("selected group is not in this result") && !Controls<TextBlock>(view).Any(x => AutomationProperties.GetName(x) == "Usage selected group name"),
            "disappeared group does not silently switch to a different identity");
        fixture.OmitKimi = false; fixture.Regressed = true;
        reads = fixture.Data.UsageCalls; await Wait(() => fixture.Data.UsageCalls > reads, "source regression poll"); await Task.Delay(120);
        Check(Selected(view) == "Kimi" && Text(view).Contains("continuity unconfirmed"), "returning identity is restored while source regression remains explicit");
        var dimension = Named<ComboBox>(view, "Compare by");
        dimension.SelectedItem = dimension.ItemsSource!.Cast<object>().Single(item => item.ToString() == "Route");
        await Wait(() => Controls<TextBlock>(view).Any(x => AutomationProperties.GetName(x) == "Usage selected group name" && x.Text == "gpt-lain-01"), "friendly route selected");
        Check(Selected(view) == "gpt-lain-01" && Text(Named<StackPanel>(view, "Usage selected group detail")).Contains("routeId: r1"),
            "route alias is readable while selected detail retains stable route identity");
        WorkspaceNavigationRequest? destination = null;
        view.NavigateRequested = value => destination = value;
        Named<Button>(view, "Open routeId r1").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(destination?.RouteId == "r1", "friendly route navigation retains the stable ID");
        window.Width = 1120; window.Height = 780;
        view.SetPresentation(new(InformationLevel.Simple, ViewDensity.Normal));
        await Task.Delay(300);
        Check(Named<TextBlock>(view, "Usage selected group name").Bounds.Height > 0,
            "friendly route label has completed native layout before capture");
        Capture(window, "usage-friendly-route.png");
        Check(fixture.Data.Requests.All(x => x.StartsWith("GET ")) && fixture.Data.Unexpected.Count == 0, "qualification made only expected in-memory reads");
        File.WriteAllText(Path.Combine(Output, "result.json"), JsonSerializer.Serialize(new { checks, status = "passed", scope = "Production UsageView and App styles, private Xvfb+DBus, native XTest selection/draft, in-memory HTTP only" }));
        window.Close(); Console.WriteLine($"PASS: {checks} checks");
    }
}
internal sealed class UsageFixture : HttpMessageHandler
{
    public readonly Fixture Data = new() { UxdScenario = true };
    private readonly HttpMessageInvoker inner;
    public bool Partial, OmitKimi, Regressed;
    public UsageFixture() => inner = new(Data);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/api/v1/routes")
        {
            Data.Requests.Add("GET /api/v1/routes");
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(Data.Config.Routes) };
        }
        var response = await inner.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return response;
        var path = request.RequestUri!.AbsolutePath;
        if (path == "/api/v1/usage")
        {
            var value = (await response.Content.ReadFromJsonAsync<UsageSummaryDto>(cancellationToken))!;
            var groups = Groups(value.Accounts);
            response.Content = JsonContent.Create(value with { Totals = Partial ? PartialTotals() : value.Totals,
                Accounts = groups, Daily = Partial ? value.Daily.Select(x => x with { Totals = PartialTotals() }).ToArray() : value.Daily,
                Cursor = Regressed ? 1 : value.Cursor });
        }
        if (path == "/api/v1/attempts")
        {
            var value = (await response.Content.ReadFromJsonAsync<PageDto<AttemptDto>>(cancellationToken))!;
            response.Content = JsonContent.Create(value with { Items = value.Items.Select(x => x with { Tokens = new(null, null, null, null, 43200) }).ToArray() });
        }
        if (path == "/api/v1/usage/analysis")
        {
            var value = (await response.Content.ReadFromJsonAsync<UsageAnalysisDto>(cancellationToken))!;
            response.Content = JsonContent.Create(value with { Totals = Partial ? PartialTotals() : value.Totals,
                Dimensions = value.Dimensions.Select(d => d with { Groups = d.Groups.Where(x => !OmitKimi || x.Key != "a4")
                    .Select(x => Partial && x.Key == "a4" ? x with { Totals = x.Totals with { Tokens = new(null, null, null, null, null), UnknownUsageAttempts = 28 } } : x).ToArray() }).ToArray() });
        }
        return response;
    }
    private UsageGroup[] Groups(UsageGroup[] groups) => groups.Where(x => !OmitKimi || x.Key != "a4")
        .Select(x => Partial && x.Key == "a4" ? x with { Totals = x.Totals with { Tokens = new(null, null, null, null, null), UnknownUsageAttempts = 28 } } : x).ToArray();
    private static UsageTotals PartialTotals() => new(128, 132, 4, new(null, null, null, null, null), 28,
        new(new(null, 0, 132), new(null, 0, 132), new(null, 0, 132), new(null, 0, 132), new(null, 0, 132), new(64200, 104, 132)));
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
}
