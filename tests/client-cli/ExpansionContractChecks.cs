using System.Net;
using System.Text;
using System.Text.Json;
using Lain.Client;
using Lain.Cli;

internal static class ExpansionContractChecks
{
    // Wire fixtures are independent JSON, so DTO naming changes cannot silently
    // update both fake response and consumer and hide a contract regression.
    internal static async Task<int> RunAsync()
    {
        const string hostSecret = "PRIVATE-HOST-TEST-SENTINEL";
        const string apiSecret = "PRIVATE-API-TEST-SENTINEL";
        var handler = new FakeHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:18418/") };
        using var client = new LainApiClient(http, hostSecret);
        var checks = 0;
        static void Require(bool value, string message) { if (!value) throw new Exception(message); }
        HttpResponseMessage Reply(HttpRequestMessage request, HttpStatusCode status, string json)
        {
            Require(request.Headers.Authorization?.Parameter == hostSecret, "new APIs retain local host authorization");
            return new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
        async Task<(int Exit, string Output, string Error)> Run(string[] args, string? input = null)
        {
            var output = new StringWriter(); var error = new StringWriter();
            var exit = await CliApplication.RunAsync(args, output, error, () => client,
                input: new StringReader(input ?? ""));
            Require(!output.ToString().Contains(hostSecret) && !error.ToString().Contains(hostSecret) &&
                !output.ToString().Contains(apiSecret) && !error.ToString().Contains(apiSecret), "management commands never echo either credential");
            return (exit, output.ToString(), error.ToString());
        }
        const string catalogJson = """
            {"contractVersion":1,"observedAt":"2026-10-24T00:00:00Z","providers":[{
              "id":"catalog-provider","name":"Catalog provider","summary":"Prepared provider",
              "accountUrl":"https://provider.test/account","documentationUrl":"https://provider.test/docs",
              "credentials":[
                {"id":"apiKey","label":"API key","kind":"api-key","required":true,"secret":true},
                {"id":"allowTrainingData","label":"Permit training","kind":"boolean","required":false,"secret":false}],
              "setupSteps":[{"title":"Get access","description":"Use your own account","url":"https://provider.test/setup"}],
              "billingPlans":[{"id":"trial","name":"Trial","kind":"trial","scope":"workspace","unit":"credits","includedAmount":null,"expiresAt":null}],
              "routes":[{"id":"catalog-route","model":"vendor/model","name":"Model","baseUrl":"https://provider.test/v1","protocol":"openai-chat",
                "billingPlanIds":["trial"],"capabilities":{"tools":null,"streaming":true,"reasoning":null},
                "modelFamily":"family","manufacturer":"vendor","inferenceProvider":"inference","gateway":"gateway",
                "pricingClass":"unknown","freeOnlyEligible":false,"freeOnlyEnforcementKind":"unsupported"}],
              "sources":[{"url":"https://provider.test/docs","title":"Official docs","checkedAt":"2026-10-24T00:00:00Z"}],
              "restrictions":[],"adapterState":"prepared","liveVerification":"not_tested","quotaSupport":"unsupported"}]}
            """;
        handler.Handle = request =>
        {
            Require(request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/api/v1/providers", "catalog command GET contract");
            return Reply(request, HttpStatusCode.OK, catalogJson);
        };
        var result = await Run(["providers", "catalog", "--json"]);
        using (var json = JsonDocument.Parse(result.Output))
        {
            var provider = json.RootElement.GetProperty("providers")[0];
            Require(result.Exit == 0 && result.Error == "" && provider.GetProperty("adapterState").GetString() == "prepared" &&
                provider.GetProperty("liveVerification").GetString() == "not_tested" &&
                provider.GetProperty("routes")[0].GetProperty("capabilities").GetProperty("tools").ValueKind == JsonValueKind.Null &&
                provider.GetProperty("billingPlans")[0].GetProperty("unit").GetString() == "credits", "catalog JSON preserves preparation, unverified capabilities and native budget unit");
        }
        checks++;
        using var catalog = JsonDocument.Parse(catalogJson);
        var descriptorJson = catalog.RootElement.GetProperty("providers")[0].GetRawText();
        var onboardingJson = "{\"contractVersion\":1,\"providerId\":\"catalog-provider\",\"state\":\"missing_credentials\",\"descriptor\":" + descriptorJson +
            ",\"accountIds\":[],\"missingCredentialFields\":[\"apiKey\"],\"liveVerification\":\"not_tested\"}";
        handler.Handle = request =>
        {
            Require(request.RequestUri!.AbsolutePath == "/api/v1/providers/catalog-provider/onboarding", "onboarding JSON endpoint");
            return Reply(request, HttpStatusCode.OK, onboardingJson);
        };
        result = await Run(["providers", "onboarding", "catalog-provider", "--json"]);
        using (var json = JsonDocument.Parse(result.Output))
            Require(result.Exit == 0 && json.RootElement.GetProperty("missingCredentialFields")[0].GetString() == "apiKey" &&
                json.RootElement.GetProperty("descriptor").GetProperty("setupSteps")[0].GetProperty("url").GetString() == "https://provider.test/setup",
                "onboarding JSON retains credential requirement and setup link without enrollment");
        checks++;
        const string snapshotJson = """
            {"contractVersion":1,"sourceId":"source-one","observedAt":"2026-10-24T00:00:00Z","state":"live","complete":false,"cursor":9,"earliestCursor":3,"droppedEvents":2,
             "limits":{"maxStreams":256,"maxEvents":8192,"contentBytes":33554432,"perStreamBytes":1048576,"maxEventBytes":262144,"contentTtlSeconds":120,"terminalTtlSeconds":60,"maxPageSize":500,"contentPolicy":"redacted_output_only_v1"},
             "streams":[{"id":"attempt-one","scope":"attempt","requestId":"request-one","attemptId":"attempt-one","startedAt":"2026-10-24T00:00:00Z","updatedAt":"2026-10-24T00:00:01Z","state":"streaming","firstCursor":3,"lastCursor":9,
               "dimensions":{"conversationId":"conversation-one","turnId":"turn-one","workerId":"worker-one","parentRequestId":null,"reasoningEffort":"high","effectiveReasoningEffort":"medium","evidence":[{"field":"workerId","source":"harness","key":"worker_id"}]},"truncated":true}]}
            """;
        handler.Handle = request => Reply(request, HttpStatusCode.OK, snapshotJson);
        result = await Run(["inspector", "--json"]);
        using (var json = JsonDocument.Parse(result.Output))
        {
            var root = json.RootElement; var stream = root.GetProperty("streams")[0];
            Require(result.Exit == 0 && root.GetProperty("sourceId").GetString() == "source-one" && root.GetProperty("droppedEvents").GetInt64() == 2 &&
                stream.GetProperty("scope").GetString() == "attempt" && stream.GetProperty("truncated").GetBoolean() &&
                stream.GetProperty("dimensions").GetProperty("parentRequestId").ValueKind == JsonValueKind.Null &&
                stream.GetProperty("dimensions").GetProperty("effectiveReasoningEffort").GetString() == "medium", "snapshot JSON keeps observed scope, unknown parent and effective reasoning");
        }
        checks++;
        handler.Handle = request =>
        {
            Require(request.RequestUri!.Query.Contains("sourceId=old-source") && request.RequestUri.Query.Contains("after=9"), "reset check sends original source/cursor");
            return Reply(request, HttpStatusCode.OK,
                "{\"sourceId\":\"new-source\",\"events\":[],\"nextCursor\":0,\"highWatermark\":8,\"earliestCursor\":1,\"droppedEvents\":0,\"truncated\":true,\"gapReason\":\"source_reset\"}");
        };
        result = await Run(["inspector", "events", "--source", "old-source", "--after", "9", "--json"]);
        using (var json = JsonDocument.Parse(result.Output))
            Require(result.Exit == 0 && json.RootElement.GetProperty("gapReason").GetString() == "source_reset" &&
                json.RootElement.GetProperty("sourceId").GetString() == "new-source" && json.RootElement.GetProperty("nextCursor").GetInt64() == 0,
                "source restart is a visible successful reset response, preserving server cursor");
        checks++;
        const string analysisJson = """
            {"contractVersion":1,"from":"2026-10-23T00:00:00Z","to":"2026-10-24T00:00:00Z","timeZone":"UTC","observedAt":"2026-10-24T00:00:00Z","cursor":123,"complete":false,
             "totals":{"requests":4,"attempts":5,"errors":1,"tokens":{"input":null,"output":20,"cached":null,"reasoning":null,"total":null},"unknownUsageAttempts":2},
             "performance":{"firstOutputLatencyMs":{"samples":2,"eligible":5,"minimum":1,"mean":2,"p50":2,"p95":3,"maximum":3},
               "totalDurationMs":{"samples":2,"eligible":5,"minimum":10,"mean":20,"p50":20,"p95":30,"maximum":30},
               "outputTokensPerSecond":{"samples":0,"eligible":5,"minimum":null,"mean":null,"p50":null,"p95":null,"maximum":null},"rateBasis":"reported_output_tokens_per_total_duration_second"},
             "dimensions":[]}
            """;
        handler.Handle = request =>
        {
            Require(request.RequestUri!.AbsolutePath == "/api/v1/usage/analysis", "raw analysis endpoint");
            return Reply(request, HttpStatusCode.OK, analysisJson);
        };
        result = await Run(["usage", "aggregate", "--json"]);
        using (var json = JsonDocument.Parse(result.Output))
        {
            var root = json.RootElement;
            Require(result.Exit == 0 && !root.GetProperty("complete").GetBoolean() && root.GetProperty("cursor").GetInt64() == 123 &&
                root.GetProperty("totals").GetProperty("tokens").GetProperty("total").ValueKind == JsonValueKind.Null &&
                root.GetProperty("performance").GetProperty("firstOutputLatencyMs").GetProperty("samples").GetInt64() == 2 &&
                root.GetProperty("performance").GetProperty("outputTokensPerSecond").GetProperty("p50").ValueKind == JsonValueKind.Null,
                "raw analysis JSON preserves coverage, sample count and unknown measurements");
        }
        checks++;
        var calls = 0;
        handler.Handle = request =>
        {
            calls++;
            Require(request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/v1/profiles/strict-free/apply", "strict-free denial belongs to selected profile");
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Require(body.RootElement.GetProperty("revision").GetInt64() == 3, "denied profile keeps optimistic revision");
            return Reply(request, HttpStatusCode.Conflict, "{\"code\":\"operation_unavailable\",\"message\":\"The profile cannot be applied under its free-only policy.\"}");
        };
        result = await Run(["profiles", "apply", "strict-free", "--revision", "3", "--json"]);
        Require(result.Exit == 5 && result.Output == "" && result.Error.Contains("operation_unavailable") && calls == 1,
            "strict-free profile rejection emits diagnostic without stdout, retry or fallback"); checks++;
        calls = 0;
        handler.Handle = request =>
        {
            calls++;
            Require(request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/v1/profiles/strict-free/apply", "conflicted profile is never replaced with another action");
            return Reply(request, HttpStatusCode.Conflict, "{\"code\":\"revision_conflict\",\"message\":\"Configuration changed; reload and retry.\"}");
        };
        result = await Run(["profiles", "apply", "strict-free", "--revision", "3", "--json"]);
        Require(result.Exit == 5 && result.Output == "" && result.Error.Contains("revision_conflict") && calls == 1,
            "mutation revision conflict is preserved without automatic reload or forced retry"); checks++;
        handler.Handle = request => Reply(request, HttpStatusCode.BadRequest,
            "{\"code\":\"validation\",\"message\":\"Invalid request values; check dates, identities and selected accounts.\"}");
        result = await Run(["inspector", "snapshot", "--limit", "100", "--json"]);
        Require(result.Exit == 5 && result.Output == "" && result.Error.Contains("validation"), "lower configured Host bounds remain an explicit rejection"); checks++;
        handler.Handle = request => Reply(request, HttpStatusCode.ServiceUnavailable, "malformed private body " + hostSecret);
        result = await Run(["providers", "catalog", "--json"]);
        Require(result.Exit == 3 && result.Output == "" && result.Error.Contains("http_error"), "malformed provider API failure cannot leak response content"); checks++;

        foreach (var permission in new[] { "true", "false" })
        {
            handler.Handle = request =>
            {
                if (request.RequestUri!.AbsolutePath == "/api/v1/providers") return Reply(request, HttpStatusCode.OK, catalogJson);
                Require(request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath == "/api/v1/accounts", "permission uses revisioned account creation");
                using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                var value = body.RootElement.GetProperty("value");
                Require(value.GetProperty("settings").GetProperty("allowTrainingData").GetString() == permission &&
                    value.GetProperty("apiKey").GetString() == apiSecret, "privacy permission is forwarded exactly as explicitly supplied");
                return permission == "true" ? Reply(request, HttpStatusCode.OK, "{\"revision\":4,\"appliedRevision\":3,\"accounts\":[],\"routes\":[],\"profiles\":[]}") :
                    Reply(request, HttpStatusCode.BadRequest, "{\"code\":\"validation\",\"message\":\"Contributor privacy permission is required.\"}");
            };
            result = await Run(["accounts", "add-api", "--name", "test", "--provider", "catalog-provider", "--base-url", "https://provider.test/v1",
                "--provider-route", "catalog-route", "--setting", "allowTrainingData=" + permission, "--key-stdin", "--revision", "3", "--json"], apiSecret + "\n");
            Require(result.Exit == (permission == "true" ? 0 : 5) && (permission == "true" || result.Output == ""),
                "CLI cannot promote false privacy permission or conceal Host denial"); checks++;
        }
        return checks;
    }
}
