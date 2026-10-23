using System.Net;
using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Quota;
using Lain.Host.Providers;

static class Tests
{
    static int assertions;
    static void Check(bool value, string label) { if (!value) throw new Exception(label); assertions++; }
    static IReadOnlyList<QuotaReading> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return SyntheticQuotaAdapter.Normalize(doc.RootElement);
    }

    static async Task Main(string[] args)
    {
        var descriptors = new[] { "synthetic", "xai" }.Select(id => JsonSerializer.Deserialize<ProviderDescriptorDto>(File.ReadAllText(Path.Combine(args[0], id + ".json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!).ToArray();
        Check(descriptors.Select(d => d.Id).SequenceEqual(new[] { "synthetic", "xai" }), "provider identity distinct from Groq");
        Check(descriptors.All(d => d.LiveVerification == "not_tested"), "prepared never means live-qualified");
        Check(descriptors.SelectMany(d => d.Routes).All(r => !r.FreeOnlyEligible && r.PricingClass is "subscription" or "payg"), "no free budget or paid fallback advertised");
        Check(descriptors.Single(d => d.Id == "xai").QuotaSupport == "unsupported", "ordinary xAI key has no quota adapter");
        Check(descriptors.Single(d => d.Id == "synthetic").Routes.All(r => r.Capabilities.Tools is null), "generic tools API does not prove per-model support");
        Check(SyntheticXaiConfiguration.ValidateConfiguration("xai", "https://api.groq.com/openai/v1", "openai-chat", "grok-4.7").Length > 0, "Groq base rejected for xAI");
        Check(SyntheticXaiConfiguration.ValidateConfiguration("xai", "https://api.x.ai/v1", "openai-responses", "grok-4.7").Length == 0, "documented xAI route valid");
        Check(SyntheticXaiConfiguration.ValidateConfiguration("synthetic", "https://api.synthetic.new/openai/v1", "openai-chat", "hf:moonshotai/Kimi-K3").Length == 0, "documented Synthetic route valid");
        Check(SyntheticXaiConfiguration.ValidateConfiguration("synthetic", "https://api.synthetic.new/openai/v1", "openai-chat", "infinite:small:vision").Length > 0, "restricted subscription mode requires separate onboarding");
        var rows = Parse("""{"subscription":{"limit":135,"requests":0,"renewsAt":"2026-11-01T00:00:00Z"}}""");
        var row = rows.Single();
        Check(row.Remaining == 135 && row.Used == 0 && row.Limit == 135, "documented schema and zero usage");
        Check(row.Unit == "units" && row.BudgetPoolId is null && row.InheritAccountPool, "provider units, account pool only");
        Check(row.ResetsAt == DateTimeOffset.Parse("2026-11-01T00:00:00Z"), "observed renewal only");
        Check(Parse("""{"subscription":{"limit":0,"requests":0}}""").Single().Remaining == 0, "zero allowance known");
        Check(Parse("""{"subscription":{"limit":10,"requests":20}}""").Single().Remaining is null, "contradiction cannot create negative budget");
        Check(Parse("""{"subscription":{"limit":10}}""").Single().Remaining is null, "missing consumption unknown");
        Check(Parse("""{"subscription":{"limit":-1,"requests":"NaN"}}""").Count == 0, "invalid quantities unknown");
        Check(Parse("""{"quota":{"credits":100}}""").Count == 0, "unknown schema not guessed");
        Check(Parse("""{"subscription":{"limit":10,"requests":2,"renewsAt":"2026-11-01T00:00:00"}}""").Single().ResetsAt is null, "unqualified time unknown");
        var handler = new FakeHttp();
        using var client = new HttpClient(handler);
        var adapter = new SyntheticQuotaAdapter(client);
        var account = new AccountDto("s1", "Synthetic", "synthetic", true, "api-key", "ready");
        Check(adapter.Supports("SYNTHETIC") && !adapter.Supports("xai") && !adapter.Supports("groq"), "providers independent");
        foreach (var baseUrl in new[] { "https://api.synthetic.new/openai/v1", "https://api.synthetic.new/anthropic/v1/", "https://api.synthetic.new/v1" })
        {
            var result = await adapter.FetchAsync(account, new("mock-only-secret", baseUrl: baseUrl));
            Check(result.Readings.Single().Remaining == 133, "official base accepted");
        }
        Check(handler.Count == 3, "only explicit fake GETs");
        foreach (var baseUrl in new string?[] { null, "https://evil.test/v1", "https://api.synthetic.new.evil.test/v1", "http://api.synthetic.new/v1", "https://api.synthetic.new/v1?x=1" })
        {
            try { await adapter.FetchAsync(account, new("mock-only-secret", baseUrl: baseUrl)); throw new Exception("Expected endpoint rejection"); }
            catch (QuotaFetchException) { Check(handler.Count == 3, "foreign key never transmitted"); }
        }
        try { await adapter.FetchAsync(account with { Provider = "xai" }, new("mock-only-secret", baseUrl: "https://api.synthetic.new/v1")); throw new Exception("Expected provider rejection"); }
        catch (QuotaFetchException) { Check(handler.Count == 3, "xAI credential not sent to Synthetic"); }
        handler.Status = HttpStatusCode.Redirect;
        try { await adapter.FetchAsync(account, new("mock-only-secret", baseUrl: "https://api.synthetic.new/v1")); throw new Exception("Expected redirect rejection"); }
        catch (QuotaFetchException e) { Check(e.Message == "Quota HTTP 302.", "redirect fails with sanitized error"); }
        handler.Status = HttpStatusCode.OK;
        handler.Body = "{}";
        var unknown = await adapter.FetchAsync(account, new("mock-only-secret", baseUrl: "https://api.synthetic.new/v1"));
        Check(unknown.Readings.Count == 0 && unknown.Error is not null, "schema drift explicit");
        Console.WriteLine($"Synthetic/xAI: {assertions} assertions (mock HTTP only).");
    }

    sealed class FakeHttp : HttpMessageHandler
    {
        public int Count;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Body = """{"subscription":{"limit":135,"requests":2}}""";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Check(request.Method == HttpMethod.Get && request.RequestUri!.ToString() == SyntheticQuotaAdapter.Endpoint, "fixed read-only endpoint");
            Check(request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Authorization.Parameter == "mock-only-secret", "own bearer credential");
            Count++;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") });
        }
    }
}
