using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lain.Client;
using Lain.Client.Lifecycle;
using Lain.Cli;
using Lain.Core;

var checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
var root = Path.Combine(Path.GetTempPath(), "lain-lifecycle-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var originalConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
try
{
    var manager = new FakeManager();
    var handler = new FakeHost { Status = Ready() };
    using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
    LainApiClient Client() => new(http, "isolated-host-token");
    var installation = new InstallationMetadata(1, "linux", root, Path.Combine(root, "app"), "unused", "unused", "unused", null,
        "systemd", "lain.service", Path.Combine(root, "unit"), true);
    var lifecycle = new UserHostLifecycle(installation, manager, Client, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(1), root);

    var result = await lifecycle.EnsureRunningAsync();
    Check(result.IsReady && manager.Starts == 0 && handler.Actions.Count == 0, "healthy Host/Core must survive opening without manager restart or Core write");
    Check(result.Host!.Revision == 8 && result.Host.AppliedRevision == 7, "newer saved revision remains pending");
    handler.Status = Ready() with { ProxyReady = false, RuntimeMode = "monitor", AppliedRevision = 0 };
    result = await lifecycle.EnsureRunningAsync();
    Check(result.IsReady && result.CoreState == "not-required" && handler.Actions.Count == 0, "Monitor never starts a Core");
    handler.Status = Ready() with { ProxyReady = false, RuntimeLifecycle = new(false, "stopped"), Application = new("completed", null, "applied", 7) };
    result = await lifecycle.EnsureRunningAsync();
    Check(result.IsReady && handler.Actions.SequenceEqual(["ensure"]), "eligible Gateway resumes through Host ensure only");
    Check(result.Host!.Revision == 8 && result.Host.AppliedRevision == 7, "Host ensure preserves pending saved revision");
    handler.Actions.Clear();
    handler.Status = Ready() with { ProxyReady = false, RuntimeLifecycle = new(true, "suppressed") };
    result = await lifecycle.EnsureRunningAsync();
    Check(result.CoreState == "suppressed" && handler.Actions.Count == 0, "automatic open respects explicit Stop intent");
    result = await lifecycle.StartAsync();
    Check(result.IsReady && handler.Actions.SequenceEqual(["start"]), "explicit lifecycle Start delegates clear-suppression policy to Host");
    handler.Actions.Clear();
    handler.Status = Ready() with { ProxyReady = false, Application = new("recovery-required", "op", "profile", 7) };
    result = await lifecycle.EnsureRunningAsync();
    Check(result.CoreState == "recovery-required" && handler.Actions.Count == 0, "startup leaves application recovery explicit");
    handler.Status = Ready() with { ProxyReady = false, AppliedRevision = 0, ActiveProfileId = null };
    result = await lifecycle.EnsureRunningAsync();
    Check(result.CoreState == "setup-required" && handler.Actions.Count == 0, "missing applied profile requires setup without Apply");
    handler.Status = Ready() with { ProxyReady = false, Error = "Core health deadline expired.", RuntimeLifecycle = new(false, "timeout", "health pending", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, CompletedAt: DateTimeOffset.UtcNow) };
    result = await lifecycle.EnsureRunningAsync();
    Check(result.CoreState == "timeout" && handler.Actions.Count == 0, "opening does not retry an unqualified Core startup failure");

    handler.Available = false;
    manager.OnStart = () => handler.Available = true;
    handler.Status = Ready();
    result = await lifecycle.EnsureRunningAsync();
    Check(result.IsReady && manager.Starts == 1, "missing first Host credential can be discovered again after OS-manager startup");
    await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => lifecycle.EnsureRunningAsync()));
    Check(manager.Starts == 1, "concurrent opens keep a healthy Host without duplicate manager starts");
    handler.AuthorizationFailure = true;
    try { await lifecycle.EnsureRunningAsync(); throw new Exception("foreign endpoint accepted"); }
    catch (IOException) { Check(manager.Starts == 1, "foreign credential failure never starts or restarts a process"); }
    handler.AuthorizationFailure = false;
    handler.Status = Ready() with { ContractVersion = 99 };
    try { await lifecycle.EnsureRunningAsync(); throw new Exception("incompatible Host accepted"); }
    catch (IOException) { Check(manager.Restarts == 0, "incompatible Host never adopted or restarted"); }
    handler.Status = Ready();
    await lifecycle.SetLoginAutostartAsync(false);
    Check(manager.Login == false && manager.Stops == 0 && manager.Starts == 1, "disabling future login startup does not stop current Host");

    var output = new StringWriter(); var error = new StringWriter();
    handler.Available = false;
    var exit = await CliApplication.RunAsync(["service", "status", "--json"], output, error, Client, lifecycleFactory: () => lifecycle);
    using (var document = JsonDocument.Parse(output.ToString()))
        Check(exit == 0 && error.ToString() == "" && document.RootElement.GetProperty("hostState").GetString() == "unavailable"
            && document.RootElement.GetProperty("manager").GetProperty("loginAutostart").GetBoolean() == false,
            "service status explains manager/login state even without Host");
    exit = await CliApplication.RunAsync(["service", "autostart", "maybe"], new StringWriter(), new StringWriter(),
        () => throw new Exception("invalid service syntax opened client"), lifecycleFactory: () => throw new Exception("invalid syntax opened manager"));
    Check(exit == 2, "invalid lifecycle syntax validates before discovery");

    var preferences = new JsonStartupPreferencesStore(root);
    Check((await preferences.ReadAsync()).StartOnOpen, "start on open defaults on independently of login state");
    await preferences.WriteAsync(new(false));
    Check(!(await preferences.ReadAsync()).StartOnOpen, "start-on-open preference persists without touching login startup");

    // Resolve native versioned binaries and Windows executable suffixes from installer metadata.
    var nativeRoot = Path.Combine(root, "native root");
    var binaryRoot = Path.Combine(nativeRoot, "versions", "payload");
    var native = new InstallationMetadata(1, "windows", nativeRoot, binaryRoot, Path.Combine(binaryRoot, "host", "Lain.Host.exe"),
        Path.Combine(binaryRoot, "cli", "Lain.Cli.exe"), Path.Combine(binaryRoot, "desktop", "Lain.Desktop.exe"), null,
        "task-scheduler", "Lain-test", Path.Combine(nativeRoot, "task.xml"), true);
    foreach (var path in new[] { native.HostPath, native.CliPath, native.DesktopPath }) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "fixture"); }
    var nativeState = Path.Combine(nativeRoot, "data"); Directory.CreateDirectory(nativeState);
    File.WriteAllText(Path.Combine(nativeState, "installation.json"), JsonSerializer.Serialize(native, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    Check(InstallationDiscovery.Discover(nativeState).DesktopPath == native.DesktopPath, "native UI discovery honors versioned root, spaces, data directory and exe suffix");

    var config = Path.Combine(root, "xdg-config"); Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", config);
    var app = Path.Combine(root, "app"); Directory.CreateDirectory(Path.Combine(app, "packaging"));
    var unit = Path.Combine(config, "systemd", "user", "lain.service"); Directory.CreateDirectory(Path.GetDirectoryName(unit)!);
    var template = "[Service]\nWorkingDirectory=@WORKING_DIRECTORY@\nEnvironment=\"LAIN_HOME=@HOME@\"\nExecStart=\"@HOME@/app/host/Lain.Host\"\n";
    File.WriteAllText(Path.Combine(app, "packaging", "lain.service.in"), template);
    File.WriteAllText(unit, template.Replace("@WORKING_DIRECTORY@", root).Replace("@HOME@", root));
    var installedHost = Path.Combine(app, "host", "Lain.Host");
    var process = new FakeProcess { Output = $"LoadState=loaded\nUnitFileState=disabled\nActiveState=inactive\nFragmentPath={unit}\nDropInPaths=\nExecStart={{ path={installedHost} ; argv[]={installedHost} ; ignore_errors=no ; }}\nNeedDaemonReload=no\n" };
    var linux = new LinuxUserServiceManager(installation with { RegistrationPath = unit, HostPath = installedHost }, process);
    Check((await linux.InspectAsync()).RegistrationState == "registered", "Linux accepts exact installed user unit");
    await linux.SetLoginAutostartAsync(true);
    Check(process.Calls.Last().SequenceEqual(["--user", "enable", "lain.service"]), "Linux enable does not imply --now or start");
    File.AppendAllText(unit, "ExecStartPost=/foreign/command\n");
    var callCount = process.Calls.Count;
    Check((await linux.InspectAsync()).RegistrationState == "modified" && process.Calls.Count == callCount, "custom user unit is rejected before any manager call");
    try { await linux.StartAsync(); throw new Exception("modified unit started"); }
    catch (IOException) { Check(process.Calls.Count == callCount, "modified registration cannot start a foreign process"); }
}
finally
{
    Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", originalConfig);
    Directory.Delete(root, true);
}
Console.WriteLine($"PASS: {checks} isolated lifecycle checks.");

static StatusDto Ready() => new(Api.Version, "test", "ready", true, 8, 7, DateTimeOffset.UtcNow);

sealed class FakeManager : IUserServiceManager
{
    public int Starts, Stops, Restarts;
    public bool Login = true;
    public Action? OnStart;
    public Task<UserServiceStatus> InspectAsync(CancellationToken ct = default) => Task.FromResult(new UserServiceStatus("fixture", "registered", Login, "running"));
    public Task StartAsync(CancellationToken ct = default) { Starts++; OnStart?.Invoke(); return Task.CompletedTask; }
    public Task StopAsync(CancellationToken ct = default) { Stops++; return Task.CompletedTask; }
    public Task RestartAsync(CancellationToken ct = default) { Restarts++; return Task.CompletedTask; }
    public Task SetLoginAutostartAsync(bool enabled, CancellationToken ct = default) { Login = enabled; return Task.CompletedTask; }
}

sealed class FakeHost : HttpMessageHandler
{
    public StatusDto Status = null!;
    public bool Available = true, AuthorizationFailure;
    public readonly List<string> Actions = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (!Available) throw new IOException("first startup token not created");
        if (request.Headers.Authorization?.Parameter != "isolated-host-token") throw new Exception("readiness was not authenticated");
        if (AuthorizationFailure) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        if (request.RequestUri!.AbsolutePath.StartsWith(Api.Prefix + "/service/", StringComparison.Ordinal))
        {
            Actions.Add(request.RequestUri.Segments.Last());
            Status = Status with { ProxyReady = true, RuntimeLifecycle = new(false, "ready") };
        }
        else if (request.RequestUri.AbsolutePath != Api.Prefix + "/status") throw new Exception("startup touched a non-lifecycle endpoint");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Status) });
    }
}

sealed class FakeProcess : ILifecycleProcessRunner
{
    public string Output = "";
    public readonly List<string[]> Calls = [];
    public Task<LifecycleProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken ct = default)
    {
        if (executable != "systemctl") throw new Exception("unexpected manager executable");
        Calls.Add(arguments.ToArray());
        return Task.FromResult(new LifecycleProcessResult(0, Output, ""));
    }
}
