using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Inspector;
using Lain.Host.Models;
using Lain.Host.Operations;
using Lain.Host.Providers;
using Lain.Host.Quota;
using Lain.Host.Usage;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.WebHost.UseUrls("http://127.0.0.1:18418");
builder.Logging.ClearProviders(); // Never log request bodies, credentials or upstream exception text.
var paths = new OwnStatePaths();
using var ownership = new FileStream(Path.Combine(paths.Root, "host.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
var secrets = new SecretStore(paths);
var hostToken = secrets.GetOrCreateToken(paths.ApiToken);
secrets.GetOrCreateToken(paths.InferenceToken);
var managementToken = secrets.GetOrCreateToken(paths.ManagementToken);
var repository = new ConfigurationRepository(paths);
var binary = Environment.GetEnvironmentVariable("LAIN_PROXY_PATH") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../proxy/lain-proxy"));
var manifestPath = Path.Combine(Path.GetDirectoryName(binary)!, "binary-manifest.json");
var hash = "";
if (File.Exists(manifestPath)) { using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath)); hash = manifest.RootElement.GetProperty("sha256").GetString() ?? ""; }
// This flag is shipped only with the independently qualified build, never inferred from a hash.
var qualificationPath = Path.Combine(Path.GetDirectoryName(binary)!, "qualification.json");
var qualified = false;
if (File.Exists(qualificationPath)) { using var doc = JsonDocument.Parse(File.ReadAllText(qualificationPath)); qualified = doc.RootElement.TryGetProperty("sha256", out var sha) && sha.GetString() == hash && doc.RootElement.TryGetProperty("passed", out var pass) && pass.ValueKind == JsonValueKind.True;
 if (!qualified && Environment.GetEnvironmentVariable("LAIN_REFERENCE_QUALIFICATION") == "1" && paths.Root.StartsWith(Path.GetTempPath(), StringComparison.Ordinal))
  qualified = doc.RootElement.TryGetProperty("sha256", out sha) && sha.GetString() == hash && doc.RootElement.TryGetProperty("syntheticPassed", out var synthetic) && synthetic.ValueKind == JsonValueKind.True;
}
var proxy = new OwnedProxyLifecycle(paths, secrets, new(binary, hash, qualified));
builder.Services.AddSingleton(paths);
builder.Services.AddSingleton(secrets);
builder.Services.AddSingleton(repository);
builder.Services.AddSingleton(proxy);
// Resolve the embedded catalog explicitly: DI would otherwise prefer the enumerable
// constructor and silently create an empty registry from unregistered descriptors.
builder.Services.AddSingleton(_ => new ProviderCatalogRegistry());
builder.Services.AddSingleton<ProviderCostPolicy>();
builder.Services.AddSingleton<AccountService>();
var catalogPath = Path.Combine(Path.GetDirectoryName(binary)!, "codex-model-catalog.json");
string[] codexModels = [];
if (File.Exists(catalogPath))
{
 using var catalog = JsonDocument.Parse(File.ReadAllText(catalogPath));
 codexModels = catalog.RootElement.GetProperty("models").EnumerateArray().Select(model => model.GetProperty("id").GetString()!).ToArray();
}
builder.Services.AddSingleton(new LegacyImportService(paths, repository, secrets, codexModels));
using var loginBackend = new HttpCodexOwnedLoginBackend(paths, secrets);
var loginAdapter = new CodexOwnedLoginAdapter(loginBackend, paths, repository, secrets, codexModels);
builder.Services.AddSingleton(new LoginOperationService([loginAdapter]));
builder.Services.AddSingleton<ModelsService>();
builder.Services.AddSingleton<CodexProfileAdapter>();
builder.Services.AddSingleton<ProxyConfigurationWriter>();
builder.Services.AddSingleton<SqliteUsageStore>(_ => new(Path.Combine(paths.Root, "usage.db")));
builder.Services.AddSingleton<IUsageStore>(sp => sp.GetRequiredService<SqliteUsageStore>());
builder.Services.AddSingleton<IUsageAnalysisStore>(sp => sp.GetRequiredService<SqliteUsageStore>());
builder.Services.AddSingleton<JournalUsageCollector>(sp => {
 var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { BaseAddress = new Uri("http://127.0.0.1:18417"), Timeout = TimeSpan.FromSeconds(10) };
 http.DefaultRequestHeaders.Authorization = new("Bearer", managementToken);
 return new(http, sp.GetRequiredService<SqliteUsageStore>(), sp.GetRequiredService<ILogger<JournalUsageCollector>>());
});
builder.Services.AddHostedService(sp => sp.GetRequiredService<JournalUsageCollector>());
builder.Services.AddInspector(managementToken);
var providerHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
var quotaSource = new HostQuotaSource(repository, secrets);
var quota = new QuotaService(quotaSource, quotaSource,
 [new CodexQuotaAdapter(providerHttp), new CommandCodeQuotaAdapter(providerHttp),
  new KimiQuotaAdapter(providerHttp), new ZaiQuotaAdapter(providerHttp),
  new StepFunQuotaAdapter(providerHttp), new PoeQuotaAdapter(providerHttp),
  new OpenRouterQuotaAdapter(providerHttp), new SyntheticQuotaAdapter(providerHttp)]);
builder.Services.AddSingleton(quota);
var app = builder.Build();
using var mutations = new SemaphoreSlim(1, 1);
app.Use(async (context, next) => {
 if (context.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote)) { context.Response.StatusCode = 403; return; }
 var supplied = context.Request.Headers.Authorization.ToString();
 var expected = "Bearer " + hostToken;
 if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(expected))) { context.Response.StatusCode = 401; await context.Response.WriteAsJsonAsync(new ApiError("unauthorized", "A local lain management token is required.")); return; }
 context.Response.Headers.CacheControl = "no-store";
 var mutation = context.Request.Method is not ("GET" or "HEAD") || context.Request.Path.StartsWithSegments(Api.Prefix + "/accounts/login");
 if (mutation) await mutations.WaitAsync(context.RequestAborted);
 try { await next(context); }
 catch (RevisionConflictException) { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new ApiError("revision_conflict", "Configuration changed; reload and retry.")); }
 catch (KeyNotFoundException) { context.Response.StatusCode = 404; await context.Response.WriteAsJsonAsync(new ApiError("not_found", "The requested item does not exist.")); }
 catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new ApiError("validation", "Invalid request values; check dates, identities and selected accounts.")); }
 catch (InvalidOperationException) { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new ApiError("operation_unavailable", "Operation unavailable; check backend qualification, configuration and port availability.")); }
 catch (Exception) { context.Response.StatusCode = 500; await context.Response.WriteAsJsonAsync(new ApiError("operation_failed", "The operation failed. Check lain paths, credentials and service readiness.")); }
 finally { if (mutation) mutations.Release(); }
});
var runtimeManifest = Path.Combine(paths.Proxy, "runtime.json");
void RequireAppliedRuntime()
{
 if (!File.Exists(runtimeManifest)) throw new InvalidOperationException("No applied runtime exists.");
 using var document = JsonDocument.Parse(File.ReadAllText(runtimeManifest));
 var runtime = document.RootElement;
 if (!runtime.GetProperty("valid").GetBoolean() || runtime.GetProperty("revision").GetInt64() != repository.Snapshot.AppliedRevision)
  throw new InvalidOperationException("Runtime application is incomplete.");
}
StatusDto Status()
{
 var config = repository.Snapshot;
 var ps = proxy.Status;
 var active = AppliedProfileStatus.Read(Path.Combine(paths.Root, "active-profile.json"), runtimeManifest, config.AppliedRevision);
 return new(Api.Version, "0.1.0", config.Revision != config.AppliedRevision ? "pending-application" : ps.State,
  ps.Ready, config.Revision, config.AppliedRevision, DateTimeOffset.UtcNow, ps.Error, active.Id, active.Revision);
}
// Matching profile identity and revision in both metadata files reject partial
// publication. Reads stay responsive while another request refreshes provider quotas.
app.MapGet(Api.Prefix + "/status", Status);
app.MapGet(Api.Prefix + "/configuration", () => repository.Snapshot);
app.MapGet(Api.Prefix + "/accounts", (AccountService svc) => svc.List());
app.MapPost(Api.Prefix + "/accounts", (RevisionRequest<AddApiAccountRequest> request, AccountService svc) => svc.AddApi(request.Revision, request.Value));
app.MapPut(Api.Prefix + "/accounts/{id}/enabled", (string id, RevisionRequest<bool> request, AccountService svc) => svc.SetEnabled(request.Revision, id, request.Value));
app.MapDelete(Api.Prefix + "/accounts/{id}", (string id, long revision, AccountService svc) => svc.Remove(revision, id));
app.MapPost(Api.Prefix + "/accounts/{id}/check", (string id, AccountService svc) => svc.Check(id));
app.MapPost(Api.Prefix + "/import", (ImportRequest request, LegacyImportService svc) => svc.Import(request));
app.MapPost(Api.Prefix + "/accounts/login", async (LoginRequest request, LoginOperationService svc, ProxyConfigurationWriter writer, CancellationToken ct) => {
 if (request.Revision != repository.Snapshot.Revision) throw new RevisionConflictException(request.Revision, repository.Snapshot.Revision);
 if (!proxy.Status.Ready)
 {
  if (File.Exists(runtimeManifest)) { RequireAppliedRuntime(); await proxy.StartAsync(ct); }
  else
  {
   var empty = new ConfigurationDto(repository.Snapshot.Revision, repository.Snapshot.AppliedRevision, [], [], []);
   var identities = writer.Write(empty);
   new RouteBindingsWriter(paths).Write(empty, identities);
   await proxy.StartAsync(ct);
  }
 }
 return await svc.BeginAsync(request.Provider, ct);
});
app.MapGet(Api.Prefix + "/accounts/login/{id}", (string id, LoginOperationService svc, CancellationToken ct) => svc.StatusAsync(id, ct));
app.MapDelete(Api.Prefix + "/accounts/login/{id}", (string id, LoginOperationService svc, CancellationToken ct) => svc.CancelAsync(id, ct));
app.MapModelEndpoints();
app.MapProviderCatalogEndpoints();
app.MapInspectorEndpoints();
app.MapPost(Api.Prefix + "/profiles/{id}/apply", async (string id, ApplyRequest request, ModelsService models, ProxyConfigurationWriter writer, CancellationToken ct) => {
 var selected = models.ResolveForApply(id, request.Revision);
 var activeRoutes = selected.Routes.Where(r => r.Enabled).ToArray();
 var accountIds = activeRoutes.SelectMany(r => r.AccountIds).ToHashSet();
 var runtime = selected.Configuration with { Routes = activeRoutes, Accounts = selected.Configuration.Accounts.Where(a => accountIds.Contains(a.Id)).ToArray() };
 await proxy.StopAsync(ct); // Stop before changing files observed by the Core's watcher.
 OwnStatePaths.WritePrivate(runtimeManifest, JsonSerializer.Serialize(new { valid = false, revision = request.Revision, profileId = id }));
 var identities = writer.Write(runtime);
 new RouteBindingsWriter(paths).Write(runtime, identities, writer.RouteCredentials,
  profileId: selected.Profile.Id, qualifiedProviderRouteIds: writer.QualifiedProviderRouteIds);
 await proxy.StartAsync(ct);
 OwnStatePaths.WritePrivate(Path.Combine(paths.Root, "active-profile.json"), JsonSerializer.Serialize(new { id, revision = request.Revision }));
 repository.MarkApplied(request.Revision);
 OwnStatePaths.WritePrivate(runtimeManifest, JsonSerializer.Serialize(new { valid = true, revision = request.Revision, profileId = id }));
 return repository.Snapshot;
});
app.MapGet(Api.Prefix + "/usage", (HttpRequest request, IUsageStore store, CancellationToken ct) => store.SummaryAsync(ParseQuery(request), ct));
app.MapGet(Api.Prefix + "/usage/analysis", (HttpRequest request, IUsageAnalysisStore store, CancellationToken ct) => store.AnalysisAsync(ParseQuery(request), ct));
app.MapGet(Api.Prefix + "/attempts", (HttpRequest request, IUsageStore store, CancellationToken ct) => store.AttemptsAsync(ParseQuery(request), ct));
app.MapGet(Api.Prefix + "/quotas", (CancellationToken ct) => quota.GetSnapshotsAsync(ct));
app.MapPost(Api.Prefix + "/quotas/refresh", (QuotaRefreshRequest request, CancellationToken ct) => quota.RefreshAsync(request.AccountId, ct));
app.MapGet(Api.Prefix + "/auth/token", (string audience) => audience == "codex" ? Results.Ok(new { token = secrets.GetOrCreateToken(paths.InferenceToken) }) : Results.BadRequest(new ApiError("invalid_audience", "Only the codex inference audience is supported.")));
app.MapPost(Api.Prefix + "/service/{action}", async (string action, CancellationToken ct) => {
 switch (action) { case "start": RequireAppliedRuntime(); await proxy.StartAsync(ct); break; case "stop": await proxy.StopAsync(ct); break; case "restart": RequireAppliedRuntime(); await proxy.RestartAsync(ct); break; default: throw new ArgumentException("Unknown proxy action."); }
 return Status();
});
using var shutdown = new CancellationTokenSource();
var referenceWithoutQuota = Environment.GetEnvironmentVariable("LAIN_REFERENCE_QUALIFICATION") == "1" && paths.Root.StartsWith(Path.GetTempPath(), StringComparison.Ordinal) && Environment.GetEnvironmentVariable("LAIN_REFERENCE_DISABLE_QUOTA") == "1";
var quotaLoop = referenceWithoutQuota ? Task.CompletedTask : quota.RunAsync(shutdown.Token);
if (qualified && File.Exists(Path.Combine(paths.Root, "active-profile.json")))
{
 try {
  using var active = JsonDocument.Parse(File.ReadAllText(Path.Combine(paths.Root, "active-profile.json")));
  if (active.RootElement.GetProperty("revision").GetInt64() != repository.Snapshot.AppliedRevision) throw new InvalidOperationException();
  RequireAppliedRuntime(); await proxy.StartAsync(shutdown.Token);
 } catch (Exception) { /* API remains available for repair. */ }
}
try { await app.RunAsync(); }
finally { shutdown.Cancel(); try { await quotaLoop; } catch (OperationCanceledException) {} await proxy.DisposeAsync(); providerHttp.Dispose(); }

static UsageQuery ParseQuery(HttpRequest request)
{
 string? Get(string name, int maximumLength = 256)
 {
  if (!request.Query.TryGetValue(name, out var values)) return null;
  if (values.Count != 1 || values[0] is not { } value || string.IsNullOrWhiteSpace(value) ||
      value.Length > maximumLength || value.Any(char.IsControl)) throw new ArgumentException("Invalid usage query value.");
  return value;
 }
 DateTimeOffset? Date(string name) => Get(name, 128) is { } value
  ? DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal) : null;
 int? Number(string name) => Get(name, 24) is { } value ? int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture) : null;
 var from = Date("from"); var to = Date("to");
 var end = to ?? DateTimeOffset.UtcNow; var start = from ?? end.AddDays(-30);
 if (start >= end || end - start > TimeSpan.FromDays(366)) throw new ArgumentException("Invalid usage interval.");
 var timeZone = Get("timeZone", 128) ?? "UTC";
 try { _ = TimeZoneInfo.FindSystemTimeZoneById(timeZone); }
 catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException)
 { throw new ArgumentException("Unknown usage time zone."); }
 var offset = Number("offset") ?? 0; var limit = Number("limit") ?? 100;
 var statusCode = Number("statusCode"); var groupLimit = Number("groupLimit") ?? 100;
 if (offset < 0 || limit is < 1 or > 500 || statusCode is < 100 or > 599 || groupLimit is < 1 or > 500)
  throw new ArgumentException("Invalid usage page or group bound.");
 var groupBy = Get("groupBy", 512);
 if (groupBy is not null)
 {
  string[] allowed = ["accountId", "routeId", "model", "provider", "modelFamily", "manufacturer",
   "inferenceProvider", "gateway", "billingAccountId", "reasoningEffort", "effectiveReasoningEffort", "mode",
   "conversationId", "turnId", "workerId", "parentWorkerId", "parentRequestId", "toolCallId",
   "harness", "profileId", "responseId", "previousResponseId"];
  var groups = groupBy.Split(',', StringSplitOptions.TrimEntries);
  if (groups.Length is < 1 or > 3 || groups.Distinct(StringComparer.Ordinal).Count() != groups.Length ||
      groups.Any(group => !allowed.Contains(group, StringComparer.Ordinal))) throw new ArgumentException("Invalid usage grouping.");
  groupBy = string.Join(',', groups);
 }
 return new(from, to, timeZone, Get("accountId"), Get("model"), statusCode, offset, limit,
  RouteId: Get("routeId"), ModelFamily: Get("modelFamily"), Manufacturer: Get("manufacturer"),
  InferenceProvider: Get("inferenceProvider"), Gateway: Get("gateway"), BillingAccountId: Get("billingAccountId"),
  ReasoningEffort: Get("reasoningEffort"), Mode: Get("mode"), ConversationId: Get("conversationId"),
  TurnId: Get("turnId"), WorkerId: Get("workerId"), Harness: Get("harness"), ProfileId: Get("profileId"),
  GroupBy: groupBy, GroupLimit: groupLimit, EffectiveReasoningEffort: Get("effectiveReasoningEffort"));
}
record ApplyRequest(long Revision);
record LoginRequest(long Revision, string Provider);
record QuotaRefreshRequest(string? AccountId);
sealed class HostQuotaSource(ConfigurationRepository repository, SecretStore secrets) : IQuotaAccountSource, IQuotaCredentialSource
{
 public Task<IReadOnlyList<AccountDto>> GetAccountsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AccountDto>>(repository.Snapshot.Accounts);
 public Task<QuotaCredential?> GetCredentialAsync(string accountId, CancellationToken ct = default) => Task.FromResult(secrets.HasAccount(accountId) ? QuotaCredential.FromSecretJson(secrets.ReadAccount(accountId)) : null);
}
