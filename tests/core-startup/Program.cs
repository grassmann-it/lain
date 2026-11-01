using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Models;
using Lain.Host.Operations;

if (args.Contains("--parent-stdin")) return await FakeCore.RunAsync(args);

var root = Path.Combine(Path.GetTempPath(), "lain-core-startup-" + Guid.NewGuid().ToString("N"));
var checks = 0;
var previousIsolation = Environment.GetEnvironmentVariable("LAIN_REFERENCE_QUALIFICATION");
var previousPort = Environment.GetEnvironmentVariable("LAIN_REFERENCE_PROXY_PORT");
void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
async Task Reject(Func<Task> action)
{
    try { await action(); }
    catch (Exception error) when (error is InvalidOperationException or IOException or OperationCanceledException) { return; }
    throw new Exception("Expected startup rejection.");
}
RuntimeStartupService Service(RuntimeStartIntent intent, FakeStartup fake, Action? require = null,
    bool gateway = true, bool credentials = true, string application = "completed") =>
    new(intent, fake, () => gateway, () => credentials, () => new(application, null, "applied", 2), require ?? (() => { }));
try
{
    Environment.SetEnvironmentVariable("LAIN_REFERENCE_QUALIFICATION", "1");
    Environment.SetEnvironmentVariable("LAIN_REFERENCE_PROXY_PORT", FreePort().ToString());
    var paths = new OwnStatePaths(Path.Combine(root, "intent"));
    var intent = new RuntimeStartIntent(paths);
    var fake = new FakeStartup();
    var service = Service(intent, fake);
    await service.StopAsync();
    Check(new RuntimeStartIntent(paths).AutomaticStartSuppressed, "explicit Stop persists across Host lifetimes");
    await service.EnsureAsync();
    Check(fake.Starts == 0 && service.Status is { AutomaticStartSuppressed: true, Stage: "suppressed" }, "ensure preserves explicit Stop");
    var blocked = Service(intent, fake, () => throw new InvalidOperationException());
    await Reject(() => blocked.StartAsync(false));
    Check(intent.AutomaticStartSuppressed && fake.Starts == 0, "invalid applied state cannot clear Stop intent");
    fake.Valid = false;
    await Reject(() => service.StartAsync(false));
    Check(new RuntimeStartIntent(paths).AutomaticStartSuppressed, "failed executable qualification cannot clear Stop intent");
    fake.Valid = true;
    await service.StartAsync(false);
    Check(!new RuntimeStartIntent(paths).AutomaticStartSuppressed && fake.Status.Ready, "validated explicit Start clears persistent suppression");
    await service.StopAsync();
    await service.StartAsync(true);
    Check(!intent.AutomaticStartSuppressed && fake.Restarts == 1, "validated explicit Restart resumes automatic startup");

    var corruptPaths = new OwnStatePaths(Path.Combine(root, "corrupt-intent"));
    File.WriteAllText(Path.Combine(corruptPaths.Root, "runtime-start-intent.json"), "not-json-private-output");
    Check(new RuntimeStartIntent(corruptPaths).AutomaticStartSuppressed, "unreadable persisted intent fails closed");

    var concurrent = new FakeStartup { Delay = TimeSpan.FromMilliseconds(25) };
    var concurrencyService = Service(new RuntimeStartIntent(new OwnStatePaths(Path.Combine(root, "concurrent"))), concurrent);
    await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => concurrencyService.EnsureAsync()));
    Check(concurrent.Starts == 1, "concurrent ensures launch one owned Core");
    var failing = new FakeStartup { Valid = false };
    var failureService = Service(new RuntimeStartIntent(new OwnStatePaths(Path.Combine(root, "failed"))), failing);
    await failureService.EnsureAsync();
    await failureService.EnsureAsync();
    Check(failing.Starts == 1 && failureService.Status.Stage == "qualification", "unclassified startup failure is retained without an automatic retry");
    foreach (var variant in new[] { "monitor", "credentials", "application", "login" })
    {
        var inactive = new FakeStartup();
        if (variant == "login") inactive.SetLoginOnly();
        var policy = Service(new RuntimeStartIntent(new OwnStatePaths(Path.Combine(root, variant))), inactive,
            gateway: variant != "monitor", credentials: variant != "credentials", application: variant == "application" ? "recovery-required" : "completed");
        await policy.EnsureAsync();
        Check(inactive.Starts == 0, variant + " cannot implicitly start or convert inference");
    }

    // The automatic path validates completed applied proof while newer saves
    // remain pending, and never invokes an Apply or writes any runtime file.
    var appliedPaths = new OwnStatePaths(Path.Combine(root, "pending"));
    var repository = new ConfigurationRepository(appliedPaths);
    repository.Update(0, c => c); repository.Update(1, c => c); repository.MarkApplied(2); repository.Update(2, c => c);
    OwnStatePaths.WritePrivate(Path.Combine(appliedPaths.Root, "active-profile.json"), """{"id":"applied","revision":2,"generationId":"completed-generation"}""");
    OwnStatePaths.WritePrivate(Path.Combine(appliedPaths.Proxy, "runtime.json"), """{"valid":true,"revision":2,"profileId":"applied","generationId":"completed-generation"}""");
    OwnStatePaths.WritePrivate(Path.Combine(appliedPaths.Root, "profile-application.json"), """{"id":"completed-generation","profileId":"applied","revision":2,"state":"completed"}""");
    OwnStatePaths.WritePrivate(appliedPaths.ProxyConfig, "PRESERVE APPLIED CONFIG");
    var applicationService = new ProfileApplicationService(appliedPaths, repository, new ModelsService(repository),
        _ => throw new Exception("Automatic startup must not drain or apply."),
        (_, _, _) => throw new Exception("Automatic startup must not publish."),
        _ => throw new Exception("Automatic startup must not repair."));
    var before = Directory.GetFiles(appliedPaths.Root, "*", SearchOption.AllDirectories).ToDictionary(file => file, File.ReadAllBytes);
    var pendingFake = new FakeStartup();
    var pendingService = Service(new RuntimeStartIntent(appliedPaths), pendingFake, applicationService.RequireCompletedRuntime);
    await pendingService.EnsureAsync();
    Check(pendingFake.Starts == 1 && repository.Snapshot is { Revision: 3, AppliedRevision: 2 } &&
        before.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)) &&
        Directory.GetFiles(appliedPaths.Root, "*", SearchOption.AllDirectories).Length == before.Count,
        "ensure resumes applied revision while pending saves and proof remain byte-for-byte unchanged");

    foreach (var item in new[]
    {
        ("{\"ready\":false,\"journalHealthy\":false,\"error\":\"PRIVATE SECRET\"}", HttpStatusCode.ServiceUnavailable, "journal"),
        ("{\"ready\":true,\"journalHealthy\":true,\"routersReady\":false}", HttpStatusCode.OK, "routers"),
        ("{\"ready\":false,\"journalHealthy\":true,\"routersReady\":true}", HttpStatusCode.ServiceUnavailable, "no more specific"),
        ("PRIVATE SECRET", HttpStatusCode.Unauthorized, "authentication"),
        (new string('s', 8192), HttpStatusCode.OK, "bounded response limit"),
        ("{\"ready\":\"true\"}", HttpStatusCode.OK, "invalid")
    })
    {
        using var response = new HttpResponseMessage(item.Item2) { Content = new StringContent(item.Item1) };
        var observation = await ProxyHealthObservation.ReadAsync(response, default);
        Check(!observation.Ready && observation.Reason.Contains(item.Item3) && !observation.Reason.Contains("PRIVATE"), "health diagnostics use only bounded documented conditions");
    }

    // Real child processes are this test's apphost, use a private random loopback
    // port and synthetic config, and never access a provider or installed state.
    var apphost = Path.Combine(AppContext.BaseDirectory, "CoreStartup.Tests" + (OperatingSystem.IsWindows() ? ".exe" : ""));
    Check(File.Exists(apphost), "synthetic Core apphost is built alongside the test");
    var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(apphost)));
    foreach (var scenario in new[] { "early-exit", "journal", "ready", "exit-after-ready" })
    {
        Environment.SetEnvironmentVariable("LAIN_REFERENCE_PROXY_PORT", FreePort().ToString());
        var privatePaths = new OwnStatePaths(Path.Combine(root, "process-" + scenario));
        Check(privatePaths.GatewayPort is not (18417 or 18418), "fake Core must never use a product port");
        OwnStatePaths.WritePrivate(privatePaths.ProxyConfig, JsonSerializer.Serialize(new { port = privatePaths.GatewayPort, scenario }));
        var privateIntent = new RuntimeStartIntent(privatePaths);
        await using (var child = new OwnedProxyLifecycle(privatePaths, new SecretStore(privatePaths), new(apphost, hash, true),
            timing: new(TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(250))))
        {
            if (scenario is "early-exit" or "journal") await Reject(async () => { await child.StartAsync(); });
            else await child.StartAsync();
            if (scenario == "early-exit")
                Check(child.Status is { Ready: false, Pid: null } && child.RuntimeLifecycle is { Stage: "early-exit", ExitCode: 7, StartedAt: not null, UpdatedAt: not null }, "early exit captures code and timestamps before cleanup");
            if (scenario == "journal")
                Check(child.RuntimeLifecycle is { Stage: "timeout", ExitCode: null } && child.Status.Error!.Contains("journal"), "timeout retains observed unmet journal condition without cleanup exit code");
            if (scenario == "ready")
            {
                var pid = child.Status.Pid;
                await child.StartAsync();
                Check(child.Status.Ready && child.Status.Pid == pid && child.RuntimeLifecycle.Stage == "ready", "healthy repeated Start retains the owned child");
            }
            if (scenario == "exit-after-ready")
            {
                var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
                while (child.Status.Ready && DateTimeOffset.UtcNow < deadline) await Task.Delay(20);
                Check(child.Status is { Ready: false, Pid: null } && child.RuntimeLifecycle is { Stage: "unexpected-exit", ExitCode: 9 }, "unexpected exit clears readiness and retains its actual exit code");
            }
            Check(!JsonSerializer.Serialize(child.RuntimeLifecycle).Contains("PRIVATE") && !JsonSerializer.Serialize(child.Status).Contains("PRIVATE"), "raw child output and health errors never enter public diagnostics");
            privateIntent.SuppressAutomaticStart();
            await child.StopAfterFailureAsync();
        }
        Check(new RuntimeStartIntent(privatePaths).AutomaticStartSuppressed, "failure cleanup and Host disposal preserve explicit Stop intent");
    }
    Console.WriteLine($"PASS Core startup: {checks} checks; isolated fake Core and synthetic state only.");
    return 0;
}
finally
{
    Environment.SetEnvironmentVariable("LAIN_REFERENCE_QUALIFICATION", previousIsolation);
    Environment.SetEnvironmentVariable("LAIN_REFERENCE_PROXY_PORT", previousPort);
    if (Directory.Exists(root)) Directory.Delete(root, true);
}

static int FreePort()
{
    while (true)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        if (port is not (18417 or 18418)) return port;
    }
}

sealed class FakeStartup : IOwnedProxyStartup
{
    public bool Valid = true;
    public int Starts;
    public int Restarts;
    public TimeSpan Delay;
    public ProxyProcessStatus Status { get; private set; } = new("stopped", false, null, null);
    public RuntimeLifecycleDto RuntimeLifecycle { get; private set; } = new(false, "stopped");
    public void SetLoginOnly() => Status = new("login-only", false, 100, null, true, true);
    public async Task<ProxyProcessStatus> StartAsync(CancellationToken ct = default, Action? validated = null)
    {
        Starts++;
        if (!Valid) { RuntimeLifecycle = new(false, "qualification", "Unqualified synthetic Core."); throw new InvalidOperationException(); }
        validated?.Invoke();
        await Task.Delay(Delay, ct);
        RuntimeLifecycle = new(false, "ready");
        return Status = new("running", true, 100, null);
    }
    public Task<ProxyProcessStatus> StopAsync(CancellationToken ct = default)
    { RuntimeLifecycle = new(false, "stopped"); return Task.FromResult(Status = new("stopped", false, null, null)); }
    public Task<ProxyProcessStatus> RestartAsync(CancellationToken ct = default, Action? validated = null)
    { Restarts++; return StartAsync(ct, validated); }
}

static class FakeCore
{
    public static async Task<int> RunAsync(string[] arguments)
    {
        string Value(string name) => arguments[Array.IndexOf(arguments, name) + 1];
        using var config = JsonDocument.Parse(File.ReadAllText(Value("--config")));
        var port = config.RootElement.GetProperty("port").GetInt32();
        var scenario = config.RootElement.GetProperty("scenario").GetString();
        if (port is 18417 or 18418) return 99;
        Console.Error.Write("PRIVATE-CHILD-SECRET" + new string('s', 100_000));
        if (scenario == "early-exit") return 7;
        var token = File.ReadAllText(Value("--management-token-file"));
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
        while (true)
        {
            var request = await listener.GetContextAsync();
            var valid = request.Request.Headers["Authorization"] == "Bearer " + token && request.Request.Url?.AbsolutePath == "/lain/health";
            var healthy = scenario != "journal";
            request.Response.StatusCode = !valid ? 401 : healthy ? 200 : 503;
            var body = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { ready = healthy, journalHealthy = healthy, routersReady = true, error = "PRIVATE-HEALTH-SECRET" }));
            request.Response.ContentLength64 = body.Length;
            await request.Response.OutputStream.WriteAsync(body); request.Response.Close();
            if (scenario == "exit-after-ready") { await Task.Delay(150); return 9; }
        }
    }
}
