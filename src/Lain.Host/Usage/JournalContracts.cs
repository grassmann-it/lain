using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Lain.Core;

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
    double? TtftMs = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AttemptDimensionsDto? Dimensions = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AttemptMeasurementsDto? Measurements = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CompletionState = null);

public sealed record JournalBatch(string SourceId, JournalEvent[] Events, long NextCursor, long HighWatermark);

public static class JournalJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type != typeof(AttemptDimensionsDto) && info.Type != typeof(AttemptMeasurementsDto)) return;
            foreach (var property in info.Properties) property.ShouldSerialize = (_, value) => value is not null;
        });
        // Keep the legacy journal's explicit null fields byte-for-byte stable.
        // Only the newly introduced extensible metadata objects omit unknowns.
        return new(JsonSerializerDefaults.Web) { TypeInfoResolver = resolver };
    }
}
