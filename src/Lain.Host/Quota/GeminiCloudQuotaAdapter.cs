using Lain.Core;
using Lain.Host.Accounts.Cloud;

namespace Lain.Host.Quota;

/// <summary>Model quota telemetry for browser grants and snapshots; generic usage remains a fallback.</summary>
public sealed class GeminiCloudQuotaAdapter : IQuotaAdapter, IDisposable
{
    private readonly GeminiMonitoringQuotaAdapter legacy;
    private readonly GeminiModelQuotaAdapter models;
    public GeminiCloudQuotaAdapter(HttpClient legacyHttp, HttpMessageHandler? modelHandler = null, TimeProvider? clock = null)
    { legacy = new(legacyHttp, clock); models = new(modelHandler, clock); }
    public string Source => "gemini:project-monitoring";
    public bool Supports(string provider) => provider.Equals(CloudResourceDiscovery.GoogleProvider, StringComparison.OrdinalIgnoreCase);
    public async Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default)
    {
        // API keys alone cannot authorize Google Monitoring. Keep the existing actionable setup state.
        if (credential.Monitor is null) return await legacy.FetchAsync(account, credential, ct);
        var result = await models.FetchAsync(account, credential, ct);
        if (result.Readings.Any(r => r.MeasurementKind == "usage") || result.Support?.Kind == "setup-required") return result;
        // Some projects expose generic API traffic but no model metric series. Preserve that useful read,
        // without describing request counts as remaining Gemini quota or losing separately returned limits.
        try
        {
            var generic = await legacy.FetchAsync(account, credential, ct);
            var errors = new[] { result.Error, generic.Error }.Where(e => !string.IsNullOrWhiteSpace(e)).ToArray();
            return result with
            {
                Readings = result.Readings.Concat(generic.Readings).ToArray(),
                Error = errors.Length == 0 ? null : string.Join(' ', errors),
                Support = result.Readings.Count == 0 ? generic.Support : result.Support
            };
        }
        catch (QuotaFetchException) when (result.Readings.Count > 0)
        { return result with { Error = "Model limits were read; generic API usage is unavailable." }; }
    }
    public void Dispose() => models.Dispose();
}
