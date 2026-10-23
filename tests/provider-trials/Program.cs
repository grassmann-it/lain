using System.Net;
using System.Text.Json;
using Lain.Host.Providers;

var assertions = 0;
var mercury = new TrialProviderConfiguration("inception", "mercury-3", "new-account-trial");
var nebius = new TrialProviderConfiguration("nebius", "deepseek-ai/DeepSeek-R1-0528", "signup-trial");
var alibaba = new TrialProviderConfiguration("alibaba-model-studio", "qwen3.8-max", "new-user-model-trial",
    Region: "ap-southeast-1", WorkspaceId: "llm-example", ServiceDeploymentScope: "International");

Check(TrialProviderConfigurationValidator.Validate(alibaba).AbsoluteUri ==
    "https://llm-example.ap-southeast-1.maas.aliyuncs.com/compatible-mode/v1/", "Singapore workspace endpoint");
foreach (var region in new[] { "cn-beijing", "ap-southeast-1", "eu-central-1", "ap-northeast-1", "cn-hongkong", "us-east-1" })
{
    var config = alibaba with { Region = region, BillingPlan = "payg", ServiceDeploymentScope = "Global" };
    Check(TrialProviderConfigurationValidator.Validate(config).Host == $"llm-example.{region}.maas.aliyuncs.com", "regional PAYG endpoint");
}
Reject(() => TrialProviderConfigurationValidator.Validate(alibaba with { Region = "cn-beijing" }), "trial cannot move regions");
Reject(() => TrialProviderConfigurationValidator.Validate(alibaba with { ServiceDeploymentScope = "Global" }), "trial cannot move scopes");
Reject(() => TrialProviderConfigurationValidator.Validate(alibaba with { WorkspaceId = "other.example" }), "workspace cannot inject host");
Reject(() => TrialProviderConfigurationValidator.Validate(alibaba with { WorkspaceId = "" }), "workspace required");
Reject(() => TrialProviderConfigurationValidator.Validate(alibaba with { Region = "invalid" }), "region required");
Reject(() => TrialProviderConfigurationValidator.Validate(alibaba with { ServiceDeploymentScope = null }), "scope required");
Reject(() => TrialProviderConfigurationValidator.Validate(alibaba with { BillingPlan = "coding-plan" }), "Coding Plan excluded");
Reject(() => TrialProviderConfigurationValidator.Validate(alibaba with { BaseUrl = "https://coding-intl.dashscope.aliyuncs.com/v1" }), "coding endpoint excluded");
Reject(() => TrialProviderConfigurationValidator.Validate(alibaba with { BaseUrl = "https://llm-example.cn-beijing.maas.aliyuncs.com/compatible-mode/v1" }), "endpoint cannot mix regions");
Reject(() => TrialProviderConfigurationValidator.Validate(nebius with { BaseUrl = "https://example.invalid/v1" }), "API keys cannot be forwarded to arbitrary endpoints");
Reject(() => TrialProviderConfigurationValidator.Validate(mercury with { Model = "mercury-2" }), "no inherited Mercury version capabilities");
foreach (var config in new[] { mercury, nebius, alibaba })
    Reject(() => TrialProviderConfigurationValidator.Validate(config with { CostPolicy = "free_only" }), "credit alone cannot enforce free-only");

using var body = JsonDocument.Parse("""
{"model":"mercury-3","messages":[{"role":"user","content":"mock prompt"}],"reasoning_effort":"instant","temperature":0.5,"max_completion_tokens":131072,"stream":true,"tools":[{"type":"function","function":{"name":"lookup","parameters":{"type":"object"}}}]}
""");
var handler = new FakeProvider();
using var client = new HttpClient(handler);
using (var request = TrialProviderRequestAdapter.CreateRequest(mercury, "test-only-not-a-real-key", body.RootElement))
using (var response = await client.SendAsync(request))
{
    Check(response.StatusCode == HttpStatusCode.OK && handler.Calls == 1, "mock request only");
    Check(handler.Uri == "https://api.inceptionlabs.ai/v1/chat/completions", "documented Mercury route");
    Check(handler.Authorization == "Bearer test-only-not-a-real-key", "bearer authentication");
    Check(handler.Body == body.RootElement.GetRawText(), "tools, streaming and reasoning fields preserved");
    Check(!handler.Body.Contains("test-only-not-a-real-key", StringComparison.Ordinal), "credential not in payload");
}

foreach (var effort in new[] { "instant", "low", "medium", "high" })
{
    using var json = JsonDocument.Parse($$"""{"model":"mercury-3","messages":[],"reasoning_effort":"{{effort}}"}""");
    using var request = TrialProviderRequestAdapter.CreateRequest(mercury, "mock-key", json.RootElement);
    Check(request.Method == HttpMethod.Post, "documented Mercury reasoning choice accepted");
}
foreach (var invalid in new[]
{
    "{\"model\":\"mercury-3\",\"messages\":[],\"reasoning_effort\":\"none\"}",
    "{\"model\":\"mercury-3\",\"messages\":[],\"reasoning_effort\":1}",
    "{\"model\":\"mercury-3\",\"messages\":[],\"temperature\":0}",
    "{\"model\":\"mercury-3\",\"messages\":[],\"temperature\":1.1}",
    "{\"model\":\"mercury-3\",\"messages\":[],\"max_completion_tokens\":131073}",
    "{\"model\":\"mercury-3\",\"messages\":[],\"max_tokens\":0}",
    "{\"model\":\"other-model\",\"messages\":[]}",
    "{\"model\":\"mercury-3\",\"model\":\"other-model\",\"messages\":[]}",
    "{\"model\":\"mercury-3\",\"messages\":[],\"api_key\":\"secret\"}",
    "{\"model\":\"mercury-3\",\"messages\":[],\"Authorization\":\"secret\"}",
    "{\"model\":\"mercury-3\"}"
})
{
    using var json = JsonDocument.Parse(invalid);
    Reject(() => TrialProviderRequestAdapter.CreateRequest(mercury, "mock-key", json.RootElement), "invalid request rejected");
}
Reject(() => TrialProviderRequestAdapter.CreateRequest(mercury, "secret\r\ninjection", body.RootElement), "header injection denied without echoing key");

foreach (var config in new[] { nebius, alibaba })
{
    using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { model = config.Model, messages = new[] { new { role = "user", content = "mock" } } }));
    using var request = TrialProviderRequestAdapter.CreateRequest(config, "mock-key", json.RootElement);
    using var response = await client.SendAsync(request);
    Check(response.IsSuccessStatusCode && handler.Uri!.EndsWith("/chat/completions", StringComparison.Ordinal), "mock OpenAI-compatible request");
}
foreach (var id in new[] { "inception", "alibaba-model-studio", "nebius" })
{
    using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Catalog", id + ".json")));
    var root = manifest.RootElement;
    Check(root.GetProperty("Id").GetString() == id && root.GetProperty("LiveVerification").GetString() == "not_tested", "catalog preserves untested status");
    Check(root.GetProperty("BillingPlans").EnumerateArray().All(p => p.GetProperty("Kind").GetString() != "free"), "no trial mislabeled as permanent free");
    Check(root.GetProperty("BillingPlans").EnumerateArray().All(p => p.GetProperty("IncludedAmount").ValueKind == JsonValueKind.Null && p.GetProperty("ExpiresAt").ValueKind == JsonValueKind.Null), "unobserved account amounts and expiry stay unknown");
    Check(root.GetProperty("Routes").EnumerateArray().All(r => !r.GetProperty("FreeOnlyEligible").GetBoolean()), "unqualified cutoff never permits free-only");
    Check(root.GetProperty("Sources").EnumerateArray().All(s => s.GetProperty("CheckedAt").ValueKind == JsonValueKind.String), "official evidence dated");
}
Console.WriteLine($"PASS: {assertions} trial-provider mock and policy assertions; no external network or live credentials.");

void Check(bool condition, string message) { if (!condition) throw new Exception(message); assertions++; }
void Reject(Action action, string message)
{
    try { action(); } catch (ArgumentException error)
    {
        Check(!error.Message.Contains("secret", StringComparison.Ordinal), "validation errors do not echo credentials");
        assertions++;
        return;
    }
    throw new Exception(message);
}

sealed class FakeProvider : HttpMessageHandler
{
    public int Calls { get; private set; }
    public string? Uri { get; private set; }
    public string? Authorization { get; private set; }
    public string Body { get; private set; } = "";
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++;
        Uri = request.RequestUri!.AbsoluteUri;
        Authorization = request.Headers.Authorization?.ToString();
        Body = await request.Content!.ReadAsStringAsync(ct);
        return new(HttpStatusCode.OK) { Content = new StringContent("{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"mock\"}}]}") };
    }
}
