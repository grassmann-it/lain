using System.Diagnostics;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using Lain.Client;
using Lain.Core;
using Lain.Desktop;

internal static class Program
{
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    internal static string Output => Environment.GetEnvironmentVariable("LAIN_INSTALLED_OUTPUT") ?? throw new Exception("Private runner output missing");
    internal static string Preferences => Path.Combine(Environment.GetEnvironmentVariable("LAIN_INSTALLED_RUNTIME") ?? throw new Exception("Private runtime missing"), "preferences.json");
    internal static readonly List<object> Assemblies = [];
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            var pid = Environment.GetEnvironmentVariable("LAIN_TEST_XVFB_PID");
            var display = Environment.GetEnvironmentVariable("DISPLAY");
            if (!int.TryParse(pid, out _) || display is null || display != Environment.GetEnvironmentVariable("LAIN_TEST_DISPLAY") ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) ||
                !File.Exists($"/proc/{pid}/cmdline")) throw new Exception("Private Xvfb required");
            var command = File.ReadAllText($"/proc/{pid}/cmdline").Split('\0');
            if (Path.GetFileName(command[0]) != "Xvfb" || !command.Contains("-displayfd") ||
                !command.Contains(Environment.GetEnvironmentVariable("XAUTHORITY"))) throw new Exception("Runner-owned Xvfb required");
            VerifyAssemblies();
            return AppBuilder.Configure<ProbeApp>().UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime([]);
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 2; }
    }
    internal static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    internal static void VerifyAssemblies()
    {
        var manifest = JsonSerializer.Deserialize<Dictionary<string, TargetAssembly>>(File.ReadAllText(
            Environment.GetEnvironmentVariable("LAIN_INSTALLED_MANIFEST") ?? throw new Exception("Assembly manifest missing")))!;
        Assemblies.Clear();
        foreach (var assembly in new[] { typeof(MainWindow).Assembly, typeof(LainApiClient).Assembly, typeof(StatusDto).Assembly, typeof(Window).Assembly })
        {
            var name = assembly.GetName().Name!;
            var expected = manifest[name];
            if (Hash(assembly.Location) != expected.Sha256 || Hash(expected.Source) != expected.Sha256 ||
                Path.GetFullPath(assembly.Location) != Path.GetFullPath(expected.Copy)) throw new Exception("Wrong target assembly: " + name);
            Assemblies.Add(new { name, source = expected.Source, loadedPath = assembly.Location, sha256 = expected.Sha256, version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion });
        }
    }
    internal sealed record TargetAssembly(string Source, string Copy, string Sha256);
}

internal sealed class ProbeApp : Application
{
    public override void Initialize()
    {
        var production = new App();
        production.Initialize();
        RequestedThemeVariant = production.RequestedThemeVariant;
        Resources = production.Resources;
        production.Resources = new ResourceDictionary();
        var styles = production.Styles.ToArray(); production.Styles.Clear();
        foreach (var style in styles) Styles.Add(style);
    }
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Dispatcher.UIThread.Post(async () =>
        {
            var probe = new Probe();
            try { await probe.Run(); lifetime.Shutdown(0); }
            catch (Exception error) { await probe.Failure(error); Console.Error.WriteLine(error); lifetime.Shutdown(1); }
        });
        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class ReadOnlyHost : DelegatingHandler
{
    private static readonly HashSet<string> Allowed = ["status", "configuration", "accounts", "models", "routes", "profiles", "quotas", "providers", "usage", "usage/analysis", "attempts", "inspector", "inspector/events"];
    internal readonly ConcurrentQueue<object> Requests = new();
    internal readonly ConcurrentQueue<string> Blocked = new();
    internal ReadOnlyHost() : base(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri ?? throw new Exception("Missing request URI");
        var path = uri.AbsolutePath.StartsWith(Api.Prefix + "/", StringComparison.Ordinal) ? uri.AbsolutePath[(Api.Prefix.Length + 1)..] : "";
        if (request.Method != HttpMethod.Get || uri.Scheme != "http" || uri.Host != "127.0.0.1" || uri.Port != 18418 ||
            !string.IsNullOrEmpty(uri.UserInfo) || !Allowed.Contains(path) || request.Content is not null)
        {
            Blocked.Enqueue(request.Method + " " + uri.AbsolutePath);
            throw new InvalidOperationException("Blocked non-allowlisted host request");
        }
        var response = await base.SendAsync(request, ct);
        Requests.Enqueue(new { method = "GET", path = uri.PathAndQuery, status = (int)response.StatusCode });
        return response;
    }
}

internal sealed class Probe
{
    private readonly ReadOnlyHost guard = new();
    private readonly List<string> checks = [];
    private readonly List<object> screenshots = [];
    private readonly List<object> quotas = [];
    private readonly List<object> profileStates = [];
    private readonly List<HttpClient> transports = [];
    private MainWindow? window;
    private string? before;
    private UsageQuery query = null!;
    private static readonly string[] Destinations = ["Accounts", "Models", "Profiles", "Usage", "Live"];
    private LainApiClient Client()
    {
        // One transport gate covers every API request from the actual production UI.
        var http = new HttpClient(guard, disposeHandler: false) { BaseAddress = LocalDiscovery.ApiAddress, Timeout = TimeSpan.FromSeconds(30) };
        transports.Add(http);
        return new LainApiClient(http, LocalDiscovery.ReadHostToken());
    }
    internal async Task Run()
    {
        Directory.CreateDirectory(Program.Output);
        var from = DateTimeOffset.Parse(Environment.GetEnvironmentVariable("LAIN_USAGE_FROM")!, CultureInfo.InvariantCulture);
        var to = DateTimeOffset.Parse(Environment.GetEnvironmentVariable("LAIN_USAGE_TO")!, CultureInfo.InvariantCulture);
        Check(from < to && to <= DateTimeOffset.UtcNow && to - from <= TimeSpan.FromDays(366), "fixed historical range is valid");
        query = new UsageQuery(From: from, To: to, TimeZone: "UTC", Limit: 100);
        using var observer = Client();
        var status = await observer.StatusAsync();
        Check(status.ProxyReady, "own host and proxy ready before opening");
        var config = await observer.GetConfigurationAsync();
        var initial = await observer.UsageAsync(query);
        before = StableUsage(initial);
        await Open();
        Check(Controls<Button>().Where(x => x.Classes.Contains("nav")).Select(x => x.Tag?.ToString()).SequenceEqual(Destinations), "exactly five primary destinations");
        Check(Controls<Button>().Single(x => x.Classes.Contains("nav") && Equals(x.Tag, "Accounts")) is var initialNav && AutomationProperties.GetHelpText(initialNav) == "Current page", "Accounts is initial destination");
        var image = Controls<Image>().Single(x => AutomationProperties.GetName(x) == "Application logo");
        Check(image.Source is not null && image.Bounds.Width > 0 && image.Bounds.Height > 0 && window!.Icon is not null, "original cat logo and native window icon are present");
        await ReadAccountWindows(observer, config);
        await CaptureBoth("accounts-simple");
        foreach (var destination in Destinations.Skip(1))
        {
            await Click(destination);
            if (destination == "Models") await Wait(() => Controls<TextBox>().Any(x => AutomationProperties.GetName(x) == "Search configured model access"), "Models configured inventory loaded");
            if (destination == "Usage") await HistoricalUsage();
            if (destination == "Profiles")
            {
                var text = Controls<TextBlock>().Single(x => AutomationProperties.GetName(x) == "Applied proxy profile status").Text;
                Check(!string.IsNullOrWhiteSpace(text), "Profiles reports applied state including explicit unknown");
                profileStates.Add(new { hostActiveProfileId = Property(status, "ActiveProfileId"), hostActiveProfileRevision = Property(status, "ActiveProfileRevision"), displayed = text });
                if (Property(status, "ActiveProfileId") is null) Check(text!.Contains("not reported", StringComparison.OrdinalIgnoreCase), "missing host profile ID remains unknown");
            }
            if (destination == "Live")
            {
                await Wait(() => guard.Requests.Any(x => JsonSerializer.Serialize(x).Contains("/inspector")), "Live reads the local inspector");
                await Wait(() => Controls<TextBlock>().Any(x => AutomationProperties.GetName(x) == "Inspector status" &&
                    (x.Text?.Contains("Connected", StringComparison.Ordinal) == true || x.Text?.Contains("Waiting", StringComparison.Ordinal) == true)), "Live reports its observed local source state");
            }
            await CaptureBoth(destination.ToLowerInvariant() + "-simple");
        }
        await Click("Accounts"); await Click("Discover providers");
        await Wait(() => Controls<TextBox>().Any(x => AutomationProperties.GetName(x) == "Search providers"), "credential-free provider discovery opens");
        await Wait(() => guard.Requests.Any(x => JsonSerializer.Serialize(x).Contains("/providers")), "provider catalog read from own host");
        var catalog = await observer.ProvidersAsync();
        foreach (var provider in catalog.Providers)
            Check(AllControls<TextBlock>().Any(x => x.Text == provider.Name), "discovery shows real catalog provider " + provider.Name);
        await CaptureBoth("discover-providers");
        await Click("Accounts");
        foreach (var preset in new[] { "Barebones", "Advanced", "Simple" })
        {
            await Click("View");
            Named<ComboBox>("Information level").SelectedIndex = Array.IndexOf(new[] { "Barebones", "Simple", "Advanced" }, preset);
            Named<ComboBox>("View density").SelectedIndex = preset == "Barebones" ? 1 : 0;
            await Click("Close view options");
            await Wait(() => File.Exists(Program.Preferences), "presentation writes only isolated preferences");
            Check(File.ReadAllText(Program.Preferences).Contains(preset, StringComparison.Ordinal), preset + " is saved in isolated preferences");
            Check(Controls<Button>().Count(x => x.Classes.Contains("nav")) == 5, preset + " keeps all five destinations");
            await CaptureBoth("accounts-" + preset.ToLowerInvariant());
        }
        await Click("Usage"); await HistoricalUsage();
        window!.Close();
        await Task.Delay(350);
        var closedStatus = await observer.StatusAsync();
        Check(closedStatus.ProxyReady, "host remains ready while window is closed");
        Check(StableUsage(await observer.UsageAsync(query)) == before, "fixed historical usage unchanged while closed");
        await Open(); await Click("Usage"); await HistoricalUsage(); await CaptureBoth("reopened-usage");
        window!.Close(); await Task.Delay(200);
        var final = await observer.UsageAsync(query);
        var finalConfig = await observer.GetConfigurationAsync();
        Check(StableUsage(final) == before, "fixed historical usage unchanged after reopen");
        Check(JsonSerializer.Serialize(config) == JsonSerializer.Serialize(finalConfig), "configuration unchanged after read-only workflow");
        var finalStatus = await observer.StatusAsync();
        Check(finalStatus.ProxyReady, "host remains ready after GUI lifecycle");
        Check(guard.Blocked.Count == 0, "all HTTP requests passed GET-only endpoint allowlist");
        Check(!File.Exists(Path.Combine(Path.GetDirectoryName(Program.Preferences)!, "blocked-browser")), "no browser or external-launch attempt");
        Program.VerifyAssemblies();
        if (File.Exists(Program.Preferences)) File.Copy(Program.Preferences, Path.Combine(Program.Output, "preferences-observed.json"), overwrite: true);
        await Write("evidence.json", new { result = "PASS", privateXvfb = true, display = Environment.GetEnvironmentVariable("DISPLAY"),
            assemblies = Program.Assemblies, checks, screenshots, accountWindows = quotas, profileStates, requests = guard.Requests,
            blocked = guard.Blocked, historicalUsage = new { from, to, before = initial, after = final }, configurationRevision = config.Revision,
            isolatedPreferences = Program.Preferences, hostBefore = status, hostWhileClosed = closedStatus, hostAfter = finalStatus,
            runtime = new { framework = RuntimeInformation.FrameworkDescription, version = Environment.Version.ToString(), os = RuntimeInformation.OSDescription },
            scope = "Exact selected Desktop/Client/Core/Avalonia.Controls assemblies and production App/MainWindow on real own host, GET-only. Routed button events and RenderTargetBitmap captures; no OS pointer hit testing, launcher/taskbar rendering, provider refresh, inference, configuration write, login or browser. Live content is observed, never fabricated. Historical range is fixed; concurrent external host activity is not disabled or attributed to this probe. Window reopen occurs in one process; preferences are a separate temporary file." });
        foreach (var http in transports) http.Dispose();
        guard.Dispose();
        Console.WriteLine("PASS exact-target installed UI probe: " + checks.Count + " checks");
    }
    private async Task Open()
    {
        var constructor = typeof(MainWindow).GetConstructor([typeof(LainApiClient), typeof(string)])
            ?? throw new Exception("Target must expose MainWindow(client, preferencesPath); refusing installed preference fallback");
        window = (MainWindow)constructor.Invoke([Client(), Program.Preferences]);
        window.Show();
        await Wait(() => Controls<TextBlock>().Any(x => x.Text?.Contains("Local host", StringComparison.Ordinal) == true), "production window connects to own host");
        await Wait(() => Controls<TextBox>().Any(x => AutomationProperties.GetName(x) == "Search accounts"), "Accounts inventory loaded");
    }
    private async Task ReadAccountWindows(LainApiClient observer, ConfigurationDto config)
    {
        var rows = await observer.QuotasAsync();
        foreach (var account in config.Accounts)
        {
            var tile = Controls<Expander>().Single(x => AutomationProperties.GetName(x) == "Account budgets for " + account.Name);
            var header = (Control)tile.Header!;
            var texts = new[] { header }.Concat(header.GetVisualDescendants().OfType<Control>()).OfType<TextBlock>().Select(x => x.Text ?? "").ToArray();
            var windows = rows.Where(x => x.AccountId == account.Id).ToArray();
            foreach (var quota in windows)
            {
                if (!string.IsNullOrWhiteSpace(quota.WindowLabel)) Check(texts.Contains(quota.WindowLabel), "named quota window shown for " + account.Name + ": " + quota.WindowLabel);
                if (quota.UsedPercent is >= 0 and <= 100 && quota.State is not ("unsupported" or "not-supported"))
                    Check(texts.Contains(quota.UsedPercent.Value.ToString("#,0.##", CultureInfo.CurrentCulture) + "% used"), "reported consumed percentage shown for " + account.Name);
            }
            Check(texts.Any(x => x.Contains("left") || x.Contains("Remaining") || x.Contains("not supplied")), "remaining state is explicit for " + account.Name);
            quotas.Add(new { accountId = account.Id, account.Provider, windowCount = windows.Length, windows, displayedHeader = texts });
        }
        Check(Controls<Expander>().Count(x => AutomationProperties.GetName(x)?.StartsWith("Account budgets for ", StringComparison.Ordinal) == true) == config.Accounts.Length, "Accounts displays the complete configured inventory");
    }
    private async Task HistoricalUsage()
    {
        await Wait(() => Controls<TextBox>().Any(x => AutomationProperties.GetName(x) == "From · inclusive"), "Usage range controls available");
        Named<TextBox>("From · inclusive").Text = query.From!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Named<TextBox>("To · exclusive").Text = query.To!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Named<TextBox>("Time zone").Text = "UTC";
        await Click("Apply");
        await Wait(() => AllControls<TextBlock>().Any(x => x.Text == "Connected · source: lain host / persistent usage API"), "Usage reads persistent own-host history");
        var range = query.From.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " → " + query.To.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        await Wait(() => AllControls<TextBlock>().Any(x => x.Text?.Contains("Applied range: " + range, StringComparison.Ordinal) == true), "Usage renders the fixed historical range");
    }
    private static object? Property(object source, string name) => source.GetType().GetProperty(name)?.GetValue(source);
    private static string StableUsage(UsageSummaryDto value) => JsonSerializer.Serialize(new { value.From, value.To, value.TimeZone, value.Complete, value.Totals, value.Daily, value.Accounts, value.Models });
    private IEnumerable<T> Controls<T>() where T : Control => window!.GetVisualDescendants().OfType<T>().Where(x => x.IsVisible && x.GetVisualAncestors().OfType<Control>().All(parent => parent.IsVisible));
    private IEnumerable<T> AllControls<T>() where T : Control
    {
        var current = window ?? throw new InvalidOperationException("Probe window is not open");
        return current.GetVisualDescendants().OfType<T>().Concat(current.GetLogicalDescendants().OfType<T>()).Distinct();
    }
    private T Named<T>(string name) where T : Control => Controls<T>().Single(x => AutomationProperties.GetName(x) == name);
    private async Task Click(string name)
    {
        if (!Destinations.Contains(name) && name is not ("Discover providers" or "View" or "Close view options" or "Apply")) throw new Exception("Nonallowlisted UI click");
        var button = Controls<Button>().First(x => x.IsEnabled && (AutomationProperties.GetName(x) == name || Equals(x.Content, name)));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(180);
    }
    private async Task CaptureBoth(string page)
    {
        foreach (var (width, height) in new[] { (1120, 780), (800, 560) })
        {
            window!.Width = width; window.Height = height;
            await Wait(() => Math.Abs(window.Bounds.Width - width) < 2 && Math.Abs(window.Bounds.Height - height) < 2, page + " viewport " + width + "x" + height);
            await Task.Delay(120); window.UpdateLayout();
            var file = page + "-" + width + "x" + height + ".png";
            Capture(file);
            screenshots.Add(new { page, width, height, actualWidth = window.Bounds.Width, actualHeight = window.Bounds.Height, file, sha256 = Program.Hash(Path.Combine(Program.Output, file)) });
        }
        window!.Width = 1120; window.Height = 780; await Task.Delay(100);
    }
    private void Capture(string filename)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(Math.Max(1, (int)Math.Ceiling(window!.Bounds.Width)), Math.Max(1, (int)Math.Ceiling(window.Bounds.Height))), new Vector(96, 96));
        bitmap.Render(window); bitmap.Save(Path.Combine(Program.Output, filename), new PngBitmapEncoderOptions());
    }
    private void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks.Add(name); Console.WriteLine("PASS " + name); }
    private async Task Wait(Func<bool> condition, string name)
    {
        var timer = Stopwatch.StartNew();
        while (!condition()) { if (timer.ElapsedMilliseconds > 12000) throw new Exception("Timeout: " + name); await Task.Delay(60); }
        Check(true, name);
    }
    private static Task Write(string filename, object value) => File.WriteAllTextAsync(Path.Combine(Program.Output, filename), JsonSerializer.Serialize(value, Program.Json));
    internal async Task Failure(Exception error)
    {
        Directory.CreateDirectory(Program.Output);
        try { if (window is not null && window.IsVisible) Capture("failure.png"); } catch { }
        await Write("failure.json", new { result = "FAIL", error = error.ToString(), checks, screenshots, requests = guard.Requests, blocked = guard.Blocked, assemblies = Program.Assemblies });
        window?.Close(); foreach (var http in transports) http.Dispose(); guard.Dispose();
    }
}
