using System.Net;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Quota;

internal static class CloudflareDiagnosticCases
{
    public static async Task<int> Run()
    {
        var checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        const string accountId = "0123456789abcdef0123456789abcdef";
        const string foreignId = "abcdef0123456789abcdef0123456789";
        var now = DateTimeOffset.Parse("2026-10-25T08:15:30Z");
        var start = CloudflareWorkersAiReader.UtcDayStart(now);
        var account = new AccountDto("fixture", "Cloudflare", "cloudflare-workers-ai", true, "api-key", "ready",
            Binding: new("cloudflare-workers-ai", ResourceId: accountId));
        var credential = new QuotaCredential("fixture-private-token", settings: new Dictionary<string, string> { ["accountId"] = accountId });
        object Errors(string message) => new { errors = new[] { new { message } } };
        object Settings(object? enabled, object fields, string selected = accountId) => new {
            data = new { viewer = new { accounts = new[] { new { accountTag = selected,
                settings = new { aiInferenceAdaptiveGroups = new { enabled, availableFields = fields } } } } } }
        };
        var unknown = Errors("query does not have permission to access this dataset fixture-raw-secret");
        var unknownResult = CloudflareQuotaAdapter.Normalize(JsonSerializer.SerializeToElement(unknown), accountId, start, now);
        Check(unknownResult.Support?.Kind == "unavailable" && unknownResult.Error!.Contains("not distinguished"),
            "generic GraphQL permission wording cannot identify missing permission or entitlement");
        Check(!unknownResult.Error!.Contains("fixture-raw-secret"), "GraphQL text is never returned");
        Check(CloudflareWorkersAiReader.DatasetAvailable(JsonSerializer.SerializeToElement(new {
            data = new { __type = new { fields = new object[] { new { name = "other" }, new { unexpected = "fixture-raw-secret" } } } }
        })) is null, "malformed introspection does not prove dataset absence");
        foreach (var message in new[] { "Account Analytics permission denied fixture-raw-secret", "Missing Account Analytics permission", "Account Analytics Read permission required" })
        {
            using var handler = new Handler(_ => Task.FromResult(Handler.Json(Errors(message))));
            using var adapter = new CloudflareQuotaAdapter(handler: handler, clock: new Clock(now));
            var result = await adapter.FetchAsync(account, credential);
            Check(result.Support?.Kind == "setup-required" && result.Error!.Contains("explicitly reported") && handler.Calls == 1,
                "only an explicit named Analytics permission denial directs grant review without extra probes");
            Check(!result.Error!.Contains("fixture-raw-secret"), "permission diagnostic is fixed safe text");
        }

        using (var handler = new Handler(_ => throw new Exception("mismatched binding must not make a request")))
        using (var adapter = new CloudflareQuotaAdapter(handler: handler, clock: new Clock(now)))
        {
            var mismatch = await adapter.FetchAsync(account, new("fixture", monitor: new("api-token", "fixture-monitor", foreignId)));
            Check(mismatch.Support?.Kind == "setup-required" && mismatch.Support.Message.Contains("different account") && handler.Calls == 0,
                "known local account mismatch has its own actionable reason before network access");
        }

        var cases = new (object Reply, string Expected, string Kind)[] {
            (Settings(false, new[] { "sum.totalNeurons" }), "settings report Workers AI analytics disabled", "unavailable"),
            (Settings(true, new[] { "sum.requests" }), "do not list the requested neuron field", "unavailable"),
            (Settings(true, Array.Empty<string>()), "do not list the requested neuron field", "unavailable"),
            (Settings(true, new[] { "sum.totalNeurons" }), "not distinguished", "unavailable"),
            (Settings(null, new[] { "sum.totalNeurons" }), "not distinguished", "unavailable"),
            (Settings("false", new[] { "sum.totalNeurons" }), "not distinguished", "unavailable"),
            (Settings(true, new object[] { 1, "sum.requests" }), "not distinguished", "unavailable"),
            (Settings(false, Array.Empty<string>(), foreignId), "different account", "setup-required"),
            (Errors("settings forbidden fixture-raw-secret"), "not distinguished", "unavailable"),
            (new { data = new { viewer = new { accounts = Array.Empty<object>() } } }, "not distinguished", "unavailable")
        };
        foreach (var item in cases)
        {
            using var handler = new Handler(async request => {
                var body = await request.Content!.ReadAsStringAsync();
                Check(request.Method == HttpMethod.Post && request.RequestUri!.AbsoluteUri == CloudflareWorkersAiReader.Endpoint,
                    "diagnostics retain fixed read-only GraphQL endpoint");
                Check(request.Headers.Authorization?.Parameter == credential.BearerToken, "diagnostics keep the selected token");
                if (!body.Contains("settings")) return Handler.Json(unknown);
                using var json = JsonDocument.Parse(body);
                var query = json.RootElement.GetProperty("query").GetString()!;
                Check(query == CloudflareWorkersAiReader.SettingsQuery(accountId) && query.Contains("enabled availableFields")
                    && !query.Contains("mutation") && !query.Contains("ai/run"), "settings query stays pinned to selected account and documented read fields");
                return Handler.Json(item.Reply);
            });
            using var adapter = new CloudflareQuotaAdapter(handler: handler, clock: new Clock(now));
            var result = await adapter.FetchAsync(account, credential);
            Check(handler.Calls == 2 && result.Readings.Count == 0 && result.Error is not null && result.Support?.Kind == item.Kind
                && result.Support.Message.Contains(item.Expected), "settings distinguish only facts established by this account response");
            Check(!result.Support!.Message.Contains("fixture-raw-secret"), "settings/primary provider messages remain private");
        }

        foreach (var response in new[] {
            new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("fixture-raw-secret") },
            new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("fixture-raw-secret") },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{invalid fixture-raw-secret") }
        })
        {
            var calls = 0;
            using var handler = new Handler(_ => Task.FromResult(++calls == 1 ? Handler.Json(unknown) : response));
            using var adapter = new CloudflareQuotaAdapter(handler: handler, clock: new Clock(now));
            var result = await adapter.FetchAsync(account, credential);
            Check(handler.Calls == 2 && result.Error == unknownResult.Error && result.Support == unknownResult.Support,
                "optional diagnostic failure cannot replace the primary usage failure or demand a login");
        }

        using (var handler = new Handler(async request => {
            var body = await request.Content!.ReadAsStringAsync();
            if (body.Contains("__type")) return Handler.Json(new { data = new { __type = new { fields = new[] { new { name = CloudflareWorkersAiReader.Dataset } } } } });
            return Handler.Json(body.Contains("settings") ? Settings(false, Array.Empty<string>()) : Errors("Cannot query field totalNeurons fixture-raw-secret"));
        }))
        using (var adapter = new CloudflareQuotaAdapter(handler: handler, clock: new Clock(now)))
        {
            var result = await adapter.FetchAsync(account, credential);
            Check(handler.Calls == 3 && result.Support!.Message.Contains("settings report"),
                "schema field presence cannot substitute for account dataset settings; total requests bounded at three");
        }

        using (var cancellation = new CancellationTokenSource())
        using (var handler = new Handler(async request => {
            if ((await request.Content!.ReadAsStringAsync()).Contains("settings")) { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); }
            return Handler.Json(unknown);
        }))
        using (var adapter = new CloudflareQuotaAdapter(handler: handler, clock: new Clock(now)))
        {
            var cancelled = false;
            try { await adapter.FetchAsync(account, credential, cancellation.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "caller cancellation during optional diagnosis propagates");
        }

        // The same QuotaDto/support contract is consumed by GUI and CLI; verify reasons survive
        // the actual service projection and retain an older observation as stale, never as zero.
        var replies = new Queue<object>(new[] {
            (object)new { data = new { viewer = new { accounts = new[] { new { accountTag = accountId,
                aiInferenceAdaptiveGroups = new[] { new { sum = new { totalNeurons = 7 } } } } } } } },
            new { data = new { viewer = new { accounts = new[] { new { accountTag = accountId, aiInferenceAdaptiveGroups = Array.Empty<object>() } } } } },
            unknown, Settings(false, Array.Empty<string>())
        });
        using (var handler = new Handler(_ => Task.FromResult(Handler.Json(replies.Dequeue()))))
        using (var adapter = new CloudflareQuotaAdapter(handler: handler, clock: new Clock(now)))
        {
            var source = new Source(account, credential);
            var service = new QuotaService(source, source, [adapter], new Clock(now));
            var measured = (await service.RefreshAsync(account.Id)).Single();
            Check(measured.Used == 7 && measured.Limit is null && measured.Remaining is null, "service starts with measured estimate only");
            var empty = (await service.RefreshAsync(account.Id)).Single();
            Check(empty.Used == 7 && empty.State == "stale" && empty.Support!.Message.Contains("accepted the analytics query")
                && empty.Error is null && empty.LastSuccessAt == measured.LastSuccessAt, "successful empty result retains old estimate as stale without inventing zero or login failure");
            var denied = (await service.RefreshAsync(account.Id)).Single();
            var serialized = JsonSerializer.Serialize(denied);
            Check(denied.Used == 7 && denied.State == "stale" && denied.Support!.Message.Contains("settings report")
                && denied.Error == denied.Support.Message && !serialized.Contains("fixture-raw-secret")
                && !serialized.Contains(credential.BearerToken), "account dataset reason survives GUI/CLI DTO projection without secrets or erased history");
        }
        using (var handler = new Handler(_ => Task.FromResult(Handler.Json(new {
            data = new { viewer = new { accounts = new[] { new { accountTag = accountId, aiInferenceAdaptiveGroups = Array.Empty<object>() } } } }
        }))))
        using (var adapter = new CloudflareQuotaAdapter(handler: handler, clock: new Clock(now)))
        {
            var source = new Source(account, credential);
            var row = (await new QuotaService(source, source, [adapter], new Clock(now)).RefreshAsync(account.Id)).Single();
            Check(row.Used is null && row.Remaining is null && row.LastSuccessAt is null && row.Error is null
                && row.Support!.Kind == "unavailable" && row.Support.Message.Contains("no neuron samples"),
                "first successful empty read exposes a concrete unknown reason without invented measurements");
        }
        return checks;
    }

    private sealed class Source(AccountDto account, QuotaCredential credential) : IQuotaAccountSource, IQuotaCredentialSource
    {
        public Task<IReadOnlyList<AccountDto>> GetAccountsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AccountDto>>([account]);
        public Task<QuotaCredential?> GetCredentialAsync(string accountId, CancellationToken ct = default) => Task.FromResult<QuotaCredential?>(credential);
    }
}
