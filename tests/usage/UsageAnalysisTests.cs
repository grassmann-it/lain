using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Usage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lain.Usage.Tests;

public sealed partial class UsageStoreTests
{
    private void RestoreLegacySchema(int version)
    {
        using var connection = new SqliteConnection($"Data Source={Database};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DROP INDEX attempts_route_time;
            DROP INDEX attempts_family_time;
            DROP INDEX attempts_conversation_time;
            DROP INDEX attempts_worker_time;
            ALTER TABLE attempts DROP COLUMN dimensions_json;
            ALTER TABLE attempts DROP COLUMN measurements_json;
            ALTER TABLE attempts DROP COLUMN first_output_latency_ms;
            ALTER TABLE attempts DROP COLUMN total_duration_ms;
            ALTER TABLE attempts DROP COLUMN completion_recorded;
            ALTER TABLE attempts DROP COLUMN completion_state;
            ALTER TABLE attempts DROP COLUMN error_category;
            """ + (version == 1 ? "DROP TABLE coverage_gaps; DROP TABLE collector_state;" : "") + $"PRAGMA user_version={version};";
        command.ExecuteNonQuery();
    }

    private static AttemptDimensionsDto Dimensions(string gateway = "gateway-a", string worker = "worker-a") => new(
        ModelFamily: "step-5", Manufacturer: "stepfun", InferenceProvider: "inference-a", Gateway: gateway,
        BillingAccountId: "billing-a", ReasoningEffort: "high", Mode: "reasoning", ConversationId: "conversation-a",
        TurnId: "turn-a", WorkerId: worker, ParentWorkerId: "parent-worker", ParentRequestId: "parent-request",
        ToolCallId: "tool-call", Harness: "synthetic-harness", ProfileId: "profile-a", ResponseId: "response-a", PreviousResponseId: "response-before",
        Evidence: new[] { "modelFamily", "manufacturer", "inferenceProvider", "gateway", "billingAccountId", "reasoningEffort", "mode", "conversationId", "turnId", "workerId", "parentWorkerId", "parentRequestId", "toolCallId", "harness", "profileId", "responseId", "previousResponseId" }
            .Select(field => new DimensionEvidenceDto(field, "core", "fixture." + field)).ToArray());

    private static AttemptMeasurementsDto Measurements(double? first = 50, double? duration = 2000) =>
        new(first, duration, first is null ? null : Start.AddMilliseconds(first.Value), duration is null ? null : Start.AddMilliseconds(duration.Value), "core_monotonic_v1");

    [Fact]
    public void OptionalJournalMetadataDoesNotChangeLegacyCanonicalDigest()
    {
        const string oldCanonical = """
            {"schemaVersion":1,"sourceId":"source-a","sequence":1,"eventId":"source-a:1","kind":"attempt_started","requestId":"r1","attemptId":"a1","accountId":"account-a","credentialId":"credential-account-a","routeId":"route-a","observedAt":"2026-03-28T23:30:00+00:00","provider":"fake","requestedModel":"alias","model":"real-model","outcome":null,"inputTokens":null,"outputTokens":null,"totalTokens":null,"cacheReadTokens":null,"cacheCreationTokens":null,"reasoningTokens":null,"usageReported":false,"statusCode":null,"latencyMs":null,"ttftMs":null}
            """;
        var parsed = JsonSerializer.Deserialize<JournalEvent>(oldCanonical, JournalJson.Options)!;
        var current = JsonSerializer.Serialize(parsed, JournalJson.Options);
        Assert.Equal(oldCanonical, current);
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(oldCanonical)), SHA256.HashData(Encoding.UTF8.GetBytes(current)));
    }

    [Fact]
    public async Task NestedUnknownFieldsDoNotChangeJournalDigestAndRequestedReasoningStaysSeparate()
    {
        var dimensions = new AttemptDimensionsDto(ReasoningEffort: "high", Evidence: [new("reasoningEffort", "request_body", "reasoning.effort")]);
        const string nestedCanonical = """{"reasoningEffort":"high","evidence":[{"field":"reasoningEffort","source":"request_body","key":"reasoning.effort"}]}""";
        Assert.Equal(nestedCanonical, JsonSerializer.Serialize(dimensions, JournalJson.Options));
        const string measuredCanonical = """{"firstOutputLatencyMs":50,"source":"core_monotonic_v1"}""";
        Assert.Equal(measuredCanonical, JsonSerializer.Serialize(new AttemptMeasurementsDto(FirstOutputLatencyMs: 50, Source: "core_monotonic_v1"), JournalJson.Options));
        var started = Started(1) with { Dimensions = dimensions };
        var serialized = JsonSerializer.Serialize(started, JournalJson.Options);
        Assert.DoesNotContain("effectiveReasoningEffort", serialized);
        Assert.DoesNotContain("\"measurements\"", serialized);
        var store = new SqliteUsageStore(Database);
        await store.IngestAsync(Batch(JsonSerializer.Deserialize<JournalEvent>(serialized, JournalJson.Options)!));
        var usage = Finished(2, started) with { Dimensions = new(EffectiveReasoningEffort: "medium", Evidence: [new("effectiveReasoningEffort", "core", "usage.reasoningEffort")]) };
        await store.IngestAsync(Batch(usage));
        for (var replay = 0; replay < 10; replay++) await store.IngestAsync(Batch(started, usage));
        var result = await store.AnalysisAsync(Query() with { GroupBy = "reasoningEffort,effectiveReasoningEffort", ReasoningEffort = "high", EffectiveReasoningEffort = "medium" });
        Assert.Equal(1, result.Totals.Attempts);
        Assert.Equal("high", Assert.Single(result.Dimensions[0].Groups).Key);
        Assert.Equal("medium", Assert.Single(result.Dimensions[1].Groups).Key);
        var row = Assert.Single((await store.AttemptsAsync(Query())).Items);
        Assert.Equal("high", row.Dimensions!.ReasoningEffort);
        Assert.Equal("medium", row.Dimensions.EffectiveReasoningEffort);
    }

    [Fact]
    public async Task VersionTwoUpgradePreservesReplayAndKeepsNewIdentitiesAndMeasurementsUnknown()
    {
        var started = Started(1);
        var batch = Batch(started, Finished(2, started));
        await new SqliteUsageStore(Database).IngestAsync(batch);
        RestoreLegacySchema(2);
        var store = new SqliteUsageStore(Database);
        for (var replay = 0; replay < 10; replay++) await store.IngestAsync(batch);
        Assert.Equal(4, InspectLong("PRAGMA user_version"));
        Assert.Equal(2, await store.GetCursorAsync("source-a"));
        var row = Assert.Single((await store.AttemptsAsync(Query())).Items);
        Assert.Null(row.Dimensions);
        Assert.Null(row.Measurements);
        Assert.Equal(2000d, row.DurationMs); // Historical timing is retained, never promoted to a new measurement.
        var analysis = await store.AnalysisAsync(Query());
        Assert.Equal(14L, analysis.Totals.Tokens.Total);
        Assert.True(analysis.Complete);
        Assert.Equal(0, analysis.Performance.TotalDurationMs.Samples);
        Assert.Equal(1, analysis.Performance.TotalDurationMs.Eligible);
        Assert.Null(analysis.Performance.TotalDurationMs.P50);
        Assert.Null(Assert.Single(analysis.Dimensions.Single(d => d.Dimension == "modelFamily").Groups).Key);
    }

    [Fact]
    public async Task ProvenFamilyAggregatesAcrossRoutesWithoutCollapsingGatewayOrWorkerIdentity()
    {
        var first = Started(1) with { Dimensions = Dimensions(), RouteId = "route-nvidia" };
        var second = Started(3, "a2", "r2", "account-b") with { Dimensions = Dimensions("gateway-b", "worker-b"), RouteId = "route-poe" };
        var unknown = Started(5, "a3", "r3");
        var store = new SqliteUsageStore(Database);
        await store.IngestAsync(Batch(first, Finished(2, first), second, Finished(4, second), unknown, Finished(6, unknown)));
        var result = await store.AnalysisAsync(Query() with { GroupBy = "modelFamily,gateway,routeId" });
        var families = result.Dimensions.Single(d => d.Dimension == "modelFamily").Groups;
        Assert.Equal(2, families.Length);
        Assert.Equal(2, families.Single(g => g.Key == "step-5").Totals.Attempts);
        Assert.Equal(1, families.Single(g => g.Key is null).Totals.Attempts);
        Assert.Equal(3, result.Dimensions.Single(d => d.Dimension == "routeId").Groups.Length);
        var filter = Query() with { ModelFamily = "step-5", Manufacturer = "stepfun", InferenceProvider = "inference-a", Gateway = "gateway-b", BillingAccountId = "billing-a", ReasoningEffort = "high", Mode = "reasoning", WorkerId = "worker-b", ConversationId = "conversation-a", TurnId = "turn-a", Harness = "synthetic-harness", ProfileId = "profile-a", RouteId = "route-poe" };
        var attempt = Assert.Single((await store.AttemptsAsync(filter)).Items);
        Assert.Equal("a2", attempt.Id);
        Assert.Equal("source-a", attempt.SourceId);
        Assert.Equal("alias", attempt.RequestedModel);
        Assert.Equal("parent-worker", attempt.Dimensions!.ParentWorkerId);
        Assert.Equal("response-before", attempt.Dimensions.PreviousResponseId);
        Assert.Equal(17, attempt.Dimensions.Evidence!.Length);
        Assert.Equal(1, (await store.SummaryAsync(filter)).Totals.Attempts);
        Assert.Equal(1, (await store.AnalysisAsync(filter)).Totals.Attempts);
    }

    [Fact]
    public async Task MissingProvenanceAndChangedIdentityRejectWholeTransaction()
    {
        var store = new SqliteUsageStore(Database);
        var invalid = Started(1) with { Dimensions = new(ModelFamily: "guessed-from-model-name") };
        await Assert.ThrowsAsync<InvalidDataException>(() => store.IngestAsync(Batch(invalid)));
        Assert.Equal(0, await store.GetCursorAsync("source-a"));
        var started = Started(1) with { Dimensions = Dimensions() };
        await store.IngestAsync(Batch(started));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.IngestAsync(Batch(Finished(2, started) with { Dimensions = Dimensions("changed-gateway") })));
        Assert.Equal(1, await store.GetCursorAsync("source-a"));
        var row = Assert.Single((await store.AttemptsAsync(Query())).Items);
        Assert.Equal("pending", row.State);
        Assert.Equal("gateway-a", row.Dimensions!.Gateway);
        Assert.Null(row.Tokens.Total);
    }

    [Fact]
    public async Task ActualCompletionAddsMeasuredTimingAndOutcomeWithoutRewritingReportedUsage()
    {
        var started = Started(1) with { Dimensions = Dimensions() };
        var usage = Finished(2, started) with { Measurements = Measurements(duration: null) };
        var store = new SqliteUsageStore(Database);
        await store.IngestAsync(Batch(started, usage));
        Assert.Equal(0, (await store.AnalysisAsync(Query())).Performance.TotalDurationMs.Samples);
        var completion = started with { Kind = "attempt_finished", Sequence = 3, EventId = "source-a:3", ObservedAt = Start.AddSeconds(3), Measurements = Measurements(duration: 3000), CompletionState = "canceled" };
        for (var replay = 0; replay < 10; replay++) await store.IngestAsync(Batch(completion));
        var result = await store.AnalysisAsync(Query());
        Assert.Equal(1, result.Totals.Attempts);
        Assert.Equal(14L, result.Totals.Tokens.Total);
        Assert.Equal(1, result.Totals.Errors);
        Assert.Equal(0, (await store.SummaryAsync(Query())).Totals.Errors);
        Assert.Equal(3000d, result.Performance.TotalDurationMs.P50);
        Assert.Equal(50d, result.Performance.FirstOutputLatencyMs.P50);
        Assert.Equal(4d / 3d, result.Performance.OutputTokensPerSecond.P50!.Value, 10);
        var row = Assert.Single((await new SqliteUsageStore(Database).AttemptsAsync(Query())).Items);
        Assert.Equal("success", row.State);
        Assert.Equal("canceled", row.CompletionState);
        Assert.Equal(250d, row.FirstTokenMs);
        Assert.Equal(Start.AddSeconds(3), row.Measurements!.FinishedAt);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.IngestAsync(Batch(completion with { Sequence = 4, EventId = "source-a:4" })));
        Assert.Equal(3, await store.GetCursorAsync("source-a"));
    }

    [Fact]
    public async Task PercentilesUseAllSixHundredFilteredAttemptsNotOneDisplayedAttempt()
    {
        var events = new List<JournalEvent>();
        for (var i = 1; i <= 600; i++)
        {
            var started = Started(i * 2 - 1, "a" + i, "r" + i) with { Dimensions = Dimensions() };
            events.Add(started);
            events.Add(Finished(i * 2, started) with { ObservedAt = Start.AddSeconds(i), Measurements = Measurements(i, i * 1000), CompletionState = "success" });
        }
        var store = new SqliteUsageStore(Database);
        foreach (var chunk in events.Chunk(1000)) await store.IngestAsync(new("source-a", chunk, chunk[^1].Sequence, events.Count));
        var query = Query() with { Limit = 1, Offset = 321, GroupBy = "modelFamily" };
        Assert.Single((await store.AttemptsAsync(query)).Items);
        var result = await store.AnalysisAsync(query);
        var metric = result.Performance.FirstOutputLatencyMs;
        Assert.Equal(600, metric.Samples);
        Assert.Equal(600, metric.Eligible);
        Assert.Equal(300.5, metric.P50!.Value, 10);
        Assert.Equal(570.05, metric.P95!.Value, 9);
        Assert.Equal(300.5, metric.Mean!.Value, 9);
        Assert.Equal(1d, metric.Minimum);
        Assert.Equal(600d, metric.Maximum);
        Assert.Equal(570050d, result.Performance.TotalDurationMs.P95!.Value, 7);
        Assert.Equal(600, result.Performance.OutputTokensPerSecond.Samples);
        Assert.Equal(4d / 600d, result.Performance.OutputTokensPerSecond.Minimum!.Value, 10);
        Assert.Equal(4d, result.Performance.OutputTokensPerSecond.Maximum);
        Assert.Equal(metric, Assert.Single(result.Dimensions[0].Groups).Performance.FirstOutputLatencyMs);
    }

    [Fact]
    public async Task MissingTokensAndZeroDurationRemainOutsideMeasuredRateSample()
    {
        var a = Started(1); var b = Started(3, "a2", "r2"); var c = Started(5, "a3", "r3");
        var store = new SqliteUsageStore(Database);
        await store.IngestAsync(Batch(a, Finished(2, a) with { Measurements = Measurements() },
            b, Finished(4, b) with { Measurements = Measurements(0, 0) },
            c, Finished(6, c, known: false) with { Measurements = Measurements() }));
        var result = await store.AnalysisAsync(Query());
        Assert.Equal(3, result.Performance.TotalDurationMs.Samples);
        Assert.Equal(1, result.Performance.OutputTokensPerSecond.Samples);
        Assert.Equal(3, result.Performance.OutputTokensPerSecond.Eligible);
        Assert.Equal(2d, result.Performance.OutputTokensPerSecond.Mean);
        Assert.Null(result.Totals.Tokens.Output);
        Assert.Equal(1, result.Totals.UnknownUsageAttempts);
    }

    [Fact]
    public async Task UnknownGroupSurvivesTruncationAndInvalidGroupQueriesAreRejected()
    {
        var a = Started(1); var b = Started(3, "a2", "r2") with { Dimensions = Dimensions() };
        var store = new SqliteUsageStore(Database);
        await store.IngestAsync(Batch(a, Finished(2, a), b, Finished(4, b)));
        var result = await store.AnalysisAsync(Query() with { GroupBy = "modelFamily", GroupLimit = 1 });
        Assert.True(result.Dimensions[0].Truncated);
        Assert.Null(Assert.Single(result.Dimensions[0].Groups).Key);
        Assert.False(result.Complete);
        Assert.Equal(2, result.Totals.Attempts);
        await Assert.ThrowsAsync<ArgumentException>(() => store.AnalysisAsync(Query() with { GroupBy = "accountId,model,routeId,provider" }));
        await Assert.ThrowsAsync<ArgumentException>(() => store.AnalysisAsync(Query() with { GroupBy = "model,model" }));
        await Assert.ThrowsAsync<ArgumentException>(() => store.AnalysisAsync(Query() with { GroupBy = "model); DROP TABLE attempts;--" }));
        await Assert.ThrowsAsync<ArgumentException>(() => store.AnalysisAsync(Query() with { GroupLimit = 501 }));
    }
}
