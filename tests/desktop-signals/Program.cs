using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Lain.Client;
using Lain.Core;
using Lain.Desktop.Tray;

static class SignalsTest
{
    private static int checks;
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-24T12:00:00Z", CultureInfo.InvariantCulture);
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        checks++;
    }

    public static async Task Main()
    {
        await Attempts();
        await Pagination();
        await QuotaCycles();
        await InvalidQuotas();
        await PoolsAndWindows();
        await MemoryBounds();
        await SafetyAndFailures();
        Console.WriteLine($"PASS desktop signals: {checks} checks; in-memory HTTP and explicit clock only.");
    }

    private static async Task Attempts()
    {
        using var fixture = new Fixture();
        fixture.Attempts.Add(Attempt("old", Start.AddMinutes(-1)));
        var monitor = new DesktopSignalMonitor(fixture.Client);
        Check((await monitor.PollAsync(Start, default)).Alerts.Length == 0, "startup history is a silent baseline");
        fixture.Attempts.Add(Attempt("retry-1", Start.AddSeconds(1)));
        fixture.Attempts.Add(Attempt("retry-2", Start.AddSeconds(2)));
        var snapshot = await monitor.PollAsync(Start.AddSeconds(10), default);
        var alert = snapshot.Alerts.Single();
        Check(alert.Title == "HTTP 429 observed" && alert.Body.Contains("2 observed HTTP 429 attempts, including retries"), "retry attempts are counted separately from their shared request");
        Check(alert.Body.Contains("Friendly route") && alert.Body.Contains("OpenAI Codex"), "observed route ID resolves the configured alias and actual provider");
        Check(!alert.Body.Contains("budget", StringComparison.OrdinalIgnoreCase) && !alert.Body.Contains("private-error"), "429 does not claim exhausted quota or include raw attempt errors");
        Check((await monitor.PollAsync(Start.AddSeconds(20), default)).Alerts.Length == 0, "repeated attempt pages are deduplicated");
        fixture.Attempts.Add(Attempt("retry-3", Start.AddSeconds(21)));
        Check((await monitor.PollAsync(Start.AddSeconds(30), default)).Alerts.Length == 0, "a route's new 429s respect the cooldown");
        var later = await monitor.PollAsync(Start.AddMinutes(6), default);
        Check(later.Alerts.Single().Body.Contains("1 observed HTTP 429 attempt,"), "cooldown summary counts only newly observed attempts");
        Check((await monitor.PollAsync(Start.AddMinutes(6).AddSeconds(10), default)).Alerts.Length == 0, "cooldown summary is not replayed");

        fixture.Attempts.Add(Attempt("same-id", Start.AddMinutes(6).AddSeconds(11), "source-1") with { RouteId = "other", Provider = "zai" });
        fixture.Attempts.Add(Attempt("same-id", Start.AddMinutes(6).AddSeconds(12), "source-2") with { RouteId = "other", Provider = "zai" });
        snapshot = await monitor.PollAsync(Start.AddMinutes(6).AddSeconds(20), default);
        Check(snapshot.Alerts.Single().Body.Contains("2 observed") && snapshot.Alerts.Single().Body.Contains("Route other · Z.ai"), "same attempt ID in distinct source generations remains two observed attempts");
        fixture.Attempts.Add(Attempt("unattributed", Start.AddMinutes(6).AddSeconds(21)) with { RouteId = null, RequestedModel = "route-1" });
        snapshot = await monitor.PollAsync(Start.AddMinutes(6).AddSeconds(30), default);
        Check(snapshot.Alerts.Single().Body.StartsWith("Unknown route", StringComparison.Ordinal), "requested model never fabricates a route relationship");
        fixture.Attempts.Add(Attempt("pending-expiry", Start.AddMinutes(6).AddSeconds(31)) with { RouteId = "other" });
        Check((await monitor.PollAsync(Start.AddMinutes(6).AddSeconds(40), default)).Alerts.Length == 0, "new attempts within a route cooldown stay bundled");
        Check((await monitor.PollAsync(Start.AddMinutes(30), default)).Alerts.Length == 0, "expired pending observations never replay after a long polling gap");
        Check(fixture.Requests.All(request => request.Method == "GET" && AllowedPath(request.Path)), "monitor is GET-only and uses only status/configuration/quotas/attempts");
        Check(fixture.Requests.Where(request => request.Path.EndsWith("/attempts", StringComparison.Ordinal)).All(request => request.Query["statusCode"] == "429" && request.Query["limit"] == "500"), "attempt queries explicitly select real 429 responses and bounded pages");
        Check(snapshot.Lines.Any(line => line.Contains("Recent local HTTP 429 observations · may be incomplete")) && snapshot.Lines.Any(line => line.Contains("separate from local usage")), "hover distinguishes quota provenance and incomplete attempt observations");
    }

    private static async Task Pagination()
    {
        using var fixture = new Fixture();
        var monitor = new DesktopSignalMonitor(fixture.Client);
        await monitor.PollAsync(Start, default);
        for (var index = 0; index < 1001; index++) fixture.Attempts.Add(Attempt("page-" + index, Start.AddSeconds(1)));
        var before = fixture.Requests.Count(request => request.Path.EndsWith("/attempts", StringComparison.Ordinal));
        var snapshot = await monitor.PollAsync(Start.AddSeconds(10), default);
        var pages = fixture.Requests.Where(request => request.Path.EndsWith("/attempts", StringComparison.Ordinal)).Skip(before).ToArray();
        Check(pages.Length == 3 && pages.Select(page => page.Query["offset"]).SequenceEqual(["0", "500", "1000"]), "multiple bounded attempt pages are read");
        Check(snapshot.Alerts.Single().Body.Contains(1001.ToString("N0", CultureInfo.CurrentCulture) + " observed"), "a retry burst spans pages without becoming one logical request");
        Check(pages.All(page => DateTimeOffset.Parse(page.Query["to"], CultureInfo.InvariantCulture) - DateTimeOffset.Parse(page.Query["from"], CultureInfo.InvariantCulture) == TimeSpan.FromMinutes(15)), "each page uses the same bounded interval");

        using var capped = new Fixture();
        var bounded = new DesktopSignalMonitor(capped.Client);
        await bounded.PollAsync(Start, default);
        for (var index = 0; index < 2501; index++) capped.Attempts.Add(Attempt("cap-" + index, Start.AddSeconds(1)));
        before = capped.Requests.Count;
        snapshot = await bounded.PollAsync(Start.AddSeconds(10), default);
        Check(capped.Requests.Skip(before).Count(request => request.Path.EndsWith("/attempts", StringComparison.Ordinal)) == 4, "large bursts never start an unbounded page loop");
        Check(snapshot.Alerts.Single().Body.Contains(2000.ToString("N0", CultureInfo.CurrentCulture) + " observed") && snapshot.Lines.Any(line => line.Contains("some attempts may be missed")), "truncation reports observed counts without claiming complete coverage");
    }

    private static async Task QuotaCycles()
    {
        using var fixture = new Fixture();
        var monitor = new DesktopSignalMonitor(fixture.Client);
        fixture.Quotas = [Quota(Start, 10)];
        Check((await monitor.PollAsync(Start, default)).Alerts.Length == 0, "already-low initial quota does not alert");
        fixture.Quotas = [Quota(Start.AddSeconds(10), 50)];
        Check((await monitor.PollAsync(Start.AddSeconds(10), default)).Alerts.Length == 0, "observed recovery arms the next crossing");
        var crossing = Start.AddSeconds(20);
        fixture.Quotas = [Quota(crossing, 20)];
        var snapshot = await monitor.PollAsync(crossing, default);
        var alert = snapshot.Alerts.Single();
        Check(alert.Title == "Provider quota low" && alert.Body.Contains("Personal account") && alert.Body.Contains("5 hours: 20% remaining"), "fresh threshold crossing names the account and exact window");
        Check(alert.Body.Contains("reported reset " + Start.AddHours(1).ToLocalTime().ToString("dd MMM HH:mm", CultureInfo.CurrentCulture) + " local"), "alert uses the reported reset in local time");
        Check((await monitor.PollAsync(crossing.AddSeconds(10), default)).Alerts.Length == 0, "same low reading produces no repeated quota alert");
        var afterReset = Start.AddHours(1).AddMinutes(1);
        fixture.Quotas = [Quota(afterReset, 10) with { ResetsAt = Start.AddHours(2) }];
        snapshot = await monitor.PollAsync(afterReset, default);
        Check(snapshot.Alerts.Length == 1, "a freshly reported later reset enables a new low-quota cycle");
        var recovery = afterReset.AddMinutes(6);
        fixture.Quotas = [Quota(recovery, 30) with { ResetsAt = Start.AddHours(2) }];
        await monitor.PollAsync(recovery, default);
        fixture.Quotas = [Quota(recovery.AddSeconds(10), 19) with { ResetsAt = Start.AddHours(2) }];
        Check((await monitor.PollAsync(recovery.AddSeconds(10), default)).Alerts.Length == 1, "recovery and a later crossing create another alert cycle");
    }

    private static async Task InvalidQuotas()
    {
        foreach (var invalid in new Func<QuotaDto, QuotaDto>[]
        {
            quota => quota with { State = "stale" }, quota => quota with { State = "unknown" }, quota => quota with { State = "unsupported" },
            quota => quota with { State = "refreshing" }, quota => quota with { State = "error" },
            quota => quota with { LastSuccessAt = Start.AddMinutes(-6) }, quota => quota with { LastSuccessAt = null },
            quota => quota with { UsedPercent = 101 }, quota => quota with { WindowLabel = null },
            quota => quota with { ResetsAt = Start.AddSeconds(-1) },
            quota => quota with { UsedPercent = null, Remaining = null, Limit = null },
            quota => quota with { Source = "unavailable" }
        })
        {
            using var fixture = new Fixture();
            var monitor = new DesktopSignalMonitor(fixture.Client);
            await monitor.PollAsync(Start, default);
            fixture.Quotas = [invalid(Quota(Start.AddSeconds(10), 10))];
            Check((await monitor.PollAsync(Start.AddSeconds(10), default)).Alerts.Length == 0, "untrusted/unknown quota evidence never triggers a low-quota signal");
            fixture.Quotas = [Quota(Start.AddSeconds(20), 10)];
            Check((await monitor.PollAsync(Start.AddSeconds(20), default)).Alerts.Length == 1, "untrusted evidence did not replace the healthy baseline");
        }

        using var amounts = new Fixture();
        var byAmount = new DesktopSignalMonitor(amounts.Client);
        amounts.Quotas = [Quota(Start, 50) with { UsedPercent = null }];
        await byAmount.PollAsync(Start, default);
        amounts.Quotas = [Quota(Start.AddSeconds(10), 20) with { UsedPercent = null, ResetsAt = null }];
        var snapshot = await byAmount.PollAsync(Start.AddSeconds(10), default);
        Check(snapshot.Alerts.Single().Body.Contains("20% remaining") && snapshot.Alerts.Single().Body.Contains("reset unknown"), "same-window reported amounts justify a percentage and never invent a reset");

        using var overdue = new Fixture();
        var resetMonitor = new DesktopSignalMonitor(overdue.Client);
        await resetMonitor.PollAsync(Start, default);
        var observed = Start.AddSeconds(10);
        overdue.Quotas = [Quota(observed, 10) with { ResetsAt = Start.AddSeconds(5) }];
        snapshot = await resetMonitor.PollAsync(observed, default);
        var line = snapshot.Lines.Single(text => text.Contains("5 hours", StringComparison.Ordinal));
        Check(snapshot.Alerts.Length == 0 && line.Contains("reset due · last known") && !line.Contains(" · reported · "),
            "a recently observed quota with an overdue reset is last-known data, not a current window or alarm");
        Check(line.Contains("reported reset " + Start.AddSeconds(5).ToLocalTime().ToString("dd MMM HH:mm", CultureInfo.CurrentCulture) + " local"),
            "an overdue reset retains its exact reported local-time label");
    }

    private static async Task PoolsAndWindows()
    {
        using var fixture = new Fixture();
        fixture.Configuration = fixture.Configuration with { Accounts = [fixture.Configuration.Accounts[0], fixture.Configuration.Accounts[0] with { Id = "account-2", Name = "Work account" }] };
        fixture.Quotas = [Quota(Start, 50) with { BudgetPoolId = "proven-pool" }, Quota(Start, 50) with { AccountId = "account-2", BudgetPoolId = "proven-pool" }];
        var monitor = new DesktopSignalMonitor(fixture.Client);
        await monitor.PollAsync(Start, default);
        fixture.Quotas = fixture.Quotas.Select(quota => quota with { UsedPercent = 90, Remaining = 10, LastSuccessAt = Start.AddSeconds(10) }).ToArray();
        var snapshot = await monitor.PollAsync(Start.AddSeconds(10), default);
        Check(snapshot.Alerts.Length == 1 && snapshot.Alerts[0].Body.Contains("Personal account / Work account") && snapshot.Alerts[0].Body.Contains("shared provider pool"), "only a known identical pool/window deduplicates shared quota alerts");

        using var independent = new Fixture();
        independent.Quotas = [Quota(Start, 50) with { BudgetPoolId = "same-pool" },
            Quota(Start, 50) with { BudgetPoolId = "same-pool", WindowLabel = "Weekly" }];
        var separate = new DesktopSignalMonitor(independent.Client);
        await separate.PollAsync(Start, default);
        independent.Quotas = independent.Quotas.Select(quota => quota with { UsedPercent = 85, Remaining = 15, LastSuccessAt = Start.AddSeconds(10) }).ToArray();
        snapshot = await separate.PollAsync(Start.AddSeconds(10), default);
        Check(snapshot.Alerts.Length == 2 && snapshot.Alerts.Any(alert => alert.Body.Contains("5 hours")) && snapshot.Alerts.Any(alert => alert.Body.Contains("Weekly")),
            "different reported window labels remain separate with the same source, unit and known pool");
    }

    private static async Task SafetyAndFailures()
    {
        using var fixture = new Fixture();
        var monitor = new DesktopSignalMonitor(fixture.Client);
        await monitor.PollAsync(Start, default);
        fixture.Configuration = fixture.Configuration with { Accounts = [fixture.Configuration.Accounts[0] with { Name = "Personal\naccount\u0001" + new string('X', 500) }] };
        fixture.Quotas = [Quota(Start.AddSeconds(10), 10) with { Error = "private-error-secret" }];
        var snapshot = await monitor.PollAsync(Start.AddSeconds(10), default);
        Check(snapshot.Lines.All(line => line.Length <= 160 && !line.Any(char.IsControl)) && snapshot.Alerts.All(alert => !alert.Body.Any(char.IsControl)), "hover and notification labels are bounded single-line text");
        Check(!string.Join(" ", snapshot.Lines.Concat(snapshot.Alerts.Select(alert => alert.Body))).Contains("private-error-secret"), "raw quota errors never enter desktop text");
        fixture.FailAll = true;
        snapshot = await monitor.PollAsync(Start.AddSeconds(20), default);
        Check(!snapshot.Connected && snapshot.Alerts.Length == 0 && snapshot.Tooltip == "lain · host unavailable", "offline state is explicit and does not synthesize alerts");
        Check(!string.Join(" ", snapshot.Lines).Contains("transport-secret"), "transport exceptions never leak into hover");

        using var unavailable = new Fixture { FailAll = true };
        var delayed = new DesktopSignalMonitor(unavailable.Client);
        await delayed.PollAsync(Start, default);
        unavailable.FailAll = false;
        unavailable.Attempts.Add(Attempt("collected-later", Start.AddSeconds(1)));
        Check((await delayed.PollAsync(Start.AddMinutes(1), default)).Alerts.Length == 0, "first successful attempts snapshot stays a baseline after startup transport failure");
        unavailable.AttemptsHttp429 = true;
        snapshot = await delayed.PollAsync(Start.AddMinutes(1).AddSeconds(10), default);
        Check(snapshot.Alerts.Length == 0 && snapshot.Lines.Any(line => line.Contains("429 observations unavailable")), "429 from the Host attempts endpoint is not an inference-attempt signal");

        var before = unavailable.Requests.Count;
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        try { await delayed.PollAsync(Start.AddMinutes(2), cancel.Token); throw new InvalidOperationException("cancellation expected"); }
        catch (OperationCanceledException) { checks++; }
        Check(unavailable.Requests.Count == before, "already-cancelled polls issue no requests");
        await unavailable.Client.StatusAsync();
        Check(unavailable.Requests.Count == before + 1, "monitor never disposes the caller-owned client");
    }

    private static async Task MemoryBounds()
    {
        using var fixture = new Fixture();
        var monitor = new DesktopSignalMonitor(fixture.Client);
        await monitor.PollAsync(Start, default);
        for (var batch = 1; batch <= 3; batch++)
        {
            var at = Start.AddSeconds(batch * 10);
            for (var index = 0; index < 2000; index++)
                fixture.Attempts.Add(Attempt($"batch-{batch}-{index}", at.AddSeconds(-1)) with { RouteId = "route-" + index });
            fixture.Quotas = Enumerable.Range(0, 600).Select(index => Quota(at, 50) with { Source = $"batch-{batch}-window-{index}", WindowLabel = "Window " + index }).ToArray();
            var snapshot = await monitor.PollAsync(at, default);
            Check(snapshot.Alerts.Length <= 12 && snapshot.Lines.Any(line => line.Contains("quota list bounded")), "route bursts and large quota inventories have bounded desktop output");
        }
        int Count(string name)
        {
            var value = typeof(DesktopSignalMonitor).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(monitor)!;
            return (int)value.GetType().GetProperty("Count")!.GetValue(value)!;
        }
        Check(Count("_seenAttempts") <= 4096 && Count("_routes") <= 256 && Count("_windows") <= 512,
            "retained signal memory stays bounded after distinct attempt/route/window churn");
    }

    private static bool AllowedPath(string path) => path is "/api/v1/status" or "/api/v1/configuration" or "/api/v1/quotas" or "/api/v1/attempts";
    private static AttemptDto Attempt(string id, DateTimeOffset at, string source = "source-1") => new(id, "shared-logical-request", "account-1", "route-1", "codex", "actual-model", at, at.AddMilliseconds(5), 429,
        "failed", new(null, null, null, null, null), Error: "private-error-secret", SourceId: source);
    private static QuotaDto Quota(DateTimeOffset at, double remaining) => new("account-1", null, "provider-window-5h", "ok", at, at,
        Start.AddHours(1), "credits", 100 - remaining, 100, remaining, false, WindowLabel: "5 hours", UsedPercent: 100 - remaining);

    private sealed class Fixture : HttpMessageHandler
    {
        public readonly List<AttemptDto> Attempts = [];
        public readonly List<Request> Requests = [];
        public ConfigurationDto Configuration = new(1, 1, [new("account-1", "Personal account", "codex", true, "snapshot", "ready")],
            [new("route-1", "Friendly\nroute\u0007", "actual-model", "codex", ["account-1"])], []);
        public QuotaDto[] Quotas = [Quota(Start, 50)];
        public bool FailAll, AttemptsHttp429;
        private readonly HttpClient _http;
        public LainApiClient Client { get; }
        public Fixture()
        {
            _http = new HttpClient(this, disposeHandler: false) { BaseAddress = new Uri("http://fixture.invalid") };
            Client = new LainApiClient(_http);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = request.RequestUri!.AbsolutePath;
            var query = request.RequestUri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2)).ToDictionary(parts => Uri.UnescapeDataString(parts[0]), parts => Uri.UnescapeDataString(parts[1]), StringComparer.Ordinal);
            Requests.Add(new Request(request.Method.Method, path, query));
            if (request.Method != HttpMethod.Get || !AllowedPath(path)) throw new InvalidOperationException("unexpected mutating or external request");
            if (FailAll) throw new HttpRequestException("transport-secret");
            if (path == "/api/v1/attempts" && AttemptsHttp429)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = JsonContent.Create(new ApiError("rate_limit", "transport-secret")) });
            object body = path switch
            {
                "/api/v1/status" => new StatusDto(Api.Version, "test", "ready", true, 1, 1, Start),
                "/api/v1/configuration" => Configuration,
                "/api/v1/quotas" => Quotas,
                "/api/v1/attempts" => Page(query),
                _ => throw new InvalidOperationException("unexpected request")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body, body.GetType()) });
        }

        private PageDto<AttemptDto> Page(Dictionary<string, string> query)
        {
            var from = DateTimeOffset.Parse(query["from"], CultureInfo.InvariantCulture);
            var to = DateTimeOffset.Parse(query["to"], CultureInfo.InvariantCulture);
            var status = int.Parse(query["statusCode"], CultureInfo.InvariantCulture);
            var offset = int.Parse(query["offset"], CultureInfo.InvariantCulture);
            var limit = int.Parse(query["limit"], CultureInfo.InvariantCulture);
            var all = Attempts.Where(attempt => attempt.StatusCode == status && attempt.StartedAt >= from && attempt.StartedAt < to)
                .OrderByDescending(attempt => attempt.StartedAt).ThenBy(attempt => attempt.SourceId, StringComparer.Ordinal).ThenBy(attempt => attempt.Id, StringComparer.Ordinal).ToArray();
            return new(all.Skip(offset).Take(limit).ToArray(), offset, limit, all.LongLength);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { Client.Dispose(); _http.Dispose(); }
            base.Dispose(disposing);
        }
    }

    private sealed record Request(string Method, string Path, Dictionary<string, string> Query);
}
