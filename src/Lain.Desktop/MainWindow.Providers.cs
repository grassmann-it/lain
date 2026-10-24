using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Lain.Client;
using Lain.Core;
using Lain.Desktop.Presentation;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    private StackPanel? _providerForm;
    private Expander? _providerOptionalSettings;
    private Expander? _providerTerms;
    private bool _providerOptionalOverride;
    private bool _providerTermsOverride;
    private bool _applyingProviderPresentation;

    // Update the existing editor rather than rebuilding controls holding credentials.
    private void SetProviderPresentation(PresentationOptions options)
    {
        if (_providerForm is not null) _providerForm.Spacing = options.Density == ViewDensity.Compact ? 8 : 12;
        _applyingProviderPresentation = true;
        try
        {
            if (_providerOptionalSettings is { } optional && !_providerOptionalOverride && !optional.IsKeyboardFocusWithin)
                optional.IsExpanded = options.Level == InformationLevel.Advanced;
            if (_providerTerms is { } terms && !_providerTermsOverride && !terms.IsKeyboardFocusWithin)
                terms.IsExpanded = options.Level == InformationLevel.Advanced;
        }
        finally { _applyingProviderPresentation = false; }
    }

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
        RegisterEditor(() => BeginProviderSetup(providerId), configuration.Revision);
        _editing = true;
        _loginVisible = false;
        _actions.Clear();
        _providerOptionalSettings = _providerTerms = null;
        _providerOptionalOverride = _providerTermsOverride = false;
        _workspace.Offset = default;
        var connections = provider.Connections ?? [];
        var secretFields = provider.Credentials.Where(field => field.Secret && field.Required).ToArray();
        if (secretFields.Length != 1 || connections.Length == 0)
        {
            _page.Content = Ui.Stack(Heading($"Set up {provider.Name}", "This provider needs a credential flow that is not prepared in this account form."),
                Ui.Text("Review the official access requirements before returning with credentials.", 13, Ui.Muted),
                Ui.Button("Official credential setup", async () => await OpenProviderLinkAsync(new Uri(provider.AccountUrl))),
                Ui.Button("Back to Discover providers", ShowProviderCatalog));
            return;
        }
        var name = Ui.Input(AccountNameSuggestions.Suggest(provider.Id, provider.DefaultAccountName ?? provider.Name, configuration.Accounts.Select(account => account.Name)));
        var connection = new ComboBox { ItemsSource = connections.Select(item => item.Name).ToArray(), PlaceholderText = "Choose an account connection", HorizontalAlignment = HorizontalAlignment.Stretch };
        var billing = new ComboBox { PlaceholderText = "Choose your access / billing plan", HorizontalAlignment = HorizontalAlignment.Stretch };
        var endpoint = Ui.Input(hint: "Derived from the account connection and scope"); endpoint.IsReadOnly = true;
        var secret = Ui.Input(hint: secretFields[0].Label); secret.PasswordChar = '●';
        var settings = new Dictionary<string, (ProviderCredentialFieldDto Field, Control Input)>(StringComparer.Ordinal);
        var settingRows = new StackPanel { Spacing = 6 };
        var optionalRows = new StackPanel { Spacing = 6 };
        foreach (var field in provider.Credentials.Where(field => !field.Secret && (field.Kind != "client-id" || field.Required)))
        {
            Control input = field.Kind == "consent" ? new CheckBox { Content = "I explicitly allow this", IsChecked = false } : Ui.Input(hint: field.Required ? "Required" : "Optional");
            settings.Add(field.Id, (field, input));
            var rows = field.Required ? settingRows : optionalRows;
            rows.Children.Add(AccountFormRow(field.Label + (field.Required ? " *" : ""), input));
            AutomationProperties.SetName(input, field.Label);
            AutomationProperties.SetHelpText(input, field.Required ? "Required account scope setting" : "Optional account scope setting");
            if (!string.IsNullOrWhiteSpace(field.Help)) rows.Children.Add(Ui.Text(field.Help, 11, Ui.Muted));
        }
        var form = AccountForm($"Set up {provider.Name}", "Add credentials for your provider account. Choose models now or later without entering the key again.", AccountFormRow("Display name", name));
        form.Children.Add(Ui.Button("Open official credential page", async () => await OpenProviderLinkAsync(new Uri(provider.AccountUrl))));
        foreach (var step in provider.SetupSteps)
        {
            form.Children.Add(Ui.Text(step.Title + " · " + step.Description, 11, Ui.Muted));
            if (Uri.TryCreate(step.Url, UriKind.Absolute, out var link) && link.Scheme == Uri.UriSchemeHttps)
                form.Children.Add(Ui.Button(step.Title, async () => await OpenProviderLinkAsync(link)));
        }
        if (connections.Length > 1) form.Children.Add(AccountFormRow("Account connection", connection));
        form.Children.Add(AccountFormRow("Billing plan", billing));
        form.Children.Add(Ui.Text("Select the plan on your account. No entitlement, balance or live access has been verified.", 11, Ui.Muted));
        form.Children.Add(settingRows);
        if (optionalRows.Children.Count > 0)
        {
            _providerOptionalSettings = new Expander { Header = "Optional account settings", Content = optionalRows, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(_providerOptionalSettings, "Optional account settings");
            _providerOptionalSettings.PropertyChanged += (_, change) => { if (change.Property == Expander.IsExpandedProperty && !_applyingProviderPresentation) _providerOptionalOverride = true; };
            form.Children.Add(_providerOptionalSettings);
        }
        form.Children.Add(AccountFormRow(secretFields[0].Label, secret));
        if (provider.MonitorAccessOptions is { Length: > 0 })
        {
            form.Children.Add(Ui.Text("Optional monitoring access can be attached to this same account after saving, from account details. The inference key does not need to be entered again.", 11, Ui.Muted));
            foreach (var option in provider.MonitorAccessOptions)
                if (!string.IsNullOrWhiteSpace(option.Description)) form.Children.Add(Ui.Text(option.Description, 11, Ui.Muted));
        }
        if (!string.IsNullOrWhiteSpace(secretFields[0].Help)) form.Children.Add(Ui.Text(secretFields[0].Help!, 11, Ui.Muted));
        var modelRows = new StackPanel { Spacing = 6 };
        modelRows.Children.Add(Ui.Text("No models selected by default. Saving only the account lets you choose compatible models later.", 11, Ui.Muted));
        var modelOptions = new StackPanel { Spacing = 6 };
        modelRows.Children.Add(modelOptions);
        var models = new Expander { Header = "Choose models (optional)", Content = modelRows, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(models, "Choose models (optional)");
        form.Children.Add(models);
        var restrictionText = Ui.Text(string.Join("\n\n", provider.Restrictions), 11, Ui.Muted);
        var technical = Ui.Stack(AccountFormRow("Base URL", endpoint), restrictionText);
        _providerTerms = new Expander { Header = "Technical details and provider terms", Content = technical, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(_providerTerms, "Technical details and provider terms");
        _providerTerms.PropertyChanged += (_, change) => { if (change.Property == Expander.IsExpandedProperty && !_applyingProviderPresentation) _providerTermsOverride = true; };
        form.Children.Add(_providerTerms);
        form.Children.Add(Ui.Text("Saving does not refresh quota, test inference or change the applied routing policy. A documented free plan does not enable strict free-only routing.", 12, Ui.Muted));
        var modelChecks = new Dictionary<string, CheckBox>(StringComparer.Ordinal);
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
            if (connection.SelectedIndex < 0 || connection.SelectedIndex >= connections.Length) { endpoint.Text = ""; return; }
            var values = CurrentSettings();
            static string Normalize(string key) => new(key.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
            endpoint.Text = Regex.Replace(connections[connection.SelectedIndex].BaseUrl, "\\{([A-Za-z0-9_]+)\\}", match => values.FirstOrDefault(pair => Normalize(pair.Key) == Normalize(match.Groups[1].Value)).Value ?? match.Value);
        }
        void UpdateModels()
        {
            modelChecks.Clear(); modelOptions.Children.Clear();
            if (connection.SelectedIndex < 0 || billing.SelectedIndex < 0) return;
            var selected = connections[connection.SelectedIndex];
            var plan = planIds[billing.SelectedIndex];
            foreach (var route in provider.Routes.Where(route => selected.ProviderRouteIds.Contains(route.Id, StringComparer.Ordinal) && route.BillingPlanIds.Contains(plan, StringComparer.Ordinal)))
            {
                var check = new CheckBox { Content = route.Name, IsChecked = false };
                AutomationProperties.SetName(check, "Add model " + route.Name);
                ToolTip.SetTip(check, route.Model);
                modelChecks.Add(route.Id, check); modelOptions.Children.Add(check);
            }
        }
        connection.SelectionChanged += (_, _) =>
        {
            if (connection.SelectedIndex < 0 || connection.SelectedIndex >= connections.Length) return;
            planIds = connections[connection.SelectedIndex].BillingPlanIds;
            billing.ItemsSource = planIds.Select(id => provider.BillingPlans.FirstOrDefault(plan => plan.Id == id) is { } plan ? $"{plan.Name} · {ProviderAccessLabel(plan.Kind)}" : id).ToArray();
            billing.SelectedIndex = -1;
            UpdateEndpoint(); UpdateModels();
        };
        billing.SelectionChanged += (_, _) => { UpdateEndpoint(); UpdateModels(); };
        foreach (var item in settings.Values) if (item.Input is TextBox text) text.TextChanged += (_, _) => UpdateEndpoint();
        connection.SelectedIndex = connections.Length == 1 ? 0 : -1;
        form.Children.Add(Ui.Row(Action("Add account", async () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(secret.Text)) throw new InvalidOperationException("Enter an account name and your own API credential.");
            if (connection.SelectedIndex < 0 || connection.SelectedIndex >= connections.Length) throw new InvalidOperationException("Choose an account connection.");
            if (billing.SelectedIndex < 0 || billing.SelectedIndex >= planIds.Length) throw new InvalidOperationException("Choose the access or billing plan on your account.");
            foreach (var item in settings.Values.Where(item => item.Field.Required))
                if ((item.Input is TextBox text && string.IsNullOrWhiteSpace(text.Text)) || (item.Input is CheckBox check && check.IsChecked != true)) throw new InvalidOperationException($"Enter the required {item.Field.Label}.");
            if (!Uri.TryCreate(endpoint.Text, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || endpoint.Text!.Contains('{')) throw new InvalidOperationException("Complete the account scope settings to resolve the official HTTPS endpoint.");
            _configuration = await _client.AddApiAccountAsync(configuration.Revision, new(name.Text.Trim(), provider.Id, secret.Text, endpoint.Text.Trim(), Settings: CurrentSettings(),
                ProviderConnectionId: connections[connection.SelectedIndex].Id, SelectedModelRouteIds: modelChecks.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key).ToArray()), _lifetime.Token);
            secret.Text = ""; _section = "Accounts";
        }, "Account saved. Provider access and live budgets remain unverified."), Ui.Button("Cancel", () => { secret.Text = ""; _editing = false; Render(); })));
        _providerForm = form;
        SetProviderPresentation(PresentationFor("Accounts"));
        _page.Content = form;
    }
}
