using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lain.Client;
using Lain.Cli;
using Lain.Core;

internal static class ExactBalanceChecks
{
    internal static async Task<int> RunAsync()
    {
        var checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        var now = DateTimeOffset.UtcNow;
        QuotaDto Balance(string amount, string unit = "USD") => new("existing", null, "fixture#balance", "ok", now, now,
            null, unit, null, null, null, false, WindowLabel: "Account balance", MeasurementKind: "balance", ExactRemaining: amount);
        foreach (var (amount, unit) in new[] { ("110.00", "CNY"), ("0.00000000000000000001", "USD"), ("0.00", "USD"), ("-0.00000000000000000001", "USD") })
        {
            var quota = Balance(amount, unit);
            var output = new StringWriter();
            await CliApplication.WriteQuotas(output, [quota], new(160, false));
            var text = output.ToString();
            Check(text.Contains("Remaining " + amount + " " + unit) && !text.Contains("Quota values not reported")
                && !text.Contains("Remaining unknown") && !text.Contains("unavailable") && !text.Contains("Exact amounts"),
                "exact balance is a primary native amount without duplicate unknown fields: " + amount);
            Check(!text.Contains('%') && !text.Contains('[') && !text.Contains("Reset") && !text.Contains("Used ") && !text.Contains("Limit "),
                "money balance creates no percentage, limit, use or reset");
            foreach (var width in new[] { 44, 160 })
            {
                output = new();
                await CliApplication.WriteAccountUsage(output, new(1, "account-usage",
                    [new(new("existing", "Exact wallet", "fixture", true, "api-key", "ready"), [quota])]), new(width, false));
                text = output.ToString();
                Check(text.Contains(amount) && text.Contains(unit) && !text.Contains("without quota readings")
                    && !text.Contains("No quota readings") && !text.Contains("unknown") && !text.Contains('%') && !text.Contains("Reset"),
                    "compact account overview retains exact-only native money at width " + width);
                Check(text.Contains("Exhausted") == (QuotaPresentation.ExactAmountSign(amount) is <= 0),
                    "exact sign distinguishes a tiny positive amount from known zero and debt");
            }
        }
        var retained = Balance("0.00000000000000000001") with
        {
            State = "unavailable", Remaining = 0,
            Support = new("unavailable", "Synthetic setup-only guidance must not hide the retained amount")
        };
        var staleOutput = new StringWriter();
        await CliApplication.WriteQuotas(staleOutput, [retained], new(160, false));
        Check(staleOutput.ToString().Contains("Last known") && staleOutput.ToString().Contains("Remaining 0.00000000000000000001 USD")
            && !staleOutput.ToString().Contains("setup-only guidance"), "support guidance cannot hide a retained exact balance");
        var compact = new StringWriter();
        await CliApplication.WriteAccountUsage(compact, new(1, "account-usage",
            [new(new("existing", "Retained wallet", "fixture", true, "api-key", "ready"), [retained])]), new(44, false));
        Check(compact.ToString().Contains("Last known") && !compact.ToString().Contains("exhausted", StringComparison.OrdinalIgnoreCase),
            "a rounded parallel double zero does not exhaust an exact positive balance");

        var fields = new[]
        {
            Balance("0") with { MeasurementKind = "usage", ExactRemaining = null, ExactUsed = "1.00000000000000000001" },
            Balance("0") with { MeasurementKind = "limit", ExactRemaining = null, ExactLimit = "2.00000000000000000001" }
        };
        var fieldOutput = new StringWriter();
        await CliApplication.WriteQuotas(fieldOutput, fields, new(160, false));
        Check(fieldOutput.ToString().Contains("Used 1.00000000000000000001 USD") && fieldOutput.ToString().Contains("Limit 2.00000000000000000001 USD")
            && !fieldOutput.ToString().Contains("Quota values not reported"), "exact used and configured limit retain their own fact labels");

        var jsonQuota = Balance("0.00000000000000000001", "CNY");
        var handler = new Handler(jsonQuota);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://fixture.invalid/") };
        var jsonOutput = new StringWriter(); var error = new StringWriter();
        var exit = await CliApplication.RunAsync(["quotas", "--json"], jsonOutput, error, () => new LainApiClient(http));
        using var json = JsonDocument.Parse(jsonOutput.ToString());
        var reading = json.RootElement[0];
        Check(exit == 0 && error.ToString() == "" && handler.Reads == 1
            && reading.GetProperty("exactRemaining").GetString() == jsonQuota.ExactRemaining
            && reading.GetProperty("unit").GetString() == "CNY" && reading.GetProperty("remaining").ValueKind == JsonValueKind.Null
            && reading.GetProperty("used").ValueKind == JsonValueKind.Null && reading.GetProperty("limit").ValueKind == JsonValueKind.Null,
            "JSON retains native exact text and unknown double fields with only the existing read request");
        return checks;
    }

    private sealed class Handler(QuotaDto quota) : HttpMessageHandler
    {
        public int Reads { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method != HttpMethod.Get || request.RequestUri!.AbsolutePath != "/api/v1/quotas")
                throw new Exception("Exact display must not mutate, refresh or call a provider");
            Reads++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { quota }) });
        }
    }
}
