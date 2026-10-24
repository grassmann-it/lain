using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lain.Client;
using Lain.Desktop;
using Lain.Desktop.Presentation;
using Lain.Desktop.Tray;

internal static class TraySmokeProgram
{
    [STAThread]
    public static int Main(string[] args)
    {
        var pid = Environment.GetEnvironmentVariable("LAIN_TEST_XVFB_PID");
        var display = Environment.GetEnvironmentVariable("DISPLAY");
        if (!int.TryParse(pid, out _) || display != Environment.GetEnvironmentVariable("LAIN_TEST_DISPLAY") || display is null
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
            || Environment.GetEnvironmentVariable("LAIN_TEST_PRIVATE_BUS") != "1"
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS"))
            || !PrivateProcess("LAIN_TEST_WM_PID", "openbox")
            || !PrivateProcess("LAIN_TEST_BUS_STUB_PID", "python3")
            || !File.Exists($"/proc/{pid}/cmdline")
            || Path.GetFileName(File.ReadAllText($"/proc/{pid}/cmdline").Split('\0')[0]) != "Xvfb")
        {
            Console.Error.WriteLine("Refusing startup: private Xvfb and private D-Bus runner required.");
            return 2;
        }
        if (args.FirstOrDefault() == "--ipc-owner") return IpcProbe.Owner(args[1], args[2], args[3], args[4]);
        if (args.FirstOrDefault() == "--ipc-client") return IpcProbe.Client(args[1], args[2]);
        return AppBuilder.Configure<TraySmokeApp>().UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime(args);
    }

    private static bool PrivateProcess(string variable, string executable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        return int.TryParse(value, out _) && File.Exists($"/proc/{value}/cmdline")
            && Path.GetFileName(File.ReadAllText($"/proc/{value}/cmdline").Split('\0')[0]) == executable;
    }
}

internal sealed class TraySmokeApp : Application
{
    public override void Initialize()
    {
        var production = new App(); production.Initialize();
        RequestedThemeVariant = production.RequestedThemeVariant;
        var resources = production.Resources; production.Resources = new ResourceDictionary(); Resources = resources;
        var styles = production.Styles.ToArray(); production.Styles.Clear();
        foreach (var style in styles) Styles.Add(style);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Dispatcher.UIThread.Post(async () =>
        {
            var scenario = new TrayScenario();
            try { await scenario.Run(); lifetime.Shutdown(0); }
            catch (Exception error)
            {
                await scenario.Failure(error);
                Console.Error.WriteLine("FAIL tray smoke: " + error);
                lifetime.Shutdown(1);
            }
        });
        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class TrayScenario
{
    private static readonly string Output = Environment.GetEnvironmentVariable("LAIN_TEST_OUTPUT") ?? throw new Exception("Output missing");
    private readonly List<string> checks = [];
    private readonly List<object> images = [];
    private Window? visible;

    public async Task Run()
    {
        Directory.CreateDirectory(Output);
        await NativeBoundary();
        await MinimizeAndClose();
        await MissingHost();
        await VisibilityRaces();
        await HostLossAndMenu();
        await ViewOptions();
        await IpcProcesses();
        var assembly = typeof(TrayWorkspaceController).Assembly;
        await File.WriteAllTextAsync(Path.Combine(Output, "evidence.json"), JsonSerializer.Serialize(new
        {
            result = "PASS", privateXvfb = true, privateDbus = true, display = Environment.GetEnvironmentVariable("DISPLAY"),
            assembly = assembly.Location, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))), checks, images,
            scope = "Native Avalonia StatusNotifier registration/PID/property read, real Linux TrayAvailability and notify-send adapter against private D-Bus fixture; production Avalonia Window/MainWindow/TrayWorkspaceController; injected quota polls, notification sink and tray availability. UiInstanceOwner from the production assembly in real child processes, isolated XDG runtime. Private Openbox on owned Xvfb; no normal desktop bus, real credentials, providers or host/service operations. Native menu events exercise Avalonia's exporter bridge; no OS pointer hit-testing."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS tray smoke: {checks.Count} checks, {images.Count} screenshots. {Output}/evidence.json");
    }

    private async Task NativeBoundary()
    {
        Check(!await TrayAvailability.IsAvailableAsync(CancellationToken.None),
            "a real private watcher alone does not prove this process registered a native icon");
        var window = NewWindow(); var input = new TrayInputs();
        using var tray = new TrayWorkspaceController(window, new UiExitProbe().Quit, input.Poll,
            new FakeSink(), TrayAvailability.IsAvailableAsync);
        var timer = Stopwatch.StartNew();
        while (!await TrayAvailability.IsAvailableAsync(CancellationToken.None))
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(8)) throw new Exception("Native Avalonia icon did not register with the private watcher");
            await Task.Delay(80);
        }
        Check(true, "real Linux TrayAvailability finds the native Avalonia registration owned by this process");
        using var snapshot = ReadBusEvidence();
        var item = snapshot.RootElement.GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("processId").GetInt32() == Environment.ProcessId);
        var status = await BusCall(item.GetProperty("service").GetString()!, item.GetProperty("path").GetString()!,
            "org.freedesktop.DBus.Properties.Get", "org.kde.StatusNotifierItem", "Status");
        Check(status.Contains("Active", StringComparison.Ordinal), "the registered native StatusNotifierItem exports its active status over private D-Bus");
        await BusCall("org.kde.StatusNotifierWatcher", "/StatusNotifierWatcher", "org.lain.TrayTest.SetHostRegistered", "false");
        Check(!await TrayAvailability.IsAvailableAsync(CancellationToken.None), "real Linux availability becomes false when the private tray host disappears");
        await BusCall("org.kde.StatusNotifierWatcher", "/StatusNotifierWatcher", "org.lain.TrayTest.SetHostRegistered", "true");
        Check(await TrayAvailability.IsAvailableAsync(CancellationToken.None), "real Linux availability recovers with the registered native icon and restored host");
        var sink = new LinuxDesktopNotificationSink();
        Check(await sink.SendAsync(new DesktopAlert("private-native", "Fixture <quota> & alert", "Private <body> & \"quoted\""), CancellationToken.None),
            "the production notify-send adapter delivers to the private Notifications service");
        using var delivered = ReadBusEvidence();
        var notification = delivered.RootElement.GetProperty("notifications").EnumerateArray().Single();
        Check(notification.GetProperty("app").GetString() == "lain"
            && notification.GetProperty("summary").GetString() == "Fixture &lt;quota&gt; &amp; alert"
            && notification.GetProperty("body").GetString() == "Private &lt;body&gt; &amp; &quot;quoted&quot;"
            && notification.GetProperty("desktopEntry").GetString() == "lain"
            && notification.GetProperty("urgency").GetInt32() == 1
            && notification.GetProperty("timeout").GetInt32() == 8000
            && notification.GetProperty("icon").GetString()!.EndsWith("lain-cat.png", StringComparison.Ordinal),
            "the native notification preserves the application icon, plain-text escaping, urgency and expiry");
        tray.Dispose(); window.Close();
    }

    private static JsonDocument ReadBusEvidence() => JsonDocument.Parse(File.ReadAllText(Path.Combine(Output, "native-bus.json")));

    private static async Task<string> BusCall(string destination, string path, string method, params string[] arguments)
    {
        var start = new ProcessStartInfo("/usr/bin/gdbus") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var value in new[] { "call", "--session", "--timeout", "2", "--dest", destination, "--object-path", path, "--method", method }.Concat(arguments))
            start.ArgumentList.Add(value);
        using var process = Process.Start(start) ?? throw new Exception("Private bus probe did not start");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(4)); }
        catch (TimeoutException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw; }
        if (process.ExitCode != 0) throw new Exception("Private bus probe failed: " + await errors);
        await errors;
        return await output;
    }

    private Window NewWindow()
    {
        using var icon = AssetLoader.Open(new Uri("avares://Lain.Desktop/Assets/Brand/lain-cat.png"));
        var window = new Window { Width = 800, Height = 560, Icon = new WindowIcon(icon), Title = "lain private tray fixture",
            Content = new TextBlock { Text = "Private tray lifecycle fixture", Margin = new Thickness(24) } };
        visible = window;
        window.Show();
        return window;
    }

    private static TrayIcon Icon() => TrayIcon.GetIcons(Application.Current!)!.Single();
    private static NativeMenuItem MenuItem(string prefix) => Icon().Menu!.Items.OfType<NativeMenuItem>().Single(x => x.Header?.StartsWith(prefix, StringComparison.Ordinal) == true);
    private static void MenuClick(NativeMenuItem item) => typeof(NativeMenuItem)
        .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
        .Single(x => x.Name.EndsWith("RaiseClicked", StringComparison.Ordinal)).Invoke(item, null);

    private async Task MinimizeAndClose()
    {
        var input = new TrayInputs(); var life = new UiExitProbe(); var sink = new FakeSink();
        var window = NewWindow(); var closed = false; window.Closed += (_, _) => closed = true;
        using var tray = new TrayWorkspaceController(window, life.Quit, input.Poll, sink, input.Availability);
        tray.Configure(true, false, false);
        Check(life.ShutdownCalls == 0, "constructing the tray controller never quits the UI");
        window.WindowState = WindowState.Minimized;
        await Wait(() => !window.IsVisible, "enabled minimize option hides the window only after positive tray availability");
        Check(!closed && tray.TrayAvailable, "tray minimize keeps the native window alive");
        MenuClick(MenuItem("Open window"));
        await Wait(() => window.IsVisible && window.WindowState == WindowState.Normal, "native Open window action restores a hidden minimized window");
        tray.Configure(false, false, false);
        window.WindowState = WindowState.Minimized; await Task.Delay(100);
        Check(window.IsVisible && !closed, "disabled minimize option uses ordinary minimization instead of hiding the window");
        tray.Reopen(); tray.Configure(false, true, false);
        window.Close();
        await Wait(() => !window.IsVisible, "enabled close option hides to a reachable tray");
        Check(!closed, "close-to-tray cancels the native close rather than destroying the window");
        tray.Reopen(); tray.Configure(false, false, false); window.Close();
        await Wait(() => closed, "disabled close option performs a real close");
        Check(input.PollCalls == 0 && sink.Attempts == 0, "window lifecycle operations do not poll providers or send notifications");
    }

    private async Task MissingHost()
    {
        var input = new TrayInputs { Available = false }; var window = NewWindow(); var closed = false;
        window.Closed += (_, _) => closed = true;
        using var tray = new TrayWorkspaceController(window, new UiExitProbe().Quit, input.Poll, new FakeSink(), input.Availability);
        tray.Configure(true, true, false);
        window.WindowState = WindowState.Minimized; await Task.Delay(150);
        Check(window.IsVisible && !closed && !tray.TrayAvailable, "no registered tray host never turns minimization into an invisible live window");
        tray.Reopen(); window.Close();
        await Wait(() => closed && !window.IsVisible, "close-to-tray falls back to a real close when no host exists");
    }

    private async Task VisibilityRaces()
    {
        foreach (var closing in new[] { false, true })
        {
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var input = new TrayInputs { AvailabilityGate = gate }; var window = NewWindow(); var closed = false;
            window.Closed += (_, _) => closed = true;
            using var tray = new TrayWorkspaceController(window, new UiExitProbe().Quit, input.Poll, new FakeSink(), input.Availability);
            tray.Configure(true, true, false);
            if (closing) window.Close(); else window.WindowState = WindowState.Minimized;
            await Wait(() => input.Probes > 0, "visibility change waits for the asynchronous host check");
            tray.Reopen(); gate.SetResult(!closing); await Task.Delay(150);
            Check(window.IsVisible && window.WindowState == WindowState.Normal && !closed,
                closing ? "Reopen wins an in-flight close check even if the host result is unavailable" : "Reopen wins an in-flight minimize/hide check");
            tray.Dispose(); window.Close();
        }
    }

    private async Task HostLossAndMenu()
    {
        var input = new TrayInputs { Lines = Enumerable.Range(1, 10).Select(x => $"Fixture account {x} · 5h usage {x * 7}%").ToArray(), Alert = new("fixture-alert", "Fixture quota warning", "Fixture account 1 · 5h usage 90%") };
        var sink = new FakeSink(); var life = new UiExitProbe(); var window = NewWindow();
        using var tray = new TrayWorkspaceController(window, life.Quit, input.Poll, sink, input.Availability);
        tray.Configure(true, true, true); tray.Start();
        await Wait(() => input.PollCalls > 0 && sink.Attempts > 0 && tray.TrayAvailable, "tray polling fills quota status and delivers an enabled alert through the injected sink");
        var quota = MenuItem("Quota summary").Menu!;
        Check(quota.Items.OfType<NativeMenuItem>().Select(x => x.Header).SequenceEqual(input.Lines), "native quota menu preserves every bounded monitor line");
        Check(!Icon().Menu!.Items.OfType<NativeMenuItem>().Any(x => x.Header?.Contains("Stop host", StringComparison.Ordinal) == true), "native UI menu exposes no host shutdown operation");
        var toggles = new List<bool>(); tray.NotificationsEnabledChanged += toggles.Add;
        MenuClick(MenuItem("Notifications:"));
        Check(toggles.SequenceEqual(new[] { false }) && MenuItem("Notifications:").Header!.Contains("off", StringComparison.Ordinal), "native notification toggle has an independent state callback");
        window.WindowState = WindowState.Minimized;
        await Wait(() => !window.IsVisible, "host-loss case begins with a reachable hidden window");
        input.Available = false;
        await Wait(() => window.IsVisible && window.WindowState == WindowState.Normal && !tray.TrayAvailable,
            "loss of the tray host automatically restores the hidden window", 13000);
        Check(sink.Attempts == 1, "disabled notification setting remains off during later polling");
        await CaptureBoth(window, "restored-after-host-loss");
        MenuClick(MenuItem("Quit UI"));
        Check(life.ShutdownCalls == 1 && input.PollCalls >= 2, "Quit UI requests only UI lifetime shutdown and never a host operation");
        tray.Dispose(); window.Close();
    }

    private async Task ViewOptions()
    {
        using var fixture = new Fixture(); using var http = new HttpClient(fixture, disposeHandler: false) { BaseAddress = new Uri("http://fixture.invalid") };
        var config = JsonSerializer.Serialize(fixture.Config);
        var input = new TrayInputs(); var sink = new FakeSink();
        MainWindow Create(out TrayWorkspaceController controller)
        {
            var result = new MainWindow(new LainApiClient(http)) { Width = 1120, Height = 780 }; visible = result;
            controller = new TrayWorkspaceController(result, new UiExitProbe().Quit, input.Poll, sink, input.Availability);
            result.AttachTrayController(controller); result.Show(); return result;
        }
        static T Named<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(x => AutomationProperties.GetName(x) == name);
        static void Click(Window window, string name) => Named<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var window = Create(out var tray);
        await Wait(() => fixture.StatusCalls > 0, "production MainWindow uses only the in-memory host fixture");
        Click(window, "View"); await Task.Delay(100);
        Check(Named<CheckBox>(window, "Minimize to tray").IsChecked == true && Named<CheckBox>(window, "Close to tray").IsChecked == false
            && Named<CheckBox>(window, "Usage and rate-limit alerts").IsChecked == true, "View exposes separate minimize close and notification defaults");
        Named<CheckBox>(window, "Minimize to tray").IsChecked = false;
        Named<CheckBox>(window, "Close to tray").IsChecked = true;
        Named<CheckBox>(window, "Usage and rate-limit alerts").IsChecked = false;
        var saved = new WorkspacePreferencesStore().Current;
        Check(!saved.MinimizeToTray && saved.CloseToTray && !saved.NotificationsEnabled, "all three background preferences save independently");
        await CaptureBoth(window, "background-view-options");
        tray.Dispose(); window.Close();
        window = Create(out tray); Click(window, "View"); await Task.Delay(100);
        Check(Named<CheckBox>(window, "Minimize to tray").IsChecked == false && Named<CheckBox>(window, "Close to tray").IsChecked == true
            && Named<CheckBox>(window, "Usage and rate-limit alerts").IsChecked == false, "new MainWindow restores each independent background preference");
        MenuClick(MenuItem("Notifications:")); await Task.Delay(100);
        saved = new WorkspacePreferencesStore().Current;
        Check(!saved.MinimizeToTray && saved.CloseToTray && saved.NotificationsEnabled
            && Named<CheckBox>(window, "Usage and rate-limit alerts").IsChecked == true, "native notification toggle updates its View checkbox without changing hide preferences");
        var preferencePath = Path.Combine(Environment.GetEnvironmentVariable("LAIN_HOME")!, "ui", "workspace-preferences.json");
        var beforeFailedSave = File.ReadAllBytes(preferencePath);
        var oldNotificationMenu = MenuItem("Notifications:").Header;
        using (var blockedSave = new FileStream(preferencePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            MenuClick(MenuItem("Notifications:"));
            Check(MenuItem("Notifications:").Header == oldNotificationMenu
                && Named<CheckBox>(window, "Usage and rate-limit alerts").IsChecked == true,
                "failed native notification preference save restores the previous menu and View checkbox");
            Check(window.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text?.Contains("could not be saved", StringComparison.Ordinal) == true),
                "failed native notification preference save reports its storage failure");
        }
        saved = new WorkspacePreferencesStore().Current;
        Check(File.ReadAllBytes(preferencePath).SequenceEqual(beforeFailedSave)
            && !saved.MinimizeToTray && saved.CloseToTray && saved.NotificationsEnabled,
            "failed native toggle leaves the preference file and independent background settings unchanged");
        Check(JsonSerializer.Serialize(fixture.Config) == config && fixture.Requests.All(x => x.StartsWith("GET ", StringComparison.Ordinal)) && fixture.Unexpected.Count == 0,
            "background preferences and native UI menu never mutate host configuration");
        tray.Dispose(); window.Close();
    }

    private async Task IpcProcesses()
    {
        var basePath = Path.Combine(Output, "ipc"); Directory.CreateDirectory(basePath);
        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")!;
        var runtimeA = Path.Combine(runtime, "ipc-a"); var runtimeB = Path.Combine(runtime, "ipc-b");
        Directory.CreateDirectory(runtimeA); Directory.CreateDirectory(runtimeB);
        var state = Path.Combine(Environment.GetEnvironmentVariable("LAIN_HOME")!, "ipc-test");
        var ready = Path.Combine(basePath, "ready"); var reopened = Path.Combine(basePath, "reopened"); var stop = Path.Combine(basePath, "stop");
        using var owner = Child(runtimeA, "--ipc-owner", state, ready, reopened, stop);
        try
        {
            await Wait(() => File.Exists(ready) || owner.HasExited, "separate process acquires the production UI owner");
            Check(!owner.HasExited && File.ReadAllText(ready) == "owner", "UI owner is held by a real separate process");
            var same = await Client(runtimeA, state);
            Check(!same.Owner && same.Reopened, "second process in the same runtime requests reopen instead of owning another UI");
            await Wait(() => File.Exists(reopened), "owner receives the exact reopen protocol callback");
            var other = await Client(runtimeB, state);
            Check(other.Owner && !other.Reopened, "a private XDG runtime remains independent even with the same LAIN_HOME");
        }
        finally
        {
            File.WriteAllText(stop, "stop");
            try { await owner.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { owner.Kill(entireProcessTree: true); await owner.WaitForExitAsync(); throw; }
        }
        Check(owner.ExitCode == 0, "owner exits with its mutex released on the creating thread");
        var replacement = await Client(runtimeA, state);
        Check(replacement.Owner, "a later process can own the UI after the previous owner exits");
    }

    private static Process Child(string runtime, params string[] args)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(TrayScenario).Assembly.Location);
        foreach (var arg in args) start.ArgumentList.Add(arg);
        start.Environment["XDG_RUNTIME_DIR"] = runtime;
        return Process.Start(start) ?? throw new Exception("IPC child did not start");
    }

    private static async Task<IpcResult> Client(string runtime, string state)
    {
        var result = Path.Combine(Output, "ipc", "client-" + Guid.NewGuid().ToString("N") + ".json");
        using var child = Child(runtime, "--ipc-client", state, result);
        try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(7)); }
        catch (TimeoutException) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); throw; }
        if (child.ExitCode != 0) throw new Exception("IPC client failed: " + await child.StandardError.ReadToEndAsync());
        return JsonSerializer.Deserialize<IpcResult>(await File.ReadAllTextAsync(result))!;
    }

    private async Task CaptureBoth(Window window, string page)
    {
        foreach (var (width, height) in new[] { (1120, 780), (800, 560) })
        {
            window.Width = width; window.Height = height; await Task.Delay(200); window.UpdateLayout();
            var file = $"{page}-{width}x{height}.png";
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height), new Vector(96, 96));
            bitmap.Render(window); bitmap.Save(Path.Combine(Output, file), new PngBitmapEncoderOptions());
            images.Add(new { file, width = window.Bounds.Width, height = window.Bounds.Height });
            Check(Math.Abs(window.Bounds.Width - width) < 2 && Math.Abs(window.Bounds.Height - height) < 2, page + " renders exact viewport " + width + "x" + height);
        }
    }

    public async Task Failure(Exception error)
    {
        Directory.CreateDirectory(Output);
        await File.WriteAllTextAsync(Path.Combine(Output, "failure.txt"), error.ToString());
        await File.WriteAllTextAsync(Path.Combine(Output, "failure-evidence.json"), JsonSerializer.Serialize(new { checks, images, error = error.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
        if (visible?.IsVisible == true)
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize(Math.Max(1, (int)visible.Bounds.Width), Math.Max(1, (int)visible.Bounds.Height)), new Vector(96, 96));
            bitmap.Render(visible); bitmap.Save(Path.Combine(Output, "failure.png"), new PngBitmapEncoderOptions());
        }
    }

    private void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        checks.Add(description); Console.WriteLine("PASS " + description);
    }
    private async Task Wait(Func<bool> condition, string description, int timeout = 5000)
    {
        var watch = Stopwatch.StartNew();
        while (!condition()) { if (watch.ElapsedMilliseconds >= timeout) throw new Exception("Timed out: " + description); await Task.Delay(40); }
        Check(true, description);
    }
}

internal sealed class TrayInputs
{
    public bool Available = true;
    public int Probes, PollCalls;
    public TaskCompletionSource<bool>? AvailabilityGate;
    public string[] Lines = ["Fixture account · 5h usage 70%"];
    public DesktopAlert? Alert;
    public Task<bool> Availability(CancellationToken token) { Probes++; return AvailabilityGate?.Task.WaitAsync(token) ?? Task.FromResult(Available); }
    public Task<TrayStatusSnapshot> Poll(DateTimeOffset now, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); PollCalls++;
        return Task.FromResult(new TrayStatusSnapshot("lain private fixture", Lines, PollCalls == 1 && Alert is not null ? [Alert] : [], true));
    }
}
internal sealed class FakeSink : IDesktopNotificationSink
{
    public int Attempts;
    public Task<bool> SendAsync(DesktopAlert alert, CancellationToken token = default) { token.ThrowIfCancellationRequested(); Attempts++; return Task.FromResult(true); }
}
internal sealed class UiExitProbe
{
    public int ShutdownCalls;
    public void Quit() => ShutdownCalls++;
}

internal sealed record IpcResult(bool Owner, bool Reopened);
internal static class IpcProbe
{
    private static readonly Type Type = typeof(TrayWorkspaceController).Assembly.GetType("Lain.Desktop.Tray.UiInstanceOwner", throwOnError: true)!;
    private static object Create(string state) => Activator.CreateInstance(Type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [state], null)!;
    private static bool IsOwner(object owner) => (bool)Type.GetProperty("IsOwner")!.GetValue(owner)!;
    public static int Owner(string state, string ready, string reopened, string stop)
    {
        // A named Mutex belongs to its creating thread; do not await before Dispose.
        using var owner = (IDisposable)Create(state);
        if (!IsOwner(owner)) return 3;
        Type.GetMethod("StartListening")!.Invoke(owner, [(Action)(() => File.WriteAllText(reopened, "reopen"))]);
        File.WriteAllText(ready, "owner");
        var deadline = Stopwatch.StartNew();
        while (!File.Exists(stop) && deadline.Elapsed < TimeSpan.FromSeconds(30)) Thread.Sleep(20);
        return File.Exists(stop) ? 0 : 4;
    }
    public static int Client(string state, string result)
    {
        using var owner = (IDisposable)Create(state);
        var owns = IsOwner(owner);
        var reopened = !owns && ((Task<bool>)Type.GetMethod("RequestReopenAsync")!.Invoke(owner, [CancellationToken.None])!).GetAwaiter().GetResult();
        File.WriteAllText(result, JsonSerializer.Serialize(new IpcResult(owns, reopened)));
        return 0;
    }
}
