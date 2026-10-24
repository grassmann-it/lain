using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Accounts.Cloud;
using Lain.Host.Accounts.Credentials;
using Lain.Host.Operations;

internal static class GoogleGcloudChecks
{
    internal static async Task<int> RunAsync()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("gcloud source fixture requires Linux.");
        var count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); count++; }
        async Task Reject(Func<Task> action, string name)
        { try { await action(); } catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or OperationCanceledException or JsonException or KeyNotFoundException) { count++; return; } throw new Exception(name); }
        var now = DateTimeOffset.Parse("2026-10-25T12:00:00Z"); var clock = new Gclock(now);
        var temporary = Path.Combine(Path.GetTempPath(), "lain-gcloud-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary); File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            var url = BrowserUrl();
            Check(GoogleGcloudProcessTransport.ValidateBrowserUrl(url) == url, "gcloud browser validates SDK-shaped PKCE loopback URL");
            Check(GoogleGcloudProcessTransport.ValidateBrowserUrl(url.Replace("localhost", "attacker.example")) is null, "gcloud redirect must stay local");
            Check(GoogleGcloudProcessTransport.ValidateBrowserUrl(url + "&code=secret") is null, "gcloud returned code is never a browser action");
            Check(GoogleGcloudProcessTransport.ValidateBrowserUrl(url + "&state=duplicate") is null, "gcloud duplicate OAuth query rejected");
            Check(GoogleGcloudProcessTransport.ValidateBrowserUrl(url.Replace(GoogleGcloudProcess.RegisteredClientId!, "foreign.apps.googleusercontent.com")) is null, "browser action must use installed SDK client identity");
            Check(GoogleGcloudProcessTransport.ValidateBrowserUrl(url.Replace("appengine.admin", "unexpected.scope")) is null, "browser action scope set must match the qualified SDK login");
            var accessJson = JsonSerializer.SerializeToUtf8Bytes(new { credential = new { access_token = "fixture-access", token_expiry = "2026-10-25T13:00:00Z", id_token = "discard-this" }, configuration = new { properties = new { core = new { account = "one@example.test" } } } });
            var access = GoogleGcloudProcess.ParseAccess(accessJson, now, "one@example.test");
            Check(access.ExpiresAt == now.AddHours(1) && !access.ToString().Contains("fixture-access"), "gcloud real expiry and redacted access projection");
            await Reject(() => Task.FromResult(GoogleGcloudProcess.ParseAccess(accessJson, now.AddHours(1))), "expired CLI snapshot never gains a guessed lifetime");
            await Reject(() => Task.FromResult(GoogleGcloudProcess.ParseAccess(accessJson, now, "other@example.test")), "wrong CLI account rejected");

            var sdkFixture = Path.Combine(temporary, "source-bridge.py");
            File.WriteAllText(sdkFixture, """
#!/usr/bin/python3
import os, sys, webbrowser, time, socket
_original_connect = socket.socket.connect
def local_only_connect(sock, address):
    if isinstance(address, tuple) and address[0] not in ('localhost', '127.0.0.1', '::1'):
        raise AssertionError('Source fixture attempted a non-loopback connection')
    return _original_connect(sock, address)
socket.socket.connect = local_only_connect
_original_lookup = socket.getaddrinfo
def local_only_lookup(host, *args, **kwargs):
    if host not in ('localhost', '127.0.0.1', '::1', None):
        raise AssertionError('Source fixture attempted non-loopback DNS')
    return _original_lookup(host, *args, **kwargs)
socket.getaddrinfo = local_only_lookup
assert 'LAIN_GCLOUD_TEST_SECRET' not in os.environ
assert 'HOME' not in os.environ and 'GOOGLE_APPLICATION_CREDENTIALS' not in os.environ
assert os.environ['CLOUDSDK_CONFIG'] == os.getcwd()
sys.path.insert(0, '/opt/google-cloud-cli/lib')
sys.path.insert(0, '/opt/google-cloud-cli/lib/third_party')
from googlecloudsdk.command_lib.util import check_browser
from googlecloudsdk.core.credentials.flow import FullWebFlow
from googlecloudsdk.core import config
from googlecloudsdk.core.credentials import gce_cache
assert not gce_cache.GetOnGCE()
assert check_browser.ShouldLaunchBrowser(True)
flow = FullWebFlow.from_client_config({'installed': {'client_id': config.CLOUDSDK_CLIENT_ID, 'client_secret': 'fixture-only', 'auth_uri': 'https://accounts.google.com/o/oauth2/auth', 'token_uri': 'https://oauth2.googleapis.com/token'}}, config.CLOUDSDK_SCOPES + (config.REAUTH_SCOPE,), autogenerate_code_verifier=True)
try:
    url, state = flow.authorization_url()
    assert webbrowser.open(url, new=1, autoraise=True)
    time.sleep(0.4)
    print('SDK_SOURCE_BRIDGE_OK', flush=True)
finally:
    flow.server.server_close()
""");
            File.SetUnixFileMode(sdkFixture, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Environment.SetEnvironmentVariable("LAIN_GCLOUD_TEST_SECRET", "fixture-inherited-secret");
            var browserReceived = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var profile = Path.Combine(temporary, "source-profile");
            var run = GoogleGcloudProcessTransport.RunAsync(sdkFixture, [], profile, value => browserReceived.TrySetResult(value), TimeSpan.FromSeconds(20), default);
            var first = await Task.WhenAny(run, browserReceived.Task).WaitAsync(TimeSpan.FromSeconds(15));
            if (first == run && !browserReceived.Task.IsCompleted)
            {
                using var failed = await run;
                throw new Exception("Isolated source fixture failed: " + Encoding.UTF8.GetString(failed.Stderr.Span));
            }
            var received = await browserReceived.Task;
            Check(!run.IsCompleted && GoogleGcloudProcessTransport.ValidateBrowserUrl(received) is not null, "real SDK source sends browser bridge before process completion without network");
            using (var output = await run) Check(output.ExitCode == 0 && Encoding.UTF8.GetString(output.Stdout.Span).Contains("SDK_SOURCE_BRIDGE_OK") && !Encoding.UTF8.GetString(output.Stdout.Span).Contains("accounts.google.com"), "typed browser action removed from private stdout result");
            Environment.SetEnvironmentVariable("LAIN_GCLOUD_TEST_SECRET", null);
            Check(!File.Exists(Path.Combine(profile, "lain-process.json")) && !File.Exists(Path.Combine(profile, "lain-browser")), "process/bridge markers removed after exit");
            var sleeper = Path.Combine(temporary, "sleep.py");
            File.WriteAllText(sleeper, "#!/usr/bin/python3\nimport time\ntime.sleep(60)\n");
            File.SetUnixFileMode(sleeper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
            {
                var watch = Stopwatch.StartNew();
                await Reject(() => GoogleGcloudProcessTransport.RunAsync(sleeper, [], profile, null, TimeSpan.FromSeconds(10), cancel.Token), "CLI cancellation terminates process");
                Check(watch.Elapsed < TimeSpan.FromSeconds(5), "CLI cancellation is bounded");
            }
            var linkProfile = Path.Combine(temporary, "link-profile"); Directory.CreateDirectory(linkProfile);
            File.CreateSymbolicLink(Path.Combine(linkProfile, "credentials.db"), Path.Combine(temporary, "outside.db"));
            await Reject(() => GoogleGcloudProcessTransport.RunAsync(sleeper, [], linkProfile, null, TimeSpan.FromSeconds(1), default), "CLI profile links cannot target another credential store");

            var fake = new FakeGcloud(now, url); var checkpoints = new List<PreparedLoginCredential>();
            var owned = Path.Combine(temporary, "owner");
            var context = Context(now, checkpoints, "operation-gcloud-1");
            string? selected = null; var currentSubject = "123456789";
            using var handler = new Ghandler(request =>
            {
                Check(request.Method == HttpMethod.Get && request.Headers.Authorization?.Parameter == "fixture-access", "gcloud discovery receives only projected access token");
                return request.RequestUri!.Host == "openidconnect.googleapis.com"
                    ? new { sub = currentSubject, email = "one@example.test", email_verified = true }
                    : (object)new { projects = new[] { new { name = "projects/123456", projectId = "project-one", state = "ACTIVE" }, new { name = "projects/654321", projectId = "project-two", state = "ACTIVE" } } };
            });
            await using (var adapter = new GoogleGcloudLoginAdapter(owned, _ => selected, fake, handler, clock))
            {
                var begun = await adapter.BeginAsync(context, default);
                Check(begun.State == "waiting" && begun.Url == url && fake.LoginCalls == 1, "official CLI method publishes browser action asynchronously");
                await adapter.BeginAsync(context, default);
                Check(fake.LoginCalls == 1, "gcloud begin replay never starts duplicate login");
                Check((await adapter.StatusAsync(context, default)).State == "waiting", "pending CLI does not publish credentials");
                fake.CompleteLogin();
                var picker = await adapter.StatusAsync(context, default);
                Check(picker.State == "resource-required" && picker.Resources!.Length == 2 && checkpoints.Count == 1 && checkpoints[0].Purposes.Length == 0, "gcloud grants checkpoint before verified project selection");
                var chosen = context with { Request = context.Request with { Binding = picker.Resources![1] } };
                var prepared = await adapter.StatusAsync(chosen, default);
                Check(prepared.State == "prepared" && prepared.Credential!.Owner == "google-gcloud" && prepared.Credential.Binding.ResourceId == "project-two", "selected project monitoring prepared with CLI owner");
                var credential = prepared.Credential!; selected = credential.PrivateJson;
                Check(!selected.Contains("refreshToken") && !selected.Contains("id_token") && selected.Contains("quotaProjectId"), "private generation contains profile reference and access only");
                var refresh = new CredentialRefreshContext("gcloud-account", new("gcloud-handle", 1, "google-gcloud", ["quota"], credential.Binding, credential.ExpiresAt), selected);
                var renewed = await adapter.RefreshAsync(refresh, default);
                Check(renewed.ExpiresAt == now.AddHours(1) && fake.RefreshCalls == 1 && renewed.Binding == credential.Binding, "CLI owner renews same user and project with provider expiry");
                currentSubject = "999999999";
                await Reject(() => adapter.RefreshAsync(refresh, default), "CLI refresh account subject cannot switch");
                currentSubject = "123456789";
                await adapter.ReleaseAsync(chosen, default);
                var reconnect = Context(now, [], "operation-gcloud-2") with { Request = context.Request with { Binding = credential.Binding } };
                await adapter.BeginAsync(reconnect, default);
                Check(fake.ExpectedAccount == "one@example.test" && fake.Profiles.Distinct().Count() == 2, "reconnect pins Google user and creates a distinct isolated profile");
                await adapter.CancelAsync(reconnect, default);
                Check((await adapter.StatusAsync(reconnect, default)).State == "cancelled", "cancelled CLI operation cannot publish late result");
                await adapter.TargetRemovedAsync(reconnect, null, default);
                Check(!Directory.Exists(fake.Profiles[1]), "target removal retires cancelled unpublished CLI profile without remote revocation");
                var reference = GoogleGcloudLoginAdapter.GetProfileReference(credential.PrivateJson);
                Check(!await adapter.DisconnectAsync(credential.PrivateJson, new HashSet<string> { reference }), "explicit retained recovery reference prevents profile retirement");
                var statePaths = new OwnStatePaths(Path.Combine(temporary, "retention-state"));
                var repository = new ConfigurationRepository(statePaths); var secrets = new SecretStore(statePaths);
                var handle = secrets.Generations.Prepare("gcloud-account", credential, "retention-operation");
                var oldAccount = new AccountDto("gcloud-account", "Fixture", "gemini-api", true, "gcloud-owned", "ready", Binding: credential.Binding,
                    Credentials: [handle], Capabilities: [new("quota", "ready", handle.Id)]);
                var retention = new GoogleGcloudProfileRetention(repository, secrets, statePaths, adapter);
                repository.Update(0, value => value with { Accounts = [oldAccount] });
                await retention.DisconnectAsync(oldAccount);
                Check(Directory.Exists(fake.Profiles[0]), "selected capability retains CLI-owned refresh profile");
                repository.Update(1, value => value with { Accounts = [] }); selected = null;
                var recoveryContext = Context(now, [], "retention-operation");
                CredentialRecoveryService.SaveStarted(secrets.Generations, recoveryContext);
                await retention.DisconnectAsync(oldAccount);
                Check(Directory.Exists(fake.Profiles[0]), "unknown remote recovery defers profile cleanup");
                CredentialRecoveryService.SavePrepared(secrets.Generations, recoveryContext, handle);
                await retention.DisconnectAsync(oldAccount);
                Check(Directory.Exists(fake.Profiles[0]), "unpublished prepared recovery retains CLI profile");
                CredentialRecoveryService.MarkPublished(secrets.Generations, "retention-operation");
                var malformed = secrets.Generations.RecoveryPath("malformed-fixture"); OwnStatePaths.WritePrivate(malformed, "{}");
                await retention.DisconnectAsync(oldAccount);
                Check(Directory.Exists(fake.Profiles[0]), "malformed recovery metadata fails closed");
                File.Delete(malformed);
                await retention.DisconnectAsync(oldAccount);
                Check(!Directory.Exists(fake.Profiles[0]), "after removal and resolved recovery old CLI refresh profile is retired");
            }
            Check(new GoogleGcloudProcess().IsAvailable, "installed SDK sources match the qualified linux-x64 release");
        }
        finally { Environment.SetEnvironmentVariable("LAIN_GCLOUD_TEST_SECRET", null); Directory.Delete(temporary, recursive: true); }
        return count;
    }
    private static LoginOperationContext Context(DateTimeOffset now, List<PreparedLoginCredential> saved, string id) => new(id, "gcloud-account", 1, 0,
        new("gemini-api", "official-cli", ["quota"]), now.AddMinutes(5))
        { CheckpointCredentialAsync = (credential, _) => { saved.Add(credential); return Task.CompletedTask; } };
    private static string BrowserUrl() => "https://accounts.google.com/o/oauth2/auth?client_id=" + GoogleGcloudProcess.RegisteredClientId + "&redirect_uri=http%3A%2F%2Flocalhost%3A8085%2F&response_type=code&scope=" + Uri.EscapeDataString(string.Join(' ', GoogleGcloudProcessTransport.ExpectedScopes)) + "&state=abcdefghijklmnop&code_challenge=" + new string('a', 43) + "&code_challenge_method=S256";
    private sealed class Gclock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private sealed class FakeGcloud(DateTimeOffset now, string url) : IGoogleGcloudProcess
    {
        public bool IsAvailable => true;
        public int LoginCalls, RefreshCalls;
        public string? ExpectedAccount;
        public List<string> Profiles = [];
        private TaskCompletionSource<GoogleGcloudAccess> login = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<GoogleGcloudAccess> LoginAsync(string profile, string? account, Action<string> browser, CancellationToken ct)
        {
            LoginCalls++; Profiles.Add(profile); ExpectedAccount = account;
            login = new(TaskCreationOptions.RunContinuationsAsynchronously); ct.Register(() => login.TrySetCanceled(ct)); browser(url); return login.Task;
        }
        public void CompleteLogin() => login.TrySetResult(new("one@example.test", "fixture-access", now.AddHours(1)));
        public Task<GoogleGcloudAccess> ReadAccessAsync(string profile, string account, CancellationToken ct)
        { RefreshCalls++; return Task.FromResult(new GoogleGcloudAccess(account, "fixture-access", now.AddHours(1))); }
    }
    private sealed class Ghandler(Func<HttpRequestMessage, object> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(response(request)), Encoding.UTF8, "application/json") });
    }
}
