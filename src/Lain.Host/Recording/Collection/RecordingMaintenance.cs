using Lain.Core;
using Microsoft.Extensions.Hosting;

namespace Lain.Host.Recording.Collection;

/// <summary>Retention works independently of the UI and never participates in inference delivery.</summary>
public sealed class RecordingMaintenance(IRecordingStore store, IRecordingProjection projection) : BackgroundService
{
    // Reconcile once after restart as well: a previous process may have stopped between
    // source purge and index synchronization. Production RecoverAsync is inert for a new
    // empty archive and uses the saved source stamp to avoid rebuilding an unchanged index.
    private bool projectionDirty = true;

    public async Task RunOnceAsync(CancellationToken ct = default)
    {
        if (store is not IRecordingMaintenance maintenance) return;
        var before = await store.HealthAsync(ct);
        if (before.State == "recovering") return;
        try { await maintenance.ApplyRetentionAsync(ct); }
        catch { projectionDirty = true; throw; }
        finally
        {
            // A purge can publish a logical visibility change before physical cleanup fails.
            // Synchronize that changed source too, so indexed titles cannot outlive denial.
            var after = await store.HealthAsync(ct);
            if (Stamp(before) != Stamp(after)) projectionDirty = true;
            if (after.State != "recovering" && projectionDirty)
            {
                if (projection is IRecordingProjectionRecovery recovery) await recovery.RecoverAsync(ct);
                else if (after.RecordCount != 0 || Stamp(before) != Stamp(after)) await projection.RebuildAsync(ct);
                // Retain dirty state on failure, including a later unchanged maintenance tick.
                projectionDirty = false;
            }
        }
    }

    private static (long HighWatermark, long RecordCount, long RetainedFromCursor) Stamp(RecordingStorageHealthDto health) =>
        (health.HighWatermark, health.RecordCount, health.RetainedFromCursor);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            do
            {
                try { await RunOnceAsync(ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch { /* Retry dirty index/source maintenance; never log raw paths or content. */ }
            } while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
}
