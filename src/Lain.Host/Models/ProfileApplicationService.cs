using System.Text.Json;
using Lain.Core;
using Lain.Host.Operations;

namespace Lain.Host.Models;

/// <summary>The drain gate may still be closed; ordinary refusal cannot restore a success marker.</summary>
public sealed class ProfileDrainRecoveryRequiredException() : InvalidOperationException(
    "The previous runtime's request admission could not be confirmed. Retry profile application to recover.");

public sealed class ProfileDrainTimedOutException() : InvalidOperationException(
    "Active requests are still running. The previous runtime remains available; retry Apply after they finish.");

/// <summary>
/// Applies a single global runtime. The durable marker is written before touching the Core;
/// an interrupted publication must be repaired by an explicit apply, never by starting partial files.
/// Callers must serialize configuration mutations with this operation.
/// </summary>
public sealed class ProfileApplicationService(
    OwnStatePaths paths, ConfigurationRepository repository, ModelsService models,
    Func<CancellationToken, Task> drainAndStop,
    Func<ConfigurationDto, ProfileDto, CancellationToken, Task> writeAndStart,
    Func<CancellationToken, Task> stopAfterFailure,
    Action<ConfigurationDto, ProfileDto>? validateRuntime = null)
{
    public const string StreamBehavior = "Apply waits for active requests to finish. If the drain deadline expires, apply is refused and the previous runtime is retained. All clients share one runtime.";
    private readonly SemaphoreSlim gate = new(1, 1);
    private ApplicationOperation? currentOperation;
    private string OperationPath => Path.Combine(paths.Root, "profile-application.json");
    private string ActivePath => Path.Combine(paths.Root, "active-profile.json");
    private string RuntimePath => Path.Combine(paths.Proxy, "runtime.json");
    private string SnapshotPath => Path.Combine(paths.Root, "applied-profile-snapshot.json");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public ProfileApplicationStatusDto Status
    {
        get
        {
            if (Volatile.Read(ref currentOperation) is { } running)
                return new(running.State, running.Id, running.ProfileId, running.Revision);
            if (!AppliedProfileStatus.MetadataEntryExists(OperationPath))
            {
                var appliedRevision = repository.Snapshot.AppliedRevision;
                if (AppliedProfileStatus.CanPreserveLegacyRuntime(ActivePath, RuntimePath, appliedRevision))
                {
                    var active = AppliedProfileStatus.Read(ActivePath, RuntimePath, appliedRevision);
                    return new("legacy-preserved", null, active.Id, appliedRevision);
                }
                if (Directory.Exists(OperationPath) ||
                    ((File.Exists(ActivePath) || File.Exists(RuntimePath)) &&
                     AppliedProfileStatus.Read(ActivePath, RuntimePath, repository.Snapshot.AppliedRevision).Id is null))
                    return Recovery(null);
                return new("idle", null, null, null);
            }
            try
            {
                var operation = Read<ApplicationOperation>(OperationPath);
                if (operation is null || operation.Id is null || operation.ProfileId is null || operation.Revision < 0)
                    return Recovery(null);
                if (operation.State == "completed" &&
                    AppliedProfileStatus.Read(ActivePath, RuntimePath, repository.Snapshot.AppliedRevision).GenerationId == operation.Id)
                    return new(operation.State, operation.Id, operation.ProfileId, operation.Revision);
                return Recovery(operation);
            }
            catch (Exception error) when (ReadFailure(error)) { return Recovery(null); }
        }
    }

    public void RequireCompletedRuntime()
    {
        var status = Status;
        if (status.State == "legacy-preserved" &&
            AppliedProfileStatus.CanPreserveLegacyRuntime(ActivePath, RuntimePath, repository.Snapshot.AppliedRevision)) return;
        if (status.State is not ("idle" or "completed") ||
            AppliedProfileStatus.Read(ActivePath, RuntimePath, repository.Snapshot.AppliedRevision).Id is null)
            throw new InvalidOperationException("Runtime application is incomplete. Preview and apply a saved profile to recover.");
    }

    public ProfileApplyPreviewDto Preview(string id, long revision)
    {
        var selected = models.ResolveForApply(id, revision);
        var routes = selected.Routes.Where(x => x.Enabled).ToArray();
        var active = AppliedProfileStatus.Read(ActivePath, RuntimePath, selected.Configuration.AppliedRevision);
        AppliedSnapshot? previous = null;
        try
        {
            if (active.Id is not null && File.Exists(SnapshotPath))
            {
                var candidate = Read<AppliedSnapshot>(SnapshotPath);
                if (candidate is { Profile: not null, Routes: not null, Accounts: not null } &&
                    candidate.Routes.All(route => route is not null && route.AccountIds is not null) &&
                    candidate.Accounts.All(account => account is not null) &&
                    candidate.Profile.Id == active.Id && candidate.Revision == active.Revision && candidate.GenerationId == active.GenerationId)
                    previous = candidate;
            }
        }
        catch (Exception error) when (ReadFailure(error)) { }
        var oldRoutes = previous?.Routes ?? [];
        var added = routes.Where(x => !oldRoutes.Any(old => old.Id == x.Id)).Select(x => x.Id).ToArray();
        var changed = routes.Where(x => oldRoutes.Any(old => old.Id == x.Id && !Same(old, x))).Select(x => x.Id).ToArray();
        var removed = oldRoutes.Where(x => !routes.Any(next => next.Id == x.Id)).Select(x => x.Id).ToArray();
        var changedIds = added.Concat(changed).Concat(removed).ToHashSet(StringComparer.Ordinal);
        var accounts = routes.Concat(oldRoutes).Where(x => changedIds.Contains(x.Id)).SelectMany(x => x.AccountIds)
            .Concat(selected.Configuration.Accounts.Where(a => routes.Any(r => r.AccountIds.Contains(a.Id)) &&
                (previous is null || !previous.Accounts.Any(old => old.Id == a.Id && Same(old, a)))).Select(a => a.Id))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return new(Api.Version, id, revision, active.Id, active.Revision, added, changed, removed, accounts,
            ["all gateway clients", "Codex exports may require regeneration"], StreamBehavior, previous is not null, selected.Profile.DefaultRouteId, previous?.Profile.DefaultRouteId,
            previous is null || !Same(previous.Profile, selected.Profile) || !Same(previous.Routers ?? [], RouterSnapshot(selected.Configuration, selected.Profile)));
    }

    public async Task<ConfigurationDto> ApplyAsync(string id, long revision, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        ApplicationOperation? operation = null;
        var invalidated = false;
        string? previousOperation = null;
        var journalWritten = false;
        try
        {
            var selected = models.ResolveForApply(id, revision);
            var activeRoutes = selected.Routes.Where(x => x.Enabled).ToArray();
            var accounts = activeRoutes.SelectMany(x => x.AccountIds).ToHashSet(StringComparer.Ordinal);
            var runtime = selected.Configuration with { Routes = activeRoutes, Accounts = selected.Configuration.Accounts.Where(x => accounts.Contains(x.Id)).ToArray() };
            // Reject invalid credentials/transports/imported options before any
            // operation marker or admission change touches the working runtime.
            validateRuntime?.Invoke(runtime, selected.Profile);
            previousOperation = File.Exists(OperationPath) ? File.ReadAllText(OperationPath) : null;
            operation = new(Guid.NewGuid().ToString("N"), id, revision, "draining");
            Write(OperationPath, operation);
            journalWritten = true;
            Volatile.Write(ref currentOperation, operation);
            // Ordinary drain failure guarantees confirmed request admission on the old runtime.
            // If reopening cannot be confirmed, the callback must throw ProfileDrainRecoveryRequiredException.
            // No forced stop is permitted on either refusal path.
            await drainAndStop(ct);
            // After the destructive boundary a caller disconnect must not leave publication half done.
            invalidated = true;
            Write(RuntimePath, new { valid = false, revision, profileId = id, generationId = operation.Id });
            Volatile.Write(ref currentOperation, operation with { State = "publishing" });
            Write(OperationPath, operation with { State = "publishing" });
            await writeAndStart(runtime, selected.Profile, CancellationToken.None);
            Write(SnapshotPath, new AppliedSnapshot(revision, operation.Id, selected.Profile, activeRoutes, runtime.Accounts, RouterSnapshot(selected.Configuration, selected.Profile)));
            Write(ActivePath, new { id, revision, generationId = operation.Id });
            repository.MarkApplied(revision);
            Write(RuntimePath, new { valid = true, revision, profileId = id, generationId = operation.Id });
            Write(OperationPath, operation with { State = "completed" });
            return repository.Snapshot;
        }
        catch (Exception error)
        {
            if (operation is not null)
            {
                if (invalidated)
                {
                    // Even a late metadata write failure is not an active success. Keep the journal
                    // incomplete and prevent a partially published runtime from serving requests.
                    try { await stopAfterFailure(CancellationToken.None); } catch { }
                    TryWrite(OperationPath, operation with { State = "recovery-required" });
                    TryWrite(RuntimePath, new { valid = false, revision, profileId = id, generationId = operation.Id });
                }
                else if (journalWritten && error is ProfileDrainRecoveryRequiredException)
                {
                    // The Core may still reject new requests. Keep the old runtime files intact,
                    // but never restore their completed operation proof until admission is known.
                    // Active streams are left to finish; recovery is a fresh explicit apply.
                    TryWrite(OperationPath, operation with { State = "recovery-required" });
                }
                else if (journalWritten)
                {
                    // Restore the previous transaction proof, not a new success-shaped marker.
                    // This also preserves an existing recovery requirement when a retry cannot drain.
                    try
                    {
                        if (previousOperation is null) File.Delete(OperationPath);
                        else OwnStatePaths.WritePrivate(OperationPath, previousOperation);
                    }
                    catch (Exception restoreError) when (restoreError is IOException or UnauthorizedAccessException) { }
                }
            }
            throw;
        }
        finally { Volatile.Write(ref currentOperation, null); gate.Release(); }
    }

    private static ProfileApplicationStatusDto Recovery(ApplicationOperation? operation) => new("recovery-required",
        operation?.Id, operation?.ProfileId, operation?.Revision,
        "Preview and explicitly apply a saved profile again. The Core will not start from incomplete runtime files.");
    private static bool Same<T>(T left, T right) => JsonSerializer.Serialize(left, Json) == JsonSerializer.Serialize(right, Json);
    private static bool ReadFailure(Exception error) => error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException;
    private static T? Read<T>(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (input.Length > 4 * 1024 * 1024) throw new IOException("Profile metadata exceeds its size limit.");
        return JsonSerializer.Deserialize<T>(input, Json);
    }
    private static void Write<T>(string path, T value) => OwnStatePaths.WritePrivate(path, JsonSerializer.Serialize(value, Json));
    private static void TryWrite<T>(string path, T value) { try { Write(path, value); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { } }
    private sealed record ApplicationOperation(string Id, string ProfileId, long Revision, string State);
    private static AppliedRouter[] RouterSnapshot(ConfigurationDto configuration, ProfileDto profile) =>
        (configuration.CollectionRouters ?? []).Where(router => router.ProfileId == profile.Id).Select(router =>
            new AppliedRouter(router, CollectionRouterCompiler.MemberIds(configuration, profile, router))).ToArray();
    private sealed record AppliedRouter(CollectionRouterDto Router, string[] Members);
    private sealed record AppliedSnapshot(long Revision, string GenerationId, ProfileDto Profile, RouteDto[] Routes, AccountDto[] Accounts, AppliedRouter[]? Routers = null);
}
