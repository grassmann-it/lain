namespace Lain.Host.Recording.Storage;

/// <summary>Experimental, configurable limits. Creating a store never enables capture.</summary>
public sealed record RecordingStoreOptions(
    int QueueCount = 256, long QueueBytes = 16L * 1024 * 1024,
    int MaximumRecordBytes = 16 * 1024 * 1024, int MaximumRecords = 100_000,
    long MaximumDiskBytes = 5L * 1024 * 1024 * 1024,
    long MinimumFreeDiskBytes = 64L * 1024 * 1024,
    int SegmentBytes = 16 * 1024 * 1024, int ReplayBatchSize = 100,
    int ContentRetentionDays = 7, int MetadataRetentionDays = 30);
