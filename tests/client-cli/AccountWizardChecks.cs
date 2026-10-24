using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lain.Client;
using Lain.Cli;
using Lain.Core;

internal static class AccountWizardChecks
{
    internal static async Task<int> RunAsync()
    {
        var checks = 0;
        void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
        var answers = new string?[] { "1", "scope-one", "", "SYNTHETIC-WIZARD-KEY", "yes" };
        var terminal = new ScriptedTerminal(answers);
        var handler = new Handler();
        var output = new StringWriter(); var error = new StringWriter();
        var exit = await Run(terminal, handler, output, error);
        Check(exit == 0 && handler.Writes == 1 && handler.LastRevision == 7, "wizard sends one atomic save using the initial revision");
        Check(handler.SavedName == "Fixture 2" && handler.SelectedModels.Length == 0, "wizard shares short name collision rule and leaves optional models for later");
        Check(terminal.SecretPrompts == 1 && !output.ToString().Contains("SYNTHETIC-WIZARD-KEY") && !error.ToString().Contains("SYNTHETIC-WIZARD-KEY"), "credential uses hidden prompt and never enters output");
        Check(output.ToString().Contains("Official guidance: https://example.invalid/keys") && output.ToString().Contains("quota observations and inference are separate facts"), "wizard shows credential link and distinguishes saving from checks");
        Check(handler.Requests.All(path => path is "/api/v1/connection-methods" or "/api/v1/configuration" or "/api/v1/accounts/connect" or "/api/v1/quotas"), "wizard has no quota refresh, login or inference side effects");
        for (var cancelAt = 0; cancelAt < answers.Length; cancelAt++)
        {
            handler = new(); output = new(); error = new();
            terminal = new(answers.Take(cancelAt).Concat(new string?[] { null }));
            exit = await Run(terminal, handler, output, error);
            Check(exit == 0 && handler.Writes == 0 && output.ToString().Contains("No account was saved"), "cancellation at prompt " + cancelAt + " leaves no partial account");
        }
        handler = new(); output = new(); error = new();
        terminal = new(["1", "scope-one", "Named by user", "SYNTHETIC-WIZARD-KEY", "yes"]);
        exit = await Run(terminal, handler, output, error);
        Check(exit == 0 && handler.SavedName == "Named by user" && handler.SelectedModels.Length == 0, "custom name and account-only save are preserved");
        handler = new() { Conflict = true }; output = new(); error = new();
        exit = await Run(new ScriptedTerminal(answers), handler, output, error);
        Check(exit == 5 && handler.Writes == 1 && error.ToString().Contains("revision_conflict"), "revision conflict is visible and never automatically retried");
        output = new(); error = new();
        exit = await CliApplication.RunAsync(["accounts", "add"], output, error, () => throw new Exception("Redirected wizard must not discover host"));
        Check(exit == 2 && error.ToString().Contains("--key-stdin") && output.ToString() == "", "redirected wizard refuses prompts before host discovery");
        return checks;
    }

    internal static async Task<int> RunFixtureAsync(TextWriter output, TextWriter error)
    {
        var handler = new Handler();
        return await Run(null, handler, output, error);
    }

    private static Task<int> Run(IAccountWizardTerminal? terminal, Handler handler, TextWriter output, TextWriter error)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://fixture.invalid/") };
        return CliApplication.RunAsync(["accounts", "add"], output, error, () => new LainApiClient(http, "FIXTURE-HOST-TOKEN"), wizardTerminal: terminal);
    }

    private sealed class ScriptedTerminal(IEnumerable<string?> answers) : IAccountWizardTerminal
    {
        private readonly Queue<string?> _answers = new(answers);
        public bool IsInteractive => true;
        public int SecretPrompts { get; private set; }
        public Task<string?> ReadAsync(string prompt, bool secret, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (secret) SecretPrompts++;
            return Task.FromResult(_answers.Dequeue());
        }
    }

    private sealed class Handler : HttpMessageHandler
    {
        public int Writes { get; private set; }
        public bool Conflict { get; init; }
        public long LastRevision { get; private set; }
        public string? SavedName { get; private set; }
        public string[] SelectedModels { get; private set; } = [];
        public List<string> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(path);
            var configuration = new ConfigurationDto(7, 7, [new("existing", "Fixture", "fixture", true, "api-key", "ready")], [], []);
            if (request.Method == HttpMethod.Get && path == "/api/v1/configuration") return Ok(configuration);
            if (request.Method == HttpMethod.Get && path == "/api/v1/connection-methods")
                return Ok(new ConnectionMethodCatalogDto(1, [new("fixture", "api-key", "Existing key", "api-key", ["inference", "quota"], "available", false, true,
                    "api-key", "Synthetic wizard fixture", "https://example.invalid/v1", [new("accountId", "Account ID")], "https://example.invalid/keys", Recommended: true)]));
            if (request.Method == HttpMethod.Get && path == "/api/v1/quotas") return Ok(Array.Empty<QuotaDto>());
            if (request.Method == HttpMethod.Post && path == "/api/v1/accounts/connect")
            {
                Writes++;
                using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var envelope = document.RootElement; var value = envelope.GetProperty("value");
                LastRevision = envelope.GetProperty("revision").GetInt64();
                SavedName = value.GetProperty("connection").GetProperty("name").GetString();
                SelectedModels = [];
                if (value.GetProperty("connection").TryGetProperty("billingPlan", out _) || value.TryGetProperty("selectedModelRouteIds", out _)) throw new Exception("Connect must not require tariff/model templates");
                if (value.GetProperty("secret").GetString() != "SYNTHETIC-WIZARD-KEY" || value.GetProperty("connection").GetProperty("method").GetString() != "api-key" ||
                    value.GetProperty("baseUrl").GetString() != "https://example.invalid/v1") throw new Exception("Wizard request contract mismatch");
                return Conflict ? new(HttpStatusCode.Conflict) { Content = JsonContent.Create(new ApiError("revision_conflict", "Reload configuration and restart setup.")) }
                    : Ok(configuration with { Revision = 8, Accounts = configuration.Accounts.Append(new("new", SavedName!, "fixture", true, "api-key", "unverified")).ToArray() });
            }
            throw new Exception("Unexpected wizard fixture request");
        }
        private static HttpResponseMessage Ok(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value, value.GetType()) };
    }
}
