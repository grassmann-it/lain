using Lain.Core;

namespace Lain.Client;

public sealed partial class LainApiClient
{
    public Task<ModelCollectionDto[]> CollectionsAsync(CancellationToken ct = default) => Get<ModelCollectionDto[]>("collections", ct);
    public Task<ModelCollectionGroupsDto> ModelGroupsAsync(CancellationToken ct = default) => Get<ModelCollectionGroupsDto>("model-groups", ct);
    public Task<ConfigurationDto> SaveCollectionAsync(long revision, ModelCollectionDto collection, CancellationToken ct = default) =>
        Send<ConfigurationDto>(HttpMethod.Put, $"collections/{Id(collection.Id)}", new RevisionRequest<ModelCollectionDto>(revision, collection), ct);
    public Task<ConfigurationDto> RemoveCollectionAsync(long revision, string id, CancellationToken ct = default) =>
        Send<ConfigurationDto>(HttpMethod.Delete, $"collections/{Id(id)}?revision={revision}", null, ct);
}
