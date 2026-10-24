using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lain.Core;

namespace Lain.Host.Quota;

/// <summary>Explicitly enabled Enterprise metrics. Rates are never remaining quota or billing usage.</summary>
public sealed class GroqEnterpriseUsageReader(HttpClient http, TimeProvider? timeProvider = null)
{
    public const string Documentation = "https://console.groq.com/docs/prometheus-metrics";
    public const string Endpoint = "https://api.groq.com/v1/metrics/prometheus/api/v1/query";
    public const string RequestsMetric = "model_project_id_status_code:requests:rate5m";
    public const string InputMetric = "model_project_id:tokens_in:rate5m";
    public const string OutputMetric = "model_project_id:tokens_out:rate5m";
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<QuotaFetchResult> FetchAsync(QuotaCredential credential, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var monitor = credential.Monitor;
        if (monitor is null)
            return new([], Support: new("setup-required", "Enterprise metrics require explicit monitor setup for the intended Groq project.", Documentation));
        if (monitor.Kind is not ("inference-key" or "api-token") || !ValidProject(monitor.ResourceId))
            return new([], Support: new("setup-required", "Select a Groq API key and the exact Enterprise project ID.", Documentation));
        if (monitor.ExpiresAt <= clock.GetUtcNow())
            throw new QuotaFetchException("Groq monitor access expired; reconnect monitoring.");
        // An explicitly configured separate monitor key never falls back to another identity.
        var token = monitor.Kind == "inference-key" ? credential.BearerToken : monitor.Secret;
        if (string.IsNullOrWhiteSpace(token))
            return new([], Support: new("setup-required", "The selected Groq monitor API key is unavailable.", Documentation));
        var query = BuildQuery(monitor.ResourceId);
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint + "?query=" + Uri.EscapeDataString(query)
            + "&time=" + clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        request.Headers.Authorization = new("Bearer", token);
        using var document = await QuotaSourceHttp.ReadJsonAsync(http, request, ct);
        return Normalize(document.RootElement, monitor.ResourceId, clock.GetUtcNow());
    }

    public static string BuildQuery(string projectId)
    {
        if (!ValidProject(projectId)) throw new ArgumentException("Invalid Groq project ID.", nameof(projectId));
        var selector = "{__name__=~\"" + RequestsMetric + "|" + InputMetric + "|" + OutputMetric
            + "\",project_id=\"" + projectId + "\"}";
        // Instant-vector result timestamps can be the query evaluation time. Return
        // timestamp(metric) in the SAME expression so we retain the underlying sample
        // time even when repeated polling sees exactly the same old observation.
        var named = "label_replace(" + selector + ",\"lain_metric\",\"$1\",\"__name__\",\"(.*)\")";
        return "label_replace(" + named + ",\"lain_sample_kind\",\"value\",\"lain_metric\",\".*\") or "
            + "label_replace(timestamp(" + named + "),\"lain_sample_kind\",\"timestamp\",\"lain_metric\",\".*\")";
    }

    public static QuotaFetchResult Normalize(JsonElement root, string projectId, DateTimeOffset now)
    {
        if (!ValidProject(projectId)) throw new QuotaFetchException("Groq metrics project ID is invalid.");
        var data = JsonFields.Object(root, "data");
        var result = JsonFields.Object(data, "result");
        if (JsonFields.String(root, "status") != "success" || JsonFields.String(data, "resultType") != "vector"
            || result.ValueKind != JsonValueKind.Array || result.GetArrayLength() > 1024)
            throw new QuotaFetchException("Groq metrics response is invalid.");
        var samples = new Dictionary<(string Metric, string Model, string? Status), Sample>();
        var invalid = false;
        foreach (var entry in result.EnumerateArray())
        {
            var labels = JsonFields.Object(entry, "metric");
            var metric = JsonFields.String(labels, "lain_metric");
            var model = JsonFields.String(labels, "model");
            var status = JsonFields.String(labels, "status_code");
            var lane = JsonFields.String(labels, "lain_sample_kind");
            var value = JsonFields.Object(entry, "value");
            if (metric is not (RequestsMetric or InputMetric or OutputMetric) || !ValidText(model)
                || JsonFields.String(labels, "project_id") != projectId || lane is not ("value" or "timestamp")
                || labels.EnumerateObject().Any(p => p.Name is not ("__name__" or "lain_metric" or "lain_sample_kind" or "model" or "project_id" or "status_code"))
                || JsonFields.String(labels, "__name__") is { } original && original != metric
                || metric == RequestsMetric && (status is null || !Regex.IsMatch(status, "\\A[1-5][0-9]{2}\\z", RegexOptions.CultureInvariant))
                || metric != RequestsMetric && status is not null
                || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 2 || !TryNumber(value[1], out var quantity))
            { invalid = true; continue; }
            var key = (metric!, model!, status);
            if (!samples.TryGetValue(key, out var sample)) samples.Add(key, sample = new());
            // Duplicate series or unsupported dimensions must never collapse into a
            // falsely exact model total. Only the documented dimensions are accepted.
            if (lane == "value")
            {
                if (sample.Value.HasValue) sample.Invalid = true;
                else sample.Value = quantity;
            }
            else
            {
                if (sample.Timestamp.HasValue) sample.Invalid = true;
                else sample.Timestamp = quantity;
            }
        }
        var rows = new List<QuotaReading>();
        foreach (var (key, sample) in samples.OrderBy(p => p.Key.Metric, StringComparer.Ordinal)
                     .ThenBy(p => p.Key.Model, StringComparer.Ordinal).ThenBy(p => p.Key.Status, StringComparer.Ordinal))
        {
            if (sample.Invalid || sample.Value is null || sample.Timestamp is null
                || !TryTimestamp(sample.Timestamp.Value, out var at) || at > now.AddMinutes(1))
            { invalid = true; continue; }
            var kind = key.Metric switch { RequestsMetric => "Request rate", InputMetric => "Input token rate", _ => "Output token rate" };
            var window = key.Metric + (key.Status is null ? "" : ":" + key.Status);
            rows.Add(new("groq:enterprise-prometheus:" + key.Model + ":" + window,
                key.Metric == RequestsMetric ? "requests/second" : "tokens/second", Used: sample.Value,
                InheritAccountPool: false,
                WindowLabel: key.Model + " · " + kind + (key.Status is null ? "" : " · HTTP " + key.Status) + " · 5-minute average",
                WindowId: window, MeasurementKind: "rate", Scope: "project-model", ScopeId: projectId,
                Model: key.Model, SourceUrl: Documentation, ObservedAt: at, DataAsOf: at,
                FreshUntil: at.AddMinutes(5), ProviderUpdatedAt: at));
        }
        var warning = JsonFields.Object(root, "warnings");
        var error = invalid || warning.ValueKind == JsonValueKind.Array && warning.GetArrayLength() > 0
            ? "Some Groq Enterprise metrics are unavailable; previous observations retained." : null;
        return new(rows, error, new(rows.Count == 0 ? "unavailable" : "api",
            "Groq Enterprise project metrics: observed 5-minute average rates, without remaining quota, spend or credit balance."
            + (rows.Count == 0 ? " No matching samples were reported; this does not mean zero usage." : ""), Documentation));
    }

    public static bool ValidProject(string? value) => value is not null
        && Regex.IsMatch(value, "\\A[a-zA-Z0-9][a-zA-Z0-9_-]{0,127}\\z", RegexOptions.CultureInvariant);
    private static bool ValidText(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && !value.Any(char.IsControl);
    private static bool TryNumber(JsonElement value, out double number)
    {
        number = 0;
        return (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out number)
            || value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            && double.IsFinite(number) && number >= 0;
    }
    private static bool TryTimestamp(double seconds, out DateTimeOffset at)
    {
        at = default;
        try
        {
            if (seconds <= 0 || seconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds()) return false;
            at = DateTimeOffset.FromUnixTimeMilliseconds(checked((long)(seconds * 1000)));
            return true;
        }
        catch (Exception error) when (error is ArgumentOutOfRangeException or OverflowException) { return false; }
    }
    private sealed class Sample
    {
        public double? Value { get; set; }
        public double? Timestamp { get; set; }
        public bool Invalid { get; set; }
    }
}
