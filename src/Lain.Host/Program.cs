using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Accounts.Credentials;
using Lain.Host.Accounts.Cloud;
using Lain.Host.Accounts.CliConnections;
using Lain.Host.Accounts.ConsoleConnections.MiMo;
using Lain.Host.Accounts.Monitoring;
using Lain.Host.Inspector;
using Lain.Host.Models;
using Lain.Host.Operations;
using Lain.Host.Providers;
using Lain.Host.Providers.Xai;
using Lain.Host.Quota;
using Lain.Host.Usage;
using Lain.Host.Workers;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Logging.ClearProviders(); // Never log request bodies, credentials or upstream exception text.
var paths = new OwnStatePaths();
var referenceIsolation = Environment.GetEnvironmentVariable("LAIN_REFERENCE_QUALIFICATION") == "1" &&
 paths.Root.StartsWith(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal);
var hostPort = OwnStatePaths.HostPort;
if (referenceIsolation && Environment.GetEnvironmentVariable("LAIN_REFERENCE_HOST_PORT") is { } referencePort)
{
 if (!int.TryParse(referencePort, out hostPort) || hostPort is < 1024 or > 65535 || hostPort == paths.GatewayPort)
  throw new InvalidOperationException("Invalid isolated reference Host port.");
}
builder.WebHost.UseUrls($"http://127.0.0.1:{hostPort}");
using var ownership = new FileStream(Path.Combine(paths.Root, "host.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
var secrets = new SecretStore(paths);
var hostToken = secrets.GetOrCreateToken(paths.ApiToken);
var managementToken = secrets.GetOrCreateToken(paths.ManagementToken);
var repository = new ConfigurationRepository(paths);
using var mutations = new SemaphoreSlim(1, 1);
var restoreReady = true;
try { RestoredCredentialQuarantine.PrepareForRestore(paths); }
catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
{ restoreReady = false; } // Keep local repair/history available; never enumerate restored grants in Core.
var binary = Environment.GetEnvironmentVariable("LAIN_PROXY_PATH") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../proxy/lain-proxy"));
var proxy = new OwnedProxyLifecycle(paths, secrets, new(binary, "", false), () => ProxyBuildMetadata.Read(paths, binary));
bool GatewayMode() => repository.Snapshot.RuntimeMode == "gateway";
bool CollectCore() => GatewayMode() && proxy.Status.Ready;
void RequireGateway()
{
 if (!GatewayMode()) throw new InvalidOperationException("This operation requires Gateway mode.");
 if (!restoreReady) throw new InvalidOperationException("Restored credential quarantine requires repair.");
}
builder.Services.AddSingleton(paths);
builder.Services.AddSingleton(secrets);
builder.Services.AddSingleton(repository);
builder.Services.AddSingleton(proxy);
// Resolve the embedded catalog explicitly: DI would otherwise prefer the enumerable
// constructor and silently create an empty registry from unregistered descriptors.
builder.Services.AddSingleton(_ => new ProviderCatalogRegistry());
builder.Services.AddSingleton<ProviderCostPolicy>();
builder.Services.AddSingleton<AccountService>();
var monitorStore = new MonitorCredentialStore(paths);
var monitorAccess = new MonitorAccessService(repository, secrets, monitorStore);
builder.Services.AddSingleton(monitorStore);
builder.Services.AddSingleton(monitorAccess);
var catalogPath = Path.Combine(Path.GetDirectoryName(binary)!, "codex-model-catalog.json");
string[] codexModels = [];
if (GatewayMode() && File.Exists(catalogPath))
{
 try
 {
  if (new FileInfo(catalogPath).Length > 4 * 1024 * 1024) throw new InvalidDataException();
  using var catalog = JsonDocument.Parse(File.ReadAllText(catalogPath));
  codexModels = catalog.RootElement.GetProperty("models").EnumerateArray()
   .Select(model => model.GetProperty("id").GetString()!).Where(id => !string.IsNullOrWhiteSpace(id)).ToArray();
 }
 catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException)
 { codexModels = []; } // Optional model discovery must not prevent account repair or history reads.
}
builder.Services.AddSingleton(new LegacyImportService(paths, repository, secrets, codexModels));
using var loginBackend = new HttpCodexOwnedLoginBackend(paths, secrets);
var loginAdapter = new CodexOwnedLoginAdapter(loginBackend, paths, repository, secrets, codexModels);
using var openRouterLogin = new OpenRouterLoginAdapter();
using var nanoGptLogin = new NanoGptLoginAdapter();
await using var kiloLogin = new Lain.Host.Accounts.Kilo.KiloDeviceLoginAdapter(configuration: repository, secrets: secrets);
using var kimiLogin = new Lain.Host.Accounts.Managed.KimiDeviceLoginAdapter();
bool FireworksCliPublished(CliConnectionState state)
{
 if (state.Provider != "fireworks" || state.Method != "official-cli") return false;
 var account = repository.Snapshot.Accounts.SingleOrDefault(account => account.Id == state.AccountId);
 if (account is null || AccountService.CanonicalProviderId(account.Provider) != "fireworks") return false;
 foreach (var handle in account.Credentials ?? [])
 {
  if (handle.Owner != "static-key" || account.Capabilities?.Any(capability => capability.CredentialId == handle.Id) != true) continue;
  try
  {
   var generation = secrets.Generations.Read(account.Id, handle);
   if (generation.OperationId == state.OperationId && generation.RemoteId is not null && generation.RemoteId == state.RemoteKeyId)
    return true;
  }
  catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { }
 }
 return false;
}
FireworksCliLoginAdapter CreateFireworksCliLogin()
{
 try
 {
  var state = new CliConnectionStateStore(Path.Combine(paths.Root, "accounts", "fireworks-cli"), FireworksCliPublished);
  // Only lain's private installed artifact can enable this method; PATH and another
  // CLI's store are never used. Reconciliation cannot start a new provider operation.
  state.ReconcileAfterRestart();
  var qualification = new FireworksCliQualification(Path.Combine(paths.Root, "tools", "fireworks", "firectl-1.8.19"));
  return new FireworksCliLoginAdapter(state, qualification);
 }
 catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or CliConnectionException or ArgumentException)
 { return new FireworksCliLoginAdapter(); } // Optional login repair must not disable existing API-key accounts.
}
await using var fireworksCliLogin = CreateFireworksCliLogin();
PoeLoginAdapter CreatePoeLogin()
{
 try { return new PoeLoginAdapter(PoeLoginOptions.LoadMaintainerConfiguration(paths.Root)); }
 catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException)
 { return new PoeLoginAdapter(); }
}
using var poeLogin = CreatePoeLogin();
// Availability checks only local paths/display state. Constructing the browser or
// polling quota never opens a window; only the explicit console-browser login does.
await using var mimoBrowser = new MiMoConsoleBrowser(paths);
await mimoBrowser.ReconcileAsync();
var mimoLogin = new MiMoConsoleLoginAdapter(mimoBrowser);
var mimoQuota = new MiMoQuotaAdapter(mimoBrowser, secrets, repository);
var mimoSessions = new MiMoSessionCleanup(mimoBrowser, secrets, repository);
await using var googleCli = new GoogleGcloudLoginAdapter(Path.Combine(paths.Root, "accounts", "google-gcloud"), accountId =>
{
 var account = repository.Snapshot.Accounts.SingleOrDefault(a => a.Id == accountId);
 if (account is null || AccountService.CanonicalProviderId(account.Provider) != "gemini-api") return null;
 var handle = QuotaConnectionCleanup.SelectedQuota(account);
 return handle?.Owner == "google-gcloud" ? secrets.Generations.Read(accountId, handle).PrivateJson : null;
});
var googleCliRetention = new GoogleGcloudProfileRetention(repository, secrets, paths, googleCli);
async Task CloseDetachedGoogle(AccountDto account, CancellationToken ct)
{
 // Grant retirement must observe a stable published selection, including recovery.
 await mutations.WaitAsync(ct);
 try { await googleCliRetention.DisconnectAsync(account, ct); }
 finally { mutations.Release(); }
}
var quotaConnectionCleanup = new QuotaConnectionCleanup(repository, [mimoSessions.CloseDetachedAsync, CloseDetachedGoogle]);
CloudMaintainerOptions cloudOptions;
try
{
 var maintainerPath = Path.Combine(paths.Root, "maintainer");
 if (Directory.Exists(maintainerPath) && new DirectoryInfo(maintainerPath).LinkTarget is not null) throw new InvalidDataException();
 cloudOptions = CloudMaintainerConfiguration.Load(Path.Combine(paths.Root, CloudMaintainerConfiguration.RelativePath));
}
catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
{ cloudOptions = new([]); }
var cloudLogins = cloudOptions.Clients.Where(c => c.Qualified).Select(c => new CloudOAuthAdapter(c)).ToArray();
IAccountLoginAdapter[] accountLogins = [loginAdapter, openRouterLogin, nanoGptLogin, kiloLogin, kimiLogin, mimoLogin, googleCli, fireworksCliLogin, .. (poeLogin.IsAvailable ? new IAccountLoginAdapter[] { poeLogin } : []), .. cloudLogins];
ICredentialOwner[] credentialOwners = [.. cloudLogins, googleCli, kimiLogin];
builder.Services.AddSingleton(sp => new LoginOperationService(configuration: repository,
 accounts: sp.GetRequiredService<AccountService>(), secrets: secrets,
 accountAdapters: accountLogins, publicationGate: mutations));
builder.Services.AddSingleton(sp => new CredentialRecoveryService(repository, sp.GetRequiredService<AccountService>(), secrets.Generations));
builder.Services.AddSingleton(sp => new CredentialOwnerService(repository, sp.GetRequiredService<AccountService>(), secrets.Generations, credentialOwners, mutations));
builder.Services.AddSingleton(sp => new OwnedCredentialRefreshService(repository, sp.GetRequiredService<CredentialOwnerService>(), secrets.Generations, credentialOwners));
builder.Services.AddSingleton(sp => new ConnectionMethodCatalog(sp.GetRequiredService<ProviderCatalogRegistry>(),
 GatewayMode, () => restoreReady && File.Exists(binary), () => poeLogin.IsAvailable,
 provider => cloudLogins.Any(login => login.Provider == provider && login.IsAvailable),
 () => mimoLogin.IsAvailable, () => googleCli.IsAvailable, () => fireworksCliLogin.IsAvailable));
builder.Services.AddSingleton<ModelsService>();
builder.Services.AddSingleton<CodexProfileAdapter>();
builder.Services.AddSingleton<ProxyConfigurationWriter>();
builder.Services.AddSingleton(sp => new ProfileApplicationService(paths, repository, sp.GetRequiredService<ModelsService>(),
 proxy.DrainAndStopAsync, async (runtime, profile, _) =>
 {
  var writer = sp.GetRequiredService<ProxyConfigurationWriter>();
  var identities = writer.Write(runtime);
  new RouteBindingsWriter(paths).Write(runtime, identities, writer.RouteCredentials,
   profileId: profile.Id, qualifiedProviderRouteIds: writer.QualifiedProviderRouteIds, transportBindings: writer.TransportBindings);
  using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
  await proxy.StartAsync(deadline.Token);
 }, async _ => { using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await proxy.StopAfterFailureAsync(deadline.Token); },
 validateRuntime: (runtime, profile) => {
  var description = sp.GetRequiredService<ProxyConfigurationWriter>().Describe(runtime);
  RouteBindings.Compile(runtime, description.CredentialIds, description.RouteCredentials,
   sp.GetRequiredService<ProviderCatalogRegistry>(), profile.Id, description.QualifiedProviderRouteIds, description.TransportBindings);
 }));
builder.Services.AddSingleton<SqliteUsageStore>(_ => new(Path.Combine(paths.Root, "usage.db")));
builder.Services.AddSingleton<IUsageStore>(sp => sp.GetRequiredService<SqliteUsageStore>());
builder.Services.AddSingleton<IUsageAnalysisStore>(sp => sp.GetRequiredService<SqliteUsageStore>());
builder.Services.AddSingleton<JournalUsageCollector>(sp => {
 var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { BaseAddress = new Uri($"http://127.0.0.1:{paths.GatewayPort}"), Timeout = TimeSpan.FromSeconds(10) };
 http.DefaultRequestHeaders.Authorization = new("Bearer", managementToken);
 return new(http, sp.GetRequiredService<SqliteUsageStore>(), sp.GetRequiredService<ILogger<JournalUsageCollector>>(), CollectCore);
});
builder.Services.AddHostedService(sp => sp.GetRequiredService<JournalUsageCollector>());
builder.Services.AddInspector(managementToken, coreAddress: new Uri($"http://127.0.0.1:{paths.GatewayPort}/"), shouldCollect: CollectCore);
var providerHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
builder.Services.AddSingleton(sp => new XaiKeyMetadataService(repository, secrets,
 sp.GetRequiredService<AccountService>(), providerHttp, mutations));
builder.Services.AddSingleton(sp => new XaiCredentialReconnectService(repository, secrets,
 sp.GetRequiredService<AccountService>(), sp.GetRequiredService<ConnectionMethodCatalog>(),
 monitorAccess, providerHttp, mutations));
using var quotaCoreHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
 { BaseAddress = new Uri($"http://127.0.0.1:{paths.GatewayPort}/"), Timeout = TimeSpan.FromSeconds(10) };
quotaCoreHttp.DefaultRequestHeaders.Authorization = new("Bearer", managementToken);
var quotaSource = new HostQuotaSource(repository, secrets, monitorAccess);
using var geminiQuota = new GeminiCloudQuotaAdapter(providerHttp);
using var cloudflareQuota = new CloudflareQuotaAdapter(cloudOptions.CloudflareNeuronCandidateQualified);
using var kimiQuota = new KimiDeviceQuotaAdapter(providerHttp);
var quota = new QuotaService(quotaSource, quotaSource,
 [new CodexQuotaAdapter(providerHttp), new CommandCodeQuotaAdapter(providerHttp),
  kimiQuota, new ZaiQuotaAdapter(providerHttp),
  new StepFunQuotaAdapter(providerHttp), new PoeQuotaAdapter(providerHttp),
  new OpenRouterQuotaAdapter(providerHttp), new SyntheticQuotaAdapter(providerHttp),
  new KiloQuotaAdapter(providerHttp), new RequestyQuotaAdapter(providerHttp),
  new DeepSeekQuotaAdapter(providerHttp), new NanoGptQuotaAdapter(providerHttp),
  new FireworksQuotaAdapter(providerHttp), geminiQuota, cloudflareQuota, mimoQuota,
  new GroqAccountQuotaAdapter(new CoreDependentQuotaAdapter(new GroqQuotaAdapter(quotaCoreHttp), CollectCore),
   new GroqEnterpriseUsageReader(providerHttp))]);
builder.Services.AddSingleton(quota);
var app = builder.Build();
var loginOperations = app.Services.GetRequiredService<LoginOperationService>();
// Retire removed targets before general profile retention examines their owners.
// Provider cleanup owns its bounded pass; never hold the publication gate here.
await loginOperations.CleanupRemovedRecoveriesAsync();
await mutations.WaitAsync();
try { await googleCliRetention.StartupReconcileAsync(); }
finally { mutations.Release(); }
var application = app.Services.GetRequiredService<ProfileApplicationService>();
app.Use(async (context, next) => {
 if (context.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote)) { context.Response.StatusCode = 403; return; }
 var supplied = context.Request.Headers.Authorization.ToString();
 var expected = "Bearer " + hostToken;
 if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(expected))) { context.Response.StatusCode = 401; await context.Response.WriteAsJsonAsync(new ApiError("unauthorized", "A local lain management token is required.")); return; }
 context.Response.Headers.CacheControl = "no-store";
 // Login owns its publication CAS. Never hold this lock during a provider wait:
 // polling and cancellation must be able to proceed concurrently.
 var mutation = context.Request.Method is not ("GET" or "HEAD") &&
  !context.Request.Path.StartsWithSegments(Api.Prefix + "/accounts/login") &&
  context.GetEndpoint()?.Metadata.GetMetadata<OwnsPublicationGate>() is null;
 if (mutation) await mutations.WaitAsync(context.RequestAborted);
 var beforeAccounts = context.Request.Path.StartsWithSegments(Api.Prefix + "/accounts") ? repository.Snapshot : null;
 try { await next(context); }
 catch (RevisionConflictException) { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new ApiError("revision_conflict", "Configuration changed; reload and retry.")); }
 catch (KeyNotFoundException) { context.Response.StatusCode = 404; await context.Response.WriteAsJsonAsync(new ApiError("not_found", "The requested item does not exist.")); }
 catch (CollectionRouterValidationException ex) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(ex.ToApiError()); }
 catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new ApiError("validation", "Invalid request values; check dates, identities and selected accounts.")); }
 catch (ProfileDrainTimedOutException ex) { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new ApiError("runtime_busy", ex.Message)); }
 catch (ProfileDrainRecoveryRequiredException ex) { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new ApiError("runtime_recovery_required", ex.Message)); }
 catch (InvalidOperationException) { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new ApiError("operation_unavailable", "Operation unavailable; check backend qualification, configuration and port availability.")); }
 catch (Exception) { context.Response.StatusCode = 500; await context.Response.WriteAsJsonAsync(new ApiError("operation_failed", "The operation failed. Check lain paths, credentials and service readiness.")); }
 finally
 {
  if (mutation) mutations.Release();
  // Includes successful login polling/resource selection and recovery publication.
  // Browser/owner cleanup happens after persistence and outside the shared CAS gate.
  if (beforeAccounts is not null)
  {
   using var cleanupBudget = new CancellationTokenSource(QuotaConnectionCleanup.CleanupBudget);
   var remainingAccounts = repository.Snapshot.Accounts.Select(account => account.Id).ToHashSet(StringComparer.Ordinal);
   foreach (var removed in beforeAccounts.Accounts.Where(account => !remainingAccounts.Contains(account.Id)))
    await app.Services.GetRequiredService<LoginOperationService>().CancelRemovedAccountAsync(removed.Id, cleanupBudget.Token);
   await quotaConnectionCleanup.AfterPublicationAsync(beforeAccounts, cleanupBudget.Token);
  }
 }
});
var runtimeManifest = Path.Combine(paths.Proxy, "runtime.json");
void RequireAppliedRuntime()
{
 RequireGateway();
 application.RequireCompletedRuntime();
}
StatusDto Status()
{
 var config = repository.Snapshot;
 var ps = proxy.Status;
 var active = AppliedProfileStatus.Read(Path.Combine(paths.Root, "active-profile.json"), runtimeManifest, config.AppliedRevision);
 var version = typeof(ProxyBuildMetadata).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
  .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "unknown";
 var gateway = config.RuntimeMode == "gateway";
 var applicationState = application.Status;
 var state = !restoreReady ? "credential-recovery-required" : gateway ?
  ps.LoginOnly && ps.ManagementReady ? "login-only" : applicationState.State == "recovery-required" ? "recovery-required" : ps.Error is not null ? ps.State :
  config.Revision != config.AppliedRevision ? "pending-application" : ps.State : "monitoring";
 return new(Api.Version, version, state,
  ps.Ready, config.Revision, config.AppliedRevision, DateTimeOffset.UtcNow, !restoreReady ? "Restored credentials require local recovery before Gateway can start." : gateway ? ps.Error : null, active.Id, active.Revision,
  config.RuntimeMode, new(true, gateway, gateway, true, gateway, gateway && restoreReady && File.Exists(binary)), applicationState);
}
// Matching profile identity and revision in both metadata files reject partial
// publication. Reads stay responsive while another request refreshes provider quotas.
app.MapGet(Api.Prefix + "/status", Status);
app.MapGet(Api.Prefix + "/configuration", () => repository.Snapshot);
app.MapPut(Api.Prefix + "/runtime-mode", async (RuntimeModeRequest request, CancellationToken ct) => {
 if (request.Mode is not ("gateway" or "monitor")) throw new ArgumentException("Unknown operating mode.");
 if (request.Revision != repository.Snapshot.Revision) throw new RevisionConflictException(request.Revision, repository.Snapshot.Revision);
 if (request.Mode == repository.Snapshot.RuntimeMode) return Status();
 if (request.Mode == "monitor") await proxy.DrainAndStopAsync(ct);
 repository.Update(request.Revision, config => config with { RuntimeMode = request.Mode });
 // Selecting Gateway exposes setup; inference starts only through an explicit Apply/Start.
 return Status();
});
app.MapGet(Api.Prefix + "/connection-methods", (string? provider, ConnectionMethodCatalog methods) => methods.Snapshot(provider));
app.MapXaiKeyMetadata();
app.MapPost(Api.Prefix + "/accounts/connect", (RevisionRequest<ConnectCredentialRequest> request, XaiCredentialReconnectService reconnect, CancellationToken ct) =>
 reconnect.ConnectAsync(request.Revision, request.Value, ct)).WithMetadata(new OwnsPublicationGate());
app.MapPost(Api.Prefix + "/accounts/{id}/models", (string id, RevisionRequest<AttachAccountModelsRequest> request, AccountService accounts) => {
 RequireGateway();
 return accounts.AttachModels(request.Revision, id, request.Value.ExpectedGeneration, request.Value.ConnectionId, request.Value.ModelRouteIds, request.Value.Settings);
});
app.MapPost(Api.Prefix + "/accounts/recovery/{operationId}", (string operationId, ApplyRequest request, CredentialRecoveryService recovery) => recovery.Adopt(request.Revision, operationId));
app.MapPost(Api.Prefix + "/accounts/{id}/credentials/{credentialId}/refresh", (string id, string credentialId, ApplyRequest request, CredentialOwnerService owners, CancellationToken ct) => {
 if (request.Revision != repository.Snapshot.Revision) throw new RevisionConflictException(request.Revision, repository.Snapshot.Revision);
 return owners.RefreshAsync(id, credentialId, ct);
}).WithMetadata(new OwnsPublicationGate());
app.MapGet(Api.Prefix + "/accounts", (AccountService svc) => svc.List());
app.MapPost(Api.Prefix + "/accounts", (RevisionRequest<AddApiAccountRequest> request, AccountService svc) => svc.AddApi(request.Revision, request.Value));
app.MapPut(Api.Prefix + "/accounts/{id}/enabled", (string id, RevisionRequest<bool> request, AccountService svc) => svc.SetEnabled(request.Revision, id, request.Value));
app.MapPut(Api.Prefix + "/accounts/{id}/name", (string id, RevisionRequest<string> request, AccountService svc) => svc.Rename(request.Revision, id, request.Value));
app.MapDelete(Api.Prefix + "/accounts/{id}", (string id, long revision, AccountService svc) => svc.Remove(revision, id));
app.MapPost(Api.Prefix + "/accounts/{id}/check", (string id, AccountService svc) => svc.Check(id));
app.MapGet(Api.Prefix + "/accounts/{id}/monitor", (string id, MonitorAccessService svc) => svc.Get(id));
app.MapPut(Api.Prefix + "/accounts/{id}/monitor", (string id, RevisionRequest<MonitorAccessRequest> request, MonitorAccessService svc) => svc.Set(request.Revision, id, request.Value));
app.MapDelete(Api.Prefix + "/accounts/{id}/monitor", (string id, long revision, MonitorAccessService svc) => svc.Remove(revision, id));
app.MapPost(Api.Prefix + "/import", (ImportRequest request, LegacyImportService svc) => svc.Import(request));
app.MapPost(Api.Prefix + "/accounts/login", async (LoginRequest request, LoginOperationService svc, ConnectionMethodCatalog methods,
 ProxyConfigurationWriter writer, AccountService accounts, CancellationToken ct) => {
 var connection = new ConnectAccountRequest(request.Provider, request.Method ?? "codex-owned", request.Purposes ?? ["inference", "quota"],
  request.Name, request.AccountId, request.Binding, request.ExpectedGeneration, request.IdempotencyKey);
 methods.RequireAvailable(connection, secret: false);
 _ = accounts.ReserveConnect(request.Revision, connection, "preflight", DateTimeOffset.UtcNow.AddMinutes(10));
 if (request.Revision != repository.Snapshot.Revision) throw new RevisionConflictException(request.Revision, repository.Snapshot.Revision);
 if (svc.RequiresCore(connection.Provider, connection.Method))
 {
  await mutations.WaitAsync(ct);
  try
  {
   RequireGateway();
   if (!proxy.Status.ManagementReady)
   {
    if (proxy.Status.Pid is not null) throw new InvalidOperationException("Existing Gateway admission requires recovery.");
    // A dedicated empty runtime permits reconnect after restore without touching
    // the saved active configuration, profile proof or inference journal.
    var loginPaths = new OwnStatePaths(Path.Combine(paths.Proxy, "login-bootstrap"));
    OwnStatePaths.WritePrivate(loginPaths.ManagementToken, managementToken);
    var empty = new ConfigurationDto(repository.Snapshot.Revision, repository.Snapshot.AppliedRevision, [], [], []);
    var loginWriter = new ProxyConfigurationWriter(loginPaths, new SecretStore(loginPaths));
    var identities = loginWriter.Write(empty);
    var bindings = new RouteBindingsWriter(loginPaths);
    bindings.Write(empty, identities);
    await proxy.StartLoginRuntimeAsync(new(loginPaths.ProxyConfig, Path.Combine(loginPaths.Proxy, "bindings.json"), Path.Combine(loginPaths.Proxy, "events.jsonl")), ct);
   }
  }
  finally { mutations.Release(); }
 }
 return await svc.BeginAsync(request.Revision, connection, ct);
});
app.MapPost(Api.Prefix + "/accounts/login/{id}/resource", (string id, ResourceSelectionRequest request, LoginOperationService svc, CancellationToken ct) => {
 if (request.Revision != repository.Snapshot.Revision) throw new RevisionConflictException(request.Revision, repository.Snapshot.Revision);
 return svc.SelectResourceAsync(id, request.Binding, ct);
});
app.MapGet(Api.Prefix + "/accounts/login/{id}", (string id, LoginOperationService svc, CancellationToken ct) => svc.StatusAsync(id, ct));
app.MapDelete(Api.Prefix + "/accounts/login/{id}", (string id, LoginOperationService svc, CancellationToken ct) => svc.CancelAsync(id, ct));
app.MapModelEndpoints();
app.MapWorkerEndpoints();
app.MapProviderCatalogEndpoints();
app.MapInspectorEndpoints();
app.MapGet(Api.Prefix + "/profiles/{id}/preview", (string id, long revision) => application.Preview(id, revision));
app.MapPost(Api.Prefix + "/profiles/{id}/apply", async (string id, ApplyRequest request, CancellationToken ct) => {
 RequireGateway();
 return await application.ApplyAsync(id, request.Revision, ct);
});
app.MapGet(Api.Prefix + "/usage", (HttpRequest request, IUsageStore store, CancellationToken ct) => store.SummaryAsync(ParseQuery(request), ct));
app.MapGet(Api.Prefix + "/usage/analysis", (HttpRequest request, IUsageAnalysisStore store, CancellationToken ct) => store.AnalysisAsync(ParseQuery(request), ct));
app.MapGet(Api.Prefix + "/attempts", (HttpRequest request, IUsageStore store, CancellationToken ct) => store.AttemptsAsync(ParseQuery(request), ct));
app.MapGet(Api.Prefix + "/quotas", (CancellationToken ct) => quota.GetSnapshotsAsync(ct));
app.MapPost(Api.Prefix + "/quotas/refresh", (QuotaRefreshRequest request, CancellationToken ct) => quota.RefreshAsync(request.AccountId, ct));
app.MapGet(Api.Prefix + "/auth/token", (string audience) => {
 RequireGateway();
 return audience == "codex" ? Results.Ok(new { token = secrets.GetOrCreateToken(paths.InferenceToken) }) : Results.BadRequest(new ApiError("invalid_audience", "Only the codex inference audience is supported."));
});
app.MapPost(Api.Prefix + "/service/{action}", async (string action, CancellationToken ct) => {
 if (action != "stop") RequireGateway();
 switch (action) { case "start": RequireAppliedRuntime(); await proxy.StartAsync(ct); break; case "stop": await proxy.StopAsync(ct); break; case "restart": RequireAppliedRuntime(); await proxy.RestartAsync(ct); break; default: throw new ArgumentException("Unknown proxy action."); }
 return Status();
});
using var shutdown = new CancellationTokenSource();
var referenceWithoutQuota = referenceIsolation && Environment.GetEnvironmentVariable("LAIN_REFERENCE_DISABLE_QUOTA") == "1";
if (GatewayMode() && restoreReady && File.Exists(Path.Combine(paths.Root, "active-profile.json")))
{
 try {
  using var active = JsonDocument.Parse(File.ReadAllText(Path.Combine(paths.Root, "active-profile.json")));
  if (active.RootElement.GetProperty("revision").GetInt64() != repository.Snapshot.AppliedRevision) throw new InvalidOperationException();
  RequireAppliedRuntime(); await proxy.StartAsync(shutdown.Token);
 } catch (Exception) { proxy.ReportStartupValidationFailure(); /* API remains available for repair. */ }
}
// Bring the owned Core up before passive quota readers connect to its local port.
var quotaLoop = referenceWithoutQuota ? Task.CompletedTask : quota.RunAsync(shutdown.Token);
// Cached quota publication is independent of provider refresh and usage collection.
var routerAvailability = new RouterAvailabilityPublisher(quotaCoreHttp, () => repository.Snapshot,
 quota.GetRouterAvailabilitySnapshotsAsync,
 ct => RouterAvailabilityBindings.ReadAsync(new RouteBindingsWriter(paths).BindingsPath, ct), canPublish: CollectCore);
var routerAvailabilityLoop = referenceWithoutQuota ? Task.CompletedTask : routerAvailability.RunAsync(shutdown.Token);
var credentialRefreshLoop = referenceWithoutQuota ? Task.CompletedTask :
 app.Services.GetRequiredService<OwnedCredentialRefreshService>().RunAsync(shutdown.Token);
var recoveryCleanupLoop = RunRecoveryCleanupAsync(loginOperations, shutdown.Token);
try { await app.RunAsync(); }
finally
{
 shutdown.Cancel();
 try { await Task.WhenAll(quotaLoop, routerAvailabilityLoop, credentialRefreshLoop, recoveryCleanupLoop); } catch (OperationCanceledException) { }
 await proxy.DisposeAsync();
 foreach (var login in cloudLogins) login.Dispose();
 providerHttp.Dispose();
}

static async Task RunRecoveryCleanupAsync(LoginOperationService operations, CancellationToken ct)
{
 using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
 while (await timer.WaitForNextTickAsync(ct))
  await operations.CleanupRemovedRecoveriesAsync(ct);
}

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
record LoginRequest(long Revision, string Provider, string? Method = null, string[]? Purposes = null,
 string? AccountId = null, ResourceBindingDto? Binding = null, long? ExpectedGeneration = null,
 string? Name = null, string? IdempotencyKey = null);
record QuotaRefreshRequest(string? AccountId);
// Owner operations wait on providers outside the global mutation lock and acquire it only to publish.
sealed record OwnsPublicationGate;
