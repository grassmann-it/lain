using Lain.Core;
using Lain.Host.Operations;
using Lain.Host.Quota;

namespace Lain.Host.Providers.Xai;

public static class XaiKeyMetadataEndpoints
{
    /// <summary>Uses the host's loopback/auth middleware and owns its short publication gate.</summary>
    public static RouteHandlerBuilder MapXaiKeyMetadata(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost(Api.Prefix + "/accounts/{id}/metadata/xai", async Task<IResult> (string id,
            RevisionRequest<AccountMetadataDiscoveryRequest> request, XaiKeyMetadataService service, CancellationToken ct) =>
        {
            try
            {
                if (request.Value is null || request.Revision < 0 || request.Value.ExpectedGeneration < 0)
                    return Results.BadRequest(new ApiError("validation", "A configuration revision and account generation are required."));
                return Results.Ok(await service.ReadAndObserveAsync(id, request.Revision, request.Value.ExpectedGeneration, ct));
            }
            catch (RevisionConflictException)
            { return Results.Conflict(new ApiError("revision_conflict", "Configuration changed; reload before discovering the team again.")); }
            catch (KeyNotFoundException)
            { return Results.NotFound(new ApiError("not_found", "The account is no longer configured.")); }
            catch (ArgumentException)
            { return Results.Conflict(new ApiError("identity_mismatch", "The returned team does not match this account. Its binding was preserved.")); }
            catch (InvalidOperationException)
            { return Results.Conflict(new ApiError("metadata_unavailable", "No current xAI team identity could be confirmed. Check the saved key, account binding and configuration; no budget was read.")); }
            catch (Exception error) when (error is QuotaFetchException or IOException or System.Text.Json.JsonException)
            { return Results.Json(new ApiError("metadata_unavailable", "xAI team identity could not be read. The existing binding was preserved; no budget was read."), statusCode: StatusCodes.Status502BadGateway); }
        }).WithMetadata(new OwnsPublicationGate());
}
