using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Lain.Client;
using Lain.Core;
using Lain.Desktop;

internal sealed partial class Scenario
{
    public async Task RunOauthDeviceDisplayCases()
    {
        Directory.CreateDirectory(Output);
        fixture.HoldLogin = true;
        var http = new HttpClient(fixture, disposeHandler: false) { BaseAddress = new("http://fixture.invalid") };
        transports.Add(http);
        window = new MainWindow(new LainApiClient(http), Path.Combine(Output, "oauth-device-preferences.json")) { Width = 1120, Height = 780 };
        lifetime.MainWindow = window; window.Show();
        await Wait(() => HasText("ready", true), "device display fixture ready");
        var requestedScale = double.Parse(Environment.GetEnvironmentVariable("LAIN_TEST_SCALE") ?? "1", CultureInfo.InvariantCulture);
        Check(Math.Abs(window.RenderScaling - requestedScale) < 0.001,
            $"device display actual RenderScaling {window.RenderScaling} matches requested {requestedScale}");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(MainWindow);
        object? Invoke(string method, params object?[] args) => type.GetMethod(method, flags)!.Invoke(window, args);
        void Set(string field, object? value) => type.GetField(field, flags)!.SetValue(window, value);
        var opened = new List<string>();
        Set("_connectionBrowser", (Func<Uri, Task<bool>>)(uri => { opened.Add(uri.AbsoluteUri); return Task.FromResult(true); }));
        TextBox Code() => Named<TextBox>("Authorization user code");
        Expander Manual() => Controls<Expander>().Single(control => Equals(control.Header, "Use the authorization link manually"));
        bool NoInstructions() => !Controls<TextBox>().Any(control => AutomationProperties.GetName(control) is
            "Authorization user code" or "Authorization link" or "Authorization code entry link")
            && !Controls<Button>().Any(control => Equals(control.Content, "Reopen browser"));
        async Task Begin(LoginDto login, bool extraPrivateFields = false)
        {
            var response = fixture.Defer("POST", "/api/v1/accounts/login");
            var started = (Task)Invoke("StartConnectionLoginAsync", new LoginRequest(fixture.Config.Revision, "codex", "browser", ["quota"]))!;
            await response.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (extraPrivateFields)
                response.Response.TrySetResult(new(HttpStatusCode.OK) { Content = JsonContent.Create(new
                {
                    login.Id, login.Provider, login.State, login.Url, login.UserCode, login.VerificationUri,
                    Error = "SYNTHETIC-PRIVATE-ERROR", device_code = "SYNTHETIC-PRIVATE-DEVICE-CODE", access_token = "SYNTHETIC-PRIVATE-TOKEN"
                }) });
            else response.Complete(login);
            await started;
            await Wait(() => Controls<TextBox>().Any(control => AutomationProperties.GetName(control) == "Authorization user code"), "device user code mounted");
        }
        async Task StopPolling()
        {
            (type.GetField("_loginPolling", flags)!.GetValue(window) as CancellationTokenSource)?.Cancel();
            await Wait(() => type.GetField("_loginPolling", flags)!.GetValue(window) is null, "device fixture polling stopped");
        }

        const string path = "/api/v1/accounts/login/device-display";
        var login = new LoginDto("device-display", "codex", "authorizing", UserCode: "ABCD-EFGH", VerificationUri: "https://example.invalid/device");
        var codePoll = fixture.Defer("GET", path);
        await Begin(login, extraPrivateFields: true);
        var code = Code(); code.SelectAll();
        Check(code.IsReadOnly && code.SelectedText == login.UserCode && code.FontSize >= 20
            && AutomationProperties.GetHelpText(code)?.Contains("copy", StringComparison.OrdinalIgnoreCase) == true,
            "authorizing shows a readable readonly selectable code with accessible copy guidance");
        Check(opened.SequenceEqual(new[] { login.VerificationUri! }) && HasText("Waiting for authorization"),
            "verification URI opens through the existing browser path when Url is absent");
        Check(!Controls<TextBox>().Any(control => control.Text?.Contains("SYNTHETIC-PRIVATE", StringComparison.Ordinal) == true)
            && !HasText("SYNTHETIC-PRIVATE", true), "provider device codes, tokens and raw errors are never displayed");
        Manual().IsExpanded = true;
        await Wait(() => Controls<TextBox>().Any(control => AutomationProperties.GetName(control) == "Authorization link"), "fallback manual authorization link mounted");
        var fallback = Named<TextBox>("Authorization link");
        Check(fallback.IsReadOnly && fallback.Text == login.VerificationUri, "verification fallback is copyable in manual help");
        Manual().IsExpanded = false;
        Check(fallback.Text == "", "collapsing manual help clears its link text");
        await CaptureBoth("oauth-device-code");

        var uriPoll = fixture.Defer("GET", path);
        await codePoll.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        login = login with { UserCode = "IJKL-MNOP" }; codePoll.Complete(login);
        await Wait(() => Controls<TextBox>().Any(control => AutomationProperties.GetName(control) == "Authorization user code" && control.Text == login.UserCode),
            "code-only polling change refreshes the pending display");
        var urlPoll = fixture.Defer("GET", path);
        await uriPoll.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var previousCode = Code();
        login = login with { VerificationUri = "https://example.invalid/device-next" }; uriPoll.Complete(login);
        await Wait(() => !ReferenceEquals(Code(), previousCode) && Code().Text == login.UserCode, "verification-only polling change refreshes the pending display");
        await Click("Reopen browser");
        await Wait(() => opened.Last() == login.VerificationUri, "verification-only polling change updates browser fallback");

        var terminalPoll = fixture.Defer("GET", path);
        await urlPoll.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        previousCode = Code();
        login = login with { Url = "https://example.invalid/authorize?fixture=complete" }; urlPoll.Complete(login);
        await Wait(() => !ReferenceEquals(Code(), previousCode), "primary authorization link page mounted");
        await Click("Reopen browser");
        await Wait(() => opened.Last() == login.Url, "primary Url keeps precedence over verification URI");
        Manual().IsExpanded = true;
        await Wait(() => Controls<TextBox>().Any(control => AutomationProperties.GetName(control) == "Authorization code entry link"), "manual code-entry help mounted");
        Check(Named<TextBox>("Authorization link").Text == login.Url
            && Named<TextBox>("Authorization code entry link").Text == login.VerificationUri,
            "manual help offers primary link and meaningful separate code-entry page");

        await terminalPoll.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.ConfigurationFailure = true; Set("_busy", true);
        terminalPoll.Complete(login with { State = "completed" });
        await Wait(() => HasText("Connected") && NoInstructions(), "terminal completion hides all instructions while busy before configuration reload");
        await Wait(() => type.GetField("_loginError", flags)!.GetValue(window) is not null, "synthetic completion configuration read fails");
        Check(NoInstructions(), "configuration failure cannot restore completed device instructions");
        Set("_busy", false); fixture.ConfigurationFailure = false; await StopPolling();

        foreach (var state in new[] { "completed", "cancelled", "failed", "expired", "unsupported" })
        {
            Set("_login", login with { State = state }); Invoke("ShowLoginPage");
            Check(NoInstructions(), state + " never displays retained code or links");
        }

        fixture.Defer("GET", path);
        await Begin(login with { State = "pending" });
        var cancel = fixture.Defer("DELETE", path);
        await Click("Cancel sign-in"); await cancel.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.ConfigurationFailure = true;
        cancel.Complete(login with { State = "cancelled" });
        await Wait(() => NoInstructions(), "cancel response clears instructions even when account refresh fails");
        fixture.ConfigurationFailure = false;
        await StopPolling();
        Check(fixture.ApplyCalls == 0 && fixture.Unexpected.Count == 0,
            "device display fixture performs no Apply, real host, credential or provider operation");
        await File.WriteAllTextAsync(Path.Combine(Output, "oauth-device-display-evidence.json"), JsonSerializer.Serialize(new
        {
            result = "PASS", checks, screenshots, requests = fixture.Requests, opened,
            requestedScale, renderScaling = window.RenderScaling,
            assembly = typeof(MainWindow).Assembly.Location,
            assemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location))).ToLowerInvariant(),
            scope = "Production MainWindow with in-memory HTTP and injected browser; synthetic public instructions only. No external browser, provider, credentials or host."
        }, new JsonSerializerOptions { WriteIndented = true }));
        window.Close(); foreach (var transport in transports) transport.Dispose();
    }
}
