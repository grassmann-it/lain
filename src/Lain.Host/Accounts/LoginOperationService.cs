using System.Collections.Concurrent;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Operations;
using Lain.Host.Accounts.Credentials;

namespace Lain.Host.Accounts;

/// <summary>Compatibility boundary for previously qualified adapters.</summary>
public interface IQualifiedLoginAdapter
{
    string Provider { get; }
    Task<LoginDto> BeginAsync(string operationId, CancellationToken ct);
    Task<LoginDto> StatusAsync(string operationId, CancellationToken ct);
    Task CancelAsync(string operationId, CancellationToken ct);
    Task ReleaseAsync(string operationId, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>Adapters perform provider authorization only; the host exclusively publishes credentials.</summary>
public interface IAccountLoginAdapter
{
    string Provider { get; }
    string Method { get; }
    bool RequiresCore => false;
    Task<LoginAdapterResult> BeginAsync(LoginOperationContext context, CancellationToken ct);
    Task<LoginAdapterResult> StatusAsync(LoginOperationContext context, CancellationToken ct);
    Task CancelAsync(LoginOperationContext context, CancellationToken ct);
    Task ReleaseAsync(LoginOperationContext context, CancellationToken ct) => Task.CompletedTask;
    // The explicit target no longer exists, so this result cannot be adopted. Default
    // cleanup releases local operation state only; it never revokes an external grant.
    Task TargetRemovedAsync(LoginOperationContext context, PreparedLoginCredential? prepared, CancellationToken ct) => ReleaseAsync(context, ct);
}
public sealed record LoginOperationContext(string OperationId, string AccountId, long Revision, long AccountGeneration,
    ConnectAccountRequest Request, DateTimeOffset ExpiresAt)
{
    /// <summary>Private fence captured when this explicit target operation was reserved.</summary>
    public string? TargetRemovalEpoch { get; init; }
    /// <summary>Persist an issued result before further provider work. Never publishes or retries remote creation.</summary>
    public Func<PreparedLoginCredential, CancellationToken, Task>? CheckpointCredentialAsync { get; init; }
}
public sealed record LoginAdapterResult(string State, string? Url = null, string? Error = null,
    PreparedLoginCredential? Credential = null, ResourceBindingDto[]? Resources = null, string? NextAction = null)
{
    public override string ToString() => "LoginAdapterResult [redacted]";
}
public sealed record PreparedLoginCredential(string Owner, string[] Purposes, ResourceBindingDto Binding, string PrivateJson,
    DateTimeOffset? ExpiresAt = null, bool PreserveForRecovery = false, string? RemoteId = null)
{
    public override string ToString() => "PreparedLoginCredential [redacted]";
}

/// <summary>Bounded, replay-safe host operations. Remote waits never hold the configuration lock.</summary>
public sealed class LoginOperationService : IAsyncDisposable
{
    public const int MaximumActiveOperations = 8;
    public const int MaximumTerminalOperations = 64;
    public static readonly TimeSpan InteractiveLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan TerminalLifetime = TimeSpan.FromHours(1);
    private readonly Dictionary<string, IQualifiedLoginAdapter> legacy;
    private readonly Dictionary<(string Provider, string Method), IAccountLoginAdapter> adapters;
    private readonly Dictionary<string, Operation> operations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> idempotency = new(StringComparer.Ordinal);
    private readonly object gate = new();
    private readonly AccountService? accounts;
    private readonly ConfigurationRepository? configuration;
    private readonly SecretStore? secrets;
    private readonly TimeProvider clock;
    private readonly SemaphoreSlim? publicationGate;
    private readonly SemaphoreSlim recoveryCleanupGate = new(1, 1);
    private IEnumerator<string>? recoveryScan;
    public const int MaximumRecoveryScanRecords = 64;
    public const int MaximumRecoveryCleanupAttempts = 8;
    private bool disposed;

    private sealed class Operation(LoginDto status, LoginOperationContext? context, DateTimeOffset createdAt)
    {
        public LoginDto Status = status;
        public LoginOperationContext? Context = context;
        public readonly SemaphoreSlim Gate = new(1, 1);
        public readonly object PublicationGate = new();
        public readonly CancellationTokenSource Cancellation = new();
        public DateTimeOffset CreatedAt = createdAt;
        public DateTimeOffset? FinishedAt;
        public bool Invalidated;
        public bool TargetRemoved;
        public bool TargetCleanupCompleted;
        public ITimer? Expiry;
        public CredentialHandleDto? Prepared;
        public PreparedLoginCredential? PreparedCredential;
        public PreparedLoginCredential? PendingCredential;
        public bool PendingPersistence;
        public bool Released;
        public bool CancellationDispatched;
        public string? RequestFingerprint;
        public Task<LoginDto>? BeginTask;
    }

    public LoginOperationService(IEnumerable<IQualifiedLoginAdapter>? qualifiedAdapters = null,
        ConfigurationRepository? configuration = null, AccountService? accounts = null, SecretStore? secrets = null,
        IEnumerable<IAccountLoginAdapter>? accountAdapters = null, TimeProvider? timeProvider = null,
        SemaphoreSlim? publicationGate = null)
    {
        legacy = (qualifiedAdapters ?? []).ToDictionary(a => a.Provider, StringComparer.Ordinal);
        adapters = (accountAdapters ?? []).ToDictionary(a => (a.Provider, a.Method));
        this.configuration = configuration; this.accounts = accounts; this.secrets = secrets;
        clock = timeProvider ?? TimeProvider.System;
        this.publicationGate = publicationGate;
    }

    public bool RequiresCore(string provider, string method) => adapters.TryGetValue((AccountService.CanonicalProviderId(provider), method), out var adapter) && adapter.RequiresCore;

    public Task<LoginDto> BeginAsync(long revision, ConnectAccountRequest request, CancellationToken ct = default)
    {
        request = request with { Provider = AccountService.CanonicalProviderId(request.Provider) };
        if (accounts is null || secrets is null) throw new InvalidOperationException("Account connection service is unavailable.");
        Operation operation;
        lock (gate)
        {
            ThrowIfDisposed(); Prune();
            var fingerprint = JsonSerializer.Serialize(new { revision, request.Provider, request.Method, request.Purposes, request.Name,
                request.AccountId, request.Binding, request.ExpectedGeneration });
            if (request.IdempotencyKey is { } key)
            {
                CredentialGenerationStore.ValidateId(key);
                if (idempotency.TryGetValue(key, out var operationId) && operations.TryGetValue(operationId, out var repeated))
                {
                    if (repeated.RequestFingerprint != fingerprint) throw new ArgumentException("The idempotency key belongs to another connection request.");
                    return repeated.BeginTask ?? Task.FromResult(repeated.Status);
                }
                if (CredentialRecoveryService.FindRequest(secrets.Generations, key, fingerprint, configuration?.Snapshot) is { } recovered)
                {
                    if (recovered.State == "target-removed" && !recovered.TargetCleanupCompleted && operations.Values.Count(NeedsActiveSlot) >= MaximumActiveOperations)
                        throw new InvalidOperationException("Too many active connection operations. Finish local cleanup before retrying recovery.");
                    var status = new LoginDto(recovered.OperationId, recovered.Request.Provider,
                        recovered.State == "published" ? "completed" : recovered.State == "target-removed" ? "cancelled" : "recovery-required", Method: recovered.Request.Method,
                        Purposes: recovered.Request.Purposes, AccountId: recovered.AccountId,
                        NextAction: recovered.State is "published" or "target-removed" ? null : "review-recovery");
                    if (recovered.State == "target-removed") status = status with { Error = "Target account was removed. Connect a new account." };
                    var recoveredContext = CredentialRecoveryService.Context(recovered);
                    var restored = new Operation(status, recoveredContext, clock.GetUtcNow())
                    { FinishedAt = clock.GetUtcNow(), Prepared = recovered.Handle, TargetRemoved = recovered.State == "target-removed",
                        TargetCleanupCompleted = recovered.TargetCleanupCompleted, RequestFingerprint = fingerprint,
                        Released = recovered.State != "target-removed" || recovered.TargetCleanupCompleted };
                    operations[recovered.OperationId] = restored;
                    idempotency[key] = recovered.OperationId;
                    restored.BeginTask = restored.TargetRemoved ? CompleteRecoveredTargetCleanupAsync(restored) : Task.FromResult(status);
                    return restored.BeginTask;
                }
            }
            if (operations.Values.Count(NeedsActiveSlot) >= MaximumActiveOperations)
                throw new InvalidOperationException("Too many active connection operations. Finish or cancel an operation first.");
            var id = Guid.NewGuid().ToString("N");
            var context = accounts.ReserveConnect(revision, request, id, clock.GetUtcNow() + InteractiveLifetime);
            if (request.IdempotencyKey is { } reservedKey) CredentialRecoveryService.ReserveRequest(secrets.Generations, reservedKey, fingerprint, context);
            operation = new(new(id, request.Provider, "preparing", Method: request.Method, Purposes: context.Request.Purposes,
                AccountId: context.AccountId, ExpiresAt: context.ExpiresAt), context, clock.GetUtcNow()) { RequestFingerprint = fingerprint };
            operation.Context = context with { CheckpointCredentialAsync = (credential, _) =>
            {
                PrepareResult(operation, credential with { PreserveForRecovery = true });
                return Task.CompletedTask;
            } };
            operations.Add(id, operation);
            if (request.IdempotencyKey is { } idempotencyKey) idempotency.Add(idempotencyKey, id);
            operation.Expiry = clock.CreateTimer(_ => _ = ExpireAsync(operation), null, InteractiveLifetime, Timeout.InfiniteTimeSpan);
            // The entry is visible before remote Begin; repeated calls share this single task.
            operation.BeginTask = BeginCoreAsync(operation, ct);
        }
        return operation.BeginTask;
    }

    public Task<LoginDto> BeginAsync(string provider, CancellationToken ct = default)
    {
        provider = AccountService.CanonicalProviderId(provider);
        var adapter = adapters.Values.FirstOrDefault(a => a.Provider == provider);
        if (adapter is not null && configuration is not null)
            return BeginAsync(configuration.Snapshot.Revision, new(provider, adapter.Method, ["inference", "quota"]), ct);
        Operation operation;
        lock (gate)
        {
            ThrowIfDisposed(); Prune();
            if (operations.Values.Count(NeedsActiveSlot) >= MaximumActiveOperations)
                throw new InvalidOperationException("Too many active connection operations.");
            var id = Guid.NewGuid().ToString("N");
            operation = new(new(id, provider, "preparing", ExpiresAt: clock.GetUtcNow() + InteractiveLifetime), null, clock.GetUtcNow());
            operations.Add(id, operation);
            operation.Expiry = clock.CreateTimer(_ => _ = ExpireAsync(operation), null, InteractiveLifetime, Timeout.InfiniteTimeSpan);
            operation.BeginTask = BeginCoreAsync(operation, ct);
        }
        return operation.BeginTask;
    }

    private async Task<LoginDto> BeginCoreAsync(Operation operation, CancellationToken ct)
    {
        await operation.Gate.WaitAsync(CancellationToken.None);
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, operation.Cancellation.Token);
            if (operation.Context is { } context)
            {
                if (!adapters.TryGetValue((context.Request.Provider, context.Request.Method), out var adapter))
                    return Finish(operation, "unsupported", "This connection method is not qualified.");
                CredentialRecoveryService.SaveStarted(secrets!.Generations, context);
                return await AcceptAsync(operation, await adapter.BeginAsync(context, linked.Token), linked.Token);
            }
            if (!legacy.TryGetValue(operation.Status.Provider, out var old)) return Finish(operation, "unsupported", "This connection method is not qualified.");
            return AcceptLegacy(operation, await old.BeginAsync(operation.Status.Id, linked.Token));
        }
        catch (OperationCanceledException) { return Finish(operation, operation.Status.State == "expired" ? "expired" : "cancelled"); }
        catch { return Finish(operation, "failed", "Connection could not be started. Reconnect after checking the provider setup."); }
        finally
        {
            operation.Gate.Release();
            if (operation.TargetRemoved) await CleanupRemovedTargetAsync(operation);
            else if (Terminal(operation.Status.State)) await CleanupTerminalAdapterAsync(operation);
        }
    }

    public async Task<LoginDto> StatusAsync(string id, CancellationToken ct = default)
    {
        var operation = Find(id);
        if (operation.Status.ExpiresAt <= clock.GetUtcNow() && !Terminal(operation.Status.State)) await ExpireAsync(operation);
        await operation.Gate.WaitAsync(ct);
        try
        {
            if (operation.PendingPersistence && operation.PendingCredential is { } pending)
            {
                try { PrepareResult(operation, pending); }
                catch (IOException) { return Finish(operation, "recovery-required", "Private credential storage is unavailable. Keep the host running and retry local recovery; do not repeat provider issuance."); }
            }
            if (Terminal(operation.Status.State)) return operation.Status;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, operation.Cancellation.Token);
            if (operation.Context is { } context)
                return await AcceptAsync(operation, await adapters[(context.Request.Provider, context.Request.Method)].StatusAsync(context, linked.Token), linked.Token);
            return AcceptLegacy(operation, await legacy[operation.Status.Provider].StatusAsync(id, linked.Token));
        }
        catch (OperationCanceledException) when (operation.Invalidated) { return operation.Status; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return Finish(operation, "failed", "Connection progress could not be read. Reconnect after checking the provider setup."); }
        finally
        {
            operation.Gate.Release();
            if (operation.TargetRemoved) await CleanupRemovedTargetAsync(operation);
            else if (Terminal(operation.Status.State)) await CleanupTerminalAdapterAsync(operation);
        }
    }

    public async Task<LoginDto> SelectResourceAsync(string id, ResourceBindingDto binding, CancellationToken ct = default)
    {
        var operation = Find(id);
        await operation.Gate.WaitAsync(ct);
        try
        {
            if (Terminal(operation.Status.State)) return operation.Status;
            if (operation.Status.ExpiresAt <= clock.GetUtcNow()) return Finish(operation, "expired");
            if (operation.Context is not { } context || operation.Status.State != "resource-required" ||
                operation.Status.Resources?.Any(r => r == binding) != true)
                throw new ArgumentException("Select one of this operation's discovered resources.");
            AccountService.EnsureCompatibleBinding(context.Request.Binding, binding);
            operation.Context = context with { Request = context.Request with { Binding = binding } };
            operation.Status = operation.Status with { State = "waiting", Resources = null, NextAction = "continue" };
            return operation.Status;
        }
        finally { operation.Gate.Release(); }
    }

    public async Task<LoginDto> CancelAsync(string id, CancellationToken ct = default)
    {
        var operation = Find(id);
        lock (operation.PublicationGate)
        {
            if (!Terminal(operation.Status.State))
            {
                operation.Invalidated = true;
                Finish(operation, "cancelled");
            }
        }
        if (operation.Status.State == "cancelled") operation.Cancellation.Cancel();
        if (operation.TargetRemoved) await CleanupRemovedTargetAsync(operation);
        else await CleanupTerminalAdapterAsync(operation);
        return operation.Status;
    }

    /// <summary>Invalidate only operations reconnecting an account whose removal was persisted.</summary>
    public async Task CancelRemovedAccountAsync(string accountId, CancellationToken? budget = null)
    {
        using var timeout = budget is null ? new CancellationTokenSource(TimeSpan.FromSeconds(5)) : null;
        var ct = budget ?? timeout!.Token;
        await InvalidateRemovedOperationsAsync(accountId, ct);
        await CleanupRemovedRecoveriesAsync(ct);
    }

    private async Task InvalidateRemovedOperationsAsync(string accountId, CancellationToken ct)
    {
        if (configuration is null || secrets is null) return;
        var state = configuration.Snapshot;
        Operation[] selected;
        lock (gate)
        {
            if (disposed) return;
            selected = operations.Values.Where(o => o.Context is { } context && context.AccountId == accountId
                && context.Request.AccountId == accountId && o.Status.State != "completed"
                && CredentialTargetRemoval.IsRemoved(secrets.Generations, context, state)).ToArray();
            foreach (var operation in selected)
                lock (operation.PublicationGate)
                {
                    operation.TargetRemoved = true;
                    operation.Invalidated = true;
                    Finish(operation, "cancelled");
                }
        }
        // Invalidate every matching operation before awaiting any provider-specific local
        // cleanup. A late checkpoint remains private and cannot regain publication rights.
        foreach (var operation in selected)
        {
            try { operation.Cancellation.Cancel(); }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { }
            try { if (operation.Context is { } context) CredentialRecoveryService.MarkTargetRemoved(secrets!.Generations, context); }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
            { /* Cleanup retries the durable invalidation before deleting any owned profile. */ }
        }
        await Task.WhenAll(selected.Select(operation => CleanupRemovedTargetAsync(operation, ct)));
    }

    /// <summary>One bounded local retirement pass. Call at startup and periodically; never starts authorization or revokes grants.</summary>
    public async Task CleanupRemovedRecoveriesAsync(CancellationToken ct = default)
    {
        // This is a nonblocking drain attempt. An exhausted shared HTTP cleanup budget
        // must not throw out of the middleware's post-publication finally block.
        if (configuration is null || secrets is null || ct.IsCancellationRequested || !await recoveryCleanupGate.WaitAsync(0)) return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        ct = timeout.Token;
        try
        {
            lock (gate) { if (disposed) return; }
            string[] targets;
            lock (gate)
                targets = operations.Values.Where(o => o.Context?.Request.AccountId is not null && o.Status.State != "completed")
                    .Select(o => o.Context!.AccountId).Distinct(StringComparer.Ordinal).ToArray();
            foreach (var target in targets)
            {
                if (ct.IsCancellationRequested) return;
                await InvalidateRemovedOperationsAsync(target, ct);
            }
            recoveryScan ??= secrets.Generations.RecoveryFiles().GetEnumerator();
            var attempts = 0;
            for (var inspected = 0; inspected < MaximumRecoveryScanRecords && attempts < MaximumRecoveryCleanupAttempts; inspected++)
            {
                if (ct.IsCancellationRequested) break;
                if (!recoveryScan.MoveNext()) { recoveryScan.Dispose(); recoveryScan = null; break; }
                var path = recoveryScan.Current;
                var id = Path.GetFileNameWithoutExtension(path);
                if (id.StartsWith("request-", StringComparison.Ordinal)) continue;
                // Enumeration progress is retained across passes, including failed candidates. No
                // unbounded retry dictionary or sorted full-directory snapshot is held in memory.
                try
                {
                    CredentialGenerationStore.RefuseLink(path);
                    var record = CredentialRecoveryService.Read(secrets.Generations, id, configuration.Snapshot, reconcilePrepared: false);
                    if (record.State != "target-removed" || record.TargetCleanupCompleted) continue;
                    lock (gate) { if (operations.ContainsKey(id)) continue; }
                    if (!adapters.TryGetValue((record.Request.Provider, record.Request.Method), out var adapter)) continue;
                    if (record.Handle is { } selected && configuration.Snapshot.Accounts.Any(a =>
                        a.Credentials?.Any(h => h.Id == selected.Id && h.Generation == selected.Generation) == true)) continue;
                    attempts++;
                    PreparedLoginCredential? prepared = record.Handle is { } handle
                        ? new(handle.Owner, handle.Purposes, handle.Binding, secrets.Generations.Read(record.AccountId, handle).PrivateJson, handle.ExpiresAt)
                        : null;
                    await adapter.TargetRemovedAsync(CredentialRecoveryService.Context(record), prepared, ct);
                    CredentialRecoveryService.MarkTargetCleanupCompleted(secrets.Generations, id, record.Handle);
                }
                catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
                { /* Durable target-removed state retains retry work; malformed entries never authorize deletion. */ }
            }
            // Terminal local cleanup keeps its original operation slot. A prior cancellation
            // is never sent again; only the adapter's existing local Release is retried.
            Operation[] localReleases;
            lock (gate) localReleases = operations.Values.Where(o => Terminal(o.Status.State) && !o.TargetRemoved && !o.Released)
                .Take(MaximumActiveOperations).ToArray();
            foreach (var operation in localReleases)
            {
                if (ct.IsCancellationRequested) break;
                await CleanupTerminalAdapterAsync(operation, ct);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException)
        { recoveryScan?.Dispose(); recoveryScan = null; }
        finally { recoveryCleanupGate.Release(); }
    }

    private async Task<LoginDto> AcceptAsync(Operation operation, LoginAdapterResult result, CancellationToken ct)
    {
        ValidateUrl(result.Url);
        if (result.State is not ("waiting" or "authorizing" or "resource-required" or "prepared" or "failed" or "cancelled" or "recovery-required"))
            throw new InvalidDataException("Adapter returned an invalid connection phase.");
        var context = operation.Context!;
        if (result.Credential is { } credential)
        {
            // Preserve late remote evidence privately. Target-removal markers still
            // prevent adoption; ordinary cancellation may retain a recoverable result.
            try { PrepareResult(operation, credential); }
            catch (IOException)
            { return Finish(operation, "recovery-required", "Private credential storage is unavailable. Keep the host running and retry local recovery; do not repeat provider issuance."); }
        }
        var publicationAcquired = false;
        if (result.State == "prepared" && publicationGate is not null)
        {
            // Runtime apply holds this host-wide gate through drain and MarkApplied. Only the final
            // local reference publication waits here; provider work and durable checkpoints stay independent.
            try
            {
                await publicationGate.WaitAsync(ct);
                publicationAcquired = true;
            }
            catch (OperationCanceledException)
            {
                lock (operation.PublicationGate)
                {
                    if (operation.Invalidated || context.ExpiresAt <= clock.GetUtcNow())
                        return Finish(operation, operation.Status.State == "cancelled" ? "cancelled" : "expired");
                    return Finish(operation, "recovery-required", "Authorization succeeded and is saved privately. Review and adopt the saved result.");
                }
            }
        }
        try
        {
            lock (operation.PublicationGate)
            {
                if (operation.Invalidated || context.ExpiresAt <= clock.GetUtcNow())
                    return Finish(operation, operation.Status.State == "cancelled" ? "cancelled" : "expired");
                if (result.State == "prepared")
                {
                    if (operation.Prepared is null) throw new InvalidDataException("Completed authorization did not provide a credential.");
                    try
                    {
                        accounts!.CommitPrepared(context, operation.Prepared, () => !operation.Invalidated && clock.GetUtcNow() < context.ExpiresAt);
                        try { CredentialRecoveryService.MarkPublished(secrets!.Generations, context.OperationId); }
                        catch (IOException) { } // Published configuration is authoritative; recovery reconciles its matching handle.
                        return Finish(operation, "completed");
                    }
                    catch (Exception error) when (error is RevisionConflictException or IOException or InvalidOperationException or ArgumentException)
                    {
                        // Never repeat remote key creation or rotation to resolve a local CAS conflict.
                        CredentialRecoveryService.SavePrepared(secrets!.Generations, context, operation.Prepared, result.Credential?.RemoteId);
                        return Finish(operation, "recovery-required", "Authorization succeeded but local state changed. Review and adopt the saved result.");
                    }
                }
                if (Terminal(result.State))
                {
                    var status = Finish(operation, result.State, result.Error ?? (result.State == "failed" ? "Provider authorization failed." : null));
                    if (result.NextAction is "review-provider-keys" or "reconnect" or "review-recovery")
                        operation.Status = status with { NextAction = result.NextAction };
                    return operation.Status;
                }
                var resources = result.Resources;
                if (resources is { Length: > 64 } || resources?.Any(r => AccountService.CanonicalProviderId(r.Provider) != context.Request.Provider) == true)
                    throw new InvalidDataException("Invalid discovered resource list.");
                operation.Status = operation.Status with { State = result.State, Url = result.Url, Error = null,
                    Resources = resources, NextAction = result.State == "resource-required" ? "select-resource" : "authorize" };
                return operation.Status;
            }
        }
        finally { if (publicationAcquired) publicationGate!.Release(); }
    }

    private void PrepareResult(Operation operation, PreparedLoginCredential credential)
    {
        lock (operation.PublicationGate)
        {
            var context = operation.Context!;
            var durable = credential with { PreserveForRecovery = true };
            var prior = operation.PreparedCredential;
            operation.PendingPersistence = true;
            operation.PendingCredential = durable;
            if (operation.Prepared is null || prior is null || prior.Owner != durable.Owner || prior.Binding != durable.Binding || prior.PrivateJson != durable.PrivateJson ||
                prior.ExpiresAt != durable.ExpiresAt || !prior.Purposes.SequenceEqual(durable.Purposes))
            {
                operation.Prepared = secrets!.Generations.Prepare(context.AccountId, durable, context.OperationId,
                    operation.Prepared?.Id, operation.Prepared is null ? 1 : checked(operation.Prepared.Generation + 1));
                operation.PreparedCredential = durable;
                if (operation.TargetRemoved) operation.TargetCleanupCompleted = false;
            }
            CredentialRecoveryService.SavePrepared(secrets!.Generations, context, operation.Prepared!, durable.RemoteId);
            operation.PendingPersistence = false;
        }
    }

    private LoginDto AcceptLegacy(Operation operation, LoginDto status)
    {
        if (status.Id != operation.Status.Id || status.Provider != operation.Status.Provider) throw new InvalidDataException("Invalid login operation identity.");
        ValidateUrl(status.Url);
        lock (operation.PublicationGate)
        {
            if (operation.Invalidated) return operation.Status;
            operation.Status = status with { ExpiresAt = operation.Status.ExpiresAt };
            if (Terminal(status.State)) Finish(operation, status.State, status.State == "failed" ? "Provider authorization failed." : null);
            return operation.Status;
        }
    }
    private LoginDto Finish(Operation operation, string state, string? error = null)
    {
        lock (operation.PublicationGate)
        {
            if (operation.Status.State == "completed") return operation.Status;
            if (operation.TargetRemoved) { state = "cancelled"; error = "Target account was removed. Connect a new account."; }
            operation.Status = operation.Status with { State = state, Url = null, Resources = null, Error = error,
                NextAction = state == "recovery-required" ? "review-recovery" : null };
            operation.FinishedAt ??= clock.GetUtcNow();
            operation.Expiry?.Dispose();
            return operation.Status;
        }
    }
    private async Task ExpireAsync(Operation operation)
    {
        lock (operation.PublicationGate)
        {
            if (Terminal(operation.Status.State)) return;
            operation.Invalidated = true; Finish(operation, "expired");
        }
        operation.Cancellation.Cancel();
        await CleanupTerminalAdapterAsync(operation);
    }
    private async Task CleanupTerminalAdapterAsync(Operation operation, CancellationToken? budget = null)
    {
        // One bounded serial attempt. Keep the operation/profile until local release succeeds;
        // subsequent attempts must not resend cancellation of an external authorization.
        using var timeout = budget is null ? new CancellationTokenSource(TimeSpan.FromSeconds(5)) : null;
        var ct = budget ?? timeout!.Token;
        try
        {
            await operation.Gate.WaitAsync(ct);
            try
            {
                if (operation.Released || operation.TargetRemoved || !Terminal(operation.Status.State)) return;
                if (operation.Context is { } context && adapters.TryGetValue((context.Request.Provider, context.Request.Method), out var adapter))
                {
                    if (!operation.CancellationDispatched && operation.Status.State is ("failed" or "cancelled" or "expired"))
                    {
                        operation.CancellationDispatched = true;
                        await adapter.CancelAsync(context, ct);
                    }
                    await adapter.ReleaseAsync(context, ct);
                }
                else if (legacy.TryGetValue(operation.Status.Provider, out var old))
                {
                    if (!operation.CancellationDispatched && operation.Status.State is ("failed" or "cancelled" or "expired"))
                    {
                        operation.CancellationDispatched = true;
                        await old.CancelAsync(operation.Status.Id, ct);
                    }
                    await old.ReleaseAsync(operation.Status.Id, ct);
                }
                operation.Released = true;
            }
            finally { operation.Gate.Release(); }
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { }
    }
    private async Task CleanupRemovedTargetAsync(Operation operation, CancellationToken? budget = null)
    {
        using var timeout = budget is null ? new CancellationTokenSource(TimeSpan.FromSeconds(5)) : null;
        var ct = budget ?? timeout!.Token;
        try
        {
            await operation.Gate.WaitAsync(ct);
            try
            {
                if (!operation.TargetRemoved || operation.TargetCleanupCompleted) return;
                if (operation.Context is { } context && adapters.TryGetValue((context.Request.Provider, context.Request.Method), out var adapter))
                {
                    CredentialRecoveryService.MarkTargetRemoved(secrets!.Generations, context);
                    var prepared = operation.PendingCredential ?? operation.PreparedCredential;
                    if (prepared is null && operation.Prepared is { } handle)
                        prepared = new(handle.Owner, handle.Purposes, handle.Binding, secrets.Generations.Read(context.AccountId, handle).PrivateJson, handle.ExpiresAt);
                    await adapter.TargetRemovedAsync(context, prepared, ct);
                    CredentialRecoveryService.MarkTargetCleanupCompleted(secrets!.Generations, context.OperationId, operation.Prepared);
                }
                operation.TargetCleanupCompleted = true;
                operation.Released = true;
            }
            finally { operation.Gate.Release(); }
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        { /* Keep the active cleanup slot; polling or shutdown retries after a late result. */ }
    }
    private async Task<LoginDto> CompleteRecoveredTargetCleanupAsync(Operation operation)
    {
        await CleanupRemovedTargetAsync(operation);
        return operation.Status;
    }
    private Operation Find(string id)
    {
        lock (gate) { ThrowIfDisposed(); Prune(); return operations.GetValueOrDefault(id) ?? throw new KeyNotFoundException("Login operation not found."); }
    }
    private void Prune()
    {
        var terminal = operations.Where(p => p.Value.FinishedAt is not null && p.Value.Released && !p.Value.PendingPersistence
            && (!p.Value.TargetRemoved || p.Value.TargetCleanupCompleted)).OrderByDescending(p => p.Value.FinishedAt).ToArray();
        foreach (var pair in terminal.Where((p, index) => index >= MaximumTerminalOperations || p.Value.FinishedAt + TerminalLifetime <= clock.GetUtcNow()))
        {
            operations.Remove(pair.Key); pair.Value.Expiry?.Dispose();
            foreach (var key in idempotency.Where(p => p.Value == pair.Key).Select(p => p.Key).ToArray()) idempotency.Remove(key);
        }
    }
    private void ThrowIfDisposed() { if (disposed) throw new ObjectDisposedException(nameof(LoginOperationService)); }
    private static bool NeedsActiveSlot(Operation operation) => !Terminal(operation.Status.State) || operation.PendingPersistence
        || !operation.Released || operation.TargetRemoved && !operation.TargetCleanupCompleted;
    private static bool Terminal(string state) => state is "completed" or "cancelled" or "expired" or "failed" or "unsupported" or "recovery-required";
    private static void ValidateUrl(string? url)
    {
        if (url is not null && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo)))
            throw new InvalidDataException("Invalid login authorization URL.");
    }
    public async ValueTask DisposeAsync()
    {
        Operation[] active;
        lock (gate) { if (disposed) return; disposed = true; active = operations.Values.ToArray(); }
        await Task.WhenAll(active.Select(async operation =>
        {
            if (!Terminal(operation.Status.State)) await ExpireAsync(operation);
            // An exchange may deliberately finish after cancellation to preserve an issued one-time secret.
            if (!await operation.Gate.WaitAsync(TimeSpan.FromSeconds(25))) return;
            try
            {
                if (operation.PendingPersistence && operation.PendingCredential is { } credential)
                    try { PrepareResult(operation, credential); } catch (IOException) { }
            }
            finally { operation.Gate.Release(); }
            if (operation.TargetRemoved) await CleanupRemovedTargetAsync(operation);
            else await CleanupTerminalAdapterAsync(operation);
        }));
        await recoveryCleanupGate.WaitAsync();
        try { recoveryScan?.Dispose(); recoveryScan = null; }
        finally { recoveryCleanupGate.Release(); }
    }
}
