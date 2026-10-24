using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace Lain.Desktop.Tray;

/// <summary>Owns one UI per local state directory; a second launch only requests reopening.</summary>
internal sealed class UiInstanceOwner : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();
    private Task? _listener;
    public bool IsOwner { get; }

    public UiInstanceOwner(string stateDirectory)
    {
        var identity = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stateDirectory));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName + "\n" + identity))).ToLowerInvariant();
        _pipeName = $"lain-ui-{hash}";
        _mutex = new Mutex(false, _pipeName);
        try { IsOwner = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsOwner = true; }
    }

    public async Task<bool> RequestReopenAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            await using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token);
            await pipe.WriteAsync("reopen"u8.ToArray(), timeout.Token);
            await pipe.FlushAsync(timeout.Token);
            return true;
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or UnauthorizedAccessException)
        { return false; }
    }

    public void StartListening(Action reopen)
    {
        if (!IsOwner || _listener is not null) return;
        _listener = ListenAsync(reopen);
    }

    private async Task ListenAsync(Action reopen)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_stop.Token);
                using var messageTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                messageTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                var message = new byte[6];
                var count = 0;
                while (count < message.Length)
                {
                    var read = await server.ReadAsync(message.AsMemory(count), messageTimeout.Token);
                    if (read == 0) break;
                    count += read;
                }
                if (count == message.Length && message.AsSpan().SequenceEqual("reopen"u8)) reopen();
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { return; }
            catch (Exception error) when (error is IOException or OperationCanceledException or UnauthorizedAccessException)
            {
                try { await Task.Delay(TimeSpan.FromMilliseconds(250), _stop.Token); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        if (IsOwner) _mutex.ReleaseMutex();
        _mutex.Dispose();
        // The asynchronous listener owns its active pipe until cancellation completes.
    }
}
