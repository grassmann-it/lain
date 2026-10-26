using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lain.Core;
using Lain.Host.Models;
using Lain.Host.Operations;
using Lain.Host.Providers;
using Lain.Host.Workers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var assertions = 0;
void Check(bool condition, string name) { assertions++; if (!condition) throw new Exception("FAILED: " + name); }
void Reject(Action action, string code, string name)
{
    try { action(); }
    catch (WorkerPlanException error)
    {
        Check(error.Code == code, name + " (expected " + code + ", got " + error.Code + ")");
        Check(!error.Message.Contains("fixture-access") && !error.Message.Contains("fixture-api") && !error.Message.Contains(Path.GetTempPath()), name + " sanitized");
        return;
    }
    throw new Exception("FAILED: accepted " + name);
}
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var request = Fixture.Request();
var validJson = JsonSerializer.Serialize(request, json);
Check(WorkerPlanValidation.Parse(Encoding.UTF8.GetBytes(validJson)).Tasks.Single().Id == "task-a", "strict parser round trip");
var empty = request with { Policy = request.Policy with { Purposes = [] }, Tasks = [] };
WorkerPlanValidation.Validate(empty);
Check(true, "empty setup policy and task batch accepted");
foreach (var mode in new string?[] { null, "inherit", "native" })
{
    var contextual = request with { Policy = request.Policy with { Purposes = [request.Policy.Purposes[0] with { ContextMode = mode }] } };
    Check(WorkerPlanValidation.Parse(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(contextual, json))).Policy.Purposes[0].ContextMode == mode,
        "optional explicit context mode round trips: " + mode);
}
var omittedContext = JsonNode.Parse(validJson)!;
omittedContext["policy"]!["purposes"]![0]!.AsObject().Remove("contextMode");
Check(WorkerPlanValidation.Parse(Encoding.UTF8.GetBytes(omittedContext.ToJsonString())).Policy.Purposes[0].ContextMode is null,
    "legacy request without context mode remains compatible");
omittedContext["policy"]!["purposes"]![0]!["contextMode"] = null;
Check(WorkerPlanValidation.Parse(Encoding.UTF8.GetBytes(omittedContext.ToJsonString())).Policy.Purposes[0].ContextMode is null,
    "explicit null context mode remains compatible");
foreach (var mode in new[] { "", "Native", "compact", "native ", "inherit-all" })
    Reject(() => WorkerPlanValidation.Validate(request with { Policy = request.Policy with {
        Purposes = [request.Policy.Purposes[0] with { ContextMode = mode }] } }), "invalid_request", "unknown context mode refused");
var duplicateContext = JsonNode.Parse(validJson)!;
duplicateContext["policy"]!["purposes"]![0]!["contextMode"] = "native";
Reject(() => WorkerPlanValidation.Parse(Encoding.UTF8.GetBytes(duplicateContext.ToJsonString().Replace("\"contextMode\":\"native\"", "\"contextMode\":\"native\",\"contextMode\":\"inherit\""))),
    "invalid_request", "duplicate context mode refused");
foreach (var value in new[] { "true", "7", "[]", "{}" })
    Reject(() => WorkerPlanValidation.Parse(Encoding.UTF8.GetBytes(duplicateContext.ToJsonString().Replace("\"contextMode\":\"native\"", "\"contextMode\":" + value))),
        "invalid_request", "context mode cannot coerce a non-string value");

foreach (var malformed in new[]
{
    "null", "[]", "{}", "{broken", validJson.Replace("\"revision\":7", "\"revision\":\"7\""),
    validJson.Replace("\"revision\":7", "\"revision\":7,\"revision\":7"),
    validJson.Replace("\"revision\":7", "\"revision\":7,\"unknown\":true"),
    validJson.Replace("\"revision\":7,", ""),
    validJson.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"),
    validJson.Replace("\"profileId\":\"profile\"", "\"profileId\":\"profile\",\"ProfileId\":\"profile\""),
    validJson.Replace("\"name\":\"code\"", "\"name\":\"code\",\"name\":\"code\""),
    validJson.Replace("\"routeIds\":[\"route-a\"]", "\"routeIds\":[\"route-a\"],\"extra\":0"),
    validJson.Replace("\"id\":\"task-a\"", "\"id\":\"task-a\",\"id\":\"other\""),
    validJson.Replace("\"prompt\":\"Inspect the explicit task.\"", "\"prompt\":\"Inspect the explicit task.\",\"extra\":[]"),
    validJson.Replace("\"tasks\":[", "\"tasks\":null,\"unused\":["),
    validJson.Replace("\"maxWorkers\":2", "\"maxWorkers\":2.5")
}) Reject(() => WorkerPlanValidation.Parse(Encoding.UTF8.GetBytes(malformed)), "invalid_request", "strict malformed/duplicate/unknown keys");

Reject(() => WorkerPlanValidation.Validate(request with { Policy = request.Policy with { SchemaVersion = 2 } }), "unsupported_schema", "future schema denied");
foreach (var limit in new[] { -1, 0, 13, 32, int.MaxValue })
    Reject(() => WorkerPlanValidation.Validate(request with { Policy = request.Policy with { MaxWorkers = limit } }), "worker_limits", "worker absolute limit");
WorkerPlanValidation.Validate(request with { Policy = request.Policy with { MaxWorkers = 12 },
    Tasks = Enumerable.Range(0, 12).Select(i => request.Tasks[0] with { Id = "task-" + i }).ToArray() });
Check(WorkerPlanValidation.MaximumWorkers == 12, "twelve-worker boundary accepted");
WorkerPlanValidation.Validate(request with { Tasks = [request.Tasks[0] with { Id = new string('a', 100) }] });
Check(true, "hundred-character task key accepted");
Reject(() => WorkerPlanValidation.Validate(request with { Policy = request.Policy with { MaxWorkers = 1 }, Tasks = [request.Tasks[0], request.Tasks[0] with { Id = "task-b" }] }), "worker_limits", "batch must fit maxworkers");
Reject(() => WorkerPlanValidation.Validate(request with { Policy = request.Policy with { Purposes = Enumerable.Range(0, 129).Select(i => new WorkerPurposePolicy("purpose-" + i, ["route-a"])).ToArray() } }), "worker_limits", "purpose limit");
Reject(() => WorkerPlanValidation.Validate(request with { Policy = request.Policy with { Purposes = [new("code", Enumerable.Range(0, 129).Select(i => "route-" + i).ToArray())] } }), "invalid_request", "routes per purpose limit");
Reject(() => WorkerPlanValidation.Validate(request with { Tasks = [request.Tasks[0], request.Tasks[0]] }), "duplicate_task", "duplicate task identity");
Reject(() => WorkerPlanValidation.Validate(request with { Policy = request.Policy with { Purposes = [request.Policy.Purposes[0], request.Policy.Purposes[0]] } }), "duplicate_purpose", "duplicate purpose identity");
Reject(() => WorkerPlanValidation.Validate(request with { Policy = request.Policy with { Purposes = [new("code", ["route-a", "route-a"])] } }), "duplicate_route", "duplicate route reference");
Reject(() => WorkerPlanValidation.Validate(request with { Tasks = [request.Tasks[0] with { Purpose = "unknown" }] }), "unknown_purpose", "unknown task purpose");
foreach (var key in new[] { "", " ", "../route", "a/b", "code:all", "code\n", new string('a', 101) })
    Reject(() => WorkerPlanValidation.Validate(request with { Tasks = [request.Tasks[0] with { Id = key }] }), "invalid_request", "bounded task identity");
foreach (var invalid in new WorkerPlanRequest[]
{
    request with { Revision = -1 }, request with { Policy = null! }, request with { Tasks = null! },
    request with { Tasks = [null!] }, request with { Policy = request.Policy with { Purposes = [null!] } },
    request with { Policy = request.Policy with { CostPolicy = null! } }, request with { Policy = request.Policy with { CostPolicy = "cheapest" } },
    request with { Policy = request.Policy with { Purposes = [new("code", [null!])] } },
    request with { Tasks = [request.Tasks[0] with { Prompt = null! }] },
    request with { Tasks = [request.Tasks[0] with { Prompt = "\0" }] },
    request with { Tasks = [request.Tasks[0] with { Prompt = new string('x', 32769) }] },
    request with { Tasks = [request.Tasks[0] with { WritePaths = Enumerable.Range(0, 129).Select(i => "file" + i).ToArray() }] }
}) Reject(() => WorkerPlanValidation.Validate(invalid), "invalid_request", "invalid null, enum or bounded field");
foreach (var path in new[] { null!, "", ".", "..", "../escape", "/absolute", "src/../escape", "src/./file", "src//file", "src/", "C:/file", "src\\file", "src/*", "src/ file", "src/file.", "file\0", new string('x', 513) })
    Reject(() => WorkerPlanValidation.Validate(request with { Tasks = [request.Tasks[0] with { WritePaths = [path] }] }), "invalid_write_path", "unsafe write path");
foreach (var (a, b) in new[] { ("src", "src/a.cs"), ("src/a.cs", "src"), ("src/a.cs", "src/a.cs"), ("SRC/A.cs", "src/a.cs") })
    Reject(() => WorkerPlanValidation.Validate(request with { Tasks = [request.Tasks[0] with { WritePaths = [a] }, request.Tasks[0] with { Id = "task-b", WritePaths = [b] }] }), "write_conflict", "cross task write collision");
Reject(() => WorkerPlanValidation.Validate(request with { Tasks = [request.Tasks[0] with { WritePaths = ["src/a", "SRC/a"] }] }), "duplicate_write_path", "duplicate paths");
WorkerPlanValidation.Validate(request with { Tasks = [request.Tasks[0] with { WritePaths = ["src/a"] }, request.Tasks[0] with { Id = "task-b", WritePaths = ["src/ab"] }] });
Check(true, "distinct sibling paths are not prefix collisions");
Reject(() => WorkerPlanValidation.Parse(new byte[WorkerPlanValidation.MaximumRequestBytes + 1]), "request_too_large", "bounded JSON bytes");

using (var fixture = new Fixture())
{
    var before = fixture.Files();
    var plan = fixture.Service.Plan(request);
    var task = plan.Tasks.Single();
    Check(task.ContextMode == "inherit", "omitted context mode resolves to inherit");
    var nativePlan = fixture.Service.Plan(request with { Policy = request.Policy with {
        Purposes = [request.Policy.Purposes[0] with { ContextMode = "native" }] } });
    Check(nativePlan.Tasks.Single().ContextMode == "native" && nativePlan.Notes.Any(note =>
        note.Contains("Task task-a: explicit native context") && note.Contains("project rules") &&
        note.Contains("Automatic skill instructions") && note.Contains("including guidance normally supplied by skills") &&
        note.Contains("not a guarantee")), "native plan exposes tool and skill guidance limitations without an inferred tool budget");
    Check(JsonSerializer.Deserialize<WorkerPlan>(JsonSerializer.Serialize(nativePlan, json), json)!.Tasks[0].ContextMode == "native",
        "resolved native mode survives public plan round trip");
    Check(task.RouteId == "route-a" && task.Model == "alias-b" && task.Provider == "codex" && task.AccountIds.SequenceEqual(["acct"]), "route ID preserves alias/upstream/provider/account identity");
    Check(task.ReasoningEffort == "low", "actual Codex export supplies native default");
    Check(task.Compatibility is { RouteId: "route-a", Provider: "codex", UpstreamModel: "gpt-6-astra", UpstreamProtocol: "responses" }, "task carries exact route-scoped compiler compatibility evidence");
    Check(task.Compatibility!.Capabilities.Functions.State == "unknown" && task.Compatibility.Capabilities.Streaming.State == "unknown" &&
        task.Compatibility.Capabilities.ToolContinuation.State == "unknown", "native client fixture and compiled transport never invent provider tool support");
    Check(plan.Notes.Any(note => note.StartsWith("Task task-a (route route-a): capability evidence:") &&
        note.Contains("functions=unknown, streaming=unknown, toolContinuation=unknown") && note.Contains("Unknown values remain unqualified") &&
        note.Contains("No provider-tested acceptance is claimed")), "unknown tools stay unqualified in advisory notes without fabricated support");
    Check(!plan.Notes.Any(note => note.Contains("execution blocked")), "unknown capabilities alone do not block known-reasoning native workers");
    var roundTrip = JsonSerializer.Deserialize<WorkerPlan>(JsonSerializer.Serialize(plan, json), json)!;
    Check(roundTrip.Tasks[0].Compatibility!.Capabilities.ToolContinuation.State == "unknown", "task evidence survives public plan round trip");
    Check(plan.Revision == 7 && plan.ContractVersion == 1 && plan.ProfileId == "profile" && plan.MaxWorkers == 2, "plan carries bounded revision contract");
    Check(task.WritePaths.Length == 0 && plan.Notes.Any(note => note.Contains("hard spend cap")), "empty writes and honest plan limits");
    Check(!JsonSerializer.Serialize(plan).Contains("fixture-access"), "no credentials in plan");
    Check(fixture.Service.Plan(empty).Tasks.Length == 0, "setup validates active runtime without any tasks or purposes");
    Reject(() => fixture.Service.Plan(request with { Revision = 6 }), "revision_conflict", "stale desired revision");
    Reject(() => fixture.Service.Plan(request with { Policy = request.Policy with { ProfileId = "missing" } }), "unknown_profile", "missing profile");
    Reject(() => fixture.Service.Plan(Fixture.WithRoutes(request, ["route-a", "missing"])), "unknown_route", "unknown later route never ignored");
    Reject(() => fixture.Service.Plan(Fixture.WithRoutes(request, ["alias-b"])), "unknown_route", "alias cannot substitute for route ID");
    Reject(() => fixture.Service.Plan(Fixture.WithRoutes(request, ["virtual-router"])), "unknown_route", "virtual router cannot substitute for route");
    var second = fixture.Service.Plan(Fixture.WithRoutes(request, ["route-b", "route-a"]));
    Check(second.Tasks[0].RouteId == "route-b" && second.Tasks[0].Model == "alias-a" && second.Tasks[0].ReasoningEffort == "medium", "declared route order wins over model names");
    var explicitEffort = Fixture.WithRoutes(request, ["route-b", "route-a"], "ultra");
    Check(fixture.Service.Plan(explicitEffort).Tasks[0].RouteId == "route-a", "ordered planning skips reasoning incompatible candidate without execution retry");
    Reject(() => fixture.Service.Plan(Fixture.WithRoutes(request, ["route-b"], "ultra")), "reasoning_mismatch", "no silent reasoning normalization");
    Reject(() => fixture.Service.Plan(Fixture.WithRoutes(request, ["route-a"], "ULTRA")), "reasoning_mismatch", "effort names are exact");
    var unknownPlan = fixture.Service.Plan(Fixture.WithRoutes(request, ["route-unknown"]));
    Check(unknownPlan.Tasks[0].ReasoningEffort is null, "unknown reasoning stays unknown");
    Check(unknownPlan.Notes.Any(note => note.StartsWith("Task task-a (route route-unknown): execution blocked;") && note.Contains("reasoning default is unknown")), "unknown reasoning blocks execution explicitly without hiding read-only plan");
    Reject(() => fixture.Service.Plan(Fixture.WithRoutes(request, ["route-unknown"], "none")), "reasoning_mismatch", "unknown does not advertise none");
    Reject(() => fixture.Service.Plan(request with { Policy = request.Policy with { CostPolicy = "free_only" } }), "cost_policy_denied", "project cannot weaken or hypothetically tighten runtime cost policy");
    Check(before.OrderBy(p => p.Key).SequenceEqual(fixture.Files().OrderBy(p => p.Key)), "all planning and rejection paths preserve every fixture file and create no tokens/artifacts");

    foreach (var field in new[] { "id", "model", "provider", "accounts", "costPolicy", "upstreamProtocol", "credentials" })
    {
        var original = File.ReadAllText(fixture.BindingsPath);
        var bindings = JsonNode.Parse(original)!;
        var route = bindings["routes"]!["alias-b"]!;
        route[field] = field switch { "accounts" => new JsonArray("other"), "credentials" => new JsonObject { ["acct"] = "wrong-private-identity" }, _ => JsonValue.Create("wrong") };
        File.WriteAllText(fixture.BindingsPath, bindings.ToJsonString());
        Reject(() => fixture.Service.Plan(request), "runtime_drift", "actual runtime " + field + " drift");
        File.WriteAllText(fixture.BindingsPath, original);
    }
    File.WriteAllText(fixture.ActivePath, "{\"id\":\"other-profile\",\"revision\":7}");
    Reject(() => fixture.Service.Plan(request), "runtime_drift", "same revision different active profile");
    fixture.PublishMetadata();
    File.WriteAllText(fixture.RuntimePath, "{\"valid\":false,\"revision\":7,\"profileId\":\"profile\"}");
    Reject(() => fixture.Service.Plan(request), "runtime_drift", "incomplete runtime");
    fixture.PublishMetadata();
    File.WriteAllText(Path.Combine(fixture.Paths.Root, "profile-application.json"), "{\"state\":\"publishing\",\"id\":\"in-flight\",\"profileId\":\"profile\",\"revision\":7}");
    Reject(() => fixture.Service.Plan(request), "runtime_drift", "uncommitted application proof");
}

foreach (var transform in new Func<ConfigurationDto, ConfigurationDto>[]
{
    c => c with { Routes = c.Routes.Select(r => r.Id == "route-a" ? r with { Enabled = false } : r).ToArray() },
    c => c with { Routes = c.Routes.Select(r => r.Id == "route-a" ? r with { Visible = false } : r).ToArray() }
})
{
    using var fixture = new Fixture(transform);
    Reject(() => fixture.Service.Plan(request), "route_unavailable", "disabled or hidden requested route denied");
    Check(fixture.Service.Plan(Fixture.WithRoutes(request, ["route-a", "route-b"])).Tasks[0].RouteId == "route-b", "next eligible ordered route selected at plan time");
}
using (var fixture = new Fixture(c => c with { Routes = c.Routes.Select(r => r.Id == "route-a" ? r with { UpstreamModel = "codex-auto-review" } : r).ToArray() }))
    Reject(() => fixture.Service.Plan(request), "internal_route", "internal review cannot be a worker");
using (var fixture = new Fixture(c => c with { Accounts = [c.Accounts[0] with { Enabled = false }] }, mutateAfterPublish: true))
    Reject(() => fixture.Service.Plan(request with { Revision = fixture.Repository.Snapshot.Revision }), "invalid_profile", "disabled account rejected after valid baseline publication");
using (var fixture = new Fixture(c => c with { AppliedRevision = 6 }, mutateAfterPublish: true))
    Reject(() => fixture.Service.Plan(request with { Revision = fixture.Repository.Snapshot.Revision }), "runtime_drift", "pending unapplied revision denied");
using (var fixture = new Fixture(c => c with { RuntimeMode = "monitor" }, mutateAfterPublish: true))
    Reject(() => fixture.Service.Plan(request with { Revision = fixture.Repository.Snapshot.Revision }), "gateway_required", "monitor cannot plan inference");
using (var fixture = new Fixture(c => c with { Accounts = [c.Accounts[0] with { Capabilities = [new("inference", "unsupported")] }] }, mutateAfterPublish: true))
    Reject(() => fixture.Service.Plan(request with { Revision = fixture.Repository.Snapshot.Revision }), "provider_transport_unavailable", "disabled inference capability cannot borrow previously compiled credential proof");
using (var fixture = new Fixture(c => c with { Profiles = [c.Profiles[0] with { RouteIds = ["route-b", "route-unknown"] }] }))
    Reject(() => fixture.Service.Plan(request), "route_not_in_profile", "configured route outside applied profile denied");
using (var fixture = new Fixture())
{
    fixture.Secrets.DeleteAccount("acct");
    Reject(() => fixture.Service.Plan(request), "provider_credential_unavailable", "missing provider credential distinctly sanitized");
}
using (var fixture = new Fixture())
{
    fixture.Secrets.WriteAccount("acct", "{\"kind\":\"monitor-access\",\"apiKey\":\"fixture-api-do-not-leak\"}");
    Reject(() => fixture.Service.Plan(request), "provider_transport_invalid", "invalid provider transport distinctly sanitized");
}
using (var fixture = new Fixture(c => c with
{
    Accounts = [c.Accounts[0], c.Accounts[0] with { Id = "other", Name = "Second fixture" }],
    Routes = c.Routes.Select(r => r.Id == "route-b" ? r with { UpstreamModel = "gpt-6-astra", AccountIds = ["other"] } : r).ToArray()
}))
{
    var task = fixture.Service.Plan(Fixture.WithRoutes(request, ["route-b"])).Tasks.Single();
    Check(task.RouteId == "route-b" && task.Model == "alias-a" && task.AccountIds.SequenceEqual(["other"]), "same upstream model on different route/account retains exact identity");
}
using (var fixture = new Fixture(c => c with
{
    Accounts = [c.Accounts[0], c.Accounts[0] with { Id = "other", Name = "Second fixture" }],
    Routes = c.Routes.Select(r => r.Id == "route-a" ? r with { Pooled = true, AccountIds = ["other", "acct"] } : r).ToArray()
}))
{
    Check(fixture.Service.Plan(request).Tasks.Single().AccountIds.SequenceEqual(["other", "acct"]), "configured pool is preserved without quota optimization or per-account scheduling");
}
using (var fixture = new Fixture())
{
    File.WriteAllText(fixture.ActivePath, "{\"id\":\"profile\",\"revision\":7,\"generationId\":\"generation-a\"}");
    File.WriteAllText(fixture.RuntimePath, "{\"valid\":true,\"profileId\":\"profile\",\"revision\":7,\"generationId\":\"generation-a\"}");
    var operationPath = Path.Combine(fixture.Paths.Root, "profile-application.json");
    File.WriteAllText(operationPath, "{\"state\":\"completed\",\"profileId\":\"profile\",\"revision\":7,\"id\":\"generation-a\"}");
    Check(fixture.Service.Plan(request).Tasks.Length == 1, "committed matching generation proves applied identity");
    File.WriteAllText(fixture.RuntimePath, "{\"valid\":true,\"profileId\":\"profile\",\"revision\":7,\"generationId\":\"generation-b\"}");
    Reject(() => fixture.Service.Plan(request), "runtime_drift", "same revision generation drift");
}

// Real guarded OpenRouter compiler path, entirely offline. No key cap, balance, quota or provider HTTP probes.
foreach (var (routeFree, profileFree, projectFree) in new[] { (false, false, true), (true, false, true), (false, true, true), (true, false, false) })
{
    using var fixture = Fixture.OpenRouter(routeFree, profileFree);
    var freeRequest = request with { Policy = request.Policy with { CostPolicy = projectFree ? "free_only" : "configured" } };
    if (!routeFree && !profileFree)
        Reject(() => fixture.Service.Plan(freeRequest), "cost_policy_denied", "hypothetical free override does not change running permissive alias");
    else
    {
        Check(fixture.Service.Plan(freeRequest).Tasks[0].Provider == "openrouter", "already enforced route/profile free-only accepted without live probe");
        var original = File.ReadAllText(fixture.BindingsPath);
        var bindings = JsonNode.Parse(original)!;
        bindings["routes"]!["alias-b"]!["freeOnlyEnforcement"] = null;
        File.WriteAllText(fixture.BindingsPath, bindings.ToJsonString());
        Reject(() => fixture.Service.Plan(freeRequest), "runtime_drift", "missing actual free runtime guard never repaired hypothetically");
    }
}
foreach (var pricing in new[] { "payg", "trial", "subscription", "unknown" })
{
    using var fixture = Fixture.OpenRouter(true, false, pricing, mutateAfterPublish: true);
    Reject(() => fixture.Service.Plan(request with { Revision = fixture.Repository.Snapshot.Revision }), "cost_policy_denied", pricing + " cannot become free via route intent");
}

// Test each required capability independently without turning synthetic evidence into production qualification.
var unqualifiedCompatibility = new ModelCompatibilityCatalog().Resolve(new("unit-route", "unit-alias", "unit-model", "fixture", ["acct"]));
Check(WorkerCapabilityPolicy.Unsupported(unqualifiedCompatibility).Length == 0 &&
    WorkerCapabilityPolicy.Unqualified(unqualifiedCompatibility).SequenceEqual(["functions", "streaming", "toolContinuation"]), "unknown capabilities are unqualified, not known unsupported");
var supportedEvidence = new ModelCapabilityEvidenceDto("supported", "unit_fixture");
var supportedCompatibility = unqualifiedCompatibility with
{
    Capabilities = unqualifiedCompatibility.Capabilities with { Functions = supportedEvidence, Streaming = supportedEvidence, ToolContinuation = supportedEvidence }
};
Check(WorkerCapabilityPolicy.Unsupported(supportedCompatibility).Length == 0 && WorkerCapabilityPolicy.Unqualified(supportedCompatibility).Length == 0,
    "all three supported states leave no unqualified evidence in unit fixture only");
foreach (var capability in new[] { "functions", "streaming", "toolContinuation" })
{
    var supported = new ModelCapabilityEvidenceDto("supported", "unit_fixture");
    var unsupported = new ModelCapabilityEvidenceDto("unsupported", "unit_fixture");
    var capabilities = unqualifiedCompatibility.Capabilities with { Functions = supported, Streaming = supported, ToolContinuation = supported };
    capabilities = capability switch
    {
        "functions" => capabilities with { Functions = unsupported },
        "streaming" => capabilities with { Streaming = unsupported },
        _ => capabilities with { ToolContinuation = unsupported }
    };
    var evidence = unqualifiedCompatibility with { Capabilities = capabilities };
    Check(WorkerCapabilityPolicy.Unsupported(evidence).SequenceEqual([capability]) && WorkerCapabilityPolicy.Unqualified(evidence).SequenceEqual([capability]), "known unsupported " + capability + " independently denied");
}
foreach (var (tools, streaming) in new[] { (false, true), (true, false) })
{
    using var fixture = Fixture.OpenRouter(false, false, tools: tools, streaming: streaming, checkedSource: true);
    var before = fixture.Files();
    Reject(() => fixture.Service.Plan(request), "worker_capability_unsupported", "actual checked route with unsupported native coding capability");
    Check(before.OrderBy(p => p.Key).SequenceEqual(fixture.Files().OrderBy(p => p.Key)), "capability rejection preserves all runtime files");
}
using (var fixture = Fixture.OpenRouter(false, false, tools: false, streaming: false))
{
    var plan = fixture.Service.Plan(request);
    Check(plan.Tasks[0].Compatibility!.Capabilities.Functions.State == "unknown" && plan.Tasks[0].Compatibility!.Capabilities.Streaming.State == "unknown", "unchecked descriptor claims do not become unsupported or supported evidence");
}
using (var fixture = Fixture.OpenRouter(false, false, tools: true, streaming: true, checkedSource: true))
{
    var task = fixture.Service.Plan(request).Tasks[0];
    Check(task.Compatibility!.Capabilities.Functions.State == "supported" && task.Compatibility.Capabilities.Streaming.State == "supported" &&
        task.Compatibility.Capabilities.ToolContinuation.State == "unknown", "documented functions and streaming do not invent tool continuation");
}
using (var fixture = Fixture.OpenRouter(false, false, tools: false, streaming: true, checkedSource: true, alternative: true))
{
    var ordered = fixture.Service.Plan(Fixture.WithRoutes(request, ["route-a", "route-b"]));
    Check(ordered.Tasks[0].RouteId == "route-b" && ordered.Tasks[0].Compatibility!.RouteId == "route-b", "ordered resolution skips known unsupported candidate with correct evidence attached to selected alternative");
    Reject(() => fixture.Service.Plan(Fixture.WithRoutes(request, ["route-a"])), "worker_capability_unsupported", "unsupported-only purpose denied rather than showing executable plan");
}
using (var fixture = Fixture.OpenRouter(false, false, checkedSource: true, alternative: true))
{
    var ordered = fixture.Service.Plan(Fixture.WithRoutes(request, ["route-a", "route-b"]));
    Check(ordered.Tasks[0].RouteId == "route-a" && ordered.Tasks[0].Compatibility!.Capabilities.Functions.State == "unknown",
        "unknown capability evidence retains explicit route order instead of requiring positive qualification");
}

using (var fixture = new Fixture())
{
    var builder = WebApplication.CreateBuilder();
    builder.Logging.ClearProviders();
    builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
    builder.Services.AddSingleton(fixture.Paths);
    builder.Services.AddSingleton(fixture.Models);
    builder.Services.AddSingleton(fixture.Compiler);
    builder.Services.AddSingleton(fixture.CostPolicy);
    builder.Services.AddSingleton(fixture.Registry);
    await using var app = builder.Build();
    app.Use(async (context, next) =>
    {
        if (context.Request.Headers.Authorization != "Bearer fixture-management") { context.Response.StatusCode = 401; return; }
        await next();
    });
    app.MapWorkerEndpoints();
    await app.StartAsync();
    try
    {
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        var url = Api.Prefix + "/workers/plan";
        using var unauthenticated = await client.PostAsJsonAsync(url, request);
        Check(unauthenticated.StatusCode == HttpStatusCode.Unauthorized, "endpoint remains behind caller's management auth middleware");
        client.DefaultRequestHeaders.Authorization = new("Bearer", "fixture-management");
        using var success = await client.PostAsJsonAsync(url, request);
        var httpPlan = await success.Content.ReadFromJsonAsync<WorkerPlan>();
        Check(success.IsSuccessStatusCode && httpPlan!.Tasks[0].Model == "alias-b" && httpPlan.Tasks[0].Compatibility!.Capabilities.ToolContinuation.State == "unknown", "HTTP plan contract carries unknown execution evidence");
        foreach (var body in new[] { validJson.Replace("\"revision\":7", "\"revision\":7,\"revision\":7"), validJson.Replace("\"revision\":7", "\"revision\":7,\"unexpected\":0") })
        {
            using var response = await client.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));
            Check(response.StatusCode == HttpStatusCode.BadRequest && (await response.Content.ReadFromJsonAsync<ApiError>())!.Code == "invalid_request", "HTTP duplicate/unknown key error");
        }
        using var stale = await client.PostAsJsonAsync(url, request with { Revision = 6 });
        Check(stale.StatusCode == HttpStatusCode.Conflict && (await stale.Content.ReadFromJsonAsync<ApiError>())!.Code == "revision_conflict", "HTTP stale revision distinct conflict");
        using var text = await client.PostAsync(url, new StringContent(validJson));
        Check(text.StatusCode == HttpStatusCode.UnsupportedMediaType, "HTTP requires JSON");
        using var oversized = await client.PostAsync(url, new StringContent(new string(' ', WorkerPlanValidation.MaximumRequestBytes + 1), Encoding.UTF8, "application/json"));
        Check(oversized.StatusCode == HttpStatusCode.RequestEntityTooLarge, "HTTP body bound");
    }
    finally { await app.StopAsync(); }
}
Console.WriteLine($"Workers host: {assertions} offline checks passed.");

sealed class Fixture : IDisposable
{
    public OwnStatePaths Paths { get; }
    public SecretStore Secrets { get; }
    public ConfigurationRepository Repository { get; }
    public ModelsService Models { get; }
    public ProxyConfigurationWriter Compiler { get; }
    public ProviderCatalogRegistry Registry { get; }
    public ProviderCostPolicy CostPolicy { get; }
    public WorkerPlanningService Service { get; }
    public string ActivePath => Path.Combine(Paths.Root, "active-profile.json");
    public string RuntimePath => Path.Combine(Paths.Proxy, "runtime.json");
    public string BindingsPath => Path.Combine(Paths.Proxy, "bindings.json");

    public Fixture(Func<ConfigurationDto, ConfigurationDto>? transform = null, bool mutateAfterPublish = false, ProviderCatalogRegistry? registry = null)
    {
        Paths = new(Path.Combine(Path.GetTempPath(), "lain-workers-host-" + Guid.NewGuid().ToString("N")));
        var configuration = new ConfigurationDto(7, 7, [new("acct", "Fixture", "codex", true, "codex-access-snapshot", "unverified")],
            [new("route-a", "alias-b", "gpt-6-astra", "codex", ["acct"]), new("route-b", "alias-a", "gpt-5.5", "codex", ["acct"]),
             new("route-unknown", "unknown", "unqualified-fixture", "codex", ["acct"])], [new("profile", "Fixture", ["route-a", "route-b", "route-unknown"])]);
        if (!mutateAfterPublish && transform is not null) configuration = transform(configuration);
        File.WriteAllText(Paths.Configuration, JsonSerializer.Serialize(configuration));
        Secrets = new(Paths);
        WriteCredentials(configuration);
        Repository = new(Paths);
        Registry = registry ?? new();
        CostPolicy = new(Registry);
        Models = new(Repository, CostPolicy);
        Compiler = new(Paths, Secrets, CostPolicy);
        Service = new(Paths, Models, Compiler, CostPolicy, Registry);
        PublishMetadata();
        var resolved = Models.ResolveForApply("profile", 7);
        var runtime = resolved.Configuration with { Routes = resolved.Routes.Where(r => r.Enabled).ToArray() };
        var description = Compiler.Describe(runtime);
        var bindings = RouteBindings.Compile(runtime, description.CredentialIds, description.RouteCredentials, Registry,
            "profile", description.QualifiedProviderRouteIds, description.TransportBindings);
        File.WriteAllText(BindingsPath, JsonSerializer.Serialize(bindings));
        if (mutateAfterPublish && transform is not null)
        {
            // Negative cases start with real baseline compilation. Only then corrupt the
            // saved/applied test metadata; never ask the fixture compiler to publish invalid state.
            var baseline = Repository.Snapshot;
            var candidate = transform(baseline);
            Repository.Update(baseline.Revision, _ => candidate);
            if (candidate.AppliedRevision == baseline.AppliedRevision) Repository.MarkApplied(Repository.Snapshot.Revision);
            WriteCredentials(Repository.Snapshot);
            PublishMetadata();
        }
    }

    private void WriteCredentials(ConfigurationDto configuration)
    {
        foreach (var account in configuration.Accounts)
            Secrets.WriteAccount(account.Id, account.Provider == "codex"
                ? "{\"kind\":\"codex-access-snapshot\",\"accessToken\":\"fixture-access-do-not-leak\",\"accountId\":\"fixture-owner\"}"
                : "{\"kind\":\"api-key\",\"apiKey\":\"fixture-api-do-not-leak\",\"baseUrl\":\"https://openrouter.ai/api/v1\",\"providerRouteIds\":[\"guarded\",\"alternative\"],\"coreProvider\":\"openrouter-openai-chat\"}");
    }

    public void PublishMetadata()
    {
        var revision = Repository.Snapshot.AppliedRevision;
        File.WriteAllText(ActivePath, JsonSerializer.Serialize(new { id = "profile", revision }));
        File.WriteAllText(RuntimePath, JsonSerializer.Serialize(new { valid = true, revision, profileId = "profile" }));
    }
    public Dictionary<string, string> Files() => Directory.GetFiles(Paths.Root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllText);
    public void Dispose() => Directory.Delete(Paths.Root, recursive: true);
    public static WorkerPlanRequest Request() => new(7, new(1, "profile", 2, [new("code", ["route-a"])]), [new("task-a", "code", "Inspect the explicit task.")]);
    public static WorkerPlanRequest WithRoutes(WorkerPlanRequest request, string[] routeIds, string? effort = null) =>
        request with { Policy = request.Policy with { Purposes = [new("code", routeIds, effort)] } };
    public static Fixture OpenRouter(bool routeFree, bool profileFree, string pricing = "free", bool mutateAfterPublish = false,
        bool? tools = null, bool? streaming = null, bool checkedSource = false, bool alternative = false)
    {
        ProviderSourceDto[] sources = checkedSource ? [new("https://example.test/docs", "Offline checked fixture", DateTimeOffset.Parse("2026-10-24T00:00:00Z"))] : [];
        var template = new ProviderRouteDescriptorDto("guarded", "qwen/qwen3-coder:free", "Fixture", "https://openrouter.ai/api/v1", "openai-chat", ["free"], new(Tools: tools, Streaming: streaming),
            PricingClass: pricing, FreeOnlyEligible: true, Sources: sources, FreeOnlyEnforcementKind: "free_model_pin");
        var descriptor = new ProviderDescriptorDto("openrouter", "Fixture OpenRouter", "Offline fixture", "https://example.test/account", "https://example.test/docs",
            [], [], [new("free", "Free", "free", "account", "requests")],
            alternative ? [template, template with { Id = "alternative", Model = "meta-llama/llama-3.3-70b-instruct:free", Capabilities = new(Tools: true, Streaming: true) }] : [template], [], []);
        return new(c => c with { Accounts = [c.Accounts[0] with { Provider = "openrouter", CredentialKind = "api-key" }],
            Routes = alternative
                ? [new("route-a", "alias-b", "qwen/qwen3-coder:free", "openrouter", ["acct"], ProviderRouteId: "guarded", Policy: new(routeFree ? "free_only" : "configured")),
                   new("route-b", "alias-a", "meta-llama/llama-3.3-70b-instruct:free", "openrouter", ["acct"], ProviderRouteId: "alternative", Policy: new(routeFree ? "free_only" : "configured"))]
                : [new("route-a", "alias-b", "qwen/qwen3-coder:free", "openrouter", ["acct"], ProviderRouteId: "guarded", Policy: new(routeFree ? "free_only" : "configured"))],
            Profiles = [new("profile", "Fixture", alternative ? ["route-a", "route-b"] : ["route-a"], Policy: new(profileFree ? "free_only" : "configured"))] }, mutateAfterPublish, new ProviderCatalogRegistry([descriptor]));
    }
}
