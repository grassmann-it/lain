using Lain.Core;
using Lain.Host.Models;
using Lain.Host.Operations;
using Lain.Host.Providers;

namespace Lain.Host.Workers;

public static class WorkerEndpoints
{
    /// <summary>Map on the host behind its existing management-token middleware. No separate listener or auth bypass.</summary>
    public static void MapWorkerEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(Api.Prefix + "/workers/plan", async Task<IResult> (HttpRequest http, OwnStatePaths paths, ModelsService models,
            ProxyConfigurationWriter compiler, ProviderCostPolicy policy, ProviderCatalogRegistry registry, CancellationToken ct) =>
        {
            try
            {
                if (!http.HasJsonContentType()) return Results.Json(new ApiError("invalid_request", "Worker planning requires application/json."), statusCode: 415);
                if (http.ContentLength > WorkerPlanValidation.MaximumRequestBytes)
                    return Results.Json(new ApiError("request_too_large", "Worker plan request exceeds the size limit."), statusCode: 413);
                using var body = new MemoryStream();
                var buffer = new byte[16 * 1024];
                int read;
                while ((read = await http.Body.ReadAsync(buffer, ct)) > 0)
                {
                    if (body.Length + read > WorkerPlanValidation.MaximumRequestBytes)
                        return Results.Json(new ApiError("request_too_large", "Worker plan request exceeds the size limit."), statusCode: 413);
                    body.Write(buffer, 0, read);
                }
                var request = WorkerPlanValidation.Parse(body.ToArray());
                return Results.Ok(new WorkerPlanningService(paths, models, compiler, policy, registry).Plan(request));
            }
            catch (WorkerPlanException error) { return Results.Json(error.ToApiError(), statusCode: error.StatusCode); }
            catch (BadHttpRequestException) { return Results.BadRequest(new ApiError("invalid_request", "The worker request body could not be read.")); }
            catch (IOException) { return Results.BadRequest(new ApiError("invalid_request", "The worker request body could not be read.")); }
        });
    }
}
