namespace Lain.Host.Recording.Storage;

public sealed partial class RecordingSourceStore
{
    // Policy revisions may change while observations are queued. Capture limits again at
    // admission/commit/maintenance; the policy gate independently rejects stale observations.
    // Existing records always decode against the constructor ceiling, so lowering a policy
    // record limit does not misclassify previously valid retained source as corrupt.
    private int MaximumAcceptedRecordBytes => Math.Clamp(policy.Snapshot.EffectiveLimits.MaxRecordBytes, 0, options.MaximumRecordBytes);
    private long MaximumQueuedBytes => Math.Clamp(policy.Snapshot.EffectiveLimits.QueueBytes, 0, options.QueueBytes);

    private long SourceDiskBudget
    {
        get {
            var maximum = Math.Clamp(policy.Snapshot.EffectiveLimits.DiskBytes, 0, options.MaximumDiskBytes);
            return maximum <= options.ExternalReservedBytes ? 0 : maximum - options.ExternalReservedBytes;
        }
    }

    private (int ContentDays, int MetadataDays) RetentionLimits()
    {
        var limits = policy.Snapshot.EffectiveLimits;
        return (Math.Clamp(limits.ContentRetentionDays, 0, options.ContentRetentionDays),
            Math.Clamp(limits.MetadataRetentionDays, 0, options.MetadataRetentionDays));
    }
}
