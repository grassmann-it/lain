using System.Globalization;
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
    public async Task RunEc4OnboardingCases()
    {
        Directory.CreateDirectory(Output);
        var account = fixture.Config.Accounts[0] with
        {
            Name = "My existing NanoGPT subscription", Provider = "nanogpt", Generation = 7,
            Binding = new("nanogpt", ResourceId: "EXISTING-BINDING-DO-NOT-SEND"),
            Capabilities = [new("inference", "unverified")]
        };
        fixture.Config = fixture.Config with { Accounts = [account], Routes = [], Profiles = [] };
        fixture.QuotaOverride = [];
        var template = new ProviderRouteDescriptorDto("nano-template", "fixture-model", "Synthetic existing model",
            "https://example.invalid/v1", "openai-chat", ["existing"], new());
        var provider = new ProviderDescriptorDto("nanogpt", "NanoGPT", "Synthetic existing subscription",
            "https://example.invalid/account", "https://example.invalid/docs", [], [],
            [new("existing", "Existing subscription", "subscription", "account", "requests")], [template], [], [],
            Connections: [new("nano-connection", "NanoGPT inference", "https://example.invalid/v1", "openai-chat", ["existing"], [template.Id])],
            MonitorAccessOptions:
            [
                new("api-token", "Separate NanoGPT usage access", true, "Account ID", "No selector is used for this self-account reader.",
                    "https://example.invalid/usage-token", ["usage:read"], "Separate quota-only management token. The OAuth inference key cannot read subscription usage.", ResourceRequired: false),
                // A separate synthetic option exercises backward-compatible required-resource behavior.
                new("inference-key", "Synthetic resource-bound reader", false, "Account ID", "Choose the matching synthetic resource.",
                    "https://example.invalid/resource", [])
            ]);
        fixture.CatalogOverride = new(1, DateTimeOffset.UtcNow, [provider]);
        var oauth = new ProviderConnectionMethodDto("nanogpt", "oauth", "Sign in to NanoGPT", "browser", ["inference"], "available", false, false);
        var keyMethod = new ProviderConnectionMethodDto("deepseek", "api-key", "Use existing DeepSeek key", "api-key", ["inference", "quota"],
            "available", false, true, "api-key");
        fixture.ConnectionMethodsOverride = new(1, [oauth, keyMethod]);
        var http = new HttpClient(fixture, disposeHandler: false) { BaseAddress = new("http://fixture.invalid") };
        transports.Add(http);
        window = new MainWindow(new LainApiClient(http), Path.Combine(Output, "ec4-preferences.json")) { Width = 1120, Height = 780 };
        lifetime.MainWindow = window; window.Show();
        await Wait(() => HasText("ready", true), "EC4 synthetic desktop ready");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Invoke(string method, params object?[] args) => typeof(MainWindow).GetMethod(method, flags)!.Invoke(window, args);
        void Set(string field, object? value) => typeof(MainWindow).GetField(field, flags)!.SetValue(window, value);
        var expectedScale = double.Parse(Environment.GetEnvironmentVariable("LAIN_TEST_SCALE") ?? "1", CultureInfo.InvariantCulture);
        Check(Math.Abs(window.RenderScaling - expectedScale) < .001, "EC4 actual render scale matches the requested private runner scale");
        Set("_providerCatalog", fixture.CatalogOverride); Set("_connectionMethods", fixture.ConnectionMethodsOverride);

        Invoke("ShowConnectionMethod", oauth, account, false);
        await Wait(() => HasText("Sign in to NanoGPT"), "EC4 OAuth review mounted");
        Check(Named<TextBox>("Account name").Text == account.Name
            && !Controls<TextBox>().Any(box => box.PasswordChar == '●')
            && HasText("This connection is for model requests.", true),
            "inference-only OAuth preserves the name and explains separate quota access without requesting a private token");
        await Click("Cancel");
        Check(fixture.LoginBegins == 0 && fixture.ConnectCalls == 0, "reviewing and cancelling OAuth starts no login or credential write");

        void Monitor(string? savedResource = null) => Invoke("BuildMonitorEditor", account, provider,
            new MonitorAccessDto(account.Id, "nanogpt", "not-configured", ResourceId: savedResource));
        Monitor();
        await Wait(() => Controls<TextBox>().Any(box => AutomationProperties.GetName(box) == "Monitoring token"), "EC4 separate monitor token mounted");
        Check(!Controls<TextBox>().Any(box => AutomationProperties.GetName(box) is "Account ID" or "Resource ID")
            && Named<TextBox>("Monitoring token").PasswordChar == '●'
            && HasText("Required read permissions: usage:read")
            && HasText("Monitoring access is for quota and usage reads.", true),
            "resource-free usage reader shows only private read-token setup with no account selector");
        await CaptureBoth("ec4-nanogpt-monitor", () => Named<TextBox>("Monitoring token").BringIntoView());
        await Click("Save monitoring access");
        Check(fixture.LastMonitorAccess is null && HasText("Enter your own monitoring token without whitespace.", true),
            "required monitor token remains required when resource selection is disabled");
        var secret = Named<TextBox>("Monitoring token"); secret.Text = "SYNTHETIC-EC4-UNSAVED";
        Named<ComboBox>("Access method").SelectedIndex = 1;
        await Wait(() => Controls<TextBox>().Any(box => AutomationProperties.GetName(box) == "Account ID"), "EC4 legacy resource input mounted");
        Check(secret.Text == "" && !Controls<TextBox>().Any(box => AutomationProperties.GetName(box) == "Monitoring token"),
            "changing to saved inference access clears the private token and requests no replacement token");
        Named<TextBox>("Account ID").Text = "";
        await Click("Save monitoring access");
        Check(fixture.LastMonitorAccess is null && HasText("Enter Account ID.", true), "default ResourceRequired preserves required ID validation");
        Named<TextBox>("Account ID").Text = "  synthetic-resource  ";
        await Click("Save monitoring access");
        await Wait(() => fixture.LastMonitorAccess is not null, "EC4 required-resource save reached synthetic host");
        Check(fixture.LastMonitorAccess is { Secret: null, ResourceId: "synthetic-resource" },
            "saved-access option sends its resource without any private token");

        Monitor("SAVED-MONITOR-RESOURCE-DO-NOT-SEND");
        await Wait(() => Controls<TextBox>().Any(box => AutomationProperties.GetName(box) == "Monitoring token"), "EC4 self-account monitor remounted");
        secret = Named<TextBox>("Monitoring token"); secret.Text = "SYNTHETIC-EC4-MONITOR";
        var calls = fixture.Requests.Count;
        await Click("Save monitoring access");
        await Wait(() => fixture.LastMonitorAccess?.Kind == "api-token", "EC4 resource-free monitor saved without ID validation");
        Check(fixture.LastMonitorAccess is { ResourceId: null, Secret: "SYNTHETIC-EC4-MONITOR" }
            && fixture.LastMonitorAccount == account.Id && secret.Text == "",
            "self-account monitor sends ResourceId null despite old saved/binding IDs and clears its token after save");
        Check(!fixture.Requests.Skip(calls).Any(request => request == "POST /api/v1/quotas/refresh")
            && fixture.Config.Accounts.Single() == account && fixture.Config.Routes.Length == 0,
            "saving read access preserves the account identity, generation, binding and models without an implicit quota request");
        Monitor();
        secret = Named<TextBox>("Monitoring token"); secret.Text = "SYNTHETIC-EC4-CANCELLED";
        var writes = fixture.Requests.Count(request => request.StartsWith("PUT ", StringComparison.Ordinal));
        await Click("Cancel");
        Check(secret.Text == "" && fixture.Requests.Count(request => request.StartsWith("PUT ", StringComparison.Ordinal)) == writes,
            "cancelling private read-token input clears it without a write");

        Set("_editing", true);
        await (Task)Invoke("ShowConnectionResultAsync", account.Id)!;
        await Wait(() => Controls<Button>().Any(button => Equals(button.Content, "Choose models (optional)")), "EC4 optional model action mounted");
        Check(fixture.Config.Routes.Length == 0 && Controls<Button>().Any(button => Equals(button.Content, "Done")),
            "a saved inference account can finish without choosing model templates");
        await Click("Choose models (optional)");
        await Wait(() => Controls<CheckBox>().Any(box => AutomationProperties.GetName(box) == "Choose provider model nano-template"), "EC4 optional templates mounted");
        Check(Named<CheckBox>("Choose provider model nano-template").IsChecked == false
            && !Controls<TextBox>().Any(box => box.PasswordChar == '●'), "model templates start unchecked and reuse saved inference access without another private token");
        await Click("Cancel"); await Click("Accounts");
        Invoke("ShowConnectionMethod", keyMethod, null, false);
        await Wait(() => Controls<TextBox>().Any(box => AutomationProperties.GetName(box) == "api-key"), "EC4 private API-key alternative mounted");
        var apiKey = Named<TextBox>("api-key"); apiKey.Text = "SYNTHETIC-EC4-PRIVATE-KEY";
        Check(apiKey.PasswordChar == '●', "manual API-key alternatives remain private");
        await Click("Cancel");
        Check(apiKey.Text == "" && fixture.LoginBegins == 0 && fixture.ConnectCalls == 0
            && fixture.ApplyCalls == 0 && fixture.Unexpected.Count == 0 && fixture.Config.Routes.Length == 0,
            "EC4 review/cancel flows perform no login, inference credential write, model attachment, Apply or external request");
        await File.WriteAllTextAsync(Path.Combine(Output, "ec4-onboarding-evidence.json"), JsonSerializer.Serialize(new
        {
            result = "PASS", checks, screenshots, requests = fixture.Requests, expectedScale, renderScaling = window.RenderScaling,
            loadedAssemblies = new[] { typeof(MainWindow).Assembly, typeof(LainApiClient).Assembly, typeof(MonitorAccessOptionDto).Assembly }
                .Select(assembly => new { name = assembly.GetName().Name, path = assembly.Location,
                    sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant() }),
            scope = "Production Desktop controls, in-memory HTTP only; synthetic local token values. No provider, browser, real host, credential store, inference or profile operation."
        }, new JsonSerializerOptions { WriteIndented = true }));
        window.Close(); foreach (var transport in transports) transport.Dispose();
    }
}
