using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Accounts.CliConnections;
using Lain.Host.Operations;
using Lain.Host.Quota;

if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux-only qualification fixture.");
var checks = 0;
void Check(bool yes, string message) { checks++; if (!yes) throw new Exception(message); }
JsonElement Json(string text) { using var document = JsonDocument.Parse(text); return document.RootElement.Clone(); }
var temporary = Path.Combine(Path.GetTempPath(), "lain-cli-fixtures-" + Guid.NewGuid().ToString("N"));
OwnStatePaths.EnsurePrivateDirectory(temporary);
try
{
    var executable = Path.Combine(temporary, "fixture-cli");
    File.Copy(args[0], executable); File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    var manifest = new CliQualificationManifest("fixture", "1.0", new string('a', 40), new Uri("https://github.com/lain-fixtures/source"),
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(executable))), "fixture-only", "linux-x64", executable, "lain-cli-fixture 1.0",
        ["normal", "inspect-env", "overflow", "stderr-overflow", "sleep", "fork", "exit-error", "login"], "fixture-schema-v1", "fixture-directory", "fixture-owner", DateTimeOffset.UtcNow,
        true, true, true, FixtureOnly: true);
    var process = new OfficialCliProcess(allowFixture: true);
    var work = Path.Combine(temporary, "work"); OwnStatePaths.EnsurePrivateDirectory(work);
    var env = new Dictionary<string, string> { ["LAIN_CLI_FIXTURE_DIR"] = work };
    await Denied(() => process.RunAsync(manifest with { ArtifactSha256 = new string('0', 64) }, ["normal"], work, env, TimeSpan.FromSeconds(3)), "changed executable hash rejected");
    await Denied(() => new OfficialCliProcess().RunAsync(manifest, ["normal"], work, env, TimeSpan.FromSeconds(3)), "fixture manifest cannot activate production runner");
    await Denied(() => process.RunAsync(manifest with { IdentityQualified = false }, ["normal"], work, env, TimeSpan.FromSeconds(3)), "missing identity qualification rejected");
    await Denied(() => process.RunAsync(manifest with { ExpectedVersionOutput = "different-version" }, ["--version"], work, env, TimeSpan.FromSeconds(3), versionProbe: true), "different version rejected");
    using (var output = await process.RunAsync(manifest, ["normal"], work, env, TimeSpan.FromSeconds(3)))
    {
        Check(output.ExitCode == 0 && Encoding.UTF8.GetString(output.Stdout.Span).Contains("fixture-private-api-key"), "targeted output private handoff");
        Check(!output.ToString().Contains("fixture-private") && !JsonSerializer.Serialize(output).Contains("fixture-private"), "process output serialization and ToString redacted");
    }
    Environment.SetEnvironmentVariable("DASHSCOPE_API_KEY", "fixture-inherited-secret");
    Environment.SetEnvironmentVariable("NODE_OPTIONS", "fixture-inherited-preload");
    try
    {
        using var output = await process.RunAsync(manifest, ["inspect-env"], work, env, TimeSpan.FromSeconds(3));
        var text = Encoding.UTF8.GetString(output.Stdout.Span);
        Check(!text.Contains("fixture-inherited") && !text.Contains("HOME") && !text.Contains("NODE_OPTIONS"), "no provider/default config overrides and no HOME rewriting");
    }
    finally { Environment.SetEnvironmentVariable("DASHSCOPE_API_KEY", null); Environment.SetEnvironmentVariable("NODE_OPTIONS", null); }
    using (var output = await process.RunAsync(manifest, ["exit-error"], work, env, TimeSpan.FromSeconds(3)))
        Check(output.ExitCode == 17 && output.Stderr.Length > 0, "exit failure preserves private separate stderr without exception echo");
    await Denied(() => process.RunAsync(manifest, ["overflow"], work, env, TimeSpan.FromSeconds(3)), "stdout overflow bounded");
    await Denied(() => process.RunAsync(manifest, ["stderr-overflow"], work, env, TimeSpan.FromSeconds(3)), "stderr overflow bounded");
    CliProcessIdentity? timedOut = null;
    await Denied(() => process.RunAsync(manifest, ["sleep"], work, env, TimeSpan.FromMilliseconds(200), started: p => timedOut = p), "process deadline enforced");
    Check(timedOut is not null && !Running(timedOut.ProcessId), "timed-out own process reaped");
    await Denied(() => process.RunAsync(manifest, ["fork"], work, env, TimeSpan.FromMilliseconds(400)), "own process tree deadline enforced");
    var childId = int.Parse(File.ReadAllText(Path.Combine(work, "child.pid")));
    for (var attempt = 0; attempt < 50 && Running(childId); attempt++) await Task.Delay(20);
    Check(!Running(childId), "own descendant stopped; fixture pid=" + childId);
    var linkedExecutable = Path.Combine(temporary, "linked-cli"); File.CreateSymbolicLink(linkedExecutable, executable);
    await Denied(() => process.RunAsync(manifest with { ExecutablePath = linkedExecutable }, ["normal"], work, env, TimeSpan.FromSeconds(3)), "symlink executable refused");
    await Denied(() => process.RunAsync(manifest, ["normal"], work, new Dictionary<string, string> { ["DASHSCOPE_API_KEY"] = "fixture-private" }, TimeSpan.FromSeconds(3)), "credential environment override refused");
    Check(RequestyCliLoginAdapter.ParsePrivateKeyExport("fixture-key\n"u8) == "fixture-key", "Requesty exact key-plus-newline helper");
    await Denied(() => Task.Run(() => RequestyCliLoginAdapter.ParsePrivateKeyExport("banner\nfixture-key\n"u8)), "Requesty banner/export drift refused");
    Check(RequestyCliLoginAdapter.LoginArguments("owned-profile", true).SequenceEqual(["login", "--profile", "owned-profile", "--force"]), "Requesty reconnect explicitly targeted");
    Check(RequestyCliLoginAdapter.ExportArguments("owned-profile").SequenceEqual(["auth", "token", "--profile", "owned-profile"]), "Requesty helper targeted, no active default profile");
    var hosts = new HashSet<string> { "auth.fixture.invalid" };
    Check(CliBrowserAction.Validate("https://auth.fixture.invalid/login", hosts) is not null, "qualified browser host allowed");
    foreach (var url in new[] { "http://auth.fixture.invalid/login", "https://auth.fixture.invalid.evil.invalid/login", "https://key@auth.fixture.invalid/login", "https://auth.fixture.invalid/login?code=private", "https://auth.fixture.invalid/login#token" })
        Check(CliBrowserAction.Validate(url, hosts) is null, "unqualified browser callback/credential target rejected");

    var store = new CliConnectionStateStore(Path.Combine(temporary, "store"));
    LoginOperationContext Context(string operation, string account = "account-one", ResourceBindingDto? binding = null) => new(operation, account, 7, 2,
        new("fixture", "official-cli", ["inference"], AccountId: account, Binding: binding), DateTimeOffset.UtcNow.AddMinutes(2));
    var context = Context("operation-one", binding: new("fixture", ResourceType: "team", ResourceId: "fixture-team"));
    await using (var adapter = new FixtureAdapter(store, manifest, "normal"))
    {
        var checkpoints = 0;
        context = context with { CheckpointCredentialAsync = (credential, _) => { checkpoints++; Check(credential.PrivateJson.Contains("fixture-private-api-key"), "private checkpoint receives exact key"); return Task.CompletedTask; } };
        Check((await adapter.BeginAsync(context, default)).State == "waiting", "qualified local CLI uses shared operation interface");
        var result = await Poll(adapter, context);
        Check(result.State == "prepared" && result.Credential?.Binding.ResourceId == "fixture-team" && checkpoints == 1, "normal credential bound and checkpointed once");
        Check((await adapter.BeginAsync(context, default)).State == "prepared" && checkpoints == 1, "same operation replay never repeats CLI issuance");
        await Denied(() => adapter.StatusAsync(context with { AccountGeneration = 3 }, default), "generation context change rejected");
        var journal = store.Read(context.OperationId)!;
        Check(journal.Phase == "result-private" && journal.RemoteKeyId == "fixture-key-id", "journal retains issuance id and private-result phase");
        var journalText = File.ReadAllText(Path.Combine(temporary, "store", "operations", context.OperationId + ".json"));
        Check(!journalText.Contains("fixture-private-api-key") && !journalText.Contains("fixture-private-code"), "journal contains no key or callback code");
        Check((File.GetUnixFileMode(Path.Combine(temporary, "store", "operations", context.OperationId + ".json")) & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) == 0, "journal 0600");
        await adapter.ReleaseAsync(context, default);
    }
    Check(store.ReconcileAfterRestart(s => s.OperationId == context.OperationId).Count == 0 && store.Read(context.OperationId)?.Phase == "adopted", "crash after commit reconciles shared publication, no key create");
    await using (var adapter = new FixtureAdapter(store, manifest, "wrong-scope"))
    {
        var wrong = Context("operation-wrong", "account-wrong", new("fixture", ResourceType: "team", ResourceId: "fixture-team"));
        await adapter.BeginAsync(wrong, default);
        Check((await Poll(adapter, wrong)).State == "recovery-required", "wrong resource cannot publish, issued result retained");
    }
    await using (var adapter = new FixtureAdapter(store, manifest, "malformed"))
    {
        var malformed = Context("operation-malformed", "account-malformed"); await adapter.BeginAsync(malformed, default);
        var result = await Poll(adapter, malformed);
        Check(result.State == "recovery-required" && !result.Error!.Contains("fixture-private"), "malformed result safe recovery, no exception/provider echo");
    }
    await using (var adapter = new FixtureAdapter(store, manifest, "sleep"))
    {
        var slow = Context("operation-slow", "account-slow"); await adapter.BeginAsync(slow, default);
        Check((await adapter.BeginAsync(Context("operation-parallel", "account-slow"), default)).State == "failed", "parallel reconnect serialized per account");
        for (var attempt = 0; attempt < 150 && store.Read(slow.OperationId)?.Process is null; attempt++) await Task.Delay(20);
        Check(store.Read(slow.OperationId)?.Process is not null, "cancellation fixture reached its own login process");
        await adapter.CancelAsync(slow, default);
        Check((await adapter.StatusAsync(slow, default)).State == "recovery-required", "cancel explains uncertain issuance without remote revoke or retry");
    }
    var lifecyclePaths = new OwnStatePaths(Path.Combine(temporary, "shared-lifecycle"));
    var repository = new ConfigurationRepository(lifecyclePaths); var secrets = new SecretStore(lifecyclePaths);
    var lifecycleStore = new CliConnectionStateStore(Path.Combine(lifecyclePaths.Credentials, "cli"), state =>
    {
        var prepared = secrets.Generations.FindPrepared(state.OperationId, state.AccountId);
        return prepared is not null && repository.Snapshot.Accounts.Any(a => a.Id == state.AccountId
            && a.Credentials?.Any(h => h.Id == prepared.Handle.Id && h.Generation == prepared.Handle.Generation) == true);
    });
    await using (var adapter = new FixtureAdapter(lifecycleStore, manifest, "normal"))
    await using (var lifecycle = new LoginOperationService(configuration: repository, accounts: new AccountService(repository, secrets), secrets: secrets, accountAdapters: [adapter]))
    {
        var request = new ConnectAccountRequest("fixture", "official-cli", ["inference"], Name: "Kept name", Binding: new("fixture", ResourceType: "team", ResourceId: "fixture-team"));
        var login = await lifecycle.BeginAsync(repository.Snapshot.Revision, request);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        do { login = await lifecycle.StatusAsync(login.Id, deadline.Token); if (login.State is "waiting" or "authorizing") await Task.Delay(20, deadline.Token); }
        while (login.State is "waiting" or "authorizing");
        Check(login.State == "completed" && repository.Snapshot.Accounts.Length == 1 && repository.Snapshot.Routes.Length == 0 && repository.Snapshot.Profiles.Length == 0, "shared lifecycle publishes one credential/account with no route/profile or inference");
        var original = repository.Snapshot.Accounts.Single();
        var reconnect = request with { AccountId = original.Id, Name = "Ignored replacement name", ExpectedGeneration = original.Generation };
        login = await lifecycle.BeginAsync(repository.Snapshot.Revision, reconnect);
        do { login = await lifecycle.StatusAsync(login.Id, deadline.Token); if (login.State is "waiting" or "authorizing") await Task.Delay(20, deadline.Token); }
        while (login.State is "waiting" or "authorizing");
        var updated = repository.Snapshot.Accounts.Single();
        Check(login.State == "completed" && updated.Id == original.Id && updated.Name == "Kept name" && updated.Generation > original.Generation, "same-account reconnect preserves ID/name and atomically advances credential generation");
    }
    var crashContext = Context("operation-crash", "account-crash"); var crashState = store.Create(crashContext);
    Check(store.ReconcileAfterRestart().Any(s => s.OperationId == crashContext.OperationId && s.Phase == "recovery-required"), "crash before result never treats old owned file as fresh login");
    await using (var adapter = new FixtureAdapter(store, manifest, "normal"))
        await Denied(() => adapter.BeginAsync(crashContext, default), "journaled crash operation cannot replay issuance");
    var profileDirectory = store.ProfileDirectory(crashState.ProfileReference);
    var profileText = "{\"active_config\":\"lain\",\"lain\":{\"api_key\":\"fixture-key\",\"access_token\":\"fixture-console-session\",\"base_url\":\"https://dashscope-intl.aliyuncs.com/compatible-mode/v1\",\"workspace_id\":\"ws-one\",\"console_site\":\"international\",\"console_region\":\"ap-southeast-1\"}}";
    OwnStatePaths.WritePrivate(Path.Combine(profileDirectory, "config.json"), profileText);
    var owned = new AlibabaOwnedProfileReader(store).Read(crashState.ProfileReference);
    Check(owned.Binding.ResourceId == "ws-one" && owned.Region == "ap-southeast-1" && !JsonSerializer.Serialize(owned).Contains("fixture-key") && !JsonSerializer.Serialize(owned).Contains("fixture-console-session"), "Alibaba named callback profile carries exact scope, secrets private");
    foreach (var changed in new[] { profileText.Replace("ap-southeast-1", "unknown-region"), profileText.Replace("https://dashscope-intl.aliyuncs.com", "https://evil.invalid"), profileText.Replace("fixture-key", "sk-sp-subscription"), profileText.Replace("\"api_key\":", "\"access_key_id\":\"unrequested\",\"api_key\":") })
    {
        OwnStatePaths.WritePrivate(Path.Combine(profileDirectory, "config.json"), changed);
        await Denied(() => Task.Run(() => new AlibabaOwnedProfileReader(store).Read(crashState.ProfileReference)), "Alibaba wrong region/host/product/cloud credential refused");
    }
    foreach (IAccountLoginAdapter unavailable in new IAccountLoginAdapter[] { new RequestyCliLoginAdapter(), new AlibabaCliLoginAdapter(store), new CommandCodeCliLoginAdapter(), new FireworksCliLoginAdapter(), new MistralVibeCliLoginAdapter() })
    {
        var disabled = Context("disabled-" + unavailable.Provider) with { Request = new(unavailable.Provider, "official-cli", ["inference"]) };
        var result = await unavailable.BeginAsync(disabled, default);
        Check(result.State == "failed" && result.NextAction == "use-api-key" && result.Error is { Length: > 30 }, "each real provider blocked independently with concrete key fallback");
    }
    // Alibaba raw schemas and fixed-operation delegation contain no HTTP or real CLI execution.
    var now = new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
    var epoch = now.AddDays(3).ToUnixTimeMilliseconds();
    var trialPayload = "{\"freeTierQuotas\":[{\"model\":\"qwen-fixture\",\"quotaInitTotal\":100,\"quotaTotal\":0,\"quotaValidityPeriod\":" + epoch + ",\"quotaStatus\":\"FIXTURE_ACTIVE\"}]}";
    var direct = "{\"data\":{\"data\":" + trialPayload + "}}";
    var nested = "{\"data\":{\"DataV2\":{\"data\":{\"data\":" + trialPayload + "}}}}";
    var active = new HashSet<string> { "FIXTURE_ACTIVE" };
    Check(AlibabaConsoleQuotaAdapter.NormalizeTrialGrants(Json(direct), now).Single() is { RemainingAmount: 0, IsAvailable: false }, "unqualified grant status never available by default");
    foreach (var envelope in new[] { direct, nested })
        Check(AlibabaConsoleQuotaAdapter.NormalizeTrialGrants(Json(envelope), now, active).Single() is { RemainingAmount: 0, OriginalAmount: 100, IsAvailable: true, ExpiresAt: var expiry } && expiry == now.AddDays(3), "raw grant remaining/original/epoch expiry preserved across envelopes");
    Check(!AlibabaConsoleQuotaAdapter.NormalizeTrialGrants(Json(direct), now.AddDays(4), active).Single().IsAvailable, "expired grant not available, no reset inferred");
    Check(AlibabaConsoleQuotaAdapter.NormalizeTrialGrants(Json(direct.Replace("FIXTURE_ACTIVE", "UNKNOWN")), now, active).Single().IsAvailable == false, "unknown grant status not available");
    Check(AlibabaConsoleQuotaAdapter.NormalizeTrialGrants(Json("{\"data\":{\"success\":false,\"errorCode\":\"NotLogined\",\"data\":" + trialPayload + "}}"), now, active).Count == 0, "session failure envelope cannot expose stale embedded quota as success");
    var usageJson = "{\"data\":{\"data\":{\"usages\":[{\"key\":\"total_token\",\"unit\":\"tokens\",\"value\":0},{\"key\":\"image_number\",\"unit\":\"images\",\"value\":2},{\"key\":\"unknown\",\"unit\":\"invented\",\"value\":50}]}}}";
    var usage = AlibabaConsoleQuotaAdapter.NormalizeUsage(Json(usageJson), "ws-one", now.AddDays(-7), now);
    Check(usage.Count == 2 && usage[0].Used == 0 && usage[1].Unit == "images" && usage.All(r => r.DataAsOf is null && r.ResetsAt is null && r.Limit is null && r.WindowStart == now.AddDays(-7)), "actual usage units/window/zero preserved, no provider time or cap invented");
    var fakeOwner = new FakeAlibabaOwner(direct, usageJson);
    var quotaAdapter = new AlibabaConsoleQuotaAdapter(fakeOwner, new Dictionary<string, string> { ["qwen-fixture"] = "tokens" }, active, new FixedClock(now));
    var account = new AccountDto("ali-account", "Fixture", "alibaba-model-studio", true, "api-key", "connected", Binding: new("alibaba-model-studio", Product: "ordinary-api", Region: "ap-southeast-1", ResourceType: "workspace", ResourceId: "ws-one"));
    var credential = new QuotaCredential("", settings: new Dictionary<string, string> { ["consoleSite"] = "international" }, monitor: new("alibaba-cli-profile", null, crashState.ProfileReference), bindingVersion: "revision-one");
    var details = await quotaAdapter.ReadDetailsAsync(account, credential);
    Check(details.Readings.Count == 3 && details.Grants.Single().RemainingAmount == 0 && details.Protection.Count == 1 && details.Readings.All(r => r.ResetsAt is null), "trial and usage separate, protection remains typed, expiry not reset");
    fakeOwner.UsageFails = true;
    details = await quotaAdapter.ReadDetailsAsync(account, credential);
    Check(details.Readings.Single().Remaining == 0 && details.Error is not null && !details.Error.Contains("fixture-private"), "partial read failure preserves successful grant without credential echo");
    await Denied(() => quotaAdapter.FetchAsync(account with { Binding = account.Binding! with { Region = "unknown" } }, credential), "unknown Alibaba region denied without Beijing fallback");
    await Denied(() => quotaAdapter.FetchAsync(account, new("fixture-key")), "ordinary inference key cannot become Console session");
    Console.WriteLine($"PASS: {checks} isolated CLI/process/lifecycle/Alibaba assertions; zero provider calls");
}
finally { Directory.Delete(temporary, true); }

async Task Denied(Func<Task> action, string message)
{
    try { await action(); } catch (Exception error) when (error is CliConnectionException or IOException or InvalidDataException or ArgumentException or InvalidOperationException || error.GetType().Name == "QuotaFetchException")
    { Check(!error.Message.Contains("fixture-private"), message); return; }
    throw new Exception(message);
}
static bool Running(int pid)
{
    try
    {
        // A killed non-child can briefly remain a zombie until its new parent reaps
        // it; it cannot execute or retain the CLI pipes. Do not report it as running.
        var stat = File.ReadAllText($"/proc/{pid}/stat"); var endName = stat.LastIndexOf(')');
        if (endName >= 0 && stat[(endName + 2)..].StartsWith("Z ", StringComparison.Ordinal)) return false;
        using var process = Process.GetProcessById(pid); return !process.HasExited;
    }
    catch (Exception error) when (error is ArgumentException or IOException) { return false; }
}
static async Task<LoginAdapterResult> Poll(IAccountLoginAdapter adapter, LoginOperationContext context)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
    while (true) { var result = await adapter.StatusAsync(context, timeout.Token); if (result.State != "waiting") return result; await Task.Delay(20, timeout.Token); }
}
sealed class FixtureAdapter(CliConnectionStateStore store, CliQualificationManifest manifest, string mode)
    : QualifiedCliLoginAdapter(store, manifest, new OfficialCliProcess(allowFixture: true))
{
    public override string Provider => "fixture";
    public override string QualificationBlocker => "Fixture only";
    protected override string IsolationEnvironment => "LAIN_CLI_FIXTURE_DIR";
    protected override IReadOnlyList<string> LoginArguments(string profile, bool reconnect) => ["login", mode];
    protected override Task<PreparedLoginCredential?> ReadCredentialAsync(LoginOperationContext context, CliConnectionState state, string directory, CancellationToken ct)
    {
        using var result = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "result.json")));
        var team = result.RootElement.GetProperty("team").GetString();
        return Task.FromResult<PreparedLoginCredential?>(new("static-key", ["inference"], new("fixture", ResourceType: "team", ResourceId: team, Provenance: "fixture"),
            result.RootElement.GetRawText(), PreserveForRecovery: true, RemoteId: "fixture-key-id"));
    }
}
sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
sealed class FakeAlibabaOwner(string trial, string usage) : IAlibabaConsoleReadOwner
{
    public bool UsageFails;
    public Task<JsonDocument> ReadTrialAsync(AlibabaConsoleScope scope, string[] models, CancellationToken ct) => Task.FromResult(JsonDocument.Parse(trial));
    public Task<JsonDocument> ReadTrialProtectionAsync(AlibabaConsoleScope scope, string[] models, CancellationToken ct) => Task.FromResult(JsonDocument.Parse("{\"data\":{\"data\":{\"freeTierOnlyStatuses\":[{\"model\":\"qwen-fixture\",\"freeTierOnly\":true}]}}}"));
    public Task<JsonDocument> ReadUsageAsync(AlibabaConsoleScope scope, DateTimeOffset start, DateTimeOffset end, CancellationToken ct) => UsageFails ? throw new IOException("fixture-private-session") : Task.FromResult(JsonDocument.Parse(usage));
}
