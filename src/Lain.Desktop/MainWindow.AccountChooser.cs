using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Layout;
using Lain.Core;
using Lain.Desktop.Providers;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    private ConnectionMethodCatalogDto? _connectionMethods;
    private string _connectProviderSearch = "";
    private long _connectionPresentationRequest;
    private readonly Dictionary<string, (string Name, Dictionary<string, string> Settings)> _connectSafeDrafts = new(StringComparer.Ordinal);

    private bool OwnsConnectionPresentation(long request, long navigation, object? content) => !_lifetime.IsCancellationRequested
        && request == _connectionPresentationRequest && navigation == _workspaceNavigationGeneration
        && _section == "Accounts" && ReferenceEquals(_page.Content, content);

    private async void ShowAddAccountChooser()
    {
        var request = ++_connectionPresentationRequest;
        long navigation = -1;
        object? ownerContent = null;
        try
        {
            if (_section != "Accounts")
            {
                await NavigateToAsync(new("Accounts"));
                if (request != _connectionPresentationRequest || _section != "Accounts" || _lifetime.IsCancellationRequested) return;
            }
            if (!CanOpenSectionEditor()) return;
            _notice.Text = "";
            _editing = true;
            _loginVisible = false;
            _actions.Clear();
            _workspace.Offset = default;
            ownerContent = Ui.Stack(Heading("Add account", "Loading available provider connections…"));
            _page.Content = ownerContent;
            navigation = _workspaceNavigationGeneration;
            async Task<ProviderCatalogDto?> ReadDocumentationAsync()
            {
                try { return await _client.ProvidersAsync(_lifetime.Token); }
                catch (Exception error) when (error is not OperationCanceledException) { return _providerCatalog; }
            }
            var methodTask = _client.ConnectionMethodsAsync(ct: _lifetime.Token);
            var documentationTask = ReadDocumentationAsync();
            await Task.WhenAll(methodTask, documentationTask);
            if (!OwnsConnectionPresentation(request, navigation, ownerContent)) return;
            var methods = methodTask.Result;
            _providerCatalog = documentationTask.Result;
            _connectionMethods = methods;
            _providers.SetConnectionMethods(methods);
            var inventory = ProviderInventory.Merge(_providerCatalog, methods);
            _providers.SetCatalog(inventory, _configuration?.Accounts);
            var search = Ui.Input(_connectProviderSearch, "Search providers");
            AutomationProperties.SetName(search, "Choose account provider");
            var rows = Ui.Stack(); rows.Spacing = 6;
            void Populate()
            {
                rows.Children.Clear();
                foreach (var provider in inventory.Providers.Where(p => $"{p.Id} {p.Name}".Contains(search.Text?.Trim() ?? "", StringComparison.OrdinalIgnoreCase)))
                {
                    var available = methods.Methods.Any(m => m.Provider == provider.Id && ConnectionMethodAvailable(m));
                    var choose = Ui.Button(provider.Name, () => ShowConnectionMethods(provider.Id));
                    var identity = Ui.Stack(Ui.Text(provider.Name, 13), Ui.Text(available ? "Use the recommended available method" : "Review connection requirements and alternatives", 11, Ui.Muted));
                    identity.Spacing = 2;
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
                    row.Children.Add(identity);
                    var arrow = Ui.Text("›", 20, Ui.Muted); Grid.SetColumn(arrow, 1); row.Children.Add(arrow);
                    choose.Content = row;
                    choose.HorizontalAlignment = HorizontalAlignment.Stretch;
                    choose.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                    choose.Padding = new Thickness(14, 7); choose.MinHeight = 56;
                    AutomationProperties.SetName(choose, "Connect provider " + provider.Id);
                    rows.Children.Add(choose);
                }
                if (rows.Children.Count == 0) rows.Children.Add(Ui.Text("No providers match this search.", 13, Ui.Muted));
            }
            search.TextChanged += (_, _) => { _connectProviderSearch = search.Text ?? ""; Populate(); }; Populate();
            var content = Ui.Stack(Heading("Add account", "Choose a provider. Models and profiles are optional later."), search, rows,
                new Expander { Header = "Manual account or existing setup", Content = OverviewActions(Ui.Button("Manual API account", ShowAddAccount), Ui.Button("Import setup", ShowImport)) },
                Ui.Button("Back to accounts", () => { _editing = false; Render(); }));
            content.MaxWidth = 720;
            _page.Content = content;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!OwnsConnectionPresentation(request, navigation, ownerContent)) return;
            _page.Content = Ui.Stack(Heading("Connections unavailable", Friendly(error)), Ui.Button("Retry", ShowAddAccountChooser),
                Ui.Button("Back to accounts", () => { _editing = false; Render(); }));
        }
    }

    private async void BeginProviderSetup(string providerId) => await BeginProviderConnectionAsync(providerId);

    private async Task BeginProviderConnectionAsync(string providerId, AccountDto? reconnect = null)
    {
        var request = ++_connectionPresentationRequest;
        long navigation = -1;
        object? ownerContent = null;
        try
        {
            if (_section != "Accounts")
            {
                await NavigateToAsync(new("Accounts", AccountId: reconnect?.Id));
                if (request != _connectionPresentationRequest || _section != "Accounts" || _lifetime.IsCancellationRequested) return;
            }
            if (!CanOpenSectionEditor()) return;
            _notice.Text = "";
            ownerContent = _page.Content;
            navigation = _workspaceNavigationGeneration;
            var normalized = ProviderInventory.Canonical(providerId);
            var methodsTask = _client.ConnectionMethodsAsync(normalized, _lifetime.Token);
            var statusTask = _client.StatusAsync(_lifetime.Token);
            await Task.WhenAll(methodsTask, statusTask);
            if (!OwnsConnectionPresentation(request, navigation, ownerContent)) return;
            _connectionMethods = methodsTask.Result;
            _status = statusTask.Result;
            ShowConnectionMethods(normalized, reconnect);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (OwnsConnectionPresentation(request, navigation, ownerContent)) _notice.Text = Friendly(error);
        }
    }

    private bool ConnectionMethodAvailable(ProviderConnectionMethodDto method) => ConnectionRequirement(method) is null;
    private bool ConnectionMonitorIntent => IsMonitorMode || _status?.RuntimeMode == "monitor";

    private string? ConnectionRequirement(ProviderConnectionMethodDto method)
    {
        if (method.RequiresCore && ConnectionMonitorIntent)
            return "This sign-in needs the Gateway Core. This host is configured as an Account Monitor; choosing a login does not change that operating mode. Review Gateway setup deliberately, or choose another available quota method.";
        if (method.RequiresCore && _status?.Capabilities?.CoreLogin != true)
            return "The required Gateway Core is missing or its login capability has not been confirmed. No authorization request has been started. Review the installed Gateway requirements, then reload the available methods.";
        if (method.Availability != "available")
            return method.Guidance ?? "This connection method is not available on the installed host. Review its official requirements or choose another available method.";
        if (ConnectionMonitorIntent && !method.Purposes.Any(purpose => purpose is "quota" or "cost"))
            return "This method provides no qualified quota or cost access for Account Monitor. Review the provider's sources or deliberately review Gateway setup for model access.";
        return null;
    }

    private string[] ConnectionPurposes(ProviderConnectionMethodDto method) => ConnectionMonitorIntent
        ? method.Purposes.Where(purpose => purpose is "quota" or "cost").ToArray() : method.Purposes;
    private string ConnectProviderName(string id) => _providerCatalog?.Providers.FirstOrDefault(p => p.Id == id)?.Name ?? Lain.Client.ProviderDisplayNames.Get(id);

    private void ShowConnectionMethods(string provider, AccountDto? reconnect = null)
    {
        if (_section != "Accounts" || !CanOpenSectionEditor()) return;
        ++_connectionPresentationRequest;
        _notice.Text = "";
        _loginVisible = false;
        _editing = true; _workspace.Offset = default;
        var methods = _connectionMethods?.Methods.Where(m => m.Provider == provider).OrderByDescending(m => m.Recommended).ToArray() ?? [];
        if (methods.FirstOrDefault(ConnectionMethodAvailable) is { } preferred)
        {
            ShowConnectionMethod(preferred, reconnect);
            return;
        }
        var content = Ui.Stack(Heading(reconnect is null ? "Connect " + ConnectProviderName(provider) : "Reconnect " + reconnect.Name,
            "Connect only the access this method provides. No model request is sent."));
        if (methods.Length == 0) content.Children.Add(Ui.Text("No connection method is installed for this documented provider. Review its official requirements in Discover providers or use a deliberate manual setup.", 13, Ui.Muted));
        foreach (var method in methods)
        {
            var choose = Ui.Button(method.Title, () => ShowConnectionMethod(method, reconnect));
            choose.IsEnabled = ConnectionMethodAvailable(method);
            AutomationProperties.SetName(choose, "Connection method " + method.Id);
            var card = Ui.Stack(choose, Ui.Text(method.Description ?? string.Join(" · ", method.Purposes), 13, Ui.Muted));
            if (!choose.IsEnabled)
            {
                card.Children.Add(Ui.Text(ConnectionRequirement(method)!, 12, Ui.Warning));
                card.Children.Add(Ui.Button("Review requirements", () => ShowConnectionPrerequisite(method, reconnect)));
            }
            content.Children.Add(Ui.Card(card));
        }
        content.Children.Add(Ui.Button("Back to providers", ShowAddAccountChooser));
        _page.Content = content;
    }

    private void ShowConnectionPrerequisite(ProviderConnectionMethodDto method, AccountDto? reconnect, bool quotaOnly = false)
    {
        if (_section != "Accounts" || !CanOpenSectionEditor()) return;
        ++_connectionPresentationRequest;
        _notice.Text = "";
        _loginVisible = false; _editing = true; _actions.Clear(); _workspace.Offset = default;
        if (_configuration is { } configuration) RegisterEditor(() => ShowConnectionPrerequisite(method, reconnect, quotaOnly), configuration.Revision);
        var details = Ui.Stack(Ui.Text(ConnectionRequirement(method) ?? "Review the selected method before continuing.", 13, Ui.Warning),
            Ui.Text("This step has not started a new sign-in. Existing accounts, quota access and host operating mode are preserved.", 12, Ui.Muted));
        if (!string.IsNullOrWhiteSpace(method.Guidance)) details.Children.Add(Ui.Text(method.Guidance, 12, Ui.Muted));
        if (method.DocumentationUrl is { } url && Uri.TryCreate(url, UriKind.Absolute, out var link) && link.Scheme == Uri.UriSchemeHttps)
            details.Children.Add(Ui.Button("Official connection guidance", async () => await OpenProviderLinkAsync(link)));
        details.Children.Add(Ui.Button("Review provider sources", ShowProviderCatalog));
        if (method.RequiresCore || ConnectionMonitorIntent && !method.Purposes.Any(purpose => purpose is "quota" or "cost"))
            details.Children.Add(Ui.Button("Review Gateway setup", async () =>
            {
                await NavigateToAsync(new("Settings"));
                if (_section == "Settings") ShowRuntimeModeSettings();
            }));
        foreach (var alternative in _connectionMethods?.Methods.Where(candidate => candidate.Provider == method.Provider && candidate.Id != method.Id
            && ConnectionMethodAvailable(candidate) && (!quotaOnly || candidate.Purposes.Any(purpose => purpose is "quota" or "cost"))) ?? [])
        {
            var choose = Ui.Button(alternative.Title, () => ShowConnectionMethod(alternative, reconnect, quotaOnly));
            AutomationProperties.SetName(choose, "Connection method " + alternative.Id);
            details.Children.Add(choose);
        }
        details.Children.Add(OverviewActions(Action("Reload connection methods", async () =>
            {
                if (await CheckConnectionPrerequisitesAsync(method.Provider, method.Id, reconnect, quotaOnly))
                    ShowConnectionMethod(_connectionMethods!.Methods.Single(candidate => candidate.Provider == method.Provider && candidate.Id == method.Id), reconnect, quotaOnly);
            }, "Connection methods refreshed. Review before connecting.", stayOnPage: true),
            Ui.Button("Back to providers", ShowAddAccountChooser), Ui.Button("Back to accounts", () => { _editing = false; Render(); })));
        var content = Ui.Stack(Heading("Connection requirements", ConnectProviderName(method.Provider) + " · " + method.Title), Ui.Card(details));
        content.MaxWidth = 720;
        AutomationProperties.SetName(content, "Connection prerequisites " + method.Id);
        _page.Content = content;
    }

    private void ShowConnectionMethod(ProviderConnectionMethodDto method, AccountDto? reconnect, bool quotaOnly = false)
    {
        if (_section != "Accounts" || !CanOpenSectionEditor()) return;
        ++_connectionPresentationRequest;
        if (!ConnectionMethodAvailable(method)) { ShowConnectionPrerequisite(method, reconnect, quotaOnly); return; }
        _notice.Text = "";
        _loginVisible = false;
        if (_configuration is not { } cfg) return;
        RegisterEditor(() => ShowConnectionMethod(method, reconnect, quotaOnly), cfg.Revision);
        _editing = true; _workspace.Offset = default;
        var draftKey = method.Provider + "|" + (reconnect?.Id ?? "new") + (quotaOnly ? "|quota" : "");
        _connectSafeDrafts.TryGetValue(draftKey, out var draft);
        var name = Ui.Input(draft.Name ?? reconnect?.Name ?? Lain.Client.AccountNameSuggestions.Suggest(method.Provider, ConnectProviderName(method.Provider), cfg.Accounts.Select(a => a.Name)));
        var secret = Ui.Input(hint: method.SecretKind ?? "Credential"); secret.PasswordChar = '●';
        var expiry = Ui.Input(hint: "2026-12-31T23:59:00Z");
        var settings = new Dictionary<string, Control>(StringComparer.Ordinal);
        var form = AccountForm(method.Title, method.Description ?? "Connect this provider to your local host.", AccountFormRow("Account name", name));
        foreach (var field in method.RequiredSettings ?? [])
        {
            Control input = field.Choices is { Length: > 0 } choices
                ? new ComboBox { ItemsSource = choices, SelectedItem = draft.Settings?.GetValueOrDefault(field.Id) ?? (choices.Length == 1 ? choices[0] : null), PlaceholderText = "Choose " + field.Label }
                : Ui.Input(draft.Settings?.GetValueOrDefault(field.Id) ?? (field.Id == "region" ? reconnect?.Binding?.Region : field.Id is "accountId" or "workspaceId" or "workspace_id" ? reconnect?.Binding?.ResourceId : null), field.Required ? "Required" : "Optional");
            settings.Add(field.Id, input);
            form.Children.Add(AccountFormRow(field.Label + (field.Required ? " *" : ""), input));
            if (field.Help is not null) form.Children.Add(Ui.Text(field.Help, 12, Ui.Muted));
        }
        var purposes = quotaOnly ? method.Purposes.Where(purpose => purpose is "quota" or "cost").ToArray() : ConnectionPurposes(method);
        form.Children.Add(Ui.Text("Available purposes: " + string.Join(" · ", purposes), 12, Ui.Muted));
        if (method.SecretRequired) form.Children.Add(AccountFormRow(method.SecretKind ?? "Credential", secret));
        if (method.SecretKind == "access-token-snapshot") form.Children.Add(AccountFormRow("Snapshot expires at", expiry));
        if (method.DocumentationUrl is { } url && Uri.TryCreate(url, UriKind.Absolute, out var link) && link.Scheme == "https")
            form.Children.Add(Ui.Button("Official connection guidance", async () => await OpenProviderLinkAsync(link)));
        var operationKey = Guid.NewGuid().ToString("N");
        void SaveSafeDraft()
        {
            _connectSafeDrafts[draftKey] = (name.Text ?? "", settings.ToDictionary(p => p.Key,
                p => p.Value switch { TextBox box => box.Text ?? "", ComboBox box => box.SelectedItem as string ?? "", _ => "" }, StringComparer.Ordinal));
        }
        var connect = Action(method.SecretRequired ? "Connect account" : method.Kind == "browser" ? "Continue in browser" : "Continue sign-in", async () => {
            SaveSafeDraft();
            if (method.SecretRequired && !await CheckConnectionPrerequisitesAsync(method.Provider, method.Id, reconnect, quotaOnly))
            {
                secret.Text = "";
                return;
            }
            if (method.SecretRequired)
            {
                var current = _connectionMethods!.Methods.Single(candidate => candidate.Provider == method.Provider && candidate.Id == method.Id);
                if (!current.SecretRequired || current.SecretKind != method.SecretKind || purposes.Any(purpose => !ConnectionPurposes(current).Contains(purpose, StringComparer.Ordinal)))
                    throw new InvalidOperationException("The selected credential access changed. Your inputs are kept. Review this provider's current connection method before connecting.");
            }
            if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidOperationException("Enter an account name.");
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in method.RequiredSettings ?? [])
            {
                var value = settings[field.Id] switch { TextBox box => box.Text?.Trim(), ComboBox box => box.SelectedItem as string, _ => null };
                if (field.Required && string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Enter " + field.Label + ".");
                if (!string.IsNullOrWhiteSpace(value)) values[field.Id] = value;
            }
            var resourceKey = values.ContainsKey("projectId") ? "projectId" : values.ContainsKey("accountId") ? "accountId" : "resourceId";
            var binding = reconnect?.Binding ?? (values.Count == 0 ? null : new ResourceBindingDto(method.Provider,
                Product: values.GetValueOrDefault("product"), Region: values.GetValueOrDefault("region"),
                ResourceType: resourceKey == "projectId" ? "project" : resourceKey == "accountId" ? "account" : null,
                ResourceId: values.GetValueOrDefault(resourceKey)));
            if (method.SecretRequired)
            {
                if (string.IsNullOrWhiteSpace(secret.Text)) throw new InvalidOperationException("Enter the credential privately.");
                DateTimeOffset? expiresAt = null;
                if (method.SecretKind == "access-token-snapshot")
                {
                    if (!DateTimeOffset.TryParse(expiry.Text, out var suppliedExpiry) || suppliedExpiry <= DateTimeOffset.UtcNow) throw new InvalidOperationException("Enter the snapshot's future expiry with a time zone.");
                    expiresAt = suppliedExpiry;
                }
                var metadata = new ConnectAccountRequest(method.Provider, method.Id, purposes, name.Text.Trim(), reconnect?.Id,
                    binding, reconnect?.Generation, operationKey);
                var saved = await _client.ConnectAccountAsync(cfg.Revision, new(metadata, secret.Text, method.SecretKind ?? "api-key", ExpiresAt: expiresAt, BaseUrl: method.DefaultBaseUrl, Settings: values), _lifetime.Token);
                secret.Text = ""; _configuration = saved; _connectSafeDrafts.Remove(draftKey);
                var account = saved.Accounts.FirstOrDefault(a => a.Id == reconnect?.Id) ?? saved.Accounts.FirstOrDefault(a => !cfg.Accounts.Any(old => old.Id == a.Id));
                _editing = true;
                if (account is not null) await ShowConnectionResultAsync(account.Id);
            }
            else
            {
                await StartConnectionLoginAsync(new(cfg.Revision, method.Provider, method.Id, purposes, reconnect?.Id, binding,
                    reconnect?.Generation, name.Text.Trim(), operationKey));
            }
        }, method.SecretRequired ? "Credential connected." : "Authorization started. Waiting for completion.", stayOnPage: true);
        form.Children.Add(OverviewActions(connect, Ui.Button(quotaOnly ? "Back to quota access" : "Back to providers", () =>
        {
            SaveSafeDraft(); secret.Text = "";
            if (quotaOnly && reconnect is not null) _ = RunAsync(() => OpenMonitorSetupAsync(reconnect), "Review quota access.", stayOnPage: true);
            else ShowAddAccountChooser();
        }),
            Ui.Button("Cancel", () => { SaveSafeDraft(); secret.Text = ""; _editing = false; Render(); })));
        var alternatives = Ui.Stack();
        foreach (var alternative in _connectionMethods?.Methods.Where(m => m.Provider == method.Provider && m.Id != method.Id
            && (!quotaOnly || m.Purposes.Any(purpose => purpose is "quota" or "cost"))) ?? [])
        {
            var choose = Ui.Button(alternative.Title, () => { SaveSafeDraft(); secret.Text = ""; ShowConnectionMethod(alternative, reconnect, quotaOnly); });
            choose.IsEnabled = ConnectionMethodAvailable(alternative);
            AutomationProperties.SetName(choose, "Connection method " + alternative.Id);
            alternatives.Children.Add(choose);
            alternatives.Children.Add(Ui.Text(choose.IsEnabled ? alternative.Description ?? string.Join(" · ", alternative.Purposes)
                : ConnectionRequirement(alternative)!, 12, choose.IsEnabled ? Ui.Muted : Ui.Warning));
            if (!choose.IsEnabled) alternatives.Children.Add(Ui.Button("Review requirements for " + alternative.Title, () =>
            {
                SaveSafeDraft(); secret.Text = "";
                ShowConnectionPrerequisite(alternative, reconnect, quotaOnly);
            }));
        }
        if (alternatives.Children.Count > 0) form.Children.Add(new Expander { Header = "Other connection methods", Content = alternatives });
        form.Children.Add(Ui.Text("Connecting does not choose models, apply a profile, purchase credits or test inference.", 12, Ui.Muted));
        _page.Content = form;
    }

    private async Task ShowConnectionResultAsync(string accountId)
    {
        if (_configuration?.Accounts.FirstOrDefault(a => a.Id == accountId) is not { } account) return;
        _notice.Text = "";
        _loginVisible = false;
        try { _quotas = await _client.QuotasAsync(_lifetime.Token); } catch (Exception error) when (error is not OperationCanceledException) { _quotaError = Friendly(error); }
        _workspace.Offset = default;
        var details = Ui.Stack(Ui.Text(account.Name, 18), Ui.Text(ConnectProviderName(account.Provider) + " · Credential connected", 13, Ui.Accent));
        foreach (var capability in account.Capabilities ?? [])
        {
            var observed = capability.State is "ready" or "available";
            var state = observed ? "Available" : capability.State is "unverified" or "connected" ? "Not verified" : capability.State;
            details.Children.Add(Ui.Text(capability.Purpose + " · " + state, 12, observed ? Ui.Accent : Ui.Muted));
        }
        var quotas = _quotas.Where(q => q.AccountId == accountId).ToArray();
        if (quotas.Length == 0) details.Children.Add(Ui.Text("Quota has not been observed. You can refresh it from the account.", 13, Ui.Muted));
        foreach (var quota in Lain.Client.QuotaPresentation.Order(quotas)) details.Children.Add(AccountTileWindow(account, quota));
        var done = Ui.PrimaryButton("Done", () => { _editing = false; Render(); });
        var actions = OverviewActions(done);
        if (!IsMonitorMode && account.Capabilities?.Any(c => c.Purpose == "inference" && c.State is "ready" or "available" or "unverified" or "connected") == true)
            actions.Children.Add(Ui.Button("Choose models (optional)", () => _ = ShowAccountModelsAsync(accountId)));
        details.Children.Add(Ui.Text("Quota observations and local credential presence do not prove successful inference.", 11, Ui.Muted));
        details.Children.Add(actions);
        var content = Ui.Stack(Heading("Connection saved", "Review the observed result. Models are optional."), Ui.Card(details));
        content.MaxWidth = 680;
        _page.Content = content;
    }
}
