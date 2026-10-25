using System.Net;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Accounts.Managed;

namespace Lain.Host.Quota;

/// <summary>API-key compatibility plus real OAuth identity/usage reads. Never rotates, imports or exports refresh material.</summary>
public sealed class KimiDeviceQuotaAdapter : IQuotaAdapter, IDisposable
{
    private readonly KimiQuotaAdapter keys;
    private readonly KimiDeviceHttp http;
    private readonly TimeProvider clock;
    public KimiDeviceQuotaAdapter(HttpClient legacyClient, HttpMessageHandler? handler = null, TimeProvider? clock = null)
    { keys = new(legacyClient); http = new(handler); this.clock = clock ?? TimeProvider.System; }
    public string Source => KimiQuotaAdapter.Endpoint;
    public bool Supports(string provider) => keys.Supports(provider);

    public async Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default)
    {
        if ((credential.CredentialKind ?? account.CredentialKind) != KimiDeviceProtocol.Kind)
            return await keys.FetchAsync(account, credential, ct);
        var settings = credential.Settings;
        var region = settings.GetValueOrDefault("region");
        var device = settings.GetValueOrDefault("deviceId");
        var subject = settings.GetValueOrDefault("subject");
        var providerRegion = settings.GetValueOrDefault("providerRegion");
        if (region is not ("global" or "mainland-cn") || settings.GetValueOrDefault("clientId") != KimiDeviceProtocol.ClientId
            || string.IsNullOrEmpty(subject) || credential.ProviderAccountId != subject || string.IsNullOrEmpty(providerRegion) || device is null
            || !Guid.TryParseExact(device, "N", out _) || !KimiDeviceProtocol.Token(credential.BearerToken)
            || credential.BaseUrl != KimiDeviceProtocol.Endpoints(region).Api || credential.ExpiresAt is null || credential.ExpiresAt <= clock.GetUtcNow()
            || account.Binding is not { Product: KimiDeviceProtocol.Product, ResourceType: "user" } binding
            || AccountService.CanonicalProviderId(binding.Provider) != "kimi"
            || binding.Region != region || binding.Subject != subject || binding.ResourceId != subject)
            throw new QuotaFetchException("Kimi monitoring requires an unexpired, subject-bound grant on its original region and official host.");
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            // A fresh profile read validates the bearer scope before exposing numeric usage.
            var me = await http.SendAsync(region, "/me", device, credential.BearerToken, null, deadline.Token);
            if (me.Status != HttpStatusCode.OK) throw QuotaFetchException.Http(me.Status);
            var actual = KimiDeviceProtocol.Identity(me.Body, region);
            if (!KimiDeviceProtocol.Matches(binding, actual) || KimiDeviceProtocol.RequiredText(me.Body, "region", 80) != providerRegion)
                throw new QuotaFetchException("Kimi monitoring identity changed. No usage was accepted; reconnect this access.");
            var response = await http.SendAsync(region, "/usages", device, credential.BearerToken, null, deadline.Token);
            if (response.Status != HttpStatusCode.OK) throw QuotaFetchException.Http(response.Status);
            var observed = clock.GetUtcNow();
            var rows = KimiQuotaAdapter.Normalize(response.Body).Select(row => row with
            {
                Source = credential.BaseUrl + "/usages#" + row.Source.Split('#').Last(),
                WindowId = row.Source.Split('#').Last(), Scope = "kimi-code-user", ScopeId = subject,
                MeasurementKind = "reported-quota", BudgetPoolId = "kimi-code:" + region + ":" + subject,
                InheritAccountPool = false, ObservedAt = observed, SourceUrl = credential.BaseUrl + "/usages"
            }).ToArray();
            return new(rows, rows.Length == 0 ? "Kimi returned no recognized quota windows; remaining budget is unknown." : null,
                new("api", "Kimi Code quota read using this lain-owned device grant and its verified user/region. Percent windows are reported independently; no token allowance or wallet balance was invented."));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (QuotaFetchException) { throw; }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or JsonException or OperationCanceledException)
        { throw new QuotaFetchException("Kimi device monitoring could not be read. Existing measurements remain historical; no zero usage was inferred."); }
    }
    public void Dispose() => http.Dispose();
}
