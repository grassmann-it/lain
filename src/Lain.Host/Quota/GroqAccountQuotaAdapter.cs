using Lain.Core;

namespace Lain.Host.Quota;

/// <summary>Combines ordinary-response limits with explicitly enabled independent Enterprise metrics.</summary>
public sealed class GroqAccountQuotaAdapter(IQuotaAdapter passive, GroqEnterpriseUsageReader enterprise) : IQuotaAdapter
{
    public string Source => passive.Source;
    public bool AllowsMissingCredential => true;
    public bool Supports(string provider) => passive.Supports(provider);

    public async Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default)
    {
        // No monitor opt-in: retain the existing purely local read, with no provider request.
        if (credential.Monitor is null) return await passive.FetchAsync(account, credential, ct);
        QuotaFetchResult? observed = null;
        string? passiveError = null;
        try { observed = await passive.FetchAsync(account, credential, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        { passiveError = "Saved Groq rate-limit observations could not be read; previous observations retained."; }
        try
        {
            var metrics = await enterprise.FetchAsync(credential, ct);
            var rows = (observed?.Readings ?? []).Concat(metrics.Readings).ToArray();
            var diagnostic = metrics.Error ?? passiveError ?? observed?.Error;
            // Explicit absence must retain older independent samples as stale even if
            // passive limits succeeded. QuotaService merges omitted rows on an error.
            if (metrics.Readings.Count == 0)
                diagnostic ??= "Groq Enterprise metrics are unavailable; previous observations retained. Check project and Enterprise access.";
            var support = metrics.Readings.Count > 0 ? metrics.Support : rows.Length > 0 ? observed?.Support : metrics.Support;
            return new(rows, diagnostic, support);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            var diagnostic = error is QuotaFetchException ? error.Message : "Groq Enterprise metrics read failed; check project and Enterprise access.";
            return new(observed?.Readings ?? [], diagnostic, observed?.Readings.Count > 0 ? observed.Support
                : new("unavailable", "Enterprise metrics could not be read. Free/Developer accounts have no qualified independent quota API; use the Groq console.", GroqQuotaAdapter.ConsoleUrl));
        }
    }
}
