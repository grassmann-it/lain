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

/// <summary>Only proven Kimi Code and Z.ai coding subscription windows are supported.
/// Other providers, balances, rates, MCP limits, generic scopes and missing resets remain unknown.</summary>
public sealed class RouterAvailabilityProjector(TimeProvider? timeProvider = null)
{
    public static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(5);
    public const int MaximumEntries = 4096;
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public RouterAvailabilityEntry[] Project(ConfigurationDto configuration, IReadOnlyList<RouterAvailabilityMember> members,
        IReadOnlyList<RouterAvailabilityQuotaSnapshot> snapshots)
    {
        if (configuration.RuntimeMode != "gateway" || configuration.Revision != configuration.AppliedRevision) return [];
        var now = clock.GetUtcNow();
        var result = new List<RouterAvailabilityEntry>();
        foreach (var member in members)
        {
            var route = configuration.Routes.SingleOrDefault(r => r.Id == member.RouteId);
            var account = configuration.Accounts.SingleOrDefault(a => a.Id == member.AccountId);
            var router = (configuration.CollectionRouters ?? []).SingleOrDefault(r => r.Id == member.RouterId);
            if (route is not { Enabled: true, Visible: true, Pooled: false } || route.AccountIds.Length != 1 ||
                route.AccountIds[0] != member.AccountId || route.UpstreamModel != member.Model || route.Provider != member.Provider ||
                account is not { Enabled: true } || AccountService.CanonicalProviderId(account.Provider) != AccountService.CanonicalProviderId(member.Provider) ||
                router is not { Enabled: true } || router.ProfileId != member.ProfileId) continue;

            var until = snapshots.Where(s => s.Reading.AccountId == member.AccountId)
                .Select(s => BlockedUntil(account, member, s, now)).Where(t => t.HasValue).Max();
            if (until is null) continue;
            result.Add(new(member.RouterId, member.ProfileId, member.RouteId, member.MemberFingerprint,
                member.AccountId, member.Provider, member.Model, until.Value));
            if (result.Count > MaximumEntries) return []; // Never publish a truncated replacement.
        }
        return result.Distinct().ToArray();
    }

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
