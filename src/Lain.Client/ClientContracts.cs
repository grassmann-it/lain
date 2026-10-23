using Lain.Core;

namespace Lain.Client;

public sealed record AddApiAccountRequest(string Name, string Provider, string ApiKey, string BaseUrl);
public sealed record LoginRequest(long Revision, string Provider);
public sealed record QuotaRefreshRequest(string? AccountId = null);
public sealed record TokenResponse(string Token);
public sealed record ModelCatalogDto(int ContractVersion, long Revision, ModelEntryDto[] Models);
public sealed record ModelEntryDto(string Id, string Provider, string[] AccountIds, string Source = "configured");
public sealed record AccountPlaceholderDto(string Id, string Provider);
public sealed record ProfileExportDto(int ContractVersion, long Revision, ProfileDto Profile, RouteDto[] Routes, AccountPlaceholderDto[] Accounts);
