using Avalonia.Automation;
using Avalonia.Controls;
using Lain.Core;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    private ConnectionMethodCatalogDto? _connectionMethods;
    private readonly Dictionary<string, (string Name, Dictionary<string, string> Settings)> _connectSafeDrafts = new(StringComparer.Ordinal);

    private async void ShowAddAccountChooser()
    {
        _editing = true;
        _loginVisible = false;
        _actions.Clear();
        _workspace.Offset = default;
        _page.Content = Ui.Stack(Heading("Add account", "Loading available provider connections…"));
        try
        {
            var methods = await _client.ConnectionMethodsAsync(ct: _lifetime.Token);
            _connectionMethods = methods;
            if (!_editing || _section != "Accounts") return;
            var search = Ui.Input(hint: "Search providers");
            AutomationProperties.SetName(search, "Choose account provider");
            var rows = Ui.Stack();
            void Populate()
            {
                rows.Children.Clear();
                foreach (var group in methods.Methods.GroupBy(m => m.Provider).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Key.Contains(search.Text?.Trim() ?? "", StringComparison.OrdinalIgnoreCase)))
                {
                    var available = group.Any(ConnectionMethodAvailable);
                    var choose = Ui.Button(ConnectProviderName(group.Key), () => ShowConnectionMethods(group.Key));
                    AutomationProperties.SetName(choose, "Connect provider " + group.Key);
                    rows.Children.Add(Ui.Card(Ui.Stack(choose, Ui.Text(available ? "Choose a connection method" : "View connection requirements and alternatives", 12, Ui.Muted))));
                }
                if (rows.Children.Count == 0) rows.Children.Add(Ui.Text("No providers match this search.", 13, Ui.Muted));
            }
            search.TextChanged += (_, _) => Populate(); Populate();
            _page.Content = Ui.Stack(Heading("Add account", "Choose a provider. Models and profiles are optional later."), search, rows,
                new Expander { Header = "Manual account or existing setup", Content = OverviewActions(Ui.Button("Manual API account", ShowAddAccount), Ui.Button("Import setup", ShowImport)) },
                Ui.Button("Back to accounts", () => { _editing = false; Render(); }));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            _page.Content = Ui.Stack(Heading("Connections unavailable", Friendly(error)), Ui.Button("Retry", ShowAddAccountChooser),
                Ui.Button("Back to accounts", () => { _editing = false; Render(); }));
        }
    }

    private async void BeginProviderSetup(string providerId) => await BeginProviderConnectionAsync(providerId);

    private async Task BeginProviderConnectionAsync(string providerId, AccountDto? reconnect = null)
    {
        try
        {
            var normalized = providerId switch { "command-code" => "commandcode", "mimo" => "xiaomi-mimo", _ => providerId };
            _connectionMethods = await _client.ConnectionMethodsAsync(normalized, _lifetime.Token);
            if (_connectionMethods.Methods.Length == 0) throw new InvalidOperationException("No connection method is installed for this provider.");
            ShowConnectionMethods(normalized, reconnect);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { _notice.Text = Friendly(error); }
    }

    private bool ConnectionMethodAvailable(ProviderConnectionMethodDto method) => method.Availability == "available"
        && (!method.RequiresCore || (_status?.Capabilities?.CoreLogin ?? !IsMonitorMode))
        && (!IsMonitorMode || method.Purposes.Any(purpose => purpose is "quota" or "cost"));

    private string[] ConnectionPurposes(ProviderConnectionMethodDto method) => IsMonitorMode
        ? method.Purposes.Where(purpose => purpose is "quota" or "cost").ToArray() : method.Purposes;
    private string ConnectProviderName(string id) => _providerCatalog?.Providers.FirstOrDefault(p => p.Id == id)?.Name ?? Lain.Client.ProviderDisplayNames.Get(id);

    private void ShowConnectionMethods(string provider, AccountDto? reconnect = null)
    {
        _editing = true; _workspace.Offset = default;
        var methods = _connectionMethods?.Methods.Where(m => m.Provider == provider).OrderByDescending(m => m.Recommended).ToArray() ?? [];
        if (methods.FirstOrDefault(ConnectionMethodAvailable) is { } preferred)
        {
            ShowConnectionMethod(preferred, reconnect);
            return;
        }
        var content = Ui.Stack(Heading(reconnect is null ? "Connect " + ConnectProviderName(provider) : "Reconnect " + reconnect.Name,
            "Connect only the access this method provides. No model request is sent."));
        foreach (var method in methods)
        {
            var choose = Ui.Button(method.Title, () => ShowConnectionMethod(method, reconnect));
            choose.IsEnabled = ConnectionMethodAvailable(method);
            AutomationProperties.SetName(choose, "Connection method " + method.Id);
            var card = Ui.Stack(choose, Ui.Text(method.Description ?? string.Join(" · ", method.Purposes), 13, Ui.Muted));
            if (!choose.IsEnabled) card.Children.Add(Ui.Text(method.Guidance ?? "This method is not available in the installed host. Choose another listed method.", 12, Ui.Warning));
            content.Children.Add(Ui.Card(card));
        }
        content.Children.Add(Ui.Button("Back to providers", ShowAddAccountChooser));
        _page.Content = content;
    }

    private void ShowConnectionMethod(ProviderConnectionMethodDto method, AccountDto? reconnect)
    {
        if (_configuration is not { } cfg) return;
        RegisterEditor(() => ShowConnectionMethod(method, reconnect), cfg.Revision);
        _editing = true; _workspace.Offset = default;
        var draftKey = method.Provider + "|" + (reconnect?.Id ?? "new");
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
        form.Children.Add(Ui.Text("Available purposes: " + string.Join(" · ", method.Purposes), 12, Ui.Muted));
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
                var metadata = new ConnectAccountRequest(method.Provider, method.Id, ConnectionPurposes(method), name.Text.Trim(), reconnect?.Id,
                    binding, reconnect?.Generation, operationKey);
                var saved = await _client.ConnectAccountAsync(cfg.Revision, new(metadata, secret.Text, method.SecretKind ?? "api-key", ExpiresAt: expiresAt, BaseUrl: method.DefaultBaseUrl, Settings: values), _lifetime.Token);
                secret.Text = ""; _configuration = saved; _connectSafeDrafts.Remove(draftKey);
                var account = saved.Accounts.FirstOrDefault(a => a.Id == reconnect?.Id) ?? saved.Accounts.FirstOrDefault(a => !cfg.Accounts.Any(old => old.Id == a.Id));
                _editing = true;
                if (account is not null) await ShowConnectionResultAsync(account.Id);
            }
            else
            {
                await StartConnectionLoginAsync(new(cfg.Revision, method.Provider, method.Id, ConnectionPurposes(method), reconnect?.Id, binding,
                    reconnect?.Generation, name.Text.Trim(), operationKey));
            }
        }, "Connection saved.", stayOnPage: true);
        form.Children.Add(OverviewActions(connect, Ui.Button("Back to providers", () => { SaveSafeDraft(); secret.Text = ""; ShowAddAccountChooser(); }),
            Ui.Button("Cancel", () => { SaveSafeDraft(); secret.Text = ""; _editing = false; Render(); })));
        var alternatives = Ui.Stack();
        foreach (var alternative in _connectionMethods?.Methods.Where(m => m.Provider == method.Provider && m.Id != method.Id) ?? [])
        {
            var choose = Ui.Button(alternative.Title, () => { SaveSafeDraft(); secret.Text = ""; ShowConnectionMethod(alternative, reconnect); });
            choose.IsEnabled = ConnectionMethodAvailable(alternative);
            AutomationProperties.SetName(choose, "Connection method " + alternative.Id);
            alternatives.Children.Add(choose);
            alternatives.Children.Add(Ui.Text(choose.IsEnabled ? alternative.Description ?? string.Join(" · ", alternative.Purposes)
                : alternative.Guidance ?? "This method is not available on the installed host. Use the current method or review its official prerequisites.", 12, choose.IsEnabled ? Ui.Muted : Ui.Warning));
        }
        if (alternatives.Children.Count > 0) form.Children.Add(new Expander { Header = "Other connection methods", Content = alternatives });
        form.Children.Add(Ui.Text("Connecting does not choose models, apply a profile, purchase credits or test inference.", 12, Ui.Muted));
        _page.Content = form;
    }

    private async Task ShowConnectionResultAsync(string accountId)
    {
        if (_configuration?.Accounts.FirstOrDefault(a => a.Id == accountId) is not { } account) return;
        try { _quotas = await _client.QuotasAsync(_lifetime.Token); } catch (Exception error) when (error is not OperationCanceledException) { _quotaError = Friendly(error); }
        var content = Ui.Stack(Heading(account.Name, "Credential connected. Quota observations are shown separately from local storage."));
        foreach (var capability in account.Capabilities ?? []) content.Children.Add(Ui.Text(capability.Purpose + " · " + capability.State, 13, capability.State is "ready" or "available" ? Ui.Accent : Ui.Muted));
        var quotas = _quotas.Where(q => q.AccountId == accountId).ToArray();
        if (quotas.Length == 0) content.Children.Add(Ui.Text("Quota has not been observed. You can refresh it from the account.", 13, Ui.Muted));
        foreach (var quota in quotas) content.Children.Add(AccountTileWindow(account, quota));
        var done = Ui.PrimaryButton("Done", () => { _editing = false; Render(); });
        var actions = OverviewActions(done);
        if (!IsMonitorMode && account.Capabilities?.Any(c => c.Purpose == "inference" && c.State is "ready" or "available" or "unverified" or "connected") == true)
            actions.Children.Add(Ui.Button("Choose models (optional)", () => _ = ShowAccountModelsAsync(accountId)));
        content.Children.Add(actions); _page.Content = content;
    }
}
