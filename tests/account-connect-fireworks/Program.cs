using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Accounts.CliConnections;

var checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
void Denied(Action action, string message) { try { action(); } catch (Exception e) when (e is InvalidDataException or CliConnectionException or ArgumentException) { checks++; return; } throw new Exception(message); }
ReadOnlyMemory<byte> Bytes(string text) => Encoding.UTF8.GetBytes(text);
var accountJson = Bytes("{\"accounts\":[{\"name\":\"accounts/owned-a\"},{\"name\":\"accounts/owned-b\"}]}");
var resources = FireworksCliResult.Accounts(accountJson);
Check(resources.Length == 2 && resources[0].ResourceId == "owned-a" && resources[0].Product == "serverless", "account identity binds ordinary serverless product");
Denied(() => FireworksCliResult.Accounts(Bytes("{\"accounts\":[{\"name\":\"accounts/a\"}],\"nextPageToken\":\"more\"}")), "partial selection refused");
Denied(() => FireworksCliResult.Accounts(Bytes("[{\"name\":\"accounts/a\"},{\"name\":\"accounts/a\"}]")), "duplicates refused");
Denied(() => FireworksCliResult.Accounts(Bytes("[{\"name\":\"accounts/../other\"}]")), "path escape refused");
var normal = "{\"keyId\":\"new-owned-key\",\"displayName\":\"lain-operation1\",\"key\":\"fixture-ordinary-key\",\"isFirepass\":false}";
var credential = FireworksCliResult.Key(Bytes(normal), "owned-a", "lain-operation1", "operation1", "profile1", ["inference", "quota"]);
Check(credential.Owner == "static-key" && credential.RemoteId == "new-owned-key" && credential.PreserveForRecovery, "new owned key retained for recovery without refresh owner");
using (var payload = JsonDocument.Parse(credential.PrivateJson))
{ Check(payload.RootElement.GetProperty("settings").GetProperty("accountId").GetString() == "owned-a", "monitor account binding persisted"); Check(!payload.RootElement.TryGetProperty("refreshToken", out _), "OIDC refresh stays in private CLI store"); }
Denied(() => FireworksCliResult.Key(Bytes(normal.Replace("fixture-ordinary-key", "fpk_fixture")), "owned-a", "lain-operation1", "operation1", "profile1", ["inference"]), "FirePass prefix refused");
Denied(() => FireworksCliResult.Key(Bytes(normal.Replace("false", "true")), "owned-a", "lain-operation1", "operation1", "profile1", ["inference"]), "FirePass semantic type refused");
Denied(() => FireworksCliResult.Key(Bytes(normal.Replace("lain-operation1", "existing-key")), "owned-a", "lain-operation1", "operation1", "profile1", ["inference"]), "existing key cannot be imported as own issuance");
Denied(() => FireworksCliResult.Key(Bytes(normal.Replace("\"keyId\":", "\"key_id\":\"other\",\"keyId\":")), "owned-a", "lain-operation1", "operation1", "profile1", ["inference"]), "ambiguous field aliases refused");
var url = "https://fireworks-fixture.auth.us-west-2.amazoncognito.com/oauth2/authorize?client_id=fixture&response_type=code&redirect_uri=http%3A%2F%2Flocalhost%3A12345%2Fcallback&state=fixture";
Check(FireworksCliResult.AuthorizationUrl(url) == url, "official original challenge passes without reconstruction");
Denied(() => FireworksCliResult.AuthorizationUrl(url.Replace("amazoncognito.com", "amazoncognito.com.evil.invalid")), "issuer spoof refused");
Denied(() => FireworksCliResult.AuthorizationUrl(url + "&access_token=secret"), "token-bearing URL refused");
Denied(() => FireworksCliResult.AuthorizationUrl(url.Replace("localhost", "evil.invalid")), "nonlocal callback refused");
Check(FireworksCliProcess.Arguments(FireworksCommand.Signin, "enterprise-a").SequenceEqual(["signin", "enterprise-a"]), "enterprise SSO account is positional");
var create = FireworksCliProcess.Arguments(FireworksCommand.CreateKey, "owned-a", "lain-operation1");
Check(create.SequenceEqual(["api-key", "create", "--account-id", "owned-a", "--key-name", "lain-operation1", "-o", "json"]), "one named ordinary key; no default profile reliance or FirePass flag");
Denied(() => FireworksCliProcess.Arguments(FireworksCommand.CreateKey, "../other", "lain-operation1"), "CLI account injection refused");
Check(new FireworksCliLoginAdapter().IsAvailable == false, "absent installation cannot enable login");
var root = Path.Combine(Path.GetTempPath(), "lain-fireworks-fixture-" + Guid.NewGuid().ToString("N"));
try
{
 var store = new CliConnectionStateStore(root); var process = new FixtureProcess(accountJson, normal, url);
 await using var adapter = FireworksCliLoginAdapter.Fixture(store, process);
 var request = new ConnectAccountRequest("fireworks", "official-cli", ["inference", "quota"], Name: "Fixture");
 var context = new LoginOperationContext("operation1", "localaccount1", 7, 2, request, DateTimeOffset.UtcNow.AddMinutes(5));
 var checkpoints = 0; context = context with { CheckpointCredentialAsync = (c, ct) => { checkpoints++; return Task.CompletedTask; } };
 Check((await adapter.BeginAsync(context, default)).State == "waiting", "begin starts private login");
 var status = await adapter.StatusAsync(context, default);
 Check(status.State == "resource-required" && status.Resources?.Length == 2 && process.Creates == 0, "no key before explicit account choice");
 Denied(() => adapter.StatusAsync(context with { Revision = 8 }, default).GetAwaiter().GetResult(), "revision mismatch refused before issuance");
 var selected = context with { Request = context.Request with { Binding = status.Resources![0] } };
 Check((await adapter.StatusAsync(selected, default)).State == "waiting", "selected account starts exactly one issuance");
 var ready = await adapter.StatusAsync(selected, default);
 Check(ready.State == "prepared" && ready.Credential?.Binding.ResourceId == "owned-a" && process.Creates == 1 && checkpoints == 1, "durable checkpoint and selected resource before publication");
 Check((await adapter.StatusAsync(selected, default)).State == "prepared" && process.Creates == 1, "status replay never creates another key");
 Denied(() => adapter.StatusAsync(selected with { AccountGeneration = 3 }, default).GetAwaiter().GetResult(), "generation mismatch refused");
 await adapter.ReleaseAsync(selected, default);
}
finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
// A bound reconnect never substitutes a different remotely available account.
var mismatchRoot = root + "-mismatch";
try
{
 var store = new CliConnectionStateStore(mismatchRoot); var fake = new FixtureProcess(accountJson, normal, url);
 await using var adapter = FireworksCliLoginAdapter.Fixture(store, fake);
 var request = new ConnectAccountRequest("fireworks", "official-cli", ["inference"], AccountId: "existing", Binding: new("fireworks", ResourceType: "account", ResourceId: "missing"));
 var context = new LoginOperationContext("reconnect1", "existing", 9, 4, request, DateTimeOffset.UtcNow.AddMinutes(5));
 await adapter.BeginAsync(context, default);
 Check((await adapter.StatusAsync(context, default)).State == "recovery-required" && fake.Creates == 0, "mismatched reconnect account never creates a key");
 await adapter.ReleaseAsync(context, default);
}
finally { if (Directory.Exists(mismatchRoot)) Directory.Delete(mismatchRoot, true); }
Console.WriteLine($"PASS: {checks} Fireworks connection assertions; synthetic only, no provider calls.");

sealed class FixtureProcess(ReadOnlyMemory<byte> accounts, string key, string url) : IFireworksCliProcess
{
 public int Creates;
 public string? AuthorizationUrl(string directory) => url;
 public Task<CliPrivateOutput> RunAsync(FireworksCommand command, string directory, string? accountId, string? keyName, TimeSpan timeout, CancellationToken ct, Action<CliProcessIdentity>? started = null)
 {
  ct.ThrowIfCancellationRequested();
  var result = command switch { FireworksCommand.Version => "1.8.19\n", FireworksCommand.Signin => "", FireworksCommand.Accounts => Encoding.UTF8.GetString(accounts.Span), FireworksCommand.CreateKey => Create(), _ => throw new Exception() };
  return Task.FromResult(new CliPrivateOutput(0, Encoding.UTF8.GetBytes(result), []));
 }
 private string Create() { Creates++; return key; }
}
