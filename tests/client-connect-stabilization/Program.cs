using System.Net;
using System.Net.Http.Json;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Lain.Client;
using Lain.Cli;
using Lain.Core;

static void Require(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }
var previousEnvironment = new[] { "LAIN_HOME", "LAIN_REFERENCE_QUALIFICATION", "LAIN_REFERENCE_HOST_PORT" }
    .ToDictionary(name => name, Environment.GetEnvironmentVariable);
try
{
    Environment.SetEnvironmentVariable("LAIN_HOME", Path.Combine(Path.GetTempPath(), "lain-client-discovery-fixture"));
    Environment.SetEnvironmentVariable("LAIN_REFERENCE_HOST_PORT", "28418");
    Environment.SetEnvironmentVariable("LAIN_REFERENCE_QUALIFICATION", null);
    Require(LocalDiscovery.ApiAddress.Port == 18418, "reference API port requires explicit qualification mode");
    Environment.SetEnvironmentVariable("LAIN_REFERENCE_QUALIFICATION", "1");
    Require(LocalDiscovery.ApiAddress.AbsoluteUri == "http://127.0.0.1:28418/", "isolated client uses guarded loopback Host port");
    Environment.SetEnvironmentVariable("LAIN_HOME", "/var/lib/lain-discovery-fixture");
    Require(LocalDiscovery.ApiAddress.Port == 18418, "reference port cannot redirect non-temporary state");
    Environment.SetEnvironmentVariable("LAIN_HOME", Path.Combine(Path.GetTempPath(), "lain-client-discovery-fixture"));
    Environment.SetEnvironmentVariable("LAIN_REFERENCE_HOST_PORT", "invalid");
    var invalidRejected = false;
    try { _ = LocalDiscovery.ApiAddress; } catch (InvalidOperationException) { invalidRejected = true; }
    Require(invalidRejected, "invalid isolated port fails before contacting an installed service");
}
finally
{
    foreach (var pair in previousEnvironment) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
}
var handler = new Fixture();
using var http = new HttpClient(handler) { BaseAddress = new Uri("http://fixture.invalid/api/v1/") };
using var client = new LainApiClient(http);
var output = new StringWriter(); var error = new StringWriter();
var code = await CliApplication.RunAsync(["status"], output, error, () => client);
Require(code == 0 && output.ToString().Contains("Quota monitoring") && !output.ToString().Contains("Proxy") && !output.ToString().Contains("Unavailable"), "intentional Monitor status does not invent a failed proxy");
output = new(); error = new();
code = await CliApplication.RunAsync(["quotas"], output, error, () => client);
Require(code == 0 && output.ToString().Contains("0.000034") && output.ToString().Contains("0.0000340000000000000001"), "small balances and provider exact precision survive human rendering");
Require(output.ToString().Contains("Partial regeneration") && output.ToString().Contains("Next billing") && output.ToString().Contains("Pass renewal"), "partial regeneration and Pass dates retain distinct meanings");
Require(!output.ToString().Contains("% used"), "unbounded money does not get a fabricated percentage");
Require(output.ToString().Contains("Kilo Pass details · stale · last success 2026-10-01 12:00 UTC"),
    "fresh wallet cannot imply stale Pass details were refreshed");
var keyFacts = QuotaDisplayFacts.Describe(new("requesty", null, "fixture", "ok", DateTimeOffset.UtcNow,
    DateTimeOffset.UtcNow, null, "USD", 1, 10, 9, false,
    RequestyKeyObservation: new("error", DateTimeOffset.UtcNow, null, null, "Key details unavailable."))).ToArray();
Require(keyFacts.Single().Contains("Requesty key details · error · no successful reading"),
    "missing Requesty metadata reports its own failed reading independently of balance");
output = new(); error = new();
code = await CliApplication.RunAsync(["history", "--today"], output, error, () => client);
Require(code == 0 && output.ToString().Contains("known reported 72,000") && output.ToString().Contains("measured 1/2 attempts"), "CLI reports known partial token totals with component measurement coverage");

Require(QuotaDisplayFacts.Percent(45.6534080833333) == "45.7" && QuotaDisplayFacts.Amount(81.519887875) == "81.52"
    && QuotaDisplayFacts.Amount(150) == "150", "fractional quota labels use one percentage decimal and at most two native decimals");
Require(QuotaDisplayFacts.Amount(1234567.891) == "1,234,567.89"
    && QuotaDisplayFacts.Percent(45.6534080833333, CultureInfo.GetCultureInfo("de-DE")) == "45,7"
    && QuotaDisplayFacts.Amount(1234567.891, CultureInfo.GetCultureInfo("de-DE")) == "1.234.567,89",
    "grouping and decimal precision follow the requested culture without changing units");
Require(QuotaDisplayFacts.Amount(0.000034) == "<0.01" && QuotaDisplayFacts.Amount(-0.004) == ">-0.01"
    && QuotaDisplayFacts.Amount(0) == "0" && QuotaDisplayFacts.Amount(-1.519887875) == "-1.52",
    "tiny signed balances stay distinct from zero and overdrawn values retain their sign");
Require(QuotaDisplayFacts.Number(9007199254740992d, "9007199254740993") == "9,007,199,254,740,993"
    && QuotaDisplayFacts.Number(1e-30, "1e-30") == "<0.01"
    && QuotaDisplayFacts.Number(-1e-30, "-1e-30") == ">-0.01",
    "exact integers retain precision beyond double range and decimal underflow cannot erase tiny balances");
Require(QuotaDisplayFacts.Percent(0.004) == "<0.1" && QuotaDisplayFacts.Percent(99.96) == ">99.9"
    && QuotaDisplayFacts.Percent(100) == "100" && QuotaDisplayFacts.Percent(100.004) == ">100"
    && QuotaDisplayFacts.Percent(145.6534) == "145.7", "percentage labels preserve nonzero, nonexhausted and over-limit distinctions");
Require(new double?[] { null, double.NaN, double.PositiveInfinity, double.NegativeInfinity }.All(value =>
    QuotaDisplayFacts.Amount(value) == "unknown" && QuotaDisplayFacts.Percent(value) == "unknown"),
    "missing and nonfinite quota values remain unknown");
var regeneration = QuotaDisplayFacts.Describe(Fixture.FractionalQuota with { NextRegenerationAt = DateTimeOffset.UtcNow.AddHours(1),
    NextRegenerationPercent = 5.6534080833333, ExactNextRegeneration = "5.6534080833333" }).Single();
Require(regeneration.StartsWith("Partial regeneration · 5.7%"), "regeneration percentage uses the same human precision");
var ratio = (double?)typeof(CliApplication).GetMethod("QuotaPercentage", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [Fixture.FractionalQuota]);
Require(ratio is { } full && Math.Abs(full - 45.6534080833333) < 1e-10 && Math.Abs(full - 45.7) > 0.01,
    "quota ratio calculation retains full precision before display formatting");

handler.Fractional = true;
output = new(); error = new();
code = await CliApplication.RunAsync(["quotas"], output, error, () => client);
Require(code == 0 && output.ToString().Contains("45.7% used") && output.ToString().Contains("Remaining 81.52 USD-Credits")
    && output.ToString().Contains("Used 68.48 USD-Credits / 150 USD-Credits") && output.ToString().Contains("1,234,567.89 tokens"),
    "full CLI quota rendering uses concise native values and grouped counts");
Require(output.ToString().Contains("remaining 81.519887875 USD-Credits"), "explicit exact amount details retain provider precision");
output = new(); error = new();
code = await CliApplication.RunAsync(["usage"], output, error, () => client);
Require(code == 0 && output.ToString().Contains("45.7% used") && output.ToString().Contains("81.52 USD-Credits left")
    && output.ToString().Contains("1,234,567.89 tokens"), "compact CLI account rendering shares the same quota formatter");
output = new(); error = new();
code = await CliApplication.RunAsync(["quotas", "--json"], output, error, () => client);
using (var raw = JsonDocument.Parse(output.ToString()))
{
    var quota = raw.RootElement[0];
    Require(code == 0 && quota.GetProperty("used").GetDouble() == Fixture.FractionalQuota.Used
        && quota.GetProperty("remaining").GetDouble() == 81.519887875
        && quota.GetProperty("exactRemaining").GetString() == "81.519887875", "machine JSON retains full numeric data and exact provider strings");
}
handler.Rate = true;
output = new(); error = new();
code = await CliApplication.RunAsync(["quotas"], output, error, () => client);
Require(code == 0 && output.ToString().Contains("Observed 0.04 requests/second") && output.ToString().Contains("5-minute average")
    && !output.ToString().Contains("Remaining") && !output.ToString().Contains("limit unknown") && !output.ToString().Contains("% used"),
    "observed Enterprise usage rates are distinct from remaining budgets and quota percentages");
output = new(); error = new();
code = await CliApplication.RunAsync(["usage"], output, error, () => client);
Require(code == 0 && output.ToString().Contains("Observed 0.04 requests/second") && output.ToString().Contains("Sample ")
    && !output.ToString().Contains("Remaining") && !output.ToString().Contains("% used"), "compact rates retain the source sample without invented quota state");
Require(handler.Requests.All(r => r.StartsWith("GET ")), "all fixtures are read-only without external credentials or provider calls");

sealed class Fixture : HttpMessageHandler
{
    public bool Fractional;
    public bool Rate;
    public static readonly QuotaDto FractionalQuota = new("commandcode", null, "fixture#monthlyCredits", "ok",
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, "USD-Credits", 68.480112125, 150, 81.519887875, false,
        WindowLabel: "Monthly credits", MeasurementKind: "quota", ExactRemaining: "81.519887875");
    public List<string> Requests { get; } = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Requests.Add(request.Method + " " + request.RequestUri!.AbsolutePath);
        if (request.Method != HttpMethod.Get || request.RequestUri.Host != "fixture.invalid") throw new InvalidOperationException("Forbidden fixture transport");
        var now = DateTimeOffset.UtcNow;
        var missing = new TokenComponentMeasurementDto(null, 0, 2);
        var known = new TokenComponentMeasurementDto(72000, 1, 2);
        var totals = new UsageTotals(2, 2, 0, new(null, null, null, null, null), 1,
            new(known, missing, missing, missing, missing, known));
        object data = request.RequestUri.AbsolutePath switch
        {
            "/api/v1/status" => new StatusDto(1, "fixture", "ready", false, 1, 1, now, RuntimeMode: "monitor", Capabilities: new(true, false, false, false, false, false)),
            "/api/v1/accounts" => new[] { new AccountDto("commandcode", "Command Code", "commandcode", true, "api-key", "ready"),
                new AccountDto("count", "Token counter", "fixture", true, "api-key", "ready") },
            "/api/v1/quotas" when Rate => new[] { new QuotaDto("commandcode", null, "fixture#rate", "ok", now, now, null,
                "requests/second", 0.04, null, null, false, WindowLabel: "model · request rate · 5-minute average", MeasurementKind: "rate", DataAsOf: now.AddMinutes(-1)) },
            "/api/v1/quotas" when Fractional => new[] { FractionalQuota,
                new QuotaDto("count", null, "fixture#tokens", "ok", now, now, null, "tokens", 1234567.891, null, null, false, MeasurementKind: "usage") },
            "/api/v1/quotas" => new[] { new QuotaDto("kilo", null, "fixture#wallet", "ok", now, now, null, "USD", null, null, 0.000034, false,
                WindowLabel: "Wallet", MeasurementKind: "balance", ExactRemaining: "0.0000340000000000000001", NextRegenerationAt: now.AddHours(1), NextRegenerationPercent: 5,
                KiloPass: new("active", "fixture", 10, 2, 1, now.AddDays(1), now.AddDays(2), true),
                KiloPassObservation: new("stale", now, DateTimeOffset.Parse("2026-10-01T12:00:00Z"), DateTimeOffset.Parse("2026-10-01T12:05:00Z"), "Pass details unavailable.")) },
            "/api/v1/usage" => new UsageSummaryDto(1, now.AddDays(-1), now, "UTC", now, 1, true, totals, [], [], []),
            _ => throw new InvalidOperationException("Unlisted fixture request " + request.RequestUri.AbsolutePath)
        };
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(data) });
    }
}
