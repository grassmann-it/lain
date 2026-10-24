using Lain.Core;

namespace Lain.Client;

public sealed partial class LainApiClient
{
    public Task<ConnectionMethodCatalogDto> ConnectionMethodsAsync(string? provider = null, CancellationToken ct = default) =>
        Get<ConnectionMethodCatalogDto>("connection-methods" + (provider is null ? "" : "?provider=" + Id(provider)), ct);
    public Task<ConfigurationDto> ConnectAccountAsync(long revision, ConnectCredentialRequest request, CancellationToken ct = default) =>
        Send<ConfigurationDto>(HttpMethod.Post, "accounts/connect", new RevisionRequest<ConnectCredentialRequest>(revision, request), ct);
    public Task<LoginDto> LoginAsync(LoginRequest request, CancellationToken ct = default) =>
        Send<LoginDto>(HttpMethod.Post, "accounts/login", request, ct);
    public Task<LoginDto> SelectLoginResourceAsync(string id, ResourceSelectionRequest request, CancellationToken ct = default) =>
        Send<LoginDto>(HttpMethod.Post, $"accounts/login/{Id(id)}/resource", request, ct);
    public Task<StatusDto> SetRuntimeModeAsync(RuntimeModeRequest request, CancellationToken ct = default) =>
        Send<StatusDto>(HttpMethod.Put, "runtime-mode", request, ct);
    public Task<ProfileApplyPreviewDto> PreviewProfileAsync(string id, long revision, CancellationToken ct = default) =>
        Get<ProfileApplyPreviewDto>($"profiles/{Id(id)}/preview?revision={revision}", ct);
}
