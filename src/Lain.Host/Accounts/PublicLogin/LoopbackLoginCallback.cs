using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Lain.Host.Accounts.PublicLogin;

/// <summary>One operation, one callback, IPv4 loopback only. Never exposes callback data to a page.</summary>
internal sealed class LoopbackLoginCallback : IDisposable
{
    private readonly TcpListener listener;
    private readonly CancellationTokenSource lifetime;
    private readonly string state;
    private int disposed;
    private readonly TaskCompletionSource<AuthorizationCallback> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Uri Uri { get; }
    public Task<AuthorizationCallback> Completion => completion.Task;

    public LoopbackLoginCallback(string operationId, DateTimeOffset expiresAt, TimeProvider clock, Func<Uri, string> stateFactory, Uri? registeredUri = null)
    {
        var remaining = expiresAt - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero || remaining > TimeSpan.FromMinutes(10)) throw new ArgumentException("Invalid login lifetime.");
        if (registeredUri is not null) ValidateRegisteredUri(registeredUri);
        listener = new TcpListener(IPAddress.Loopback, registeredUri?.Port ?? 0);
        listener.Start(8);
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Uri = registeredUri ?? new Uri($"http://127.0.0.1:{port}/lain/login/{System.Uri.EscapeDataString(operationId)}/callback");
        try { state = stateFactory(Uri); } catch { listener.Stop(); throw; }
        lifetime = new CancellationTokenSource(remaining, clock);
        _ = ReceiveAsync();
    }

    internal static void ValidateRegisteredUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != "http" || uri.Host is not ("127.0.0.1" or "localhost") || uri.IsDefaultPort
            || uri.Port < 1024 || uri.AbsolutePath == "/" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Poe callback must be an exact registered localhost or 127.0.0.1 HTTP URL with a nonprivileged fixed port and path.");
    }

    private async Task ReceiveAsync()
    {
        try
        {
            // Invalid requests cannot retain a listener indefinitely or allocate unbounded tasks.
            for (var requestNumber = 0; requestNumber < 16; requestNumber++)
            {
                using var connection = await listener.AcceptTcpClientAsync(lifetime.Token);
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                var stream = connection.GetStream();
                AuthorizationCallback? callback = null;
                try
                {
                    var bytes = new byte[8192];
                    var count = 0;
                    while (count < bytes.Length)
                    {
                        var read = await stream.ReadAsync(bytes.AsMemory(count), requestTimeout.Token);
                        if (read == 0) break;
                        count += read;
                        if (Encoding.ASCII.GetString(bytes, 0, count).Contains("\r\n\r\n", StringComparison.Ordinal)) break;
                    }
                    var header = Encoding.ASCII.GetString(bytes, 0, count);
                    var first = header.Split("\r\n", 2)[0].Split(' ');
                    if (count < bytes.Length && header.Contains("\r\n\r\n", StringComparison.Ordinal) && first.Length == 3 && first[0] == "GET"
                        && first[1].StartsWith('/') && !first[1].StartsWith("//", StringComparison.Ordinal)
                        && System.Uri.TryCreate(Uri.GetLeftPart(UriPartial.Authority) + first[1], UriKind.Absolute, out var target)
                        && target.AbsolutePath == Uri.AbsolutePath && target.Fragment.Length == 0)
                    {
                        Dictionary<string, string>? query = null;
                        try { query = ParseQuery(target.Query); } catch (ArgumentException) { }
                        if (query is not null && query.TryGetValue("state", out var receivedState) && StateEquals(receivedState))
                        {
                            if (query.ContainsKey("error") && !query.ContainsKey("code")) callback = new(null, true);
                            else if (query.TryGetValue("code", out var code) && code.Length is > 0 and <= 4096 && !code.Any(char.IsControl)
                                && !query.ContainsKey("error")) callback = new(code, false);
                        }
                    }
                    var body = callback is null ? "Invalid or expired authorization callback." : "Authorization received. You may close this window and return to lain.";
                    var response = $"HTTP/1.1 {(callback is null ? "400 Bad Request" : "200 OK")}\r\nContent-Type: text/plain; charset=utf-8\r\nCache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nContent-Security-Policy: default-src 'none'\r\nConnection: close\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n{body}";
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(response), requestTimeout.Token);
                }
                catch (Exception error) when (error is IOException or OperationCanceledException or ArgumentException) { }
                // Browser disconnect after acceptance must not discard a valid one-time code.
                if (callback is not null) { completion.TrySetResult(callback); return; }
            }
            completion.TrySetException(new InvalidDataException("Too many invalid login callbacks."));
        }
        catch (Exception error) when (error is SocketException or OperationCanceledException or ObjectDisposedException)
        { completion.TrySetCanceled(); }
        finally { Dispose(); }
    }

    private bool StateEquals(string candidate) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(candidate));
    internal static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = System.Uri.UnescapeDataString(parts[0].Replace('+', ' '));
            var value = parts.Length == 2 ? System.Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : "";
            if (!result.TryAdd(key, value)) throw new ArgumentException("Duplicate callback parameter.");
        }
        return result;
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel(); listener.Stop(); completion.TrySetCanceled(); lifetime.Dispose();
    }
    public override string ToString() => "LoopbackLoginCallback [redacted]";
}

public sealed record AuthorizationCallback(string? Code, bool Denied)
{
    public override string ToString() => "AuthorizationCallback [redacted]";
}
