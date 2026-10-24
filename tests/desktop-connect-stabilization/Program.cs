using System.Net;
using System.Net.Http.Json;
using System.Reflection;
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
using Lain.Desktop.Presentation;

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
        Dispatcher.UIThread.Post(async () => {
            try { await Probe.Run(); lifetime.Shutdown(0); }
            catch (Exception e) { File.WriteAllText(Path.Combine(Probe.Output, "failure.txt"), e.ToString()); Console.Error.WriteLine(e); lifetime.Shutdown(1); }
        });
        base.OnFrameworkInitializationCompleted();
    }
}
internal static class Probe
{
    public static string Output => Environment.GetEnvironmentVariable("LAIN_TEST_OUTPUT")!;
    private static MainWindow window = null!;
    private static readonly List<string> checks = [];
    private static T[] Controls<T>() where T : Control => window.GetVisualDescendants().OfType<T>().Where(c => c.IsVisible).ToArray();
    private static void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }
    private static Button Named(string name) => Controls<Button>().Single(b => AutomationProperties.GetName(b) == name);
    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task Wait(Func<bool> ready) { for (var n = 0; n < 100; n++) { if (ready()) return; await Task.Delay(30); } throw new TimeoutException("UI fixture readiness"); }
    private static void Invoke(string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, args);
    private static async Task InvokeAsync(string name, params object[] args) => await (Task)typeof(MainWindow).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, args)!;
    private static string[] Nav() => Controls<Button>().Where(b => b.Tag is string tag && new[] { "Accounts", "Models", "Profiles", "Usage", "Live" }.Contains(tag)).Select(b => (string)b.Tag!).ToArray();
    public static async Task Run()
    {
        Directory.CreateDirectory(Output);
        var fixture = new Fixture();
        var http = new HttpClient(fixture) { BaseAddress = new Uri("http://fixture.invalid/api/v1/") };
        window = new MainWindow(new LainApiClient(http), Path.Combine(Output, "preferences.json"));
        window.Show();
        await Wait(() => Controls<Button>().Any(b => AutomationProperties.GetName(b) == "Open account details for Command Code"));
        Check(!Controls<TextBlock>().Any(t => t.Text == "Exhausted" || t.Text == "Last known exhausted"), "empty free-credit window never claims whole-account exhaustion");
        Check(Controls<ProgressBar>().Length == 2, "balances have no invented percentage bars");
        Check(Controls<TextBlock>().Any(t => t.Text?.Contains("Partial regeneration") == true), "partial regeneration is visible separately from reset");
        Check(Nav().SequenceEqual(new[] { "Accounts", "Models", "Profiles", "Usage" }), "Simple has four direct destinations");
        var tile = Controls<Border>().First(b => AutomationProperties.GetName(b) == "Account budgets for Command Code");
        var before = tile.Bounds;
        var origin = Named("Open account details for Command Code"); Click(origin);
        await Wait(() => Controls<Button>().Any(b => AutomationProperties.GetName(b) == "Close account details for Command Code"));
        Check(tile.Bounds == before, "detail drawer preserves inventory coordinates");
        var menuKeys = tile.ContextMenu!.Items.Cast<MenuItem>().Select(i => i.Header?.ToString()).ToArray();
        Check(menuKeys.Contains("Refresh quota") && menuKeys.Contains("Remove account…"), "visible and context account actions share complete named commands");
        await Capture("account-details", 1120, 780);
        var inspector = Controls<Border>().Single(b => AutomationProperties.GetName(b) == "Account inspector for Command Code");
        Check(tile.TranslatePoint(default, window)!.Value.X + tile.Bounds.Width <= inspector.TranslatePoint(default, window)!.Value.X, "wide inspector is adjacent and never overlaps account inventory");
        await Capture("account-details", 800, 560);
        Check(!Controls<Border>().Any(b => AutomationProperties.GetName(b) == "Account budgets for Command Code"), "narrow detail uses a separate task page");
        Click(Named("Close account details for Command Code"));
        await Task.Delay(60);
        Check(origin.IsFocused, "closing details restores origin keyboard focus");
        var levels = Controls<ComboBox>().Single(c => AutomationProperties.GetName(c) == "Information level");
        foreach (var level in new[] { InformationLevel.Barebones, InformationLevel.Simple, InformationLevel.Advanced })
        {
            levels.SelectedIndex = (int)level; await Task.Delay(90);
            Check(Nav().Length == (level == InformationLevel.Barebones ? 3 : level == InformationLevel.Simple ? 4 : 5), level + " changes direct navigation");
            Check(Nav().Contains("Profiles"), level + " keeps Gateway profiles directly reachable");
            await Capture("accounts-" + level.ToString().ToLowerInvariant(), 1120, 780);
            await Capture("accounts-" + level.ToString().ToLowerInvariant(), 800, 560);
        }
        Invoke("Navigate", "Profiles"); await Task.Delay(100);
        var workspace = (ScrollViewer)typeof(MainWindow).GetField("_workspace", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        workspace.Offset = new Vector(0, 150);
        Click(Controls<Button>().Single(b => Equals(b.Content, "Edit selection")));
        await Task.Delay(80);
        Check(workspace.Offset.Y == 0, "new profile editor resets inherited detail scroll");
        Invoke("Navigate", "Settings"); await Task.Delay(50); Invoke("Navigate", "Profiles"); await Task.Delay(50);
        Check(Controls<TextBox>().Any(t => AutomationProperties.GetName(t) == "Profile name"), "mode/navigation keeps mounted profile drafts");
        Click(Controls<Button>().Single(b => Equals(b.Content, "Cancel")));
        Invoke("Navigate", "Accounts"); await Task.Delay(60);
        Click(Controls<Button>().Single(b => Equals(b.Content, "Add account")));
        await Wait(() => Controls<Button>().Any(b => AutomationProperties.GetName(b) == "Connect provider kilo"));
        Click(Named("Connect provider kilo"));
        Check(!Named("Connection method device").IsEnabled, "unqualified device flow stays unavailable with guidance");
        Click(Named("Connection method api-key"));
        var privateKey = Controls<TextBox>().Single(b => AutomationProperties.GetName(b) == "api-key");
        Check(privateKey.PasswordChar == '●' && !Controls<TextBlock>().Any(t => t.Text == "Billing plan"), "provider key connect is private and has no artificial tariff");
        Controls<TextBox>().Single(b => AutomationProperties.GetName(b) == "Account name").Text = "My Kilo";
        privateKey.Text = "SYNTHETIC-DESKTOP-KEY";
        Click(Controls<Button>().Single(b => Equals(b.Content, "Connect account")));
        await Wait(() => Controls<Button>().Any(b => Equals(b.Content, "Done")));
        Check(privateKey.Text == "" && fixture.LastConnect?.Connection.Name == "My Kilo", "private input clears after successful handoff and custom name survives");
        Check(fixture.LastConnect?.Connection.IdempotencyKey is { Length: > 0 }, "connect carries one stable operation identity");
        await Capture("connect-result", 1120, 780);
        Click(Controls<Button>().Single(b => Equals(b.Content, "Done")));
        Invoke("Navigate", "Models"); await Task.Delay(60); await Capture("models", 1120, 780); await Capture("models", 800, 560);
        Check(Controls<Button>().Count(b => (AutomationProperties.GetName(b) ?? "").StartsWith("Edit model access ")) == 45, "45 model access entries retain direct edit actions");
        Invoke("Navigate", "Profiles"); await Task.Delay(60); await Capture("profiles", 1120, 780);
        Click(Controls<Button>().Single(b => Equals(b.Content, "Apply to proxy…")));
        await Wait(() => Controls<Button>().Any(b => Equals(b.Content, "Apply to proxy")));
        Check(Controls<TextBlock>().Any(t => t.Text?.Contains("Active baseline is unknown") == true), "Apply preview keeps unknown active baseline visible");
        await Capture("apply-preview", 800, 560);
        Click(Controls<Button>().Single(b => Equals(b.Content, "Cancel")));
        fixture.Monitor = true;
        Invoke("Navigate", "Accounts");
        await InvokeAsync("RefreshAsync", true);
        await Wait(() => (window.Title ?? "").Contains("Monitor"));
        Check(Nav().SequenceEqual(new[] { "Accounts" }), "explicit Monitor hides gateway navigation without fabricating history");
        await Capture("monitor", 640, 420);
        fixture.Monitor = false; await InvokeAsync("RefreshAsync", true); await Wait(() => !(window.Title ?? "").Contains("Monitor"));
        fixture.Offline = true; await InvokeAsync("RefreshAsync", true); await Task.Delay(100);
        Check(Nav().Contains("Profiles"), "offline configured Gateway keeps its navigation");
        Check(fixture.Requests.Count(r => r.StartsWith("POST ")) == 1 && fixture.Requests.All(r => r.StartsWith("GET ") || r == "POST /api/v1/accounts/connect"), "only explicit Connect mutates the synthetic host; presentation sends no provider or inference actions");
        File.WriteAllText(Path.Combine(Output, "evidence.json"), JsonSerializer.Serialize(new { checks, requests = fixture.Requests }, new JsonSerializerOptions { WriteIndented = true }));
        window.Close();
    }
    private static async Task Capture(string name, int width, int height)
    {
        window.Width = width; window.Height = height; await Task.Delay(100);
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        bitmap.Render(window); bitmap.Save(Path.Combine(Output, $"{name}-{width}x{height}.png"));
    }
}
internal sealed class Fixture : HttpMessageHandler
{
    public bool Monitor, Offline;
    public ConnectCredentialRequest? LastConnect;
    public List<string> Requests { get; } = [];
    private AccountDto[] accounts = [new("cc", "Command Code", "command-code", true, "api-key", "ready"), new("kilo", "Kilo Wallet", "kilo", true, "api-key", "ready")];
    private readonly RouteDto[] routes = Enumerable.Range(1, 45).Select(i => new RouteDto("r" + i, "research-" + i.ToString("00"), "model-" + i, "fixture", ["cc"])).ToArray();
    private readonly ProfileDto[] profiles = [new("p1", "Daily", ["r1"], "r1")];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Requests.Add(request.Method + " " + request.RequestUri!.AbsolutePath);
        if (Offline) throw new HttpRequestException("synthetic offline");
        if (request.RequestUri.Host != "fixture.invalid") throw new InvalidOperationException("Forbidden fixture request");
        if (request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath == "/api/v1/accounts/connect")
        {
            var envelope = request.Content!.ReadFromJsonAsync<RevisionRequest<ConnectCredentialRequest>>(cancellationToken: token).GetAwaiter().GetResult()!;
            LastConnect = envelope.Value;
            if (LastConnect.Secret != "SYNTHETIC-DESKTOP-KEY" || envelope.Revision != 1) throw new Exception("Private connect contract mismatch");
            var id = LastConnect.Connection.AccountId ?? "connected";
            var connected = new AccountDto(id, LastConnect.Connection.Name!, LastConnect.Connection.Provider, true, "api-key", "unverified", Generation: 1,
                Binding: LastConnect.Connection.Binding, Capabilities: LastConnect.Connection.Purposes.Select(p => new AccountCapabilityDto(p, "unverified")).ToArray());
            accounts = accounts.Where(a => a.Id != id).Append(connected).ToArray();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new ConfigurationDto(1, 1, accounts, routes, profiles, Monitor ? "monitor" : "gateway")) });
        }
        if (request.Method != HttpMethod.Get) throw new InvalidOperationException("Unlisted fixture mutation");
        var now = DateTimeOffset.UtcNow;
        object data = request.RequestUri.AbsolutePath switch
        {
            "/api/v1/status" => new StatusDto(1, "fixture", "ready", !Monitor, 1, 1, now, RuntimeMode: Monitor ? "monitor" : "gateway", Capabilities: new(true, !Monitor, !Monitor, !Monitor, !Monitor, !Monitor)),
            "/api/v1/configuration" => new ConfigurationDto(1, 1, accounts, routes, profiles, Monitor ? "monitor" : "gateway"),
            "/api/v1/connection-methods" => new ConnectionMethodCatalogDto(1,
                [new("kilo", "api-key", "Use existing Kilo key", "api-key", ["quota", "inference"], "available", false, true, "api-key", "Wallet and personal Pass use the same key.", "https://example.invalid/v1", Recommended: true),
                 new("kilo", "device", "Kilo device sign-in", "device", ["quota"], "unavailable", false, false, Guidance: "Device lifecycle is not qualified in this host."),
                 new("groq", "api-key", "Use existing Groq key", "api-key", ["inference"], "available", false, true, "api-key", Guidance: "Quota headers require normal Gateway traffic; no standalone monitor.")]),
            "/api/v1/profiles/p1/preview" => new ProfileApplyPreviewDto(1, "p1", 1, null, null, ["r1"], [], [], ["cc"], ["all connected clients"], "global proxy restart", false),
            "/api/v1/models" => new ModelCatalogDto(1, 1, []),
            "/api/v1/quotas" => new QuotaDto[] {
                new("cc", null, "fixture#freeCredits", "ok", now, now, null, "credits", null, null, 0, false, WindowLabel: "Free credits", MeasurementKind: "balance"),
                new("cc", null, "fixture#fiveHour", "ok", now, now, now.AddHours(2), "percent", null, null, null, false, WindowLabel: "5 hours", UsedPercent: 30),
                new("cc", null, "fixture#weekly", "ok", now, now, now.AddDays(2), "percent", null, null, null, false, WindowLabel: "Weekly", UsedPercent: 20, NextRegenerationAt: now.AddHours(1), NextRegenerationPercent: 10),
                new("kilo", null, "fixture#wallet", "ok", now, now, null, "USD", null, null, 0.000034, false, WindowLabel: "Wallet", MeasurementKind: "balance", ExactRemaining: "0.000034", KiloPass: new("active", "fixture", 10, 2, null, now.AddDays(3), null, true)) },
            _ => throw new InvalidOperationException("Unlisted synthetic request: " + request.RequestUri.AbsolutePath)
        };
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(data) });
    }
}
