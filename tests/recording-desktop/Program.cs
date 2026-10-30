using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lain.Client;
using Lain.Core;
using Lain.Desktop;
using Lain.Desktop.Recording;

internal static class Program
{
    [STAThread] public static int Main(string[] args)
    {
        var pid = Environment.GetEnvironmentVariable("LAIN_TEST_XVFB_PID");
        if (!int.TryParse(pid, out _) || Environment.GetEnvironmentVariable("DISPLAY") != Environment.GetEnvironmentVariable("LAIN_TEST_DISPLAY")
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) || !File.Exists($"/proc/{pid}/cmdline")
            || Path.GetFileName(File.ReadAllText($"/proc/{pid}/cmdline").Split('\0')[0]) != "Xvfb") return 2;
        return AppBuilder.Configure<ProbeApp>().UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime(args);
    }
}
internal sealed class ProbeApp : Application
{
    public override void Initialize()
    {
        var app = new App(); app.Initialize(); RequestedThemeVariant = app.RequestedThemeVariant;
        var resources = app.Resources; app.Resources = new ResourceDictionary(); Resources = resources;
        foreach (var style in app.Styles.ToArray()) { app.Styles.Remove(style); Styles.Add(style); }
    }
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Dispatcher.UIThread.Post(async () =>
        {
            try { await Probe.Run(); lifetime.Shutdown(0); }
            catch (Exception e) { Directory.CreateDirectory(Probe.Output); File.WriteAllText(Path.Combine(Probe.Output, "failure.txt"), e.ToString()); Console.Error.WriteLine(e); lifetime.Shutdown(1); }
        });
        base.OnFrameworkInitializationCompleted();
    }
}
internal static class Probe
{
    public static string Output => Environment.GetEnvironmentVariable("LAIN_TEST_OUTPUT") ?? throw new Exception("LAIN_TEST_OUTPUT is required");
    private static Window window = null!;
    private static readonly List<string> checks = [];
    private static IEnumerable<T> Controls<T>() where T : Control => window.GetVisualDescendants().OfType<T>();
    private static Button Button(string name) => Controls<Button>().Single(b => AutomationProperties.GetName(b) == name);
    private static string Text => string.Join("\n", Controls<TextBlock>().Select(t => t.Text).Concat(Controls<SelectableTextBlock>().Select(t => t.Text)));
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add(label); }
    private static async Task Wait(Func<bool> condition) { for (var i = 0; i < 120; i++) { if (condition()) return; await Task.Delay(25); } throw new TimeoutException("Recording fixture readiness"); }
    private static async Task Capture(string name, int width, int height)
    {
        window.Width = width; window.Height = height; await Task.Delay(100);
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height)); bitmap.Render(window); bitmap.Save(Path.Combine(Output, name + ".png"));
    }
    public static async Task Run()
    {
        Directory.CreateDirectory(Output);
        var fixture = new Fixture();
        using var http = new HttpClient(fixture) { BaseAddress = new Uri("http://recording-fixture.invalid/") };
        using var client = new LainApiClient(http);
        var view = new RecordingView(client);
        window = new Window { Content = view, Width = 1120, Height = 780, Title = "Synthetic recording archive" }; window.Show();
        await Wait(() => Controls<Button>().Any(b => AutomationProperties.GetName(b) == "Open saved conversation root"));
        Check(fixture.PolicyWrites == 0 && !fixture.Policy.Enabled, "opening the production view leaves recording default off");
        Check(!Button("Enable recording").IsEnabled, "enable requires explicit opt-in");
        Check(fixture.DetailReads == 0, "metadata list does not eagerly query conversations");
        var stopwatch = Stopwatch.StartNew(); Click(Button("Open saved conversation root"));
        await Wait(() => Text.Contains("Synthetic first turn"));
        stopwatch.Stop();
        Check(fixture.DetailReads == 1 && fixture.Limits.All(n => n <= 25), "selected conversation uses bounded metadata and timeline pages");
        Check(!Text.Contains("sequence 1 · event"), "source details stay lazy");
        Check(Controls<SelectableTextBlock>().All(t => (t.Text?.Length ?? 0) <= 4096), "text sections remain bounded at 4096 characters");
        Check(Controls<SelectableTextBlock>().Sum(t => t.Text?.Length ?? 0) < 20000, "long output retains a bounded 8192-character visible page");
        Check(Text.Contains("model-b") && Text.Contains("incomplete") && Text.Contains("client"), "model switch, partial capture and client results are readable");
        var sources = Controls<Expander>().Single(e => AutomationProperties.GetName(e) == "API source details record-0"); sources.IsExpanded = true;
        await Wait(() => Text.Contains("sequence 1 · event"));
        Check(Text.Contains("not original wire bytes") && Text.Contains("unknown"), "normalized source and unknown revision remain explicit");
        var worker = Controls<Expander>().First(e => AutomationProperties.GetName(e) == "Worker evidence worker-a"); worker.IsExpanded = true;
        Check(Text.Contains("Parent worker: root-worker"), "worker parent is shown only from supplied evidence");
        var firstCard = Controls<Border>().Single(b => AutomationProperties.GetName(b) == "Timeline record record-0");
        Click(Button("Load more timeline records")); await Wait(() => Controls<Border>().Any(b => AutomationProperties.GetName(b) == "Timeline record record-27"));
        Check(ReferenceEquals(firstCard, Controls<Border>().Single(b => AutomationProperties.GetName(b) == "Timeline record record-0")) && sources.IsExpanded, "appended page preserves prior controls and expansion state");
        await Capture("archive-wide", 1120, 780);
        await Capture("archive-narrow", 800, 560);
        Check(Button("Back to saved conversations").IsVisible, "narrow selection provides a dedicated back action");
        Click(Button("Back to saved conversations"));
        Click(Button("Load more saved conversations")); await Wait(() => Controls<Button>().Any(b => AutomationProperties.GetName(b) == "Open saved conversation child"));
        Click(Button("Open saved conversation child")); await Wait(() => Text.Contains("Worker child history"));
        Check(!Text.Contains("Synthetic first turn"), "selection cancels and replaces the prior conversation display");
        window.Content = null; await Task.Delay(50);
        window.Content = new RecordingView(client);
        await Wait(() => Controls<Button>().Any(b => AutomationProperties.GetName(b) == "Open saved conversation root"));
        Click(Button("Open saved conversation root")); await Wait(() => Text.Contains("Synthetic first turn"));
        Check(fixture.PolicyWrites == 0, "close and reopen reads retained fixture archive without enabling capture");
        fixture.SourceUnavailable = true; Click(Button("Refresh recording archive")); await Wait(() => Text.Contains("source_unavailable"));
        Check(Text.Contains("Synthetic first turn"), "source failure preserves retained readable history");
        fixture.Deleted = true; Click(Button("Refresh recording archive")); await Wait(() => Text.Contains("deleted or expired"));
        Check(!Text.Contains("Synthetic first turn") && !Controls<SelectableTextBlock>().Any(), "deleted source invalidates visible content and copy surfaces");
        File.WriteAllText(Path.Combine(Output, "evidence.json"), JsonSerializer.Serialize(new { checks, firstConversationMilliseconds = stopwatch.Elapsed.TotalMilliseconds, workingSetBytes = Process.GetCurrentProcess().WorkingSet64, fixture.ListReads, fixture.DetailReads, fixture.PolicyWrites, fixture.Limits, synthetic = true }, new JsonSerializerOptions { WriteIndented = true }));
        window.Close();
    }
}
internal sealed class Fixture : HttpMessageHandler
{
    public CapturePolicySnapshot Policy = new();
    public int PolicyWrites, ListReads, DetailReads;
    public bool SourceUnavailable, Deleted;
    public List<int> Limits = [];
    private static readonly DateTimeOffset Time = DateTimeOffset.Parse("2026-10-30T09:00:00Z");
    private static RecordingConversationSummary Summary(string id, string? worker = null) => new(id, Time, Time.AddMinutes(3), 3, 6, ["model-a", "model-b"], WorkerId: worker);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
        if (int.TryParse(query["limit"], out var limit)) Limits.Add(limit);
        object result;
        if (request.Method != HttpMethod.Get)
        {
            if (path.EndsWith("policy"))
            {
                PolicyWrites++;
                var update = await request.Content!.ReadFromJsonAsync<RecordingPolicyUpdate>(cancellationToken: ct);
                Policy = Policy with { Enabled = update!.Enabled, Mode = update.Mode, Revision = Policy.Revision + 1 };
                result = Policy;
            }
            else { Deleted = true; result = new RecordingClearResult(true); }
        }
        else if (path.EndsWith("/runs")) result = new RecordingRunManifest[] { new("run-a", "skill-v1", "revision-a", SkillHash: "configured-hash", Evidence: "configured"), new("run-b", "skill-v2", ObservedInstructionHash: "observed-hash", Evidence: "observed") };
        else if (path.Contains("conversations/"))
        {
            DetailReads++;
            if (Deleted) return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = JsonContent.Create(new { code = "not_found", message = "synthetic" }) };
            var id = path.Split('/').Last();
            var records = id == "child" ? new[] { Record(99, "Worker child history") } : query["cursor"] is not null ? Enumerable.Range(3, 25).Select(i => Record(i, $"Synthetic small tool block {i}")).ToArray() : Enumerable.Range(0, 3).Select(i => Record(i, i == 0 ? "Synthetic first turn\n" + new string('L', 20000) : $"Synthetic turn {i + 1}")).ToArray();
            result = new RecordingConversationDetail(Summary(id), records, NextCursor: id == "root" && query["cursor"] is null ? "timeline-2" : null, HasMore: id == "root" && query["cursor"] is null, Runs: [new("run-a", "skill-v1", "revision-a", SkillHash: "configured-hash", Evidence: "configured"), new("run-b", "skill-v2", ObservedInstructionHash: "observed-hash", Evidence: "observed")]);
        }
        else if (path.EndsWith("conversations"))
        {
            ListReads++;
            result = Deleted ? new RecordingConversationPage([]) : query["cursor"] is null ? new RecordingConversationPage([Summary("root")], "page-2", true) : new RecordingConversationPage([Summary("child", "worker-a")]);
        }
        else result = new RecordingHealthDto(1, Policy, SourceUnavailable ? "source_unavailable" : "off", Storage: new(RecordCount: 3), ErrorCode: SourceUnavailable ? "source_unavailable" : null);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(result) };
    }
    private static RecordingNormalizedRecord Record(int i, string text) => new($"record-{i}", "capture-root", $"request-{i}", i == 2 ? "failed-retry" : $"attempt-{i}", Time.AddMinutes(i), "responses",
        [new($"message-{i}", i % 2 == 0 ? "assistant" : "user", [new("text", text), new("tool_call", ToolCallId: $"call-{i}", ToolName: "read", Arguments: "{\"path\":\"synthetic\"}", Result: "Synthetic client result")], [new("epoch", i + 1, $"event-{i}")], Complete: i != 2)], [new("epoch", i + 1, $"event-{i}")],
        ConversationId: "root", WorkerId: i == 1 ? "worker-a" : i == 2 ? "worker-b" : null, ParentWorkerId: i > 0 ? "root-worker" : null, Model: i == 0 ? "model-a" : "model-b", Complete: i != 2, GapReason: i == 2 ? "compaction / source gap" : null);
}
