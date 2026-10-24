using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts.CliConnections;
using Lain.Host.Accounts.Credentials;
using Lain.Host.Operations;

namespace Lain.Host.Accounts.Cloud;

internal sealed record GoogleGcloudGrant(string Kind, string Provider, string LocalAccountId, string ProfileReference,
    string LoginAccount, string? LoginSubject, string AccessToken, DateTimeOffset ExpiresAt, Dictionary<string, string> Settings)
{
    public override string ToString() => "GoogleGcloudGrant [redacted]";
}

/// <summary>Browser login through the source-qualified official CLI, with a fresh private profile per connection.</summary>
public sealed class GoogleGcloudLoginAdapter : IAccountLoginAdapter, ICredentialOwner, IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string root;
    private readonly IGoogleGcloudProcess process;
    private readonly CloudProviderHttp http;
    private readonly CloudResourceDiscovery discovery;
    private readonly Func<string, string?> selectedCredential;
    private readonly TimeProvider clock;
    private readonly object gate = new();
    private readonly Dictionary<string, Attempt> attempts = new(StringComparer.Ordinal);
    private bool disposed;
    public string Provider => CloudResourceDiscovery.GoogleProvider;
    public string Method => "official-cli";
    public string Owner => "google-gcloud";
    public bool RequiresCore => false;
    public bool IsAvailable => process.IsAvailable;
    private sealed class Attempt(LoginOperationContext context, string profile, GoogleGcloudGrant? previous, CancellationTokenSource cancellation)
    {
        public LoginOperationContext Context = context;
        public string Profile = profile;
        public GoogleGcloudGrant? Previous = previous;
        public CancellationTokenSource Cancellation = cancellation;
        public SemaphoreSlim Gate = new(1, 1);
        public Task<GoogleGcloudAccess> Login = null!;
        public string? BrowserUrl;
        public GoogleGcloudGrant? Grant;
        public ResourceBindingDto[]? Resources;
        public LoginAdapterResult? Result;
    }
    public GoogleGcloudLoginAdapter(string ownedRoot, Func<string, string?> selectedCredential,
        IGoogleGcloudProcess? process = null, HttpMessageHandler? handler = null, TimeProvider? clock = null)
    {
        root = Path.GetFullPath(ownedRoot); CliPrivatePaths.Directory(root);
        this.clock = clock ?? TimeProvider.System;
        this.process = process ?? new GoogleGcloudProcess(this.clock);
        this.selectedCredential = selectedCredential ?? throw new ArgumentNullException(nameof(selectedCredential));
        http = new(handler); discovery = new(http);
        // Only markers in this adapter's private profile tree are considered; shared gcloud state is never opened.
        foreach (var directory in Directory.EnumerateDirectories(root).Take(513))
        {
            if (!Path.GetFileName(directory).StartsWith("profile-", StringComparison.Ordinal)) continue;
            CliPrivatePaths.RefuseLinks(directory);
            GoogleGcloudProcessTransport.ReconcileOwnedProcess(directory);
        }
    }
    public Task<LoginAdapterResult> BeginAsync(LoginOperationContext context, CancellationToken ct)
    {
        Validate(context); ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!IsAvailable) return Task.FromResult(new LoginAdapterResult("failed", Error: "The qualified Google Cloud CLI is not installed or its version changed. Install the supported CLI or use the registered Google browser connection.", NextAction: "configure-cli"));
            if (attempts.TryGetValue(context.OperationId, out var existing))
            { Same(existing.Context, context); return Task.FromResult(existing.Result ?? new("waiting", existing.BrowserUrl)); }
            if (attempts.Count >= 8 || attempts.Values.Any(a => a.Context.AccountId == context.AccountId && a.Result is null))
                throw new InvalidOperationException("A Google Cloud CLI connection is already active or the operation limit was reached.");
            var previous = ReadSelected(context.AccountId);
            var profile = "profile-" + Guid.NewGuid().ToString("N");
            var directory = Profile(profile);
            OwnStatePaths.WritePrivate(Path.Combine(directory, "lain-owner.json"), JsonSerializer.Serialize(new ProfileOwner(context.AccountId, context.OperationId, null, null), Json));
            var cancellation = new CancellationTokenSource(context.ExpiresAt - clock.GetUtcNow(), clock);
            var attempt = new Attempt(context, profile, previous, cancellation);
            attempts.Add(context.OperationId, attempt);
            attempt.Login = process.LoginAsync(directory, previous?.LoginAccount, url =>
            {
                var safe = GoogleGcloudProcessTransport.ValidateBrowserUrl(url);
                if (safe is null) throw new InvalidDataException("Unsupported Google browser action.");
                lock (gate) if (!attempt.Cancellation.IsCancellationRequested && attempt.Result is null) attempt.BrowserUrl = safe;
            }, cancellation.Token);
            return Task.FromResult(new LoginAdapterResult("waiting", attempt.BrowserUrl));
        }
    }
    public async Task<LoginAdapterResult> StatusAsync(LoginOperationContext context, CancellationToken ct)
    {
        Attempt attempt;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!attempts.TryGetValue(context.OperationId, out attempt!)) throw new KeyNotFoundException("Google Cloud CLI connection not found.");
            Same(attempt.Context, context);
        }
        await attempt.Gate.WaitAsync(ct);
        try
        {
            if (attempt.Result is not null) return attempt.Result;
            if (attempt.Cancellation.IsCancellationRequested || clock.GetUtcNow() >= context.ExpiresAt)
            { attempt.Cancellation.Cancel(); return attempt.Result = new("cancelled"); }
            if (!attempt.Login.IsCompleted) return new("waiting", attempt.BrowserUrl);
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, attempt.Cancellation.Token);
            if (attempt.Grant is null)
            {
                var access = await attempt.Login;
                attempt.BrowserUrl = null;
                // CLI retains its grant independently of the access projection. Checkpoint before any identity/resource read.
                attempt.Grant = new("gcloud-owned", Provider, context.AccountId, attempt.Profile, access.Account, null, access.AccessToken, access.ExpiresAt, new());
                await context.CheckpointCredentialAsync!(Credential(attempt.Grant, context.Request.Binding ?? new(Provider), []), CancellationToken.None);
                var subject = await VerifyUserAsync(access, attempt.Previous?.LoginSubject, lifetime.Token);
                attempt.Grant = attempt.Grant with { LoginSubject = subject };
                OwnStatePaths.WritePrivate(Path.Combine(Profile(attempt.Profile), "lain-owner.json"), JsonSerializer.Serialize(new ProfileOwner(context.AccountId, context.OperationId, access.Account, subject), Json));
            }
            if (attempt.Grant.LoginSubject is null) throw new InvalidDataException("Google account identity could not be verified.");
            if (attempt.Resources is null)
                attempt.Resources = context.Request.Binding?.ResourceId is { } project
                    ? [await discovery.GoogleProjectAsync(attempt.Grant.AccessToken, project, lifetime.Token)]
                    : await discovery.GoogleProjectsAsync(attempt.Grant.AccessToken, lifetime.Token);
            var candidates = context.Request.Binding is { } selected ? attempt.Resources.Where(r => Matches(selected, r)).ToArray() : attempt.Resources;
            if (candidates.Length > 1) return new("resource-required", Resources: candidates);
            if (candidates.Length != 1) return attempt.Result = new("recovery-required", Error: "The selected Google project is not available to this account. Review project access; the existing account is preserved.", NextAction: "review-recovery");
            var binding = candidates[0];
            var grant = attempt.Grant with { Settings = Settings(binding) };
            if (attempt.Cancellation.IsCancellationRequested) return attempt.Result = new("cancelled");
            return attempt.Result = new("prepared", Credential: Credential(grant, binding, ["quota"]));
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            return attempt.Result = new(attempt.Cancellation.IsCancellationRequested ? "cancelled" : "recovery-required",
                Error: "Google Cloud CLI authorization could not be completed for the selected account and project. Its isolated profile is preserved; reconnect or review project permissions.", NextAction: "reconnect");
        }
        finally { attempt.Gate.Release(); }
    }
    public async Task<PreparedLoginCredential> RefreshAsync(CredentialRefreshContext context, CancellationToken ct)
    {
        if (!IsAvailable || context.Handle.Owner != Owner || context.Handle.Binding.Provider != Provider) throw new InvalidDataException("Google Cloud CLI owner is unavailable or mismatched.");
        var grant = ParseGrant(context.PrivateJson, context.AccountId);
        if (grant.LoginSubject is null || !Settings(context.Handle.Binding).OrderBy(p => p.Key).SequenceEqual(grant.Settings.OrderBy(p => p.Key)))
            throw new InvalidDataException("Google Cloud CLI project binding changed.");
        ValidateOwner(grant);
        var access = await process.ReadAccessAsync(Profile(grant.ProfileReference), grant.LoginAccount, ct);
        await VerifyUserAsync(access, grant.LoginSubject, ct);
        // Existing immutable project identity is pinned through the owner's generation; quota reads revalidate labels.
        return Credential(grant with { AccessToken = access.AccessToken, ExpiresAt = access.ExpiresAt }, context.Handle.Binding, context.Handle.Purposes);
    }
    private async Task<string> VerifyUserAsync(GoogleGcloudAccess access, string? expectedSubject, CancellationToken ct)
    {
        GoogleGcloudProcess.ValidateAccount(access.Account);
        if (access.ExpiresAt <= clock.GetUtcNow().AddSeconds(30)) throw new InvalidDataException("Google Cloud CLI access expired.");
        using var request = CloudProviderHttp.Get("https://openidconnect.googleapis.com/v1/userinfo", access.AccessToken);
        using var response = await http.ReadAsync(request, ct);
        var subject = CloudResourceDiscovery.Text(response.RootElement, "sub");
        var email = CloudResourceDiscovery.Text(response.RootElement, "email");
        if (subject is not { Length: > 0 and <= 128 } || !subject.All(char.IsAsciiDigit) || expectedSubject is not null && subject != expectedSubject
            || !string.Equals(email, access.Account, StringComparison.OrdinalIgnoreCase)
            || !response.RootElement.TryGetProperty("email_verified", out var verified) || verified.ValueKind != JsonValueKind.True)
            throw new InvalidDataException("Google account identity does not match this connection.");
        return subject;
    }
    private GoogleGcloudGrant? ReadSelected(string accountId)
    {
        var json = selectedCredential(accountId);
        if (json is null) return null;
        var grant = ParseGrant(json, accountId); ValidateOwner(grant); return grant;
    }
    private GoogleGcloudGrant ParseGrant(string json, string accountId)
    {
        var grant = JsonSerializer.Deserialize<GoogleGcloudGrant>(json, Json) ?? throw new InvalidDataException("Invalid Google Cloud CLI credential.");
        if (grant.Kind != "gcloud-owned" || grant.Provider != Provider || grant.LocalAccountId != accountId || grant.Settings is null) throw new InvalidDataException("Google Cloud CLI account binding changed.");
        GoogleGcloudProcess.ValidateAccount(grant.LoginAccount); _ = Profile(grant.ProfileReference, create: false); return grant;
    }
    private void ValidateOwner(GoogleGcloudGrant grant)
    {
        var owner = JsonSerializer.Deserialize<ProfileOwner>(CliPrivatePaths.Read(Path.Combine(Profile(grant.ProfileReference, false), "lain-owner.json"), 4096), Json);
        if (owner is null || owner.LocalAccountId != grant.LocalAccountId || owner.LoginAccount != grant.LoginAccount || owner.LoginSubject != grant.LoginSubject || owner.LoginSubject is null)
            throw new InvalidDataException("Google Cloud CLI profile identity differs from the credential.");
    }
    private sealed record ProfileOwner(string LocalAccountId, string OperationId, string? LoginAccount, string? LoginSubject);
    private string Profile(string reference, bool create = true)
    {
        if (reference is not { Length: 40 } || !reference.StartsWith("profile-", StringComparison.Ordinal) || !reference[8..].All(char.IsAsciiHexDigit)) throw new InvalidDataException("Invalid Google Cloud CLI profile reference.");
        var path = Path.Combine(root, reference); CliPrivatePaths.RefuseLinks(path);
        if (create) CliPrivatePaths.Directory(path); else if (!Directory.Exists(path)) throw new InvalidDataException("Google Cloud CLI profile is missing.");
        return path;
    }
    private PreparedLoginCredential Credential(GoogleGcloudGrant grant, ResourceBindingDto binding, string[] purposes) =>
        new(Owner, purposes, binding, JsonSerializer.Serialize(grant, Json), grant.ExpiresAt, PreserveForRecovery: true);
    private static Dictionary<string, string> Settings(ResourceBindingDto binding) => binding is { Provider: CloudResourceDiscovery.GoogleProvider, ResourceType: "project" }
        && CloudResourceDiscovery.ProjectId(binding.ResourceId) && CloudResourceDiscovery.ProjectNumber(binding.Subject)
        ? new() { ["projectId"] = binding.ResourceId!, ["projectNumber"] = binding.Subject!, ["quotaProjectId"] = binding.ResourceId! }
        : throw new InvalidDataException("Google Cloud CLI requires a verified project binding.");
    private static bool Matches(ResourceBindingDto expected, ResourceBindingDto actual) => expected.Provider == actual.Provider
        && (expected.ResourceId is null || expected.ResourceId == actual.ResourceId) && (expected.Subject is null || expected.Subject == actual.Subject)
        && (expected.ResourceType is null || expected.ResourceType == actual.ResourceType) && (expected.Product is null || expected.Product == actual.Product)
        && (expected.Region is null || expected.Region == actual.Region);
    private void Validate(LoginOperationContext context)
    {
        CliPrivatePaths.Id(context.OperationId); CliPrivatePaths.Id(context.AccountId);
        if (context.Request.Provider != Provider || context.Request.Method != Method || !context.Request.Purposes.SequenceEqual(["quota"])
            || context.CheckpointCredentialAsync is null || context.ExpiresAt <= clock.GetUtcNow() || context.ExpiresAt > clock.GetUtcNow().AddMinutes(10))
            throw new ArgumentException("Google Cloud CLI connects project monitoring only.");
    }
    private static void Same(LoginOperationContext a, LoginOperationContext b)
    {
        if (a.OperationId != b.OperationId || a.AccountId != b.AccountId || a.Revision != b.Revision || a.AccountGeneration != b.AccountGeneration || a.ExpiresAt != b.ExpiresAt
            || JsonSerializer.Serialize(a.Request with { Binding = null }, Json) != JsonSerializer.Serialize(b.Request with { Binding = null }, Json)
            || a.Request.Binding is { } pinned && (b.Request.Binding is null || !Matches(pinned, b.Request.Binding))) throw new InvalidOperationException("Google Cloud CLI operation context changed.");
    }
    public static string GetProfileReference(string privateJson)
    {
        var grant = JsonSerializer.Deserialize<GoogleGcloudGrant>(privateJson, Json) ?? throw new InvalidDataException("Invalid Google Cloud CLI credential.");
        if (grant.Kind != "gcloud-owned" || grant.Provider != CloudResourceDiscovery.GoogleProvider || grant.ProfileReference is not { Length: 40 }
            || !grant.ProfileReference.StartsWith("profile-", StringComparison.Ordinal) || !grant.ProfileReference[8..].All(char.IsAsciiHexDigit))
            throw new InvalidDataException("Invalid Google Cloud CLI profile reference.");
        return grant.ProfileReference;
    }
    /// <summary>Call only after successful publication/removal, with all selected and recovery references retained.</summary>
    public Task<bool> DisconnectAsync(string privateJson, IReadOnlySet<string> retainedProfileReferences, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var reference = GetProfileReference(privateJson);
        if (retainedProfileReferences.Contains(reference)) return Task.FromResult(false);
        lock (gate)
        {
            if (attempts.Values.Any(a => a.Profile == reference && (!a.Login.IsCompleted || a.Gate.CurrentCount == 0))) return Task.FromResult(false);
            var path = Path.Combine(root, reference);
            if (!Directory.Exists(path)) return Task.FromResult(true);
            var grant = JsonSerializer.Deserialize<GoogleGcloudGrant>(privateJson, Json)!;
            ValidateOwner(grant); GoogleGcloudProcessTransport.ValidateProfileTree(path);
            var lockPath = Path.Combine(path, "lain-process.lock"); CliPrivatePaths.RefuseLinks(lockPath);
            try
            {
                using var lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                ct.ThrowIfCancellationRequested();
                Directory.Delete(path, recursive: true);
                return Task.FromResult(true);
            }
            catch (IOException) { return Task.FromResult(false); } // An active CLI or uncertain filesystem state retains the grant.
        }
    }

    public async Task CancelAsync(LoginOperationContext context, CancellationToken ct)
    {
        Attempt? attempt;
        lock (gate)
        {
            if (!attempts.TryGetValue(context.OperationId, out attempt)) return;
            Same(attempt.Context, context); attempt.Cancellation.Cancel(); attempt.BrowserUrl = null;
        }
        try { await attempt.Login.WaitAsync(TimeSpan.FromSeconds(5), ct); } catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException) { }
        await attempt.Gate.WaitAsync(ct);
        try { attempt.Result = new("cancelled"); } finally { attempt.Gate.Release(); }
    }
    public async Task ReleaseAsync(LoginOperationContext context, CancellationToken ct)
    {
        Attempt? attempt; lock (gate) { if (!attempts.TryGetValue(context.OperationId, out attempt)) return; Same(attempt.Context, context); }
        await CancelAsync(context, ct);
        lock (gate) { attempts.Remove(context.OperationId); attempt.Cancellation.Dispose(); }
    }
    public async ValueTask DisposeAsync()
    {
        Attempt[] pending; lock (gate) { if (disposed) return; pending = attempts.Values.ToArray(); }
        foreach (var attempt in pending) await ReleaseAsync(attempt.Context, CancellationToken.None);
        lock (gate) { disposed = true; http.Dispose(); }
    }
}
