using System.Net;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Quota;

var checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
JsonElement Json(string value) { using var doc = JsonDocument.Parse(value); return doc.RootElement.Clone(); }
IReadOnlyList<QuotaReading> Synthetic(string value) => SyntheticQuotaAdapter.Normalize(Json(value));

var rolling = Synthetic("""{"rollingFiveHourLimit":{"max":10,"remaining":0,"tickPercent":0.02,"nextTickAt":"2026-10-25T01:00:00Z"}}""").Single();
Check(rolling.NextRegenerationPercent == 2 && rolling.Remaining == 0, "rolling ratio becomes percentage points and actual zero survives");
Check(rolling.NextRegenerationAt == DateTimeOffset.Parse("2026-10-25T01:00:00Z") && rolling.ResetsAt is null,
    "partial regeneration remains separate from full reset");
var modern = Synthetic("""{"planWeeklyLimit":{"percentRemaining":25,"nextRegenPercent":2},"weeklyTokenLimit":{"maxCredits":"999","remainingCredits":"999"},"rollingFiveHourLimit":{"max":10,"remaining":0,"tickPercent":0.02},"subscription":{"limit":100,"requests":1}}""");
Check(modern.Count == 2 && modern.Single(r => r.WindowId == "plan-weekly").NextRegenerationPercent == 2,
    "modern percent unchanged; new weekly block preferred over weekly and subscription fallbacks");
Check(modern.Single(r => r.WindowId == "rolling-five-hour").NextRegenerationPercent == 2
    && modern.All(r => !r.InheritAccountPool && r.BudgetPoolId is null), "coexisting rolling constraint stays separate without additive pools");
var exact = Synthetic("""{"weeklyTokenLimit":{"maxCredits":"123456789012345678901234567890.1234567890","remainingCredits":"0.00000000000000000001","nextRegenCredits":"2.00"}}""").Single();
Check(exact.ExactRemaining == "0.00000000000000000001" && exact.ExactNextRegeneration == "2.00" && exact.Remaining is null,
    "legacy exact credit strings survive without conversion");
Check(Synthetic("""{"subscription":{"limit":10,"requests":0},"rollingFiveHourLimit":{"max":5,"remaining":2,"tickPercent":0.02}}""").Count == 2,
    "legacy subscription remains beside separately supplied rolling constraint");
foreach (var value in new[] { "null", "-0.02", "1.01", "2", "\"NaN\"", "true", "{}" })
{
    var row = Synthetic("{\"rollingFiveHourLimit\":{\"max\":10,\"tickPercent\":" + value + "}}").Single();
    Check(row.NextRegenerationPercent is null, "invalid rolling ratio stays unknown: " + value);
}
Check(Synthetic("""{"rollingFiveHourLimit":{"max":10}}""").Single().NextRegenerationPercent is null, "missing ratio stays unknown");
Check(Synthetic("""{"rollingFiveHourLimit":{"max":10,"tickPercent":0}}""").Single().NextRegenerationPercent == 0, "zero regeneration is a measurement");
Check(Synthetic("""{"rollingFiveHourLimit":{"max":10,"tickPercent":1}}""").Single().NextRegenerationPercent == 100, "ratio upper boundary is one");
foreach (var value in new[] { "null", "-1", "101", "\"NaN\"" })
    Check(Synthetic("{\"planWeeklyLimit\":{\"percentRemaining\":20,\"nextRegenPercent\":" + value + "}}").Single().NextRegenerationPercent is null,
        "invalid modern percentage stays unknown: " + value);

const string counter = """{"data":{"free_model_daily_requests":{"used":10,"limit":50,"remaining":40},"include_byok_in_limit":false,"limit":20,"limit_remaining":17,"limit_reset":"monthly","usage_monthly":3}}""";
var at = DateTimeOffset.Parse("2026-10-25T23:59:59Z");
var end = DateTimeOffset.Parse("2026-10-26T00:00:00Z");
IReadOnlyList<QuotaReading> Open(string body, DateTimeOffset? observed = null, DateTimeOffset? received = null) =>
    OpenRouterQuotaAdapter.Normalize(Json(body), observed ?? at, received);
var free = Open(counter).Single(r => r.Unit == "requests");
Check(free.ResetsAt == end && free.WindowEnd == end && free.WindowStart == end.AddDays(-1), "UTC Free day and policy reset are observation-bound");
Check(free.ObservedAt == at && free.FreshUntil == end && free.Remaining == 40, "fresh observation expires at earlier day boundary");
Check(free.WindowId == "free-model-daily-requests" && free.MeasurementKind == "quota" && free.Scope == "account"
    && free.ScopeId is null && !free.InheritAccountPool && free.BudgetPoolId is null, "account scope does not fabricate identity or pool merge");
var cap = Open(counter).Single(r => r.Unit == "usd");
Check(cap.Remaining == 17 && cap.Used == 3 && cap.ResetsAt is null && cap.WindowEnd is null, "USD key spend cap does not inherit Free day reset or counts");
Check(Open(counter, received: end).Single(r => r.Unit == "requests") is { Remaining: null, Used: null }, "response crossing midnight cannot supply current Free remainder");
Check(Open(counter, received: end.AddHours(1)).Single(r => r.Unit == "usd").Remaining == 17, "ambiguous Free day does not erase independent key cap");
var afterMidnight = Open(counter, end).Single(r => r.Unit == "requests");
Check(afterMidnight.ResetsAt == end.AddDays(1) && afterMidnight.FreshUntil == end.AddMinutes(5), "new day's actual read gets new window and normal freshness deadline");
// Explicit offsets around Europe/Berlin DST transitions must still produce UTC days.
foreach (var instant in new[] { "2026-03-29T01:30:00+01:00", "2026-03-29T03:30:00+02:00", "2026-10-25T02:30:00+02:00", "2026-10-25T02:30:00+01:00" })
{
    var observed = DateTimeOffset.Parse(instant);
    var row = Open(counter, observed).Single(r => r.Unit == "requests");
    var utc = observed.UtcDateTime.Date;
    Check(row.WindowStart == new DateTimeOffset(utc, TimeSpan.Zero) && row.ResetsAt == new DateTimeOffset(utc.AddDays(1), TimeSpan.Zero)
        && row.ObservedAt?.Offset == TimeSpan.Zero, "DST/local offset does not move UTC reset: " + instant);
}
foreach (var body in new[] { """{"data":{}}""", """{"data":{"free_model_daily_requests":null}}""" })
    Check(Open(body).Count == 0, "missing or null Free block does not invent a quota");
var unknown = Open("""{"data":{"free_model_daily_requests":{"used":null,"limit":"NaN","remaining":-1}}}""").Single();
Check(unknown is { Used: null, Limit: null, Remaining: null }, "null and invalid counts remain unknown");
Check(Open("""{"data":{"free_model_daily_requests":{"used":0,"limit":0,"remaining":0}}}""").Single() is { Used: 0, Limit: 0, Remaining: 0 }, "real Free zero stays known");
Check(Open("""{"data":{"free_model_daily_requests":{"limit":50}}}""").Single().Remaining is null, "missing remainder is not inferred from limit");
var serialized = JsonSerializer.SerializeToElement(free, new JsonSerializerOptions(JsonSerializerDefaults.Web));
Check(serialized.GetProperty("resetsAt").GetDateTimeOffset() == end && serialized.GetProperty("scope").GetString() == "account"
    && serialized.GetProperty("remaining").GetDouble() == 40, "JSON preserves shared reader facts and policy reset");

var clock = new FixtureClock(at);
var calls = 0;
var crossMidnight = false;
using var handler = new FakeHandler(request =>
{
    calls++;
    Check(request.Method == HttpMethod.Get && request.RequestUri?.ToString() == OpenRouterQuotaAdapter.Endpoint,
        "fixed read-only metadata endpoint");
    Check(request.Headers.Authorization?.ToString() == "Bearer fixture-only-key", "only selected credential used");
    if (crossMidnight) clock.Now = end;
    return new(HttpStatusCode.OK) { Content = new StringContent(counter) };
});
using var client = new HttpClient(handler);
var adapter = new OpenRouterQuotaAdapter(client, clock);
var account = new AccountDto("a", "Local", "openrouter", true, "api-key", "ready", "unrelated-pool");
var result = await adapter.FetchAsync(account, new("fixture-only-key", baseUrl: "https://openrouter.ai/api/v1"));
Check(result.Readings.Single(r => r.Unit == "requests").ObservedAt == at && result.Error is null, "Fetch uses injected observation clock");
crossMidnight = true;
var crossed = await adapter.FetchAsync(account, new("fixture-only-key", baseUrl: "https://openrouter.ai/api/v1"));
Check(crossed.Readings.Single(r => r.Unit == "requests") is { Remaining: null, Used: null } && crossed.Readings.Single(r => r.Unit == "usd").Remaining == 17,
    "fake HTTP midnight crossing keeps Free unknown and independent key cap intact");
Check(calls == 2, "exactly two explicit fake GETs, no inference or retry");
Console.WriteLine($"N02 reader fixtures passed ({checks} assertions); fake HTTP and clock only.");

sealed class FixtureClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now = now;
    public override DateTimeOffset GetUtcNow() => Now;
}
sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request));
}
