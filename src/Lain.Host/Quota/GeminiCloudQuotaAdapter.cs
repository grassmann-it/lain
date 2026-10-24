using Lain.Core;
using Lain.Host.Accounts.Cloud;

namespace Lain.Host.Quota;

/// <summary>Preserves legacy snapshot reporting; only discovered project-bound grants select the model reader.</summary>
public sealed class GeminiCloudQuotaAdapter : IQuotaAdapter, IDisposable
{
    private readonly GeminiMonitoringQuotaAdapter legacy;
    private readonly GeminiModelQuotaAdapter models;
    public GeminiCloudQuotaAdapter(HttpClient legacyHttp, HttpMessageHandler? modelHandler = null, TimeProvider? clock = null)
    { legacy = new(legacyHttp, clock); models = new(modelHandler, clock); }
    public string Source => "gemini:project-monitoring";
    public bool Supports(string provider) => provider.Equals(CloudResourceDiscovery.GoogleProvider, StringComparison.OrdinalIgnoreCase);
    public Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default)
    {
        var binding = account.Binding;
        var discovered = binding is { Provider: CloudResourceDiscovery.GoogleProvider, ResourceType: "project", Provenance: "provider-discovery" }
            && CloudResourceDiscovery.ProjectId(binding.ResourceId) && CloudResourceDiscovery.ProjectNumber(binding.Subject)
            && credential.Settings.GetValueOrDefault("projectId") == binding.ResourceId
            && credential.Settings.GetValueOrDefault("projectNumber") == binding.Subject;
        return discovered ? models.FetchAsync(account, credential, ct) : legacy.FetchAsync(account, credential, ct);
    }
    public void Dispose() => models.Dispose();
}
