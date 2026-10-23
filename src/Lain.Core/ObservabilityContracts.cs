namespace Lain.Core;

/// <summary>Evidence identifies a curated source and field, never a raw header or payload value.</summary>
public sealed record DimensionEvidenceDto(string Field, string Source, string? Key = null);

/// <summary>Only observed or explicitly configured dimensions; null means unknown. IDs require matching evidence.</summary>
public sealed record AttemptDimensionsDto(
    string? ModelFamily = null, string? Manufacturer = null, string? InferenceProvider = null,
    string? Gateway = null, string? BillingAccountId = null, string? ReasoningEffort = null,
    string? Mode = null, string? ConversationId = null, string? TurnId = null,
    string? WorkerId = null, string? ParentWorkerId = null, string? ParentRequestId = null,
    string? ToolCallId = null, string? Harness = null, string? ProfileId = null,
    DimensionEvidenceDto[]? Evidence = null, string? ResponseId = null, string? PreviousResponseId = null);

/// <summary>Monotonic durations measured by the owned core, distinct from historical provider timing fields.</summary>
public sealed record AttemptMeasurementsDto(
    double? FirstOutputLatencyMs = null, double? TotalDurationMs = null,
    DateTimeOffset? FirstOutputAt = null, DateTimeOffset? FinishedAt = null, string? Source = null);

public sealed record MetricStatisticsDto(long Samples, long Eligible, double? Minimum, double? Mean, double? P50, double? P95, double? Maximum);
public sealed record PerformanceSummaryDto(
    MetricStatisticsDto FirstOutputLatencyMs, MetricStatisticsDto TotalDurationMs,
    MetricStatisticsDto OutputTokensPerSecond,
    string RateBasis = "reported_output_tokens_per_total_duration_second");
public sealed record UsageDimensionGroupDto(string? Key, UsageTotals Totals, PerformanceSummaryDto Performance);
public sealed record UsageDimensionDto(string Dimension, UsageDimensionGroupDto[] Groups, bool Truncated = false);
public sealed record UsageAnalysisDto(
    int ContractVersion, DateTimeOffset From, DateTimeOffset To, string TimeZone,
    DateTimeOffset ObservedAt, long Cursor, bool Complete, UsageTotals Totals,
    PerformanceSummaryDto Performance, UsageDimensionDto[] Dimensions);

public interface IUsageAnalysisStore
{
    Task<UsageAnalysisDto> AnalysisAsync(UsageQuery query, CancellationToken ct = default);
}

public sealed record InspectorQuery(string? RequestId = null, int Limit = 100);
public sealed record InspectorEventQuery(string? SourceId = null, long After = 0, int Limit = 100, string? RequestId = null);
public sealed record InspectorLimitsDto(
    int MaxStreams = 256, int MaxEvents = 8192, long ContentBytes = 33554432,
    long PerStreamBytes = 1048576, int MaxEventBytes = 262144,
    int ContentTtlSeconds = 120, int TerminalTtlSeconds = 60, int MaxPageSize = 500,
    string ContentPolicy = "redacted_output_only_v1");

public sealed record InspectorStreamDto(
    string Id, string Scope, string RequestId, string? AttemptId,
    DateTimeOffset StartedAt, DateTimeOffset UpdatedAt, string State,
    long FirstCursor, long LastCursor,
    string? AccountId = null, string? RouteId = null, string? Provider = null,
    string? Model = null, DateTimeOffset? FinishedAt = null,
    AttemptDimensionsDto? Dimensions = null, TokenCounts? Tokens = null,
    int? StatusCode = null, AttemptMeasurementsDto? Measurements = null,
    bool Truncated = false, string? RequestedModel = null);

public sealed record InspectorEventDto(
    long Cursor, string Kind, DateTimeOffset ObservedAt, string RequestId,
    string? AttemptId = null, string? Scope = null, string? AccountId = null,
    string? RouteId = null, string? Provider = null, string? Model = null,
    AttemptDimensionsDto? Dimensions = null, string? Channel = null, string? Text = null,
    string? ToolCallId = null, string? ToolName = null, string? State = null,
    int? StatusCode = null, TokenCounts? Tokens = null,
    AttemptMeasurementsDto? Measurements = null, bool Truncated = false,
    string? GapReason = null, string? RequestedModel = null, string? RedactionState = null);

public sealed record InspectorSnapshotDto(
    int ContractVersion, string SourceId, DateTimeOffset ObservedAt, string State,
    bool Complete, long Cursor, long EarliestCursor, long DroppedEvents,
    InspectorLimitsDto Limits, InspectorStreamDto[] Streams, string? Error = null);

public sealed record InspectorEventPageDto(
    string SourceId, InspectorEventDto[] Events, long NextCursor, long HighWatermark,
    long EarliestCursor, long DroppedEvents, bool Truncated = false, string? GapReason = null);
