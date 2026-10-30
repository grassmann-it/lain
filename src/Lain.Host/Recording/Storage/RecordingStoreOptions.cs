namespace Lain.Host.Recording.Storage;

/// <summary>Constructor hard ceilings; the current policy can impose lower limits without reopening the store.</summary>
public sealed record RecordingStoreOptions(
    int QueueCount = 256, long QueueBytes = 16L * 1024 * 1024,
    int MaximumRecordBytes = 16 * 1024 * 1024, int MaximumRecords = 100_000,
    long MaximumDiskBytes = 5L * 1024 * 1024 * 1024,
    long MinimumFreeDiskBytes = 64L * 1024 * 1024,
    int SegmentBytes = 16 * 1024 * 1024, int ReplayBatchSize = 100,
    int ContentRetentionDays = 7, int MetadataRetentionDays = 30,
    long ExternalReservedBytes = 0)
{
    /// <summary>Permit the policy's supported range while preserving explicit constructor hard ceilings.</summary>
    public static RecordingStoreOptions SuggestedProductionOptions(long externalReservedBytes = 0) => new(
        QueueBytes: 1024L * 1024 * 1024,
        MaximumRecordBytes: 64 * 1024 * 1024,
        MaximumDiskBytes: 1024L * 1024 * 1024 * 1024,
        ContentRetentionDays: 3650, MetadataRetentionDays: 3650,
        ExternalReservedBytes: externalReservedBytes);
}
