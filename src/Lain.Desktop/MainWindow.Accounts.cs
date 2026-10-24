using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Lain.Client;
using Lain.Core;
using Lain.Desktop.Presentation;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    private string _accountSearch = "";
    private string _accountProvider = "All providers";
    private string _accountStatus = "All states";
    private bool _accountFiltersExpanded;
    private string[]? _fiveHourAccountOrder;

    private Action CaptureAccountsNavigation()
    {
        var search = _accountSearch;
        var provider = _accountProvider;
        var status = _accountStatus;
        var filtersExpanded = _accountFiltersExpanded;
        return () =>
        {
            _accountSearch = search;
            _accountProvider = provider;
            _accountStatus = status;
            _accountFiltersExpanded = filtersExpanded;
            _accountNavigationId = null;
        };
    }

    private Control AccountsPage()
    {
        var options = PresentationFor("Accounts");
        var panel = Ui.Stack(Ui.PageHeader("Accounts", null,
            Ui.Button("Discover providers", ShowProviderCatalog),
            Ui.PrimaryButton("Add account", ShowAddAccountChooser)));
        panel.Spacing = options.Density == ViewDensity.Compact ? 8 : 12;
        if (_login is { State: "waiting" }) panel.Children.Add(Action("Continue Codex sign-in", StartCodexLoginAsync,
            "Complete sign-in in your browser.", stayOnPage: true));
        if (_configuration is not { } cfg)
        {
            panel.Children.Add(Empty("Connect to the local host to load or add accounts. You can discover providers without an account."));
            return panel;
        }
        if (cfg.Accounts.Length == 0)
        {
            panel.Children.Add(Empty("Connect your first account to choose models and create a client profile. Add account offers browser sign-in, an API key or an existing setup import."));
            return panel;
        }

        if (!_hostAvailable || _quotaError is not null) panel.Children.Add(Ui.Text(BudgetSnapshotNote(), 12, Ui.Warning));
        if (!cfg.Accounts.Any(account => account.Enabled))
            panel.Children.Add(Ui.Text("All accounts are disabled. Open an account and enable it before using its models.", 12, Ui.Warning));

        var search = Ui.Input(_accountSearch, "Search accounts or providers");
        AutomationProperties.SetName(search, "Search accounts");
        var providerNames = new[] { "All providers" }.Concat(cfg.Accounts.Select(ProviderName)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (!providerNames.Contains(_accountProvider, StringComparer.OrdinalIgnoreCase)) _accountProvider = providerNames[0];
        var provider = new ComboBox { ItemsSource = providerNames, SelectedItem = _accountProvider, MinWidth = 145 };
        var status = new ComboBox { ItemsSource = new[] { "All states", "Enabled", "Disabled", "Needs attention", "Pinned" }, SelectedItem = _accountStatus, MinWidth = 140 };
        var order = new ComboBox { ItemsSource = new[] { "Pinned first", "Provider then name", "Name", "5h usage" }, SelectedIndex = _preferences.AccountSort switch { "provider" => 1, "name" => 2, "five-hour" => 3, _ => 0 }, MinWidth = 155 };
        AutomationProperties.SetName(provider, "Filter accounts by provider");
        AutomationProperties.SetName(status, "Filter accounts by state");
        AutomationProperties.SetName(order, "Account order");
        var group = new CheckBox { Content = "Group by provider", IsChecked = _preferences.GroupAccountsByProvider, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(group, "Group accounts by provider");
        var clear = Ui.Button("Clear filters", () => { search.Text = ""; provider.SelectedIndex = 0; status.SelectedIndex = 0; });
        var updateFiveHourOrder = new Button { Content = "Update 5h order" };
        AutomationProperties.SetName(updateFiveHourOrder, "Update 5h order");
        ToolTip.SetTip(updateFiveHourOrder, "Sort by highest current 5h consumption. Last known and unknown follow; polling keeps the chosen order.");
        var filterToggle = new ToggleButton
        {
            Content = "Filters and order", IsChecked = _accountFiltersExpanded,
            VerticalAlignment = VerticalAlignment.Stretch, Padding = new Thickness(10, 5)
        };
        AutomationProperties.SetName(filterToggle, "Account filters and order");
        AutomationProperties.SetHelpText(filterToggle, "Show or hide provider and account state filters, ordering and provider grouping.");
        var searchRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        searchRow.Children.Add(search);
        Grid.SetColumn(filterToggle, 1); searchRow.Children.Add(filterToggle);
        panel.Children.Add(searchRow);
        var filterOptions = OverviewActions(provider, status, order, updateFiveHourOrder, group, clear);
        filterOptions.IsVisible = _accountFiltersExpanded;
        filterToggle.IsCheckedChanged += (_, _) =>
        {
            _accountFiltersExpanded = filterToggle.IsChecked == true;
            filterOptions.IsVisible = _accountFiltersExpanded;
        };
        panel.Children.Add(filterOptions);
        var activeFilters = new WrapPanel { Orientation = Orientation.Horizontal, IsVisible = false };
        AutomationProperties.SetName(activeFilters, "Active account filters");
        panel.Children.Add(activeFilters);
        var count = Ui.Text("", 12, Ui.Muted);
        var inventory = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(count);
        panel.Children.Add(inventory);
        var previousInventoryActions = new List<Button>();

        void Populate()
        {
            foreach (var action in previousInventoryActions) _actions.Remove(action);
            previousInventoryActions.Clear();
            var actionsBefore = _actions.ToHashSet();
            updateFiveHourOrder.IsVisible = _preferences.AccountSort == "five-hour";
            var found = OrderedAccounts(cfg.Accounts).Where(account =>
                (_accountProvider == "All providers" || ProviderName(account).Equals(_accountProvider, StringComparison.OrdinalIgnoreCase))
                && AccountMatchesStatus(account)
                && $"{account.Name} {account.Provider} {ProviderName(account)} {account.Id}".Contains(_accountSearch.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
            activeFilters.Children.Clear();
            void AddFilterChip(string label, string action, Action remove)
            {
                var chip = Ui.Button(label + " ×", remove);
                chip.Margin = new Thickness(0, 0, 6, 4);
                chip.Padding = new Thickness(8, 3);
                AutomationProperties.SetName(chip, action);
                activeFilters.Children.Add(chip);
            }
            if (!string.IsNullOrWhiteSpace(_accountSearch)) AddFilterChip("Search: " + _accountSearch.Trim(), "Clear account search", () => search.Text = "");
            if (_accountProvider != "All providers") AddFilterChip(_accountProvider, "Clear account provider filter", () => provider.SelectedIndex = 0);
            if (_accountStatus != "All states") AddFilterChip(_accountStatus, "Clear account state filter", () => status.SelectedIndex = 0);
            activeFilters.IsVisible = activeFilters.Children.Count > 0;
            count.Text = $"{found.Length} of {cfg.Accounts.Length} accounts · {cfg.Accounts.Count(account => account.Enabled)} enabled"
                + " · " + (_preferences.AccountSort switch { "name" => "Ordered by name", "provider" => "Ordered by provider and name", "five-hour" => "5h usage · order held until updated", _ => "Pinned first, then provider and name" });
            if (found.Length == 0)
            {
                inventory.Content = Empty("No accounts match these filters. Clear filters to see the full inventory.");
                return;
            }
            if (!_preferences.GroupAccountsByProvider)
            {
                inventory.Content = AccountTiles(cfg, found, Populate);
                previousInventoryActions.AddRange(_actions.Where(action => !actionsBefore.Contains(action)));
                return;
            }
            var groups = new StackPanel { Spacing = options.Density == ViewDensity.Compact ? 12 : 18 };
            var pinned = _preferences.AccountSort == "favorites" ? found.Where(IsAccountPinned).ToArray() : [];
            if (pinned.Length > 0) groups.Children.Add(Ui.Section("Pinned", AccountTiles(cfg, pinned, Populate)));
            var providerGroups = found.Where(account => !pinned.Contains(account)).GroupBy(ProviderName);
            // In 5h mode, the first account supplies the group's highest ranked
            // actual 5h window. Shared or separate budgets are never summed.
            if (_preferences.AccountSort != "five-hour") providerGroups = providerGroups.OrderBy(providerGroup => providerGroup.Key, StringComparer.OrdinalIgnoreCase);
            foreach (var accounts in providerGroups)
                groups.Children.Add(Ui.Section(accounts.Key, AccountTiles(cfg, accounts.ToArray(), Populate)));
            inventory.Content = groups;
            previousInventoryActions.AddRange(_actions.Where(action => !actionsBefore.Contains(action)));
        }
        search.TextChanged += (_, _) => { _accountSearch = search.Text ?? ""; Populate(); };
        updateFiveHourOrder.Click += (_, _) => { CaptureFiveHourOrder(cfg.Accounts); Populate(); };
        provider.SelectionChanged += (_, _) => { _accountProvider = provider.SelectedItem as string ?? "All providers"; Populate(); };
        status.SelectionChanged += (_, _) => { _accountStatus = status.SelectedItem as string ?? "All states"; Populate(); };
        order.SelectionChanged += (_, _) =>
        {
            SavePreferences(current => current with { AccountSort = order.SelectedIndex switch { 1 => "provider", 2 => "name", 3 => "five-hour", _ => "favorites" } }, render: false);
            if (_preferences.AccountSort == "five-hour") CaptureFiveHourOrder(cfg.Accounts);
            Populate();
        };
        group.IsCheckedChanged += (_, _) => { SavePreferences(current => current with { GroupAccountsByProvider = group.IsChecked == true }, render: false); Populate(); };
        Populate();
        return panel;
    }

    private IEnumerable<AccountDto> OrderedAccounts(IEnumerable<AccountDto> accounts)
    {
        if (_preferences.AccountSort == "five-hour")
        {
            var rows = accounts.ToArray();
            if (_fiveHourAccountOrder is null) CaptureFiveHourOrder(rows);
            var positions = _fiveHourAccountOrder!.Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index, StringComparer.Ordinal);
            // New accounts append stably until the user explicitly updates the
            // snapshot. Polling never promotes or moves an existing account.
            return rows.OrderBy(account => positions.GetValueOrDefault(account.Id, int.MaxValue))
                .ThenBy(ProviderName, StringComparer.OrdinalIgnoreCase).ThenBy(AccountDisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(account => account.Id, StringComparer.Ordinal);
        }
        if (_preferences.AccountSort == "name") return accounts.OrderBy(AccountDisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(ProviderName, StringComparer.OrdinalIgnoreCase).ThenBy(account => account.Id, StringComparer.Ordinal);
        if (_preferences.AccountSort == "provider") return accounts.OrderBy(ProviderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(AccountDisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(account => account.Id, StringComparer.Ordinal);
        var pins = _preferences.PinnedAccountIds.Select((id, position) => (id, position))
            .GroupBy(item => item.id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First().position, StringComparer.Ordinal);
        // Quota availability and consumption deliberately have no bearing on order.
        return accounts.OrderBy(account => pins.TryGetValue(account.Id, out var position) ? position : int.MaxValue)
            .ThenBy(ProviderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(AccountDisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(account => account.Id, StringComparer.Ordinal);
    }

    private void CaptureFiveHourOrder(IEnumerable<AccountDto> accounts)
    {
        _fiveHourAccountOrder = accounts.Select(account => (Account: account, Window: FiveHourWindow(account.Id)))
            .OrderBy(row => row.Window is not null && QuotaUsedPercent(row.Window).HasValue
                ? CurrentFiveHourWindow(row.Window) ? 0 : 1 : 2)
            .ThenByDescending(row => QuotaUsedPercent(row.Window))
            .ThenBy(row => ProviderName(row.Account), StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => AccountDisplayName(row.Account), StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Account.Id, StringComparer.Ordinal)
            .Select(row => row.Account.Id).ToArray();
    }

    private bool IsAccountPinned(AccountDto account) => _preferences.PinnedAccountIds.Contains(account.Id, StringComparer.Ordinal);

    private bool AccountMatchesStatus(AccountDto account) => _accountStatus switch
    {
        "Enabled" => account.Enabled,
        "Disabled" => !account.Enabled,
        "Pinned" => IsAccountPinned(account),
        "Needs attention" => AccountNeedsAttention(account),
        _ => true
    };

    private bool AccountNeedsAttention(AccountDto account)
    {
        if (!account.Enabled || AccountCredentialWarning(account) is not null) return true;
        var quotas = _quotas.Where(quota => quota.AccountId == account.Id).ToArray();
        return quotas.Length == 0 || quotas.Any(quota => LastKnownQuota(quota) || QuotaUsedPercent(quota) >= 90
            || QuotaUnavailable(quota) || !string.IsNullOrWhiteSpace(quota.Error)
            || !QuotaUsedPercent(quota).HasValue && !ValidAmount(quota.Remaining).HasValue);
    }

    private static string? AccountCredentialWarning(AccountDto account)
    {
        if (account.ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow) return "Credential expired · reconnect this account.";
        return account.State.ToLowerInvariant() switch
        {
            "ready" or "ok" or "available" => null,
            "missing" or "missing-credential" => "Credential missing · reconnect this account.",
            "expired" => "Credential expired · reconnect this account.",
            _ => $"Credential state: {account.State}. Check credential details."
        };
    }

    private static TextBlock AccountCell(string text, double size, IBrush? color = null) => new()
    {
        Text = text, FontSize = size, Foreground = color ?? Ui.Ink,
        TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
    };

    private Control AccountDetail(AccountDto account, long revision, Action? inventoryChanged = null)
    {
        var options = PresentationFor("Accounts");
        var content = new StackPanel { Spacing = options.Density == ViewDensity.Compact ? 6 : 8 };
        var quotas = _quotas.Where(q => q.AccountId == account.Id).ToArray();
        if (account.SharedWithLegacy || quotas.Any(quota => quota.SharedWithLegacy))
            content.Children.Add(Ui.Text("This account shares the provider budget with the imported connection. These balances describe the same budget and must not be added together.", 12, Ui.Warning));
        else if (account.BudgetPoolId is not null || quotas.Any(quota => quota.BudgetPoolId is not null))
            content.Children.Add(Ui.Text("This account uses a provider budget pool. Balances can be shared across accounts using that pool; do not assume separate capacity.", 12, Ui.Muted));
        var removal = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        void ConfirmAccountRemoval()
        {
            var remove = Action("Remove", async () => { _configuration = await _client.RemoveAccountAsync(revision, account.Id, _lifetime.Token); }, "Account removed. Historical usage remains available.");
            remove.Classes.Add("danger");
            removal.Content = Ui.Card(Ui.Stack(Ui.Text($"Remove {account.Name}?", 13, Ui.Warning),
                Ui.Text("This removes the saved account. Historical usage remains available. Model route bindings must be removed first.", 12, Ui.Muted),
                OverviewActions(remove, Ui.Button("Cancel removal", () => removal.Content = null))));
        }
        var actions = OverviewActions(
            Action(account.Enabled ? "Disable" : "Enable", async () => { _configuration = await _client.SetAccountEnabledAsync(revision, account.Id, !account.Enabled, _lifetime.Token); }, "Account updated."),
            Action("Check credential", async () => { var result = await _client.CheckAccountAsync(account.Id, _lifetime.Token); _notice.Text = $"Credential status: {result.State}. Checked local presence and expiry only."; }, "Local credential check complete."),
            Action("Refresh quota", async () => { _quotas = await _client.RefreshQuotasAsync(account.Id, _lifetime.Token); }, "Quota refresh complete."),
            Ui.Button("Remove…", ConfirmAccountRemoval));
        content.Children.Add(actions);
        content.Children.Add(removal);
        var context = OverviewActions(Ui.Button("Recorded usage", () => ShowUsageForAccount(account.Id)),
            Ui.Button("Models for this account", () => ShowModelsForAccount(account.Id)));
        content.Children.Add(context);
        content.Children.Add(AccountPinControls(account, inventoryChanged));

        if (account.ExpiresAt is { } expiry) content.Children.Add(Ui.Text($"Credential expires {expiry.ToLocalTime():dd MMM · HH:mm} local time", 11, expiry <= DateTimeOffset.UtcNow ? Ui.Warning : Ui.Muted));
        var boundRoutes = _configuration?.Routes.Count(route => route.AccountIds.Contains(account.Id, StringComparer.Ordinal)) ?? 0;
        if (boundRoutes > 0) content.Children.Add(Ui.Text($"Used by {boundRoutes} model {(boundRoutes == 1 ? "route" : "routes")}. Remove those bindings before removing the account.", 11, Ui.Muted));
        if (IsMiMo(account))
        {
            content.Children.Add(Ui.Text("Quota lookup for MiMo is not supported here yet. View the coding-plan allowance in the provider console.", 12, Ui.Muted));
            content.Children.Add(Action("Check in provider console", async () =>
            {
                if (!await Launcher.LaunchUriAsync(new Uri("https://platform.xiaomimimo.com/#/console/plan-manage")))
                    throw new InvalidOperationException("The browser could not be opened. Check your default browser and try again.");
            }, "MiMo provider console opened."));
        }
        if (!_hostAvailable || _quotaError is not null) content.Children.Add(Ui.Text(BudgetSnapshotNote(), 11, Ui.Warning));
        if (_quotaError is not null) content.Children.Add(Ui.Text(_quotaError, 11, Ui.Warning));

        var technical = new StackPanel { Spacing = 9 };
        technical.Children.Add(QuotaTechnicalField("Account ID", account.Id));
        technical.Children.Add(QuotaTechnicalField("Credential", $"{account.CredentialKind} · local state: {account.State}"));
        technical.Children.Add(Ui.Text("Credential checks confirm local presence and expiry only; provider access is not verified.", 11, Ui.Muted));
        if (account.ExpiresAt is null) technical.Children.Add(Ui.Text("Credential expiry not supplied.", 11, Ui.Muted));
        if (account.BudgetPoolId is { } pool) technical.Children.Add(QuotaTechnicalField("Account pool", pool));
        if (quotas.Length == 0) technical.Children.Add(QuotaDetails(null));
        foreach (var quota in quotas) technical.Children.Add(QuotaDetails(quota));
        var technicalDetails = new Expander
        {
            Header = "Technical details", IsExpanded = _accountTechnicalExpansion.TryGetValue(account.Id, out var expanded) ? expanded : options.Level == InformationLevel.Advanced,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new Border { Padding = new Thickness(0, 8), Child = technical }
        };
        technicalDetails.PropertyChanged += (_, args) =>
        {
            if (args.Property == Expander.IsExpandedProperty) _accountTechnicalExpansion[account.Id] = technicalDetails.IsExpanded;
        };
        content.Children.Add(technicalDetails);
        return content;
    }

    private Control AccountPinControls(AccountDto account, Action? inventoryChanged)
    {
        var pinned = IsAccountPinned(account);
        void Changed() { if (inventoryChanged is null) Render(); else inventoryChanged(); }
        var pin = Ui.Button(pinned ? "Unpin account" : "Pin account", () =>
        {
            SavePreferences(current => current with { PinnedAccountIds = pinned
                ? current.PinnedAccountIds.Where(id => id != account.Id).ToArray()
                : current.PinnedAccountIds.Append(account.Id).ToArray() }, render: false);
            Changed();
        });
        AutomationProperties.SetName(pin, $"{(pinned ? "Unpin" : "Pin")} account {account.Name}");
        var controls = OverviewActions(pin);
        if (!pinned) return controls;
        var currentPins = _preferences.PinnedAccountIds.Where(id => _configuration?.Accounts.Any(item => item.Id == id) == true).Distinct(StringComparer.Ordinal).ToArray();
        var index = Array.IndexOf(currentPins, account.Id);
        void Move(int offset)
        {
            SavePreferences(current =>
            {
                var ids = current.PinnedAccountIds.Where(id => _configuration?.Accounts.Any(item => item.Id == id) == true).Distinct(StringComparer.Ordinal).ToArray();
                var position = Array.IndexOf(ids, account.Id);
                var target = position + offset;
                if (position < 0 || target < 0 || target >= ids.Length) return current;
                (ids[position], ids[target]) = (ids[target], ids[position]);
                return current with { PinnedAccountIds = ids };
            }, render: false);
            Changed();
        }
        var earlier = Ui.Button("Move pin earlier", () => Move(-1));
        var later = Ui.Button("Move pin later", () => Move(1));
        earlier.IsEnabled = index > 0; later.IsEnabled = index >= 0 && index < currentPins.Length - 1;
        AutomationProperties.SetName(earlier, $"Move pinned account {account.Name} earlier");
        AutomationProperties.SetName(later, $"Move pinned account {account.Name} later");
        controls.Children.Add(earlier); controls.Children.Add(later);
        return controls;
    }

    private void ShowAddAccount()
    {
        if (_configuration is not { } cfg) { _notice.Text = "Connect to the host first."; return; }
        RegisterEditor(ShowAddAccount, cfg.Revision);
        _editing = true;
        var name = Ui.Input(hint: "My API account");
        var provider = Ui.Input(hint: "e.g. command-code");
        var endpoint = Ui.Input(hint: "https://…/v1");
        var key = Ui.Input(hint: "API key"); key.PasswordChar = '●';
        var form = AccountForm("Add API account", "Credentials stay with your local host and are excluded from exports.",
            AccountFormRow("Display name", name), AccountFormRow("Provider", provider),
            AccountFormRow("Base URL", endpoint), AccountFormRow("API key", key));
        form.Children.Insert(1, Ui.Button("Discover providers", () => { key.Text = ""; _editing = false; ShowProviderCatalog(); }));
        form.Children.Add(OverviewActions(Action("Add account", async () => {
            if (string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(provider.Text) || string.IsNullOrWhiteSpace(key.Text) || string.IsNullOrWhiteSpace(endpoint.Text)) throw new InvalidOperationException("Enter a name, provider, base URL and API key.");
            if (!Uri.TryCreate(endpoint.Text, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) throw new InvalidOperationException("Enter a valid HTTP or HTTPS base URL.");
            _configuration = await _client.AddApiAccountAsync(cfg.Revision, new AddApiAccountRequest(name.Text.Trim(), provider.Text.Trim(), key.Text, endpoint.Text.Trim()), _lifetime.Token); key.Text = "";
            _section = "Accounts";
        }, "Account saved. Provider access has not been verified. Choose models for this account next."), Ui.Button("Cancel", () => { key.Text = ""; _editing = false; _section = "Accounts"; Render(); })));
        _page.Content = form;
    }

    private void ShowImport()
    {
        if (_configuration is not { } cfg) { _notice.Text = "Connect to the host first."; return; }
        RegisterEditor(ShowImport, cfg.Revision);
        _editing = true;
        var path = Ui.Input(hint: "/path/to/config.yaml"); var auth = Ui.Input(hint: "Optional auth directory");
        var form = AccountForm("Import setup", "Import supported API configuration and Codex access-token snapshots.",
            AccountFormRow("Configuration path", path), AccountFormRow("Auth directory", auth));
        form.Children.Add(Ui.Text("Source files stay unchanged. Imported OAuth snapshots are never refreshed or revoked.", 12, Ui.Muted));
        form.Children.Add(OverviewActions(Action("Import", async () => {
            if (string.IsNullOrWhiteSpace(path.Text)) throw new InvalidOperationException("Enter a configuration path.");
            var result = await _client.ImportAsync(new ImportRequest(cfg.Revision, path.Text.Trim(), string.IsNullOrWhiteSpace(auth.Text) ? null : auth.Text.Trim()), _lifetime.Token);
            _notice.Text = $"Imported {result.Added}, updated {result.Updated}. {string.Join(" ", result.Warnings)}";
            _section = "Accounts";
        }, "Import complete. Inspect the imported accounts before choosing models."), Ui.Button("Cancel", () => { _editing = false; _section = "Accounts"; Render(); })));
        _page.Content = form;
    }

    private StackPanel AccountForm(string title, string description, params Control[] rows)
    {
        var form = new StackPanel { Spacing = 12, MaxWidth = 740, HorizontalAlignment = HorizontalAlignment.Stretch };
        form.Children.Add(Heading(title, description));
        var fields = new StackPanel { Spacing = 0 };
        foreach (var row in rows) fields.Children.Add(row);
        form.Children.Add(fields);
        return form;
    }

    private static Control AccountFormRow(string label, Control input)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*"), ColumnSpacing = 12 };
        var text = Ui.Text(label, 12, Ui.Muted); text.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(input, label);
        grid.Children.Add(text); Grid.SetColumn(input, 1); grid.Children.Add(input);
        return new Border { Padding = new Thickness(0, 9), BorderBrush = Ui.Edge, BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
    }
}
