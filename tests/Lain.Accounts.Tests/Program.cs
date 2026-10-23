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
                proxy-url: http://127.0.0.1:19001
            models:
              - name: upstream-model
                alias: short-model
        """);
    File.WriteAllText(authPath, """{"type":"codex","access_token":"access-secret","account_id":"shared-account","refresh_token":"NEVER-COPY-ME","id_token":"NEVER-COPY-ID","expired":"2099-01-01T00:00:00Z","disabled":false}""");
    var before = Directory.GetFiles(legacy).ToDictionary(p => p, p => SHA256.HashData(File.ReadAllBytes(p)));
    var first = importer.Import(new(0, configPath, legacy));
    Check(first.Added == 2, "API and Codex snapshot accounts imported");
    Check(configuration.Snapshot.Accounts.Any(a => a.Provider == "command-code" && !a.Enabled), "disabled preserved");
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
    var login = await new LoginOperationService().BeginAsync("codex");
    Check(login.State == "unsupported" && login.Url is null, "unqualified login fails closed");
    await using (var lifecycle = new OwnedProxyLifecycle(paths, secrets, new ProxyLaunchOptions("unused", "unused")))
    {
        try { await lifecycle.StartAsync(); throw new Exception("Expected qualification rejection."); } catch (InvalidOperationException) { }
        Check(lifecycle.Status.State == "stopped", "qualification rejects launch before touching any process");
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
