using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Lain.Client;
using Lain.Core;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    private async Task RefreshProviderCatalogAsync()
    {
        if (_providerLoading || _lifetime.IsCancellationRequested) return;
        _providerLoading = true;
        try
        {
            var catalog = await _client.ProvidersAsync(_lifetime.Token);
            _providerCatalog = catalog;
            _providers.SetCatalog(catalog, _configuration?.Accounts);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (_providerCatalog is null) _providers.ShowUnavailable(Friendly(error));
            else if (_section == "Providers") _notice.Text = "Provider descriptions could not be refreshed. Showing the previously loaded catalog.";
        }
        finally { _providerLoading = false; }
    }

    private async Task OpenProviderLinkAsync(Uri uri)
    {
        try
        {
            if (uri.Scheme != Uri.UriSchemeHttps || !await Launcher.LaunchUriAsync(uri))
                _notice.Text = "The official page could not be opened. Check the default browser and try again.";
        }
        catch (Exception) { _notice.Text = "The official page could not be opened. Check the default browser and try again."; }
    }

    private static string ProviderAccessLabel(string kind) => kind switch
    {
        "free" => "Free access", "trial" => "One-time trial", "subscription" => "Subscription",
        "payg" => "Pay as you go", _ => "Unknown pricing"
    };

    private void BeginProviderSetup(string providerId)
    {
        if (_configuration is not { } configuration) { _notice.Text = "Connect to the local host before adding credentials. You can still read the provider's setup guide."; return; }
        var provider = _providerCatalog?.Providers.FirstOrDefault(item => item.Id == providerId);
        if (provider is null) { _notice.Text = "Refresh the provider catalog before starting setup."; return; }
        _editing = true;
        _workspace.Offset = default;
        var name = Ui.Input($"My {provider.Name} account");
        var route = new ComboBox { ItemsSource = provider.Routes.Select(item => $"{item.Name} · {item.Protocol}").ToArray(), PlaceholderText = "Choose a concrete route", HorizontalAlignment = HorizontalAlignment.Stretch };
        var billing = new ComboBox { PlaceholderText = "Choose an access / billing plan", HorizontalAlignment = HorizontalAlignment.Stretch };
        var endpoint = Ui.Input(hint: "Derived from the selected route and account settings");
        endpoint.IsReadOnly = true;
        var secretFields = provider.Credentials.Where(field => field.Secret).ToArray();
        if (secretFields.Length != 1)
        {
            _page.Content = Ui.Stack(Heading($"Set up {provider.Name}", "This provider needs a credential flow that is not prepared in this account form."),
                Ui.Text("Review the official access requirements before returning with credentials.", 13, Ui.Muted),
                Ui.Button("Back to supported providers", () => { _editing = false; Render(); }));
            return;
        }
        var secret = Ui.Input(hint: secretFields[0].Label); secret.PasswordChar = '●';
        var settings = new Dictionary<string, (ProviderCredentialFieldDto Field, Control Input)>(StringComparer.Ordinal);
        var settingRows = new StackPanel { Spacing = 6 };
        foreach (var field in provider.Credentials.Where(field => !field.Secret && field.Kind != "client-id"))
        {
            Control input = field.Kind == "consent" ? new CheckBox { Content = "I explicitly allow this", IsChecked = false } : Ui.Input(hint: field.Required ? "Required" : "Optional");
            settings.Add(field.Id, (field, input));
            settingRows.Children.Add(AccountFormRow(field.Label + (field.Required ? " *" : ""), input));
            AutomationProperties.SetName(input, field.Label);
            AutomationProperties.SetHelpText(input, field.Required ? "Required account setting" : "Optional account setting");
            if (!string.IsNullOrWhiteSpace(field.Help)) settingRows.Children.Add(Ui.Text(field.Help, 11, Ui.Muted));
        }
        var routeNotes = Ui.Text("Choose a route to review its protocol, billing plans and restrictions.", 12, Ui.Muted);
        var form = AccountForm($"Set up {provider.Name}", "Save your own credentials locally. Saving does not verify provider access or enable free-only routing.",
            AccountFormRow("Display name", name), AccountFormRow("Provider route", route), AccountFormRow("Billing plan", billing));
        form.Children.Add(routeNotes);
        form.Children.Add(settingRows);
        form.Children.Add(AccountFormRow("Base URL", endpoint));
        form.Children.Add(AccountFormRow(secretFields[0].Label, secret));
        if (!string.IsNullOrWhiteSpace(secretFields[0].Help)) form.Children.Add(Ui.Text(secretFields[0].Help!, 11, Ui.Muted));
        var restrictionText = Ui.Text(string.Join("\n\n", provider.Restrictions), 11, Ui.Muted);
        var restrictions = new Expander
        {
            Header = "Provider terms and route details", HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = restrictionText
        };
        AutomationProperties.SetName(restrictions, "Provider terms and route details");
        form.Children.Add(restrictions);
        string[] planIds = [];
        Dictionary<string, string> CurrentSettings()
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (id, item) in settings)
            {
                if (item.Input is CheckBox check) { if (check.IsChecked == true) values[id] = "true"; }
                else if (item.Input is TextBox text && !string.IsNullOrWhiteSpace(text.Text)) values[id] = text.Text.Trim();
            }
            if (billing.SelectedIndex >= 0 && billing.SelectedIndex < planIds.Length) values["billingPlan"] = planIds[billing.SelectedIndex];
            return values;
        }
        void UpdateEndpoint()
        {
            if (route.SelectedIndex < 0 || route.SelectedIndex >= provider.Routes.Length) { endpoint.Text = ""; return; }
            var values = CurrentSettings();
            static string Normalize(string key) => new(key.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
            endpoint.Text = Regex.Replace(provider.Routes[route.SelectedIndex].BaseUrl, "\\{([A-Za-z0-9_]+)\\}", match =>
                values.FirstOrDefault(pair => Normalize(pair.Key) == Normalize(match.Groups[1].Value)).Value ?? match.Value);
        }
        route.SelectionChanged += (_, _) =>
        {
            if (route.SelectedIndex < 0 || route.SelectedIndex >= provider.Routes.Length) return;
            var selected = provider.Routes[route.SelectedIndex];
            planIds = selected.BillingPlanIds;
            billing.ItemsSource = planIds.Select(id => provider.BillingPlans.FirstOrDefault(plan => plan.Id == id) is { } plan ? $"{plan.Name} · {ProviderAccessLabel(plan.Kind)}" : id).ToArray();
            billing.SelectedIndex = -1;
            routeNotes.Text = string.Join("\n", new[] { $"{selected.Model} · {selected.Protocol}", "Choose the plan for this route explicitly. No account balance or live access has been verified.", provider.Summary });
            restrictionText.Text = string.Join("\n\n", provider.Restrictions.Concat(selected.Restrictions ?? []).Distinct(StringComparer.Ordinal));
            UpdateEndpoint();
        };
        billing.SelectionChanged += (_, _) => UpdateEndpoint();
        foreach (var item in settings.Values)
            if (item.Input is TextBox text) text.TextChanged += (_, _) => UpdateEndpoint();
        form.Children.Add(Ui.Row(Action("Add account", async () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(secret.Text)) throw new InvalidOperationException("Enter an account name and your own API credential.");
            if (route.SelectedIndex < 0 || route.SelectedIndex >= provider.Routes.Length) throw new InvalidOperationException("Choose a concrete provider route.");
            if (billing.SelectedIndex < 0 || billing.SelectedIndex >= planIds.Length) throw new InvalidOperationException("Choose the access or billing plan you intend to use.");
            foreach (var item in settings.Values.Where(item => item.Field.Required))
                if ((item.Input is TextBox text && string.IsNullOrWhiteSpace(text.Text)) || (item.Input is CheckBox check && check.IsChecked != true))
                    throw new InvalidOperationException($"Enter the required {item.Field.Label}.");
            if (!Uri.TryCreate(endpoint.Text, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || endpoint.Text!.Contains('{'))
                throw new InvalidOperationException("Complete the account settings to resolve the official HTTPS endpoint.");
            _configuration = await _client.AddApiAccountAsync(configuration.Revision,
                new AddApiAccountRequest(name.Text.Trim(), provider.Id, secret.Text, endpoint.Text.Trim(), provider.Routes[route.SelectedIndex].Id, CurrentSettings()), _lifetime.Token);
            secret.Text = "";
            _section = "Accounts";
        }, "Account saved. Provider access and live budgets remain unverified."), Ui.Button("Cancel", () => { secret.Text = ""; _editing = false; Render(); })));
        _page.Content = form;
    }
}
