using System.Collections.Concurrent;
using System.Text.Json;
using Lain.Core;

namespace Lain.Host.Accounts.CliConnections;

/// <summary>Uses the shared operation lifecycle; local attempts cannot publish account credentials.</summary>
public abstract class QualifiedCliLoginAdapter : IAccountLoginAdapter, IAsyncDisposable
{
    private sealed class Attempt(LoginOperationContext context, CancellationTokenSource cancellation)
    { public LoginOperationContext Context = context; public CancellationTokenSource Cancellation = cancellation; public Task<LoginAdapterResult> Completion = Task.FromResult(new LoginAdapterResult("waiting")); }
    private readonly ConcurrentDictionary<string, Attempt> attempts = new();
    private readonly ConcurrentDictionary<string, string> connections = new();
    protected readonly CliConnectionStateStore Store;
    protected readonly OfficialCliProcess Process;
    protected readonly CliQualificationManifest? Qualification;
    protected readonly TimeProvider Clock;
    public abstract string Provider { get; }
    public string Method => "official-cli";
    public bool RequiresCore => false;
    public abstract string QualificationBlocker { get; }
    protected abstract string IsolationEnvironment { get; }
    protected abstract IReadOnlyList<string> LoginArguments(string profile, bool reconnect);
    protected abstract Task<PreparedLoginCredential?> ReadCredentialAsync(LoginOperationContext context, CliConnectionState state, string directory, CancellationToken ct);
    protected QualifiedCliLoginAdapter(CliConnectionStateStore store, CliQualificationManifest? qualification,
        OfficialCliProcess? process = null, TimeProvider? clock = null)
    { Store = store; Qualification = qualification; Process = process ?? new(); Clock = clock ?? TimeProvider.System; }

    public virtual Task<LoginAdapterResult> BeginAsync(LoginOperationContext context, CancellationToken ct)
    {
        Validate(context); ct.ThrowIfCancellationRequested();
        if (Qualification is null || !Qualification.IsQualified) return Task.FromResult(new LoginAdapterResult("failed", Error: QualificationBlocker, NextAction: "use-api-key"));
        Qualification.Validate(Provider, Qualification.FixtureOnly);
        if (Qualification.FixtureOnly && IsolationEnvironment != "LAIN_CLI_FIXTURE_DIR")
            throw new CliConnectionException("fixture-qualification-cannot-enable-provider-cli");
        if (attempts.TryGetValue(context.OperationId, out var prior))
        { Same(prior.Context, context); return Task.FromResult(prior.Completion.IsCompletedSuccessfully ? prior.Completion.Result : new("waiting")); }
        if (!connections.TryAdd(context.AccountId, context.OperationId)) return Task.FromResult(new LoginAdapterResult("failed", Error: "A CLI connection is already active for this account.", NextAction: "finish-active-connection"));
        try
        {
            var state = Store.Create(context); // durable before any key-creating process
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var attempt = new Attempt(context, cancellation);
            if (!attempts.TryAdd(context.OperationId, attempt)) throw new CliConnectionException("cli-operation-already-started");
            attempt.Completion = RunAsync(context, state, cancellation.Token);
            return Task.FromResult(new LoginAdapterResult("waiting"));
        }
        catch { connections.TryRemove(context.AccountId, out _); throw; }
    }
    private async Task<LoginAdapterResult> RunAsync(LoginOperationContext context, CliConnectionState state, CancellationToken ct)
    {
        var issuanceStarted = false;
        try
        {
            using var connectionLease = Store.AcquireConnection(context.AccountId);
            var directory = Store.ProfileDirectory(state.ProfileReference);
            var environment = new Dictionary<string, string> { [IsolationEnvironment] = directory };
            using (var version = await Process.RunAsync(Qualification!, ["--version"], directory, environment, TimeSpan.FromSeconds(5), ct, versionProbe: true))
                if (version.ExitCode != 0) throw new CliConnectionException("cli-version-probe-failed");
            var remaining = context.ExpiresAt - Clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero) throw new CliConnectionException("cli-operation-expired");
            Store.Update(context, "browser");
            issuanceStarted = true;
            using var output = await Process.RunAsync(Qualification!, LoginArguments("lain", context.Request.AccountId is not null), directory,
                environment, remaining > TimeSpan.FromMinutes(10) ? TimeSpan.FromMinutes(10) : remaining, ct,
                identity => Store.Update(context, "browser", identity));
            if (output.ExitCode != 0) return Recovery(context, "CLI login did not complete. Review the provider key list before retrying.");
            Store.Update(context, "result-private");
            // A possibly issued key must be checkpointed even after Cancel/Disconnect.
            var credential = await ReadCredentialAsync(context, state, directory, CancellationToken.None);
            if (credential is null) return Recovery(context, "CLI result did not provide a qualified credential and resource. Review before retrying.");
            Store.Update(context, "result-private", remoteKeyId: credential.RemoteId);
            if (context.CheckpointCredentialAsync is { } checkpoint)
                await checkpoint(credential, CancellationToken.None);
            if (!Matches(context.Request.Binding, credential.Binding))
                return Recovery(context, "CLI credential identity does not match the selected account. Review the private saved result.", credential);
            return new("prepared", Credential: credential);
        }
        catch (Exception error) when (error is CliConnectionException or IOException or InvalidDataException or JsonException or ArgumentException or OperationCanceledException)
        {
            if (!issuanceStarted)
            { Store.Update(context, "failed"); return new("failed", Error: "The qualified CLI setup or private isolation could not be verified. Use an existing API key.", NextAction: "use-api-key"); }
            return Recovery(context, "CLI connection could not be confirmed. Review the owned profile and provider keys before retrying.");
        }
    }
    private LoginAdapterResult Recovery(LoginOperationContext context, string error, PreparedLoginCredential? credential = null)
    { Store.Update(context, "recovery-required"); return new("recovery-required", Error: error, Credential: credential, NextAction: "review-provider-keys"); }
    public Task<LoginAdapterResult> StatusAsync(LoginOperationContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); Validate(context);
        if (!attempts.TryGetValue(context.OperationId, out var attempt))
            return Task.FromResult(new LoginAdapterResult("recovery-required", Error: "CLI operation is not active in this host session. Review recovery before reconnecting.", NextAction: "review-recovery"));
        Same(attempt.Context, context);
        return Task.FromResult(attempt.Completion.IsCompletedSuccessfully ? attempt.Completion.Result : new LoginAdapterResult("waiting"));
    }
    public async Task CancelAsync(LoginOperationContext context, CancellationToken ct)
    {
        if (!attempts.TryGetValue(context.OperationId, out var attempt)) return;
        Same(attempt.Context, context); attempt.Cancellation.Cancel();
        try { await attempt.Completion.WaitAsync(TimeSpan.FromSeconds(5), ct); } catch (TimeoutException) { }
        // Never erase an uncertain/issued result or claim remote key revocation.
    }
    public async Task ReleaseAsync(LoginOperationContext context, CancellationToken ct)
    {
        if (!attempts.TryGetValue(context.OperationId, out var attempt)) return;
        Same(attempt.Context, context);
        await CancelAsync(context, ct);
        if (!attempt.Completion.IsCompleted) return;
        attempts.TryRemove(context.OperationId, out _); connections.TryRemove(context.AccountId, out _); attempt.Cancellation.Dispose();
    }
    private void Validate(LoginOperationContext context)
    {
        CliPrivatePaths.Id(context.OperationId); CliPrivatePaths.Id(context.AccountId);
        if (context.Request.Provider != Provider || context.Request.Method != Method || context.Request.Binding?.Provider is { } p && p != Provider
            || context.ExpiresAt <= Clock.GetUtcNow() || context.Request.Purposes.Length == 0)
            throw new ArgumentException("Invalid CLI connection context.");
    }
    private static void Same(LoginOperationContext a, LoginOperationContext b)
    { if (CliConnectionStateStore.Digest(a) != CliConnectionStateStore.Digest(b)) throw new CliConnectionException("cli-operation-context-mismatch"); }
    protected static bool Matches(ResourceBindingDto? expected, ResourceBindingDto actual) => expected is null ||
        expected.Provider == actual.Provider && (expected.Subject is null || expected.Subject == actual.Subject)
        && (expected.Product is null || expected.Product == actual.Product) && (expected.Region is null || expected.Region == actual.Region)
        && (expected.ResourceType is null || expected.ResourceType == actual.ResourceType) && (expected.ResourceId is null || expected.ResourceId == actual.ResourceId);
    public async ValueTask DisposeAsync()
    { foreach (var attempt in attempts.Values.ToArray()) await ReleaseAsync(attempt.Context, CancellationToken.None); }
}
