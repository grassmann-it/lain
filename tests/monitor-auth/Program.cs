using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts.Monitoring;
using Lain.Host.Operations;

var root = Path.Combine(Path.GetTempPath(), "lain-monitor-auth-" + Guid.NewGuid().ToString("N"));
var passed = 0;
try
{
    var paths = new OwnStatePaths(root);
    var store = new MonitorCredentialStore(paths);
    var secrets = new SecretStore(paths);
    var account = new AccountDto("acct-fireworks", "Fireworks", "fireworks", true, "api-key", "unverified");
    var credential = new StoredMonitorAccess(account.Id, account.Provider, "api-token", "synthetic-monitor-token", "example-account", null);
    Equal(null, store.Read(account), "missing monitor remains absent");
    store.Write(account, credential);
    Equal("synthetic-monitor-token", store.Read(account)!.Secret, "private credential round trip");
    Equal("StoredMonitorAccess [redacted]", credential.ToString(), "credential formatting is redacted");

    secrets.WriteAccount(account.Id, JsonSerializer.Serialize(new { kind = "api-key", apiKey = "synthetic-inference-a" }));
    secrets.WriteAccount(account.Id, JsonSerializer.Serialize(new { kind = "api-key", apiKey = "synthetic-inference-b" }));
    Equal("synthetic-monitor-token", store.Read(account)!.Secret, "inference replacement cannot overwrite monitor");
    Equal(null, store.Read(account with { Id = "acct-other" }), "separate accounts do not share monitor access");
    Throws<InvalidDataException>(() => store.Read(account with { Provider = "cloudflare-workers-ai" }), "provider rebinding is refused");
    Throws<ArgumentException>(() => store.Write(account with { Id = "acct-other" }, credential), "write identity mismatch refused");
    Throws<ArgumentException>(() => store.Delete("../outside"), "path traversal refused");
    Throws<InvalidDataException>(() => store.Write(account, new(account.Id, account.Provider, "oauth-owned", "synthetic", "example-account", null)), "unimplemented OAuth kind refused");
    Throws<InvalidDataException>(() => store.Write(account, new(account.Id, account.Provider, "inference-key", "synthetic", "example-account", null)), "key reuse cannot stash another token");
    Throws<InvalidDataException>(() => store.Write(account, new(account.Id, account.Provider, "access-token-snapshot", "synthetic", "example-account", null)), "snapshot needs explicit expiry");
    Throws<InvalidDataException>(() => store.Write(account, new(account.Id, account.Provider, "api-token", "synthetic\r\nInjected: value", "example-account", null)), "header injection refused");
    Throws<InvalidDataException>(() => store.Write(account, new(account.Id, account.Provider, "api-token", "synthetic", "../wrong", null)), "invalid resource refused");

    var path = Path.Combine(paths.Credentials, "monitors", account.Id + ".json");
    if (!OperatingSystem.IsWindows())
    {
        Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path), "monitor file is private");
        Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(Path.GetDirectoryName(path)!), "monitor directory is private");
    }
    var json = File.ReadAllText(path);
    OwnStatePaths.WritePrivate(path, "{broken-json-containing-synthetic-token");
    var parseError = Throws<InvalidDataException>(() => store.Read(account), "malformed secret has safe error");
    Check(!parseError.ToString().Contains("synthetic-token", StringComparison.Ordinal), "parse error cannot reveal body");
    OwnStatePaths.WritePrivate(path, json.Replace("acct-fireworks", "acct-swapped", StringComparison.Ordinal));
    Throws<InvalidDataException>(() => store.Read(account), "copied account file rejected");
    OwnStatePaths.WritePrivate(path, json);
    store.Delete(account.Id);
    Equal(null, store.Read(account), "local disconnect removes monitor");
    Check(secrets.ReadAccount(account.Id).Contains("synthetic-inference-b", StringComparison.Ordinal), "disconnect preserves inference");
    store.Delete(account.Id);
    Equal(null, store.Read(account), "disconnect is locally idempotent");

    if (!OperatingSystem.IsWindows())
    {
        var target = Path.Combine(root, "do-not-overwrite");
        File.WriteAllText(target, "unchanged");
        File.CreateSymbolicLink(path, target);
        Throws<IOException>(() => store.Read(account), "read symlink refused");
        Throws<IOException>(() => store.Write(account, credential), "write symlink refused");
        Throws<IOException>(() => store.Delete(account.Id), "delete symlink refused");
        Equal("unchanged", File.ReadAllText(target), "symlink target untouched");
        File.Delete(path);
        File.CreateSymbolicLink(path, Path.Combine(root, "missing-target"));
        Throws<IOException>(() => store.Write(account, credential), "dangling symlink refused");
        File.Delete(path);
    }

    var configuration = new ConfigurationRepository(paths);
    var cloudflare = new AccountDto("acct-cloudflare", "Cloudflare", "cloudflare-workers-ai", true, "api-key", "unverified");
    var gemini = new AccountDto("acct-gemini", "Gemini", "gemini-api", true, "api-key", "unverified");
    var groq = new AccountDto("acct-groq", "Groq", "groq", true, "api-key", "unverified");
    configuration.Update(0, state => state with { Accounts = [account, cloudflare, gemini, groq] });
    var now = new ManualClock(new DateTimeOffset(2026, 10, 24, 10, 0, 0, TimeSpan.Zero));
    var service = new MonitorAccessService(configuration, secrets, store, now);
    Equal("not-configured", service.Get(account.Id).State, "status does not contact provider");
    Equal("not-configured", service.Get(groq.Id).State, "optional Enterprise monitor is not implicitly enabled for passive Groq");
    Throws<KeyNotFoundException>(() => service.Get("acct-absent"), "unknown account rejected");
    Throws<ArgumentException>(() => service.Set(configuration.Snapshot.Revision, groq.Id, new("access-token-snapshot", "synthetic", "unused")), "unsupported Groq OAuth snapshot refused");
    var before = configuration.Snapshot;
    var configured = service.Set(before.Revision, account.Id, new("inference-key", ResourceId: "example-account"));
    Equal(before.Revision + 1, configured.Revision, "monitor mutation participates in revisions");
    Check(before.Accounts.SequenceEqual(configured.Accounts), "monitor setup creates no inference account or state mutation");
    Equal(null, service.GetCredential(account.Id)!.Secret, "reuse dynamically refers to inference key");
    Equal("configured-unverified", service.Get(account.Id).State, "saving is not permission verification");
    var revision = configured.Revision;
    service.Set(revision, account.Id, new("api-token", "synthetic-separate", "example-account"));
    Throws<RevisionConflictException>(() => service.Set(revision, account.Id, new("api-token", "synthetic-loser", "example-account")), "stale revision refused before credential write");
    Equal("synthetic-separate", service.GetCredential(account.Id)!.Secret, "revision loser cannot change private token");
    var statusJson = JsonSerializer.Serialize(service.Get(account.Id));
    Check(!statusJson.Contains("synthetic", StringComparison.Ordinal), "public status cannot expose credentials");
    Equal("MonitorAccessRequest [redacted]", new MonitorAccessRequest("api-token", "synthetic").ToString(), "setup request format redacted");
    Equal(0, MonitorAccessCatalog.Options("nvidia-build").Length, "no invented NVIDIA monitor auth");

    var cloudflareId = new string('a', 32);
    secrets.WriteAccount(cloudflare.Id, JsonSerializer.Serialize(new { kind = "api-key", apiKey = "synthetic-cf",
        baseUrl = "https://api.cloudflare.com/client/v4/accounts/" + cloudflareId + "/ai/v1", settings = new { accountId = cloudflareId } }));
    Throws<ArgumentException>(() => service.Set(configuration.Snapshot.Revision, cloudflare.Id, new("api-token", "synthetic-cf-monitor", new string('b', 32))), "Cloudflare cross-account setup refused");
    service.Set(configuration.Snapshot.Revision, cloudflare.Id, new("inference-key", ResourceId: cloudflareId.ToUpperInvariant()));
    Equal("inference-key", service.GetCredential(cloudflare.Id)!.Kind, "Cloudflare existing token can be reused");
    secrets.WriteAccount(cloudflare.Id, JsonSerializer.Serialize(new { kind = "api-key", apiKey = "synthetic-cf-new-account",
        baseUrl = "https://api.cloudflare.com/client/v4/accounts/" + new string('b', 32) + "/ai/v1" }));
    Equal("invalid", service.Get(cloudflare.Id).State, "changed inference account invalidates monitor binding");
    Throws<ArgumentException>(() => service.GetCredential(cloudflare.Id), "changed account cannot leak old monitor token to reader");
    secrets.WriteAccount(account.Id, JsonSerializer.Serialize(new { kind = "api-key", apiKey = "synthetic-fireworks", settings = new { accountId = "different-account" } }));
    Throws<ArgumentException>(() => service.GetCredential(account.Id), "Fireworks configured account mismatch refused");
    secrets.WriteAccount(account.Id, JsonSerializer.Serialize(new { kind = "api-key", apiKey = "synthetic-fireworks", settings = new { accountId = "example-account" } }));

    Throws<ArgumentException>(() => service.Set(configuration.Snapshot.Revision, gemini.Id, new("inference-key", ResourceId: "example-project")), "Gemini API key is not Monitoring OAuth access");
    Throws<ArgumentException>(() => service.Set(configuration.Snapshot.Revision, gemini.Id, new("access-token-snapshot", "synthetic-google", "example-project")), "Google snapshot expiry required");
    Throws<ArgumentException>(() => service.Set(configuration.Snapshot.Revision, gemini.Id, new("access-token-snapshot", "synthetic-google", "example-project", now.GetUtcNow())), "expired Google snapshot refused");
    Throws<ArgumentException>(() => service.Set(configuration.Snapshot.Revision, gemini.Id, new("access-token-snapshot", "synthetic-google", "1234567890", now.GetUtcNow().AddHours(1))), "Google numeric project needs separate identity resolution");
    service.Set(configuration.Snapshot.Revision, gemini.Id, new("access-token-snapshot", "synthetic-google", "example-project", now.GetUtcNow().AddHours(1)));
    Equal("configured-unverified", service.Get(gemini.Id).State, "snapshot scope is not claimed verified");
    var googlePath = Path.Combine(paths.Credentials, "monitors", gemini.Id + ".json");
    var beforeExpiryBytes = File.ReadAllBytes(googlePath);
    now.Advance(TimeSpan.FromHours(2));
    Equal("expired", service.Get(gemini.Id).State, "snapshot expiry requires reconnect");
    Equal("synthetic-google", service.GetCredential(gemini.Id)!.Secret, "expired snapshot projected without refresh");
    Check(beforeExpiryBytes.SequenceEqual(File.ReadAllBytes(googlePath)), "status and reader never rotate external snapshot");
    Check(!JsonSerializer.Serialize(service.Get(gemini.Id)).Contains("synthetic-google", StringComparison.Ordinal), "expired status remains secret-free");
    var disconnectRevision = configuration.Snapshot.Revision;
    Throws<RevisionConflictException>(() => service.Remove(disconnectRevision - 1, gemini.Id), "stale disconnect refused");
    service.Remove(disconnectRevision, gemini.Id);
    Equal("not-configured", service.Get(gemini.Id).State, "snapshot disconnect is local removal");
    Check(configuration.Snapshot.Accounts.Any(a => a.Id == gemini.Id), "disconnect retains inference account");

    var raceRevision = configuration.Snapshot.Revision;
    var writers = Enumerable.Range(0, 2).Select(i => Task.Run(() =>
    {
        try { service.Set(raceRevision, account.Id, new("api-token", "synthetic-writer-" + i, "example-account")); return true; }
        catch (RevisionConflictException) { return false; }
    })).ToArray();
    var outcomes = Task.WhenAll(writers).GetAwaiter().GetResult();
    Equal(1, outcomes.Count(won => won), "concurrent setup has one revision winner");
    var winningIndex = Array.IndexOf(outcomes, true);
    Equal("synthetic-writer-" + winningIndex, service.GetCredential(account.Id)!.Secret, "concurrent readers see the winning complete credential");

    Console.WriteLine($"PASS: {passed} monitor credential checks (synthetic only).");
}
finally { if (Directory.Exists(root)) Directory.Delete(root, true); }

void Check(bool condition, string label)
{
    if (!condition) throw new Exception("FAIL: " + label);
    passed++;
}
void Equal<T>(T expected, T actual, string label) => Check(EqualityComparer<T>.Default.Equals(expected, actual), label);
T Throws<T>(Action action, string label) where T : Exception
{
    try { action(); }
    catch (T error) { passed++; return error; }
    throw new Exception("FAIL: " + label);
}

sealed class ManualClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset current = now;
    public override DateTimeOffset GetUtcNow() => current;
    public void Advance(TimeSpan amount) => current += amount;
}
