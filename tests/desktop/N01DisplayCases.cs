using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Lain.Client;
using Lain.Core;
using Lain.Desktop;

internal sealed partial class Scenario
{
    public async Task RunN01DisplaySmoke()
    {
        Directory.CreateDirectory(Output);
        var original = fixture.Config;
        var a = original.Accounts[0] with { Name = "My Gemini API account", Provider = "gemini-api", SharedWithLegacy = false };
        var b = original.Accounts[1] with { Name = "Science account" };
        var sibling = original.Accounts[2] with { Name = a.Name, Provider = a.Provider };
        fixture.Config = original with { Accounts = [a, b, sibling], Routes = [], Profiles = [] };
        var observed = DateTimeOffset.UtcNow.AddMinutes(-30);
        var retained = new QuotaDto(b.Id, null, "fixture#weekly", "ok", observed, observed, DateTimeOffset.UtcNow.AddDays(2),
            "percent", 37, 100, 63, false, WindowLabel: "7 days");
        fixture.QuotaOverride = [retained];
        var http = new HttpClient(fixture, disposeHandler: false) { BaseAddress = new Uri("http://fixture.invalid") };
        transports.Add(http);
        window = new MainWindow(new LainApiClient(http), Path.Combine(Output, "n01-display-preferences.json")) { Width = 1120, Height = 780 };
        lifetime.MainWindow = window; window.Show();
        await Wait(() => HasText("Gemini 1") && HasText("Gemini 2"), "N01 default labels are short and unique across all configured siblings");
        var search = Named<TextBox>("Search accounts");
        search.Text = a.Id;
        await Wait(() => AccountTileNames().Length == 1, "N01 ID filter selects the expected account");
        Check(HasText("Gemini 1") && !HasText("Gemini 2"), "N01 filtering preserves full-inventory labels and identity");
        search.Text = "";
        await Wait(() => AccountTileNames().Length == 3, "N01 filter reset restores account inventory");

        search.Text = a.Id;
        await Wait(() => AccountTileNames().Length == 1, "N01 refresh test selects A by stable ID");
        var pending = fixture.Defer("POST", "/api/v1/quotas/refresh");
        await Click("Refresh quota for " + a.Name); await pending.Entered.Task;
        pending.Response.TrySetException(new TaskCanceledException("Synthetic timeout"));
        await Wait(() => Controls<TextBlock>().Any(text => AutomationProperties.GetName(text) == "Quota refresh reason for " + a.Name
            && text.IsVisible && text.Text!.Contains("too long to respond")), "N01 timeout reason is visible without a snapshot or tooltip");
        await Click("Open account details for " + a.Name);
        var detail = Named<ScrollViewer>("Account detail content for " + a.Name);
        Check(detail.GetVisualDescendants().OfType<SelectableTextBlock>().Any(text => text.Text!.Contains("too long to respond"))
            && detail.GetVisualDescendants().OfType<SelectableTextBlock>().Any(text => text.Text == a.Name),
            "N01 Inspector shows the complete error and original saved name");
        Check(!detail.GetVisualDescendants().OfType<Button>().Any(button => button.IsVisible &&
                (button.Content?.ToString()?.StartsWith("Reconnect", StringComparison.Ordinal) == true
                || AutomationProperties.GetName(button) == "Set up quota access for " + a.Name)),
            "N01 timeout does not claim reconnect or quota setup is required");

        await Click("Close account details for " + a.Name);
        var forbidden = fixture.Defer("POST", "/api/v1/quotas/refresh");
        await Click("Refresh quota for " + a.Name); await forbidden.Entered.Task;
        forbidden.Response.TrySetResult(new(HttpStatusCode.Forbidden) { Content = JsonContent.Create(new ApiError("reporting_denied", "HTTP 403: reporting access denied")) });
        await Wait(() => HasText("Quota could not be loaded · try Refresh quota")
            && Controls<TextBlock>().Any(text => text.Text?.Contains("reporting access denied") == true), "N01 known 403 reason is visible");
        Check(!HasText("Pro required") && !HasText("Upgrade to Pro"), "N01 unspecified 403 does not invent a subscription requirement");

        search.Text = "";
        await Wait(() => AccountTileNames().Length == 3, "N01 restore inventory for retained account B");
        var bCard = Named<Border>("Account budgets for " + b.Name);
        var bars = bCard.GetVisualDescendants().OfType<ProgressBar>().ToArray();
        var refreshB = fixture.Defer("POST", "/api/v1/quotas/refresh");
        await Click("Refresh quota for " + b.Name); await refreshB.Entered.Task;
        refreshB.Fail("Synthetic reporting source unavailable");
        await Wait(() => HasText("Refresh failed · showing last known values"), "N01 failure retains known measurements");
        Check(bCard.GetVisualDescendants().OfType<ProgressBar>().SequenceEqual(bars) && bars.Single().Value == 37
            && bCard.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text?.StartsWith("Checked ", StringComparison.Ordinal) == true),
            "N01 failed refresh preserves mounted values and their observation age");
        var refreshIds = fixture.RequestDetails.Select(item => JsonSerializer.SerializeToElement(item))
            .Where(item => item.GetProperty("path").GetString() == "/api/v1/quotas/refresh")
            .Select(item => JsonDocument.Parse(item.GetProperty("body").GetString()!).RootElement.GetProperty("accountId").GetString()).ToArray();
        Check(refreshIds.SequenceEqual(new[] { a.Id, a.Id, b.Id }), "N01 each refresh remains attached to its stable account ID");
        Check(fixture.LoginBegins == 0 && fixture.ApplyCalls == 0 && fixture.Unexpected.Count == 0,
            "N01 display flow performs no login, apply or external provider request");
        await File.WriteAllTextAsync(Path.Combine(Output, "n01-display-evidence.json"), JsonSerializer.Serialize(new {
            checks, requests = fixture.Requests, requestDetails = fixture.RequestDetails,
            viewport = new { width = window.Bounds.Width, height = window.Bounds.Height }, renderScaling = window.RenderScaling,
            informationLevel = "Simple", runtimeMode = "Gateway", visualReview = "required",
            scope = "Synthetic production UI actions; no external provider or credentials. Native viewport acceptance belongs to N09."
        }, new JsonSerializerOptions { WriteIndented = true }));
        window.Close(); foreach (var transport in transports) transport.Dispose();
    }
}
