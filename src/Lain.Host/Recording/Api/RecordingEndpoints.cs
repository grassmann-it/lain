using Lain.Core;
using System.Text;
using System.Text.Json;
using Lain.Host.Recording.Collection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lain.Host.Recording.Api;

public static class RecordingEndpoints
{
    public const string Prefix = Lain.Core.Api.Prefix + "/recording";
    /// <summary>Policy/store/projection are registered by their owning packages. Host token middleware protects all routes.</summary>
    public static IServiceCollection AddRecordingCollection(this IServiceCollection services, string directory,
        Uri coreAddress, string managementToken, Func<bool>? shouldCollect = null)
    {
        services.AddSingleton(new RecordingSourceClient(coreAddress, managementToken));
        services.AddSingleton(new RecordingRunLabels(directory));
        services.AddSingleton(provider => new RecordingCollector(provider.GetRequiredService<RecordingSourceClient>(),
            provider.GetRequiredService<IRecordingPolicy>(), provider.GetRequiredService<IRecordingStore>(),
            provider.GetRequiredService<IRecordingProjection>(), provider.GetRequiredService<ILogger<RecordingCollector>>(), shouldCollect));
        services.AddHostedService(provider => provider.GetRequiredService<RecordingCollector>());
        return services;
    }
    public static IEndpointRouteBuilder MapRecordingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Prefix + "/status", async (IRecordingPolicy policy, IRecordingStore store, RecordingCollector collector, CancellationToken ct) =>
        {
            RecordingStorageHealthDto storage;
            try { storage = await store.HealthAsync(ct); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { storage = new(State: "unavailable", ErrorCode: "storage_unavailable"); }
            var snapshot = policy.Snapshot;
            return new RecordingHealthDto(RecordingContract.Version, snapshot, snapshot.Enabled ? collector.State : "off",
                DroppedRecords: collector.DroppedRecords, UnknownLoss: collector.UnknownLoss, Storage: storage,
                Coverage: ["owned_gateway", "retained_archive", "complete_json", "stream_fragments_omitted"], ErrorCode: collector.LastErrorCode ?? storage.ErrorCode);
        });
        endpoints.MapGet(Prefix + "/policy", (IRecordingPolicy policy) => policy.Snapshot);
        endpoints.MapPut(Prefix + "/policy", (RecordingPolicyUpdate update, IRecordingPolicy policy) =>
        {
            var result = policy.Update(update);
            return result.Applied ? Results.Ok(result.Policy) : Failure(result.ErrorCode ?? "policy_rejected");
        });
        endpoints.MapGet(Prefix + "/conversations", async (HttpRequest request, IRecordingPolicy policy, IRecordingProjection projection, CancellationToken ct) =>
        {
            RecordingArguments.QueryKeys(request, "limit", "cursor", "model", "routeId", "workerId");
            var result = await projection.ListAsync(new(RecordingArguments.Limit(request, policy.Snapshot.EffectiveLimits.MaxPageSize),
                RecordingArguments.Value(request, "cursor", 4096), RecordingArguments.Value(request, "model"),
                RecordingArguments.Value(request, "routeId"), RecordingArguments.Value(request, "workerId")), ct);
            return result.ErrorCode is null ? Results.Ok(result) : Failure(result.ErrorCode);
        });
        endpoints.MapGet(Prefix + "/conversations/{id}", async (string id, HttpRequest request, IRecordingPolicy policy,
            IRecordingProjection projection, RecordingRunLabels labels, CancellationToken ct) =>
        {
            RecordingArguments.Identity(id); RecordingArguments.QueryKeys(request, "limit", "cursor");
            var result = await projection.GetAsync(id, RecordingArguments.Value(request, "cursor", 4096),
                RecordingArguments.Limit(request, policy.Snapshot.EffectiveLimits.MaxPageSize), ct);
            return result is null ? (IResult)Results.NotFound(new ApiError("not_found", "The saved conversation does not exist.")) : Results.Ok(BoundedDetail(result with { Runs = labels.List(id) }));
        });
        // Bodies appear only on this explicit bounded selected-record read; conversation lists contain summaries.
        endpoints.MapGet(Prefix + "/events", async (HttpRequest request, IRecordingStore store, IRecordingPolicy policy, CancellationToken ct) =>
        {
            RecordingArguments.QueryKeys(request, "after", "limit");
            return BoundedEvents(await store.ReadCommittedAsync(RecordingArguments.After(request), RecordingArguments.Limit(request, policy.Snapshot.EffectiveLimits.MaxPageSize), ct));
        });
        endpoints.MapGet(Prefix + "/conversations/{id}/runs", async (string id, IRecordingProjection projection, RecordingRunLabels labels, CancellationToken ct) =>
        {
            RecordingArguments.Identity(id);
            return await projection.GetAsync(id, limit: 1, ct: ct) is null ? (IResult)Results.NotFound(new ApiError("not_found", "The saved conversation does not exist.")) : Results.Ok(labels.List(id));
        });
        endpoints.MapPut(Prefix + "/conversations/{id}/runs", async (string id, RecordingRunLabelUpdate update,
            IRecordingPolicy policy, IRecordingProjection projection, RecordingRunLabels labels, CancellationToken ct) =>
        {
            RecordingArguments.Identity(id);
            if (update.ExpectedRevision != policy.Snapshot.Revision) return Failure("stale_revision");
            if (await projection.GetAsync(id, limit: 1, ct: ct) is null) return Results.NotFound(new ApiError("not_found", "The saved conversation does not exist."));
            return Results.Ok(labels.Set(id, update.Manifest));
        });
        endpoints.MapDelete(Prefix + "/archive", async (long expectedRevision, IRecordingPolicy policy, IRecordingStore store,
            IRecordingProjection projection, RecordingRunLabels labels, CancellationToken ct) =>
        {
            var snapshot = policy.Snapshot;
            if (expectedRevision != snapshot.Revision) return Failure("stale_revision");
            if (snapshot.Enabled) return Failure("recording_must_be_off");
            await store.ClearAsync(ct); labels.Clear(); await projection.RebuildAsync(ct);
            return Results.Ok(new RecordingClearResult(true));
        });
        return endpoints;
    }
    public const int MaximumContentResponseBytes = 4 * 1024 * 1024;
    internal static RecordingConversationDetail BoundedDetail(RecordingConversationDetail detail)
    {
        long bytes = 0;
        var records = detail.Records.Select(record =>
        {
            bytes += JsonSerializer.SerializeToUtf8Bytes(record).Length;
            return bytes <= MaximumContentResponseBytes ? record : record with
            { Messages = [], Complete = false, GapReason = "api_body_limit", Sources = record.Sources.Take(100).ToArray() };
        }).ToArray();
        return detail with { Records = records, GapReason = bytes > MaximumContentResponseBytes ? "api_body_limit" : detail.GapReason };
    }
    internal static RecordingCommitPage BoundedEvents(RecordingCommitPage page)
    {
        long bytes = 0;
        var records = page.Records.Select(record =>
        {
            bytes += record.Observation.Body is { } body ? Encoding.UTF8.GetByteCount(body) : 0;
            return bytes <= MaximumContentResponseBytes ? record : record with
            { Observation = record.Observation with { Body = null, BodyEncoding = null, Complete = false, GapReason = "api_body_limit" } };
        }).ToArray();
        return page with { Records = records, GapReason = bytes > MaximumContentResponseBytes ? "api_body_limit" : page.GapReason };
    }
    private static IResult Failure(string code) => Results.Json(new ApiError(code, "Recording request rejected; refresh recording state and check the request."),
        statusCode: code is "stale_revision" or "cursor_stale" or "cursor_expired" or "recording_must_be_off" ? 409 : 400);
}
public sealed record RecordingRunLabelUpdate(long ExpectedRevision, RecordingRunManifest Manifest);
