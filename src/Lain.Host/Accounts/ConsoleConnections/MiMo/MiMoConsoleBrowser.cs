using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Lain.Host.Operations;

namespace Lain.Host.Accounts.ConsoleConnections.MiMo;

public sealed class MiMoConsoleSessionUnavailableException() : InvalidOperationException("MiMo console session closed; reconnect monitoring.") { }

/// <summary>Only three named GETs in an explicitly opened, lain-owned browser. No cookie/JS/URL export API.</summary>
public interface IMiMoConsoleBrowser
{
    bool IsAvailable { get; }
    Task<string> OpenAsync(string accountId, CancellationToken ct);
    Task<JsonDocument> ReadAsync(string profile, ConsoleReadOperation operation, CancellationToken ct);
    Task CloseAsync(string profile, bool erase, CancellationToken ct);
}

public sealed record MiMoConsoleReconciliation(int Examined, int Removed, int Retained, int Failed, bool Truncated);

public sealed class MiMoConsoleBrowser : IMiMoConsoleBrowser, IAsyncDisposable
{
    public const int MaximumReconciliationProfiles = 512;
    private sealed record ProcessReceipt(int Pid, long StartedAt, int? HostPid, long? HostStartedAt);
    private sealed class Session(IMiMoBrowserProcess process, string directory)
    {
        public IMiMoBrowserProcess Process = process;
        public string Directory = directory;
        public int Port;
        public volatile bool Released;
        public SemaphoreSlim Gate = new(1);
        public CancellationTokenSource Lifetime = new();
    }
    private readonly ConcurrentDictionary<string, Session> sessions = new();
    private readonly OwnStatePaths paths;
    private readonly IMiMoBrowserRuntime runtime;
    private readonly TimeSpan startupTimeout;
    private readonly object lifecycle = new();
    private bool disposed;

    public MiMoConsoleBrowser(OwnStatePaths paths) : this(paths, new MiMoBrowserRuntime(), TimeSpan.FromSeconds(15)) { }
    internal MiMoConsoleBrowser(OwnStatePaths paths, IMiMoBrowserRuntime runtime, TimeSpan startupTimeout)
    {
        if (startupTimeout <= TimeSpan.Zero || startupTimeout > TimeSpan.FromSeconds(15)) throw new ArgumentOutOfRangeException(nameof(startupTimeout));
        this.paths = paths; this.runtime = runtime; this.startupTimeout = startupTimeout;
    }
    public bool IsAvailable => runtime.Executable is not null && runtime.HasDesktop;
    public async Task<string> OpenAsync(string accountId, CancellationToken ct)
    {
        if (!MiMoConsoleIdentity.IsIdentifier(accountId)) throw new ArgumentException("Invalid MiMo account identifier.");
        ct.ThrowIfCancellationRequested();
        lock (lifecycle) ObjectDisposedException.ThrowIf(disposed, this);
        var executable = runtime.Executable ?? throw new MiMoConsoleBrowserStartException(MiMoBrowserStartReason.BrowserUnavailable);
        if (!runtime.HasDesktop) throw new MiMoConsoleBrowserStartException(MiMoBrowserStartReason.DesktopUnavailable);
        var profile = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(paths.Credentials, "console", "mimo", profile);
        IMiMoBrowserProcess? process = null;
        Session? session = null;
        Task[] drains = [];
        var diagnostic = new MiMoBrowserStartupDiagnostic();
        var stage = "owned-state";
        try
        {
            // Allocation, Process.Start and receipt publication belong to the same cleanup scope.
            OwnStatePaths.EnsurePrivateDirectory(directory);
            OwnStatePaths.WritePrivate(Path.Combine(directory, "Default", "Preferences"), "{\"credentials_enable_service\":false,\"profile\":{\"password_manager_enabled\":false}}");
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "--user-data-dir=" + directory, "--remote-debugging-address=127.0.0.1", "--remote-debugging-port=0",
                "--no-first-run", "--no-default-browser-check", "--disable-sync", "--disable-extensions", "--new-window", "https://platform.xiaomimimo.com/" }) start.ArgumentList.Add(argument);
            stage = "process-launch";
            ct.ThrowIfCancellationRequested();
            process = runtime.Start(start);
            drains = [DrainAsync(process.StandardOutput), DrainAsync(process.StandardError, diagnostic)];
            session = new Session(process, directory);
            lock (lifecycle)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                sessions[profile] = session;
            }
            stage = "process-receipt";
            if (process.HasExited) throw new MiMoConsoleBrowserStartException(MiMoBrowserStartReason.ProcessExited);
            using var host = Process.GetCurrentProcess();
            OwnStatePaths.WritePrivate(Path.Combine(directory, "lain-process.json"), JsonSerializer.Serialize(new {
                kind = "lain-mimo-console-profile", pid = process.Id, startedAt = process.StartTime.ToUniversalTime().Ticks,
                hostPid = host.Id, hostStartedAt = host.StartTime.ToUniversalTime().Ticks }));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, session.Lifetime.Token);
            timeout.CancelAfter(startupTimeout);
            stage = "endpoint";
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (process.HasExited) throw new MiMoConsoleBrowserStartException(MiMoBrowserStartReason.ProcessExited);
                var portPath = Path.Combine(directory, "DevToolsActivePort");
                if (File.Exists(portPath))
                {
                    if (new FileInfo(portPath).LinkTarget is not null || new FileInfo(portPath).Length > 4096)
                        throw new MiMoConsoleBrowserStartException(MiMoBrowserStartReason.EndpointInvalid);
                    var lines = await File.ReadAllLinesAsync(portPath, timeout.Token);
                    if (lines.Length < 2 || lines[1].Length == 0) { await Task.Delay(100, timeout.Token); continue; }
                    if (lines.Length != 2 || !int.TryParse(lines[0], out var port) || port is <= 1024 or > 65535
                        || !lines[1].StartsWith("/devtools/browser/", StringComparison.Ordinal)
                        || lines[1].Length > 128 || lines[1].Any(char.IsControl))
                        throw new MiMoConsoleBrowserStartException(MiMoBrowserStartReason.EndpointInvalid);
                    ct.ThrowIfCancellationRequested();
                    lock (lifecycle)
                    {
                        ObjectDisposedException.ThrowIf(disposed, this);
                        if (session.Lifetime.IsCancellationRequested) throw new MiMoConsoleBrowserStartException(MiMoBrowserStartReason.StartupInterrupted);
                        if (process.HasExited) throw new MiMoConsoleBrowserStartException(MiMoBrowserStartReason.ProcessExited);
                        session.Port = port;
                        return profile;
                    }
                }
                await Task.Delay(100, timeout.Token);
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            var cancelled = error is OperationCanceledException && ct.IsCancellationRequested;
            var reason = error switch
            {
                MiMoConsoleBrowserStartException known => known.Reason,
                OperationCanceledException when session?.Lifetime.IsCancellationRequested == true => MiMoBrowserStartReason.StartupInterrupted,
                OperationCanceledException => MiMoBrowserStartReason.EndpointTimeout,
                System.ComponentModel.Win32Exception when stage == "process-launch" => MiMoBrowserStartReason.ProcessLaunchFailed,
                IOException or UnauthorizedAccessException => MiMoBrowserStartReason.OwnedStateUnavailable,
                InvalidOperationException when stage == "process-receipt" => MiMoBrowserStartReason.ProcessReceiptUnavailable,
                _ => MiMoBrowserStartReason.StartupRejected
            };
            int? exitCode = null;
            try { if (process?.HasExited == true) exitCode = process.ExitCode; } catch (InvalidOperationException) { }
            var native = error is System.ComponentModel.Win32Exception win32 ? win32.NativeErrorCode : (int?)null;
            try
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                if (sessions.ContainsKey(profile)) await CloseAsync(profile, true, cleanup.Token);
                else if (session?.Released != true)
                {
                    if (process is not null) { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(cleanup.Token); } process.Dispose(); }
                    if (Directory.Exists(directory)) { OwnStatePaths.EnsurePrivateDirectory(directory); Directory.Delete(directory, recursive: true); }
                }
                try { await Task.WhenAll(drains).WaitAsync(TimeSpan.FromMilliseconds(250)); }
                catch (TimeoutException) { } // Draining remains bounded; raw text is never exposed.
            }
            catch (Exception cleanupError) when (cleanupError is not OutOfMemoryException and not StackOverflowException)
            { throw new MiMoConsoleBrowserStartException(MiMoBrowserStartReason.CleanupIncomplete, exitCode, native); }
            if (cancelled) throw;
            if (reason == MiMoBrowserStartReason.ProcessReceiptUnavailable && exitCode is not null) reason = MiMoBrowserStartReason.ProcessExited;
            if (reason == MiMoBrowserStartReason.ProcessExited) reason = diagnostic.ExitReason();
            throw new MiMoConsoleBrowserStartException(reason, exitCode, native);
        }
    }
    public async Task<JsonDocument> ReadAsync(string profile, ConsoleReadOperation operation, CancellationToken ct)
    {
        var route = ConsoleReadRoutes.Resolve(operation);
        if (!sessions.TryGetValue(profile, out var session) || session.Process.HasExited)
            throw new MiMoConsoleSessionUnavailableException();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, session.Lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        await session.Gate.WaitAsync(timeout.Token);
        try
        {
            using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false });
            using var response = await http.GetAsync($"http://127.0.0.1:{session.Port}/json/list", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            using var targetStream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var targets = JsonDocument.Parse(await ReadBoundedAsync(targetStream, 1024 * 1024, timeout.Token));
            if (targets.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("Invalid browser target response.");
            var pages = targets.RootElement.EnumerateArray().Where(p => p.TryGetProperty("type", out var type) && type.GetString() == "page"
                && p.TryGetProperty("url", out var url) && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri) && uri.GetLeftPart(UriPartial.Authority) == route.Origin).ToArray();
            if (pages.Length != 1) throw new InvalidOperationException("Complete sign-in in the single MiMo console window.");
            var address = pages[0].GetProperty("webSocketDebuggerUrl").GetString();
            if (!Uri.TryCreate(address, UriKind.Absolute, out var endpoint) || endpoint.Scheme != "ws" || endpoint.Host != "127.0.0.1" || endpoint.Port != session.Port || endpoint.UserInfo.Length != 0)
                throw new InvalidOperationException("Invalid owned browser endpoint.");
            using var socket = new ClientWebSocket(); socket.Options.Proxy = null;
            await socket.ConnectAsync(endpoint, timeout.Token);
            // Fixed source and route. All provider cookies, Origin and CSRF policy stay in the browser.
            var script = "(async()=>{if(location.origin!==" + JsonSerializer.Serialize(route.Origin) + ")throw Error('origin');const r=await fetch(" + JsonSerializer.Serialize(route.Path)
                + ",{method:'GET',credentials:'same-origin',redirect:'error',cache:'no-store'});if(r.status!==200)return {status:r.status};const rd=r.body.getReader();const chunks=[];let size=0;try{for(;;){const x=await rd.read();if(x.done)break;size+=x.value.length;if(size>262144)throw Error('size');chunks.push(x.value)}}finally{await rd.cancel()}const b=new Uint8Array(size);let o=0;for(const c of chunks){b.set(c,o);o+=c.length}return {status:200,body:new TextDecoder('utf-8',{fatal:true}).decode(b)}})()";
            var request = JsonSerializer.SerializeToUtf8Bytes(new { id = 1, method = "Runtime.evaluate", @params = new { expression = script, awaitPromise = true, returnByValue = true } });
            await socket.SendAsync(request.AsMemory(), WebSocketMessageType.Text, true, timeout.Token);
            for (var count = 0; count < 64; count++)
            {
                using var answer = JsonDocument.Parse(await ReceiveAsync(socket, timeout.Token));
                if (!answer.RootElement.TryGetProperty("id", out var id) || id.GetInt32() != 1) continue;
                if (!answer.RootElement.TryGetProperty("result", out var result) || result.TryGetProperty("exceptionDetails", out _)
                    || !result.TryGetProperty("result", out var remote) || !remote.TryGetProperty("value", out var value)
                    || !value.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.Number || !status.TryGetInt32(out var httpStatus))
                    throw new InvalidOperationException("MiMo console read denied or unavailable; reconnect monitoring if the session expired.");
                if (httpStatus is 401 or 403) throw new MiMoConsoleSessionUnavailableException();
                if (httpStatus != 200 || !value.TryGetProperty("body", out var body) || body.ValueKind != JsonValueKind.String)
                    throw new InvalidOperationException("MiMo console read denied or unavailable; reconnect monitoring if the session expired.");
                var bytes = Encoding.UTF8.GetBytes(body.GetString()!);
                if (bytes.Length > 256 * 1024) throw new InvalidOperationException("MiMo console response too large.");
                return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 24 });
            }
            throw new InvalidOperationException("MiMo console response unavailable.");
        }
        finally { session.Gate.Release(); }
    }
    public async Task CloseAsync(string profile, bool erase, CancellationToken ct)
    {
        if (profile.Length != 32 || profile.Any(c => !char.IsAsciiHexDigit(c))) throw new ArgumentException("Invalid browser profile reference.");
        if (!sessions.TryGetValue(profile, out var session))
        {
            if (!erase) return;
            var directory = Path.Combine(paths.Credentials, "console", "mimo", profile);
            if (!Directory.Exists(directory)) return;
            OwnStatePaths.EnsurePrivateDirectory(directory);
            var saved = await ReadReceiptAsync(directory, ct);
            if (saved is not null)
            {
                try
                {
                    using var process = Process.GetProcessById(saved.Pid);
                    if (!process.HasExited && process.StartTime.ToUniversalTime().Ticks == saved.StartedAt)
                    { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(ct); }
                }
                catch (ArgumentException) { } // Original owned process already exited.
            }
            Directory.Delete(directory, recursive: true);
            return;
        }
        session.Lifetime.Cancel();
        await session.Gate.WaitAsync(ct);
        try
        {
            if (!sessions.TryGetValue(profile, out var current) || !ReferenceEquals(current, session)) return;
            try { if (!session.Process.HasExited) { session.Process.Kill(entireProcessTree: true); await session.Process.WaitForExitAsync(ct); } }
            catch (InvalidOperationException) { }
            session.Process.Dispose();
            // Only our randomly allocated directory; never a user's browser profile.
            if (erase && Directory.Exists(session.Directory)) { OwnStatePaths.EnsurePrivateDirectory(session.Directory); Directory.Delete(session.Directory, recursive: true); }
            session.Released = true;
            sessions.TryRemove(profile, out _);
        }
        finally { session.Gate.Release(); }
    }
    /// <summary>Call once at host startup before accepting connects. Never resumes a persisted session.</summary>
    public async Task<MiMoConsoleReconciliation> ReconcileAsync(CancellationToken ct = default)
    {
        var root = Path.Combine(paths.Credentials, "console", "mimo");
        if (!Directory.Exists(root)) return new(0, 0, 0, 0, false);
        OwnStatePaths.EnsurePrivateDirectory(root);
        // Bound enumeration as well as cleanup work; report any unvisited remainder.
        var directories = Directory.EnumerateDirectories(root).Take(MaximumReconciliationProfiles + 1).ToArray();
        int examined = 0, removed = 0, retained = 0, failed = 0;
        foreach (var directory in directories.Take(MaximumReconciliationProfiles))
        {
            ct.ThrowIfCancellationRequested(); examined++;
            var profile = Path.GetFileName(directory);
            if (profile.Length != 32 || profile.Any(c => !char.IsAsciiHexDigit(c)) || sessions.ContainsKey(profile))
            { retained++; continue; }
            try
            {
                OwnStatePaths.EnsurePrivateDirectory(directory);
                var receipt = await ReadReceiptAsync(directory, ct);
                // An arbitrary hex directory without our validated receipt is not owned.
                // A live owner can still adopt a prepared result; never sweep its profile.
                if (receipt is null || receipt.HostPid is { } hostPid && receipt.HostStartedAt is { } hostStartedAt && IsRunning(hostPid, hostStartedAt))
                { retained++; continue; }
                if (sessions.ContainsKey(profile)) { retained++; continue; }
                await CloseAsync(profile, true, ct); removed++;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            { failed++; }
        }
        return new(examined, removed, retained, failed, directories.Length > MaximumReconciliationProfiles);
    }
    private static async Task<ProcessReceipt?> ReadReceiptAsync(string directory, CancellationToken ct)
    {
        var path = Path.Combine(directory, "lain-process.json");
        if (new FileInfo(path).LinkTarget is not null) throw new IOException("Invalid browser process receipt.");
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 4096) throw new IOException("Invalid browser process receipt.");
        using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(path, ct), new JsonDocumentOptions { MaxDepth = 4 });
        var root = saved.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("pid", out var pid) || !pid.TryGetInt32(out var processId) || processId <= 0
            || !root.TryGetProperty("startedAt", out var startedAt) || !startedAt.TryGetInt64(out var start) || start <= 0 || start > DateTime.MaxValue.Ticks)
            throw new InvalidDataException("Invalid browser process receipt.");
        // The original implementation wrote only pid/start time. Accept that exact
        // legacy receipt within our guarded random profile directory for cleanup.
        if (!root.TryGetProperty("kind", out var kind))
        {
            if (root.EnumerateObject().Count() != 2) throw new InvalidDataException("Invalid browser process receipt.");
            return new(processId, start, null, null);
        }
        if (kind.ValueKind != JsonValueKind.String || kind.GetString() != "lain-mimo-console-profile"
            || !root.TryGetProperty("hostPid", out var hostPid) || !hostPid.TryGetInt32(out var owner) || owner <= 0
            || !root.TryGetProperty("hostStartedAt", out var hostStartedAt) || !hostStartedAt.TryGetInt64(out var ownerStart) || ownerStart <= 0 || ownerStart > DateTime.MaxValue.Ticks)
            throw new InvalidDataException("Invalid browser process receipt.");
        return new(processId, start, owner, ownerStart);
    }
    private static bool IsRunning(int pid, long startedAt)
    {
        try { using var process = Process.GetProcessById(pid); return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == startedAt; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
    private static async Task DrainAsync(TextReader reader, MiMoBrowserStartupDiagnostic? diagnostic = null)
    {
        try { var buffer = new char[4096]; int count; while ((count = await reader.ReadAsync(buffer)) > 0) diagnostic?.Append(buffer.AsSpan(0, count)); }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }
    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int cap, CancellationToken ct)
    {
        using var output = new MemoryStream(); var buffer = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(buffer, ct)) > 0) { if (output.Length + count > cap) throw new InvalidOperationException("Browser response too large."); output.Write(buffer, 0, count); }
        return output.ToArray();
    }
    private static async Task<byte[]> ReceiveAsync(ClientWebSocket socket, CancellationToken ct)
    {
        using var output = new MemoryStream(); var buffer = new byte[8192];
        while (true) { var result = await socket.ReceiveAsync(buffer.AsMemory(), ct); if (result.MessageType != WebSocketMessageType.Text || output.Length + result.Count > 2 * 1024 * 1024) throw new InvalidOperationException("Invalid browser response."); output.Write(buffer, 0, result.Count); if (result.EndOfMessage) return output.ToArray(); }
    }
    public async ValueTask DisposeAsync()
    {
        string[] profiles;
        lock (lifecycle) { disposed = true; profiles = sessions.Keys.ToArray(); }
        foreach (var profile in profiles) await CloseAsync(profile, true, CancellationToken.None);
    }
}
