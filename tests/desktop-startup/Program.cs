using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lain.Client;
using Lain.Client.Lifecycle;
using Lain.Core;
using Lain.Desktop;
using Lain.Desktop.Tray;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var pid = Environment.GetEnvironmentVariable("LAIN_TEST_XVFB_PID");
        var display = Environment.GetEnvironmentVariable("DISPLAY");
        var home = Environment.GetEnvironmentVariable("HOME");
        if (!int.TryParse(pid, out _) || display != Environment.GetEnvironmentVariable("LAIN_TEST_DISPLAY")
            || string.IsNullOrEmpty(display) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
            || !File.Exists($"/proc/{pid}/cmdline") || Path.GetFileName(File.ReadAllText($"/proc/{pid}/cmdline").Split('\0')[0]) != "Xvfb"
            || home is null || !home.StartsWith("/tmp/lain-startup-", StringComparison.Ordinal)
            || Environment.GetEnvironmentVariable("LAIN_HOME") is not { } state || !state.StartsWith("/tmp/lain-startup-", StringComparison.Ordinal))
        { Console.Error.WriteLine("Private startup Xvfb runner and synthetic HOME/state required."); return 2; }
        return AppBuilder.Configure<StartupApp>().UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime(args);
    }
}
internal sealed class StartupApp : Application
{
    public override void Initialize()
    {
        var source = new App(); source.Initialize(); RequestedThemeVariant = source.RequestedThemeVariant;
        var resources = source.Resources; source.Resources = new ResourceDictionary(); Resources = resources;
        var styles = source.Styles.ToArray(); source.Styles.Clear(); foreach (var style in styles) Styles.Add(style);
    }
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Dispatcher.UIThread.Post(async () =>
        {
            try { await new StartupScenario(lifetime).Run(); lifetime.Shutdown(0); }
            catch (Exception error) { Console.Error.WriteLine(error); lifetime.Shutdown(1); }
        });
        base.OnFrameworkInitializationCompleted();
    }
}
internal sealed class StartupScenario(IClassicDesktopStyleApplicationLifetime lifetime)
{
    private readonly List<string> checks = [];
    private MainWindow window = null!;
    private readonly Fixture fixture = new();
    private readonly Preferences preferences = new();
    private readonly List<bool> loginChanges = [];
    private int ensures, inspections;
    private bool login, failLogin, callbackOnUiThread, throwMaintenance;
    private TaskCompletionSource? hold;
    private TaskCompletionSource entered = NewCompletion();
    private UserHostLifecycleStatus? overrideResult;
    private static TaskCompletionSource NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private void Check(bool condition, string text) { if (!condition) throw new Exception(text); checks.Add(text); }
    private T Named<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(control => AutomationProperties.GetName(control) == name);
    private async Task Wait(Func<bool> condition)
    {
        for (var index = 0; index < 250; index++) { if (condition()) return; await Task.Delay(20); }
        throw new TimeoutException("Fixture condition did not complete.");
    }
    private async Task Click(string name)
    {
        var button = window.GetVisualDescendants().OfType<Button>().First(control => AutomationProperties.GetName(control) == name || Equals(control.Content, name));
        Check(button.IsEnabled, name + " stays enabled"); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(60);
    }
    private UserHostLifecycleStatus Status() => overrideResult ?? new("installed", new("fixture", "registered", login, fixture.Available ? "running" : "stopped"),
        fixture.Available ? "ready" : "stopped", fixture.Current.RuntimeMode == "monitor" ? "not-required" : fixture.Current.RuntimeLifecycle?.Stage ?? (fixture.Current.ProxyReady ? "ready" : "setup-required"),
        fixture.Available ? fixture.Current : null);
    private async Task<UserHostLifecycleStatus> Ensure(CancellationToken token)
    {
        callbackOnUiThread |= Dispatcher.UIThread.CheckAccess();
        Interlocked.Increment(ref ensures); entered.TrySetResult();
        if (hold is { } pending) await pending.Task.WaitAsync(token);
        if (throwMaintenance) throw new LifecycleMaintenanceException("maintenance-active", "Synthetic durable maintenance marker");
        if (overrideResult is not null) return overrideResult;
        fixture.Available = true; return Status();
    }
    private Task<UserHostLifecycleStatus> Inspect(CancellationToken token) { token.ThrowIfCancellationRequested(); Interlocked.Increment(ref inspections); return Task.FromResult(Status()); }
    private Task<UserHostLifecycleStatus> SetLogin(bool enabled, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); loginChanges.Add(enabled);
        if (failLogin) throw new IOException("Synthetic manager failure");
        login = enabled; return Task.FromResult(Status());
    }
    private async Task Open()
    {
        var services = new DesktopStartupServices(Ensure, Inspect, SetLogin, preferences);
        window = new MainWindow(new LainApiClient(new HttpClient(fixture, false) { BaseAddress = new("http://fixture.invalid") }),
            Path.Combine(Environment.GetEnvironmentVariable("LAIN_HOME")!, Guid.NewGuid() + ".json"), services);
        lifetime.MainWindow = window; window.Show(); await Task.Delay(80);
    }
    public async Task Run()
    {
        NativeShortcutArguments();
        hold = NewCompletion(); await Open(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var initial = window.EnsureServiceOnOpenAsync(); var repeated = window.EnsureServiceOnOpenAsync();
        Check(ReferenceEquals(initial, repeated) && ensures == 1, "concurrent opens share one startup operation");
        Check(window.IsVisible && fixture.Requests.Count == 0, "window appears before bootstrap and missing-token API reads");
        Check(!callbackOnUiThread, "potentially blocking bootstrap callback runs off the UI thread");
        await Click("Settings");
        Check(Named<Control>("Settings page").IsEffectivelyVisible, "Settings is available while bootstrap waits");
        Check(Named<TextBlock>("Background service startup status").Text == "Starting background service…", "startup progress is distinct from unavailable");
        hold.TrySetResult(); await initial; hold = null;
        Check(fixture.Available && fixture.StatusReads > 0, "first startup retries authenticated refresh after token creation");
        Check(preferences.Value.StartOnOpen && !login && loginChanges.Count == 0, "start-on-open defaults on without enabling sign-in");
        Check(fixture.Current.Revision == 58 && fixture.Current.AppliedRevision == 57 && fixture.Mutations == 0,
            "bootstrap preserves pending revision and performs no Apply or other Host writes");
        await window.EnsureServiceOnOpenAsync();
        Check(ensures == 2 && fixture.Mutations == 0, "healthy reopen uses idempotent ensure without proxy restart");
        using (var tray = new TrayWorkspaceController(window, () => { }, (_, _) => Task.FromResult(new TrayStatusSnapshot("fixture", [], [], true)),
            new SilentSink(), _ => Task.FromResult(true)))
        {
            tray.Reopened += () => _ = window.EnsureServiceOnOpenAsync();
            window.Hide(); tray.Reopen(); var trayStartup = window.EnsureServiceOnOpenAsync(); await trayStartup;
            Check(window.IsVisible && ensures == 3, "tray and existing-instance reopen use the same coalesced bootstrap path");
        }
        Named<CheckBox>("Start background service when opening Lain").IsChecked = false;
        await Wait(() => !preferences.Value.StartOnOpen);
        var beforeEnsure = ensures; await window.EnsureServiceOnOpenAsync();
        Check(ensures == beforeEnsure && inspections == 1, "disabled start-on-open inspects without starting");
        Named<CheckBox>("Start background service at sign-in").IsChecked = true;
        await Wait(() => login && Named<CheckBox>("Start background service at sign-in").IsEnabled);
        Check(loginChanges.SequenceEqual([true]) && !preferences.Value.StartOnOpen && ensures == beforeEnsure,
            "sign-in toggle calls manager independently of start-on-open and service start");
        failLogin = true; Named<CheckBox>("Start background service at sign-in").IsChecked = false;
        await Wait(() => Named<TextBlock>("Background startup settings error").Text!.Contains("Requested: Off"));
        Check(Named<CheckBox>("Start background service at sign-in").IsChecked == true && fixture.Available,
            "manager failure restores authoritative checkbox and keeps current Host running");
        failLogin = false; await Click("Retry startup setting");
        await Wait(() => !login && Named<CheckBox>("Start background service at sign-in").IsEnabled);
        Check(loginChanges.SequenceEqual([true, false, false]) && fixture.Available, "explicit settings retry applies captured sign-in choice without Stop");
        preferences.FailWrite = true;
        Named<CheckBox>("Start background service when opening Lain").IsChecked = true;
        await Wait(() => Named<TextBlock>("Background startup settings error").Text!.Contains("Requested: On"));
        Check(Named<CheckBox>("Start background service when opening Lain").IsChecked == false && !preferences.Value.StartOnOpen,
            "failed start-on-open save rolls back without starting service");
        preferences.FailWrite = false; await Click("Retry startup setting");
        await Wait(() => preferences.Value.StartOnOpen && Named<CheckBox>("Start background service when opening Lain").IsEnabled);
        Check(ensures == beforeEnsure && !login, "saving start-on-open never implicitly starts or changes sign-in setting");
        preferences.Value = new(false); fixture.Available = false;
        await window.EnsureServiceOnOpenAsync();
        Check(Named<TextBlock>("Background service startup status").Text == "Background service unavailable", "offline Host has explicit unavailable status");
        Check(Named<Control>("Settings page").IsEffectivelyVisible, "offline refresh preserves open Settings");
        await window.RetryServiceStartupAsync();
        Check(fixture.Available && !preferences.Value.StartOnOpen && !login, "explicit retry starts absent Host without changing either preference");
        fixture.Current = fixture.Current with { RuntimeMode = "monitor", ProxyReady = false, RuntimeLifecycle = new(false, "monitoring") };
        preferences.Value = new(true); await window.EnsureServiceOnOpenAsync();
        Check(!fixture.Current.ProxyReady && fixture.Mutations == 0, "Monitor startup stays Core-free");
        fixture.Current = fixture.Current with { RuntimeMode = "gateway", ProxyReady = false, RuntimeLifecycle = new(true, "suppressed") };
        await window.EnsureServiceOnOpenAsync(); await window.EnsureServiceOnOpenAsync();
        Check(Named<TextBlock>("Background service startup status").Text == "Host ready · Proxy stopped" && fixture.Mutations == 0,
            "repeat opens preserve manual Stop and show Host/Core separately");
        fixture.Current = fixture.Current with { RuntimeLifecycle = new(false, "recovery-required", "Synthetic recovery required") };
        await window.EnsureServiceOnOpenAsync();
        Check(Named<TextBlock>("Background service startup status").Text == "Host ready · Gateway recovery required" && fixture.Mutations == 0,
            "recovery is displayed without automatic Apply or direct Core writes");
        fixture.Current = fixture.Current with { AppliedRevision = 0, RuntimeLifecycle = new(false, "setup-required") };
        await window.EnsureServiceOnOpenAsync();
        Check(Named<TextBlock>("Background service startup status").Text == "Host ready · Gateway setup needed", "fresh Gateway distinguishes setup from failed Host");
        fixture.Current = fixture.Current with { AppliedRevision = 57, RuntimeLifecycle = new(false, "health", StartedAt: DateTimeOffset.UtcNow) };
        await window.EnsureServiceOnOpenAsync();
        Check(Named<TextBlock>("Background service startup status").Text == "Host ready · Proxy Core starting", "Core startup stages remain distinct from a terminal readiness failure");
        fixture.Current = fixture.Current with { RuntimeLifecycle = new(false, "health", "Synthetic health check failure", StartedAt: DateTimeOffset.UtcNow, CompletedAt: DateTimeOffset.UtcNow) };
        await window.EnsureServiceOnOpenAsync();
        Check(Named<TextBlock>("Background service startup status").Text == "Host ready · Proxy Core not ready", "Core health failure distinguishes ready Host");
        fixture.Available = false;
        overrideResult = new("maintenance", new("fixture", "registered", false, "stopped"), "blocked", "unknown", Error: "Synthetic maintenance gate");
        await window.EnsureServiceOnOpenAsync();
        Check(Named<TextBlock>("Background service startup status").Text == "Background service startup blocked",
            "maintenance block is distinct and retry remains available");
        var beforeMaintenanceReads = fixture.Requests.Count;
        overrideResult = null; throwMaintenance = true;
        await window.EnsureServiceOnOpenAsync(); await Click("Reload status");
        Check(Named<TextBlock>("Background service startup status").Text == "Background service startup blocked"
            && Named<TextBlock>("Background service startup detail").Text!.Contains("Synthetic durable maintenance marker")
            && fixture.Requests.Count == beforeMaintenanceReads,
            "maintenance exception preserves its reason and blocks fallback and periodic API refresh");
        Check(fixture.Mutations == 0, "all Desktop refresh and bootstrap paths leave Host mutation ownership with shared lifecycle");
        var output = Environment.GetEnvironmentVariable("LAIN_TEST_OUTPUT")!; Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "startup-evidence.json"), JsonSerializer.Serialize(new { checks, ensures, inspections, loginChanges,
            fixture.Requests, assembly = typeof(MainWindow).Assembly.Location,
            scope = "Private Xvfb, synthetic HOME/state, in-memory startup callbacks, preferences and HTTP. No service manager, real Host or provider." }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS {checks.Count} Desktop startup checks"); window.Close();
    }
    private void NativeShortcutArguments()
    {
        var method = typeof(App).Assembly.GetType("Lain.Desktop.Program")!.GetMethod("ConfigureStateDirectory", BindingFlags.Static | BindingFlags.NonPublic)!;
        var state = Environment.GetEnvironmentVariable("LAIN_HOME")!;
        var path = Path.Combine(state, "data with spaces");
        try
        {
            var remaining = (string[])method.Invoke(null, [new[] { "--lain-home", path, "--fixture" }])!;
            Check(Environment.GetEnvironmentVariable("LAIN_HOME") == path && remaining.SequenceEqual(["--fixture"]), "native shortcut selects separated data path before discovery");
            try { method.Invoke(null, [new[] { "--lain-home", "relative-path" }]); throw new Exception("Relative path accepted"); }
            catch (TargetInvocationException error) when (error.InnerException is ArgumentException) { Check(true, "native shortcut rejects relative state path"); }
        }
        finally { Environment.SetEnvironmentVariable("LAIN_HOME", state); }
    }
    private sealed class SilentSink : IDesktopNotificationSink { public Task<bool> SendAsync(DesktopAlert alert, CancellationToken cancellationToken = default) => Task.FromResult(true); }
    private sealed class Preferences : IStartupPreferencesStore
    {
        public StartupPreferences Value = new(); public bool FailWrite;
        public Task<StartupPreferences> ReadAsync(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return Task.FromResult(Value); }
        public Task WriteAsync(StartupPreferences preferences, CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); if (FailWrite) throw new IOException("Synthetic preference failure"); Value = preferences; return Task.CompletedTask; }
    }
    private sealed class Fixture : HttpMessageHandler
    {
        public bool Available; public int StatusReads, Mutations;
        public StatusDto Current = new(1, "fixture-startup", "ready", true, 58, 57, DateTimeOffset.UtcNow, ActiveProfileId: "applied", ActiveProfileRevision: 57,
            RuntimeLifecycle: new(false, "ready"));
        public readonly List<string> Requests = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Requests.Add(request.Method + " " + request.RequestUri!.AbsolutePath);
            if (request.RequestUri.Host != "fixture.invalid" || request.Headers.Authorization is not null) throw new Exception("Unexpected external request");
            if (request.Method != HttpMethod.Get) { Mutations++; throw new Exception("Unexpected Host mutation"); }
            if (!Available) throw new FileNotFoundException("Synthetic first-start token is not present");
            object value = request.RequestUri.AbsolutePath switch
            {
                "/api/v1/status" => ReadStatus(),
                "/api/v1/configuration" => new ConfigurationDto(58, 57, [], [], [], Current.RuntimeMode),
                "/api/v1/quotas" => Array.Empty<QuotaDto>(),
                "/api/v1/models" => new ModelCatalogDto(1, 58, []),
                _ => throw new Exception("Unexpected fixture request: " + request.RequestUri.AbsolutePath)
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(value) });
        }
        private StatusDto ReadStatus() { StatusReads++; return Current; }
    }
}
