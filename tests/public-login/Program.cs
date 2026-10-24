using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Accounts.PublicLogin;

var checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
LoginOperationContext Context(string provider = "openrouter", ResourceBindingDto? binding = null) => new(Guid.NewGuid().ToString("N"), "account-fixed", 7, 3,
    new(provider, "browser", ["quota"], AccountId: "account-fixed", Binding: binding), DateTimeOffset.UtcNow.AddMinutes(2));
Dictionary<string, string> Query(string value) => LoopbackLoginCallback.ParseQuery(new Uri(value).Query);
async Task Callback(LoginAdapterResult begin, string? stateOverride = null, string extra = "code=fixture-code", bool valid = true)
{
    var auth = Query(begin.Url!);
    var uri = auth.GetValueOrDefault("callback_url") ?? auth["redirect_uri"];
    using var browser = new HttpClient(new SocketsHttpHandler { UseProxy = false });
    using var response = await browser.GetAsync(uri + "?state=" + Uri.EscapeDataString(stateOverride ?? auth["state"]) + "&" + extra);
    Check(response.StatusCode == (valid ? HttpStatusCode.OK : HttpStatusCode.BadRequest), "callback acceptance");
    var page = await response.Content.ReadAsStringAsync();
    Check(!page.Contains("fixture-code") && !page.Contains(auth["state"]), "callback page redaction");
}
async Task<LoginAdapterResult> Poll(IAccountLoginAdapter adapter, LoginOperationContext context, CancellationToken ct = default)
{
    for (var i = 0; i < 100; i++)
    {
        var result = await adapter.StatusAsync(context, ct);
        if (result.State != "waiting") return result;
        await Task.Delay(5);
    }
    throw new Exception("callback did not settle");
}

// Real loopback callback, fake provider HTTP: PKCE, identity, scope and one-time adoption.
var checkpointed = false;
var fake = new FakeProvider { BeforeMetadata = () => Check(checkpointed, "issued key checkpoint precedes identity HTTP") };
using (var adapter = new OpenRouterLoginAdapter(fake))
{
    var context = Context(binding: new("openrouter", Subject: "user-a", ResourceType: "workspace", ResourceId: "workspace-a")) with
    { CheckpointCredentialAsync = (credential, _) => { Check(credential.PreserveForRecovery, "checkpoint is recoverable"); checkpointed = true; return Task.CompletedTask; } };
    var begin = await adapter.BeginAsync(context, default);
    Check(begin.State == "waiting" && !adapter.RequiresCore, "browser without core");
    var auth = Query(begin.Url!);
    Check(auth["required_workspace_id"] == "workspace-a" && auth["code_challenge_method"] == "S256", "workspace is enforced in consent");
    await Callback(begin, "wrong", valid: false);
    Check((await adapter.StatusAsync(context, default)).State == "waiting" && fake.Posts == 0, "wrong state never exchanges");
    await Callback(begin, extra: "code=fixture-code&code=replay", valid: false);
    await Callback(begin);
    var result = await Poll(adapter, context);
    Check(result.State == "prepared" && result.Credential?.Owner == "static-key" && result.Credential.PreserveForRecovery, "one-time key privately prepared");
    Check(result.Credential!.Purposes.SequenceEqual(new[] { "quota" }) && result.Credential.Binding.Subject == "user-a", "exact scope and identity");
    Check(!JsonSerializer.Serialize(result with { Credential = null }).Contains("fixture-key"), "safe operation metadata");
    using var posted = JsonDocument.Parse(fake.LastBody!);
    var verifier = posted.RootElement.GetProperty("code_verifier").GetString()!;
    var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    Check(challenge == auth["code_challenge"] && posted.RootElement.GetProperty("code").GetString() == "fixture-code", "S256 exchange binding");
    Check((await adapter.StatusAsync(context, default)).State == "prepared" && fake.Posts == 1, "poll replay never repeats issuance");
    try { await adapter.StatusAsync(context with { AccountGeneration = 4 }, default); throw new Exception("generation accepted"); }
    catch (InvalidOperationException) { checks++; }
}

// A foreign workspace is preserved for explicit recovery, never published to the target.
using (var adapter = new OpenRouterLoginAdapter(new FakeProvider { Workspace = "other" }))
{
    var context = Context(binding: new("openrouter", ResourceType: "workspace", ResourceId: "workspace-a"));
    var begin = await adapter.BeginAsync(context, default); await Callback(begin);
    var result = await Poll(adapter, context);
    Check(result.State == "recovery-required" && result.Credential?.Binding.ResourceId == "other", "foreign workspace blocks publication");
}
// Unknown metadata preserves a new key and unknown identity; it does not invent one.
using (var adapter = new OpenRouterLoginAdapter(new FakeProvider { Metadata = false }))
{
    var context = Context(); var begin = await adapter.BeginAsync(context, default); await Callback(begin);
    var result = await Poll(adapter, context);
    Check(result.State == "prepared" && result.Credential!.Binding.Subject is null && result.Credential.Binding.Provenance == "unverified", "unknown metadata remains connected");
}
foreach (var mode in new[] { "lost", "redirect", "oversize", "malformed" })
{
    var transport = new FakeProvider { Mode = mode };
    using var adapter = new OpenRouterLoginAdapter(transport);
    var context = Context(); var begin = await adapter.BeginAsync(context, default); await Callback(begin);
    Check((await Poll(adapter, context)).State == "recovery-required", "uncertain exchange " + mode);
    await adapter.StatusAsync(context, default);
    Check(transport.Posts == 1, "uncertain exchange is never retried " + mode);
}
// Deny and cancellation never issue a key.
using (var adapter = new OpenRouterLoginAdapter(new FakeProvider()))
{
    var context = Context(); var begin = await adapter.BeginAsync(context, default); await Callback(begin, extra: "error=access_denied");
    Check((await Poll(adapter, context)).State == "failed", "denied consent");
    context = Context(); begin = await adapter.BeginAsync(context, default); await adapter.CancelAsync(context, default);
    Check((await adapter.StatusAsync(context, default)).State == "cancelled", "cancel closes operation");
}
// Operation expiry releases callbacks without an exchange, even without a status caller.
using (var adapter = new OpenRouterLoginAdapter(new FakeProvider()))
{
    var context = Context() with { ExpiresAt = DateTimeOffset.UtcNow.AddMilliseconds(50) };
    await adapter.BeginAsync(context, default); await Task.Delay(100);
    Check((await adapter.StatusAsync(context, default)).State == "cancelled", "callback TTL");
}
// Once sent, cancellation cannot discard an issued key: finish and return it to the recovery owner.
var delayed = new FakeProvider { Mode = "delayed" };
using (var adapter = new OpenRouterLoginAdapter(delayed))
{
    var context = Context(); var begin = await adapter.BeginAsync(context, default); await Callback(begin);
    using var cancel = new CancellationTokenSource();
    var status = Poll(adapter, context, cancel.Token);
    await delayed.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2)); cancel.Cancel(); await adapter.CancelAsync(context, default); delayed.Release.TrySetResult();
    Check((await status).Credential?.PreserveForRecovery == true, "late issued key survives cancellation");
}
// Availability is maintainer-owned; users do not register a client.
using (var unavailable = new PoeLoginAdapter())
{
    Check(!unavailable.IsAvailable, "Poe unavailable without maintainer client");
    Check((await unavailable.BeginAsync(Context("poe"), default)).NextAction == "use-api-key", "Poe API-key fallback");
}
var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start(); var poePort = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
using (var adapter = new PoeLoginAdapter(new("maintainer-fixture", new Uri($"http://127.0.0.1:{poePort}/poe/callback"), true), new FakeProvider { Poe = true }))
{
    var context = Context("poe"); var begin = await adapter.BeginAsync(context, default); var auth = Query(begin.Url!);
    Check(auth["scope"] == "apikey:create" && auth["client_id"] == "maintainer-fixture", "Poe official scope and maintainer ID");
    await Callback(begin);
    var result = await Poll(adapter, context);
    Check(result.State == "prepared" && result.Credential!.ExpiresAt > DateTimeOffset.UtcNow && result.Credential.Binding.Subject is null, "Poe expiry and unverified identity");
}
Console.WriteLine($"Public login: {checks} checks passed; fake provider HTTP only.");

sealed class FakeProvider : HttpMessageHandler
{
    public int Posts;
    public string? LastBody;
    public string Workspace = "workspace-a";
    public bool Metadata = true;
    public Action? BeforeMetadata;
    public bool Poe;
    public string Mode = "";
    public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Method == HttpMethod.Post)
        {
            Posts++; LastBody = await request.Content!.ReadAsStringAsync(ct);
            if (Mode == "lost") throw new HttpRequestException("private provider failure text");
            if (Mode == "delayed") { Entered.TrySetResult(); await Release.Task; }
            if (Mode == "redirect") return new(HttpStatusCode.Found) { Headers = { Location = new Uri("https://evil.invalid") } };
            if (Mode == "oversize") return Json(new string('a', 70000));
            if (Mode == "malformed") return Json("{}");
            return Json(Poe ? "{\"api_key\":\"fixture-key\",\"api_key_expires_in\":3600}" : "{\"key\":\"fixture-key\"}");
        }
        BeforeMetadata?.Invoke();
        if (request.RequestUri!.AbsoluteUri != OpenRouterLoginAdapter.KeyMetadataEndpoint || request.Headers.Authorization?.Parameter != "fixture-key")
            throw new Exception("unexpected credential destination");
        return Metadata ? Json(JsonSerializer.Serialize(new { data = new { creator_user_id = "user-a", workspace_id = Workspace } })) : new(HttpStatusCode.Forbidden);
    }
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
}
