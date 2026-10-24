using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Lain.Host.Operations;

namespace Lain.Host.Accounts.ConsoleConnections.MiMo;

/// <summary>Only three named GETs in an explicitly opened, lain-owned browser. No cookie/JS/URL export API.</summary>
public interface IMiMoConsoleBrowser
{
    bool IsAvailable { get; }
    Task<string> OpenAsync(string accountId, CancellationToken ct);
    Task<JsonDocument> ReadAsync(string profile, ConsoleReadOperation operation, CancellationToken ct);
    Task CloseAsync(string profile, bool erase, CancellationToken ct);
}

public sealed class MiMoConsoleBrowser(OwnStatePaths paths) : IMiMoConsoleBrowser, IAsyncDisposable
{
    private sealed class Session(Process process, string directory)
    {
        public Process Process = process;
        public string Directory = directory;
        public int Port;
        public SemaphoreSlim Gate = new(1);
        public CancellationTokenSource Lifetime = new();
    }
    private readonly ConcurrentDictionary<string, Session> sessions = new();
    private static string? Executable => new[] { "/usr/bin/chromium", "/usr/bin/chromium-browser", "/usr/bin/google-chrome", "/opt/google/chrome/chrome" }.FirstOrDefault(File.Exists);
    public bool IsAvailable => OperatingSystem.IsLinux() && Executable is not null &&
        (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")));
    public async Task<string> OpenAsync(string accountId, CancellationToken ct)
    {
        if (!MiMoConsoleIdentity.IsIdentifier(accountId) || !IsAvailable) throw new InvalidOperationException("MiMo monitoring requires an installed Chromium browser and a desktop session.");
        var profile = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(paths.Credentials, "console", "mimo", profile);
        OwnStatePaths.EnsurePrivateDirectory(directory);
        OwnStatePaths.WritePrivate(Path.Combine(directory, "Default", "Preferences"), "{\"credentials_enable_service\":false,\"profile\":{\"password_manager_enabled\":false}}");
        var start = new ProcessStartInfo(Executable!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "--user-data-dir=" + directory, "--remote-debugging-address=127.0.0.1", "--remote-debugging-port=0",
            "--no-first-run", "--no-default-browser-check", "--disable-sync", "--disable-extensions", "--new-window", "https://platform.xiaomimimo.com/" }) start.ArgumentList.Add(argument);
        var process = Process.Start(start) ?? throw new InvalidOperationException("The MiMo browser could not start.");
        _ = DrainAsync(process.StandardOutput); _ = DrainAsync(process.StandardError);
        var session = new Session(process, directory);
        sessions[profile] = session;
        try
        {
            OwnStatePaths.WritePrivate(Path.Combine(directory, "lain-process.json"), JsonSerializer.Serialize(new { pid = process.Id, startedAt = process.StartTime.ToUniversalTime().Ticks }));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (process.HasExited) throw new InvalidOperationException("The MiMo browser could not start.");
                var portPath = Path.Combine(directory, "DevToolsActivePort");
                if (File.Exists(portPath))
                {
                    if (new FileInfo(portPath).LinkTarget is not null || new FileInfo(portPath).Length > 4096) throw new InvalidOperationException("Invalid owned browser endpoint.");
                    var first = (await File.ReadAllLinesAsync(portPath, timeout.Token)).FirstOrDefault();
                    if (int.TryParse(first, out var port) && port is > 1024 and <= 65535) { session.Port = port; return profile; }
                }
                await Task.Delay(100, timeout.Token);
            }
        }
        catch { await CloseAsync(profile, true, CancellationToken.None); throw; }
    }
    public async Task<JsonDocument> ReadAsync(string profile, ConsoleReadOperation operation, CancellationToken ct)
    {
        var route = ConsoleReadRoutes.Resolve(operation);
        if (!sessions.TryGetValue(profile, out var session) || session.Process.HasExited)
            throw new InvalidOperationException("MiMo console session closed; reconnect monitoring.");
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
                    || !value.TryGetProperty("status", out var status) || status.GetInt32() != 200
                    || !value.TryGetProperty("body", out var body) || body.ValueKind != JsonValueKind.String)
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
            var receipt = Path.Combine(directory, "lain-process.json");
            if (new FileInfo(receipt).LinkTarget is not null) throw new IOException("Invalid browser process receipt.");
            if (File.Exists(receipt))
            {
                if (new FileInfo(receipt).Length > 4096) throw new IOException("Invalid browser process receipt.");
                using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(receipt, ct));
                try
                {
                    using var process = Process.GetProcessById(saved.RootElement.GetProperty("pid").GetInt32());
                    if (!process.HasExited && process.StartTime.ToUniversalTime().Ticks == saved.RootElement.GetProperty("startedAt").GetInt64())
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
            if (!sessions.TryRemove(profile, out _)) return;
            try { if (!session.Process.HasExited) { session.Process.Kill(entireProcessTree: true); await session.Process.WaitForExitAsync(ct); } }
            catch (InvalidOperationException) { }
            session.Process.Dispose();
            // Only our randomly allocated directory; never a user's browser profile.
            if (erase && Directory.Exists(session.Directory)) Directory.Delete(session.Directory, recursive: true);
        }
        finally { session.Gate.Release(); }
    }
    private static async Task DrainAsync(StreamReader reader) { try { var buffer = new char[4096]; while (await reader.ReadAsync(buffer) > 0) { } } catch (IOException) { } }
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
    public async ValueTask DisposeAsync() { foreach (var profile in sessions.Keys.ToArray()) await CloseAsync(profile, false, CancellationToken.None); }
}
