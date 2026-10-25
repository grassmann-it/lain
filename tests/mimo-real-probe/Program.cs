using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Lain.Host.Accounts.ConsoleConnections;
using Lain.Host.Accounts.ConsoleConnections.MiMo;
using Lain.Host.Operations;

// This opt-in live probe calls production browser methods. It never starts the Host.
if (args.Length != 2 || args[0] != "--real-mimo-authgate"
    || Environment.GetEnvironmentVariable("LAIN_MIMO_PROBE_SLOT") != "1")
    throw new InvalidOperationException("Use the explicit real-probe private runner in the assigned serial slot.");
var output = Path.GetFullPath(args[1]);
var runtime = Environment.GetEnvironmentVariable("LAIN_MIMO_PROBE_RUNTIME") ?? "";
var state = Environment.GetEnvironmentVariable("LAIN_HOME");
var pidText = Environment.GetEnvironmentVariable("LAIN_TEST_XVFB_PID");
if (!runtime.StartsWith("/tmp/lain-mimo-real-probe-", StringComparison.Ordinal)
    || state != Path.Combine(runtime, "lain") || output == runtime || output.StartsWith(runtime + "/", StringComparison.Ordinal)
    || Environment.GetEnvironmentVariable("DISPLAY") != Environment.GetEnvironmentVariable("LAIN_TEST_DISPLAY")
    || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
    || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
    || !int.TryParse(pidText, out var xvfbPid) || !File.Exists($"/proc/{xvfbPid}/cmdline")
    || Path.GetFileName(File.ReadAllText($"/proc/{xvfbPid}/cmdline").Split('\0')[0]) != "Xvfb"
    || !(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") ?? "").Contains(runtime + "/", StringComparison.Ordinal)
    || Directory.EnumerateFileSystemEntries(state).Any())
    throw new InvalidOperationException("Fresh private state, Xvfb and D-Bus are required.");
var hostAssembly = typeof(MiMoConsoleBrowser).Assembly;
var hostSha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(hostAssembly.Location))).ToLowerInvariant();
if (!string.Equals(hostSha, Environment.GetEnvironmentVariable("LAIN_MIMO_PROBE_HOST_SHA256"), StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Pin the reviewed production Host assembly SHA-256 before running.");
Directory.CreateDirectory(output);
var reads = new List<object>();
var opened = false;
var erased = false;
var stop = "not-started";
var started = DateTimeOffset.UtcNow;
var paths = new OwnStatePaths(state);
await using var browser = new MiMoConsoleBrowser(paths);
string? profile = null;
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
try
{
    if (!browser.IsAvailable) stop = "supported-browser-or-private-display-unavailable";
    else
    {
        profile = await browser.OpenAsync("mimo-real-probe", deadline.Token);
        opened = true;
        // Only allow the public landing page to settle. No sign-in interaction occurs.
        await Task.Delay(TimeSpan.FromSeconds(10), deadline.Token);
        await File.WriteAllTextAsync(Path.Combine(output, "capture-ready"), "Public console only; no sign-in performed.\n", deadline.Token);
        foreach (var operation in new[] { ConsoleReadOperation.MiMoIdentity, ConsoleReadOperation.MiMoIndividualUsage })
        {
            var route = ConsoleReadRoutes.Resolve(operation);
            try
            {
                using var reply = await browser.ReadAsync(profile, operation, deadline.Token);
                var successEnvelope = MiMoConsoleIdentity.TrySuccessData(reply.RootElement, out _);
                var identity = operation == ConsoleReadOperation.MiMoIdentity ? MiMoConsoleIdentity.Discover(reply.RootElement) : null;
                // Never persist the body, UID, cookie, response fields or provider error text.
                reads.Add(new { operation = operation.ToString(), route.Method, route.Origin, route.Path,
                    outcome = "json-received", successEnvelope, identityVerified = identity is not null,
                    region = identity?.Region, usageEntitlementVerified = false });
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                reads.Add(new { operation = operation.ToString(), route.Method, route.Origin, route.Path,
                    outcome = Category(error), identityVerified = false, usageEntitlementVerified = false });
            }
        }
        stop = "stopped-before-sign-in-no-authenticated-monitor-published";
        // The runner records the actual private X11 screen before profile cleanup.
        for (var attempt = 0; attempt < 50 && !File.Exists(Path.Combine(output, "capture-complete")); attempt++)
            await Task.Delay(100, deadline.Token);
    }
}
catch (Exception error) when (error is not OutOfMemoryException) { stop = Category(error); }
finally
{
    if (profile is not null) await browser.CloseAsync(profile, true, CancellationToken.None);
    erased = !Directory.Exists(Path.Combine(paths.Credentials, "console", "mimo"))
        || !Directory.EnumerateDirectories(Path.Combine(paths.Credentials, "console", "mimo")).Any();
    var evidence = new {
        startedAt = started, finishedAt = DateTimeOffset.UtcNow, stop, productionOpenSucceeded = opened,
        ownedProfileErased = erased, signInPerformed = false, accountPublished = false,
        hostAssembly = hostAssembly.Location, hostSha256 = hostSha,
        hostInformationalVersion = hostAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        harnessSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Assembly.GetExecutingAssembly().Location))).ToLowerInvariant(),
        reads, boundary = "Real public website/browser attempt. No sign-in, MFA, personal cookies, raw body/UID export, Host startup, account mutation or inference. Network outcome is observed, never synthesized. A session-unavailable exception alone does not prove an HTTP status or visible auth gate."
    };
    await File.WriteAllTextAsync(Path.Combine(output, "evidence.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
}
Console.WriteLine($"MiMo real probe: {stop}; browser opened={opened}; fixed reads attempted={reads.Count}; profile erased={erased}.");
return opened && erased && reads.Count == 2 ? 0 : 2;

static string Category(Exception error) => error switch
{
    MiMoConsoleSessionUnavailableException => "authorization-or-session-unavailable",
    OperationCanceledException => "bounded-deadline-or-cancellation",
    HttpRequestException => "owned-browser-endpoint-or-network-unavailable",
    System.Net.WebSockets.WebSocketException => "owned-browser-websocket-unavailable",
    JsonException => "response-is-not-supported-json",
    System.ComponentModel.Win32Exception => "browser-process-start-unavailable",
    InvalidOperationException when error.Message == "Complete sign-in in the single MiMo console window." => "single-console-origin-page-unavailable-sign-in-may-be-required",
    InvalidOperationException => "production-browser-read-or-start-rejected",
    IOException => "owned-state-or-browser-io-error",
    _ => "unexpected-probe-failure"
};
