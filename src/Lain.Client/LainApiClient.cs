using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Lain.Core;

namespace Lain.Client;

/// <summary>The common typed application API used by the desktop and CLI.</summary>
public sealed class LainApiClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient http;
    private readonly string? bearerToken;
    private readonly bool ownsHttp;
    private Func<string>? tokenProvider;

    public LainApiClient(HttpClient http, string? bearerToken = null) : this(http, bearerToken, false) { }
    private LainApiClient(HttpClient http, string? bearerToken, bool ownsHttp)
    {
        this.http = http;
        this.bearerToken = bearerToken;
        this.ownsHttp = ownsHttp;
        if (http.BaseAddress is null) http.BaseAddress = LocalDiscovery.ApiAddress;
    }

    public static LainApiClient CreateLocal() => new(new HttpClient { BaseAddress = LocalDiscovery.ApiAddress,
        Timeout = TimeSpan.FromSeconds(30) }, null, true) { tokenProvider = LocalDiscovery.ReadHostToken };

    public Task<StatusDto> StatusAsync(CancellationToken ct = default) => Get<StatusDto>("status", ct);
    public Task<ConfigurationDto> GetConfigurationAsync(CancellationToken ct = default) => Get<ConfigurationDto>("configuration", ct);
    public Task<ConfigurationDto> SaveConfigurationAsync(long revision, ConfigurationDto value, CancellationToken ct = default) =>
        Send<ConfigurationDto>(HttpMethod.Put, "configuration", new RevisionRequest<ConfigurationDto>(revision, value), ct);
    public Task<AccountDto[]> AccountsAsync(CancellationToken ct = default) => Get<AccountDto[]>("accounts", ct);
    public Task<ConfigurationDto> AddApiAccountAsync(long revision, AddApiAccountRequest value, CancellationToken ct = default) =>
        Send<ConfigurationDto>(HttpMethod.Post, "accounts", new RevisionRequest<AddApiAccountRequest>(revision, value), ct);
    public Task<ConfigurationDto> SetAccountEnabledAsync(long revision, string id, bool enabled, CancellationToken ct = default) =>
        Send<ConfigurationDto>(HttpMethod.Put, $"accounts/{Id(id)}/enabled", new RevisionRequest<bool>(revision, enabled), ct);
    public Task<ConfigurationDto> RemoveAccountAsync(long revision, string id, CancellationToken ct = default) =>
        Send<ConfigurationDto>(HttpMethod.Delete, $"accounts/{Id(id)}?revision={revision}", null, ct);
    public Task<AccountDto> CheckAccountAsync(string id, CancellationToken ct = default) =>
        Send<AccountDto>(HttpMethod.Post, $"accounts/{Id(id)}/check", null, ct);
    public Task<LoginDto> LoginAsync(long revision, string provider, CancellationToken ct = default) =>
        Send<LoginDto>(HttpMethod.Post, "accounts/login", new LoginRequest(revision, provider), ct);
    public Task<LoginDto> GetLoginAsync(string id, CancellationToken ct = default) => Get<LoginDto>($"accounts/login/{Id(id)}", ct);
    public Task<LoginDto> CancelLoginAsync(string id, CancellationToken ct = default) =>
        Send<LoginDto>(HttpMethod.Delete, $"accounts/login/{Id(id)}", null, ct);
    public Task<ImportResult> ImportAsync(ImportRequest request, CancellationToken ct = default) => Send<ImportResult>(HttpMethod.Post, "import", request, ct);
    public Task<ModelCatalogDto> ModelsAsync(CancellationToken ct = default) => Get<ModelCatalogDto>("models", ct);
    public Task<RouteDto[]> RoutesAsync(CancellationToken ct = default) => Get<RouteDto[]>("routes", ct);
    public Task<ProfileDto[]> ProfilesAsync(CancellationToken ct = default) => Get<ProfileDto[]>("profiles", ct);
    public Task<ConfigurationDto> SaveRouteAsync(long revision, RouteDto route, CancellationToken ct = default) =>
        Send<ConfigurationDto>(HttpMethod.Put, $"routes/{Id(route.Id)}", new RevisionRequest<RouteDto>(revision, route), ct);
    public Task<ConfigurationDto> SaveProfileAsync(long revision, ProfileDto profile, CancellationToken ct = default) =>
        Send<ConfigurationDto>(HttpMethod.Put, $"profiles/{Id(profile.Id)}", new RevisionRequest<ProfileDto>(revision, profile), ct);
    public Task<ConfigurationDto> RemoveRouteAsync(long revision, string id, CancellationToken ct = default) =>
        Send<ConfigurationDto>(HttpMethod.Delete, $"routes/{Id(id)}?revision={revision}", null, ct);
    public Task<ConfigurationDto> RemoveProfileAsync(long revision, string id, CancellationToken ct = default) =>
        Send<ConfigurationDto>(HttpMethod.Delete, $"profiles/{Id(id)}?revision={revision}", null, ct);
    public Task<ProfileExportDto> ExportProfileAsync(string id, CancellationToken ct = default) => Get<ProfileExportDto>($"profiles/{Id(id)}/export", ct);
    public Task<UsageSummaryDto> UsageAsync(UsageQuery query, CancellationToken ct = default) => Get<UsageSummaryDto>("usage" + QueryString(query), ct);
    public Task<PageDto<AttemptDto>> AttemptsAsync(UsageQuery query, CancellationToken ct = default) => Get<PageDto<AttemptDto>>("attempts" + QueryString(query), ct);
    public Task<QuotaDto[]> QuotasAsync(CancellationToken ct = default) => Get<QuotaDto[]>("quotas", ct);
    public Task<QuotaDto[]> RefreshQuotasAsync(string? accountId = null, CancellationToken ct = default) =>
        Send<QuotaDto[]>(HttpMethod.Post, "quotas/refresh", new QuotaRefreshRequest(accountId), ct);
    public Task<StatusDto> ServiceAsync(string action, CancellationToken ct = default)
    {
        if (action is not ("start" or "stop" or "restart")) throw new ArgumentException("Unknown service action.", nameof(action));
        return Send<StatusDto>(HttpMethod.Post, $"service/{action}", null, ct);
    }
    public Task<TokenResponse> InferenceTokenAsync(string audience = "codex", CancellationToken ct = default) =>
        Get<TokenResponse>($"auth/token?audience={Id(audience)}", ct);

    private Task<T> Get<T>(string path, CancellationToken ct) => Send<T>(HttpMethod.Get, path, null, ct);
    private async Task<T> Send<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, Api.Prefix + "/" + path);
        var token = tokenProvider?.Invoke() ?? bearerToken;
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            ApiError? error = null;
            try { error = await response.Content.ReadFromJsonAsync<ApiError>(Json, ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is JsonException or NotSupportedException) { }
            throw new LainApiException(response.StatusCode, error?.Code ?? "http_error",
                error?.Message ?? $"lain API returned HTTP {(int)response.StatusCode}.");
        }
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false)
                ?? throw new LainApiException(response.StatusCode, "invalid_response", "lain API returned an empty response.");
        }
        catch (JsonException) { throw new LainApiException(response.StatusCode, "invalid_response", "lain API returned an invalid response."); }
    }

    private static string Id(string value) => Uri.EscapeDataString(value);
    public static string QueryString(UsageQuery query)
    {
        var values = new List<KeyValuePair<string, string>>();
        void Add(string key, string? value) { if (value is not null) values.Add(new(key, value)); }
        Add("from", query.From?.ToString("O", CultureInfo.InvariantCulture));
        Add("to", query.To?.ToString("O", CultureInfo.InvariantCulture));
        Add("timeZone", query.TimeZone); Add("accountId", query.AccountId); Add("model", query.Model);
        Add("statusCode", query.StatusCode?.ToString(CultureInfo.InvariantCulture));
        Add("offset", query.Offset.ToString(CultureInfo.InvariantCulture)); Add("limit", query.Limit.ToString(CultureInfo.InvariantCulture));
        return "?" + string.Join("&", values.Select(v => $"{Id(v.Key)}={Id(v.Value)}"));
    }

    public void Dispose() { if (ownsHttp) http.Dispose(); }
}
