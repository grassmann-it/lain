using Lain.Core;

namespace Lain.Host.Inspector;

/// <summary>Owned-core adapter contract. Never exposed as an unfiltered public response.</summary>
public sealed record CoreInspectorBatch(
    string InstanceId, long EarliestCursor, long NextCursor, long HighWatermark,
    long DroppedCount, CoreInspectorEvent[] Events);

public sealed record CoreInspectorEvent(
    long Sequence, DateTimeOffset ObservedAt, string Kind, string Scope,
    string RequestId, string AttemptId, string? AccountId = null,
    string? RouteId = null, string? Provider = null, string? RequestedModel = null,
    string? Model = null, AttemptDimensionsDto? Dimensions = null,
    AttemptMeasurementsDto? Measurements = null, string? Outcome = null,
    string? Text = null, string? ToolName = null, string? ToolCallId = null,
    bool Truncated = false, TokenCounts? Tokens = null, int? StatusCode = null,
    long? InputTokens = null, long? OutputTokens = null, long? TotalTokens = null);
