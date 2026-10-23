using System.Text.Json;
using Lain.Core;
using Microsoft.Data.Sqlite;

namespace Lain.Host.Usage;

public sealed partial class SqliteUsageStore
{
    private static readonly string[] DimensionFields =
    [
        "modelFamily", "manufacturer", "inferenceProvider", "gateway", "billingAccountId", "reasoningEffort", "mode",
        "conversationId", "turnId", "workerId", "parentWorkerId", "parentRequestId", "toolCallId", "harness", "profileId",
        "responseId", "previousResponseId", "effectiveReasoningEffort"
    ];
    private static readonly HashSet<string> EvidenceSources = ["route_binding", "harness_header", "request_body", "provider_response", "core"];

    private static Dictionary<string, string?> Values(AttemptDimensionsDto d) => new(StringComparer.Ordinal)
    {
        ["modelFamily"] = d.ModelFamily, ["manufacturer"] = d.Manufacturer, ["inferenceProvider"] = d.InferenceProvider,
        ["gateway"] = d.Gateway, ["billingAccountId"] = d.BillingAccountId, ["reasoningEffort"] = d.ReasoningEffort,
        ["mode"] = d.Mode, ["conversationId"] = d.ConversationId, ["turnId"] = d.TurnId, ["workerId"] = d.WorkerId,
        ["parentWorkerId"] = d.ParentWorkerId, ["parentRequestId"] = d.ParentRequestId, ["toolCallId"] = d.ToolCallId,
        ["harness"] = d.Harness, ["profileId"] = d.ProfileId, ["responseId"] = d.ResponseId, ["previousResponseId"] = d.PreviousResponseId,
        ["effectiveReasoningEffort"] = d.EffectiveReasoningEffort
    };

    private static void ValidateDimensions(AttemptDimensionsDto? dimensions)
    {
        if (dimensions is null) return;
        var values = Values(dimensions);
        var evidence = dimensions.Evidence ?? [];
        if (evidence.Length > 64) throw new InvalidDataException("Too much dimension evidence.");
        foreach (var item in evidence)
        {
            if (item is null || !values.ContainsKey(item.Field) || !EvidenceSources.Contains(item.Source) ||
                item.Key is { } key && (key.Length > 128 || key.Any(c => char.IsControl(c))))
                throw new InvalidDataException("Invalid dimension provenance.");
        }
        foreach (var (field, value) in values)
        {
            if (value is null) continue;
            if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl) || !evidence.Any(e => e.Field == field))
                throw new InvalidDataException("A dimension requires a bounded value and explicit provenance.");
        }
    }

    private static void ValidateMeasurements(AttemptMeasurementsDto? measurements)
    {
        if (measurements is null) return;
        if (measurements.Source != "core_monotonic_v1" ||
            new[] { measurements.FirstOutputLatencyMs, measurements.TotalDurationMs }.Any(v => v is double n && (!double.IsFinite(n) || n < 0)) ||
            measurements.FirstOutputLatencyMs > measurements.TotalDurationMs || measurements.FirstOutputAt > measurements.FinishedAt)
            throw new InvalidDataException("Measurements require valid core monotonic timing provenance.");
    }

    private static AttemptDimensionsDto? MergeDimensions(AttemptDimensionsDto? before, AttemptDimensionsDto? incoming)
    {
        if (before is null) return incoming;
        if (incoming is null) return before;
        var merged = Values(before);
        foreach (var (key, value) in Values(incoming))
        {
            if (value is null) continue;
            if (merged[key] is { } previous && previous != value)
                throw new InvalidDataException("Observed attempt identity changed after dispatch.");
            merged[key] = value;
        }
        var evidence = (before.Evidence ?? []).Concat(incoming.Evidence ?? []).Distinct().ToArray();
        return new(ModelFamily: merged["modelFamily"], Manufacturer: merged["manufacturer"], InferenceProvider: merged["inferenceProvider"],
            Gateway: merged["gateway"], BillingAccountId: merged["billingAccountId"], ReasoningEffort: merged["reasoningEffort"], Mode: merged["mode"],
            ConversationId: merged["conversationId"], TurnId: merged["turnId"], WorkerId: merged["workerId"], ParentWorkerId: merged["parentWorkerId"],
            ParentRequestId: merged["parentRequestId"], ToolCallId: merged["toolCallId"], Harness: merged["harness"], ProfileId: merged["profileId"],
            ResponseId: merged["responseId"], PreviousResponseId: merged["previousResponseId"], Evidence: evidence,
            EffectiveReasoningEffort: merged["effectiveReasoningEffort"]);
    }

    private static T? MergeKnown<T>(T? before, T? incoming) where T : struct
    {
        if (before is not null && incoming is not null && !EqualityComparer<T>.Default.Equals(before.Value, incoming.Value))
            throw new InvalidDataException("An already measured value changed.");
        return incoming ?? before;
    }

    private static AttemptMeasurementsDto? MergeMeasurements(AttemptMeasurementsDto? before, AttemptMeasurementsDto? incoming)
    {
        if (before is null) return incoming;
        if (incoming is null) return before;
        if (before.Source != incoming.Source) throw new InvalidDataException("Measurement provenance changed.");
        return new(MergeKnown(before.FirstOutputLatencyMs, incoming.FirstOutputLatencyMs), MergeKnown(before.TotalDurationMs, incoming.TotalDurationMs),
            MergeKnown(before.FirstOutputAt, incoming.FirstOutputAt), MergeKnown(before.FinishedAt, incoming.FinishedAt), before.Source);
    }

    private static async Task SaveObservationsAsync(SqliteConnection connection, SqliteTransaction transaction, JournalEvent e, CancellationToken ct)
    {
        if (e.Dimensions is null && e.Measurements is null && e.CompletionState is null) return;
        using var read = Command(connection, transaction, "SELECT dimensions_json,measurements_json,completion_state FROM attempts WHERE source_id=$source AND attempt_id=$attempt",
            ("$source", e.SourceId), ("$attempt", e.AttemptId));
        using var reader = await read.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidDataException("Observations require an existing attempt.");
        var dimensions = MergeDimensions(reader.IsDBNull(0) ? null : JsonSerializer.Deserialize<AttemptDimensionsDto>(reader.GetString(0), JournalJson.Options), e.Dimensions);
        var measurements = MergeMeasurements(reader.IsDBNull(1) ? null : JsonSerializer.Deserialize<AttemptMeasurementsDto>(reader.GetString(1), JournalJson.Options), e.Measurements);
        var previousCompletion = reader.IsDBNull(2) ? null : reader.GetString(2);
        if (previousCompletion is not null && e.CompletionState is not null && previousCompletion != e.CompletionState)
            throw new InvalidDataException("Observed wrapper completion changed.");
        var completion = e.CompletionState ?? previousCompletion;
        reader.Close();
        ValidateDimensions(dimensions);
        ValidateMeasurements(measurements);
        using var update = Command(connection, transaction, """
            UPDATE attempts SET dimensions_json=$dimensions,measurements_json=$measurements,
                first_output_latency_ms=$first,total_duration_ms=$duration,completion_state=$completion
            WHERE source_id=$source AND attempt_id=$attempt
            """, ("$source", e.SourceId), ("$attempt", e.AttemptId),
            ("$dimensions", dimensions is null ? null : JsonSerializer.Serialize(dimensions, JournalJson.Options)),
            ("$measurements", measurements is null ? null : JsonSerializer.Serialize(measurements, JournalJson.Options)),
            ("$first", measurements?.FirstOutputLatencyMs), ("$duration", measurements?.TotalDurationMs), ("$completion", completion));
        await update.ExecuteNonQueryAsync(ct);
    }
}
