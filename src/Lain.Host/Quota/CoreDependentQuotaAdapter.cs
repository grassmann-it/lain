using Lain.Core;

namespace Lain.Host.Quota;

/// <summary>A passive Core reader must not turn an intentionally absent Core into a provider error.</summary>
public sealed class CoreDependentQuotaAdapter(IQuotaAdapter inner, Func<bool> available) : IQuotaAdapter
{
    public bool Supports(string provider) => inner.Supports(provider);
    public string Source => inner.Source;
    public bool RequiresCredential => inner.RequiresCredential;
    public string CredentialPurpose => inner.CredentialPurpose;
    public Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default) =>
        available() ? inner.FetchAsync(account, credential, ct) : Task.FromResult(new QuotaFetchResult([], Support:
            new("awaiting-observation", "This passive source needs a running Gateway and an ordinary provider request. No inference is sent by refresh.",
                GroqQuotaAdapter.ConsoleUrl)));
}
