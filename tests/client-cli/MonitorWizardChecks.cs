using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lain.Client;
using Lain.Cli;
using Lain.Core;

internal static class MonitorWizardChecks
{
    internal static async Task<int> RunAsync()
    {
        var checks = 0;
        void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
        string?[] answers = ["2", "1", "project-one", "SYNTHETIC-MONITOR-TOKEN", "2099-01-01T00:00:00Z", "yes"];
        var handler = new Handler(); var output = new StringWriter(); var error = new StringWriter();
        var exit = await Run(answers, handler, output, error);
        Check(exit == 0 && handler.Writes == 1 && handler.Saved is { Kind: "access-token-snapshot", ResourceId: "project-one" }, "monitor wizard saves one separate access snapshot on the existing account");
        Check(handler.Saved!.Secret == "SYNTHETIC-MONITOR-TOKEN" && !output.ToString().Contains(handler.Saved.Secret) && error.ToString() == "", "monitor secret reaches only the private request");
        Check(output.ToString().Contains("No automatic refresh") && output.ToString().Contains("Required read permissions: monitoring.read") && output.ToString().Contains("https://example.invalid/monitor"), "monitor explains purpose, scope, official instructions and snapshot limit");
        for (var index = 0; index < answers.Length; index++)
        {
            handler = new(); output = new(); error = new();
            exit = await Run(answers.Take(index).Concat(new string?[] { null }), handler, output, error);
            Check(exit == 0 && handler.Writes == 0 && output.ToString().Contains("Saved account is unchanged"), "monitor cancellation at prompt " + index + " preserves existing state");
        }
        handler = new(); output = new(); error = new();
        exit = await Run(["2", "1", "project-one", "SYNTHETIC-MONITOR-TOKEN", "2020-01-01T00:00:00Z"], handler, output, error);
        Check(exit == 2 && handler.Writes == 0 && !error.ToString().Contains("SYNTHETIC-MONITOR-TOKEN"), "expired snapshot is rejected without storing a token");
        handler = new() { Configured = true }; output = new(); error = new();
        exit = await Run(["3", "yes"], handler, output, error);
        Check(exit == 0 && handler.Removals == 1 && handler.Writes == 0 && output.ToString().Contains("external access was not revoked"), "remove deletes only same-account local monitoring access");
        handler = new(); output = new(); error = new();
        exit = await Run(["1"], handler, output, error);
        Check(exit == 0 && handler.RefreshAccount == "existing" && handler.Writes == 0 && output.ToString().Contains("81.52 USD"),
            "saved access can refresh this account without entering credentials again or creating an account");
        handler = new() { Direct = true }; output = new(); error = new();
        exit = await Run(["2", "SYNTHETIC-REPLACEMENT", "yes"], handler, output, error);
        Check(exit == 0 && handler.Connection is { Connection.AccountId: "existing", Connection.Name: "Custom project", Connection.ExpectedGeneration: 4 }
            && handler.Connection.Connection.Purposes.SequenceEqual(new[] { "quota" }) && handler.Connection.Secret == "SYNTHETIC-REPLACEMENT"
            && !output.ToString().Contains("SYNTHETIC-REPLACEMENT"), "key reconnect preserves same-account identity, custom name and generation while requesting only quota access");
        handler = new() { Direct = true, Browser = true }; output = new(); error = new();
        exit = await Run(["2", "yes"], handler, output, error);
        Check(exit == 0 && handler.Login is { AccountId: "existing", ExpectedGeneration: 4, Method: "browser" }
            && handler.Login.Purposes!.SequenceEqual(new[] { "quota" }), "available browser login reconnects quota on the existing account");
        handler = new() { Direct = true, Browser = true, Blocked = true }; output = new(); error = new();
        exit = await Run([], handler, output, error);
        Check(exit == 0 && handler.Login is null && handler.Connection is null && output.ToString().Contains("No direct quota connection is available"),
            "unavailable browser monitoring never starts OAuth or asks for credentials");
        return checks;
    }

    private static Task<int> Run(IEnumerable<string?> answers, Handler handler, TextWriter output, TextWriter error)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://fixture.invalid/") };
        return CliApplication.RunAsync(["accounts", "monitor", "existing"], output, error, () => new LainApiClient(http), wizardTerminal: new Terminal(answers));
    }
    private sealed class Terminal(IEnumerable<string?> answers) : IAccountWizardTerminal
    {
        private readonly Queue<string?> queue = new(answers);
        public bool IsInteractive => true;
        public Task<string?> ReadAsync(string prompt, bool secret, CancellationToken ct) => Task.FromResult(queue.Dequeue());
    }
    private sealed class Handler : HttpMessageHandler
    {
        public bool Configured { get; init; }
        public bool Direct { get; init; }
        public bool Browser { get; init; }
        public bool Blocked { get; init; }
        public string? RefreshAccount;
        public ConnectCredentialRequest? Connection;
        public LoginRequest? Login;
        public int Writes { get; private set; }
        public int Removals { get; private set; }
        public MonitorAccessRequest? Saved { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var configuration = new ConfigurationDto(7, 7, [new("existing", "Custom project", "fixture", true, "api-key", "unverified", Generation: 4)], [], []);
            if (request.Method == HttpMethod.Get && path == "/api/v1/configuration") return Ok(configuration);
            if (request.Method == HttpMethod.Get && path == "/api/v1/connection-methods") return Ok(new ConnectionMethodCatalogDto(1,
                Direct ? [new("fixture", Browser ? "browser" : "api-key", "Existing provider access", Browser ? "browser" : "api-key", ["inference", "quota"],
                    Blocked ? "setup-required" : "available", false, !Browser, Browser ? null : "api-key", DefaultBaseUrl: "https://example.invalid/v1")] : []));
            if (request.Method == HttpMethod.Post && path == "/api/v1/quotas/refresh")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                RefreshAccount = body.RootElement.GetProperty("accountId").GetString();
                return Ok(new[] { new QuotaDto("existing", null, "fixture#credits", "ok", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null,
                    "USD", null, null, 81.519887875, false, MeasurementKind: "balance") });
            }
            if (request.Method == HttpMethod.Post && path == "/api/v1/accounts/connect")
            {
                var body = await request.Content!.ReadFromJsonAsync<RevisionRequest<ConnectCredentialRequest>>(cancellationToken: ct);
                if (body!.Revision != 7) throw new Exception("Wrong reconnect revision");
                Connection = body.Value; return Ok(configuration with { Revision = 8 });
            }
            if (request.Method == HttpMethod.Post && path == "/api/v1/accounts/login")
            {
                Login = await request.Content!.ReadFromJsonAsync<LoginRequest>(cancellationToken: ct);
                return Ok(new LoginDto("fixture-login", "fixture", "completed", AccountId: "existing"));
            }
            if (request.Method == HttpMethod.Get && path == "/api/v1/quotas") return Ok(Array.Empty<QuotaDto>());
            if (request.Method == HttpMethod.Get && path == "/api/v1/providers") return Ok(new ProviderCatalogDto(1, DateTimeOffset.UtcNow,
                [new("fixture", "Fixture", "Synthetic monitor fixture", "https://example.invalid/keys", "https://example.invalid/docs", [], [], [], [], [], [],
                    MonitorAccessOptions: Direct ? [] : [new("access-token-snapshot", "Read project usage", true, "Project ID", "Use the matching reporting project.", "https://example.invalid/monitor", ["monitoring.read"], "No automatic refresh; explicit external snapshot for project usage.")])]));
            if (path == "/api/v1/accounts/existing/monitor")
            {
                if (request.Method == HttpMethod.Get) return Ok(new MonitorAccessDto("existing", "fixture", Configured ? "configured-unverified" : Direct ? "unsupported" : "not-configured"));
                if (request.Method == HttpMethod.Put)
                {
                    var envelope = await request.Content!.ReadFromJsonAsync<RevisionRequest<MonitorAccessRequest>>(cancellationToken: ct);
                    if (envelope!.Revision != 7) throw new Exception("Wrong monitor revision");
                    Writes++; Saved = envelope.Value; return Ok(configuration with { Revision = 8 });
                }
                if (request.Method == HttpMethod.Delete && request.RequestUri.Query == "?revision=7") { Removals++; return Ok(configuration with { Revision = 8 }); }
            }
            throw new Exception("Unexpected monitor wizard request");
        }
        private static HttpResponseMessage Ok(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value, value.GetType()) };
    }
}
