using System.Text.Json;
using System.Security.Cryptography;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Operations;

var testRoot = Path.Combine(Path.GetTempPath(), "lain-accounts-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
try
{
    var paths = new OwnStatePaths(Path.Combine(testRoot, "own"));
    var secrets = new SecretStore(paths);
    var configuration = new ConfigurationRepository(paths);
    var importer = new LegacyImportService(paths, configuration, secrets);
    var legacy = Path.Combine(testRoot, "legacy"); Directory.CreateDirectory(legacy);
    var configPath = Path.Combine(legacy, "config.yaml");
    var authPath = Path.Combine(legacy, "codex.json");
    File.WriteAllText(configPath, """
        port: 8317
        api-keys: [legacy-client-secret]
        routing:
          strategy: round-robin
        payload:
          filter:
            - models:
                - name: cmd-*
                  protocol: codex
              params: [include, reasoning.encrypted_content]
        oauth-model-alias:
          codex:
            - name: gpt-test
              alias: my-codex
              visible: false
        openai-compatibility:
          - name: command-code
            base-url: https://example.test/v1
            disabled: true
            api-key-entries:
              - api-key: upstream-secret
                label: Primary API
                proxy-url: http://127.0.0.1:19001
            models:
              - name: upstream-model
                alias: short-model
        """);
    File.WriteAllText(authPath, """{"type":"codex","display_name":"Personal Codex","email":"person@example.test","access_token":"access-secret","account_id":"shared-account","refresh_token":"NEVER-COPY-ME","id_token":"NEVER-COPY-ID","expired":"2099-01-01T00:00:00Z","disabled":false}""");
    var before = Directory.GetFiles(legacy).ToDictionary(p => p, p => SHA256.HashData(File.ReadAllBytes(p)));
    var first = importer.Import(new(0, configPath, legacy));
    Check(first.Added == 2, "API and Codex snapshot accounts imported");
    Check(configuration.Snapshot.Accounts.Any(a => a.Provider == "commandcode" && !a.Enabled), "disabled preserved");
    Check(configuration.Snapshot.Accounts.Any(a => a.Name == "Primary API") && configuration.Snapshot.Accounts.Any(a => a.Name == "Personal Codex"), "explicit API and Codex account names imported");
    Check(configuration.Snapshot.Accounts.All(a => a.SharedWithLegacy), "shared source disclosed");
    var again = importer.Import(new(first.Revision, configPath, legacy));
    Check(again.Revision == first.Revision && again.Added == 0 && again.Updated == 0, "replay is idempotent");
    foreach (var source in before) Check(SHA256.HashData(File.ReadAllBytes(source.Key)).SequenceEqual(source.Value), "source remains byte identical");
    foreach (var file in Directory.GetFiles(paths.Root, "*", SearchOption.AllDirectories))
    {
        var text = File.ReadAllText(file);
        Check(!text.Contains("NEVER-COPY-ME") && !text.Contains("NEVER-COPY-ID"), "OAuth refresh and ID tokens never copied");
        if (!OperatingSystem.IsWindows()) Check((File.GetUnixFileMode(file) & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) == 0, "private file permissions");
    }
    Check(configuration.Snapshot.Routes.Length == 2 && configuration.Snapshot.Routes.Any(r => r.Alias == "short-model"), "model aliases imported");
    Check(configuration.Snapshot.Routes.Any(r => r.Alias == "my-codex" && !r.Visible), "Codex OAuth alias and visibility imported");
    var compiled = new ProxyConfigurationWriter(paths, secrets).Write(configuration.Snapshot with { Accounts = configuration.Snapshot.Accounts.Select(a => a with { Enabled = true }).ToArray() });
    var ownConfig = File.ReadAllText(paths.ProxyConfig);
    var importedApi = configuration.Snapshot.Accounts.Single(a => a.CredentialKind == "api-key");
    var expectedCoreId = "openai-compatibility:command-code:" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("openai-compatibility:command-code\0upstream-secret\0https://example.test/v1\0http://127.0.0.1:19001"))).ToLowerInvariant()[..12];
    Check(compiled[importedApi.Id] == expectedCoreId, "per-entry transport participates in credential identity");
    Check(compiled.Count == 2 && ownConfig.Contains("proxy-url: http://127.0.0.1:19001") && ownConfig.Contains("18417"), "own runtime preserves per-entry proxy and uses own port");
    Check(!ownConfig.Contains("legacy-client-secret"), "inference uses an independent credential");
    Check(ownConfig.Contains("payload:") && ownConfig.Contains("name: cmd-*") && ownConfig.Contains("reasoning.encrypted_content"), "legacy payload rules and original route patterns preserved at runtime");
    Check(ownConfig.Contains("alias: upstream-model"), "core-private model names normalized");
    var publicJson = JsonSerializer.Serialize(configuration.Snapshot);
    Check(!publicJson.Contains("upstream-secret") && !publicJson.Contains("access-secret") && !publicJson.Contains("legacy-client-secret"), "public DTOs secret free");
    var hostToken = secrets.GetOrCreateToken(paths.ApiToken);
    var inferenceToken = secrets.GetOrCreateToken(paths.InferenceToken);
    Check(hostToken != inferenceToken && hostToken == secrets.GetOrCreateToken(paths.ApiToken), "independent stable tokens");
    var clone = configuration.Snapshot; clone.Accounts[0] = clone.Accounts[0] with { Name = "mutated" };
    Check(configuration.Snapshot.Accounts[0].Name != "mutated", "snapshot cannot mutate repository");
    try { configuration.Update(0, x => x); throw new Exception("Expected revision conflict."); } catch (RevisionConflictException) { }
    var reloaded = new ConfigurationRepository(paths);
    Check(reloaded.Snapshot.Revision == first.Revision && reloaded.Snapshot.Accounts.Length == 2, "configuration survives restart");
    var service = new AccountService(configuration, secrets);
    try { service.AddApi(configuration.Snapshot.Revision, null!); throw new Exception("Expected null API account rejection."); } catch (ArgumentNullException) { }
    var apiAccount = configuration.Snapshot.Accounts.Single(a => a.CredentialKind == "api-key");
    var replaced = service.ReplaceApiCredential(first.Revision, apiAccount.Id, "rotated-secret");
    Check(replaced.Accounts.Any(a => a.Id == apiAccount.Id), "explicit credential rotation retains stable account identity");
    try { service.AddApi(replaced.Revision, new("duplicate", apiAccount.Provider, "rotated-secret", "https://example.test/v1")); throw new Exception("Expected duplicate rejection."); } catch (ArgumentException) { }
    var priorApiRoute = replaced.Routes.Single(r => r.Alias == "short-model");
    var priorCodexRoute = replaced.Routes.Single(r => r.Alias == "my-codex");
    File.WriteAllText(configPath, File.ReadAllText(configPath).Replace("alias: short-model", "alias: renamed-model").Replace("name: gpt-test", "name: gpt-next"));
    var changedSourceHash = SHA256.HashData(File.ReadAllBytes(configPath));
    var refreshed = importer.Import(new(replaced.Revision, configPath, legacy));
    Check(configuration.Snapshot.Routes.Single(r => r.Id == priorApiRoute.Id).Alias == "renamed-model", "reimport updates alias while preserving imported route identity");
    Check(configuration.Snapshot.Routes.Single(r => r.Id == priorCodexRoute.Id).UpstreamModel == "gpt-next", "reimport updates upstream model while preserving route identity");
    Check(SHA256.HashData(File.ReadAllBytes(configPath)).SequenceEqual(changedSourceHash), "reimport remains read-only");
    var manual = service.AddApi(refreshed.Revision, new("manual", "manual-provider", "manual-secret", "https://manual.test/v1"));
    var manualAccount = manual.Accounts.Single(a => a.Name == "manual");
    var manualRoute = new RouteDto("route-manual", "manual-alias", "manual-upstream", manualAccount.Provider, [manualAccount.Id]);
    new ProxyConfigurationWriter(paths, secrets).Write(manual with { Accounts = [manualAccount], Routes = [manualRoute] });
    Check(File.ReadAllText(paths.ProxyConfig).Contains("name: manual-upstream"), "manual API accounts receive selected core model declarations");
    Check(AccountService.CanonicalProvider("codex", "https://api.kimi.ai/v1") == "kimi", "Kimi endpoint attributed independently of protocol");
    Check(AccountService.CanonicalProvider("codex", "https://api.z.ai/api/coding/paas/v4") == "zai", "Z.ai endpoint attributed independently of protocol");
    Check(AccountService.CanonicalProvider("codex", "https://token-plan-ams.xiaomimimo.com/v1") == "xiaomi-mimo", "Xiaomi MiMo endpoint attributed independently of protocol");
    Check(AccountService.CanonicalProvider("codex", "https://unknown-provider.test/v1") == "unknown-provider.test", "unknown direct upstream uses explicit endpoint host");
    Check(AccountService.CanonicalProvider("codex", "https://api.kimi.ai.attacker.test/v1") == "api.kimi.ai.attacker.test", "provider domain mapping requires host boundary");
    Check(AccountService.CanonicalProvider("codex", "https://api.kimi.ai/v1") == AccountService.CanonicalProvider("openai-compatibility", "https://api.kimi.ai/v1"), "known upstream canonical identity survives protocol changes");
    var attributionPaths = new OwnStatePaths(Path.Combine(testRoot, "attribution"));
    var attributionSecrets = new SecretStore(attributionPaths); var attributionRepository = new ConfigurationRepository(attributionPaths);
    var attributionConfig = Path.Combine(legacy, "attribution.yaml");
    File.WriteAllText(attributionConfig, """
        codex-api-key:
          - api-key: kimi-shared
            base-url: https://api.kimi.ai/v1
          - api-key: zai-key
            base-url: https://api.z.ai/v1
          - api-key: mimo-key
            base-url: https://token-plan-ams.xiaomimimo.com/v1
          - api-key: unknown-key
            base-url: https://unidentified.test/v1
        openai-compatibility:
          - name: named-kimi-transport
            label: Work Kimi
            base-url: https://api.kimi.ai/v1
            api-key-entries:
              - api-key: kimi-shared
        """);
    var attributionImporter = new LegacyImportService(attributionPaths, attributionRepository, attributionSecrets);
    attributionImporter.Import(new(0, attributionConfig));
    var attribution = attributionRepository.Snapshot;
    Check(attribution.Accounts.Length == 4 && attribution.Accounts.All(a => a.Provider != "codex"), "direct API protocol is not presented as upstream provider");
    var kimiAccount = attribution.Accounts.Single(a => a.Provider == "kimi");
    Check(kimiAccount.Name == "Work Kimi", "merged transport prefers explicit account label over protocol fallback");
    using (var kimiCredential = JsonDocument.Parse(attributionSecrets.ReadAccount(kimiAccount.Id)))
        Check(kimiCredential.RootElement.GetProperty("transports").GetArrayLength() == 2, "known provider same key merges across protocol transports");
    var attributedReplay = attributionImporter.Import(new(attribution.Revision, attributionConfig));
    Check(attributedReplay.Revision == attribution.Revision && attributionRepository.Snapshot.Accounts.Single(a => a.Provider == "kimi").Id == kimiAccount.Id, "canonical provider import identity remains stable");
    var multiPaths = new OwnStatePaths(Path.Combine(testRoot, "multi"));
    var multiSecrets = new SecretStore(multiPaths);
    var multiRepository = new ConfigurationRepository(multiPaths);
    var multiConfigPath = Path.Combine(legacy, "multi.yaml");
    File.WriteAllText(multiConfigPath, """
        request-retry: 1
        disable-image-generation: passthrough
        claude-api-key:
          - api-key: shared-key
            base-url: https://api.commandcode.ai/provider
            models:
              - name: claude-test
                alias: cmd-claude-test
        codex-api-key:
          - api-key: shared-key
            base-url: https://api.commandcode.ai/provider/v1
            models:
              - name: gpt-6-luna
                alias: cmd-gpt-6-luna
        openai-compatibility:
          - name: command-code
            base-url: https://api.commandcode.ai/v1
            api-key-entries:
              - api-key: shared-key
            models:
              - name: qwen-test
                alias: cmd-qwen
              - name: ling-test
                alias: cmd-ling
              - name: gpt-6-luna
                alias: cmd-gpt-chat
        """);
    var multiImporter = new LegacyImportService(multiPaths, multiRepository, multiSecrets);
    var multiImported = multiImporter.Import(new(0, multiConfigPath));
    var multiState = multiRepository.Snapshot;
    Check(multiState.Accounts.Length == 1 && multiState.Accounts[0].Provider == "commandcode", "same Command key across transport protocols remains one logical account");
    Check(multiState.Routes.Length == 5, "Responses and Qwen/Ling/chat aliases all preserved");
    using (var multiSecret = JsonDocument.Parse(multiSecrets.ReadAccount(multiState.Accounts[0].Id)))
        Check(multiSecret.RootElement.GetProperty("transports").GetArrayLength() == 3, "private transport definitions remain separate");
    var multiWriter = new ProxyConfigurationWriter(multiPaths, multiSecrets);
    multiWriter.Write(multiState);
    var logicalAccountId = multiState.Accounts[0].Id;
    var responsesRoute = multiState.Routes.Single(r => r.Alias == "cmd-gpt-6-luna");
    var claudeRoute = multiState.Routes.Single(r => r.Alias == "cmd-claude-test");
    var qwenRoute = multiState.Routes.Single(r => r.Alias == "cmd-qwen");
    var chatRoute = multiState.Routes.Single(r => r.Alias == "cmd-gpt-chat");
    Check(multiWriter.RouteCredentials[responsesRoute.Id][logicalAccountId].StartsWith("codex:apikey:"), "Responses route pins direct Codex transport");
    var expectedClaudeId = "claude:apikey:" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("claude:apikey\0shared-key\0https://api.commandcode.ai/provider\0\0\0"))).ToLowerInvariant()[..12];
    Check(multiWriter.RouteCredentials[claudeRoute.Id][logicalAccountId] == expectedClaudeId, "Claude API transport preserves exact synthesizer credential identity");
    var multiRuntime = File.ReadAllText(multiPaths.ProxyConfig);
    Check(multiRuntime.Contains("claude-api-key:") && multiRuntime.Contains("request-retry: 1") && multiRuntime.Contains("disable-image-generation: passthrough"), "runtime preserves qualified Claude API and explicit retry/image settings");
    Check(multiWriter.RouteCredentials[qwenRoute.Id][logicalAccountId].StartsWith("openai-compatibility:command-code:"), "Qwen route pins original chat transport");
    Check(multiWriter.RouteCredentials[chatRoute.Id][logicalAccountId] != multiWriter.RouteCredentials[responsesRoute.Id][logicalAccountId], "shared model names retain distinct route transports");
    var multiReplay = multiImporter.Import(new(multiImported.Revision, multiConfigPath));
    Check(multiReplay.Revision == multiImported.Revision && multiRepository.Snapshot.Accounts.Length == 1, "multi-transport import replay is idempotent");
    var ambiguous = new RouteDto("route-ambiguous", "ambiguous", "gpt-6-luna", "commandcode", [logicalAccountId]);
    try { multiWriter.Write(multiState with { Routes = [ambiguous] }); throw new Exception("Expected ambiguous transport rejection."); } catch (InvalidOperationException) { }
    var migratedPaths = new OwnStatePaths(Path.Combine(testRoot, "migrated"));
    var migratedSecrets = new SecretStore(migratedPaths);
    var migratedRepository = new ConfigurationRepository(migratedPaths);
    migratedSecrets.WriteAccount("legacy-direct", JsonSerializer.Serialize(new { kind = "api-key", apiKey = "shared-key", baseUrl = "https://api.commandcode.ai/provider/v1" }));
    migratedSecrets.WriteAccount("legacy-chat", JsonSerializer.Serialize(new { kind = "api-key", apiKey = "shared-key", baseUrl = "https://api.commandcode.ai/v1" }));
    migratedRepository.Update(0, state => state with
    {
        Accounts = [new("legacy-direct", "codex", "codex", true, "api-key", "unverified"), new("legacy-chat", "command-code", "command-code", true, "api-key", "unverified")],
        Routes = [new("legacy-route-gpt", "cmd-gpt-6-luna", "gpt-6-luna", "codex", ["legacy-direct"]), new("legacy-route-qwen", "cmd-qwen", "qwen-test", "command-code", ["legacy-chat"])]
    });
    OwnStatePaths.WritePrivate(Path.Combine(migratedPaths.Imports, "routes.json"), """{"legacy-route-gpt":{"AccountId":"legacy-direct","Alias":"cmd-gpt-6-luna","Model":"gpt-6-luna"},"legacy-route-qwen":{"AccountId":"legacy-chat","Alias":"cmd-qwen","Model":"qwen-test"}}""");
    new LegacyImportService(migratedPaths, migratedRepository, migratedSecrets).Import(new(1, multiConfigPath));
    var migrated = migratedRepository.Snapshot;
    Check(migrated.Accounts.Length == 1 && migrated.Accounts[0].Id == "legacy-direct" && migrated.Accounts[0].Provider == "commandcode", "canonical migration retains an existing stable identity");
    Check(migrated.Routes.Length == 5 && migrated.Routes.Any(r => r.Id == "legacy-route-gpt") && migrated.Routes.Any(r => r.Id == "legacy-route-qwen") && migrated.Routes.All(r => r.AccountIds.SequenceEqual(new[] { "legacy-direct" })), "canonical migration preserves route identities and remaps logical account references");
    var migratedWriter = new ProxyConfigurationWriter(migratedPaths, migratedSecrets); migratedWriter.Write(migrated);
    Check(migratedWriter.RouteCredentials["legacy-route-qwen"]["legacy-direct"].StartsWith("openai-compatibility:"), "migrated route transport remains chat");
    var emptyAliasConfig = Path.Combine(legacy, "empty-alias.yaml"); File.WriteAllText(emptyAliasConfig, "oauth-model-alias: {}\noauth-excluded-models:\n  codex: [hidden-*]\n");
    var catalogPaths = new OwnStatePaths(Path.Combine(testRoot, "catalog")); var catalogRepository = new ConfigurationRepository(catalogPaths);
    new LegacyImportService(catalogPaths, catalogRepository, new SecretStore(catalogPaths), ["gpt-qualified", "hidden-qualified"]).Import(new(0, emptyAliasConfig, legacy));
    Check(catalogRepository.Snapshot.Routes.Length == 1 && catalogRepository.Snapshot.Routes[0].Alias == "gpt-qualified", "qualified Codex catalog seeds empty aliases while preserving exclusions");
    var ownedPaths = new OwnStatePaths(Path.Combine(testRoot, "owned-login"));
    var ownedSecrets = new SecretStore(ownedPaths); var ownedRepository = new ConfigurationRepository(ownedPaths);
    var ownedBackend = new FakeCodexOwnedBackend(ownedPaths);
    var ownedAdapter = new CodexOwnedLoginAdapter(ownedBackend, ownedPaths, ownedRepository, ownedSecrets, ["gpt-qualified"]);
    var ownedLogins = new LoginOperationService([ownedAdapter]);
    var pendingLogin = await ownedLogins.BeginAsync("codex");
    Check(pendingLogin.State == "waiting" && pendingLogin.Url!.StartsWith("https://auth.openai.com/"), "own Codex login begins with authorization URL");
    Check((await ownedLogins.StatusAsync(pendingLogin.Id)).State == "waiting", "own login pending state preserved");
    var cancelledLogin = await ownedLogins.CancelAsync(pendingLogin.Id);
    Check(cancelledLogin.State == "cancelled" && cancelledLogin.Url is null && ownedRepository.Snapshot.Accounts.Length == 0, "cancelled own login adopts no credentials");
    ownedBackend.Complete(ownedBackend.LatestState, "owned-provider-account");
    Check((await ownedLogins.StatusAsync(pendingLogin.Id)).State == "cancelled" && ownedRepository.Snapshot.Accounts.Length == 0, "late callback after cancellation cannot adopt credentials");
    var successfulLogin = await ownedLogins.BeginAsync("codex"); ownedBackend.Complete(ownedBackend.LatestState, "owned-provider-account");
    var completedLogin = await ownedLogins.StatusAsync(successfulLogin.Id);
    Check(completedLogin.State == "completed" && completedLogin.Url is null, "own login completes");
    var ownedAccount = ownedRepository.Snapshot.Accounts.Single();
    Check(ownedAccount.CredentialKind == "codex-owned" && !ownedAccount.SharedWithLegacy && ownedRepository.Snapshot.Routes.Length == 1, "own account and pinned catalog routes adopted");
    var descriptorText = File.ReadAllText(Path.Combine(ownedPaths.Credentials, ownedAccount.Id + ".json"));
    Check(!descriptorText.Contains("owned-access") && !descriptorText.Contains("owned-refresh"), "Host persists ownership descriptor without copying core credentials");
    var hydrated = ownedSecrets.ReadAccount(ownedAccount.Id);
    Check(hydrated.Contains("owned-access-one") && !hydrated.Contains("owned-refresh"), "internal quota credential hydration excludes refresh material");
    var ownedFile = Path.Combine(ownedPaths.OwnedAuth, ownedBackend.LatestFilename);
    File.WriteAllText(ownedFile, File.ReadAllText(ownedFile).Replace("owned-access-one", "owned-access-two").Replace("2099-01-01", "2099-02-01"));
    Check(ownedSecrets.ReadAccount(ownedAccount.Id).Contains("owned-access-two"), "core-owned token rotation remains authoritative");
    var ownedHash = SHA256.HashData(File.ReadAllBytes(ownedFile)); var ownedWriter = new ProxyConfigurationWriter(ownedPaths, ownedSecrets);
    var ownedCoreIds = ownedWriter.Write(ownedRepository.Snapshot);
    Check(ownedCoreIds[ownedAccount.Id] == "owned/" + ownedBackend.LatestFilename && SHA256.HashData(File.ReadAllBytes(ownedFile)).SequenceEqual(ownedHash), "runtime apply binds and preserves owned credential source");
    var publicOwned = JsonSerializer.Serialize(ownedRepository.Snapshot) + JsonSerializer.Serialize(completedLogin);
    Check(!publicOwned.Contains("owned-access") && !publicOwned.Contains("owned-refresh") && !publicOwned.Contains(ownedBackend.LatestFilename), "own login public DTOs contain no credentials or private references");
    var ownLegacyAuth = Path.Combine(legacy, "own-legacy-auth"); Directory.CreateDirectory(ownLegacyAuth);
    File.WriteAllText(Path.Combine(ownLegacyAuth, "snapshot.json"), """{"type":"codex","account_id":"owned-provider-account","access_token":"external-old-access","refresh_token":"NEVER-IMPORT-OWN-REFRESH","expired":"2098-01-01T00:00:00Z"}""");
    new LegacyImportService(ownedPaths, ownedRepository, ownedSecrets, ["gpt-qualified"]).Import(new(ownedRepository.Snapshot.Revision, emptyAliasConfig, ownLegacyAuth));
    Check(ownedRepository.Snapshot.Accounts.Single().CredentialKind == "codex-owned" && ownedSecrets.ReadAccount(ownedAccount.Id).Contains("owned-access-two"), "legacy reimport cannot replace an explicitly owned login");
    using (var redirectBackend = new HttpCodexOwnedLoginBackend(ownedPaths, ownedSecrets, new RedirectLoginHttpHandler()))
    {
        try { await redirectBackend.BeginAsync(default); throw new Exception("Expected redirect rejection."); } catch (InvalidOperationException) { }
    }
    using (var httpBackend = new HttpCodexOwnedLoginBackend(ownedPaths, ownedSecrets, new FakeLoginHttpHandler()))
    {
        var started = await httpBackend.BeginAsync(default); var progress = await httpBackend.StatusAsync(started.State, default); await httpBackend.CancelAsync(started.State, default);
        Check(progress.State == "waiting", "narrow HTTP login contract uses typed state without exposing core errors");
    }
    var login = await new LoginOperationService().BeginAsync("codex");
    Check(login.State == "unsupported" && login.Url is null, "unqualified login fails closed");
    await using (var lifecycle = new OwnedProxyLifecycle(paths, secrets, new ProxyLaunchOptions("unused", "unused")))
    {
        try { await lifecycle.StartAsync(); throw new Exception("Expected qualification rejection."); } catch (InvalidOperationException) { }
        Check(lifecycle.Status.State == "stopped", "qualification rejects launch before touching any process");
        Check(lifecycle.Status.Error == "Proxy backend qualification has not passed.", "failed startup exposes a bounded actionable diagnostic");
        lifecycle.ReportStartupValidationFailure();
        Check(lifecycle.Status.Error == "Proxy backend qualification has not passed.", "outer startup validation does not hide the precise failure stage");
        await lifecycle.StopAsync();
        Check(lifecycle.Status.Error is null, "intentional stop clears an obsolete startup failure");
    }
    // Explicitly opt in only inside an isolated network namespace: production
    // lifecycle binds the fixed owned port, so normal suites never touch it.
    if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("LAIN_TEST_ISOLATED_PROXY_LIFECYCLE") == "1")
    {
        var fakeCore = Path.Combine(testRoot, "fake-core");
        File.WriteAllText(fakeCore, "#!/bin/sh\nprintf 'synthetic-private-stderr-do-not-forward\\n' >&2\nexit 7\n");
        File.SetUnixFileMode(fakeCore, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fakeCore)));
        await using var lifecycle = new OwnedProxyLifecycle(paths, secrets, new ProxyLaunchOptions(fakeCore, hash, true));
        try { await lifecycle.StartAsync(); throw new Exception("Expected launch rejection."); } catch (System.ComponentModel.Win32Exception) { }
        Check(lifecycle.Status.State == "stopped" && lifecycle.Status.Error == "The proxy process could not be launched.", "unstarted process leaves a readable safe status");
        await lifecycle.StopAsync();
        Check(lifecycle.Status.Error is null, "failed process launch can be stopped safely");
        File.SetUnixFileMode(fakeCore, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try { await lifecycle.StartAsync(); throw new Exception("Expected child exit rejection."); } catch (InvalidOperationException) { }
        Check(lifecycle.Status.State == "stopped" && lifecycle.Status.Error == "The proxy exited before becoming ready (exit code 7).", "early child exit retains exit code without forwarding output");
        Check(!JsonSerializer.Serialize(lifecycle.Status).Contains("synthetic-private-stderr"), "child output is absent from public diagnostics");
    }
    try { secrets.ReadAccount("../legacy"); throw new Exception("Expected path rejection."); } catch (ArgumentException) { }
    if (!OperatingSystem.IsWindows())
    {
        var link = Path.Combine(testRoot, "linked-state"); Directory.CreateSymbolicLink(link, legacy);
        try { _ = new OwnStatePaths(link); throw new Exception("Expected symlink rejection."); } catch (IOException) { }
    }
    Console.WriteLine("Accounts/operations checks passed.");
}
finally { Directory.Delete(testRoot, recursive: true); }
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

sealed class FakeCodexOwnedBackend(OwnStatePaths paths) : ICodexOwnedLoginBackend
{
    private readonly Dictionary<string, CoreCodexLoginProgress> states = [];
    public string LatestState { get; private set; } = "";
    public string LatestFilename { get; private set; } = "";
    public Task<CoreCodexLoginStart> BeginAsync(CancellationToken ct)
    {
        LatestState = Guid.NewGuid().ToString("N"); states[LatestState] = new("waiting");
        return Task.FromResult(new CoreCodexLoginStart(LatestState, "https://auth.openai.com/oauth/authorize?state=" + LatestState));
    }
    public Task<CoreCodexLoginProgress> StatusAsync(string state, CancellationToken ct) => Task.FromResult(states[state]);
    public Task CancelAsync(string state, CancellationToken ct)
    {
        if (states[state].State == "completed") throw new InvalidOperationException("Already completed.");
        states[state] = new("cancelled"); return Task.CompletedTask;
    }
    public void Complete(string state, string accountId)
    {
        if (states[state].State == "cancelled") return;
        LatestFilename = "codex-" + state + ".json";
        OwnStatePaths.WritePrivate(Path.Combine(paths.OwnedAuth, LatestFilename), JsonSerializer.Serialize(new { type = "codex", account_id = accountId, access_token = "owned-access-one", refresh_token = "owned-refresh-one", expired = "2099-01-01T00:00:00Z" }));
        states[state] = new("completed", LatestFilename);
    }
}
sealed class FakeLoginHttpHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri!.Host != "127.0.0.1" || request.RequestUri.Port != OwnStatePaths.ProxyPort || request.Headers.Authorization?.Scheme != "Bearer") throw new Exception("Invalid management transport.");
        var json = request.Method == HttpMethod.Post ? """{"state":"test-state","status":"pending","authorizationUrl":"https://auth.openai.com/oauth/authorize"}"""
            : request.Method == HttpMethod.Delete ? """{"state":"test-state","status":"canceled"}""" : """{"state":"test-state","status":"exchanging"}""";
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json) });
    }
}

sealed class RedirectLoginHttpHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.Redirect);
        response.Headers.Location = new Uri("https://example.invalid/collect-token");
        return Task.FromResult(response);
    }
}
