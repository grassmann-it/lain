using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lain.Client;
using Lain.Cli;
using Lain.Core;

internal static class ResourceOptionalMonitorChecks
{
    private const string Token = "SYNTHETIC-PRIVATE-MANAGEMENT-TOKEN";
    internal static async Task<int> RunAsync()
    {
        var checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        string?[] answers = ["2", "1", Token, "yes"];
        var handler = new Handler();
        var terminal = new Terminal(answers);
        var result = await Run(handler, terminal: terminal);
        Check(result.Exit == 0 && handler.Writes == 1 && handler.Saved is { Kind: "management-token", ResourceId: null, Secret: Token },
            "resource-less monitor saves the separate token once on the existing account without inheriting its resource");
        Check(terminal.Prompts.Count == 4 && terminal.Prompts.Single(prompt => prompt.Secret).Prompt.Contains("hidden")
            && !terminal.Prompts.Any(prompt => prompt.Prompt.Contains("Project ID")), "resource requirement controls prompts independently of a nonempty label");
        Check(!result.Output.Contains(Token) && !result.Error.Contains(Token) && result.Output.Contains("usage:read")
            && result.Output.Contains("does not replace inference credentials"), "monitor explains narrow access without printing the secret");
        for (var index = 0; index < answers.Length; index++)
        {
            handler = new();
            result = await Run(handler, terminal: new Terminal(answers.Take(index).Append(null)));
            Check(result.Exit == 0 && handler.Writes == 0 && result.Output.Contains("Saved account is unchanged"),
                "resource-less cancellation preserves credentials at prompt " + index);
        }
        handler = new() { SecretRequired = false };
        terminal = new(["2", "1", "yes"]);
        result = await Run(handler, terminal: terminal);
        Check(result.Exit == 0 && handler.Saved is { Secret: null, ResourceId: null } && terminal.Prompts.All(prompt => !prompt.Secret),
            "SecretRequired alone controls hidden token entry");
        handler = new() { ResourceRequired = true };
        result = await Run(handler, terminal: new Terminal(["2", "1", "explicit-project", Token, "yes"]));
        Check(result.Exit == 0 && handler.Saved?.ResourceId == "explicit-project", "default required-resource monitor behavior is preserved");
        Check(new MonitorAccessOptionDto("fixture", "Fixture", true, "", "", "", []).ResourceRequired,
            "omitted additive resource capability defaults to required");

        foreach (var suffix in new[] { "", "\n", "\r\n" })
        {
            handler = new();
            result = await Run(handler, ["--token-stdin", "--kind", "management-token", "--revision", "7", "--json"], new StringReader(Token + suffix));
            using var json = JsonDocument.Parse(result.Output);
            Check(result.Exit == 0 && handler.Writes == 1 && handler.Saved?.Secret == Token && handler.Saved.ResourceId is null
                && json.RootElement.GetProperty("accountId").GetString() == "existing" && json.RootElement.GetProperty("state").GetString() == "configured-unverified"
                && !result.Output.Contains(Token) && result.Error == "", "private stdin handles EOF/LF/CRLF and emits public state only");
        }
        foreach (var (arguments, resourceRequired, kind, secretRequired, account, expectedExit) in new (string[], bool, string, bool, string, int)[]
        {
            (["--revision", "6"], false, "management-token", true, "existing", 5),
            ([], false, "management-token", true, "missing", 2),
            ([], false, "unknown-kind", true, "existing", 2),
            (["--resource", "fabricated-self"], false, "management-token", true, "existing", 2),
            ([], true, "management-token", true, "existing", 2),
            ([], false, "management-token", false, "existing", 2),
            ([], false, "access-token-snapshot", true, "existing", 2),
            (["--expires-at", "2020-01-01T00:00:00Z"], false, "management-token", true, "existing", 2),
            (["--expires-at", "2099-01-01T00:00:00"], false, "management-token", true, "existing", 2),
            (["--token", Token], false, "management-token", true, "existing", 2)
        })
        {
            handler = new() { ResourceRequired = resourceRequired, Kind = kind == "unknown-kind" ? "management-token" : kind, SecretRequired = secretRequired };
            var input = new UnreadInput();
            result = await Run(handler, new[] { "--token-stdin", "--kind", kind }.Concat(arguments).ToArray(), input, account: account);
            Check(result.Exit == expectedExit && !input.Read && handler.Writes == 0 && !result.Output.Contains(Token) && !result.Error.Contains(Token),
                "invalid monitor metadata is rejected before consuming private input: " + string.Join(' ', arguments.Select(value => value == Token ? "[private]" : value)));
        }
        foreach (var invalid in new[] { "", "\n", " has-space", "has\ttab", "control\u0001", "unicode\u00a0space", new string('x', 4097), new string('x', 4098) + "\r\n" })
        {
            handler = new();
            result = await Run(handler, ["--token-stdin", "--kind", "management-token"], new StringReader(invalid));
            Check(result.Exit == 2 && handler.Writes == 0 && result.Output == "" && !result.Error.Contains(string.IsNullOrWhiteSpace(invalid) ? Token : invalid),
                "invalid private token is rejected without write or echo");
        }
        foreach (var suffix in new[] { "", "\n", "\r\n" })
        {
            handler = new();
            result = await Run(handler, ["--token-stdin", "--kind", "management-token"], new StringReader(new string('x', 4096) + suffix));
            Check(result.Exit == 0 && handler.Saved!.Secret!.Length == 4096, "bounded token accepts exactly 4096 characters");
        }
        handler = new() { ResourceRequired = true, Kind = "access-token-snapshot" };
        result = await Run(handler, ["--token-stdin", "--kind", "access-token-snapshot", "--resource", "project", "--expires-at", "2099-01-01T00:00:00Z"], new StringReader(Token));
        Check(result.Exit == 0 && handler.Saved is { ResourceId: "project", ExpiresAt: not null }, "stdin snapshot preserves explicit resource and expiry requirements");
        return checks;
    }

    private static async Task<(int Exit, string Output, string Error)> Run(Handler handler, string[]? options = null,
        TextReader? input = null, Terminal? terminal = null, string account = "existing")
    {
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://fixture.invalid/") };
        var output = new StringWriter(); var error = new StringWriter();
        var exit = await CliApplication.RunAsync(new[] { "accounts", "monitor", account }.Concat(options ?? []).ToArray(), output, error,
            () => new LainApiClient(http), input: input, wizardTerminal: terminal);
        return (exit, output.ToString(), error.ToString());
    }
    private sealed class Terminal(IEnumerable<string?> answers) : IAccountWizardTerminal
    {
        private readonly Queue<string?> values = new(answers);
        internal List<(string Prompt, bool Secret)> Prompts { get; } = [];
        public bool IsInteractive => true;
        public Task<string?> ReadAsync(string prompt, bool secret, CancellationToken ct)
        {
            Prompts.Add((prompt, secret));
            return Task.FromResult(values.Dequeue());
        }
    }
    private sealed class UnreadInput : TextReader
    {
        public bool Read { get; private set; }
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken ct = default)
        { Read = true; throw new Exception("Private input must remain unread"); }
    }
    private sealed class Handler : HttpMessageHandler
    {
        public bool ResourceRequired { get; init; }
        public bool SecretRequired { get; init; } = true;
        public string Kind { get; init; } = "management-token";
        public int Writes { get; private set; }
        public MonitorAccessRequest? Saved { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var configuration = new ConfigurationDto(7, 6, [new("existing", "Custom inference account", "fixture", true, "oauth", "ready",
                Generation: 4, Binding: new("fixture", ResourceId: "inference-resource"))], [], []);
            if (request.Method == HttpMethod.Get && path == "/api/v1/configuration") return Ok(configuration);
            if (request.Method == HttpMethod.Get && path == "/api/v1/connection-methods") return Ok(new ConnectionMethodCatalogDto(1, []));
            if (request.Method == HttpMethod.Get && path == "/api/v1/providers") return Ok(new ProviderCatalogDto(1, DateTimeOffset.UtcNow,
                [new("fixture", "Fixture", "Synthetic monitor", "https://example.invalid/account", "https://example.invalid/docs", [], [], [], [], [], [],
                    MonitorAccessOptions: [new(Kind, "Usage reader", SecretRequired, "Project ID", "Quota-only access", "https://example.invalid/usage", ["usage:read"], ResourceRequired: ResourceRequired)])]));
            if (path == "/api/v1/accounts/existing/monitor")
            {
                if (request.Method == HttpMethod.Get) return Ok(new MonitorAccessDto("existing", "fixture", "not-configured", ResourceId: "previous-monitor-resource"));
                if (request.Method == HttpMethod.Put)
                {
                    var envelope = await request.Content!.ReadFromJsonAsync<RevisionRequest<MonitorAccessRequest>>(cancellationToken: ct);
                    if (envelope!.Revision != 7) throw new Exception("Wrong monitor revision");
                    Writes++; Saved = envelope.Value;
                    return Ok(configuration with { Revision = 8 });
                }
            }
            throw new Exception("Unexpected request: monitor setup must not connect, refresh, apply or call providers");
        }
        private static HttpResponseMessage Ok(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value, value.GetType()) };
    }
}
