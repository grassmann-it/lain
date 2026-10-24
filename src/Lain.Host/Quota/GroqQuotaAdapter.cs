using System.Net;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Providers;

namespace Lain.Host.Quota;

/// <summary>Reads only the private core's persisted observations of ordinary inference responses.</summary>
public sealed class GroqQuotaAdapter(HttpClient core, TimeProvider? timeProvider = null) : IQuotaAdapter
{
    public const string Documentation = "https://console.groq.com/docs/rate-limits";
    public const string ConsoleUrl = "https://console.groq.com/settings/limits";
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    public string Source => "groq:response-headers";
    public bool RequiresCredential => false;
    public bool Supports(string provider) => provider.Equals("groq", StringComparison.OrdinalIgnoreCase);

    public async Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default)
    {
        // The caller supplies a client authenticated to the private core. Never attach a provider key.
        using var request = new HttpRequestMessage(HttpMethod.Get, "lain/quota/observations");
        using var document = await QuotaSourceHttp.ReadJsonAsync(core, request, ct);
        return Normalize(document.RootElement, account.Id, clock.GetUtcNow());
    }

    public static QuotaFetchResult Normalize(JsonElement root, string accountId, DateTimeOffset now)
    {
        if (JsonFields.Number(root, "version") != 1
            || JsonFields.Object(root, "observations") is not { ValueKind: JsonValueKind.Array } entries
            || entries.GetArrayLength() > 512)
            throw new QuotaFetchException("Quota observation response is invalid.");

        var latest = new Dictionary<string, (DateTimeOffset At, IReadOnlyList<QuotaReading> Rows)>(StringComparer.Ordinal);
        foreach (var entry in entries.EnumerateArray())
        {
            if (JsonFields.String(entry, "accountId") != accountId || JsonFields.String(entry, "provider") != "groq") continue;
            var model = JsonFields.String(entry, "model");
            var observed = JsonFields.Date(entry, "observedAt");
            if (string.IsNullOrWhiteSpace(model) || model.Length > 256 || model.Any(char.IsControl)
                || observed is null || observed > now.AddMinutes(1)) continue;
            if (latest.TryGetValue(model, out var existing) && existing.At >= observed) continue;
            var status = JsonFields.Number(entry, "statusCode");
            if (status is null or < 100 or > 599 || status != Math.Truncate(status.Value)) continue;
            using var response = new HttpResponseMessage((HttpStatusCode)(int)status.Value);
            var headers = JsonFields.Object(entry, "headers");
            foreach (var suffix in new[] { "requests", "tokens" })
            foreach (var kind in new[] { "limit", "remaining", "reset" })
            {
                var name = $"x-ratelimit-{kind}-{suffix}";
                var values = JsonFields.Object(headers, name);
                if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() != 1) continue;
                var item = values[0];
                if (item.ValueKind != JsonValueKind.String || item.GetString() is not { Length: <= 100 } value) continue;
                response.Headers.TryAddWithoutValidation(name, value);
            }
            var parsed = GroqRateLimitHeaders.Parse(response, observed.Value);
            var rows = parsed.Windows.Where(w => w.Limit.HasValue || w.Remaining.HasValue).Select(window =>
            {
                var freshness = observed.Value.AddMinutes(5);
                if (window.ResetsAt < freshness) freshness = window.ResetsAt.Value;
                return new QuotaReading($"groq:response-headers:{model}:{window.Kind}", window.Unit,
                    Limit: window.Limit, Remaining: window.Remaining, ResetsAt: window.ResetsAt,
                    InheritAccountPool: false,
                    WindowLabel: $"{model} · {(window.Window == "day" ? "Requests per day" : "Tokens per minute")}",
                    WindowId: window.Kind, MeasurementKind: "remaining", Scope: "organization-model-observed-via-account",
                    ScopeId: accountId, Model: model, SourceUrl: Documentation, DataAsOf: observed,
                    FreshUntil: freshness, ObservedAt: observed);
            }).ToArray();
            if (rows.Length > 0) latest[model] = (observed.Value, rows);
        }
        var readings = latest.OrderBy(x => x.Key, StringComparer.Ordinal).SelectMany(x => x.Value.Rows).ToArray();
        return new(readings, Support: readings.Length == 0
            ? new("awaiting-observation", "Limits appear after the next ordinary Groq request; refreshing does not send inference.", ConsoleUrl)
            : new("passive", "Observed Groq response headers; organization rate limits may be shared with other keys.", ConsoleUrl));
    }
}
