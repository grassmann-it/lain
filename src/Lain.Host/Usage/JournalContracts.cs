using System.Text.Json;

namespace Lain.Host.Usage;

/// <summary>Adapter-only journal v1. No credentials, headers or request/response bodies.</summary>
public sealed record JournalEvent(
    int SchemaVersion, string SourceId, long Sequence, string EventId, string Kind,
    string RequestId, string AttemptId, string AccountId, string CredentialId,
    string? RouteId, DateTimeOffset ObservedAt, string Provider, string RequestedModel,
    string Model, string? Outcome = null, long? InputTokens = null,
    long? OutputTokens = null, long? TotalTokens = null, long? CacheReadTokens = null,
    long? CacheCreationTokens = null, long? ReasoningTokens = null,
    bool UsageReported = false, int? StatusCode = null, double? LatencyMs = null,
    double? TtftMs = null);

public sealed record JournalBatch(string SourceId, JournalEvent[] Events, long NextCursor, long HighWatermark);

public static class JournalJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
