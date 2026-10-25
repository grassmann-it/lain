using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Accounts.ConsoleConnections.MiMo;
using Lain.Host.Operations;

var temporary = Environment.GetEnvironmentVariable("LAIN_TEST_TMPDIR")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "lain-build-tmp");
var root = Path.Combine(temporary, "lain-mimo-startup-fixtures-" + Guid.NewGuid().ToString("N"));
OwnStatePaths.EnsurePrivateDirectory(root);
var passed = 0;
try
{
    await Case("invalid identifier and pre-cancel allocate nothing", async (paths, runtime) =>
    {
        await using var browser = Browser(paths, runtime);
        try { await browser.OpenAsync("../invalid", default); throw new Exception("identifier accepted"); } catch (ArgumentException) { }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await browser.OpenAsync("fixture", cancelled.Token); throw new Exception("cancel accepted"); } catch (OperationCanceledException) { }
        Check(runtime.Starts == 0 && Empty(paths), "preconditions preserve empty state");
    });
    await Case("browser and display availability have distinct fixed reasons", async (paths, runtime) =>
    {
        await using var browser = Browser(paths, runtime);
        runtime.Executable = null;
        await Failure(() => browser.OpenAsync("fixture", default), MiMoBrowserStartReason.BrowserUnavailable);
        runtime.Executable = "/synthetic/chromium"; runtime.HasDesktop = false;
        await Failure(() => browser.OpenAsync("fixture", default), MiMoBrowserStartReason.DesktopUnavailable);
        Check(runtime.Starts == 0 && Empty(paths), "no profile on unavailable runtime");
    });
    await Case("Process.Start failure erases preallocated profile and redacts native text", async (paths, runtime) =>
    {
        runtime.OnStart = _ => throw new Win32Exception(13, "synthetic-private-cookie-and-path");
        await using var browser = Browser(paths, runtime);
        var error = await Failure(() => browser.OpenAsync("fixture", default), MiMoBrowserStartReason.ProcessLaunchFailed);
        Check(error.NativeErrorCode == 13 && Empty(paths), "native numeric code and profile cleanup");
    });
    foreach (var (marker, reason) in new[] {
        ("Missing X server or $DISPLAY", MiMoBrowserStartReason.DisplayUnavailable),
        ("No usable sandbox!", MiMoBrowserStartReason.SandboxUnavailable),
        ("error while loading shared libraries: private-file", MiMoBrowserStartReason.BrowserLibraryUnavailable),
        ("Failed to create a ProcessSingleton", MiMoBrowserStartReason.OwnedProfileLocked),
        ("synthetic-private-cookie-and-path", MiMoBrowserStartReason.ProcessExited) })
    {
        await Case("early exit: " + reason, async (paths, runtime) =>
        {
            var process = new FakeProcess { Exited = true, ErrorText = marker + " synthetic-private-cookie-and-path" };
            runtime.OnStart = _ => process;
            await using var browser = Browser(paths, runtime);
            var error = await Failure(() => browser.OpenAsync("fixture", default), reason);
            Check(error.ExitCode == 42 && process.Disposed && Empty(paths), "exit status and cleanup");
        });
    }
    await Case("stderr capture is bounded and unknown remains unknown", async (paths, runtime) =>
    {
        runtime.OnStart = _ => new FakeProcess { Exited = true, ErrorText = new string('x', 16 * 1024) + "Missing X server or $DISPLAY" };
        await using var browser = Browser(paths, runtime);
        await Failure(() => browser.OpenAsync("fixture", default), MiMoBrowserStartReason.ProcessExited);
        Check(Empty(paths), "large output leaves no profile");
    });
    await Case("receipt failure does not invent a browser exit", async (paths, runtime) =>
    {
        var process = new FakeProcess { StartTimeUnavailable = true }; runtime.OnStart = _ => process;
        await using var browser = Browser(paths, runtime);
        var error = await Failure(() => browser.OpenAsync("fixture", default), MiMoBrowserStartReason.ProcessReceiptUnavailable);
        Check(error.ExitCode is null && process.Killed && process.Disposed && Empty(paths), "receipt failure closes exact owned process");
    });
    await Case("endpoint timeout kills owned process and erases profile", async (paths, runtime) =>
    {
        var process = new FakeProcess(); runtime.OnStart = _ => process;
        await using var browser = Browser(paths, runtime, TimeSpan.FromMilliseconds(20));
        await Failure(() => browser.OpenAsync("fixture", default), MiMoBrowserStartReason.EndpointTimeout);
        Check(process.Killed && process.Disposed && Empty(paths), "timeout cleanup");
    });
    await Case("cleanup failure retains owned bookkeeping for a retry", async (paths, runtime) =>
    {
        var process = new FakeProcess { FailKill = true }; runtime.OnStart = _ => process;
        var browser = Browser(paths, runtime, TimeSpan.FromMilliseconds(20));
        await Failure(() => browser.OpenAsync("fixture", default), MiMoBrowserStartReason.CleanupIncomplete);
        Check(!process.Disposed && !Empty(paths), "failed cleanup is not falsely reported as complete");
        process.FailKill = false; await browser.DisposeAsync();
        Check(process.Killed && process.Disposed && Empty(paths), "owner can retry cleanup after a temporary stop failure");
    });
    await Case("caller cancellation remains cancellation and cleans up", async (paths, runtime) =>
    {
        using var cancellation = new CancellationTokenSource(); var process = new FakeProcess();
        runtime.OnStart = _ => { cancellation.Cancel(); return process; };
        await using var browser = Browser(paths, runtime);
        try { await browser.OpenAsync("fixture", cancellation.Token); throw new Exception("cancel became success"); }
        catch (OperationCanceledException) { }
        Check(process.Killed && process.Disposed && Empty(paths), "cancel cleanup");
    });
    await Case("invalid endpoint is rejected without keeping profile", async (paths, runtime) =>
    {
        var process = new FakeProcess();
        runtime.OnStart = start => { File.WriteAllText(Path.Combine(Profile(start), "DevToolsActivePort"), "1023\n/devtools/browser/fixture"); return process; };
        await using var browser = Browser(paths, runtime);
        await Failure(() => browser.OpenAsync("fixture", default), MiMoBrowserStartReason.EndpointInvalid);
        Check(process.Killed && Empty(paths), "invalid endpoint cleanup");
    });
    if (!OperatingSystem.IsWindows()) await Case("endpoint link cannot read or erase external state", async (paths, runtime) =>
    {
        var external = Path.Combine(paths.Root, "external-synthetic-state"); File.WriteAllText(external, "synthetic-sentinel");
        runtime.OnStart = start => { File.CreateSymbolicLink(Path.Combine(Profile(start), "DevToolsActivePort"), external); return new FakeProcess(); };
        await using var browser = Browser(paths, runtime);
        await Failure(() => browser.OpenAsync("fixture", default), MiMoBrowserStartReason.EndpointInvalid);
        Check(File.ReadAllText(external) == "synthetic-sentinel" && Empty(paths), "external endpoint state untouched");
    });
    await Case("partial endpoint publication is retried", async (paths, runtime) =>
    {
        Task? publish = null;
        runtime.OnStart = start =>
        {
            var port = Path.Combine(Profile(start), "DevToolsActivePort"); File.WriteAllText(port, "19001\n");
            publish = Task.Run(async () => { await Task.Delay(20); File.WriteAllText(port, "19001\n/devtools/browser/fixture"); });
            return new FakeProcess();
        };
        await using var browser = Browser(paths, runtime);
        var profile = await browser.OpenAsync("fixture", default); await publish!;
        await browser.CloseAsync(profile, true, default); Check(Empty(paths), "partial endpoint cleanup");
    });
    await Case("reconnect owns distinct profiles and disposal closes only owned sessions", async (paths, runtime) =>
    {
        var processes = new List<FakeProcess>();
        runtime.OnStart = start => { Ready(start); var process = new FakeProcess(); processes.Add(process); return process; };
        var browser = Browser(paths, runtime);
        var first = await browser.OpenAsync("fixture", default); var second = await browser.OpenAsync("fixture", default);
        Check(first != second, "fresh profile per attempt");
        await browser.CloseAsync(first, true, default);
        Check(processes[0].Killed && !processes[1].Killed, "one close does not close reconnect");
        await browser.DisposeAsync(); Check(processes.All(p => p.Killed && p.Disposed) && Empty(paths), "dispose owns both profiles");
        var starts = runtime.Starts; try { await browser.OpenAsync("fixture", default); throw new Exception("disposed open accepted"); } catch (ObjectDisposedException) { }
        Check(runtime.Starts == starts, "disposed owner never starts another browser");
    });
    await Case("dispose racing with launch cannot orphan a process", async (paths, runtime) =>
    {
        MiMoConsoleBrowser? browser = null; var process = new FakeProcess();
        runtime.OnStart = _ => { browser!.DisposeAsync().GetAwaiter().GetResult(); return process; };
        browser = Browser(paths, runtime);
        await Failure(() => browser.OpenAsync("fixture", default), MiMoBrowserStartReason.StartupRejected);
        Check(process.Killed && process.Disposed && Empty(paths), "race cleanup");
    });
    await Case("safe startup reason survives the existing login operation service", async (paths, runtime) =>
    {
        runtime.OnStart = _ => throw new Win32Exception(13, "synthetic-private-cookie-and-path");
        await using var browser = Browser(paths, runtime);
        var secrets = new SecretStore(paths); var configuration = new ConfigurationRepository(paths); var accounts = new AccountService(configuration, secrets);
        var account = new AccountDto("fixture", "MiMo", "xiaomi-mimo", true, "api-key", "unverified");
        secrets.WriteAccount(account.Id, "{\"kind\":\"api-key\",\"apiKey\":\"synthetic-inference-preserved\"}");
        configuration.Update(0, s => s with { Accounts = [account] });
        var original = File.ReadAllBytes(paths.Configuration);
        await using var operations = new LoginOperationService(configuration: configuration, accounts: accounts, secrets: secrets, accountAdapters: [new MiMoConsoleLoginAdapter(browser)]);
        var result = await operations.BeginAsync(configuration.Snapshot.Revision, new("xiaomi-mimo", "console-browser", ["quota"], AccountId: account.Id, ExpectedGeneration: 0));
        var json = JsonSerializer.Serialize(result);
        Check(result.State == "failed" && json.Contains("process-launch-failed") && !json.Contains("synthetic-private-cookie"), "fixed reason reaches public result");
        Check(original.SequenceEqual(File.ReadAllBytes(paths.Configuration)) && secrets.ReadAccount(account.Id).Contains("synthetic-inference-preserved"), "failed start preserves account, routes and inference key");
        Check(Empty(paths), "login failure profile cleanup");
    });
    Console.WriteLine($"PASS {passed} MiMo startup/lifecycle fixtures; no browser, provider process, network or real credentials used.");
}
finally { if (Directory.Exists(root)) Directory.Delete(root, true); }

async Task Case(string name, Func<OwnStatePaths, FakeRuntime, Task> run)
{
    var paths = new OwnStatePaths(Path.Combine(root, passed.ToString("D2"))); var runtime = new FakeRuntime();
    await run(paths, runtime); passed++; Console.WriteLine("PASS " + name);
}
static MiMoConsoleBrowser Browser(OwnStatePaths paths, FakeRuntime runtime, TimeSpan? timeout = null) => new(paths, runtime, timeout ?? TimeSpan.FromSeconds(2));
static string Profile(ProcessStartInfo start) => start.ArgumentList.Single(a => a.StartsWith("--user-data-dir=", StringComparison.Ordinal))["--user-data-dir=".Length..];
static void Ready(ProcessStartInfo start) => File.WriteAllText(Path.Combine(Profile(start), "DevToolsActivePort"), "19001\n/devtools/browser/fixture");
static bool Empty(OwnStatePaths paths)
{
    var root = Path.Combine(paths.Credentials, "console", "mimo"); return !Directory.Exists(root) || !Directory.EnumerateDirectories(root).Any();
}
static void Check(bool good, string message) { if (!good) throw new Exception(message); }
static async Task<MiMoConsoleBrowserStartException> Failure(Func<Task<string>> action, MiMoBrowserStartReason expected)
{
    try { await action(); } catch (MiMoConsoleBrowserStartException error)
    {
        Check(error.Reason == expected, "wrong startup reason: " + error.Reason);
        Check(error.InnerException is null && error.Message == error.PublicMessage && !error.PublicMessage.Contains("synthetic-private-cookie-and-path"), "error redaction");
        return error;
    }
    throw new Exception("startup failure became success");
}
internal sealed class FakeRuntime : IMiMoBrowserRuntime
{
    public string? Executable { get; set; } = "/synthetic/chromium";
    public bool HasDesktop { get; set; } = true;
    public int Starts;
    public Func<ProcessStartInfo, IMiMoBrowserProcess>? OnStart;
    public IMiMoBrowserProcess Start(ProcessStartInfo start)
    {
        Starts++;
        if (start.UseShellExecute || !start.RedirectStandardOutput || !start.RedirectStandardError
            || start.ArgumentList.Any(a => a == "--no-sandbox") || !start.ArgumentList.Contains("--remote-debugging-address=127.0.0.1"))
            throw new Exception("browser boundary changed");
        return OnStart?.Invoke(start) ?? new FakeProcess();
    }
}
internal sealed class FakeProcess : IMiMoBrowserProcess
{
    public int Id => int.MaxValue;
    public DateTime StartTime => StartTimeUnavailable ? throw new InvalidOperationException("synthetic-private-cookie-and-path") : DateTime.UtcNow;
    public bool StartTimeUnavailable;
    public bool Exited;
    public bool HasExited => Exited;
    public int ExitCode => 42;
    public string ErrorText = "";
    public TextReader StandardOutput => new StringReader("");
    public TextReader StandardError => new StringReader(ErrorText);
    public bool Killed;
    public bool FailKill;
    public bool Disposed;
    public void Kill(bool entireProcessTree) { if (FailKill) throw new Win32Exception(13, "synthetic-private-cookie-and-path"); if (!entireProcessTree) throw new Exception("process tree not closed"); Killed = true; Exited = true; }
    public Task WaitForExitAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    public void Dispose() => Disposed = true;
}
