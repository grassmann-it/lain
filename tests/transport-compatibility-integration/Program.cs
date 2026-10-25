using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Models;
using Lain.Host.Operations;
using Lain.Host.Providers;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

var assertions = 0;
void Check(bool condition, string name) { assertions++; if (!condition) throw new Exception(name); }
void Reject(Action action, string name)
{
    try { action(); }
    catch (Exception ex) when (ex is ArgumentException or InvalidDataException or InvalidOperationException or IOException) { assertions++; return; }
    throw new Exception(name);
}
var root = Path.Combine(Path.GetTempPath(), "lain-transport-compatibility-" + Guid.NewGuid().ToString("N"));
var source = root + ".yaml";
try
{
    var paths = new OwnStatePaths(root);
    var secrets = new SecretStore(paths);
    var repository = new ConfigurationRepository(paths);
    var registry = new ProviderCatalogRegistry();
    var writer = new ProxyConfigurationWriter(paths, secrets, new ProviderCostPolicy(registry));
    // Real import/compiler path, with synthetic credentials and misleading public aliases.
    File.WriteAllText(source, """
        openai-compatibility:
          - name: xiaomi-mimo
            base-url: https://fixture.invalid/v1
            api-key-entries:
              - api-key: synthetic-mimo
            models:
              - name: mimo-v2.6-pro
                alias: native-responses-search
              - name: mimo-v2.6-flash
                alias: unrelated-display-name
              - name: unrelated-model
                alias: mimo-v2.6-pro
          - name: commandcode
            base-url: https://fixture.invalid/v1
            api-key-entries:
              - api-key: synthetic-commandcode
            models:
              - name: stealth/glyph-cluster:free
                alias: beam-display-name
              - name: reflection/beam:free
                alias: glyph-display-name
        """);
    new LegacyImportService(paths, repository, secrets).Import(new(repository.Snapshot.Revision, source));
    var imported = repository.Snapshot;
    Check(imported.Routes.Length == 5, "five synthetic imported routes");
    var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllText);
    var description = writer.Describe(imported);
    Check(Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length == before.Count && before.All(p => File.ReadAllText(p.Key) == p.Value), "Describe creates no token/config/auth file and changes no contents");
    Check(writer.TransportBindings.Count == 0, "Describe does not publish mutable runtime state");
    Check(description.QualifiedProviderRouteIds.Count == 0, "imports do not borrow catalog qualification");
    Check(description.TransportBindings.Values.All(b => b.UpstreamProtocol == "openai" && b.Source == "compiled:openai-compatibility"), "imports describe actual Chat executor");
    var compatibility = new ModelCompatibilityCatalog(registry, description.QualifiedProviderRouteIds, description.TransportBindings);
    foreach (var route in imported.Routes)
    {
        var facts = compatibility.Resolve(route);
        Check(facts.UpstreamProtocol == "openai", "imported protocol preserved");
        Check(facts.Capabilities.NativeSearch.State == (route.UpstreamModel is "mimo-v2.6-pro" or "mimo-v2.6-flash" ? "unsupported" : "unknown"), "exact model restriction independent of alias");
        if (route.UpstreamModel == "stealth/glyph-cluster:free")
            Check(facts.Capabilities.NamespaceTools.State == "unsupported" && facts.Capabilities.CustomTools.State == "unsupported", "Glyph restrictions reach export");
        if (route.UpstreamModel == "reflection/beam:free")
        {
            foreach (var count in new[] { 128, 129 })
            {
                var request = JsonSerializer.SerializeToElement(new { tools = Enumerable.Range(0, count).Select(i => new { type = "function", name = "tool_" + i }) });
                var original = request.GetRawText();
                Check(ModelCompatibilityRequestValidator.Validate(facts, request, finalWireRequest: true).Length == (count == 129 ? 1 : 0), "actual top-level tool boundary");
                Check(request.GetRawText() == original, "tools preserved exactly");
            }
        }
    }
    writer.Write(imported);
    Check(writer.TransportBindings.SequenceEqual(description.TransportBindings), "Write and Describe use identical provenance");
    var bindings = RouteBindings.Compile(imported, writer.Describe(imported).CredentialIds, writer.RouteCredentials, transportBindings: writer.TransportBindings);
    Check(bindings.Routes.Values.All(b => b.UpstreamProtocol == "openai"), "Core gets compiled protocol");
    Check(JsonSerializer.Serialize(bindings).Contains("\"upstreamProtocol\":\"openai\""), "Core protocol serialized");
    var models = new ModelsService(repository);
    var profileState = models.PutProfile(repository.Snapshot.Revision, new("fixture-profile", "Fixture", imported.Routes.Select(r => r.Id).ToArray(), imported.Routes[0].Id));
    var resolvedProfile = models.ResolveForApply("fixture-profile", profileState.Revision);
    var exportDescription = writer.Describe(resolvedProfile.Configuration with { Routes = resolvedProfile.Routes });
    var exported = new CodexProfileAdapter(models, new ModelCompatibilityCatalog(registry, exportDescription.QualifiedProviderRouteIds, exportDescription.TransportBindings))
        .Generate("fixture-profile", profileState.Revision, "/fixture/lain", "/fixture/models.json");
    Check(exported.Compatibility!.Routes.All(r => r.UpstreamProtocol == "openai"), "Codex export includes real compiler provenance");
    Check(!JsonSerializer.Serialize(exported).Contains("synthetic-mimo"), "public compatibility export contains no private credential");
    var monitor = new AccountDto("acct-monitor", "Monitor", "fixture", true, "session-token", "unverified", Capabilities: [new("quota", "available")]);
    var unused = new AccountDto("acct-unused", "Unused missing secret", "fixture", true, "api-key", "unverified");
    var withUnused = imported with { Accounts = [.. imported.Accounts, monitor, unused] };
    Check(writer.Describe(withUnused).CredentialIds.Count == imported.Accounts.Length, "unreferenced and monitor accounts are not read");
    var sentinel = Path.Combine(paths.ProxyAuth, "acct-preserve.json");
    OwnStatePaths.WritePrivate(sentinel, "synthetic sentinel");
    var generatedBefore = File.ReadAllText(paths.ProxyConfig);
    var badRoute = new RouteDto("bad", "bad", "model", "fixture", [monitor.Id]);
    Reject(() => writer.Write(withUnused with { Routes = [.. imported.Routes, badRoute] }), "monitor-only inference must fail");
    Reject(() => writer.Write(withUnused with { Routes = [badRoute with { AccountIds = [unused.Id] }] }), "missing inference credential must fail");
    Reject(() => writer.Write(imported with { Routes = [imported.Routes[0] with { AccountIds = [] }] }), "empty route must fail");
    Check(File.ReadAllText(sentinel) == "synthetic sentinel" && File.ReadAllText(paths.ProxyConfig) == generatedBefore, "validation fails before generated auth deletion or config replacement");

    // A successful Describe must not leave quoted retry counts for Core's integer field.
    var retryPaths = new OwnStatePaths(Path.Combine(root, "retry-runtime"));
    var retrySecrets = new SecretStore(retryPaths);
    var retryRepository = new ConfigurationRepository(retryPaths);
    var retryImporter = new LegacyImportService(retryPaths, retryRepository, retrySecrets);
    var retryWriter = new ProxyConfigurationWriter(retryPaths, retrySecrets);
    var retrySource = Path.Combine(root, "retry-source.yaml");
    foreach (var (input, expected) in new[] { ("0", "0"), ("3", "3"), ("'4'", "4"), ("\"5\"", "5"), ("!!str 6", "6"), ("-1", "-1") })
    {
        File.WriteAllText(retrySource, "request-retry: " + input + "\n");
        retryImporter.Import(new(retryRepository.Snapshot.Revision, retrySource));
        retryWriter.Describe(retryRepository.Snapshot);
        retryWriter.Write(retryRepository.Snapshot);
        var yaml = new YamlStream();
        using (var read = new StringReader(File.ReadAllText(retryPaths.ProxyConfig))) yaml.Load(read);
        var retry = (YamlScalarNode)((YamlMappingNode)yaml.Documents.Single().RootNode).Children[new YamlScalarNode("request-retry")];
        var integerType = retry.Tag.IsEmpty ? retry.Style == ScalarStyle.Plain : retry.Tag.Value == "tag:yaml.org,2002:int";
        Check(retry.Value == expected && integerType,
            "compiled retry retains its integer value and YAML type: " + input);
    }
    var retryConfigBefore = File.ReadAllText(retryPaths.ProxyConfig);
    var retrySentinel = Path.Combine(retryPaths.ProxyAuth, "acct-preserve.json");
    OwnStatePaths.WritePrivate(retrySentinel, "synthetic retry sentinel");
    foreach (var input in new[] { "null", "true", "1.5", "\"retries\"", "[1]", "{count: 1}", "9223372036854775808", "!!float 1" })
    {
        File.WriteAllText(retrySource, "request-retry: " + input + "\n");
        retryImporter.Import(new(retryRepository.Snapshot.Revision, retrySource));
        Reject(() => retryWriter.Describe(retryRepository.Snapshot), "Describe rejects malformed retry: " + input);
        Reject(() => retryWriter.Write(retryRepository.Snapshot), "Write rejects malformed retry: " + input);
        Check(File.ReadAllText(retryPaths.ProxyConfig) == retryConfigBefore && File.ReadAllText(retrySentinel) == "synthetic retry sentinel",
            "malformed retry leaves generated runtime/auth unchanged: " + input);
    }

    // Every supported executor derives from its compiled section/switch, never display/provider guesses.
    foreach (var (section, protocolSwitch, expected) in new[] {
        ("openai-compatibility", "", "openai"), ("openai-compatibility", "openai-responses", "responses"),
        ("codex-api-key", "", "responses"), ("claude-api-key", "", "anthropic"), ("gemini-api-key", "", "gemini") })
    {
        var account = new AccountDto("acct-protocol", "Chat display name", "xiaomi-mimo", true, "api-key", "unverified");
        secrets.WriteAccount(account.Id, JsonSerializer.Serialize(new {
            kind = "api-key", apiKey = "synthetic-protocol", sourceSection = section,
            coreProvider = "misleading-openai-responses-name", baseUrl = "https://fixture.invalid/v1",
            sourceConfig = protocolSwitch.Length == 0 ? "{}" : "upstream-protocol: " + protocolSwitch
        }));
        var route = new RouteDto("protocol", "mimo-v2.6-pro", "mimo-v2.6-pro", account.Provider, [account.Id]);
        var state = new ConfigurationDto(0, 0, [account], [route], []);
        var result = writer.Describe(state);
        Check(result.TransportBindings[route.Id].UpstreamProtocol == expected, "compiled executor " + section + protocolSwitch);
        var resolved = new ModelCompatibilityCatalog(transportBindings: result.TransportBindings).Resolve(route);
        Check(resolved.Capabilities.NativeSearch.State == (expected == "openai" ? "unsupported" : "unknown"), "bridge restriction only for compiled Chat");
    }
    // Unknown configured switch cannot silently become Chat.
    secrets.WriteAccount("acct-protocol", JsonSerializer.Serialize(new { kind = "api-key", apiKey = "synthetic", sourceSection = "openai-compatibility", sourceConfig = "upstream-protocol: invented" }));
    var invalid = new ConfigurationDto(0, 0, [new("acct-protocol", "Invalid", "fixture", true, "api-key", "unverified")], [new("invalid", "invalid", "model", "fixture", ["acct-protocol"])], []);
    Reject(() => writer.Describe(invalid), "unknown compiled protocol rejected");
    // Explicit invalid generation never falls back to a legacy key.
    invalid = invalid with { Accounts = [invalid.Accounts[0] with { Capabilities = [new("inference", "unverified", "cred-missing")] }] };
    Reject(() => writer.Describe(invalid), "missing explicit generation rejected");
    // Catalog protocol normalization keeps checked facts only when actual protocol matches.
    var sourceEvidence = new ProviderSourceDto("https://fixture.invalid/docs", "Synthetic source", DateTimeOffset.Parse("2026-10-24T00:00:00Z"));
    var template = new ProviderRouteDescriptorDto("template", "model", "Model", "https://fixture.invalid/v1", "openai-chat", ["plan"], new(Tools: true), Sources: [sourceEvidence]);
    var provider = new ProviderDescriptorDto("fixture", "Fixture", "Fixture", "https://fixture.invalid", "https://fixture.invalid", [], [], [new("plan", "Plan", "unknown", "model", "tokens")], [template], [], []);
    var catalog = new ProviderCatalogRegistry([provider]);
    var qualifiedRoute = new RouteDto("qualified", "display", "model", "fixture", ["acct"], ProviderRouteId: "template");
    ModelCompatibilityDto ResolveProtocol(string protocol) => new ModelCompatibilityCatalog(catalog, new HashSet<string> { qualifiedRoute.Id }, new Dictionary<string, ModelCompatibilityTransportBinding> {
        [qualifiedRoute.Id] = new("fixture", "model", protocol, "compiled:fixture") }).Resolve(qualifiedRoute);
    Check(ResolveProtocol("openai").Capabilities.Functions.State == "supported", "canonical Chat matches catalog openai-chat");
    Check(ResolveProtocol("responses").Capabilities.Functions.State == "unknown", "canonical mismatch discards catalog capability");
    Check(ResolveProtocol("invented").Capabilities.Functions.State == "unknown", "unmapped protocol cannot borrow catalog capability");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, true);
    if (File.Exists(source)) File.Delete(source);
}
Console.WriteLine($"{assertions} transport compatibility integration assertions passed; synthetic local fixtures only.");
