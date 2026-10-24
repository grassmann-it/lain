using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts.Cloud;

namespace Lain.Host.Quota;

/// <summary>Direct project/model telemetry and effective limits, deliberately not an unqualified remaining-quota join.</summary>
public sealed class GeminiModelQuotaAdapter : IQuotaAdapter, IDisposable
{
    public const string Documentation = "https://docs.cloud.google.com/monitoring/api/metrics_gcp_d_h";
    public const string QuotaDocumentation = "https://docs.cloud.google.com/docs/quotas/reference/rest/v1/projects.locations.services.quotaInfos/list";
    public static readonly string[] Families = ["generate_content_free_tier_requests", "generate_content_free_tier_input_token_count", "generate_requests_per_model", "generate_content_paid_tier_input_token_count"];
    private readonly CloudProviderHttp http;
    private readonly TimeProvider clock;
    public GeminiModelQuotaAdapter(HttpMessageHandler? handler = null, TimeProvider? clock = null) { http = new(handler); this.clock = clock ?? TimeProvider.System; }
    public string Source => "gemini:cloud-monitoring:model-usage";
    public bool Supports(string provider) => provider == CloudResourceDiscovery.GoogleProvider;

    public async Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default)
    {
        try { return await FetchCoreAsync(account, credential, ct); }
        catch (CloudProviderException error) { throw new QuotaFetchException(error.Message); }
    }
    private async Task<QuotaFetchResult> FetchCoreAsync(AccountDto account, QuotaCredential credential, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(60)); ct = deadline.Token;
        var now = clock.GetUtcNow();
        var project = credential.Settings.GetValueOrDefault("projectId") ?? credential.Monitor?.ResourceId;
        var number = credential.Settings.GetValueOrDefault("projectNumber");
        var access = credential.Monitor?.Secret;
        if (!CloudResourceDiscovery.ProjectId(project) || string.IsNullOrWhiteSpace(access) || credential.Monitor?.ExpiresAt is null || credential.Monitor.ExpiresAt <= now
            || account.Binding?.ResourceId is { } pinned && pinned != project || number is not null && !CloudResourceDiscovery.ProjectNumber(number)
            || account.Binding?.Subject is { } pinnedNumber && number is not null && pinnedNumber != number)
            return new([], Support: new("setup-required", "Connect Google monitoring to the existing Gemini project with a valid access grant and verified project identity.", GeminiMonitoringQuotaAdapter.ConsoleUrl, "quota"));
        var start = PacificDayStart(now);
        var readings = new List<QuotaReading>(); var diagnostics = new List<string>();
        foreach (var family in Families)
        {
            var metric = "generativelanguage.googleapis.com/quota/" + family + "/usage";
            var filter = "metric.type = \"" + metric + "\" AND resource.type = \"generativelanguage.googleapis.com/Location\"";
            var endpoint = $"https://monitoring.googleapis.com/v3/projects/{Uri.EscapeDataString(project!)}/timeSeries?filter={Uri.EscapeDataString(filter)}"
                + $"&interval.startTime={Uri.EscapeDataString(start.ToString("O", CultureInfo.InvariantCulture))}&interval.endTime={Uri.EscapeDataString(now.ToString("O", CultureInfo.InvariantCulture))}&view=FULL&pageSize=1000";
            var pages = new List<JsonDocument>();
            try
            {
                await ReadPages(endpoint, "nextPageToken", access!, pages, ct);
                var result = NormalizeUsage(pages.Select(p => p.RootElement), project!, number, family, start, now);
                readings.AddRange(result.Readings);
                if (result.Error is not null) diagnostics.Add(result.Error);
            }
            catch (CloudProviderException error) when (error.Status is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            { diagnostics.Add("Model usage is unavailable for one or more metric families. Check monitoring.timeSeries.list and project metric availability."); }
            finally { foreach (var page in pages) page.Dispose(); }
        }
        if (number is not null)
        {
            var pages = new List<JsonDocument>();
            try
            {
                await ReadPages($"https://cloudquotas.googleapis.com/v1/projects/{number}/locations/global/services/generativelanguage.googleapis.com/quotaInfos?pageSize=100", "nextPageToken", access!, pages, ct);
                readings.AddRange(NormalizeLimits(pages.Select(p => p.RootElement), project!, number, now));
            }
            catch (CloudProviderException error) when (error.Status is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            { diagnostics.Add("Effective limits are unavailable. Check cloudquotas.quotas.get and the cloud-platform OAuth grant; existing usage remains usable."); }
            finally { foreach (var page in pages) page.Dispose(); }
        }
        else diagnostics.Add("Project number is not verified; effective quota limits were not queried.");
        return new(readings, diagnostics.Count == 0 ? null : string.Join(' ', diagnostics.Distinct()),
            Support: new(readings.Count == 0 ? "unavailable" : "api", "Reported project/model usage may be delayed by 150 seconds and incomplete. Effective limits are separate; model/limit/window joins and remaining capacity require project qualification. Empty series do not mean zero usage.", GeminiMonitoringQuotaAdapter.ConsoleUrl));
    }
    private async Task ReadPages(string endpoint, string tokenField, string access, List<JsonDocument> pages, CancellationToken ct)
    {
        string? token = null; var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var page = 0; page < 16; page++)
        {
            using var request = CloudProviderHttp.Get(endpoint + (token is null ? "" : "&pageToken=" + Uri.EscapeDataString(token)), access);
            var document = await http.ReadAsync(request, ct); pages.Add(document);
            token = JsonFields.String(document.RootElement, tokenField);
            if (string.IsNullOrEmpty(token)) return;
            if (token.Length > 4096 || !seen.Add(token)) throw new QuotaFetchException("Google reporting pagination is invalid.");
        }
        throw new QuotaFetchException("Google reporting exceeds the page bound.");
    }

    public static DateTimeOffset PacificDayStart(DateTimeOffset now)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        var date = TimeZoneInfo.ConvertTime(now, zone).Date;
        return new DateTimeOffset(date, zone.GetUtcOffset(date)).ToUniversalTime();
    }
    public static QuotaFetchResult NormalizeUsage(IEnumerable<JsonElement> pages, string project, string? number, string family, DateTimeOffset from, DateTimeOffset observed)
    {
        if (!Families.Contains(family, StringComparer.Ordinal) || !CloudResourceDiscovery.ProjectId(project) || number is not null && !CloudResourceDiscovery.ProjectNumber(number) || from >= observed)
            throw new ArgumentException("Invalid Google model usage selection.");
        var metricType = "generativelanguage.googleapis.com/quota/" + family + "/usage";
        var series = new Dictionary<string, Series>(StringComparer.Ordinal); var malformed = false; var count = 0;
        foreach (var page in pages)
        {
            var rows = JsonFields.Object(page, "timeSeries");
            if (rows.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
            if (rows.ValueKind != JsonValueKind.Array) throw new QuotaFetchException("Invalid model time series.");
            foreach (var row in rows.EnumerateArray())
            {
                var metric = JsonFields.Object(row, "metric"); var labels = JsonFields.Object(metric, "labels");
                var resource = JsonFields.Object(row, "resource"); var resourceLabels = JsonFields.Object(resource, "labels");
                var container = JsonFields.String(resourceLabels, "resource_container");
                var model = JsonFields.String(labels, "model"); var limit = JsonFields.String(labels, "limit_name");
                var method = JsonFields.String(labels, "method"); var location = JsonFields.String(resourceLabels, "location");
                if (JsonFields.String(metric, "type") != metricType || JsonFields.String(resource, "type") != "generativelanguage.googleapis.com/Location"
                    || (container != "projects/" + project && (number is null || container != "projects/" + number))
                    || JsonFields.String(row, "metricKind") != "DELTA" || JsonFields.String(row, "valueType") != "INT64"
                    || !Label(model) || !Label(limit) || !Label(method) || !Label(location)) { malformed = true; continue; }
                var identity = Canonical(labels) + "|" + Canonical(resourceLabels);
                if (!series.TryGetValue(identity, out var item)) series.Add(identity, item = new(model!, limit!, method!, location!));
                var points = JsonFields.Object(row, "points");
                if (points.ValueKind != JsonValueKind.Array) { item.Invalid = true; malformed = true; continue; }
                foreach (var point in points.EnumerateArray())
                {
                    if (++count > 16000) throw new QuotaFetchException("Model usage exceeds the sample bound.");
                    var interval = JsonFields.Object(point, "interval"); var start = JsonFields.Date(interval, "startTime"); var end = JsonFields.Date(interval, "endTime");
                    var quantity = JsonFields.Object(JsonFields.Object(point, "value"), "int64Value");
                    var raw = quantity.ValueKind == JsonValueKind.String ? quantity.GetString() : quantity.ValueKind == JsonValueKind.Number ? quantity.GetRawText() : null;
                    if (start is null || end is null || start < from || end > observed || start >= end || !long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0 || value > 9007199254740991L)
                    { item.Invalid = true; malformed = true; continue; }
                    var key = (start.Value, end.Value);
                    if (item.Points.TryGetValue(key, out var prior)) { if (prior != value) { item.Invalid = true; malformed = true; } continue; }
                    item.Points.Add(key, value);
                }
            }
        }
        var readings = new List<QuotaReading>();
        foreach (var (identity, item) in series)
        {
            if (item.Invalid || item.Points.Count == 0) continue;
            var ordered = item.Points.OrderBy(p => p.Key.Start).ToArray();
            if (ordered.Skip(1).Where((p, i) => p.Key.Start < ordered[i].Key.End).Any()) { malformed = true; continue; }
            var used = ordered.Sum(p => (decimal)p.Value);
            if (used > 9007199254740991L) throw new QuotaFetchException("Model usage is outside the supported range.");
            readings.Add(new("gemini:cloud-monitoring:model-usage", family.Contains("token_count", StringComparison.Ordinal) ? "input-tokens" : "requests", Used: (double)used,
                BudgetPoolId: "google-project:" + project, InheritAccountPool: false,
                WindowLabel: "Reported usage · Pacific day · " + item.Limit + " · " + item.Method + " · " + item.Location + " · delayed/incomplete",
                WindowId: family + ":" + Hash(identity), MeasurementKind: "usage", Scope: "project-model-limit", ScopeId: project, Model: item.Model,
                SourceUrl: Documentation, ObservedAt: observed, DataAsOf: ordered.Max(p => p.Key.End), WindowStart: from, WindowEnd: observed));
        }
        return new(readings, malformed ? "Invalid, overlapping or mismatched model series were excluded; reported usage is partial." : null);
    }
    private sealed class Series(string model, string limit, string method, string location)
    {
        public string Model = model, Limit = limit, Method = method, Location = location;
        public bool Invalid;
        public Dictionary<(DateTimeOffset Start, DateTimeOffset End), long> Points = new();
    }
    public static QuotaReading[] NormalizeLimits(IEnumerable<JsonElement> pages, string project, string number, DateTimeOffset observed)
    {
        if (!CloudResourceDiscovery.ProjectId(project) || !CloudResourceDiscovery.ProjectNumber(number)) throw new ArgumentException("Invalid quota project binding.");
        var result = new Dictionary<string, QuotaReading>(StringComparer.Ordinal);
        foreach (var page in pages)
        {
            var rows = JsonFields.Object(page, "quotaInfos");
            if (rows.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) continue;
            if (rows.ValueKind != JsonValueKind.Array) throw new QuotaFetchException("Invalid quota configuration response.");
            foreach (var row in rows.EnumerateArray())
            {
                var quotaId = JsonFields.String(row, "quotaId"); var name = JsonFields.String(row, "name"); var metric = JsonFields.String(row, "metric");
                if (!Label(quotaId) || name != $"projects/{number}/locations/global/services/generativelanguage.googleapis.com/quotaInfos/{quotaId}"
                    || JsonFields.String(row, "service") != "generativelanguage.googleapis.com" || !Label(metric))
                    throw new QuotaFetchException("Quota configuration resource does not match the selected project/service.");
                var dimensions = JsonFields.Object(row, "dimensionsInfos");
                if (dimensions.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) continue;
                if (dimensions.ValueKind != JsonValueKind.Array) throw new QuotaFetchException("Invalid quota dimensions.");
                foreach (var dimension in dimensions.EnumerateArray())
                {
                    var labels = JsonFields.Object(dimension, "dimensions"); var value = JsonFields.Number(JsonFields.Object(dimension, "details"), "value");
                    if (labels.ValueKind != JsonValueKind.Object || value is null || value > 9007199254740991d) continue;
                    if (labels.EnumerateObject().Count() > 16 || labels.EnumerateObject().Any(p => !Label(p.Name) || p.Value.ValueKind != JsonValueKind.String || !Label(p.Value.GetString())))
                        throw new QuotaFetchException("Invalid quota dimension identity.");
                    var key = quotaId + ":" + Hash(Canonical(labels));
                    var dimensionLabel = string.Join(", ", labels.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => p.Name + "=" + p.Value.GetString()));
                    var unit = JsonFields.String(row, "metricUnit"); var interval = JsonFields.String(row, "refreshInterval");
                    var reading = new QuotaReading("gemini:cloud-quotas:limits", Label(unit) ? unit! : "unknown", Limit: value,
                        BudgetPoolId: "google-project:" + project, InheritAccountPool: false, WindowLabel: "Effective limit · " + quotaId + " · " + (Label(interval) ? interval : "window unknown") + " · " + dimensionLabel,
                        WindowId: key, MeasurementKind: "limit", Scope: "project-quota-dimensions", ScopeId: project, Model: JsonFields.String(labels, "model"), SourceUrl: QuotaDocumentation, ObservedAt: observed);
                    if (result.TryGetValue(key, out var previous) && previous != reading) throw new QuotaFetchException("Conflicting effective quota values.");
                    result[key] = reading;
                    if (result.Count > 1000) throw new QuotaFetchException("Quota configuration exceeds the dimension bound.");
                }
            }
        }
        return result.Values.ToArray();
    }
    private static bool Label(string? value) => value is { Length: > 0 and <= 256 } && !value.Any(char.IsControl);
    private static string Canonical(JsonElement labels) => labels.ValueKind == JsonValueKind.Object
        ? JsonSerializer.Serialize(labels.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => new[] { p.Name, p.Value.ToString() })) : "{}";
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public void Dispose() => http.Dispose();
}
