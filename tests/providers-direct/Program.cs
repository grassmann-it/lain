using System.Net;
using System.Text;
using System.Text.Json;
using Lain.Host.Providers;

var assertions = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); assertions++; }
var now = DateTimeOffset.Parse("2026-10-23T22:12:49Z");
var clock = new FixedClock(now);
var calls = 0;
var transport = new FakeHttp(request =>
{
    calls++;
    Check(request.Method == HttpMethod.Get, "metadata adapter is GET-only");
    Check(request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Authorization.Parameter == "mock-api-secret", "private credential only in Bearer header");
    Check(!request.RequestUri!.ToString().Contains("mock-api-secret") && request.RequestUri.Query == "", "key never in URI");
    return Json("""{"data":[{"id":"sample/model"},{"id":"sample/model"},{"id":"a-model"}]}""");
});
using var adapter = new DirectApiModelAdapter(transport, clock);
foreach (var (provider, expected) in new[]
{
    ("gemini-api", "https://generativelanguage.googleapis.com/v1beta/openai/models"),
    ("groq", "https://api.groq.com/openai/v1/models"),
    ("mistral", "https://api.mistral.ai/v1/models")
})
{
    Check(DirectApiModelAdapter.ModelsEndpoint(provider) == expected, "official endpoint pinned per provider");
    var observation = await adapter.ObserveAsync(provider, "mock-api-secret");
    Check(observation.State == "metadata_available" && observation.ObservedAt == now, "metadata success is explicit");
    Check(observation.Models.SequenceEqual(new[] { "a-model", "sample/model" }), "model IDs sorted and deduplicated");
    Check(observation.InferenceVerification == "not_tested" && observation.QuotaVerification == "unsupported", "metadata success never claims inference/quota qualification");
    Check(transport.Uris.Last() == expected, "request reached the correct fixed endpoint");
}
Check(calls == 3, "exactly one metadata request per provider");
Check((await adapter.ObserveAsync("groq", null)).State == "missing-credential", "missing keys remain visible");
Check((await adapter.ObserveAsync("grok", "mock-api-secret")).State == "unsupported", "xAI/Grok is not Groq");
Check((await adapter.ObserveAsync("https://evil.invalid", "mock-api-secret")).State == "unsupported", "no caller-controlled credential destination");
Check((await adapter.ObserveAsync("groq", "bad\r\nsecret")).State == "error" && calls == 3, "invalid/missing/unsupported credentials never send HTTP");
transport.Respond = _ => new(HttpStatusCode.Redirect) { Headers = { Location = new("https://evil.invalid/") }, Content = new StringContent("mock-api-secret") };
var redirect = await adapter.ObserveAsync("groq", "mock-api-secret");
Check(redirect.State == "error" && !JsonSerializer.Serialize(redirect).Contains("mock-api-secret"), "redirect refused, provider body never disclosed");
transport.Respond = _ => Json("""[{"id":"mistral-small-2603"}]""");
Check((await adapter.ObserveAsync("mistral", "mock-api-secret")).Models.Single() == "mistral-small-2603", "documented array response shape accepted");
foreach (var invalid in new[] { "{}", "{broken", """{"data":[{"id":"mock-api-secret"}]}""", """{"data":[{"id":42}]}""", """{"data":[{"id":"<html>"}]}""", """{"data":[{}]}""" })
{
    transport.Respond = _ => Json(invalid);
    var bad = await adapter.ObserveAsync("gemini-api", "mock-api-secret");
    Check(bad.State == "error" && bad.Models.Length == 0 && !JsonSerializer.Serialize(bad).Contains("mock-api-secret"), "malformed or secret-echoed metadata rejected without disclosure");
}
transport.Respond = _ => new(HttpStatusCode.TooManyRequests) { Content = new StringContent("mock-api-secret") };
Check((await adapter.ObserveAsync("groq", "mock-api-secret")).Error == "Provider metadata rate limit reached.", "HTTP 429 sanitized");
transport.Respond = _ => throw new HttpRequestException("mock-api-secret");
Check(!(await adapter.ObserveAsync("groq", "mock-api-secret")).Error!.Contains("mock-api-secret"), "transport errors sanitized");
transport.Respond = _ => Json(new string('x', 2 * 1024 * 1024 + 1));
Check((await adapter.ObserveAsync("groq", "mock-api-secret")).Error!.Contains("too large"), "metadata size bound");
using var cancelled = new CancellationTokenSource();
cancelled.Cancel();
transport.Respond = _ => { cancelled.Token.ThrowIfCancellationRequested(); return Json("{}"); };
try { await adapter.ObserveAsync("groq", "mock-api-secret", cancelled.Token); throw new Exception("cancellation expected"); }
catch (OperationCanceledException) { Check(true, "caller cancellation propagates"); }

using var response = new HttpResponseMessage(HttpStatusCode.OK);
response.Headers.TryAddWithoutValidation("x-ratelimit-limit-requests", "14400");
response.Headers.TryAddWithoutValidation("x-ratelimit-remaining-requests", "0");
response.Headers.TryAddWithoutValidation("x-ratelimit-reset-requests", "2m59.56s");
response.Headers.TryAddWithoutValidation("x-ratelimit-limit-tokens", "18000");
response.Headers.TryAddWithoutValidation("x-ratelimit-remaining-tokens", "17997");
response.Headers.TryAddWithoutValidation("x-ratelimit-reset-tokens", "7.66s");
response.Headers.TryAddWithoutValidation("retry-after", "2");
var observed = GroqRateLimitHeaders.Parse(response, now);
var requests = observed.Windows.Single(w => w.Kind == "requests_per_day");
var tokens = observed.Windows.Single(w => w.Kind == "tokens_per_minute");
Check(requests.Unit == "requests" && requests.Window == "day" && requests.Remaining == 0, "requests header is RPD; zero is not unknown");
Check(requests.ResetsAt == now.AddSeconds(179.56), "compound reset duration relative to actual response time");
Check(tokens.Window == "minute" && tokens.Limit == 18000 && tokens.Remaining == 17997 && tokens.ResetsAt == now.AddSeconds(7.66), "tokens header is TPM, not an account token balance");
Check(observed.RetryAfter is null, "retry-after ignored outside 429");
response.StatusCode = HttpStatusCode.TooManyRequests;
Check(GroqRateLimitHeaders.Parse(response, now).RetryAfter == TimeSpan.FromSeconds(2), "429 retry-after parsed separately");
using var empty = new HttpResponseMessage();
Check(GroqRateLimitHeaders.Parse(empty, now).Windows.Length == 0, "missing headers do not fabricate a window");
foreach (var invalid in new[] { "-1", "NaN", "Infinity", "1.5", "999999999999999999999999999999" })
{
    using var malformed = new HttpResponseMessage();
    malformed.Headers.TryAddWithoutValidation("x-ratelimit-remaining-tokens", invalid);
    Check(GroqRateLimitHeaders.Parse(malformed, now).Windows.Length == 0, "invalid numeric headers discarded");
}
foreach (var invalid in new[] { "-1s", "7.66", "today", "1s1s", "1e3s", "99999999999999999999d" })
{
    using var malformed = new HttpResponseMessage();
    malformed.Headers.TryAddWithoutValidation("x-ratelimit-reset-tokens", invalid);
    Check(GroqRateLimitHeaders.Parse(malformed, now).Windows.Length == 0, "invalid reset duration never becomes a timestamp");
}
using var inconsistent = new HttpResponseMessage();
inconsistent.Headers.TryAddWithoutValidation("x-ratelimit-limit-tokens", "100");
inconsistent.Headers.TryAddWithoutValidation("x-ratelimit-remaining-tokens", "101");
Check(GroqRateLimitHeaders.Parse(inconsistent, now).Windows.Single().Remaining is null, "contradictory remaining amount discarded");
using var duplicate = new HttpResponseMessage();
duplicate.Headers.TryAddWithoutValidation("x-ratelimit-remaining-tokens", new[] { "1", "2" });
Check(GroqRateLimitHeaders.Parse(duplicate, now).Windows.Length == 0, "ambiguous duplicate headers discarded");
Console.WriteLine($"Direct API mock checks passed: {assertions} assertions; no live requests.");

static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public Func<HttpRequestMessage, HttpResponseMessage> Respond = respond;
    public List<string> Uris { get; } = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    { Uris.Add(request.RequestUri!.ToString()); return Task.FromResult(Respond(request)); }
}
