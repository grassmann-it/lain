using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts.CliConnections;

namespace Lain.Host.Quota;

public sealed record AlibabaTrialGrant(string Model, string? Status, double? OriginalAmount, double? RemainingAmount,
    DateTimeOffset? ExpiresAt, bool IsAvailable);
public sealed record AlibabaTrialProtection(string Model, bool? FreeTierOnly);
public sealed record AlibabaConsoleObservation(IReadOnlyList<QuotaReading> Readings, IReadOnlyList<AlibabaTrialGrant> Grants,
    IReadOnlyList<AlibabaTrialProtection> Protection, string? Error = null);

/// <summary>Ordinary API trial/Online workspace usage through a private, fixed-operation CLI owner.</summary>
public sealed class AlibabaConsoleQuotaAdapter(IAlibabaConsoleReadOwner owner, IReadOnlyDictionary<string, string>? trialUnits = null,
    IReadOnlySet<string>? qualifiedActiveGrantStatuses = null, TimeProvider? clock = null) : IQuotaAdapter
{
    public const string SourceId = "official-cli:alibaba-model-studio:console";
    public string Source => SourceId;
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    public bool Supports(string provider) => provider.Equals("alibaba-model-studio", StringComparison.OrdinalIgnoreCase);
    public async Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default)
    {
        var details = await ReadDetailsAsync(account, credential, ct);
        return new(details.Readings, details.Error);
    }
    public async Task<AlibabaConsoleObservation> ReadDetailsAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default)
    {
        if (!Supports(account.Provider) || account.Binding is not { Provider: "alibaba-model-studio", Product: "ordinary-api", ResourceType: "workspace", ResourceId: { } workspace, Region: { } region }
            || region is not ("cn-beijing" or "ap-southeast-1") || credential.Monitor is not { Kind: "alibaba-cli-profile", Secret: null, ResourceId: { } profileId } monitor
            || string.IsNullOrWhiteSpace(credential.BindingVersion) || !credential.Settings.TryGetValue("consoleSite", out var site)
            || site is not ("international" or "domestic")) throw new QuotaFetchException("Alibaba requires a current owned ordinary-API Console profile bound to its site, region and workspace.");
        if (credential.Monitor.ExpiresAt is { } expiry && expiry <= time.GetUtcNow())
            throw new QuotaFetchException("Alibaba Console session expired; renew the owned usage connection.");
        var scope = new AlibabaConsoleScope(account.Id, profileId, credential.BindingVersion, site, region, workspace);
        var end = time.GetUtcNow(); var start = end.AddDays(-7);
        var rows = new List<QuotaReading>(); var grants = new List<AlibabaTrialGrant>(); var protection = new List<AlibabaTrialProtection>(); var errors = new List<string>();
        var models = trialUnits?.Keys.ToArray() ?? [];
        // Trial is qualified only in International/Singapore; Beijing telemetry is separate.
        if (site == "international" && region == "ap-southeast-1" && models.Length > 0)
        {
            try
            {
                using var raw = await owner.ReadTrialAsync(scope, models, ct);
                grants.AddRange(NormalizeTrialGrants(raw.RootElement, end, qualifiedActiveGrantStatuses));
                foreach (var grant in grants.Where(g => g.IsAvailable))
                {
                    if (trialUnits!.TryGetValue(grant.Model, out var unit) && Unit(unit) is { } validUnit)
                        rows.Add(new(SourceId + "#trial/" + grant.Model, validUnit, Limit: grant.OriginalAmount, Remaining: grant.RemainingAmount,
                            InheritAccountPool: false, WindowLabel: "One-time model trial grant", WindowId: "trial/" + grant.Model,
                            MeasurementKind: "grant", Scope: "account_model", ScopeId: account.Id, Model: grant.Model, WindowEnd: grant.ExpiresAt));
                }
            }
            catch (Exception error) when (Known(error, ct)) { errors.Add("Alibaba model trial read unavailable; remaining quota unknown."); }
            try { using var raw = await owner.ReadTrialProtectionAsync(scope, models, ct); protection.AddRange(NormalizeProtection(raw.RootElement)); }
            catch (Exception error) when (Known(error, ct)) { errors.Add("Alibaba trial protection status unavailable."); }
        }
        try
        {
            using var raw = await owner.ReadUsageAsync(scope, start, end, ct);
            rows.AddRange(NormalizeUsage(raw.RootElement, workspace, start, end));
        }
        catch (Exception error) when (Known(error, ct)) { errors.Add("Alibaba Online workspace usage unavailable; renew the owned session if access expired."); }
        if (rows.Count == 0 && errors.Count == 0) errors.Add("No qualified Alibaba quota/usage values; remaining budget unknown.");
        return new(rows, grants, protection, errors.Count > 0 ? string.Join(" ", errors) : null);
    }
    private static bool Known(Exception error, CancellationToken ct) => error is CliConnectionException or IOException or InvalidDataException or JsonException or ArgumentException
        || error is OperationCanceledException && !ct.IsCancellationRequested;
    public static IReadOnlyList<AlibabaTrialGrant> NormalizeTrialGrants(JsonElement root, DateTimeOffset observedAt, IReadOnlySet<string>? activeStatuses = null)
    {
        if (!Successful(root)) return [];
        var values = JsonFields.Object(Unwrap(root), "freeTierQuotas"); if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > 1000) return [];
        var result = new List<AlibabaTrialGrant>();
        foreach (var value in values.EnumerateArray())
        {
            var model = AlibabaOwnedProfileReader.Identifier(JsonFields.String(value, "model")); if (model is null) continue;
            var rawStatus = JsonFields.String(value, "quotaStatus");
            var status = rawStatus is { Length: > 0 and <= 64 } && rawStatus.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') ? rawStatus : null;
            var original = JsonFields.Number(value, "quotaInitTotal"); var remaining = JsonFields.Number(value, "quotaTotal");
            var expiry = EpochMilliseconds(value, "quotaValidityPeriod");
            var available = status is not null && activeStatuses?.Contains(status) == true && expiry > observedAt
                && original.HasValue && remaining.HasValue && remaining <= original;
            result.Add(new(model, status, original, remaining, expiry, available));
        }
        return result;
    }
    public static IReadOnlyList<AlibabaTrialProtection> NormalizeProtection(JsonElement root)
    {
        if (!Successful(root)) return [];
        var values = JsonFields.Object(Unwrap(root), "freeTierOnlyStatuses"); if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > 1000) return [];
        return values.EnumerateArray().Where(v => AlibabaOwnedProfileReader.Identifier(JsonFields.String(v, "model")) is not null)
            .Select(v => new AlibabaTrialProtection(JsonFields.String(v, "model")!, JsonFields.Object(v, "freeTierOnly").ValueKind switch
            { JsonValueKind.True => true, JsonValueKind.False => false, _ => (bool?)null })).ToArray();
    }
    public static IReadOnlyList<QuotaReading> NormalizeUsage(JsonElement root, string workspace, DateTimeOffset start, DateTimeOffset end)
    {
        if (!Successful(root) || start >= end) return [];
        var data = Unwrap(root);
        if (JsonFields.String(data, "workspaceId") is { } returnedWorkspace && returnedWorkspace != workspace) return [];
        var usages = JsonFields.Object(data, "usages");
        if (usages.ValueKind != JsonValueKind.Array || usages.GetArrayLength() > 1000) return [];
        var rows = new List<QuotaReading>(); var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in usages.EnumerateArray())
        {
            var key = AlibabaOwnedProfileReader.Identifier(JsonFields.String(entry, "key"));
            var unit = Unit(JsonFields.String(entry, "unit")); var amount = JsonFields.Number(entry, "value");
            if (key is null || unit is null || amount is null || !identities.Add(key)) continue;
            rows.Add(new(SourceId + "#online/" + key, unit, Used: amount, InheritAccountPool: false,
                WindowLabel: "Online workspace usage · reported unit", WindowId: "online/" + key, MeasurementKind: "usage",
                Scope: "workspace", ScopeId: workspace, WindowStart: start, WindowEnd: end));
        }
        return rows;
    }
    private static string? Unit(string? value) => value is "tokens" or "images" or "sec" or "chars" or "requests" ? value : null;
    private static DateTimeOffset? EpochMilliseconds(JsonElement root, string key)
    {
        var value = JsonFields.Object(root, key);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var ms) || ms <= 0) return null;
        try { return DateTimeOffset.FromUnixTimeMilliseconds(ms); } catch (ArgumentOutOfRangeException) { return null; }
    }
    private static bool Successful(JsonElement root)
    {
        var data = JsonFields.Object(root, "data"); var v2 = JsonFields.Object(data, "DataV2");
        foreach (var current in new[] { root, data, v2, JsonFields.Object(data, "data"), JsonFields.Object(v2, "data"), Unwrap(root) })
            if (JsonFields.Object(current, "success").ValueKind == JsonValueKind.False || JsonFields.Object(current, "errorCode").ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null)) return false;
        return true;
    }
    public static JsonElement Unwrap(JsonElement root)
    {
        var data = JsonFields.Object(root, "data"); if (data.ValueKind != JsonValueKind.Object) return root;
        var v2 = JsonFields.Object(data, "DataV2");
        if (v2.ValueKind == JsonValueKind.Object)
        { var inner = JsonFields.Object(v2, "data"); var payload = JsonFields.Object(inner, "data"); return payload.ValueKind == JsonValueKind.Object ? payload : inner.ValueKind == JsonValueKind.Object ? inner : v2; }
        var direct = JsonFields.Object(data, "data"); return direct.ValueKind == JsonValueKind.Object ? direct : data;
    }
}
