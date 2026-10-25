using Lain.Core;

namespace Lain.Client;

public sealed partial class LainApiClient
{
    public Task<CollectionRouterDto[]> CollectionRoutersAsync(CancellationToken ct = default) => Get<CollectionRouterDto[]>("collection-routers", ct);
    public Task<ConfigurationDto> SaveCollectionRouterAsync(long revision, CollectionRouterDto router, CancellationToken ct = default) =>
        Send<ConfigurationDto>(HttpMethod.Put, $"collection-routers/{Id(router.Id)}", new RevisionRequest<CollectionRouterDto>(revision, router), ct);
    public Task<ConfigurationDto> RemoveCollectionRouterAsync(long revision, string id, CancellationToken ct = default) =>
        Send<ConfigurationDto>(HttpMethod.Delete, $"collection-routers/{Id(id)}?revision={revision}", null, ct);
}
