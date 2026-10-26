using System.Collections.Concurrent;
using System.Text;
using Lain.Core;

namespace Lain.Host.Accounts.CliConnections;

/// <summary>Official OIDC signin followed by one explicitly named ordinary Serverless API key; no external stores.</summary>
public sealed class FireworksCliLoginAdapter : IAccountLoginAdapter, IAsyncDisposable
{
    private sealed class Attempt(LoginOperationContext context, CliConnectionState state, string directory, CancellationTokenSource cancellation)
    {
        public readonly LoginOperationContext Initial = context;
        public readonly CliConnectionState State = state;
        public readonly string Directory = directory;
        public readonly CancellationTokenSource Cancellation = cancellation;
        public readonly SemaphoreSlim Gate = new(1, 1);
        public Task<ResourceBindingDto[]>? Authentication;
        public Task<LoginAdapterResult>? Creation;
        public ResourceBindingDto? Selection;
        public LoginAdapterResult? Result;
    }
    private readonly CliConnectionStateStore? store;
    private readonly IFireworksCliProcess? process;
    private readonly FireworksCliQualification? qualification;
    private readonly bool fixture;
    private readonly ConcurrentDictionary<string, Attempt> attempts = new();
    private readonly ConcurrentDictionary<string, string> accounts = new();
    public string Provider => "fireworks";
    public string Method => "official-cli";
    public bool RequiresCore => false;
    public bool IsAvailable => store is not null && process is not null && (fixture || qualification?.IsAvailable == true);
    public string QualificationBlocker => "Install the pinned Fireworks CLI 1.8.19 and Linux private mount isolation, or use an existing API key.";
    public FireworksCliLoginAdapter(CliConnectionStateStore? store = null, FireworksCliQualification? qualification = null)
    { this.store = store; this.qualification = qualification; if (qualification is not null) process = new FireworksCliProcess(qualification); }
    // Explicit fixture factory cannot make a production installation available.
    public static FireworksCliLoginAdapter Fixture(CliConnectionStateStore store, IFireworksCliProcess process) => new(store, process);
    private FireworksCliLoginAdapter(CliConnectionStateStore store, IFireworksCliProcess process)
    { this.store = store; this.process = process; fixture = true; }
    public static ProviderConnectionMethodDto ConnectionMethod(bool available) => new("fireworks", "official-cli", "Sign in with Fireworks", "cli",
        ["inference", "models", "quota"], available ? "available" : "setup-required", false, false,
        Description: "Sign in through the official Fireworks CLI, choose your account, and create a dedicated ordinary Serverless API key for lain. No purchase or inference request is performed.",
        Guidance: "Google SSO and account-specific enterprise SSO use a separate private login. This creates a new named API key; existing keys remain available. Account quotas are constraints, not prepaid credit balance. Disconnect does not revoke the provider key.",
        DefaultBaseUrl: "https://api.fireworks.ai/inference/v1", RequiredSettings: [new("accountId", "Fireworks account ID", Required: false, Help: "Optional before sign-in; required for enterprise SSO. A reconnect keeps the existing account.")], DocumentationUrl: "https://docs.fireworks.ai/tools-sdks/firectl/commands/authentication", Recommended: available);
    public Task<LoginAdapterResult> BeginAsync(LoginOperationContext context, CancellationToken ct)
    {
        Validate(context); ct.ThrowIfCancellationRequested();
        if (!IsAvailable) return Task.FromResult(new LoginAdapterResult("failed", Error: QualificationBlocker, NextAction: "use-api-key"));
        if (attempts.TryGetValue(context.OperationId, out var existing)) { Same(existing, context); return StatusAsync(context, ct); }
        if (attempts.Count >= 8 || !accounts.TryAdd(context.AccountId, context.OperationId))
            return Task.FromResult(new LoginAdapterResult("failed", Error: "Finish the active Fireworks login before starting another.", NextAction: "finish-active-connection"));
        try
        {
            var state = store!.Create(context); var directory = store.ProfileDirectory(state.ProfileReference);
            var attempt = new Attempt(context, state, directory, CancellationTokenSource.CreateLinkedTokenSource(ct));
            if (!attempts.TryAdd(context.OperationId, attempt)) throw new CliConnectionException("fireworks-operation-already-started");
            attempt.Authentication = Authenticate(attempt);
            return Task.FromResult(new LoginAdapterResult("waiting"));
        }
        catch { accounts.TryRemove(context.AccountId, out _); throw; }
    }
    private async Task<ResourceBindingDto[]> Authenticate(Attempt attempt)
    {
        using var lease = store!.AcquireConnection(attempt.Initial.AccountId);
        var ct = attempt.Cancellation.Token; var context = attempt.Initial;
        using (var version = await process!.RunAsync(FireworksCommand.Version, attempt.Directory, null, null, TimeSpan.FromSeconds(10), ct))
            if (version.ExitCode != 0 || Encoding.UTF8.GetString(version.Stdout.Span).Trim() != FireworksCliQualification.Version)
                throw new CliConnectionException("fireworks-version-mismatch");
        store.Update(context, "browser");
        using (var login = await process.RunAsync(FireworksCommand.Signin, attempt.Directory, context.Request.Binding?.ResourceId, null, Remaining(context), ct,
            identity => store.Update(context, "browser", identity)))
            if (login.ExitCode != 0) throw new CliConnectionException("fireworks-signin-not-complete");
        using var list = await process.RunAsync(FireworksCommand.Accounts, attempt.Directory, null, null, TimeSpan.FromSeconds(30), ct);
        if (list.ExitCode != 0) throw new CliConnectionException("fireworks-account-list-failed");
        return FireworksCliResult.Accounts(list.Stdout);
    }
    public async Task<LoginAdapterResult> StatusAsync(LoginOperationContext context, CancellationToken ct)
    {
        Validate(context); ct.ThrowIfCancellationRequested();
        if (!attempts.TryGetValue(context.OperationId, out var attempt)) return new("recovery-required", Error: "Review the private Fireworks login before retrying.", NextAction: "review-recovery");
        Same(attempt, context); await attempt.Gate.WaitAsync(ct);
        try
        {
            if (attempt.Result is not null) return attempt.Result;
            if (attempt.Creation is not null)
                return attempt.Creation.IsCompleted ? attempt.Result = await attempt.Creation : new("waiting");
            if (attempt.Cancellation.IsCancellationRequested) return attempt.Result = Recovery(attempt, "Fireworks login was cancelled. Review the private login before retrying.");
            if (attempt.Authentication is not { IsCompleted: true }) return new("waiting", Url: process!.AuthorizationUrl(attempt.Directory));
            var resources = await attempt.Authentication;
            var requested = context.Request.Binding;
            if (requested?.ResourceId is null) return new("resource-required", Resources: resources);
            var selected = resources.SingleOrDefault(r => r.ResourceId == requested.ResourceId);
            if (selected is null || requested.Provider != Provider || requested.Product is not (null or "serverless") || requested.Subject is not null
                || requested.Region is not null || requested.ResourceType is not (null or "account"))
                return attempt.Result = Recovery(attempt, "The selected Fireworks account does not match this login. No key was requested.");
            attempt.Selection = selected;
            attempt.Creation = Create(attempt, context, selected);
            return new("waiting");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException or CliConnectionException or OperationCanceledException or ArgumentException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return attempt.Result = Recovery(attempt, "Fireworks login could not be confirmed. Review the private result before retrying."); }
        finally { attempt.Gate.Release(); }
    }
    private async Task<LoginAdapterResult> Create(Attempt attempt, LoginOperationContext context, ResourceBindingDto selected)
    {
        using var lease = store!.AcquireConnection(context.AccountId);
        try
        {
            var name = "lain-" + context.OperationId;
            // Durable issuance intent precedes the only key-creating invocation. There is no retry.
            store.Update(attempt.Initial, "result-private");
            using var output = await process!.RunAsync(FireworksCommand.CreateKey, attempt.Directory, selected.ResourceId, name,
                Remaining(context), attempt.Cancellation.Token, identity => store.Update(attempt.Initial, "result-private", identity));
            if (output.ExitCode != 0) return Recovery(attempt, "Fireworks key creation was not confirmed. Review provider keys before retrying.");
            var credential = FireworksCliResult.Key(output.Stdout, selected.ResourceId!, name, context.OperationId, attempt.State.ProfileReference, context.Request.Purposes);
            store.Update(attempt.Initial, "result-private", remoteKeyId: credential.RemoteId);
            if (context.CheckpointCredentialAsync is { } checkpoint) await checkpoint(credential, CancellationToken.None);
            return new("prepared", Credential: credential);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException or CliConnectionException or OperationCanceledException or ArgumentException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return Recovery(attempt, "An owned Fireworks key may have been issued. Keep the private result and review provider keys before retrying."); }
    }
    private LoginAdapterResult Recovery(Attempt attempt, string message)
    {
        try { store!.Update(attempt.Initial, "recovery-required"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* Private CLI result remains; do not retry issuance. */ }
        return new("recovery-required", Error: message, NextAction: "review-provider-keys");
    }
    private static TimeSpan Remaining(LoginOperationContext context)
    { var left = context.ExpiresAt - DateTimeOffset.UtcNow; if (left <= TimeSpan.Zero) throw new CliConnectionException("fireworks-operation-expired"); return left < TimeSpan.FromMinutes(8) ? left : TimeSpan.FromMinutes(8); }
    private static void Validate(LoginOperationContext context)
    {
        CliPrivatePaths.Id(context.OperationId); CliPrivatePaths.Id(context.AccountId);
        if (context.Request.Provider != "fireworks" || context.Request.Method != "official-cli" || context.Request.Purposes.Length == 0
            || context.Request.Purposes.Any(p => p is not ("inference" or "models" or "quota"))) throw new ArgumentException("Invalid Fireworks operation.");
        if (context.Request.Binding?.ResourceId is { } id) FireworksCliResult.Identifier(id);
    }
    private static void Same(Attempt attempt, LoginOperationContext current)
    {
        // The host may add a resource selected from this attempt after signin. No other identity/revision change is allowed.
        var normalized = current with { Request = current.Request with { Binding = attempt.Initial.Request.Binding } };
        if (CliConnectionStateStore.Digest(normalized) != CliConnectionStateStore.Digest(attempt.Initial)
            || attempt.Initial.Request.Binding?.ResourceId is { } bound && current.Request.Binding?.ResourceId != bound
            || attempt.Selection is { } selected && current.Request.Binding?.ResourceId != selected.ResourceId)
            throw new CliConnectionException("fireworks-operation-context-mismatch");
    }
    public async Task CancelAsync(LoginOperationContext context, CancellationToken ct)
    {
        if (!attempts.TryGetValue(context.OperationId, out var attempt)) return; Same(attempt, context); attempt.Cancellation.Cancel();
        try { if (attempt.Creation is { } create) await create.WaitAsync(TimeSpan.FromSeconds(5), ct); else if (attempt.Authentication is { } auth) await auth.WaitAsync(TimeSpan.FromSeconds(5), ct); }
        catch (Exception e) when (e is TimeoutException or OperationCanceledException or CliConnectionException or IOException) { }
    }
    public async Task ReleaseAsync(LoginOperationContext context, CancellationToken ct)
    {
        if (!attempts.TryGetValue(context.OperationId, out var attempt)) return; await CancelAsync(context, ct);
        if (attempt.Creation?.IsCompleted == false || attempt.Authentication?.IsCompleted == false) return;
        attempts.TryRemove(context.OperationId, out _); accounts.TryRemove(context.AccountId, out _); attempt.Cancellation.Dispose();
    }
    public async ValueTask DisposeAsync()
    { foreach (var attempt in attempts.Values) await ReleaseAsync(attempt.Selection is { } selection ? attempt.Initial with { Request = attempt.Initial.Request with { Binding = selection } } : attempt.Initial, CancellationToken.None); }
}
