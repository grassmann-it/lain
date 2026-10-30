namespace Lain.Core;

/// <summary>Local opt-in archive contract. Unknown values remain null; source IDs are never guessed.</summary>
public static class RecordingContract
{
    public const int Version = 1;
    public const string RedactionVersion = "recording_secrets_v1";
    public const string OwnedGatewayScope = "owned_gateway";
    public const string MetadataOnly = "metadata_only";
    public const string Content = "content";
}

public sealed record RecordingLimitsDto(
    int MaxRecordBytes = 16 * 1024 * 1024, long QueueBytes = 64L * 1024 * 1024,
    long DiskBytes = 5L * 1024 * 1024 * 1024, int ContentRetentionDays = 7,
    int MetadataRetentionDays = 30, int MaxPageSize = 100, int MaxBodyDepth = 64);

/// <summary>Independent from inference/profile revisions. Enablement explicitly covers all models on the owned gateway.</summary>
public sealed record CapturePolicySnapshot(
    long Revision = 0, bool Enabled = false, string Mode = RecordingContract.MetadataOnly,
    string Scope = RecordingContract.OwnedGatewayScope, long StopGeneration = 0,
    string[]? RouteIds = null, RecordingLimitsDto? Limits = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public RecordingLimitsDto EffectiveLimits => Limits ?? new();
}

public sealed record RecordingPolicyUpdate(long ExpectedRevision, bool Enabled,
    string Mode = RecordingContract.MetadataOnly, string Scope = RecordingContract.OwnedGatewayScope,
    string[]? RouteIds = null, RecordingLimitsDto? Limits = null);
public sealed record RecordingPolicyResult(bool Applied, CapturePolicySnapshot Policy, string? ErrorCode = null);

/// <summary>Only trusted local source transport may construct these transient raw observations. No header/auth maps.</summary>
public sealed record SourceObservation(
    string SourceEpoch, long Sequence, string EventId, string CaptureId,
    string RequestId, DateTimeOffset ObservedAt, string Boundary, string Protocol, string Kind,
    string? AttemptId = null, string? RouteId = null, string? AccountId = null,
    string? Provider = null, string? Model = null, string? RequestedModel = null,
    AttemptDimensionsDto? Dimensions = null, string? BodyEncoding = null, byte[]? Body = null,
    bool Complete = true, bool Truncated = false, string? GapReason = null,
    int? StatusCode = null, TokenCounts? Tokens = null, long? MonotonicNanoseconds = null,
    int SchemaVersion = RecordingContract.Version, long PolicyRevision = 0, long StopGeneration = 0);

public sealed record SourceObservationBatch(string SourceEpoch, SourceObservation[] Observations,
    long DroppedCount = 0, bool UnknownLoss = false, string? GapReason = null);
public sealed record RecordingSourceReference(string SourceEpoch, long Sequence, string EventId);

/// <summary>Persistence seam: contains only retention-approved metadata and sanitized UTF-8 JSON, never raw bytes.</summary>
public sealed record SanitizedObservation(
    string SourceEpoch, long Sequence, string EventId, string CaptureId,
    string RequestId, DateTimeOffset ObservedAt, string Boundary, string Protocol, string Kind,
    long PolicyRevision, long StopGeneration, string RedactionVersion, string RedactionState,
    string? AttemptId = null, string? RouteId = null, string? AccountId = null,
    string? Provider = null, string? Model = null, string? RequestedModel = null,
    AttemptDimensionsDto? Dimensions = null, string? BodyEncoding = null, string? Body = null,
    bool Complete = true, bool Truncated = false, string? GapReason = null,
    int? StatusCode = null, TokenCounts? Tokens = null, long? MonotonicNanoseconds = null,
    int SchemaVersion = RecordingContract.Version);

public sealed record CommittedObservation(SanitizedObservation Observation, long Cursor,
    string JournalId, long JournalOffset, string ContentHash, int JournalVersion = 1);
public sealed record RecordingCommitResult(bool Committed, CommittedObservation? Record = null, string? ErrorCode = null);
public sealed record RecordingCommitPage(CommittedObservation[] Records, long NextCursor,
    long HighWatermark, bool HasMore, string? GapReason = null);

public interface IRecordingPolicy
{
    CapturePolicySnapshot Snapshot { get; }
    RecordingPolicyResult Update(RecordingPolicyUpdate update);
    bool IsCurrent(long revision, long stopGeneration);
    SanitizedObservation? Sanitize(SourceObservation observation, CapturePolicySnapshot policy);
}

/// <summary>The policy gate must be rechecked at commit; enqueue is not durable commit.</summary>
public interface IRecordingStore
{
    Task<RecordingCommitResult> CommitAsync(SanitizedObservation observation, CancellationToken ct = default);
    Task<RecordingCommitPage> ReadCommittedAsync(long after = 0, int limit = 100, CancellationToken ct = default);
    Task<RecordingStorageHealthDto> HealthAsync(CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
}

public sealed record RecordingContentPart(string Kind, string? Text = null, string? ToolCallId = null,
    string? ToolName = null, string? Arguments = null, string? Result = null,
    string Evidence = "observed", bool Complete = true);
public sealed record RecordingMessage(string Id, string Role, RecordingContentPart[] Parts,
    RecordingSourceReference[] Sources, string? NativeId = null, string? State = null,
    bool Complete = true, string? GapReason = null);
public sealed record RecordingUsage(long? Input = null, long? Output = null, long? Cached = null,
    long? Reasoning = null, long? Total = null, string? Source = null);
public sealed record RecordingNormalizedRecord(string Id, string CaptureId, string RequestId,
    string? AttemptId, DateTimeOffset ObservedAt, string Protocol, RecordingMessage[] Messages,
    RecordingSourceReference[] Sources, string? ConversationId = null, string? ParentConversationId = null,
    string? WorkerId = null, string? ParentWorkerId = null, string? ParentRequestId = null,
    string? Model = null, string? RequestedModel = null, string? RouteId = null,
    string? Provider = null, string? AccountId = null, string? ResponseId = null,
    string? PreviousResponseId = null, string? FinishReason = null, RecordingUsage? Usage = null,
    bool Complete = true, string? GapReason = null, string? ReasoningEffort = null,
    string? EffectiveReasoningEffort = null, long? ContextWindow = null, string? ModelRevision = null,
    int ProjectionVersion = 1);

public interface IRecordingNormalizer
{
    IReadOnlyList<RecordingNormalizedRecord> Normalize(CommittedObservation observation);
    void Reset();
}

public sealed record RecordingRunManifest(string Id, string? Variant = null,
    string? ConfiguredRevision = null, string? PromptHash = null, string? SkillHash = null,
    string? ObservedInstructionHash = null, string Evidence = "configured");
public sealed record RecordingConversationSummary(string Id, DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt, int RequestCount, int MessageCount, string[] Models,
    string State = "partial", bool Complete = false, string? ParentId = null,
    string? WorkerId = null, string? ParentWorkerId = null, string? Title = null);
public sealed record RecordingConversationDetail(RecordingConversationSummary Conversation,
    RecordingNormalizedRecord[] Records, string? NextCursor = null, bool HasMore = false,
    RecordingRunManifest[]? Runs = null, string? GapReason = null);
public sealed record RecordingConversationQuery(int Limit = 50, string? Cursor = null,
    string? Model = null, string? RouteId = null, string? WorkerId = null);
public sealed record RecordingConversationPage(RecordingConversationSummary[] Items,
    string? NextCursor = null, bool HasMore = false, string? ErrorCode = null, long Snapshot = 0);

public interface IRecordingProjection
{
    Task ApplyAsync(CommittedObservation observation, CancellationToken ct = default);
    Task RebuildAsync(CancellationToken ct = default);
    Task<RecordingConversationPage> ListAsync(RecordingConversationQuery query, CancellationToken ct = default);
    Task<RecordingConversationDetail?> GetAsync(string conversationId, string? cursor = null,
        int limit = 100, CancellationToken ct = default);
}

public sealed record RecordingStorageHealthDto(long DiskBytes = 0, long RecordCount = 0,
    long HighWatermark = 0, long RetainedFromCursor = 0, string State = "ready",
    string? ErrorCode = null, long DroppedRecords = 0);
public sealed record RecordingHealthDto(int ContractVersion, CapturePolicySnapshot Policy,
    string State, long QueueBytes = 0, int QueueCount = 0, long DroppedRecords = 0,
    bool UnknownLoss = false, RecordingStorageHealthDto? Storage = null,
    string[]? Coverage = null, string? ErrorCode = null);
public sealed record RecordingClearRequest(long ExpectedRevision);
public sealed record RecordingClearResult(bool Cleared, string? ErrorCode = null);
