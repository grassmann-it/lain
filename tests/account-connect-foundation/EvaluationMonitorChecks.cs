using System.Security.Cryptography;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Accounts.Credentials;
using Lain.Host.Accounts.Monitoring;
using Lain.Host.Operations;
using Lain.Host.Quota;

/// <summary>EC1–EC4 local integration checks; no provider requests or real credentials.</summary>
internal static class EvaluationMonitorChecks
{
    private const string ManagementToken = "sk-nano-mgmt-fixture";
    private const string InferenceKey = "sk-nano-inference-fixture";
    private const string NanoBase = "https://api.nano-gpt.com/api/v1";

    public static async Task<int> Run(string root)
    {
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        void Reject(Action action, string message)
        {
            try { action(); }
            catch (Exception error) when (error is ArgumentException or InvalidDataException or InvalidOperationException or RevisionConflictException)
            { checks++; return; }
            throw new Exception(message);
        }
        void NoWrites(State state, Action action, string message)
        {
            var before = Tree(state.Paths.Root);
            Reject(action, message);
            Check(before.SequenceEqual(Tree(state.Paths.Root)), message + ": rejection must preserve paths, modes and bytes");
        }

        foreach (var resource in new string?[] { null, "", " \t " })
        {
            var state = New("standalone");
            var config = state.Monitor.Create(0, "My Nano subscription", "nanogpt", new("management-token", ManagementToken, resource));
            var account = config.Accounts.Single(); var handle = account.Credentials!.Single();
            Check(account.Name == "My Nano subscription" && account.State == "unverified" && account.Enabled,
                "Nano monitor saves locally without claiming provider verification");
            Check(config.Routes.Length == 0 && config.Profiles.Length == 0 && account.Capabilities is [{ Purpose: "quota", State: "unverified" }]
                && handle.Purposes.SequenceEqual(new[] { "quota" }), "standalone monitor grants quota only and creates no route/profile");
            Check(account.Binding == new ResourceBindingDto("nanogpt") && handle.Binding == new ResourceBindingDto("nanogpt"),
                "resource-less Nano monitor invents no subject/product/region/resource or observed provenance");
            var display = state.Monitor.Get(account.Id); var monitor = state.Monitor.GetCredential(account.Id);
            Check(display.State == "configured-unverified" && display.Kind == "management-token" && display.ResourceId is null
                && monitor?.Kind == "management-token" && monitor.Secret == ManagementToken && monitor.ResourceId is null,
                "null and blank Nano resource IDs are normalized to absent through private/public projections");
            var quota = await state.Source.GetCredentialAsync(account.Id);
            Check(quota?.BearerToken == "" && quota.MonitorBearerToken == ManagementToken && quota.ProviderAccountId is null
                && quota.Monitor?.ResourceId is null && quota.Purposes?.SequenceEqual(new[] { "quota" }) == true,
                "real quota source projects management access separately without an inference token or fake account ID");
            Check(state.Resolver.Resolve(account.Id, "inference") is null && state.Resolver.Resolve(account.Id, "models") is null
                && !state.Secrets.HasAccount(account.Id), "monitor-only account exposes no inference/models credential");
            Check(!JsonSerializer.Serialize(config).Contains(ManagementToken) && !JsonSerializer.Serialize(display).Contains(ManagementToken),
                "management secret never enters public configuration or monitor metadata");
        }

        var nanoOption = MonitorAccessCatalog.Options("nanogpt").Single(o => o.Kind == "management-token");
        Check(nanoOption.SecretRequired && !nanoOption.ResourceRequired, "Nano monitor metadata declares a private token without a resource selector");
        foreach (var resource in new[] { "invented-account", " owner " })
        {
            var state = New("selector");
            NoWrites(state, () => state.Monitor.Create(0, "Nano", "nanogpt", new("management-token", ManagementToken, resource)),
                "Nano monitor refuses a supplied account selector");
        }
        foreach (var secret in new[] { "sk-nano-mgmt-", InferenceKey, "ordinary-fixture-token" })
        {
            var state = New("invalid-monitor-token");
            NoWrites(state, () => state.Monitor.Create(0, "Nano", "nanogpt", new("management-token", secret)),
                "Nano management setup validates its own token kind");
        }

        var existing = New("existing");
        var observed = new ResourceBindingDto("nanogpt", "observed-inference-owner", Product: "inference", Provenance: "observed");
        var initial = existing.Accounts.Connect(0, new("nanogpt", "fixture", ["inference", "models"], Name: "Keep my Nano name"),
            new("static-key", ["inference", "models"], observed, KeyJson(InferenceKey, "nanogpt", NanoBase)));
        var initialAccount = initial.Accounts.Single(); var inferenceHandle = initialAccount.Credentials!.Single();
        initial = existing.Repository.Update(initial.Revision, config => config with {
            Routes = [new("route-nano-keep", "nano-keep-alias", "fixture-model", "nanogpt", [initialAccount.Id])],
            Profiles = [new("profile-nano-keep", "Keep Nano profile", ["route-nano-keep"], "route-nano-keep")] });
        var oldCredential = existing.Secrets.Generations.Read(initialAccount.Id, inferenceHandle).PrivateJson;
        var routeSnapshot = JsonSerializer.Serialize(initial.Routes); var profileSnapshot = JsonSerializer.Serialize(initial.Profiles);
        var writer = new ProxyConfigurationWriter(existing.Paths, existing.Secrets); writer.Write(initial);
        var connected = existing.Monitor.Set(initial.Revision, initialAccount.Id, new("management-token", ManagementToken));
        var after = connected.Accounts.Single(); var quotaHandle = after.Credentials!.Single(h => h.Purposes.SequenceEqual(new[] { "quota" }));
        Check(after.Id == initialAccount.Id && after.Name == initialAccount.Name && after.Binding == observed,
            "adding Nano monitor preserves existing observed inference identity and user-selected name");
        Check(JsonSerializer.Serialize(after.Credentials.Single(h => h.Id == inferenceHandle.Id)) == JsonSerializer.Serialize(inferenceHandle)
            && existing.Secrets.Generations.Read(after.Id, inferenceHandle).PrivateJson == oldCredential,
            "adding Nano monitor preserves the inference handle and private generation exactly");
        Check(quotaHandle.Binding == new ResourceBindingDto("nanogpt") && quotaHandle.Owner == "static-key"
            && after.Capabilities!.Single(c => c.Purpose == "inference").CredentialId == inferenceHandle.Id
            && after.Capabilities.Single(c => c.Purpose == "models").CredentialId == inferenceHandle.Id,
            "separate quota handle stays provider-only declared and does not claim the inference owner's identity");
        Check(JsonSerializer.Serialize(connected.Routes) == routeSnapshot && JsonSerializer.Serialize(connected.Profiles) == profileSnapshot,
            "monitor attachment preserves route/profile identities and selections");
        Check(existing.Resolver.Resolve(after.Id, "inference")!.PrivateJson == oldCredential
            && existing.Resolver.Resolve(after.Id, "models")!.PrivateJson == oldCredential,
            "purpose-bound inference/models resolution still selects the original key");
        var attachedQuota = await existing.Source.GetCredentialAsync(after.Id);
        Check(attachedQuota?.BearerToken == "" && attachedQuota.MonitorBearerToken == ManagementToken && attachedQuota.ProviderAccountId is null,
            "quota handoff does not copy the observed inference subject into a management-token identity claim");
        writer.Write(connected);
        Check(File.ReadAllText(existing.Paths.ProxyConfig).Contains(InferenceKey)
            && !File.ReadAllText(existing.Paths.ProxyConfig).Contains(ManagementToken), "actual writer uses the inference key and never the separate management token");
        NoWrites(existing, () => existing.Monitor.Set(initial.Revision, after.Id, new("management-token", ManagementToken)),
            "stale monitor revision is rejected before writes");
        NoWrites(existing, () => existing.Monitor.Create(initial.Revision, "Stale Nano", "nanogpt", new("management-token", ManagementToken)),
            "stale monitor creation revision is rejected before writes");
        NoWrites(existing, () => existing.Monitor.Set(connected.Revision, after.Id, new("management-token", ManagementToken, "fake-id")),
            "existing Nano monitor also refuses an account selector");
        NoWrites(existing, () => existing.Accounts.ReserveConnect(connected.Revision,
            new("nanogpt", "monitor-management-token", ["quota"], AccountId: after.Id, ExpectedGeneration: after.Generation - 1),
            "stale-monitor-reservation", DateTimeOffset.UtcNow.AddMinutes(5)), "stale target generation cannot reserve monitor attachment");

        foreach (var invalidMonitor in new[] {
            new StoredMonitorAccess(after.Id, "nanogpt", "management-token", InferenceKey, null!, null),
            new StoredMonitorAccess("acct-another-fixture", "nanogpt", "management-token", ManagementToken, null!, null),
            new StoredMonitorAccess(after.Id, "nanogpt", "management-token", ManagementToken, "invented-resource", null) })
        {
            // Prepare the invalid fixture explicitly, then isolate the publication
            // check. The provider-only exception must not bypass an observed
            // inference binding for malformed or differently scoped material.
            var invalidContext = existing.Accounts.ReserveConnect(connected.Revision,
                new("nanogpt", "monitor-management-token", ["quota"], AccountId: after.Id, Binding: new("nanogpt"), ExpectedGeneration: after.Generation),
                "invalid-monitor-" + Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow.AddMinutes(5));
            var invalidHandle = existing.Secrets.Generations.Prepare(after.Id, new("static-key", ["quota"], new("nanogpt"),
                JsonSerializer.Serialize(new { kind = "monitor-access", monitorAccess = invalidMonitor })));
            NoWrites(existing, () => existing.Accounts.CommitPrepared(invalidContext, invalidHandle),
                "provider-only monitor publication exception requires the valid account/token/resource contract");
        }

        var context = existing.Accounts.ReserveConnect(connected.Revision,
            new("nanogpt", "monitor-management-token", ["quota"], AccountId: after.Id, Binding: new("nanogpt"), ExpectedGeneration: after.Generation),
            "stale-monitor-commit", DateTimeOffset.UtcNow.AddMinutes(5));
        var privateMonitor = JsonSerializer.Serialize(new { kind = "monitor-access", monitorAccess =
            new StoredMonitorAccess(after.Id, "nanogpt", "management-token", ManagementToken, null!, null) });
        var prepared = existing.Secrets.Generations.Prepare(after.Id, new("static-key", ["quota"], new("nanogpt"), privateMonitor));
        var advanced = existing.Repository.Update(connected.Revision, config => config with {
            Accounts = config.Accounts.Select(a => a with { Generation = a.Generation + 1 }).ToArray() });
        NoWrites(existing, () => existing.Accounts.CommitPrepared(context with { Revision = advanced.Revision }, prepared),
            "monitor commit rechecks target generation even with a fresh revision");

        foreach (var provider in new[] { "fireworks", "groq", "cloudflare-workers-ai", "deepseek", "test" })
        {
            var state = New("wrong-provider");
            NoWrites(state, () => state.Monitor.Create(0, "Wrong provider", provider, new("management-token", ManagementToken)),
                "Nano management-token kind must not broaden to another provider");
        }
        foreach (var (provider, resource) in new[] { ("fireworks", "fixture-account"), ("groq", "proj_fixture"),
            ("cloudflare-workers-ai", "0123456789abcdef0123456789abcdef") })
        {
            var state = New("required-resource");
            Check(MonitorAccessCatalog.Options(provider).Single(o => o.Kind == "api-token").ResourceRequired,
                "existing provider monitor still requires its resource ID");
            foreach (var missing in new string?[] { null, "", " \t " })
                NoWrites(state, () => state.Monitor.Create(0, "Monitor", provider, new("api-token", "fixture-monitor-token", missing)),
                    "old monitor resource requirement remains enforced");
            var created = state.Monitor.Create(0, "Monitor", provider, new("api-token", "fixture-monitor-token", resource));
            Check(created.Accounts.Single().Binding?.ResourceId == resource
                && state.Monitor.Get(created.Accounts.Single().Id).ResourceId == resource, "existing resource-bound monitor continues to work with its exact ID");
        }

        foreach (var token in new[] { ManagementToken, "sk-nano-mgmt-" })
        {
            foreach (var provider in new[] { "nanogpt", "test" })
            {
                var state = New("ingress");
                NoWrites(state, () => state.Accounts.AddApi(0, new("Forbidden inference", provider, token, NanoBase)),
                    "AddApi rejects management-token prefix for inference");
                foreach (var purpose in new[] { "inference", "models" })
                {
                    NoWrites(state, () => state.Accounts.ConnectCredential(0,
                        new(new(provider, "manual-key", [purpose]), token, BaseUrl: NanoBase)),
                        "ConnectCredential rejects management-token prefix for inference/models");
                    NoWrites(state, () => state.Accounts.ConnectCredential(0,
                        new(new(provider, "manual-snapshot", [purpose]), token, Kind: "access-token-snapshot", ExpiresAt: DateTimeOffset.UtcNow.AddHours(1), BaseUrl: NanoBase)),
                        "an access-token snapshot cannot disguise management access as inference/models access");
                }
                var saved = state.Accounts.Connect(0, new(provider, "fixture", ["inference"]),
                    new("static-key", ["inference"], new(provider), KeyJson(InferenceKey, provider, NanoBase)));
                NoWrites(state, () => state.Accounts.ReplaceApiCredential(saved.Revision, saved.Accounts.Single().Id, token),
                    "ReplaceApiCredential rejects management-token prefix while retaining the old key");
            }
            var import = New("import-ingress"); var source = Path.Combine(root, "synthetic-import-" + Guid.NewGuid().ToString("N") + ".yaml");
            OwnStatePaths.WritePrivate(source, $$"""
                openai-compatibility:
                  - name: test
                    base-url: https://api.nano-gpt.com/api/v1
                    api-key-entries:
                      - api-key: {{token}}
                    models:
                      - name: fixture-model
                        alias: forbidden-management-alias
                """);
            NoWrites(import, () => new LegacyImportService(import.Paths, import.Repository, import.Secrets).Import(new(0, source)),
                "actual legacy import rejects management-token inference before private snapshots or public state are written");
            var oauthImport = New("snapshot-import-ingress");
            var emptyConfig = Path.Combine(root, "synthetic-empty-" + Guid.NewGuid().ToString("N") + ".yaml");
            var authDirectory = Path.Combine(root, "synthetic-auth-" + Guid.NewGuid().ToString("N"));
            OwnStatePaths.WritePrivate(emptyConfig, "{}");
            OwnStatePaths.WritePrivate(Path.Combine(authDirectory, "snapshot.json"), JsonSerializer.Serialize(new {
                type = "codex", access_token = token, account_id = "fixture-snapshot-owner" }));
            NoWrites(oauthImport, () => new LegacyImportService(oauthImport.Paths, oauthImport.Repository, oauthImport.Secrets).Import(new(0, emptyConfig, authDirectory)),
                "actual legacy OAuth snapshot import also refuses a management token without writes");
        }

        foreach (var secretJson in new[] {
            KeyJson(ManagementToken, "test", NanoBase),
            JsonSerializer.Serialize(new { kind = "access-token-snapshot", accessToken = ManagementToken, provider = "test", baseUrl = NanoBase }),
            JsonSerializer.Serialize(new { kind = "codex-access-snapshot", accessToken = ManagementToken, provider = "test", accountId = "fixture-snapshot-owner" }),
            JsonSerializer.Serialize(new { kind = "api-key", apiKey = InferenceKey, provider = "test", baseUrl = NanoBase,
                transports = new[] { new { kind = "api-key", apiKey = ManagementToken, provider = "test", baseUrl = NanoBase } } }),
            JsonSerializer.Serialize(new { kind = "monitor-access", monitorAccess = new { Kind = "management-token", Secret = ManagementToken, ResourceId = (string?)null } }) })
        {
            // Model an already saved legacy/corrupt credential to exercise final
            // readers independently of the normal ingress rejection above.
            var state = New("final-reader"); var accountId = "acct-final-fixture";
            state.Secrets.WriteAccount(accountId, secretJson);
            var config = state.Repository.Update(0, value => value with {
                Accounts = [new(accountId, "Saved legacy fixture", "test", true, "api-key", "unverified")],
                Routes = [new("route-final-fixture", "final-fixture", "fixture-model", "test", [accountId])] });
            foreach (var purpose in new[] { "inference", "models" })
                NoWrites(state, () => state.Resolver.Resolve(accountId, purpose), "final purpose-bound resolver rejects management material or monitor-only kind");
            NoWrites(state, () => new ProxyConfigurationWriter(state.Paths, state.Secrets).Write(config),
                "actual writer rejects forbidden saved credentials before output/auth/token mutations");
        }

        return checks;

        State New(string name) => new(Path.Combine(root, name + "-" + Guid.NewGuid().ToString("N")));
    }

    private static string KeyJson(string key, string provider, string endpoint) => JsonSerializer.Serialize(new {
        kind = "api-key", apiKey = key, provider, coreProvider = provider, sourceSection = "openai-compatibility", baseUrl = endpoint });

    private static string[] Tree(string root) => Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(root, path) + ":" + (OperatingSystem.IsWindows() ? "" : ((int)File.GetUnixFileMode(path)).ToString())
            + (Directory.Exists(path) ? ":directory" : ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))))
        .Order(StringComparer.Ordinal).ToArray();

    private sealed class State
    {
        public OwnStatePaths Paths { get; }
        public SecretStore Secrets { get; }
        public ConfigurationRepository Repository { get; }
        public AccountService Accounts { get; }
        public MonitorAccessService Monitor { get; }
        public CredentialResolver Resolver { get; }
        public HostQuotaSource Source { get; }
        public State(string root)
        {
            Paths = new(root); Secrets = new(Paths); Repository = new(Paths); Accounts = new(Repository, Secrets);
            Monitor = new(Repository, Secrets, new MonitorCredentialStore(Paths)); Resolver = new(Repository, Secrets);
            Source = new(Repository, Secrets, Monitor);
        }
    }
}
