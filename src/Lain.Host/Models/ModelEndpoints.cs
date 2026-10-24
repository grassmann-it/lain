using Lain.Core;
using Lain.Host.Operations;
using Lain.Host.Providers;

namespace Lain.Host.Models;

public record CodexExportRequest(long Revision, string ExecutablePath, string CatalogPath);

public static class ModelEndpoints
{
    /// <summary>Map behind the host's loopback authorization boundary.</summary>
    public static void MapModelEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(Api.Prefix + "/routes", (ModelsService models) => models.Snapshot.Routes);
        app.MapGet(Api.Prefix + "/profiles", (ModelsService models) => models.Snapshot.Profiles);
        app.MapGet(Api.Prefix + "/models", (ModelsService models) => models.ConfiguredModels());
        app.MapPut(Api.Prefix + "/routes/{id}", (string id, RevisionRequest<RouteDto> request, ModelsService models) =>
            Execute(() => request.Value is not null && id == request.Value.Id ? Results.Ok(models.PutRoute(request.Revision, request.Value)) : Results.BadRequest(new ApiError("invalid_route", "Path and route identity differ."))));
        app.MapPut(Api.Prefix + "/profiles/{id}", (string id, RevisionRequest<ProfileDto> request, ModelsService models) =>
            Execute(() => request.Value is not null && id == request.Value.Id ? Results.Ok(models.PutProfile(request.Revision, request.Value)) : Results.BadRequest(new ApiError("invalid_profile", "Path and profile identity differ."))));
        app.MapDelete(Api.Prefix + "/routes/{id}", (string id, long revision, ModelsService models) => Execute(() => Results.Ok(models.DeleteRoute(revision, id))));
        app.MapDelete(Api.Prefix + "/profiles/{id}", (string id, long revision, ModelsService models) => Execute(() => Results.Ok(models.DeleteProfile(revision, id))));
        app.MapGet(Api.Prefix + "/profiles/{id}/export", (string id, ModelsService models) => Execute(() => Results.Ok(models.Export(id))));
        app.MapPost(Api.Prefix + "/profiles/{id}/codex/export", (string id, CodexExportRequest request, ModelsService models, ProxyConfigurationWriter compiler, ProviderCatalogRegistry registry) =>
            Execute(() =>
            {
                var resolved = models.ResolveForApply(id, request.Revision);
                var description = compiler.Describe(resolved.Configuration with { Routes = resolved.Routes });
                var compatibility = new ModelCompatibilityCatalog(registry, description.QualifiedProviderRouteIds, description.TransportBindings);
                return Results.Ok(new CodexProfileAdapter(models, compatibility).Generate(id, request.Revision, request.ExecutablePath, request.CatalogPath));
            }));
    }

    private static IResult Execute(Func<IResult> action)
    {
        try { return action(); }
        catch (Operations.RevisionConflictException) { return Results.Conflict(new ApiError("revision_conflict", "Configuration changed; reload and retry.")); }
        catch (KeyNotFoundException) { return Results.NotFound(new ApiError("not_found", "Route or profile does not exist.")); }
        catch (InvalidDataException) { return Results.BadRequest(new ApiError("invalid_transport", "The selected inference transport is invalid; reconnect the affected account.")); }
        catch (IOException) { return Results.BadRequest(new ApiError("missing_credential", "The selected inference credential cannot be read; reconnect the affected account.")); }
        catch (InvalidOperationException) { return Results.BadRequest(new ApiError("invalid_transport", "The selected inference transport is unavailable or ambiguous.")); }
        catch (ArgumentException ex) { return Results.BadRequest(new ApiError("validation", ex.Message)); }
    }
}
