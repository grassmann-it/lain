using System.Net;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts.ConsoleConnections.Nvidia;
using Lain.Host.Quota;

static void Check(bool value, string name) { if (!value) throw new Exception(name); }
var fixture = File.ReadAllText("tests/nvidia-quota/fixtures/public-limits.json");
using var raw = JsonDocument.Parse(fixture);
var limits = NvidiaBuildQuotaReader.ReadPublicLimits(raw.RootElement);
Check(limits is { RequestsPerMinute: "Up to 40 rpm", RequestsPerDay: "10,000 requests per day" }, "native reference wording preserved");
var account = new AccountDto("synthetic-account", "NVIDIA", "nvidia-build", true, "api-key", "ready");
var body = fixture;
var status = HttpStatusCode.OK;
var calls = 0;
using var adapter = new NvidiaBuildQuotaAdapter(new FixtureHttp(request =>
{
    calls++;
    Check(request.Method == HttpMethod.Get && request.RequestUri!.AbsoluteUri == NvidiaBuildQuotaReader.PublicSource, "fixed public GET");
    Check(request.Headers.Authorization is null && !request.Headers.Contains("Cookie"), "no inference or monitor secret sent");
    return new(status) { Content = new StringContent(body) };
}));
var credential = new QuotaCredential("synthetic-inference-secret", monitor: new("api-token", "synthetic-monitor-secret", "foreign"));
var result = await adapter.FetchAsync(account, credential);
Check(!adapter.RequiresCredential && result.Readings.Count == 0 && result.Error is null, "public reference is never account quota");
Check(result.Support is { Kind: "setup-required" } && result.Support.Message.Contains("Up to 40 rpm")
    && result.Support.Message.Contains("account-bound"), "reference and concrete prerequisite visible together");
Check(NvidiaBuildConnection.Method() is { Availability: "setup-required", SecretRequired: false }, "no unsupported login offered as available");
foreach (var bad in new[] { "{}", "{\"remainingCredits\":12345}", fixture.Replace("Up to 40 rpm", "<script>private</script>"), "not-json", new string('x', 32769) })
{
    body = bad;
    result = await adapter.FetchAsync(account, credential);
    Check(result.Readings.Count == 0 && result.Support?.Kind == "setup-required" && result.Error is null, "missing or changed public source retains precise prerequisite");
    Check(!result.Support.Message.Contains("private") && !result.Support.Message.Contains("12345"), "untrusted text and unrelated credits not projected");
}
foreach (var denied in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests, HttpStatusCode.Redirect })
{
    status = denied;
    body = "private provider error text";
    result = await adapter.FetchAsync(account, credential);
    Check(result.Readings.Count == 0 && result.Support is { Kind: "setup-required" } && !result.Support.Message.Contains("private"), "HTTP failures sanitized without fallback login");
}
var before = calls;
using var cancelled = new CancellationTokenSource();
cancelled.Cancel();
try { await adapter.FetchAsync(account, credential, cancelled.Token); throw new Exception("cancellation swallowed"); }
catch (OperationCanceledException) { }
Check(calls == before, "cancelled request never sent");
status = HttpStatusCode.OK;
body = fixture;
var service = new QuotaService(new FixtureAccounts(account), new NoCredentials(), [adapter]);
var snapshot = (await service.RefreshAsync(account.Id)).Single();
Check(snapshot.State == "setup-required" && snapshot.Error is null && snapshot.LastSuccessAt is null
    && snapshot.Used is null && snapshot.Limit is null && snapshot.Remaining is null && snapshot.ResetsAt is null,
    "service exposes reporting prerequisite without reading credentials or claiming measured success");
Console.WriteLine("NVIDIA public-reference and qualification checks passed.");

sealed class FixtureHttp(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(reply(request));
    }
}

sealed class FixtureAccounts(AccountDto account) : IQuotaAccountSource
{
    public Task<IReadOnlyList<AccountDto>> GetAccountsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AccountDto>>([account]);
}
sealed class NoCredentials : IQuotaCredentialSource
{
    public Task<QuotaCredential?> GetCredentialAsync(string accountId, CancellationToken ct = default) =>
        throw new Exception("The anonymous product reference must not load account credentials.");
}
