using System.Text.Json;
using System.Text.Json.Nodes;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Accounts.Credentials;
using Lain.Host.Operations;

var assertions = 0;
const string Old = "synthetic-original-primary-key-0001";
const string New = "synthetic-replacement-primary-key-0002";
var temp = Path.Combine(Path.GetTempPath(), "lain-static-rotation-" + Guid.NewGuid().ToString("N"));
try
{
    var template = Template();
    var rotated = StaticApiKeyRotation.Rotate(template, New);
    Check(!rotated.Contains(Old, StringComparison.Ordinal), "old key absent from newly issued descriptor, including YAML");
    using (var document = JsonDocument.Parse(rotated))
    {
        Check(document.RootElement.GetProperty("apiKey").GetString() == New, "root uses intended new key");
        var transports = document.RootElement.GetProperty("transports").EnumerateArray().ToArray();
        Check(transports.Length == 3 && transports.Select(t => t.GetProperty("transportId").GetString()).SequenceEqual(["transport-responses", "transport-anthropic", "transport-chat"]), "all original transport identities preserved");
        foreach (var t in transports)
        {
            Check(t.GetProperty("apiKey").GetString() == New, "every transport receives intended new key");
            var yaml = t.GetProperty("sourceConfig").GetString()!;
            Check(!yaml.Contains("api-key:", StringComparison.Ordinal) && !yaml.Contains("api-key-entries:", StringComparison.Ordinal), "old YAML credential slots removed");
            Check(!yaml.Contains("Authorization", StringComparison.Ordinal) && !yaml.Contains("X-Api-Key", StringComparison.Ordinal), "old authenticating headers removed");
            Check(yaml.Contains("synthetic-model-", StringComparison.Ordinal), "model template retained");
        }
        Check(transports[2].GetProperty("sourceConfig").GetString()!.Contains("X-Feature"), "non-authentication header retained");
        Check(transports[2].GetProperty("entryConfig").GetString()!.Contains("priority"), "entry transport settings retained");
    }
    StaticApiKeyRotation.ValidateReconnectOptions(template, "https://provider.example.test/v1", null);
    StaticApiKeyRotation.ValidateReconnectOptions(template, null, new Dictionary<string, string> { ["billingPlan"] = "configured" });
    Reject<ArgumentException>(() => StaticApiKeyRotation.ValidateReconnectOptions(template, "https://different.example.test/v1", null), "endpoint replacement must be explicit");
    Reject<ArgumentException>(() => StaticApiKeyRotation.ValidateReconnectOptions(template, null, new Dictionary<string, string> { ["billingPlan"] = "different" }), "settings replacement must be explicit");
    var unknown = JsonNode.Parse(template)!.AsObject(); unknown["unknownPrivateToken"] = Old;
    Reject<InvalidDataException>(() => StaticApiKeyRotation.Rotate(unknown.ToJsonString(), New), "unknown private additions fail before publication");
    var unrecognized = JsonNode.Parse(template)!.AsObject(); unrecognized["sourceConfig"] = "description: " + Old;
    Reject<InvalidDataException>(() => StaticApiKeyRotation.Rotate(unrecognized.ToJsonString(), New), "unrecognized old-key use fails instead of copying it");
    Reject<ArgumentException>(() => StaticApiKeyRotation.Rotate(template, "sk-nano-mgmt-synthetic"), "management-only token excluded");

    var fixture = new Fixture(Path.Combine(temp, "reconnect"));
    var initial = Seed(fixture);
    var account = initial.Accounts.Single();
    var original = fixture.Secrets.ReadAccount(account.Id);
    var writer = new ProxyConfigurationWriter(fixture.Paths, fixture.Secrets);
    var before = writer.Describe(initial);
    var request = new ConnectCredentialRequest(new("test", "api-key", ["inference"], AccountId: account.Id,
        ExpectedGeneration: account.Generation), New, BaseUrl: "https://provider.example.test/v1");
    var reconnected = fixture.Accounts.ConnectCredential(initial.Revision, request);
    var after = reconnected.Accounts.Single();
    var selected = fixture.Secrets.ReadAccount(after.Id);
    Check(!selected.Contains(Old) && selected.Contains(New), "same-account reconnect publishes cleaned new transport descriptor");
    Check(after.Id == account.Id && after.Name == account.Name && after.Enabled == account.Enabled && after.BudgetPoolId == account.BudgetPoolId, "identity and custom metadata preserved");
    Check(JsonSerializer.Serialize(initial.Routes) == JsonSerializer.Serialize(reconnected.Routes), "route bindings unchanged");
    var compiled = writer.Describe(reconnected);
    Check(compiled.TransportBindings.Count == 3 && compiled.TransportBindings.OrderBy(p => p.Key).Select(p => p.Value.UpstreamProtocol)
        .SequenceEqual(before.TransportBindings.OrderBy(p => p.Key).Select(p => p.Value.UpstreamProtocol)), "all routes retain Responses/Anthropic/Chat protocol mappings");
    Check(compiled.TransportBindings.OrderBy(p => p.Key).Select(p => p.Value.UpstreamProtocol).SequenceEqual(["responses", "anthropic", "openai"]), "compiled protocols explicitly match all three intended shapes");
    writer.Write(reconnected);
    var generated = File.ReadAllText(fixture.Paths.ProxyConfig);
    Check(!generated.Contains(Old, StringComparison.Ordinal) && generated.Contains(New, StringComparison.Ordinal), "generated runtime config uses new key without old auth remnants");
    Check(generated.Contains("X-Feature", StringComparison.Ordinal), "generated config keeps non-authentication transport headers");
    Check(after.Capabilities!.Single(c => c.Purpose == "inference").CredentialId != account.Capabilities!.Single(c => c.Purpose == "inference").CredentialId, "requested inference moves to new immutable generation");
    foreach (var purpose in new[] { "quota", "models" })
        Check(after.Capabilities!.Single(c => c.Purpose == purpose).CredentialId == account.Capabilities!.Single(c => c.Purpose == purpose).CredentialId, "explicit purpose reconnect does not broaden: " + purpose);
    Check(fixture.Secrets.Generations.Read(account.Id, account.Credentials!.Single()).PrivateJson == original, "previous immutable generation is never rewritten");
    var publicBefore = JsonSerializer.Serialize(fixture.Repository.Snapshot);
    Reject<RevisionConflictException>(() => fixture.Accounts.ConnectCredential(initial.Revision, request), "stale revision rejected");
    Reject<InvalidOperationException>(() => fixture.Accounts.ConnectCredential(reconnected.Revision, request), "stale expected generation rejected");
    Check(JsonSerializer.Serialize(fixture.Repository.Snapshot) == publicBefore, "stale reconnect does not publish");
    var generationsBefore = GenerationFiles(fixture);
    Reject<ArgumentException>(() => fixture.Accounts.ConnectCredential(reconnected.Revision, request with
        { Connection = request.Connection with { ExpectedGeneration = after.Generation }, BaseUrl = "https://different.example.test/v1" }), "same-account endpoint conflict rejected");
    Check(GenerationFiles(fixture) == generationsBefore, "transport conflict rejected before immutable preparation");

    var cohort = new Fixture(Path.Combine(temp, "cohort"));
    var cohortState = Seed(cohort);
    var oldAccount = cohortState.Accounts.Single();
    var quotaJson = JsonSerializer.Serialize(new { kind = "api-key", apiKey = "synthetic-independent-monitor-key", provider = "test" });
    cohortState = cohort.Accounts.Connect(cohortState.Revision, new("test", "separate-monitor", ["quota"], AccountId: oldAccount.Id),
        new("static-key", ["quota"], new("test"), quotaJson));
    var independentQuota = cohortState.Accounts.Single().Capabilities!.Single(c => c.Purpose == "quota").CredentialId;
    var rotatedState = cohort.Accounts.ReplaceApiCredential(cohortState.Revision, oldAccount.Id, New);
    var caps = rotatedState.Accounts.Single().Capabilities!;
    Check(caps.Single(c => c.Purpose == "inference").CredentialId == caps.Single(c => c.Purpose == "models").CredentialId, "explicit rotation moves shared inference/models cohort");
    Check(caps.Single(c => c.Purpose == "quota").CredentialId == independentQuota, "independent quota binding preserved");
    var resolver = new CredentialResolver(cohort.Repository, cohort.Secrets);
    Check(resolver.Resolve(oldAccount.Id, "models")!.PrivateJson.Contains(New), "models resolution uses new key after explicit shared-cohort rotation");
    Check(!resolver.Resolve(oldAccount.Id, "inference")!.PrivateJson.Contains(Old), "inference resolution never resurrects old key");
    Check(resolver.Resolve(oldAccount.Id, "quota")!.PrivateJson.Contains("synthetic-independent-monitor-key"), "monitor keeps its own intended key");

    var missing = new Fixture(Path.Combine(temp, "missing"));
    var missingState = Seed(missing);
    var target = missingState.Accounts.Single();
    var handle = target.Credentials!.Single();
    File.Delete(Path.Combine(missing.Paths.Credentials, "generations", handle.Id, "1.json"));
    Reject<FileNotFoundException>(() => missing.Accounts.ConnectCredential(missingState.Revision,
        new(new("test", "api-key", ["inference"], AccountId: target.Id), New)), "missing referenced generation cannot use legacy or historical fallback");

    var verified = new Fixture(Path.Combine(temp, "verified"));
    var verifiedState = Seed(verified);
    verifiedState = verified.Repository.Update(verifiedState.Revision, s => s with
    { Accounts = s.Accounts.Select(a => a with { Binding = new("test", ResourceType: "project", ResourceId: "confirmed", Provenance: "verified") }).ToArray() });
    var verifiedAccount = verifiedState.Accounts.Single();
    Reject<ArgumentException>(() => verified.Accounts.ConnectCredential(verifiedState.Revision,
        new(new("test", "api-key", ["inference"], AccountId: verifiedAccount.Id), New)), "declared reconnect cannot bypass verified identity binding");

    var save = new Fixture(Path.Combine(temp, "save-failure"));
    var saveState = Seed(save);
    var saveTarget = saveState.Accounts.Single();
    var existingJson = save.Secrets.ReadAccount(saveTarget.Id);
    var savedConfig = save.Paths.Configuration + ".saved";
    File.Move(save.Paths.Configuration, savedConfig); Directory.CreateDirectory(save.Paths.Configuration);
    try { Reject<IOException>(() => save.Accounts.ConnectCredential(saveState.Revision,
        new(new("test", "api-key", ["inference"], AccountId: saveTarget.Id), New)), "failed public save surfaces"); }
    finally { Directory.Delete(save.Paths.Configuration); File.Move(savedConfig, save.Paths.Configuration); }
    Check(save.Secrets.ReadAccount(saveTarget.Id) == existingJson && JsonSerializer.Serialize(save.Repository.Snapshot) == JsonSerializer.Serialize(saveState), "failed save leaves previous generation active");
    Console.WriteLine($"Static API-key rotation: {assertions} assertions passed (offline synthetic state only).");
}
finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }

void Check(bool condition, string message) { assertions++; if (!condition) throw new Exception(message); }
void Reject<T>(Action action, string message) where T : Exception
{ try { action(); } catch (T) { assertions++; return; } throw new Exception(message); }
int GenerationFiles(Fixture fixture) => Directory.GetFiles(Path.Combine(fixture.Paths.Credentials, "generations"), "*.json", SearchOption.AllDirectories).Length;
ConfigurationDto Seed(Fixture fixture)
{
    var state = fixture.Accounts.Connect(0, new("test", "seed", ["inference", "models", "quota"], Name: "My saved custom account"),
        new("static-key", ["inference", "models", "quota"], new("test"), Template()));
    var account = state.Accounts.Single();
    return fixture.Repository.Update(state.Revision, s => s with
    {
        Accounts = s.Accounts.Select(a => a with { BudgetPoolId = "keep-pool" }).ToArray(),
        Routes = Enumerable.Range(0, 3).Select(i => new RouteDto("route-" + i, "alias-" + i, "synthetic-model-" + i, "test", [account.Id])).ToArray()
    });
}
string Template()
{
    JsonObject Transport(string id, string section, int index, bool headers)
    {
        var yaml = "base-url: https://provider.example.test/v1\napi-key: " + Old + "\napi-key-entries:\n  - api-key: " + Old + "\n" +
            (headers ? "headers:\n  Authorization: Bearer " + Old + "\n  X-Api-Key: " + Old + "\n  X-Feature: fixture-v1\n" : "") +
            "models:\n  - name: synthetic-model-" + index + "\n    alias: original-alias-" + index + "\n";
        return new JsonObject { ["kind"] = "api-key", ["apiKey"] = Old, ["provider"] = "test", ["coreProvider"] = "test",
            ["sourceSection"] = section, ["transportId"] = id, ["baseUrl"] = "https://provider.example.test/v1",
            ["sourceConfig"] = yaml, ["entryConfig"] = "api-key: " + Old + "\npriority: 2\n",
            ["settings"] = new JsonObject { ["billingPlan"] = "configured" }, ["sourceEnabled"] = true };
    }
    var first = Transport("transport-responses", "codex-api-key", 0, false);
    var second = Transport("transport-anthropic", "claude-api-key", 1, false);
    var third = Transport("transport-chat", "openai-compatibility", 2, true);
    var root = first.DeepClone().AsObject();
    root["transports"] = new JsonArray(first, second, third);
    return root.ToJsonString();
}
sealed class Fixture
{
    public OwnStatePaths Paths { get; }
    public SecretStore Secrets { get; }
    public ConfigurationRepository Repository { get; }
    public AccountService Accounts { get; }
    public Fixture(string path) { Paths = new(path); Secrets = new(Paths); Repository = new(Paths); Accounts = new(Repository, Secrets); }
}
