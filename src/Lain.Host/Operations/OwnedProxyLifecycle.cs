using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Lain.Core;

namespace Lain.Host.Operations;

public sealed record ProxyLaunchOptions(string BinaryPath, string Sha256, bool Qualified = false);
public sealed record ProxyProcessStatus(string State, bool Ready, int? Pid, string? Error, bool LoginOnly = false, bool ManagementReady = false);
public sealed record ProxyRuntimeInputs(string Configuration, string Bindings, string Journal);

/// <summary>Owns exactly the child started here; it never discovers or signals other proxies.</summary>
public sealed class OwnedProxyLifecycle(OwnStatePaths paths, SecretStore secrets, ProxyLaunchOptions options,
    Func<ProxyLaunchOptions>? optionsFactory = null, ProxyStartupTiming? timing = null) : IAsyncDisposable, IOwnedProxyStartup
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object stateGate = new();
    private Process? process;
    private volatile bool ready;
    private volatile bool stopping;
    private volatile string? startupError;
    private bool admissionRecoveryRequired;
    private bool loginOnly;
    private RuntimeLifecycleDto diagnostic = new(false, "stopped");
    public RuntimeLifecycleDto RuntimeLifecycle => Volatile.Read(ref diagnostic);
    public ProxyProcessStatus Status
    {
        get
        {
            lock (stateGate)
            {
                if (IsRunning(process)) return new(admissionRecoveryRequired ? "drain-recovery-required" : ready ? loginOnly ? "login-only" : "running" : "starting",
                    ready && !loginOnly, process!.Id, startupError, loginOnly, ready);
                if (ready && process is not null) ObserveUnexpectedExit(process);
                return new("stopped", false, null, startupError);
            }
        }
    }

    // Startup metadata checks precede launch. Keep an already precise failure.
    public void ReportStartupValidationFailure()
    {
        if (startupError is not null) return;
        startupError = "Saved proxy runtime metadata is inconsistent. Review the applied profile.";
        Report("recovery-required", startupError, completed: true);
    }

    public Task<ProxyProcessStatus> StartAsync(CancellationToken ct = default, Action? validated = null) => StartCoreAsync(null, false, validated, ct);

    public Task<ProxyProcessStatus> StartLoginRuntimeAsync(ProxyRuntimeInputs inputs, CancellationToken ct = default)
    {
        foreach (var path in new[] { inputs.Configuration, inputs.Bindings, inputs.Journal })
            if (!Path.GetFullPath(path).StartsWith(paths.Proxy + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new ArgumentException("Login runtime files must remain in the owned proxy directory.");
        return StartCoreAsync(inputs, false, null, ct);
    }

    private async Task<ProxyProcessStatus> StartCoreAsync(ProxyRuntimeInputs? inputs, bool restart, Action? validated, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        var stage = "existing-process";
        var healthReason = "The Core management endpoint is not reachable yet.";
        int? exitCode = null;
        var cleanupNeeded = false;
        var startupTiming = timing ?? ProxyStartupTiming.Default;
        try
        {
            if (IsRunning(process) && !restart)
            {
                if (inputs is null && loginOnly) throw new InvalidOperationException("A login-only runtime is active; explicitly apply a profile before inference.");
                if (!ready) throw new InvalidOperationException("The active owned process requires recovery before another login.");
                validated?.Invoke();
                return Status;
            }
            startupError = null;
            admissionRecoveryRequired = false;
            Volatile.Write(ref diagnostic, new(false, "starting", StartedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow));
            stage = "qualification"; Report(stage);
            var launchOptions = optionsFactory?.Invoke() ?? options;
            if (!launchOptions.Qualified) throw new InvalidOperationException("Proxy backend qualification gate has not passed.");
            stage = "configuration"; Report(stage);
            using (File.OpenRead(inputs?.Configuration ?? paths.ProxyConfig)) { }
            stage = "integrity"; Report(stage);
            using (var binary = File.OpenRead(launchOptions.BinaryPath))
            {
                var actual = Convert.ToHexString(SHA256.HashData(binary));
                if (!actual.Equals(launchOptions.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Proxy binary checksum mismatch.");
            }
            stage = "port"; Report(stage);
            if (!IsRunning(process)) RequireAvailablePort();
            stage = "intent";
            validated?.Invoke();
            if (restart)
            {
                cleanupNeeded = true;
                await StopOwnedProcessAsync();
                stage = "port"; Report(stage); RequireAvailablePort();
            }
            stage = "launch"; Report(stage);
            var start = new ProcessStartInfo(Path.GetFullPath(launchOptions.BinaryPath))
            {
                WorkingDirectory = paths.Proxy, UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true,
                RedirectStandardInput = true, CreateNoWindow = true
            };
            start.Environment["LAIN_RECORDING_CONFIG"] = Path.Combine(paths.Root, "recording", "policy.json");
            start.ArgumentList.Add("--config"); start.ArgumentList.Add(inputs?.Configuration ?? paths.ProxyConfig);
            start.ArgumentList.Add("--parent-stdin");
            start.ArgumentList.Add("--owned-auth-dir"); start.ArgumentList.Add(paths.OwnedAuth);
            start.ArgumentList.Add("--bindings"); start.ArgumentList.Add(inputs?.Bindings ?? Path.Combine(paths.Proxy, "bindings.json"));
            start.ArgumentList.Add("--journal"); start.ArgumentList.Add(inputs?.Journal ?? Path.Combine(paths.Proxy, "events.jsonl"));
            start.ArgumentList.Add("--management-token-file"); start.ArgumentList.Add(paths.ManagementToken);
            var managementToken = secrets.GetOrCreateToken(paths.ManagementToken);
            lock (stateGate) { process?.Dispose(); process = null; }
            var child = new Process { StartInfo = start, EnableRaisingEvents = true };
            // Drain raw bytes with constant memory. Line events can buffer an
            // unbounded line, and Core output may contain credential material.
            child.Exited += (_, _) => ObserveUnexpectedExit(child);
            try { if (!child.Start()) throw new InvalidOperationException("Own proxy failed to start."); }
            catch { child.Dispose(); throw; }
            cleanupNeeded = true;
            lock (stateGate) { process = child; ready = false; loginOnly = inputs is not null; }
            var discardOutput = child.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
            var discardError = child.StandardError.BaseStream.CopyToAsync(Stream.Null);
            _ = ObserveDiscardAsync(discardOutput, discardError);
            stage = "health"; Report(stage, healthReason);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(startupTiming.ReadinessDeadline);
            using var http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
            { BaseAddress = new Uri($"http://127.0.0.1:{paths.GatewayPort}"), Timeout = startupTiming.RequestTimeout };
            http.DefaultRequestHeaders.Authorization = new("Bearer", managementToken);
            while (IsRunning(child))
            {
                if (timeout.IsCancellationRequested) { stage = ct.IsCancellationRequested ? "cancelled" : "timeout"; timeout.Token.ThrowIfCancellationRequested(); }
                try
                {
                    using var response = await http.GetAsync("/lain/health", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                    var health = await ProxyHealthObservation.ReadAsync(response, timeout.Token);
                    healthReason = health.Reason;
                    Report("health", healthReason);
                    if (health.Ready && IsRunning(child))
                    {
                        lock (stateGate)
                        {
                            ready = true;
                            // An exit racing health must never publish a ready status.
                            if (!IsRunning(child)) { ready = false; stage = "early-exit"; break; }
                            Report("ready", health.Reason, completed: true);
                        }
                        return Status;
                    }
                }
                catch (HttpRequestException) { healthReason = "The Core management endpoint is not reachable yet."; Report("health", healthReason); }
                catch (OperationCanceledException) when (!timeout.IsCancellationRequested)
                { healthReason = "A Core management health request exceeded its request deadline."; Report("health", healthReason); }
                try { await Task.Delay(startupTiming.PollInterval, timeout.Token); }
                catch (OperationCanceledException) { stage = ct.IsCancellationRequested ? "cancelled" : "timeout"; throw; }
            }
            stage = "early-exit";
            exitCode = ReadExitCode(child);
            throw new InvalidOperationException("Own proxy exited before becoming ready.");
        }
        catch when (stage != "existing-process")
        {
            if (ct.IsCancellationRequested) stage = "cancelled";
            else if (stage == "health") stage = "timeout";
            if (stage == "early-exit") exitCode ??= ReadExitCode(process);
            startupError = stage switch
            {
                "qualification" => "Proxy backend qualification has not passed.",
                "configuration" => "Applied proxy configuration is missing or unreadable.",
                "integrity" => "Proxy executable integrity verification failed.",
                "port" => $"Local proxy port {paths.GatewayPort} is already in use.",
                "intent" => "Runtime start intent could not be saved.",
                "launch" => "The proxy process could not be launched.",
                "early-exit" => $"The proxy exited before becoming ready (exit code {exitCode?.ToString() ?? "unavailable"}).",
                "cancelled" => "Core startup was cancelled before readiness was confirmed.",
                _ => "The proxy did not become ready within the startup deadline. " + healthReason
            };
            // Capture the unmet health condition and exit code before cleanup kills
            // the child. Cleanup's exit code cannot become startup evidence.
            Report(stage, startupError, exitCode, completed: true);
            if (cleanupNeeded) await StopOwnedProcessAsync();
            throw;
        }
        finally { gate.Release(); }
    }

    public Task<ProxyProcessStatus> StopAsync(CancellationToken ct = default) => StopCoreAsync(false, ct);
    public Task<ProxyProcessStatus> StopAfterFailureAsync(CancellationToken ct = default) => StopCoreAsync(true, ct);
    private async Task<ProxyProcessStatus> StopCoreAsync(bool preserveStartupError, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            await StopOwnedProcessAsync();
            if (!preserveStartupError) { startupError = null; Report("stopped", completed: true); }
            admissionRecoveryRequired = false;
            return Status;
        }
        finally { gate.Release(); }
    }
    public Task<ProxyProcessStatus> RestartAsync(CancellationToken ct = default, Action? validated = null) => StartCoreAsync(null, true, validated, ct);

    private void RequireAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, paths.GatewayPort);
        try { listener.Start(); } finally { listener.Stop(); }
    }
    private void Report(string stage, string? reason = null, int? exitCode = null, bool completed = false) =>
        Volatile.Write(ref diagnostic, new(false, stage, reason, RuntimeLifecycle.StartedAt, DateTimeOffset.UtcNow, exitCode,
            completed ? DateTimeOffset.UtcNow : null));
    private void ObserveUnexpectedExit(Process child)
    {
        lock (stateGate)
        {
            if (!ReferenceEquals(process, child) || stopping || !ready) return;
            ready = false;
            var exitCode = ReadExitCode(child);
            startupError = $"The owned proxy exited unexpectedly (exit code {exitCode?.ToString() ?? "unavailable"}). Use Start after reviewing the runtime.";
            Report("unexpected-exit", startupError, exitCode, completed: true);
        }
    }
    private static bool IsRunning(Process? child)
    {
        try { return child is { HasExited: false }; }
        catch (InvalidOperationException) { return false; }
    }
    private static int? ReadExitCode(Process? child)
    {
        try { return child is { HasExited: true } ? child.ExitCode : null; }
        catch (InvalidOperationException) { return null; }
    }
    private static async Task ObserveDiscardAsync(Task output, Task error)
    {
        try { await Task.WhenAll(output, error); }
        catch (Exception failure) when (failure is IOException or ObjectDisposedException or InvalidOperationException) { }
    }
    /// <summary>Quiesce admission before waiting. Timeout restores the old runtime without killing a stream.</summary>
    public async Task DrainAndStopAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (process is not { HasExited: false }) return;
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
            { BaseAddress = new Uri($"http://127.0.0.1:{paths.GatewayPort}/"), Timeout = TimeSpan.FromSeconds(3) };
            http.DefaultRequestHeaders.Authorization = new("Bearer", secrets.GetOrCreateToken(paths.ManagementToken));
            var admissionMayBeClosed = false;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                admissionMayBeClosed = true; // A lost POST response can still mean Core accepted the drain.
                using var begin = await http.PostAsync("lain/drain", null, deadline.Token);
                begin.EnsureSuccessStatusCode();
                while (true)
                {
                    using var response = await http.GetAsync("lain/activity", deadline.Token);
                    response.EnsureSuccessStatusCode();
                    var body = await response.Content.ReadAsStringAsync(deadline.Token);
                    if (body.Length > 4096) throw new InvalidDataException("Invalid activity response.");
                    using var json = System.Text.Json.JsonDocument.Parse(body);
                    if (!json.RootElement.GetProperty("draining").GetBoolean())
                        throw new InvalidOperationException("Core did not preserve the admission barrier.");
                    var active = json.RootElement.GetProperty("activeRequests").GetInt64();
                    if (active < 0) throw new InvalidDataException("Invalid active request count.");
                    if (active == 0) break;
                    await Task.Delay(100, deadline.Token);
                }
                await StopOwnedProcessAsync();
                Report("stopped", completed: true);
                admissionMayBeClosed = false;
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                // The finally block must confirm reopening; otherwise its recovery error supersedes this refusal.
                throw new Lain.Host.Models.ProfileDrainTimedOutException();
            }
            finally
            {
                if (admissionMayBeClosed && process is { HasExited: false })
                {
                    // Independent of a disconnected caller, reopen the unchanged previous runtime.
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    try
                    {
                        try { using var resume = await http.DeleteAsync("lain/drain", cleanup.Token); }
                        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException) { }
                        // DELETE may have succeeded despite a lost response. Prove admission independently.
                        using var activity = await http.GetAsync("lain/activity", cleanup.Token);
                        activity.EnsureSuccessStatusCode();
                        var body = await activity.Content.ReadAsStringAsync(cleanup.Token);
                        if (body.Length > 4096) throw new InvalidDataException();
                        using var json = System.Text.Json.JsonDocument.Parse(body);
                        if (json.RootElement.GetProperty("draining").GetBoolean()) throw new InvalidDataException();
                        admissionRecoveryRequired = false;
                    }
                    catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException or
                        System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException)
                    {
                        ready = false;
                        admissionRecoveryRequired = true;
                        startupError = "Gateway admission could not be restored. Existing streams were retained; explicitly apply a profile to recover.";
                        Report("recovery-required", startupError, completed: true);
                        throw new Lain.Host.Models.ProfileDrainRecoveryRequiredException();
                    }
                }
            }
        }
        finally { gate.Release(); }
    }
    private async Task StopOwnedProcessAsync()
    {
        Process? child;
        lock (stateGate) { stopping = true; ready = false; child = process; }
        try
        {
            if (!IsRunning(child)) return;
            // No PID files, process-name matching, service stop, or port-owner termination.
            try { child!.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) when (!IsRunning(child)) { }
            await child!.WaitForExitAsync();
        }
        finally { stopping = false; }
    }
    public async ValueTask DisposeAsync()
    {
        await StopAfterFailureAsync();
        lock (stateGate) { process?.Dispose(); process = null; }
        gate.Dispose();
    }
}
