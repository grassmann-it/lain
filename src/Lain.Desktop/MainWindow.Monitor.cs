using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Lain.Core;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    private Control QuotaAccessActions(AccountDto account, QuotaDto? quota)
    {
        var actions = Ui.Stack(); actions.Spacing = 4;
        var provider = ConnectProviderName(account.Provider);
        var kind = quota?.Support?.Kind;
        if (kind is not ("console" or "unsupported" or "unimplemented"))
        {
            var refresh = Action(kind == "awaiting-observation" ? "Reload saved observations" : "Refresh quota", async () =>
            {
                if (!_hostAvailable) throw new InvalidOperationException("The local host is offline. Reload status before refreshing quota.");
                _quotas = await _client.RefreshQuotasAsync(account.Id, _lifetime.Token);
            }, "Quota refresh finished. Review the reported result.");
            AutomationProperties.SetName(refresh, "Refresh quota for " + account.Name);
            refresh.IsEnabled = _hostAvailable;
            actions.Children.Add(refresh);
        }
        if (kind == "setup-required" || quota is null || quota.State is "error" or "stale" || quota.Error is not null)
        {
            var setup = Action("Set up " + provider + " quota access", async () =>
            {
                CloseAccountInspector(false);
                await OpenMonitorSetupAsync(account);
            }, "Review the available access methods for this account.", stayOnPage: true);
            AutomationProperties.SetName(setup, "Set up quota access for " + account.Name);
            setup.IsEnabled = _hostAvailable;
            actions.Children.Add(setup);
        }
        if (Uri.TryCreate(quota?.Support?.ActionUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            actions.Children.Add(Ui.Button("Open " + provider + " usage console", async () => await OpenProviderLinkAsync(uri)));
        return actions;
    }

    private async Task OpenMonitorSetupAsync(AccountDto account)
    {
        account = _configuration?.Accounts.FirstOrDefault(saved => saved.Id == account.Id)
            ?? throw new InvalidOperationException("This account is no longer configured. Reload accounts before connecting access.");
        var catalog = _providerCatalog ?? await _client.ProvidersAsync(_lifetime.Token);
        _providerCatalog = catalog;
        var provider = catalog.Providers.FirstOrDefault(provider => provider.Id == QuotaProviderId(account.Provider));
        _connectionMethods = await _client.ConnectionMethodsAsync(QuotaProviderId(account.Provider), _lifetime.Token);
        var current = await _client.MonitorAccessAsync(account.Id, _lifetime.Token);
        BuildMonitorEditor(account, provider, current);
    }

    private void BuildMonitorEditor(AccountDto account, ProviderDescriptorDto? provider, MonitorAccessDto current)
    {
        if (_configuration is not { } configuration) throw new InvalidOperationException("Connect to the local host first.");
        RegisterEditor(() => BuildMonitorEditor(account, provider, current), configuration.Revision);
        _editing = true; _actions.Clear(); _workspace.Offset = default; _notice.Text = "";
        var options = provider?.MonitorAccessOptions ?? [];
        var methods = _connectionMethods?.Methods.Where(method => method.Provider == QuotaProviderId(account.Provider)
                && method.Purposes.Any(purpose => purpose is "quota" or "cost"))
            .OrderByDescending(method => method.Recommended).ToArray() ?? [];
        var form = AccountForm("Monitoring access for " + account.Name,
            "Read quota on this same account. Use saved access where the provider supports it, or connect one of the available methods below.");
        var state = current.State == "configured-unverified" ? "Saved locally · monitor access not checked"
            : current.State is "not-configured" or "unsupported" ? "No separate monitoring access saved" : current.State;
        form.Children.Add(Ui.Text(state, 12, current.State == "expired" ? Ui.Warning : Ui.Muted));
        if (current.ExpiresAt is { } savedExpiry) form.Children.Add(Ui.Text("Saved token expires " + savedExpiry.ToLocalTime().ToString("dd MMM · HH:mm zzz", CultureInfo.CurrentCulture), 11, savedExpiry <= DateTimeOffset.UtcNow ? Ui.Warning : Ui.Muted));
        if (!string.IsNullOrWhiteSpace(current.Guidance)) form.Children.Add(Ui.Text(current.Guidance, 12, Ui.Muted));
        if (methods.Any(ConnectionMethodAvailable) || options.Length > 0 || current.State is not ("not-configured" or "unsupported"))
        {
            var refresh = Action("Refresh quota with saved access", async () =>
            {
                _quotas = await _client.RefreshQuotasAsync(account.Id, _lifetime.Token);
            }, "Quota refresh finished. Review the reported result.");
            AutomationProperties.SetName(refresh, "Refresh quota with saved access for " + account.Name);
            form.Children.Add(refresh);
        }
        foreach (var method in methods.Where(method => method.Kind != "monitor-token" && ConnectionMethodAvailable(method)))
        {
            var connect = Ui.Button(method.Title, () => ShowConnectionMethod(method, account, quotaOnly: true));
            AutomationProperties.SetName(connect, "Connect quota access " + method.Id + " for " + account.Name);
            form.Children.Add(connect);
            if (!string.IsNullOrWhiteSpace(method.Description)) form.Children.Add(Ui.Text(method.Description, 12, Ui.Muted));
        }
        var unavailableMethods = methods.Where(method => !ConnectionMethodAvailable(method)).ToArray();
        if (unavailableMethods.Length > 0)
            form.Children.Add(new Expander { Header = "Other connection requirements", Content = Ui.Stack(unavailableMethods.Select(method =>
                (Control)Ui.Stack(Ui.Text(method.Title + " · " + ConnectionRequirement(method), 12, Ui.Muted),
                    Ui.Button("Review requirements for " + method.Title, () => ShowConnectionPrerequisite(method, account, quotaOnly: true)))).ToArray()) });
        if (options.Length == 0)
        {
            form.Children.Add(Ui.Text(methods.Any(ConnectionMethodAvailable)
                ? "Saved account access is reused for supported quota reads. Reconnect above when that access needs replacing."
                : "No direct quota connection is available on this host for this provider. Review the provider console for usage.", 12, Ui.Muted));
            if (Uri.TryCreate(current.DocumentationUrl ?? provider?.AccountUrl, UriKind.Absolute, out var link) && link.Scheme == Uri.UriSchemeHttps)
                form.Children.Add(Ui.Button("Open " + ConnectProviderName(account.Provider) + " console", async () => await OpenProviderLinkAsync(link)));
            if (current.State is not ("not-configured" or "unsupported")) form.Children.Add(Action("Remove monitoring access", async () =>
            {
                _configuration = await _client.RemoveMonitorAccessAsync(configuration.Revision, account.Id, _lifetime.Token);
            }, "Monitoring access removed locally. External access was not revoked."));
            form.Children.Add(Ui.Button("Back to accounts", () => { _editing = false; Render(); }));
            _page.Content = form; return;
        }
        var kind = new ComboBox { ItemsSource = options.Select(option => option.Name).ToArray(), HorizontalAlignment = HorizontalAlignment.Stretch };
        var description = Ui.Text("", 12, Ui.Muted);
        var scopes = Ui.Text("", 11, Ui.Muted);
        var resource = Ui.Input(current.ResourceId ?? account.Binding?.ResourceId, "Resource ID from the official console");
        var resourceHelp = Ui.Text("", 11, Ui.Muted);
        var resourceRow = AccountFormRow("Resource", resource);
        var token = Ui.Input(hint: "Monitoring token (hidden)"); token.PasswordChar = '●';
        var tokenRow = AccountFormRow("Monitoring token", token);
        var expiry = Ui.Input(hint: "2026-10-24T20:00:00+02:00");
        var expiryRow = AccountFormRow("Expires at", expiry);
        var docs = Ui.Button("Official monitor access instructions", async () =>
        {
            if (kind.SelectedIndex >= 0 && Uri.TryCreate(options[kind.SelectedIndex].DocumentationUrl, UriKind.Absolute, out var url)) await OpenProviderLinkAsync(url);
        });
        form.Children.Add(AccountFormRow("Access method", kind));
        form.Children.Add(description); form.Children.Add(scopes); form.Children.Add(docs);
        form.Children.Add(resourceRow); form.Children.Add(resourceHelp); form.Children.Add(tokenRow); form.Children.Add(expiryRow);
        form.Children.Add(Ui.Text("Saving keeps access on this account. Use Save and refresh quota to check its reporting permissions. External token snapshots must be replaced after expiry.", 11, Ui.Muted));
        kind.SelectionChanged += (_, _) =>
        {
            if (kind.SelectedIndex < 0) return;
            var selected = options[kind.SelectedIndex];
            description.Text = selected.Description ?? selected.Name;
            scopes.Text = selected.Scopes.Length == 0 ? "Read permissions are described in the official instructions." : "Required read permissions: " + string.Join(", ", selected.Scopes);
            resourceHelp.Text = selected.ResourceLabel + " · " + selected.ResourceHelp;
            resourceRow.IsVisible = resourceHelp.IsVisible = !string.IsNullOrWhiteSpace(selected.ResourceLabel);
            AutomationProperties.SetName(resource, selected.ResourceLabel.Length > 0 ? selected.ResourceLabel : "Resource ID");
            tokenRow.IsVisible = selected.SecretRequired;
            expiryRow.IsVisible = selected.Kind == "access-token-snapshot";
            token.Text = "";
        };
        var index = Array.FindIndex(options, option => option.Kind == current.Kind);
        kind.SelectedIndex = index >= 0 ? index : 0;
        async Task SaveAccessAsync(bool checkQuota)
        {
            var selected = options[kind.SelectedIndex];
            if (selected.SecretRequired && (string.IsNullOrWhiteSpace(token.Text) || token.Text.Any(char.IsWhiteSpace))) throw new InvalidOperationException("Enter your own monitoring token without whitespace.");
            if (!string.IsNullOrWhiteSpace(selected.ResourceLabel) && string.IsNullOrWhiteSpace(resource.Text)) throw new InvalidOperationException("Enter " + selected.ResourceLabel + ".");
            DateTimeOffset? expiresAt = null;
            if (selected.Kind == "access-token-snapshot")
            {
                if (string.IsNullOrWhiteSpace(expiry.Text) || !(expiry.Text.EndsWith('Z') || expiry.Text.LastIndexOf('+') > expiry.Text.IndexOf('T') || expiry.Text.LastIndexOf('-') > expiry.Text.IndexOf('T')) ||
                    !DateTimeOffset.TryParse(expiry.Text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) || parsed <= DateTimeOffset.UtcNow)
                    throw new InvalidOperationException("Enter the token's actual future expiry as an ISO timestamp with timezone.");
                expiresAt = parsed;
            }
            _configuration = await _client.SetMonitorAccessAsync(configuration.Revision, account.Id,
                new(selected.Kind, selected.SecretRequired ? token.Text : null, resourceRow.IsVisible && !string.IsNullOrWhiteSpace(resource.Text) ? resource.Text.Trim() : null, expiresAt), _lifetime.Token);
            token.Text = "";
            if (checkQuota)
            {
                try { _quotas = await _client.RefreshQuotasAsync(account.Id, _lifetime.Token); }
                catch (Exception error) when (error is not OperationCanceledException)
                { _notice.Text = "Monitoring access was saved. Quota refresh failed: " + Friendly(error); }
            }
        }
        form.Children.Add(OverviewActions(Action("Save and refresh quota", () => SaveAccessAsync(true), "Monitoring access saved. Review the quota result."),
            Action("Save monitoring access", () => SaveAccessAsync(false), "Monitoring access saved locally. Refresh quota to check the configured source."),
            Ui.Button("Cancel", () => { token.Text = ""; _editing = false; Render(); })));
        if (current.State is not ("not-configured" or "unsupported")) form.Children.Add(Action("Remove monitoring access", async () =>
        {
            _configuration = await _client.RemoveMonitorAccessAsync(configuration.Revision, account.Id, _lifetime.Token); token.Text = "";
        }, "Monitoring access removed locally. External access was not revoked."));
        _page.Content = form;
    }

    private static string QuotaProviderId(string provider) => provider switch
    {
        "gemini" or "generativelanguage.googleapis.com" => "gemini-api",
        "cloudflare" or "api.cloudflare.com" => "cloudflare-workers-ai",
        "nvidia" => "nvidia-build", "mimo" or "xiaomi" => "xiaomi-mimo",
        "command-code" => "commandcode", "fireworks-ai" or "api.fireworks.ai" => "fireworks",
        _ => provider
    };
}
