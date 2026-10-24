using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Lain.Client;
using Lain.Core;
using Lain.Desktop;

internal sealed partial class Scenario
{
    // Hook owned by UXD-22: run instead of the full legacy Scenario.Run().
    public async Task RunAccountSnapshotsSmoke()
    {
        Directory.CreateDirectory(Output);
        var original = fixture.Config;
        var observed = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        QuotaDto Weekly(string id, double used) => new(id, null, "fixture#weekly", "ok", observed, observed,
            new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero), "percent", used, 100, 100 - used, false,
            WindowLabel: "7 days", UsedPercent: used);
        var a = original.Accounts[0]; var b = original.Accounts[1];
        var aWeek = Weekly(a.Id, 37); var bWeek = Weekly(b.Id, 62);
        var unknown = new QuotaDto(a.Id, null, "fixture#unknown", "unknown", observed, null, null,
            "tokens", null, null, null, false, WindowLabel: "Unknown window");
        var balance = new QuotaDto(a.Id, null, "fixture#balance", "ok", observed, observed, null,
            "USD", null, null, 12, false, WindowLabel: "Credit balance", MeasurementKind: "balance");

        async Task<Uxd18AccountHandler> OpenGated(bool fail = false)
        {
            var handler = new Uxd18AccountHandler(fixture) { ConfigurationFailure = fail };
            var http = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://fixture.invalid") };
            transports.Add(http);
            window = new MainWindow(new LainApiClient(http), Path.Combine(Output, "uxd18-preferences.json")) { Width = 1120, Height = 780 };
            lifetime.MainWindow = window; window.Show();
            await Wait(() => handler.ConfigurationRequested, "UXD18 first configuration request is held");
            Check(HasText("Loading account inventory…") && !HasText("No accounts configured")
                && !HasText("Account inventory could not be loaded") && Controls<ProgressBar>().Length == 0,
                "UXD18 pending inventory claims neither empty data nor host failure or zero percent");
            return handler;
        }

        fixture.Config = original with { Accounts = [], Routes = [], Profiles = [] };
        var empty = await OpenGated();
        await CaptureBoth("uxd18-accounts-first-pending");
        empty.ReleaseConfiguration();
        await Wait(() => HasText("No accounts configured"), "UXD18 successful empty inventory has its own next step");
        await CaptureBoth("uxd18-accounts-empty"); window.Close();

        var failed = await OpenGated(fail: true); failed.ReleaseConfiguration();
        await Wait(() => HasText("Account inventory could not be loaded"), "UXD18 failed first request has retry guidance");
        Check(!HasText("No accounts configured"), "UXD18 failed first request does not impersonate empty inventory");
        await CaptureBoth("uxd18-accounts-first-error"); window.Close();

        fixture.Config = original with { Accounts = [a, b, original.Accounts[2] with { Name = "Long fixture account name that must truncate without displacing refresh or neighboring account actions" }], Routes = [], Profiles = [] };
        fixture.AddDensityAccounts(24);
        fixture.QuotaOverride = [aWeek, unknown, balance, bWeek];
        var live = await OpenGated(); live.ReleaseConfiguration();
        await Wait(() => HasText("37% used") && HasText("62% used"), "UXD18 delayed first data snapshot becomes visible");
        var aCard = Named<Border>("Account budgets for " + a.Name);
        Check(aCard.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "7 days")
            && aCard.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Unknown window")
            && aCard.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Credit balance")
            && aCard.GetVisualDescendants().OfType<ProgressBar>().Count() == 1,
            "UXD18 closed account retains every known window; unknown and balance-only do not invent a percent bar");
        await Click("Open account details for " + a.Name);
        await ResizeViewport(1120, 780);
        var bars = Controls<ProgressBar>().Where(bar => AutomationProperties.GetName(bar) == a.Name + " · 7 days · consumed").ToArray();
        Check(bars.Length == 2 && bars.All(bar => bar.Value == 37), "UXD18 list and detail both show 37 percent consumed");
        var tileBounds = aCard.Bounds;
        var inventory = Named<ScrollViewer>("Account inventory"); var listOffset = inventory.Offset;
        await Click("Refresh quota for " + a.Name);
        await Wait(() => live.RefreshRequests.Count == 1, "UXD18 refresh is requested for account A");
        Check(live.RefreshRequests[0] == a.Id && !Named<Button>("Refresh quota for " + a.Name).IsEnabled
            && Named<Button>("Refresh quota for " + b.Name).IsEnabled && Named<Button>("Models").IsEnabled,
            "UXD18 only A refresh action is locked; B and main navigation remain available");
        Check(bars.All(bar => bar.Value == 37 && TopLevel.GetTopLevel(bar) is not null) && aCard.Bounds == tileBounds
            && inventory.Offset == listOffset && HasText("63 of 100% left"),
            "UXD18 Busy preserves mounted bars, last values, tile geometry and inventory scroll");
        await CaptureBoth("uxd18-accounts-refresh-busy");
        await ResizeViewport(1120, 780);
        await Click("Open account details for " + b.Name);
        var bDetail = Named<ScrollViewer>("Account detail content for " + b.Name);
        inventory.Offset = new Vector(0, 160); window.UpdateLayout();
        var heldOffset = inventory.Offset;
        live.Complete(a.Id, [aWeek with { Used = 38, UsedPercent = 38, Remaining = 62 }, unknown, balance,
            bWeek with { Used = 99, UsedPercent = 99, Remaining = 1 }]);
        await Wait(() => HasText("38% used"), "UXD18 late A success updates only A");
        Check(ReferenceEquals(bDetail, Named<ScrollViewer>("Account detail content for " + b.Name)) && HasText("62% used")
            && !HasText("99% used") && inventory.Offset == heldOffset, "UXD18 A completion cannot replace B inspector or B quota from its full response");
        await Click("Refresh quota for " + a.Name);
        await Wait(() => live.RefreshRequests.Count == 2, "UXD18 A retry uses a second A request");
        live.Fail(a.Id);
        await Wait(() => Controls<TextBlock>().Any(text => text.Text == "Refresh failed · showing last known values"), "UXD18 A error is account local");
        Check(ReferenceEquals(bDetail, Named<ScrollViewer>("Account detail content for " + b.Name)) && HasText("38% used")
            && HasText("62% used"), "UXD18 failed A update retains both accounts and the mounted B detail");
        await CaptureBoth("uxd18-accounts-refresh-error-b-selected");
        await Click("Open account details for " + a.Name);
        Check(HasText("Refresh failed · showing last known values") && HasText("Last known"), "UXD18 returning to A shows its retained error and last-known values");
        await CaptureBoth("uxd18-accounts-refresh-error-a-selected");
        Check(fixture.Unexpected.Count == 0 && fixture.LoginBegins == 0 && fixture.ApplyCalls == 0,
            "UXD18 synthetic flow performs no login or apply");
        window.Close(); foreach (var transport in transports) transport.Dispose();
        await File.WriteAllTextAsync(Path.Combine(Output, "evidence.json"), JsonSerializer.Serialize(new {
            result = "PASS", scenario = "account-snapshots", checks, screenshots,
            refreshAccountIds = live.RefreshRequests, requests = fixture.Requests,
            scope = "Production MainWindow and in-memory handlers on private Xvfb. Native input acceptance remains UXD-22."
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class Uxd18AccountHandler : DelegatingHandler
    {
        private readonly TaskCompletionSource<bool> configuration = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Dictionary<string, TaskCompletionSource<HttpResponseMessage>> pending = new(StringComparer.Ordinal);
        public bool ConfigurationRequested, ConfigurationFailure;
        public readonly List<string> RefreshRequests = [];
        public Uxd18AccountHandler(HttpMessageHandler inner) => InnerHandler = inner;
        public void ReleaseConfiguration() => configuration.TrySetResult(true);
        public void Complete(string id, QuotaDto[] result) => pending[id].TrySetResult(new(HttpStatusCode.OK) { Content = JsonContent.Create(result) });
        public void Fail(string id) => pending[id].TrySetResult(new(HttpStatusCode.ServiceUnavailable) {
            Content = JsonContent.Create(new ApiError("fixture_quota_failed", "Synthetic account refresh failed")) });
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath == "/api/v1/configuration")
            {
                ConfigurationRequested = true;
                await configuration.Task.WaitAsync(ct);
                if (ConfigurationFailure) return new(HttpStatusCode.ServiceUnavailable) {
                    Content = JsonContent.Create(new ApiError("fixture_configuration_failed", "Synthetic configuration read failed")) };
            }
            if (request.RequestUri.AbsolutePath == "/api/v1/quotas/refresh")
            {
                var body = await request.Content!.ReadFromJsonAsync<QuotaRefreshRequest>(cancellationToken: ct);
                var id = body!.AccountId ?? throw new InvalidOperationException("Account ID required by UXD18 fixture");
                var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
                pending[id] = response; RefreshRequests.Add(id);
                return await response.Task.WaitAsync(ct);
            }
            return await base.SendAsync(request, ct);
        }
    }
}
