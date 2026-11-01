using Lain.Client;
using Lain.Core;

namespace Lain.Desktop.Presentation;

public sealed record AccountOutcomeRow(string Code, string Label, string State, string Value,
    string Detail, string Evidence, DateTimeOffset? ObservedAt = null);
public sealed record AccountOutcomeSummaryDto(string AccountId, string AccountName,
    IReadOnlyList<AccountOutcomeRow> Rows, int ConfiguredRouteCount, int EnabledVisibleRouteCount);

/// <summary>
/// Pure account-detail presentation of existing snapshots. Credential/capability declarations,
/// route configuration, global gateway state and quota observations prove different things.
/// This component neither performs probes nor qualifies provider inference from other purposes.
/// </summary>
public static class AccountOutcomeSummary
{
    public static AccountOutcomeSummaryDto Build(AccountDto account, IEnumerable<RouteDto> routes,
        IEnumerable<QuotaDto> quotas, StatusDto? status, DateTimeOffset now)
    {
        var bound = routes.Where(r => r.AccountIds.Contains(account.Id, StringComparer.Ordinal))
            .DistinctBy(r => r.Id, StringComparer.Ordinal).ToArray();
        var visible = bound.Count(r => r.Enabled && r.Visible);
        var observations = quotas.Where(q => StringComparer.Ordinal.Equals(q.AccountId, account.Id)).ToArray();
        var rows = new List<AccountOutcomeRow>();
        var expired = account.ExpiresAt is { } expiry && expiry <= now;
        var credentialState = expired ? "expired" : account.State switch
        {
            "missing-credential" => "missing",
            "expired" => "expired",
            "recovery-required" => "recovery_required",
            "unverified" or "available" => "configured",
            _ => "unknown"
        };
        rows.Add(new("credential", "Credential", credentialState, credentialState switch
        {
            "missing" => "Credential missing",
            "expired" => "Credential expired",
            "recovery_required" => "Credential recovery required",
            "configured" => "Credential configured",
            _ => "Credential presence not established"
        }, "Local credential state does not establish provider authentication or entitlement.", "account_snapshot"));

        var inference = Capability(account, "inference");
        var inferenceState = LocalCapabilityState(inference, now);
        rows.Add(new("inference", "Inference", inferenceState,
            inferenceState switch
            {
                "configured" => "Configured for inference; provider response unverified",
                "missing" => "Inference credential missing",
                "expired" => "Inference credential expired",
                "recovery_required" => "Inference credential recovery required",
                "not_configured" => "No inference capability configured",
                "unsupported" => "Inference unsupported by this connection",
                _ => "Inference capability not established"
            }, "Quota readings, enabled routes and gateway readiness do not verify this account's inference access.",
            inference.Evidence));

        rows.Add(new("model_routes", "Model routes", bound.Length == 0 ? "not_configured" : "configured",
            $"{bound.Length} configured · {visible} enabled and visible",
            account.Enabled
                ? "Profile inclusion, applied selection and a successful provider response are separate observations."
                : "This account is disabled. Route configuration remains saved.", "configuration_snapshot"));
        var discovery = Capability(account, "models");
        if (discovery.Declared)
            rows.Add(new("model_discovery", "Model discovery", LocalCapabilityState(discovery, now),
                LocalCapabilityState(discovery, now) switch
                {
                    "configured" => "Configured for model discovery; provider response unverified",
                    "missing" => "Model-discovery credential missing",
                    "expired" => "Model-discovery credential expired",
                    "unsupported" => "Model discovery unsupported by this connection",
                    _ => "Model-discovery access not established"
                },
                "A declared model-discovery capability does not prove a discovery request succeeded.", discovery.Evidence));

        rows.Add(MonitorRow(observations, now));
        rows.Add(status is null
            ? new("gateway", "Gateway", "unknown", "Gateway state unavailable",
                "No service status snapshot was supplied.", "not_observed")
            : status.RuntimeMode == "monitor"
                ? new("gateway", "Gateway", "monitor_only", "Service is in monitoring mode",
                    "Monitoring mode supplies quota views; it does not provide an inference gateway.", "service_status", status.ObservedAt)
                : new("gateway", "Gateway", status.ProxyReady ? "reported_ready" : "not_ready",
                    status.ProxyReady ? "Gateway reported ready" : "Gateway not ready",
                    "This global service observation does not establish account selection or provider access.", "service_status", status.ObservedAt));
        return new(account.Id, account.Name, rows.ToArray(), bound.Length, visible);
    }

    private sealed record CapabilityFact(AccountCapabilityDto[] Values, bool Declared, string Evidence,
        DateTimeOffset? ExpiresAt);
    private static CapabilityFact Capability(AccountDto account, string purpose)
    {
        if (account.Capabilities is { } capabilities)
        {
            var supplied = capabilities.Where(c => c.Purpose == purpose).ToArray();
            return new(supplied, supplied.Length > 0, "account_capability", Expiry(account, supplied));
        }
        // Explicit credential-purpose metadata is useful; credential kind alone is not a capability declaration.
        var handles = account.Credentials?.Where(h => h.Purposes.Contains(purpose, StringComparer.Ordinal)).ToArray();
        if (handles is { Length: > 0 }) return new(handles.Select(h => new AccountCapabilityDto(purpose, "unverified", h.Id)).ToArray(),
            true, "credential_purpose", Expiry(account, handles.Select(h => new AccountCapabilityDto(purpose, "unverified", h.Id)).ToArray()));
        return new([], false, account.Capabilities is null ? "legacy_unknown" : "account_capability", null);
    }

    private static DateTimeOffset? Expiry(AccountDto account, AccountCapabilityDto[] capabilities)
    {
        if (capabilities.Length == 0) return null;
        if (capabilities.All(c => c.CredentialId is null) && account.Credentials is not { Length: > 0 })
            return account.ExpiresAt;
        var ids = capabilities.Select(c => c.CredentialId).ToHashSet(StringComparer.Ordinal);
        var expiries = account.Credentials?.Where(h => ids.Contains(h.Id)).Select(h => h.ExpiresAt).ToArray() ?? [];
        // No inferred expiry for another credential purpose.
        return expiries.Length > 0 && expiries.All(x => x.HasValue) ? expiries.Max() : null;
    }

    private static string LocalCapabilityState(CapabilityFact fact, DateTimeOffset now)
    {
        if (!fact.Declared) return fact.Evidence == "legacy_unknown" ? "unknown" : "not_configured";
        if (fact.ExpiresAt is { } expiry && expiry <= now) return "expired";
        if (fact.Values.Any(c => c.State is "unverified" or "available")) return "configured";
        if (fact.Values.All(c => c.State == "unsupported")) return "unsupported";
        if (fact.Values.Any(c => c.State == "recovery-required")) return "recovery_required";
        if (fact.Values.All(c => c.State == "expired")) return "expired";
        if (fact.Values.All(c => c.State == "missing-credential")) return "missing";
        return "unknown";
    }

    private static AccountOutcomeRow MonitorRow(QuotaDto[] quotas, DateTimeOffset now)
    {
        var readings = quotas.Where(QuotaPresentation.HasReading).ToArray();
        var observed = readings.Where(q => (q.LastSuccessAt ?? q.ObservedAt) is { } at && at <= now).ToArray();
        var current = observed.Count(q => (q.State is "ok" or "fresh") && (q.FreshUntil is null || q.FreshUntil > now));
        var lastKnown = observed.Length - current;
        var latest = observed.Select(q => q.LastSuccessAt ?? q.ObservedAt).Max();
        if (current > 0)
            return new("monitoring", "Quota monitoring", lastKnown > 0 ? "partial" : "fresh",
                lastKnown > 0 ? $"{current} readings reported current · {lastKnown} last known" : $"{current} readings reported current",
                "Provider quota/balance observations apply only to monitoring. Freshness uses supplied status and explicit expiry.", "quota_snapshot", latest);
        if (lastKnown > 0)
            return new("monitoring", "Quota monitoring", "stale", $"{lastKnown} last-known readings",
                "The supplied readings are no longer reported current; current quota availability is unknown.", "quota_snapshot", latest);
        if (readings.Length > 0)
            return new("monitoring", "Quota monitoring", "unknown", "Reading supplied without a valid observation time",
                "Freshness cannot be established from this snapshot.", "quota_snapshot");
        if (quotas.Length > 0 && quotas.All(q => q.State is "unsupported" or "not-supported"))
            return new("monitoring", "Quota monitoring", "unsupported", "Quota lookup is not supported here",
                "This monitoring limitation does not determine inference access.", "quota_snapshot");
        return new("monitoring", "Quota monitoring", "not_observed", "No provider quota reading observed",
            "Review monitoring access separately if a quota reading is needed; no automatic login or inference probe is implied.",
            quotas.Length > 0 ? "quota_snapshot" : "not_observed");
    }
}
