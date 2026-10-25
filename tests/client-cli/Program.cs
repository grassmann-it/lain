using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lain.Client;
using Lain.Cli;
using Lain.Core;

if (args.FirstOrDefault() == "--wizard-fixture")
{
    Environment.ExitCode = await AccountWizardChecks.RunFixtureAsync(Console.Out, Console.Error);
    return;
}

if (args.FirstOrDefault() == "--render-fixture")
{
    Environment.ExitCode = await CliPresentationFixtures.RunAsync(args[1..], Console.Out, Console.Error);
    return;
}

static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
var assertions = 0;
var handler = new FakeHandler();
using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:18418/") };
using var client = new LainApiClient(http, "HOST-TEST-TOKEN");
var status = new StatusDto(1, "test", "ready", true, 2, 2, DateTimeOffset.UtcNow);
handler.Handle = request =>
{
    Require(request.Headers.Authorization?.Parameter == "HOST-TEST-TOKEN", "host bearer is required");
    Require(request.RequestUri!.AbsolutePath == "/api/v1/status", "versioned status path");
    return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(status) };
};
Require((await client.StatusAsync()).Revision == 2, "typed status deserialize"); assertions++;

var query = new UsageQuery(DateTimeOffset.Parse("2026-10-23T00:00:00+02:00"), DateTimeOffset.Parse("2026-10-24T00:00:00+02:00"),
    "Europe/Berlin", "account &1", "a/b+c", 429, 20, 10);
var queryString = LainApiClient.QueryString(query);
Require(queryString.Contains("timeZone=Europe%2FBerlin") && queryString.Contains("accountId=account%20%261") &&
    queryString.Contains("model=a%2Fb%2Bc") && queryString.Contains("%2B02%3A00") && queryString.Contains("statusCode=429") &&
    queryString.Contains("offset=20&limit=10"), "query preserves all dimensions and escapes offset/model/account"); assertions++;

var configuration = new ConfigurationDto(3, 2, [], [], []);
handler.Handle = request =>
{
    Require(request.Method == HttpMethod.Put && request.RequestUri!.AbsolutePath == "/api/v1/routes/route-1", "route mutation uses typed action endpoint");
    using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
    Require(body.RootElement.GetProperty("revision").GetInt64() == 2 && body.RootElement.GetProperty("value").GetProperty("alias").GetString() == "test-alias", "revision envelope and route payload");
    return new(HttpStatusCode.OK) { Content = JsonContent.Create(configuration) };
};
Require((await client.SaveRouteAsync(2, new RouteDto("route-1", "test-alias", "test-model", "test", ["acct-1"]))).Revision == 3, "mutation response preserves applied revision difference"); assertions++;
handler.Handle = request =>
{
    Require(request.Method == HttpMethod.Delete && request.RequestUri!.PathAndQuery == "/api/v1/accounts/acct-1?revision=3", "account delete carries revision");
    return new(HttpStatusCode.OK) { Content = JsonContent.Create(configuration) };
};
await client.RemoveAccountAsync(3, "acct-1"); assertions++;
handler.Handle = request =>
{
    Require(request.RequestUri!.AbsolutePath == "/api/v1/status", "status path");
    return new(HttpStatusCode.OK) { Content = JsonContent.Create(status) };
};

var output = new StringWriter(); var error = new StringWriter();
var exit = await CliApplication.RunAsync(["status", "--json"], output, error, () => client);
Require(exit == 0 && error.ToString() == "", "JSON status succeeds without diagnostics");
Require(JsonDocument.Parse(output.ToString()).RootElement.GetProperty("revision").GetInt64() == 2, "JSON stdout is machine readable"); assertions++;

handler.Handle = request =>
{
    Require(request.RequestUri!.PathAndQuery == "/api/v1/auth/token?audience=codex", "dedicated inference token endpoint");
    return new(HttpStatusCode.OK) { Content = JsonContent.Create(new TokenResponse("INFERENCE-TEST-ONLY")) };
};
output = new(); error = new();
exit = await CliApplication.RunAsync(["auth", "token", "--audience", "codex"], output, error, () => client);
Require(exit == 0 && output.ToString() == "INFERENCE-TEST-ONLY" + Environment.NewLine && error.ToString() == "", "auth stdout contains only inference secret"); assertions++;

handler.Handle = _ => new(HttpStatusCode.Conflict) { Content = JsonContent.Create(new ApiError("revision_conflict", "Reload first.")) };
output = new(); error = new();
exit = await CliApplication.RunAsync(["accounts", "--json"], output, error, () => client);
Require(exit == 5 && output.ToString() == "" && error.ToString().Contains("revision_conflict"), "rejected operation keeps JSON stdout empty"); assertions++;

handler.Handle = _ => new(HttpStatusCode.Unauthorized) { Content = new StringContent("not-json HOST-TEST-TOKEN") };
output = new(); error = new();
exit = await CliApplication.RunAsync(["status", "--json"], output, error, () => client);
Require(exit == 4 && output.ToString() == "" && !error.ToString().Contains("HOST-TEST-TOKEN"), "non-JSON authorization failure never echoes raw response"); assertions++;

handler.Handle = _ => throw new HttpRequestException("secret exception detail");
output = new(); error = new();
exit = await CliApplication.RunAsync(["usage", "--today", "--json"], output, error, () => client);
Require(exit == 3 && output.ToString() == "" && error.ToString().Contains("host is unavailable") && !error.ToString().Contains("secret exception"), "host-down diagnostic is distinct and sanitized"); assertions++;

output = new(); error = new();
exit = await CliApplication.RunAsync(["usage", "--today", "--from", "2026-10-23T00:00:00Z"], output, error,
    () => throw new Exception("should validate before opening client"));
Require(exit == 2 && error.ToString().Contains("cannot be combined"), "ambiguous interval rejected before discovery"); assertions++;

handler.Handle = request =>
{
    var uri = Uri.UnescapeDataString(request.RequestUri!.Query);
    Require(uri.Contains("timeZone=Europe/Berlin") && uri.Contains("from=") && uri.Contains("to=") && uri.Contains("statusCode=429"), "today sends explicit timezone and bounds with status filter");
    return new(HttpStatusCode.OK) { Content = JsonContent.Create(new PageDto<AttemptDto>([], 0, 100, 0)) };
};
output = new(); error = new();
exit = await CliApplication.RunAsync(["attempts", "--today", "--timezone", "Europe/Berlin", "--status", "429", "--json"], output, error, () => client);
Require(exit == 0, "today attempts filters accepted"); assertions++;

handler.Handle = request =>
{
    Require(request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/v1/accounts/login", "account login endpoint");
    using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
    Require(payload.RootElement.GetProperty("provider").GetString() == "codex" && payload.RootElement.GetProperty("revision").GetInt64() == 3, "login provider/revision");
    return new(HttpStatusCode.OK) { Content = JsonContent.Create(new LoginDto("login-1", "codex", "pending", "https://example.test/login")) };
};
output = new(); error = new();
exit = await CliApplication.RunAsync(["accounts", "login", "codex", "--revision", "3"], output, error, () => client);
Require(exit == 0 && output.ToString().Contains("https://example.test/login") && error.ToString() == "", "login emits authorization URL"); assertions++;
foreach (var action in new[] { "login-status", "login-cancel" })
{
    handler.Handle = request =>
    {
        Require(request.RequestUri!.AbsolutePath == "/api/v1/accounts/login/login-1" && request.Method == (action == "login-status" ? HttpMethod.Get : HttpMethod.Delete), "login status/cancel typed method");
        return new(HttpStatusCode.OK) { Content = JsonContent.Create(new LoginDto("login-1", "codex", "cancelled")) };
    };
    output = new(); error = new();
    exit = await CliApplication.RunAsync(["accounts", action, "login-1", "--json"], output, error, () => client);
    Require(exit == 0 && JsonDocument.Parse(output.ToString()).RootElement.GetProperty("state").GetString() == "cancelled", "login lifecycle JSON"); assertions++;
}
foreach (var action in new[] { "enable", "disable" })
{
    handler.Handle = request =>
    {
        Require(request.Method == HttpMethod.Put && request.RequestUri!.AbsolutePath == "/api/v1/accounts/acct-1/enabled", "account state endpoint");
        using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
        Require(payload.RootElement.GetProperty("revision").GetInt64() == 3 && payload.RootElement.GetProperty("value").GetBoolean() == (action == "enable"), "enable/disable revision request");
        return new(HttpStatusCode.OK) { Content = JsonContent.Create(configuration) };
    };
    output = new(); error = new();
    exit = await CliApplication.RunAsync(["accounts", action, "acct-1", "--revision", "3", "--json"], output, error, () => client);
    Require(exit == 0 && error.ToString() == "", "account state mutation succeeds"); assertions++;
}
var secretName = "LAIN_SYNTHETIC_TEST_API_KEY";
var previousSecret = Environment.GetEnvironmentVariable(secretName);
try
{
    Environment.SetEnvironmentVariable(secretName, "SYNTHETIC-API-KEY");
    handler.Handle = request =>
    {
        Require(request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/v1/accounts", "add API account endpoint");
        using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
        Require(payload.RootElement.GetProperty("value").GetProperty("apiKey").GetString() == "SYNTHETIC-API-KEY", "secret supplied only in API request body");
        return new(HttpStatusCode.OK) { Content = JsonContent.Create(configuration) };
    };
    foreach (var source in new[] { "env", "stdin" })
    {
        output = new(); error = new();
        string[] accountArgs = ["accounts", "add-api", "--name", "test", "--provider", "test", "--base-url", "https://example.test/v1", "--revision", "3", "--json",
            .. (source == "env" ? new[] { "--key-env", secretName } : new[] { "--key-stdin" })];
        exit = await CliApplication.RunAsync(accountArgs, output, error, () => client, input: new StringReader("SYNTHETIC-API-KEY\n"));
        Require(exit == 0 && !output.ToString().Contains("SYNTHETIC-API-KEY") && error.ToString() == "", "secret sources do not echo credentials"); assertions++;
    }
}
finally { Environment.SetEnvironmentVariable(secretName, previousSecret); }
output = new(); error = new();
exit = await CliApplication.RunAsync(["accounts", "add-api", "--api-key", "SHOULD-NOT-ECHO"], output, error,
    () => throw new Exception("invalid input must not reach client"));
Require(exit == 2 && output.ToString() == "" && !error.ToString().Contains("SHOULD-NOT-ECHO"), "plaintext API key option rejected without echo"); assertions++;

handler.Handle = request =>
{
    Require(request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/v1/profiles/profile-1/apply", "apply profile endpoint");
    using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
    Require(payload.RootElement.GetProperty("revision").GetInt64() == 3, "apply uses optimistic revision");
    return new(HttpStatusCode.OK) { Content = JsonContent.Create(configuration with { AppliedRevision = 3 }) };
};
output = new(); error = new();
exit = await CliApplication.RunAsync(["profiles", "apply", "profile-1", "--revision", "3", "--json"], output, error, () => client);
Require(exit == 0 && JsonDocument.Parse(output.ToString()).RootElement.GetProperty("appliedRevision").GetInt64() == 3, "CLI apply emits effective configuration"); assertions++;

var codex = Path.Combine(Path.GetTempPath(), "lain-codex-" + Guid.NewGuid().ToString("N"));
var artifacts = new CodexProfileArtifacts("lain.config.toml", "synthetic generated config", "lain.models.json", "{\"models\":[]}", ["--no-daemon", "--profile", "lain"], 3);
try
{
    Directory.CreateDirectory(codex);
    File.WriteAllText(Path.Combine(codex, "config.toml"), "EXISTING-CONFIG");
    handler.Handle = request =>
    {
        Require(request.RequestUri!.AbsolutePath == "/api/v1/profiles/profile-1/codex/export", "Codex export endpoint");
        using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
        Require(payload.RootElement.GetProperty("executablePath").GetString() == "/absolute/lain" &&
            payload.RootElement.GetProperty("catalogPath").GetString() == Path.Combine(codex, "lain.models.json"), "Codex artifact request uses absolute helper/catalog paths");
        return new(HttpStatusCode.OK) { Content = JsonContent.Create(artifacts) };
    };
    var generated = await client.ExportCodexProfileAsync(3, "profile-1", "/absolute/lain", Path.Combine(codex, "lain.models.json"));
    var installed = CodexArtifactInstaller.Install(generated, codex);
    Require(File.ReadAllText(installed.ConfigurationPath) == artifacts.Configuration && File.ReadAllText(installed.CatalogPath) == artifacts.Catalog,
        "generated artifacts installed");
    Require(File.ReadAllText(Path.Combine(codex, "config.toml")) == "EXISTING-CONFIG" && Directory.GetFiles(codex).Length == 3,
        "ordinary config unchanged and temporary files removed");
    if (!OperatingSystem.IsWindows()) Require(File.GetUnixFileMode(installed.ConfigurationPath) == (UnixFileMode.UserRead | UnixFileMode.UserWrite), "installed configuration is private");
    assertions++;
    try { CodexArtifactInstaller.Install(artifacts with { ConfigFileName = "config.toml" }, codex); throw new Exception("unexpected file must be rejected"); }
    catch (InvalidDataException) { assertions++; }
}
finally { if (Directory.Exists(codex)) Directory.Delete(codex, true); }

// Readable output keeps unknown measurements explicit and terminal control characters inert.
handler.Handle = _ => new(HttpStatusCode.OK) { Content = JsonContent.Create(new PageDto<AttemptDto>([
    new AttemptDto("attempt-1", "request-1", "account\u001b[31m\nnext", null, "provider", "model", DateTimeOffset.UtcNow,
        null, null, "streaming", new TokenCounts(null, null, null, null, null))], 0, 10, 12)) };
output = new(); error = new();
exit = await CliApplication.RunAsync(["attempts", "--limit", "10"], output, error, () => client);
Require(exit == 0 && output.ToString().Contains("Attempts 1 of 12; offset 0; limit 10") &&
    output.ToString().Contains("first output unknown ms") && !output.ToString().Contains('\u001b'),
    "attempt text shows page bounds, unknown measurements and no terminal escapes"); assertions++;
handler.Handle = _ => new(HttpStatusCode.OK) { Content = JsonContent.Create(new[] {
    new QuotaDto("account", "shared-pool", "provider", "unavailable", null, null, null, "points", null, null, null, true) }) };
output = new(); error = new();
exit = await CliApplication.RunAsync(["quotas"], output, error, () => client);
Require(exit == 0 && output.ToString().Contains("Quota values not reported") &&
    output.ToString().Contains("Shared budget with legacy"), "quota text preserves native units and shared budget scope"); assertions++;

var providerRoute = new ProviderRouteDescriptorDto("route/test", "vendor/model", "Test model", "https://provider.test/v1", "openai-chat", ["trial"],
    new RouteCapabilitiesDto(), ModelFamily: "family", Manufacturer: "vendor", InferenceProvider: "inference", Gateway: "gateway");
var providerDescriptor = new ProviderDescriptorDto("test/provider", "Test provider", "Synthetic documentation", "https://provider.test/account", "https://provider.test/docs",
    [new ProviderCredentialFieldDto("api_key", "API key", "api_key")], [new ProviderSetupStepDto("Create key", "Use your own account")],
    [new ProviderBillingPlanDto("trial", "Trial", "trial", "account", "points")], [providerRoute], [], []);
var providerCatalog = new ProviderCatalogDto(1, DateTimeOffset.UtcNow, [providerDescriptor]);
handler.Handle = request =>
{
    Require(request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/api/v1/providers", "provider catalog is read only");
    return new(HttpStatusCode.OK) { Content = JsonContent.Create(providerCatalog) };
};
Require((await client.ProvidersAsync()).Providers[0].Id == "test/provider", "typed provider catalog"); assertions++;
output = new(); error = new();
exit = await CliApplication.RunAsync(["providers", "routes", "--provider", "test/provider"], output, error, () => client);
Require(exit == 0 && output.ToString().Contains("pricing unknown; free-only blocked") &&
    output.ToString().Contains("tools unknown") && output.ToString().Contains("inference inference; gateway gateway"),
    "provider route text preserves uncertainty and distinct identities"); assertions++;
handler.Handle = request =>
{
    Require(request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/api/v1/providers/test%2Fprovider/onboarding", "onboarding ID is escaped");
    return new(HttpStatusCode.OK) { Content = JsonContent.Create(new ProviderOnboardingDto(1, "test/provider", "missing_credentials", providerDescriptor, [], ["api_key"])) };
};
output = new(); error = new();
exit = await CliApplication.RunAsync(["providers", "onboarding", "test/provider"], output, error, () => client);
Require(exit == 0 && output.ToString().Contains("Missing credentials: api_key") && output.ToString().Contains("live check Not checked") &&
    output.ToString().Contains("scope account; unknown points") && output.ToString().Contains("https://provider.test/account"),
    "keyless onboarding shows account link, credential field names and native budget units"); assertions++;
handler.Handle = request =>
{
    Require(request.Method == HttpMethod.Get && request.RequestUri!.PathAndQuery == "/api/v1/routes/route%2Ftest/cost-policy?costPolicy=free_only", "free policy check is escaped and read only");
    return new(HttpStatusCode.OK) { Content = JsonContent.Create(new RouteCostDecisionDto("route/test", false, "free_only", "unknown", ["Unknown pricing is blocked."])) };
};
output = new(); error = new();
exit = await CliApplication.RunAsync(["providers", "check-free", "route/test", "--json"], output, error, () => client);
Require(exit == 0 && !JsonDocument.Parse(output.ToString()).RootElement.GetProperty("allowed").GetBoolean(), "free-only inspection preserves blocked decision"); assertions++;

var emptyMetric = new MetricStatisticsDto(0, 12, null, null, null, null, null);
var performance = new PerformanceSummaryDto(new MetricStatisticsDto(10, 12, 1, 7, 8, 11, 12), emptyMetric, emptyMetric);
var analysisTotals = new UsageTotals(12, 14, 1, new TokenCounts(null, 99, null, null, null), 2);
var analysis = new UsageAnalysisDto(1, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, "UTC", DateTimeOffset.UtcNow,
    17, true, analysisTotals, performance, [new UsageDimensionDto("modelFamily", [new UsageDimensionGroupDto(null, analysisTotals, performance)], true)]);
handler.Handle = request =>
{
    Require(request.RequestUri!.AbsolutePath == "/api/v1/usage/analysis", "usage analysis authoritative endpoint");
    var uri = Uri.UnescapeDataString(request.RequestUri.Query);
    Require(uri.Contains("groupBy=modelFamily,reasoningEffort") && uri.Contains("groupLimit=5") && uri.Contains("inferenceProvider=provider/one") &&
        uri.Contains("billingAccountId=bill 1") && uri.Contains("reasoningEffort=high") && uri.Contains("effectiveReasoningEffort=medium") && uri.Contains("workerId=worker/one"), "analysis forwards grouping and identity filters");
    return new(HttpStatusCode.OK) { Content = JsonContent.Create(analysis) };
};
output = new(); error = new();
exit = await CliApplication.RunAsync(["usage", "analysis", "--group-by", "modelFamily, reasoningEffort", "--group-limit", "5",
    "--inference-provider", "provider/one", "--billing-account", "bill 1", "--reasoning", "high", "--effective-reasoning-effort", "medium", "--worker", "worker/one"], output, error, () => client);
Require(exit == 0 && output.ToString().Contains("samples 10/12 eligible; min 1; mean 7; p50 8; p95 11; max 12") &&
    output.ToString().Contains("samples 0/12 eligible; min unknown") && output.ToString().Contains("groups 1; truncated") &&
    output.ToString().Contains("Rate basis: reported_output_tokens_per_total_duration_second"), "analysis uses server statistics with sample counts, basis, unknowns and group coverage"); assertions++;
output = new(); error = new();
exit = await CliApplication.RunAsync(["usage", "performance", "--group-by", "modelFamily,reasoningEffort", "--group-limit", "5",
    "--inference-provider", "provider/one", "--billing-account", "bill 1", "--reasoning", "high", "--effective-reasoning-effort", "medium", "--worker", "worker/one", "--json"], output, error, () => client);
using (var analysisJson = JsonDocument.Parse(output.ToString()))
    Require(exit == 0 && analysisJson.RootElement.GetProperty("dimensions")[0].GetProperty("groups")[0].GetProperty("key").ValueKind == JsonValueKind.Null,
        "unknown group identity stays null in JSON"); assertions++;
foreach (var invalid in new[] {
    new[] { "usage", "analysis", "--group-by", "modelFamily, modelFamily" },
    new[] { "usage", "analysis", "--worker", new string('w', 257) },
    new[] { "usage", "analysis", "--model", "" },
    new[] { "usage", "analysis", "--reasoning", "high\nprivate" },
    new[] { "inspector", "--request", new string('r', 257) },
    new[] { "usage", "analysis", "--group-by", "model,provider,accountId,workerId" },
    new[] { "usage", "analysis", "--group-limit", "501" },
    new[] { "usage", "analysis", "--offset", "1" },
    new[] { "usage", "--group-by", "model" },
    new[] { "inspector", "events", "--after", "5" },
    new[] { "inspector", "--limit", "257" },
    new[] { "inspector", "events", "--limit", "501" } })
{
    output = new(); error = new();
    exit = await CliApplication.RunAsync(invalid, output, error, () => throw new Exception("invalid options must not discover host"));
    Require(exit == 2 && output.ToString() == "", "invalid grouping, pagination or inspector options rejected before discovery"); assertions++;
}
handler.Handle = request =>
{
    Require(request.RequestUri!.PathAndQuery == "/api/v1/inspector?limit=5&requestId=request%2Fone", "bounded snapshot with escaped request filter");
    var stream = new InspectorStreamDto("stream", "request", "request/one", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
        "streaming", 1, 4, Dimensions: new AttemptDimensionsDto(ConversationId: "conversation", TurnId: "turn", WorkerId: "worker",
            Evidence: [new DimensionEvidenceDto("workerId", "harness", "worker_id")]));
    return new(HttpStatusCode.OK) { Content = JsonContent.Create(new InspectorSnapshotDto(1, "source", DateTimeOffset.UtcNow,
        "ready", false, 4, 1, 2, new InspectorLimitsDto(), [stream])) };
};
output = new(); error = new();
exit = await CliApplication.RunAsync(["inspector", "snapshot", "--request", "request/one", "--limit", "5"], output, error, () => client);
Require(exit == 0 && output.ToString().Contains("coverage incomplete") && output.ToString().Contains("dropped events 2") &&
    output.ToString().Contains("Conversation conversation; turn turn; worker worker") && output.ToString().Contains("workerId (harness; worker_id)"),
    "inspector snapshot reports observed identities with evidence and loss/retention state"); assertions++;
handler.Handle = request =>
{
    Require(request.RequestUri!.PathAndQuery == "/api/v1/inspector/events?after=4&limit=5&sourceId=source%2Fone&requestId=request%2Fone", "event pagination carries source and cursor");
    return new(HttpStatusCode.OK) { Content = JsonContent.Create(new InspectorEventPageDto("source/one", [
        new InspectorEventDto(5, "content", DateTimeOffset.UtcNow, "request/one", Channel: "output", Text: "redacted\u001b[31m\ncontent")], 5, 7, 1, 3, true, "buffer_evicted")) };
};
output = new(); error = new();
exit = await CliApplication.RunAsync(["inspector", "events", "--source", "source/one", "--after", "4", "--request", "request/one", "--limit", "5"], output, error, () => client);
Require(exit == 0 && output.ToString().Contains("next cursor 5; high watermark 7") && output.ToString().Contains("Gap: buffer_evicted") &&
    output.ToString().Contains("Group identities: unknown") && !output.ToString().Contains('\u001b'), "event output preserves gaps/cursors and does not invent grouping"); assertions++;

handler.Handle = request =>
{
    if (request.RequestUri!.AbsolutePath == "/api/v1/providers")
        return new(HttpStatusCode.OK) { Content = JsonContent.Create(new ProviderCatalogDto(1, DateTimeOffset.UtcNow,
            [providerDescriptor with { Id = "test", Credentials = [new ProviderCredentialFieldDto("region", "Region", "text", Secret: false), new ProviderCredentialFieldDto("project", "Project", "text", Secret: false)] }])) };
    Require(request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/v1/accounts", "catalog account uses existing revisioned endpoint");
    using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
    var value = payload.RootElement.GetProperty("value");
    Require(value.GetProperty("providerRouteId").GetString() == "catalog/route" && value.GetProperty("settings").GetProperty("region").GetString() == "singapore" &&
        value.GetProperty("settings").GetProperty("workspaceId").GetString() == "workspace" && value.GetProperty("settings").GetProperty("project").GetString() == "project-one" && value.GetProperty("apiKey").GetString() == "SYNTHETIC-API-KEY", "route selection and repeated nonsecret settings retained beside dedicated secret");
    return new(HttpStatusCode.OK) { Content = JsonContent.Create(configuration) };
};
output = new(); error = new();
exit = await CliApplication.RunAsync(["accounts", "add-api", "--name", "test", "--provider", "test", "--base-url", "https://provider.test/v1",
    "--provider-route", "catalog/route", "--setting", "region=singapore", "--setting", "workspaceId=workspace", "--setting", "project=project-one", "--key-stdin", "--revision", "3", "--json"],
    output, error, () => client, input: new StringReader("SYNTHETIC-API-KEY\n"));
Require(exit == 0 && !output.ToString().Contains("SYNTHETIC-API-KEY") && error.ToString() == "", "catalog account CLI does not echo secret or setting input"); assertions++;
foreach (var settingsArgs in new[] {
    new[] { "--provider-route", "route", "--setting", "apiKey=LEAK-ME" },
    new[] { "--provider-route", "route", "--setting", "region=one", "--setting", "region=two" },
    new[] { "--setting", "region=one" } })
{
    output = new(); error = new();
    exit = await CliApplication.RunAsync(["accounts", "add-api", "--name", "test", "--provider", "test", "--base-url", "https://provider.test/v1",
        "--key-stdin", .. settingsArgs], output, error, () => throw new Exception("invalid settings must not discover host"));
    Require(exit == 2 && output.ToString() == "" && !error.ToString().Contains("LEAK-ME"), "invalid, duplicated or secret settings rejected before discovery without values"); assertions++;
}

handler.Handle = request =>
{
    Require(request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/api/v1/providers", "secret setting validation performs only catalog read");
    return new(HttpStatusCode.OK) { Content = JsonContent.Create(new ProviderCatalogDto(1, DateTimeOffset.UtcNow,
        [providerDescriptor with { Id = "test", Credentials = [new ProviderCredentialFieldDto("session", "Session", "secret", Secret: true)] }])) };
};
output = new(); error = new();
exit = await CliApplication.RunAsync(["accounts", "add-api", "--name", "test", "--provider", "test", "--base-url", "https://provider.test/v1",
    "--provider-route", "route", "--setting", "session=LEAK-ME", "--key-stdin"], output, error, () => client);
Require(exit == 2 && output.ToString() == "" && !error.ToString().Contains("LEAK-ME"), "descriptor secret fields cannot be passed as settings"); assertions++;
handler.Handle = request =>
{
    Require(request.Method == HttpMethod.Put && request.RequestUri!.AbsolutePath == "/api/v1/routes/free-route", "policy mutation uses existing route API");
    using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
    var value = payload.RootElement.GetProperty("value");
    var policy = value.GetProperty("policy");
    Require(value.GetProperty("providerRouteId").GetString() == "catalog-route" && policy.GetProperty("costPolicy").GetString() == "free_only" &&
        !policy.GetProperty("allowPaidFallback").GetBoolean() && policy.GetProperty("fallbackRouteIds")[0].GetString() == "explicit-free-fallback", "catalog identity and fail-closed explicit policy are preserved");
    return new(HttpStatusCode.OK) { Content = JsonContent.Create(configuration) };
};
await client.SaveRouteAsync(3, new RouteDto("free-route", "free", "model", "provider", ["account"], ProviderRouteId: "catalog-route",
    Policy: new RoutingPolicyDto("free_only", ["explicit-free-fallback"]))); assertions++;
handler.Handle = request =>
{
    Require(request.Method == HttpMethod.Put && request.RequestUri!.AbsolutePath == "/api/v1/profiles/free-profile", "profile policy uses typed existing endpoint");
    using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
    Require(payload.RootElement.GetProperty("value").GetProperty("policy").GetProperty("costPolicy").GetString() == "free_only", "profile policy is serialized");
    return new(HttpStatusCode.OK) { Content = JsonContent.Create(configuration) };
};
await client.SaveProfileAsync(3, new ProfileDto("free-profile", "Free", ["free-route"], Policy: new RoutingPolicyDto("free_only"))); assertions++;

var state = Path.Combine(Path.GetTempPath(), "lain-discovery-" + Guid.NewGuid().ToString("N"));
var previous = Environment.GetEnvironmentVariable("LAIN_HOME");
try
{
    Environment.SetEnvironmentVariable("LAIN_HOME", state);
    using var offline = LainApiClient.CreateLocal();
    Require(LocalDiscovery.StateDirectory == state && !Directory.Exists(state), "offline discovery creates no directories or credentials"); assertions++;
    try { await offline.StatusAsync(); throw new Exception("missing token must fail"); }
    catch (IOException ex) { Require(ex.Message.Contains("Start the lain service"), "first-run token error"); }
    Directory.CreateDirectory(Path.Combine(state, "credentials"));
    File.WriteAllText(Path.Combine(state, "credentials", "host.token"), "test-token");
    Require(LocalDiscovery.ReadHostToken() == "test-token", "LAIN_HOME bearer discovery"); assertions++;
}
finally
{
    Environment.SetEnvironmentVariable("LAIN_HOME", previous);
    if (Directory.Exists(state)) Directory.Delete(state, true);
}
assertions += await ExpansionContractChecks.RunAsync();
assertions += await CollectionCommandChecks.RunAsync();
assertions += await HumanRenderingChecks.RunAsync();
assertions += await SetupPresentationChecks.RunAsync();
assertions += await AccountWizardChecks.RunAsync();
assertions += await MonitorWizardChecks.RunAsync();
assertions += await AccountUsageChecks.RunAsync();
assertions += await UsageStyleChecks.RunAsync();
Console.WriteLine($"PASS: {assertions} client/CLI contract checks.");

sealed class FakeHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, HttpResponseMessage> Handle { private get; set; } = _ => throw new Exception("No test handler");
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(Handle(request));
}
