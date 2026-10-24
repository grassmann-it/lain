using System.Net;
using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Quota;
using Lain.Host.Providers.NvidiaStep;

var assertions = 0;
void Check(bool value, string label) { if (!value) throw new Exception(label); assertions++; }
var root = args.Single();
string Fixture(string name) => File.ReadAllText(Path.Combine(root, "tests/providers-nvidia-step/fixtures", name + ".json"));
var http = new FixtureHttp(Fixture("prepaid"));
using var client = new HttpClient(http);
var adapter = new StepFunQuotaAdapter(client);
var account = new AccountDto("synthetic", "Synthetic PAYG fixture", "stepfun", true, "api-key", "unverified", "step-payg-pool");
var result = await adapter.FetchAsync(account, new("synthetic-test-secret", baseUrl: StepFunQuotaAdapter.ApiBaseUrl));
var row = result.Readings.Single();
Check(http.Calls == 1 && http.LastUrl == StepFunQuotaAdapter.Endpoint && http.LastMethod == HttpMethod.Get, "only fixed read-only official balance endpoint");
Check(http.AuthScheme == "Bearer" && http.AuthToken == "synthetic-test-secret", "official bearer authentication");
Check(row.Unit == "CNY" && row.Remaining == 12.5 && row.Used is null && row.Limit is null && row.UsedPercent is null && row.ResetsAt is null, "balance is money, not tokens or invented percent/reset");
Check(!JsonSerializer.Serialize(result).Contains("synthetic-test-secret"), "output contains no credential");
foreach (var baseUrl in new[] { "https://api.stepfun.com/step_plan/v1", "https://api.stepfun.com/step_plan", "https://gateway.invalid/v1", "https://api.stepfun.com/v1?key=anything", "https://api.stepfun.com.evil.invalid/v1", "", "http://api.stepfun.com/v1" })
{
    try { await adapter.FetchAsync(account, new("synthetic-test-secret", baseUrl: baseUrl)); throw new Exception("Expected endpoint rejection"); }
    catch (QuotaFetchException e) { Check(!e.Message.Contains("synthetic-test-secret"), "sanitized route rejection"); }
}
Check(http.Calls == 1, "plan/gateway/unknown endpoints never cause a request");
try { await adapter.FetchAsync(account with { CredentialKind = "oauth" }, new("synthetic-test-secret", baseUrl: StepFunQuotaAdapter.ApiBaseUrl)); throw new Exception("Expected credential rejection"); }
catch (QuotaFetchException) { Check(http.Calls == 1, "non API key blocked before HTTP"); }
using (var zero = JsonDocument.Parse(Fixture("zero"))) Check(StepFunQuotaAdapter.Normalize(zero.RootElement).Single().Remaining == 0, "zero is a real observation");
foreach (var json in new[] { Fixture("plan-credits-not-account"), "{}", "{\"object\":\"account\",\"type\":\"prepaid\",\"balance\":-1}", "{\"object\":\"account\",\"type\":\"prepaid\",\"balance\":\"NaN\"}" })
{
    using var doc = JsonDocument.Parse(json);
    Check(StepFunQuotaAdapter.Normalize(doc.RootElement).Count == 0, "unrecognized/malformed balances remain unknown");
}
http.Status = HttpStatusCode.TooManyRequests;
try { await adapter.FetchAsync(account, new("synthetic-test-secret", baseUrl: StepFunQuotaAdapter.ApiBaseUrl)); throw new Exception("Expected HTTP rejection"); }
catch (QuotaFetchException e) { Check(e.Message == "Quota HTTP 429 (rate-limit).", "HTTP body and credentials never exposed"); }
Check(NvidiaStepConfiguration.Validate("nvidia-build", "developer", "openai-chat", "https://integrate.api.nvidia.com/v1").Count == 0, "NVIDIA official setup");
Check(NvidiaStepConfiguration.Validate("stepfun", "payg", "openai-chat", StepFunQuotaAdapter.ApiBaseUrl).Count == 0, "PAYG official setup");
Check(NvidiaStepConfiguration.Validate("stepfun", "step-plan-mini", "openai-chat", "https://api.stepfun.com/step_plan/v1").Count == 0, "Plan OpenAI setup");
Check(NvidiaStepConfiguration.Validate("stepfun", "step-plan-plus", "anthropic-messages", "https://api.stepfun.com/step_plan").Count == 0, "Plan Anthropic SDK setup");
Check(NvidiaStepConfiguration.Validate("stepfun", "step-plan-plus", "anthropic-messages", "https://api.stepfun.com/step_plan/v1").Count > 0, "Anthropic double-v1 blocked");
Check(NvidiaStepConfiguration.Validate("stepfun", "step-plan-mini", "openai-chat", StepFunQuotaAdapter.ApiBaseUrl).Count > 0, "Plan cannot silently become PAYG");
foreach (var provider in new[] { "nvidia-build", "stepfun" })
{
    using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src/Lain.Host/Providers/Catalog", provider + ".json")));
    Check(doc.RootElement.GetProperty("liveVerification").GetString() == "not_tested", "mock is not real access verification");
    foreach (var route in doc.RootElement.GetProperty("routes").EnumerateArray())
    {
        var baseUrl = route.GetProperty("baseUrl").GetString()!;
        foreach (var plan in route.GetProperty("billingPlanIds").EnumerateArray())
            Check(NvidiaStepConfiguration.Validate(provider, plan.GetString()!, route.GetProperty("protocol").GetString()!, baseUrl).Count == 0, "descriptor matches configuration validation");
        if (provider == "stepfun") Check(!route.GetProperty("freeOnlyEligible").GetBoolean() && route.GetProperty("pricingClass").GetString() == "paid", "paid routes never qualify for free-only");
    }
}
Console.WriteLine($"PASS: {assertions} NVIDIA/Step synthetic assertions; no live provider access.");

sealed class FixtureHttp(string body) : HttpMessageHandler
{
    public int Calls;
    public string? LastUrl, AuthScheme, AuthToken;
    public HttpMethod? LastMethod;
    public HttpStatusCode Status = HttpStatusCode.OK;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++; LastUrl = request.RequestUri?.ToString(); LastMethod = request.Method;
        AuthScheme = request.Headers.Authorization?.Scheme; AuthToken = request.Headers.Authorization?.Parameter;
        return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
