using System.Text.Json;
using Lain.Core;

namespace Lain.Host.Quota;

// Pinned official client: Kilo-Org/kilocode@9723be6 packages/kilo-gateway/src/api/kilo-pass.ts.
// This is a versioned manufacturer client contract, not a public billing API promise.
public sealed class KiloPassReader(HttpClient client)
{
    public const string Endpoint = "https://api.kilo.ai/api/trpc/kiloPass.getState?batch=1&input=%7B%220%22%3Anull%7D";

    public async Task<KiloPassState?> ReadAsync(QuotaCredential credential, CancellationToken ct = default)
    {
        KiloQuotaAdapter.ValidateCredential(credential);
        try
        {
            using var doc = await QuotaHttp.GetAsync(client, Endpoint, credential, ct, userAgent: "lain/1.0");
            var root = SubscriptionRoot(doc.RootElement);
            // Only an explicit absent subscription is a successful empty observation.
            // An unknown envelope/schema must not erase the last successful Pass.
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("subscription", out var subscription)
                && subscription.ValueKind == JsonValueKind.Null) return null;
            return Normalize(doc.RootElement) ?? throw new QuotaFetchException("Kilo Pass response unavailable or invalid.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new QuotaFetchException("Kilo Pass request timed out."); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or ArgumentException or FormatException)
        { throw new QuotaFetchException("Kilo Pass response unavailable or invalid."); }
    }

    private static JsonElement SubscriptionRoot(JsonElement value)
    {
        var item = value.ValueKind == JsonValueKind.Array ? (value.GetArrayLength() == 1 ? value[0] : default) : value;
        var data = JsonFields.Object(JsonFields.Object(item, "result"), "data");
        var root = JsonFields.Object(data, "json");
        return root.ValueKind == JsonValueKind.Object ? root : data.ValueKind == JsonValueKind.Object ? data : item;
    }

    public static KiloPassState? Normalize(JsonElement value)
    {
        var root = SubscriptionRoot(value);
        var sub = JsonFields.Object(root, "subscription");
        if (sub.ValueKind != JsonValueKind.Object) return null;
        var rawStatus = JsonFields.String(sub, "status");
        // An allowlist prevents arbitrary server strings (including echoed credentials)
        // entering status/UI/log fields. Missing and new statuses remain unknown.
        var status = rawStatus is "active" or "past_due" or "trialing" or "canceled" or "unpaid"
            or "incomplete" or "incomplete_expired" or "paused" ? rawStatus : null;
        bool? consumable = status is null ? null : status is "active" or "past_due" or "trialing";
        var basis = JsonFields.Number(sub, "currentPeriodBaseCreditsUsd");
        var usage = JsonFields.Number(sub, "currentPeriodUsageUsd");
        var bonus = JsonFields.Number(sub, "currentPeriodBonusCreditsUsd");
        var billing = JsonFields.Date(sub, "nextBillingAt");
        var renewal = JsonFields.Date(sub, "nextRenewalAt");
        if (status is null && basis is null && usage is null && bonus is null && billing is null && renewal is null) return null;
        // No tier field is qualified in the pinned schema; never infer one from credits.
        return new(status, null, basis, usage, bonus, billing, renewal, consumable);
    }
}
