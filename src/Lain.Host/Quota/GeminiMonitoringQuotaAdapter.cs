using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lain.Core;

namespace Lain.Host.Quota;

/// <summary>Optional Google Cloud Monitoring usage, not AI Studio remaining model quota.</summary>
public sealed class GeminiMonitoringQuotaAdapter(HttpClient http, TimeProvider? timeProvider = null) : IQuotaAdapter
{
    public const string Metric = "serviceruntime.googleapis.com/api/request_count";
    public const string Service = "generativelanguage.googleapis.com";
    public const string Documentation = "https://docs.cloud.google.com/monitoring/api/metrics_gcp_p_z#serviceruntime";
    public const string ConsoleUrl = "https://aistudio.google.com/usage";
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    public string Source => "gemini:cloud-monitoring:api-requests";
    public bool Supports(string provider) => provider.Equals("gemini-api", StringComparison.OrdinalIgnoreCase);

    public async Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default)
    {
        var monitor = credential.Monitor;
        var now = clock.GetUtcNow();
        if (monitor?.Kind != "access-token-snapshot" || string.IsNullOrWhiteSpace(monitor.Secret)
            || !ValidProject(monitor.ResourceId) || monitor.ExpiresAt is null || monitor.ExpiresAt <= now)
            return new([], Support: new("setup-required",
                "Add a Google Monitoring access-token snapshot, its expiry and the Gemini project's ID. The inference key cannot read Monitoring.",
                "https://docs.cloud.google.com/monitoring/api/v3/authorization", "monitor"));

        var project = monitor.ResourceId!;
        var start = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var filter = $"metric.type = \"{Metric}\" AND resource.type = \"consumed_api\" AND resource.labels.service = \"{Service}\" AND resource.labels.project_id = \"{project}\"";
        var baseUrl = $"https://monitoring.googleapis.com/v3/projects/{Uri.EscapeDataString(project)}/timeSeries"
            + $"?filter={Uri.EscapeDataString(filter)}&interval.startTime={Uri.EscapeDataString(start.ToString("O", CultureInfo.InvariantCulture))}"
            + $"&interval.endTime={Uri.EscapeDataString(now.ToString("O", CultureInfo.InvariantCulture))}&view=FULL&pageSize=1000";
        var pages = new List<JsonDocument>();
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        string? token = null;
        try
        {
            for (var page = 0; page < 16; page++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + (token is null ? "" : "&pageToken=" + Uri.EscapeDataString(token)));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", monitor.Secret);
                if (credential.Settings.TryGetValue("quotaProjectId", out var quotaProject))
                {
                    if (quotaProject != project) throw new QuotaFetchException("Google quota project does not match the monitor resource.");
                    request.Headers.Add("X-Goog-User-Project", quotaProject);
                }
                var document = await QuotaSourceHttp.ReadJsonAsync(http, request, ct);
                pages.Add(document);
                token = JsonFields.String(document.RootElement, "nextPageToken");
                if (string.IsNullOrEmpty(token))
                    return Normalize(pages.Select(p => p.RootElement), project, start, now);
                if (token.Length > 4096 || !tokens.Add(token)) throw new QuotaFetchException("Monitoring pagination is invalid.");
            }
            throw new QuotaFetchException("Monitoring result exceeds the bounded collection size.");
        }
        finally { foreach (var document in pages) document.Dispose(); }
    }

    public static bool ValidProject(string? project) => project is not null && Regex.IsMatch(project,
        "^[a-z][a-z0-9-]{4,28}[a-z0-9]$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static QuotaFetchResult Normalize(IEnumerable<JsonElement> pages, string project,
        DateTimeOffset start, DateTimeOffset observedAt)
    {
        var points = new HashSet<string>(StringComparer.Ordinal);
        double used = 0;
        DateTimeOffset? dataAsOf = null;
        var malformed = false;
        var count = 0;
        foreach (var page in pages)
        {
            var series = JsonFields.Object(page, "timeSeries");
            if (series.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) continue;
            if (series.ValueKind != JsonValueKind.Array) throw new QuotaFetchException("Monitoring time series are invalid.");
            foreach (var item in series.EnumerateArray())
            {
                var metric = JsonFields.Object(item, "metric");
                var resource = JsonFields.Object(item, "resource");
                var labels = JsonFields.Object(resource, "labels");
                if (JsonFields.String(metric, "type") != Metric || JsonFields.String(resource, "type") != "consumed_api"
                    || JsonFields.String(labels, "project_id") != project || JsonFields.String(labels, "service") != Service
                    || JsonFields.String(item, "metricKind") != "DELTA" || JsonFields.String(item, "valueType") != "INT64")
                { malformed = true; continue; }
                var identity = CanonicalLabels(JsonFields.Object(metric, "labels")) + "|" + CanonicalLabels(labels);
                var values = JsonFields.Object(item, "points");
                if (values.ValueKind != JsonValueKind.Array) { malformed = true; continue; }
                foreach (var point in values.EnumerateArray())
                {
                    if (++count > 16000) throw new QuotaFetchException("Monitoring result exceeds the bounded collection size.");
                    var interval = JsonFields.Object(point, "interval");
                    var from = JsonFields.Date(interval, "startTime");
                    var to = JsonFields.Date(interval, "endTime");
                    var value = JsonFields.Object(JsonFields.Object(point, "value"), "int64Value");
                    var raw = value.ValueKind == JsonValueKind.String ? value.GetString()
                        : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;
                    if (from is null || to is null || from < start || to > observedAt || from >= to
                        || !long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity) || quantity < 0)
                    { malformed = true; continue; }
                    var key = identity + "|" + from.Value.ToString("O", CultureInfo.InvariantCulture) + "|" + to.Value.ToString("O", CultureInfo.InvariantCulture);
                    if (!points.Add(key)) continue;
                    used += quantity;
                    if (!double.IsFinite(used) || used > 9007199254740991d) throw new QuotaFetchException("Monitoring quantity is outside the supported range.");
                    if (dataAsOf is null || to > dataAsOf) dataAsOf = to;
                }
            }
        }
        if (points.Count == 0)
            return new([], malformed ? "Monitoring returned no valid project-scoped points." : null,
                Support: new("unavailable", "No Gemini API request series was returned for this project and UTC day. This does not prove zero usage or remaining quota.", ConsoleUrl));
        var reading = new QuotaReading("gemini:cloud-monitoring:api-requests", "requests", Used: used,
            BudgetPoolId: $"google-project:{project}", InheritAccountPool: false,
            WindowLabel: "API requests · UTC day · up to 30 min delayed", WindowId: "utc-day-api-requests",
            MeasurementKind: "usage", Scope: "project-service", ScopeId: project, SourceUrl: Documentation,
            DataAsOf: dataAsOf, WindowStart: start, WindowEnd: observedAt, FreshUntil: observedAt.AddMinutes(5), ObservedAt: observedAt);
        return new([reading], malformed ? "Some Monitoring points were invalid or outside the requested project/window; usage is partial." : null,
            Support: new("api", "Project API request usage, including other keys and API methods; no model limit or remaining quota. Samples may appear up to 30 minutes late.", ConsoleUrl));
    }

    private static string CanonicalLabels(JsonElement labels) => labels.ValueKind != JsonValueKind.Object ? "{}"
        : JsonSerializer.Serialize(labels.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => new[] { p.Name, p.Value.ToString() }).ToArray());
}
