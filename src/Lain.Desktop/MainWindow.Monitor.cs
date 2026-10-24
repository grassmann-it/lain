using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Lain.Core;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    private async Task OpenMonitorSetupAsync(AccountDto account)
    {
        var catalog = _providerCatalog ?? await _client.ProvidersAsync(_lifetime.Token);
        _providerCatalog = catalog;
        var provider = catalog.Providers.FirstOrDefault(provider => provider.Id == account.Provider);
        var current = await _client.MonitorAccessAsync(account.Id, _lifetime.Token);
        BuildMonitorEditor(account, provider, current);
    }

    private void BuildMonitorEditor(AccountDto account, ProviderDescriptorDto? provider, MonitorAccessDto current)
    {
        if (_configuration is not { } configuration) throw new InvalidOperationException("Connect to the local host first.");
        RegisterEditor(() => BuildMonitorEditor(account, provider, current), configuration.Revision);
        _editing = true; _actions.Clear(); _workspace.Offset = default; _notice.Text = "";
        var options = provider?.MonitorAccessOptions ?? [];
        var form = AccountForm("Monitoring access for " + account.Name,
            "Attach optional quota-reading access to this same account. Inference credentials and monitoring permissions serve different purposes.");
        var state = current.State == "configured-unverified" ? "Saved locally · monitor access not checked" : current.State == "not-configured" ? "No separate monitoring access saved" : current.State;
        form.Children.Add(Ui.Text(state, 12, current.State == "expired" ? Ui.Warning : Ui.Muted));
        if (current.ExpiresAt is { } savedExpiry) form.Children.Add(Ui.Text("Saved token expires " + savedExpiry.ToLocalTime().ToString("dd MMM · HH:mm zzz", CultureInfo.CurrentCulture), 11, savedExpiry <= DateTimeOffset.UtcNow ? Ui.Warning : Ui.Muted));
        if (!string.IsNullOrWhiteSpace(current.Guidance)) form.Children.Add(Ui.Text(current.Guidance, 12, Ui.Muted));
        if (options.Length == 0)
        {
            form.Children.Add(Ui.Text("No additional monitor credential flow is implemented for this provider. Inference can remain usable without it.", 12, Ui.Muted));
            if (Uri.TryCreate(current.DocumentationUrl ?? provider?.AccountUrl, UriKind.Absolute, out var link) && link.Scheme == Uri.UriSchemeHttps)
                form.Children.Add(Ui.Button("Official quota guidance", async () => await OpenProviderLinkAsync(link)));
            if (current.State != "not-configured") form.Children.Add(Action("Remove monitoring access", async () =>
            {
                _configuration = await _client.RemoveMonitorAccessAsync(configuration.Revision, account.Id, _lifetime.Token);
            }, "Monitoring access removed locally. External access was not revoked."));
            form.Children.Add(Ui.Button("Back to accounts", () => { _editing = false; Render(); }));
            _page.Content = form; return;
        }
        var kind = new ComboBox { ItemsSource = options.Select(option => option.Name).ToArray(), HorizontalAlignment = HorizontalAlignment.Stretch };
        var description = Ui.Text("", 12, Ui.Muted);
        var scopes = Ui.Text("", 11, Ui.Muted);
        var resource = Ui.Input(current.ResourceId, "Resource ID from the official console");
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
        form.Children.Add(Ui.Text("Saving access does not refresh quota or test inference. External token snapshots are never refreshed or revoked by lain; replace them here after expiry.", 11, Ui.Muted));
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
        form.Children.Add(OverviewActions(Action("Save monitoring access", async () =>
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
        }, "Monitoring access saved locally. Refresh quota separately to check the configured source."),
            Ui.Button("Cancel", () => { token.Text = ""; _editing = false; Render(); })));
        if (current.State != "not-configured") form.Children.Add(Action("Remove monitoring access", async () =>
        {
            _configuration = await _client.RemoveMonitorAccessAsync(configuration.Revision, account.Id, _lifetime.Token); token.Text = "";
        }, "Monitoring access removed locally. External access was not revoked."));
        _page.Content = form;
    }
}
