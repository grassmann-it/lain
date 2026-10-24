using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Lain.Host.Operations;

public sealed record ProxyLaunchOptions(string BinaryPath, string Sha256, bool Qualified = false);
public sealed record ProxyProcessStatus(string State, bool Ready, int? Pid, string? Error);

/// <summary>Owns exactly the child started here; it never discovers or signals other proxies.</summary>
public sealed class OwnedProxyLifecycle(OwnStatePaths paths, SecretStore secrets, ProxyLaunchOptions options,
    Func<ProxyLaunchOptions>? optionsFactory = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private Process? process;
    private bool ready;
    private string? startupError;
    private bool admissionRecoveryRequired;
    public ProxyProcessStatus Status => process is { HasExited: false }
        ? new(admissionRecoveryRequired ? "drain-recovery-required" : ready ? "running" : "starting", ready, process.Id, startupError)
        : new("stopped", false, null, startupError);

    // Startup metadata checks happen before StartAsync; expose a bounded reason
    // without forwarding exception messages, paths or Core output.
    public void ReportStartupValidationFailure() => startupError ??= "Saved proxy runtime metadata is inconsistent. Review the applied profile.";

    public async Task<ProxyProcessStatus> StartAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        var stage = "qualification";
        try
        {
            if (process is { HasExited: false }) return Status;
            startupError = null;
            admissionRecoveryRequired = false;
            var launchOptions = optionsFactory?.Invoke() ?? options;
            if (!launchOptions.Qualified) throw new InvalidOperationException("Proxy backend qualification gate has not passed.");
            stage = "configuration";
            if (!File.Exists(paths.ProxyConfig)) throw new FileNotFoundException("Own proxy configuration is missing.");
            stage = "integrity";
            using (var binary = File.OpenRead(launchOptions.BinaryPath))
            {
                var actual = Convert.ToHexString(SHA256.HashData(binary));
                if (!actual.Equals(launchOptions.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Proxy binary checksum mismatch.");
            }
            stage = "port";
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, OwnStatePaths.ProxyPort);
                try { listener.Start(); } finally { listener.Stop(); }
            }
            catch (SocketException) { throw new InvalidOperationException("Own proxy port 18417 is occupied; the existing process is left untouched."); }
            stage = "launch";
            var start = new ProcessStartInfo(Path.GetFullPath(launchOptions.BinaryPath))
            {
                WorkingDirectory = paths.Proxy, UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true,
                RedirectStandardInput = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("--config"); start.ArgumentList.Add(paths.ProxyConfig);
            start.ArgumentList.Add("--parent-stdin");
            start.ArgumentList.Add("--owned-auth-dir"); start.ArgumentList.Add(paths.OwnedAuth);
            start.ArgumentList.Add("--bindings"); start.ArgumentList.Add(Path.Combine(paths.Proxy, "bindings.json"));
            start.ArgumentList.Add("--journal"); start.ArgumentList.Add(Path.Combine(paths.Proxy, "events.jsonl"));
            start.ArgumentList.Add("--management-token-file"); start.ArgumentList.Add(paths.ManagementToken);
            secrets.GetOrCreateToken(paths.ManagementToken);
            process?.Dispose();
            process = null;
            var child = new Process { StartInfo = start, EnableRaisingEvents = true };
            // Core output may contain credential material. Do not forward or persist it.
            child.OutputDataReceived += (_, _) => { };
            child.ErrorDataReceived += (_, _) => { };
            try
            {
                if (!child.Start()) throw new InvalidOperationException("Own proxy failed to start.");
            }
            catch { child.Dispose(); throw; }
            process = child;
            ready = false;
            try
            {
                process.BeginOutputReadLine(); process.BeginErrorReadLine();
                stage = "readiness";
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                using var http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { BaseAddress = new Uri($"http://127.0.0.1:{OwnStatePaths.ProxyPort}"), Timeout = TimeSpan.FromSeconds(2) };
                http.DefaultRequestHeaders.Authorization = new("Bearer", secrets.GetOrCreateToken(paths.ManagementToken));
                while (!process.HasExited)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    try
                    {
                        using var response = await http.GetAsync("/lain/health", timeout.Token);
                        if (response.IsSuccessStatusCode)
                        {
                            using var health = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
                            if (health.RootElement.TryGetProperty("ready", out var readiness) && readiness.ValueKind == System.Text.Json.JsonValueKind.True)
                            { ready = true; return Status; }
                        }
                    }
                    catch (HttpRequestException) { }
                    catch (System.Text.Json.JsonException) { }
                    catch (TaskCanceledException) when (!timeout.IsCancellationRequested) { }
                    await Task.Delay(100, timeout.Token);
                }
                stage = "early-exit";
                throw new InvalidOperationException("Own proxy exited before becoming ready.");
            }
            catch { await StopOwnedProcessAsync(); throw; }
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            startupError = stage switch
            {
                "qualification" => "Proxy backend qualification has not passed.",
                "configuration" => "Applied proxy configuration is missing or unreadable.",
                "integrity" => "Proxy executable integrity verification failed.",
                "port" => "Local proxy port 18417 is already in use.",
                "launch" => "The proxy process could not be launched.",
                "early-exit" => $"The proxy exited before becoming ready (exit code {process!.ExitCode}).",
                _ => "The proxy did not become ready within the startup deadline."
            };
            throw;
        }
        finally { gate.Release(); }
    }
    public async Task<ProxyProcessStatus> StopAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try { await StopOwnedProcessAsync(); startupError = null; admissionRecoveryRequired = false; return Status; }
        finally { gate.Release(); }
    }
    public async Task<ProxyProcessStatus> RestartAsync(CancellationToken ct = default)
    { await StopAsync(ct); return await StartAsync(ct); }

    /// <summary>Quiesce admission before waiting. Timeout restores the old runtime without killing a stream.</summary>
    public async Task DrainAndStopAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (process is not { HasExited: false }) return;
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
            { BaseAddress = new Uri($"http://127.0.0.1:{OwnStatePaths.ProxyPort}/"), Timeout = TimeSpan.FromSeconds(3) };
            http.DefaultRequestHeaders.Authorization = new("Bearer", secrets.GetOrCreateToken(paths.ManagementToken));
            var admissionMayBeClosed = false;
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(TimeSpan.FromSeconds(20));
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
                admissionMayBeClosed = false;
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
                        throw new Lain.Host.Models.ProfileDrainRecoveryRequiredException();
                    }
                }
            }
        }
        finally { gate.Release(); }
    }
    private async Task StopOwnedProcessAsync()
    {
        ready = false;
        if (process is not { HasExited: false }) return;
        // No PID files, process-name matching, service stop, or port-owner termination.
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
    }
    public async ValueTask DisposeAsync() { await StopAsync(); process?.Dispose(); gate.Dispose(); }
}
