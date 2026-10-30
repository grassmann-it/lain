using Lain.Core;
using Microsoft.Extensions.Hosting;

namespace Lain.Host.Recording.Collection;

/// <summary>Retention works independently of the UI and never participates in inference delivery.</summary>
public sealed class RecordingMaintenance(IRecordingStore store) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            do
            {
                if (store is IRecordingMaintenance maintenance)
                    try { await maintenance.ApplyRetentionAsync(ct); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                    catch { /* Fixed archive health records failure; no raw paths or content logged. */ }
            } while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
}
