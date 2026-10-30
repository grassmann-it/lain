using Lain.Core;
using Lain.Host.Accounts;

namespace Lain.Host.Quota;

public sealed record RouterAvailabilityCoreState(int SchemaVersion, string Generation, ulong Sequence);
public sealed record RouterAvailabilityEntry(string RouterId, string ProfileId, string RouteId, string MemberFingerprint,
    string AccountId, string Provider, string Model, DateTimeOffset BlockedUntil, string Reason = "quota_exhausted");
public sealed record RouterAvailabilityReplacement(int SchemaVersion, string Generation, ulong Sequence,
    DateTimeOffset ObservedAt, DateTimeOffset ExpiresAt, RouterAvailabilityEntry[] Entries);
public sealed record RouterAvailabilityMember(string RouterId, string ProfileId, string RouteId, string MemberFingerprint,
    string AccountId, string Provider, string Model);

/// <summary>A durable applied-runtime configuration already verified by its owner. This value is read-only evidence here.</summary>
public sealed record RouterAvailabilityAppliedSnapshot(long Revision, string GenerationId, ConfigurationDto Configuration);

public interface IRouterAvailabilityAppliedSnapshotSource
{
    Task<RouterAvailabilityAppliedSnapshot?> GetRouterAvailabilityAppliedSnapshotAsync(CancellationToken ct);
}

/// <summary>Distinguishes unknown active state (skip) from a known empty replacement.</summary>
public readonly record struct RouterAvailabilityProjection(RouterAvailabilityEntry[] Entries,
    bool IsKnownState = true, bool CanOmitEntries = true);

/// <summary>Only proven Kimi Code and Z.ai coding subscription windows are supported.
/// Other providers, balances, rates, MCP limits, generic scopes and missing resets remain unknown.</summary>
public sealed class RouterAvailabilityProjector(TimeProvider? timeProvider = null)
{
    public static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(5);
    public const int MaximumEntries = 4096;
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public RouterAvailabilityEntry[] Project(ConfigurationDto configuration, IReadOnlyList<RouterAvailabilityMember> members,
        IReadOnlyList<RouterAvailabilityQuotaSnapshot> snapshots,
        RouterAvailabilityAppliedSnapshot? appliedSnapshot = null) =>
        ProjectState(configuration, members, snapshots, appliedSnapshot).Entries;

    public RouterAvailabilityProjection ProjectState(ConfigurationDto configuration, IReadOnlyList<RouterAvailabilityMember> members,
        IReadOnlyList<RouterAvailabilityQuotaSnapshot> snapshots,
        RouterAvailabilityAppliedSnapshot? appliedSnapshot = null)
    {
        if (configuration.RuntimeMode != "gateway") return new([]);
        if (configuration.Revision != configuration.AppliedRevision && appliedSnapshot is null)
            return new([], IsKnownState: false, CanOmitEntries: false);
        if (appliedSnapshot is not null && !IsUsableAppliedSnapshot(appliedSnapshot, configuration.AppliedRevision))
            return new([], IsKnownState: false, CanOmitEntries: false);
        var runtime = appliedSnapshot is null ? configuration : appliedSnapshot.Configuration with
        {
            Revision = appliedSnapshot.Revision,
            AppliedRevision = appliedSnapshot.Revision
        };
        var now = clock.GetUtcNow();
        var result = new List<RouterAvailabilityEntry>();
        var credentialFence = false;
        foreach (var member in members)
        {
            var route = runtime.Routes.SingleOrDefault(r => r.Id == member.RouteId);
            var account = runtime.Accounts.SingleOrDefault(a => a.Id == member.AccountId);
            var currentAccount = configuration.Accounts.SingleOrDefault(a => a.Id == member.AccountId);
            var router = (runtime.CollectionRouters ?? []).SingleOrDefault(r => r.Id == member.RouterId);
            if (route is not { Enabled: true, Visible: true, Pooled: false } || route.AccountIds.Length != 1 ||
                route.AccountIds[0] != member.AccountId || route.UpstreamModel != member.Model || route.Provider != member.Provider ||
                account is not { Enabled: true } || AccountService.CanonicalProviderId(account.Provider) != AccountService.CanonicalProviderId(member.Provider) ||
                router is not { Enabled: true } || router.ProfileId != member.ProfileId) continue;
            if (!CurrentCredentialAllowsAppliedAccount(account, currentAccount))
            {
                // A desired retirement/recovery fence is authoritative. Do not replace an
                // older complete cache with one that silently makes that member eligible.
                credentialFence = true;
                continue;
            }

            var until = snapshots.Where(s => s.Reading.AccountId == member.AccountId)
                .Select(s => BlockedUntil(account, member, s, now)).Where(t => t.HasValue).Max();
            if (until is null) continue;
            result.Add(new(member.RouterId, member.ProfileId, member.RouteId, member.MemberFingerprint,
                member.AccountId, member.Provider, member.Model, until.Value));
            if (result.Count > MaximumEntries)
                return new([], IsKnownState: false, CanOmitEntries: false); // Never publish a truncated replacement.
        }
        return new(result.Distinct().ToArray(), CanOmitEntries: !credentialFence);
    }

    private static bool IsUsableAppliedSnapshot(RouterAvailabilityAppliedSnapshot snapshot, long appliedRevision) =>
        snapshot.Revision == appliedRevision && snapshot.Revision >= 0 &&
        !string.IsNullOrWhiteSpace(snapshot.GenerationId) && snapshot.GenerationId.Length <= 200 &&
        snapshot.Configuration is not null &&
        snapshot.Configuration.Revision == snapshot.Revision &&
        snapshot.Configuration.AppliedRevision == snapshot.Revision &&
        snapshot.Configuration.Accounts is not null && snapshot.Configuration.Routes is not null &&
        snapshot.Configuration.Profiles is not null;

    private static bool CurrentCredentialAllowsAppliedAccount(AccountDto applied, AccountDto? current)
    {
        if (current is not { Enabled: true } || current.Generation != applied.Generation ||
            current.CredentialKind != applied.CredentialKind || current.ExpiresAt != applied.ExpiresAt ||
            current.Binding != applied.Binding ||
            AccountService.CanonicalProviderId(current.Provider) != AccountService.CanonicalProviderId(applied.Provider) ||
            current.State is "recovery-required" or "missing-credential" or "expired" or "retired" or "revoked") return false;
        var oldHandles = applied.Credentials ?? [];
        var currentHandles = current.Credentials ?? [];
        return oldHandles.Length == currentHandles.Length &&
            oldHandles.OrderBy(h => h.Id, StringComparer.Ordinal).Zip(currentHandles.OrderBy(h => h.Id, StringComparer.Ordinal), SameHandle).All(ok => ok);
    }

    private static bool SameHandle(CredentialHandleDto left, CredentialHandleDto right) =>
        left.Id == right.Id && left.Generation == right.Generation && left.Owner == right.Owner &&
        left.ExpiresAt == right.ExpiresAt && left.Binding == right.Binding &&
        left.Purposes.OrderBy(p => p, StringComparer.Ordinal).SequenceEqual(right.Purposes.OrderBy(p => p, StringComparer.Ordinal));

    private static DateTimeOffset? BlockedUntil(AccountDto account, RouterAvailabilityMember member,
        RouterAvailabilityQuotaSnapshot snapshot, DateTimeOffset now)
    {
        var row = snapshot.Reading;
        var proof = snapshot.Proof;
        if (proof is null || proof.AccountId != account.Id || proof.AccountGeneration != account.Generation ||
            proof.Provider != AccountService.CanonicalProviderId(member.Provider) ||
            RouterAvailabilityProofStore.CodingEndpoint(proof.Provider, proof.Endpoint) is null ||
            row.State != "ok" || row.Error is not null || row.ObservedAt is not { } observed ||
            row.LastSuccessAt is not { } success || observed > now || success > now ||
            row.DataAsOf > now || row.ProviderUpdatedAt > now ||
            row.MeasurementKind is not (null or "reported-quota") ||
            row.Model is not null && row.Model != member.Model || !SupportedWindow(proof, row)) return null;
        if (!Exhausted(row)) return null;

        // Freshness is capped by the original observation. Publishing every 15 seconds
        // does not renew a provider observation or manufacture a reset.
        var until = observed + MaximumAge;
        until = Min(until, success + MaximumAge);
        if (row.FreshUntil is { } fresh) until = Min(until, fresh);
        if (row.DataAsOf is { } data) until = Min(until, data + MaximumAge);
        var reset = row.ResetsAt;
        if (row.WindowEnd is { } end) reset = reset is { } value ? Min(value, end) : end;
        if (reset is null || reset <= now) return null;
        until = Min(until, reset.Value);
        return until > now ? until : null;
    }

    private static bool SupportedWindow(RouterAvailabilityQuotaProof proof, QuotaDto row)
    {
        // These exact adapter sources describe coding allowance, not account money.
        // Single-transport proof joins that allowance to this inference resource.
        if (row.Scope is not (null or "account") || row.ScopeId is not null) return false;
        if (proof.Provider == "kimi")
            return row.Unit == "percent" && row.Source.StartsWith(KimiQuotaAdapter.Endpoint + "#", StringComparison.Ordinal) &&
                row.Source[(KimiQuotaAdapter.Endpoint.Length + 1)..] is "limit_5h" or "limit_7d" or "limit_month_total" or "limit_month_code";
        if (proof.Provider != "zai") return false;
        return (row.Unit == "tokens" && row.Source is ZaiQuotaAdapter.Endpoint + "#TOKENS_LIMIT/unit/3/number/5" or
                    ZaiQuotaAdapter.Endpoint + "#TOKENS_LIMIT/unit/6/number/1") ||
               (row.Unit == "credits" && row.Source is ZaiQuotaAdapter.Endpoint + "#CREDIT_LIMIT/unit/3/number/5" or
                    ZaiQuotaAdapter.Endpoint + "#CREDIT_LIMIT/unit/6/number/1");
    }

    private static bool Exhausted(QuotaDto row)
    {
        static bool Valid(double? value) => value is null || double.IsFinite(value.Value) && value.Value >= 0;
        if (!Valid(row.Remaining) || !Valid(row.Used) || !Valid(row.Limit) || !Valid(row.UsedPercent)) return false;
        // Conflicting positive remaining is not a proven exhaustion observation.
        if (row.Remaining > 0 || row.Limit == 0 || row.UsedPercent > 100) return false;
        return row.Remaining == 0 || row.Limit > 0 && row.Used >= row.Limit || row.UsedPercent == 100;
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left < right ? left : right;
}
