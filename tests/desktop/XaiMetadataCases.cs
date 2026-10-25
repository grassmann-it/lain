using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Lain.Client;
using Lain.Core;
using Lain.Desktop;

internal sealed partial class Scenario
{
    public async Task RunXaiMetadataSmoke()
    {
        Directory.CreateDirectory(Output);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(MainWindow);
        var xai = new AccountDto("xai-ui", "xAI UI fixture", "xai", true, "api-key", "unverified", Generation: 7);
        var other = new AccountDto("other-ui", "Other UI fixture", "openrouter", true, "api-key", "unverified");
        fixture.Config = new(12, 11, [xai, other], [], []);
        fixture.QuotaOverride = [];
        var handler = new XaiMetadataUiHandler(fixture);
        var http = new HttpClient(handler) { BaseAddress = new("http://fixture.invalid") };
        transports.Add(http);
        window = new MainWindow(new LainApiClient(http), Path.Combine(Output, "xai-ui-preferences.json")) { Width = 1120, Height = 780 };
        lifetime.MainWindow = window;
        window.Show();
        await Wait(() => HasText(xai.Name), "xAI fixture account appears");
        void Inspect(string id) => type.GetMethod("ShowAccountInspector", flags)!.Invoke(window, [id, null, false, null]);
        Array Commands(AccountDto account, long revision) => (Array)type.GetMethod("AccountCommands", flags)!.Invoke(window, [account, revision, null])!;
        static object? Find(Array commands, string key) => commands.Cast<object>().SingleOrDefault(c => Equals(c.GetType().GetProperty("Key")!.GetValue(c), key));
        static void Execute(object command) => ((Action)command.GetType().GetProperty("Execute")!.GetValue(command)!)();
        Inspect(xai.Id);
        await Wait(() => HasText("Team identity has not been discovered."), "xAI detail explains missing team identity");
        Check(handler.MetadataCalls == 0 && Controls<Button>().Any(b => Equals(b.Content, "Discover team") && b.IsEnabled),
            "opening account details exposes enabled Discover team without an automatic request");
        Check(Find(Commands(xai, 12), "discover-team") is not null && Find(Commands(other, 12), "discover-team") is null,
            "context command exists only for xAI");
        await CaptureBoth("xai-discover-before");
        await Click("Discover team");
        await Wait(() => handler.MetadataCalls == 1 && HasText("Team: actual-ui-team"), "visible Discover team click displays returned team");
        Check(handler.Request is { Revision: 12, Value.ExpectedGeneration: 7 }, "visible action sends frozen account revision and generation");
        Check(fixture.Config.Accounts.Single(a => a.Id == xai.Id).Generation == 8 && fixture.Config.Routes.Length == 0
            && fixture.Config.Profiles.Length == 0 && fixture.ApplyCalls == 0 && fixture.LoginBegins == 0,
            "discovery keeps stable account identity without login, route or Apply");
        Check(HasText("xAI team identity checked. No budget was read."), "completion explains identity-only result");
        await CaptureBoth("xai-discover-after");

        var current = fixture.Config.Accounts.Single(a => a.Id == xai.Id);
        var frozen = Find(Commands(current, fixture.Config.Revision), "discover-team")!;
        type.GetField("_configuration", flags)!.SetValue(window, fixture.Config with { Revision = fixture.Config.Revision + 1 });
        Execute(frozen);
        Check(handler.MetadataCalls == 1 && HasText("Configuration changed while these actions were open. Reopen account details to review the current account before changing it."),
            "stale context action rejects locally instead of retargeting a network request");
        type.GetField("_configuration", flags)!.SetValue(window, fixture.Config);
        type.GetField("_hostAvailable", flags)!.SetValue(window, false);
        Execute(Find(Commands(current, fixture.Config.Revision), "discover-team")!);
        Check(handler.MetadataCalls == 1 && HasText("The host is offline. The last known account remains visible; reload status before changing it."),
            "offline context action sends no request");
        type.GetField("_hostAvailable", flags)!.SetValue(window, true);
        var disabled = Find(Commands(current with { Enabled = false }, fixture.Config.Revision), "discover-team")!;
        Check(!(bool)disabled.GetType().GetProperty("Available")!.GetValue(disabled)!, "disabled xAI account cannot discover its team");
        await File.WriteAllTextAsync(Path.Combine(Output, "xai-metadata-evidence.json"), JsonSerializer.Serialize(new
        {
            checks, screenshots, handler.MetadataCalls,
            scope = "Synthetic UI HTTP only; no real provider, credential, login or installed host access."
        }, new JsonSerializerOptions { WriteIndented = true }));
        window.Close();
    }

    private sealed class XaiMetadataUiHandler(Fixture fixture) : DelegatingHandler(fixture)
    {
        public int MetadataCalls;
        public RevisionRequest<AccountMetadataDiscoveryRequest>? Request;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == Api.Prefix + "/accounts/xai-ui/metadata/xai")
            {
                MetadataCalls++;
                Request = await request.Content!.ReadFromJsonAsync<RevisionRequest<AccountMetadataDiscoveryRequest>>(cancellationToken: ct);
                var target = fixture.Config.Accounts.Single(a => a.Id == "xai-ui");
                if (Request is null || Request.Revision != fixture.Config.Revision || Request.Value.ExpectedGeneration != target.Generation)
                    return new(HttpStatusCode.Conflict) { Content = JsonContent.Create(new ApiError("revision_conflict", "Synthetic stale discovery")) };
                fixture.Config = fixture.Config with
                {
                    Revision = fixture.Config.Revision + 1,
                    Accounts = fixture.Config.Accounts.Select(a => a.Id == target.Id ? a with
                    {
                        Generation = a.Generation + 1,
                        Binding = new("xai", ResourceType: "team", ResourceId: "actual-ui-team", Provenance: "provider-discovery")
                    } : a).ToArray()
                };
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(fixture.Config) };
            }
            return await base.SendAsync(request, ct);
        }
    }
}
