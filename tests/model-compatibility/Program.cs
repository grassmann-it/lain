using System.Text.Json;
using Lain.Core;
using Lain.Host.Models;
using Lain.Host.Operations;
using Lain.Host.Providers;

var assertions = 0;
void Check(bool condition, string name) { assertions++; if (!condition) throw new Exception(name); }
void Reject(Action action, string name) { try { action(); } catch (ArgumentException) { assertions++; return; } throw new Exception(name); }
RouteDto Route(string model, string provider = "commandcode", string? template = null) => new("route", "arbitrary-alias", model, provider, ["fixture"], ProviderRouteId: template);
JsonDocument Request(string json) => JsonDocument.Parse(json);
var catalog = new ModelCompatibilityCatalog();
ModelCompatibilityDto Bound(string model, string protocol = "openai", string? provider = null, string ingress = "responses")
{
    provider ??= model.StartsWith("mimo-", StringComparison.Ordinal) ? "xiaomi-mimo" : "commandcode";
    return catalog.Resolve(Route(model, provider), transport: new(provider, model, protocol, "synthetic:compiled-transport"), ingressProtocol: ingress);
}
var mimo = Bound("mimo-v2.6-pro");
var imported = new ModelCompatibilityCatalog(transportBindings: new Dictionary<string, ModelCompatibilityTransportBinding> {
    ["route"] = new("xiaomi-mimo", "mimo-v2.6-pro", "openai", "synthetic:actual-compiled-route") });
Check(imported.Resolve(Route("mimo-v2.6-pro", "xiaomi-mimo")).Capabilities.NativeSearch.Qualification == "translator_constraint", "compiled protocol facts qualify configured imports without a model template");
Check(imported.Resolve(Route("another-model", "xiaomi-mimo")).Capabilities.NativeSearch.State == "unknown", "compiled protocol binding cannot leak to a changed model");
Check(mimo.ClientVersion == "0.164.0" && mimo.ClientQualification == "local_fixture", "client qualification is exact");
Check(mimo.Capabilities.NativeSearch.State == "unsupported" && mimo.Capabilities.NativeSearch.Qualification == "translator_constraint", "reported failure is not real provider certification");
Check(mimo.Capabilities.Streaming.State == "unknown" && mimo.Capabilities.ContextWindow.Value is null, "unknown streaming/context remains explicit");
Check(catalog.Resolve(Route("mimo-v2.6-pro", "another-provider")).Capabilities.NativeSearch.State == "unknown", "no cross-provider model inheritance");
Check(catalog.Resolve(Route("mimo-v2.6-pro", "xiaomi-mimo"), "0.165.0", new("xiaomi-mimo", "mimo-v2.6-pro", "openai", "synthetic:future-client")).ClientQualification == "unqualified", "future clients not silently qualified");
Check(catalog.Resolve(Route("mimo-v2.6-pro", "xiaomi-mimo"), "0.165.0", new("xiaomi-mimo", "mimo-v2.6-pro", "openai", "synthetic:future-client")).Capabilities.NativeSearch.State == "unsupported", "known bridge restriction survives an unqualified client version");
Check(catalog.Resolve(Route("prefix-mimo-v2.6-pro")).Capabilities.NativeSearch.State == "unknown", "no substring model matching");
using (var request = Request("""{"tools":[{"type":"web_search"}]}"""))
    Check(ModelCompatibilityRequestValidator.Validate(mimo, request.RootElement).Single().Code == "native_search_unsupported", "MiMo search rejects before request preview");
using (var request = Request("""{"tool_choice":{"type":"web_search_preview"}}"""))
    Check(ModelCompatibilityRequestValidator.Validate(mimo, request.RootElement).Single().Code == "native_search_unsupported", "search choice also checked");
using (var request = Request("""{"tools":[{"type":"function","name":"web_search"}]}"""))
    Check(ModelCompatibilityRequestValidator.Validate(mimo, request.RootElement).Length == 0, "function named search is not native search");
var glyph = Bound("stealth/glyph-cluster:free");
using (var request = Request("""{"tools":[{"type":"namespace","name":"agents","tools":[{"type":"function","name":"spawn"}]},{"type":"custom","name":"apply_patch"}]}"""))
{
    var before = request.RootElement.GetRawText();
    var summary = ModelCompatibilityRequestValidator.SummarizeTools(request.RootElement);
    Check(summary.TopLevelToolCount == 2 && summary.TotalDeclarationCount == 3 && summary.Types.SequenceEqual(new[] { "custom", "function", "namespace" }), "count namespace containers and nested tool declarations");
    Check(ModelCompatibilityRequestValidator.Validate(glyph, request.RootElement).Select(e => e.Code).SequenceEqual(new[] { "namespace_tools_unsupported", "custom_tools_unsupported" }), "custom/namespace are distinct restrictions");
    Check(request.RootElement.GetRawText() == before, "request tools are never stripped");
}
Check(catalog.Resolve(Route("stealth/glyph-cluster:free")).Capabilities.NamespaceTools.State == "unknown", "unknown upstream protocol is not a bridge claim");
Check(Bound("stealth/glyph-cluster:free", "responses").Capabilities.NamespaceTools.State == "unknown", "native Responses route does not inherit chat bridge limitation");
var beam = Bound("reflection/beam:free");
foreach (var count in new[] { 128, 129 })
{
    using var request = Request(JsonSerializer.Serialize(new { tools = Enumerable.Range(0, count).Select(i => new { type = "function", name = "tool_" + i }) }));
    Check(ModelCompatibilityRequestValidator.Validate(beam, request.RootElement, finalWireRequest: true).Length == (count == 128 ? 0 : 1), "exact Beam boundary " + count);
}
using (var request = Request(JsonSerializer.Serialize(new { tools = Enumerable.Range(0, 129).Select(i => new { type = "function", name = "preview_" + i }) })))
    Check(ModelCompatibilityRequestValidator.Validate(beam, request.RootElement).Length == 0, "original cardinality is not claimed to equal final transmitted tools");
using (var request = Request(JsonSerializer.Serialize(new { tools = new[] { new { type = "namespace", name = "container", tools = Enumerable.Range(0, 129).Select(i => new { type = "function", name = "nested_" + i }) } } })))
    Check(ModelCompatibilityRequestValidator.Validate(beam, request.RootElement, finalWireRequest: true).Length == 0, "nested declarations do not invent a larger transmitted array");
foreach (var json in new[] { """{"tools":[],"tools":[]}""", """{"tools":[{"type":"function","type":"namespace"}]}""", """{"tools":[{"type":"namespace","tools":{}}]}""" })
{
    using var request = Request(json);
    Reject(() => ModelCompatibilityRequestValidator.Validate(beam, request.RootElement), "ambiguous controls fail closed");
}
var checkedAt = new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
ProviderDescriptorDto Descriptor(ProviderSourceDto[] sources) => new("fixture-provider", "Fixture", "Synthetic documentation only", "https://fixture.invalid/", "https://fixture.invalid/docs", [], [],
    [new("fixture-plan", "Fixture", "unknown", "model", "tokens")],
    [new("template", "fixture-model", "Fixture model", "https://fixture.invalid/v1", "openai", ["fixture-plan"], new(ContextWindow: 8192, MaximumOutput: 512, Tools: true, Streaming: false), Sources: sources)], [], []);
var source = new ProviderSourceDto("https://fixture.invalid/model", "Fixture route documentation", checkedAt);
var qualified = new ModelCompatibilityCatalog(new ProviderCatalogRegistry([Descriptor([source])]), new HashSet<string> { "route" }).Resolve(Route("fixture-model", "fixture-provider", "template"));
Check(qualified.Capabilities.ContextWindow.Value == 8192 && qualified.Capabilities.ContextWindow.Evidence.Qualification == "documentation", "context retains checked source evidence");
Check(qualified.Capabilities.Functions.State == "supported" && qualified.Capabilities.NamespaceTools.State == "unknown", "generic tool claim does not invent namespace capability");
Check(qualified.Capabilities.ToolContinuation.State == "unknown", "tool support is not continuation qualification");
var protocolMismatch = new ModelCompatibilityCatalog(new ProviderCatalogRegistry([Descriptor([source])]), new HashSet<string> { "route" })
    .Resolve(Route("fixture-model", "fixture-provider", "template"), transport: new("fixture-provider", "fixture-model", "responses", "synthetic:changed-protocol"));
Check(protocolMismatch.Capabilities.ContextWindow.Value is null && protocolMismatch.UpstreamProtocol == "responses", "changed protocol cannot borrow descriptor facts");
var mismatch = new ModelCompatibilityCatalog(new ProviderCatalogRegistry([Descriptor([source])]), new HashSet<string> { "route" }).Resolve(Route("another-model", "fixture-provider", "template"));
Check(mismatch.Capabilities.ContextWindow.Value is null && mismatch.UpstreamProtocol is null, "modified import cannot borrow descriptor facts");
var unboundTemplate = new ModelCompatibilityCatalog(new ProviderCatalogRegistry([Descriptor([source])])).Resolve(Route("fixture-model", "fixture-provider", "template"));
Check(unboundTemplate.Capabilities.ContextWindow.Value is null, "template identity alone does not prove bound endpoint capabilities");
var uncheckedSource = new ModelCompatibilityCatalog(new ProviderCatalogRegistry([Descriptor([source with { CheckedAt = null }])]), new HashSet<string> { "route" }).Resolve(Route("fixture-model", "fixture-provider", "template"));
Check(uncheckedSource.Capabilities.ContextWindow.Value is null, "unchecked docs not qualified");
using (var request = Request("""{"stream":true,"max_output_tokens":513}"""))
    Check(ModelCompatibilityRequestValidator.Validate(qualified, request.RootElement).Select(e => e.Code).SequenceEqual(new[] { "streaming_unsupported", "output_limit_exceeded" }), "known output/streaming limits are actionable");
using (var matrix = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "dispatch-matrix.json"))))
{
    foreach (var fixture in matrix.RootElement.GetProperty("cases").EnumerateArray())
    {
        var scoped = Bound(fixture.GetProperty("model").GetString()!, fixture.GetProperty("upstreamProtocol").GetString()!, fixture.GetProperty("provider").GetString()!, fixture.GetProperty("ingressProtocol").GetString()!);
        var rejection = ModelCompatibilityRequestValidator.Validate(scoped, fixture.GetProperty("request"));
        var expected = fixture.GetProperty("expectedCode");
        Check(expected.ValueKind == JsonValueKind.Null ? rejection.Length == 0 : rejection.Single().Code == expected.GetString(), "shared dispatch matrix " + fixture.GetProperty("name").GetString());
    }
}
var temporary = Path.Combine(Path.GetTempPath(), "lain-compatibility-" + Guid.NewGuid().ToString("N"));
try
{
    var repository = new ConfigurationRepository(new OwnStatePaths(temporary));
    var state = repository.Update(0, c => c with { Accounts = [new("fixture", "Fixture", "commandcode", true, "api-key", "unverified")],
        Routes = [Route("mimo-v2.6-pro")], Profiles = [new("fixture-profile", "Fixture", ["route"], "route")] });
    var result = new CodexProfileAdapter(new ModelsService(repository)).Generate("fixture-profile", state.Revision, "/opt/lain/lain", "/tmp/lain.models.json");
    Check(CodexProfileAdapter.VerifiedVersion == "0.164.0" && result.Configuration.Contains("0.164.0"), "export version agrees with qualified client");
    Check(result.Compatibility?.Routes.Single().Capabilities.NativeSearch.State == "unknown", "import with unknown protocol remains unqualified in manifest");
    Check(result.ProfileId == "fixture-profile" && result.GenerationId?.Length == 64, "export identity includes profile and deterministic generation");
    var again = new CodexProfileAdapter(new ModelsService(repository)).Generate("fixture-profile", state.Revision, "/opt/lain/lain", "/tmp/lain.models.json");
    Check(result.GenerationId == again.GenerationId, "identical export generation hash is stable");
    Check(result.Configuration.Contains("web_search = \"disabled\"") && result.Configuration.Contains("model = \"arbitrary-alias\""), "preserve selection and disable native search");
    Check(!result.Configuration.Contains("multi_agent=false") && !result.Configuration.Contains("mcp_servers"), "no implicit loss of client tools");
    using var modelCatalog = JsonDocument.Parse(result.Catalog);
    var model = modelCatalog.RootElement.GetProperty("models")[0];
    Check(!model.TryGetProperty("context_window", out _) && !model.TryGetProperty("max_output_tokens", out _), "unqualified model limits are not invented in catalog");
    Check(repository.Snapshot.Revision == state.Revision, "export is pure");
}
finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
Console.WriteLine($"{assertions} model compatibility assertions passed; no provider access or personal Codex state.");
