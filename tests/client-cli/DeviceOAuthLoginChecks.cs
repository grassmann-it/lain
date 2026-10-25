using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lain.Client;
using Lain.Cli;
using Lain.Core;

internal static class DeviceOAuthLoginChecks
{
    private const string Code = "ABCD-EFGH";
    private const string Verification = "https://example.invalid/device";
    private const string CompleteUrl = "https://example.invalid/device?user_code=ABCD-EFGH";
    private const string ProviderSecret = "SYNTHETIC-PROVIDER-DEVICE-SECRET";

    internal static async Task<int> RunAsync()
    {
        var checks = 0;
        void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
        foreach (var status in new[] { false, true })
        foreach (var url in new string?[] { null, "", CompleteUrl })
        {
            using var handler = new ResponseHandler(request =>
            {
                RequireLoginRequest(request, status);
                // device_code is provider-only and is never part of LoginDto.
                return Ok(new { id = "device-login", provider = "fixture", state = "waiting", url,
                    userCode = Code, verificationUri = Verification, device_code = ProviderSecret });
            });
            var output = new StringWriter(); var error = new StringWriter();
            var exit = await RunCommand(status, false, handler, output, error);
            var lines = output.ToString().Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
            Check(exit == 0 && error.ToString() == "" && lines.Contains("User code: " + Code),
                "human login and login-status show a separate user-code line");
            Check(lines.Contains(string.IsNullOrEmpty(url) ? Verification : CompleteUrl)
                && (string.IsNullOrEmpty(url) || !lines.Contains(Verification)),
                "human device instructions fall back to verification URI while retaining a complete URL");
            Check(!output.ToString().Contains(ProviderSecret) && !output.ToString().Contains("device_code"),
                "human login never exposes the provider device credential");
        }
        foreach (var status in new[] { false, true })
        {
            using var handler = new ResponseHandler(request =>
            {
                RequireLoginRequest(request, status);
                return Ok(new { id = "device-login", provider = "fixture", state = "authorizing",
                    userCode = Code, verificationUri = Verification, device_code = ProviderSecret });
            });
            var output = new StringWriter(); var error = new StringWriter();
            var exit = await RunCommand(status, true, handler, output, error);
            using var document = JsonDocument.Parse(output.ToString());
            Check(exit == 0 && error.ToString() == "" && document.RootElement.GetProperty("userCode").GetString() == Code
                && document.RootElement.GetProperty("verificationUri").GetString() == Verification,
                "login JSON retains the additive public device instructions");
            Check(!document.RootElement.TryGetProperty("device_code", out _) && !output.ToString().Contains(ProviderSecret),
                "typed login JSON excludes unknown provider credentials");
        }
        using (var handler = new ResponseHandler(_ => Ok(new LoginDto("device-login", "fixture", "completed"))))
        {
            var output = new StringWriter(); var error = new StringWriter();
            var exit = await RunCommand(true, false, handler, output, error);
            Check(exit == 0 && !output.ToString().Contains("User code:") && !output.ToString().Contains(Verification),
                "terminal login without instructions does not leave a stale code or link in human output");
        }

        using (var handler = new WizardHandler())
        {
            var output = new StringWriter(); var error = new StringWriter();
            var terminal = new Terminal(["1", "", "yes", "", "", "1"], output);
            var exit = await RunWizard(terminal, handler, output, error);
            Check(exit == 0 && error.ToString() == "" && handler.Polls == 2 && handler.Resources == 1 && handler.Cancels == 0,
                "wizard polls waiting and authorizing before selecting the verified resource");
            Check(terminal.PollPrompts == 2 && terminal.CodeVisibleBeforeEveryPoll && terminal.SecretPrompts == 0
                && output.ToString().Split("User code: " + Code, StringSplitOptions.None).Length == 3,
                "wizard displays the public code and URI before each non-secret provider check");
            Check(handler.Selected is { Revision: 7, Binding.ResourceId: "project-one" }
                && handler.Login is { Name: "Fixture", Purposes: ["quota"] },
                "device polling preserves captured revision, explicit resource and requested login purpose");
            Check(handler.Requests.All(request => request is "GET /api/v1/connection-methods" or "GET /api/v1/configuration"
                or "POST /api/v1/accounts/login" or "GET /api/v1/accounts/login/device-login"
                or "POST /api/v1/accounts/login/device-login/resource" or "GET /api/v1/quotas"),
                "device wizard stays inside its existing local login/resource allowlist");
        }
        foreach (var state in new[] { "waiting", "authorizing" })
        {
            using var handler = new WizardHandler { InitialState = state };
            var output = new StringWriter(); var error = new StringWriter();
            var terminal = new Terminal(["1", "", "yes", null], output);
            var exit = await RunWizard(terminal, handler, output, error);
            Check(exit == 0 && error.ToString() == "" && handler.Cancels == 1 && handler.Polls == 0
                && handler.Resources == 0 && output.ToString().Contains("No account was saved"),
                "cancelling " + state + " still cancels only the active local login");
            Check(terminal.CodeVisibleBeforeEveryPoll && terminal.PollPrompts == 1 && terminal.SecretPrompts == 0,
                "cancel remains available while the device code is visibly displayed in " + state);
        }
        return checks;
    }

    private static void RequireLoginRequest(HttpRequestMessage request, bool status)
    {
        if (request.Method != (status ? HttpMethod.Get : HttpMethod.Post)
            || request.RequestUri!.AbsolutePath != (status ? "/api/v1/accounts/login/device-login" : "/api/v1/accounts/login"))
            throw new Exception("Unexpected device login display request.");
    }
    private static async Task<int> RunCommand(bool status, bool json, HttpMessageHandler handler, TextWriter output, TextWriter error)
    {
        using var http = new HttpClient(handler, disposeHandler: false) { BaseAddress = new("http://fixture.invalid/") };
        var args = status ? new[] { "accounts", "login-status", "device-login" }
            : new[] { "accounts", "login", "fixture", "--revision", "7" };
        if (json) args = [.. args, "--json"];
        return await CliApplication.RunAsync(args, output, error, () => new LainApiClient(http));
    }
    private static async Task<int> RunWizard(IAccountWizardTerminal terminal, WizardHandler handler, TextWriter output, TextWriter error)
    {
        using var http = new HttpClient(handler, disposeHandler: false) { BaseAddress = new("http://fixture.invalid/") };
        return await CliApplication.RunAsync(["accounts", "add"], output, error, () => new LainApiClient(http), wizardTerminal: terminal);
    }
    private sealed class Terminal(IEnumerable<string?> answers, StringWriter output) : IAccountWizardTerminal
    {
        private readonly Queue<string?> pending = new(answers);
        public bool IsInteractive => true;
        public int SecretPrompts { get; private set; }
        public int PollPrompts { get; private set; }
        public bool CodeVisibleBeforeEveryPoll { get; private set; } = true;
        public Task<string?> ReadAsync(string prompt, bool secret, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (secret) SecretPrompts++;
            if (prompt.StartsWith("Complete the provider step", StringComparison.Ordinal))
            {
                PollPrompts++;
                CodeVisibleBeforeEveryPoll &= output.ToString().Contains("User code: " + Code)
                    && output.ToString().Contains("Continue in your browser: " + Verification);
            }
            return Task.FromResult(pending.Dequeue());
        }
    }
    private sealed class ResponseHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request));
    }
    private sealed class WizardHandler : HttpMessageHandler
    {
        public string InitialState { get; init; } = "waiting";
        public int Polls { get; private set; }
        public int Resources { get; private set; }
        public int Cancels { get; private set; }
        public LoginRequest? Login { get; private set; }
        public ResourceSelectionRequest? Selected { get; private set; }
        public List<string> Requests { get; } = [];
        private bool completed;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(request.Method + " " + path);
            if (request.Method == HttpMethod.Get && path == "/api/v1/connection-methods")
                return Ok(new ConnectionMethodCatalogDto(1, [new("fixture", "device-oauth", "Device login", "device", ["quota"], "available", false, false)]));
            if (request.Method == HttpMethod.Get && path == "/api/v1/configuration")
                return Ok(new ConfigurationDto(7, 7, completed ? [new("connected", "Fixture", "fixture", true, "device-oauth", "ready")] : [], [], []));
            if (request.Method == HttpMethod.Get && path == "/api/v1/quotas") return Ok(Array.Empty<QuotaDto>());
            if (request.Method == HttpMethod.Post && path == "/api/v1/accounts/login")
            {
                Login = await request.Content!.ReadFromJsonAsync<LoginRequest>(cancellationToken: ct);
                return Ok(Waiting(InitialState));
            }
            if (request.Method == HttpMethod.Get && path == "/api/v1/accounts/login/device-login")
            {
                Polls++;
                return Ok(Polls == 1 ? Waiting("authorizing") : new LoginDto("device-login", "fixture", "resource-required",
                    Resources: [new("fixture", Product: "usage", Region: "test", ResourceType: "project", ResourceId: "project-one")]));
            }
            if (request.Method == HttpMethod.Post && path == "/api/v1/accounts/login/device-login/resource")
            {
                Resources++;
                Selected = await request.Content!.ReadFromJsonAsync<ResourceSelectionRequest>(cancellationToken: ct);
                completed = true;
                return Ok(new LoginDto("device-login", "fixture", "completed", AccountId: "connected"));
            }
            if (request.Method == HttpMethod.Delete && path == "/api/v1/accounts/login/device-login")
            {
                Cancels++;
                return Ok(new LoginDto("device-login", "fixture", "cancelled"));
            }
            throw new Exception("Unexpected device wizard request.");
        }
        private static LoginDto Waiting(string state) => new("device-login", "fixture", state, UserCode: Code, VerificationUri: Verification);
    }
    private static HttpResponseMessage Ok(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value, value.GetType()) };
}
