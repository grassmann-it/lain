using Lain.Core;

namespace Lain.Client;

public sealed record AddApiAccountRequest(string Name, string Provider, string ApiKey, string BaseUrl,
    string? ProviderRouteId = null, Dictionary<string, string>? Settings = null,
    string? ProviderConnectionId = null, string[]? SelectedModelRouteIds = null)
{
    public override string ToString() => "AddApiAccountRequest [redacted]";
}
public sealed record LoginRequest(long Revision, string Provider, string? Method = null,
    string[]? Purposes = null, string? AccountId = null, ResourceBindingDto? Binding = null,
    long? ExpectedGeneration = null, string? Name = null, string? IdempotencyKey = null);
public sealed record QuotaRefreshRequest(string? AccountId = null);
public sealed record TokenResponse(string Token);
public sealed record ModelCatalogDto(int ContractVersion, long Revision, ModelEntryDto[] Models);
public sealed record ModelEntryDto(string Id, string Provider, string[] AccountIds, string Source = "configured");
public sealed record AccountPlaceholderDto(string Id, string Provider);
public sealed record ProfileExportDto(int ContractVersion, long Revision, ProfileDto Profile, RouteDto[] Routes, AccountPlaceholderDto[] Accounts);
public sealed record ProfileApplyRequest(long Revision);
public sealed record CodexExportRequest(long Revision, string ExecutablePath, string CatalogPath, bool EnableLainModelPicker = false);
public sealed record CodexProfileArtifacts(string ConfigFileName, string Configuration, string CatalogFileName, string Catalog, string[] LaunchArguments, long Revision,
    ModelCompatibilityManifestDto? Compatibility = null, string? ProfileId = null, string? GenerationId = null);
