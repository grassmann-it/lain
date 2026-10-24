using System.Net;
using System.Net.Http.Json;
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
output = new(); error = new();
code = await CliApplication.RunAsync(["history", "--today"], output, error, () => client);
Require(code == 0 && output.ToString().Contains("known reported 72,000") && output.ToString().Contains("measured 1/2 attempts"), "CLI reports known partial token totals with component measurement coverage");
Require(handler.Requests.All(r => r.StartsWith("GET ")), "all fixtures are read-only without external credentials or provider calls");

sealed class Fixture : HttpMessageHandler
{
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
            "/api/v1/quotas" => new[] { new QuotaDto("kilo", null, "fixture#wallet", "ok", now, now, null, "USD", null, null, 0.000034, false,
                WindowLabel: "Wallet", MeasurementKind: "balance", ExactRemaining: "0.0000340000000000000001", NextRegenerationAt: now.AddHours(1), NextRegenerationPercent: 5,
                KiloPass: new("active", "fixture", 10, 2, 1, now.AddDays(1), now.AddDays(2), true)) },
            "/api/v1/usage" => new UsageSummaryDto(1, now.AddDays(-1), now, "UTC", now, 1, true, totals, [], [], []),
            _ => throw new InvalidOperationException("Unlisted fixture request " + request.RequestUri.AbsolutePath)
        };
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(data) });
    }
}
