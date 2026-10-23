using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Lain.Host.Operations;

public sealed record ProxyLaunchOptions(string BinaryPath, string Sha256, bool Qualified = false);
public sealed record ProxyProcessStatus(string State, bool Ready, int? Pid, string? Error);

/// <summary>Owns exactly the child started here; it never discovers or signals other proxies.</summary>
public sealed class OwnedProxyLifecycle(OwnStatePaths paths, SecretStore secrets, ProxyLaunchOptions options) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private Process? process;
    private bool ready;
    public ProxyProcessStatus Status => process is { HasExited: false }
        ? new(ready ? "running" : "starting", ready, process.Id, null)
        : new("stopped", false, null, null);

    public async Task<ProxyProcessStatus> StartAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (process is { HasExited: false }) return Status;
            if (!options.Qualified) throw new InvalidOperationException("Proxy backend qualification gate has not passed.");
            if (!File.Exists(paths.ProxyConfig)) throw new FileNotFoundException("Own proxy configuration is missing.");
            using (var binary = File.OpenRead(options.BinaryPath))
            {
                var actual = Convert.ToHexString(SHA256.HashData(binary));
                if (!actual.Equals(options.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Proxy binary checksum mismatch.");
            }
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, OwnStatePaths.ProxyPort);
                try { listener.Start(); } finally { listener.Stop(); }
            }
            catch (SocketException) { throw new InvalidOperationException("Own proxy port 18417 is occupied; the existing process is left untouched."); }
            var start = new ProcessStartInfo(Path.GetFullPath(options.BinaryPath))
            {
                WorkingDirectory = paths.Proxy, UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("--config"); start.ArgumentList.Add(paths.ProxyConfig);
            start.ArgumentList.Add("--owned-auth-dir"); start.ArgumentList.Add(paths.OwnedAuth);
            start.ArgumentList.Add("--bindings"); start.ArgumentList.Add(Path.Combine(paths.Proxy, "bindings.json"));
            start.ArgumentList.Add("--journal"); start.ArgumentList.Add(Path.Combine(paths.Proxy, "events.jsonl"));
            start.ArgumentList.Add("--management-token-file"); start.ArgumentList.Add(paths.ManagementToken);
            secrets.GetOrCreateToken(paths.ManagementToken);
            process?.Dispose();
            process = new Process { StartInfo = start, EnableRaisingEvents = true };
            // Core output may contain credential material. Do not forward or persist it.
            process.OutputDataReceived += (_, _) => { };
            process.ErrorDataReceived += (_, _) => { };
            if (!process.Start()) throw new InvalidOperationException("Own proxy failed to start.");
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            ready = false;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{OwnStatePaths.ProxyPort}"), Timeout = TimeSpan.FromSeconds(2) };
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
                throw new InvalidOperationException("Own proxy exited before becoming ready.");
            }
            catch { await StopOwnedProcessAsync(); throw; }
        }
        finally { gate.Release(); }
    }
    public async Task<ProxyProcessStatus> StopAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try { await StopOwnedProcessAsync(); return Status; }
        finally { gate.Release(); }
    }
    public async Task<ProxyProcessStatus> RestartAsync(CancellationToken ct = default)
    { await StopAsync(ct); return await StartAsync(ct); }
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
