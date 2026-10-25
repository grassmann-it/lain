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
    private readonly HashSet<string> _accountQuotaRefreshes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _accountQuotaErrors = new(StringComparer.Ordinal);
    private long _accountQuotaRefreshGeneration;
    private event Action<string>? AccountQuotaRefreshChanged;

    private bool IsAccountQuotaRefreshing(string accountId) => _accountQuotaRefreshes.Contains(accountId);
    private string? AccountQuotaError(string accountId) => _accountQuotaErrors.GetValueOrDefault(accountId);

    // Refresh responses contain the host's full inventory. Only the requested
    // account belongs to this operation; another concurrent account may be newer.
    private async Task RefreshAccountQuotaAsync(string accountId)
    {
        if (_busy || !_hostAvailable || _configuration?.Accounts.Any(account => account.Id == accountId) != true
            || !_accountQuotaRefreshes.Add(accountId)) return;
        _accountQuotaRefreshGeneration++;
        _accountQuotaErrors.Remove(accountId);
        AccountQuotaRefreshChanged?.Invoke(accountId);
        if (_section == "Accounts" && !_editing) _renderedPageState = PageState();
        try
        {
            var response = await _client.RefreshQuotasAsync(accountId, _lifetime.Token);
            if (_configuration?.Accounts.Any(account => account.Id == accountId) == true)
                _quotas = _quotas.Where(quota => quota.AccountId != accountId)
                    .Concat(response.Where(quota => quota.AccountId == accountId)).ToArray();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { _accountQuotaErrors[accountId] = Friendly(error); }
        finally
        {
            _accountQuotaRefreshes.Remove(accountId);
            _accountQuotaRefreshGeneration++;
            if (!_lifetime.IsCancellationRequested)
            {
                AccountQuotaRefreshChanged?.Invoke(accountId);
                // A completion never touches the inspector now showing B, a draft,
                // or the account removal confirmation opened while A was loading.
                if (_section == "Accounts" && !_editing && !_accountRemovalVisible && _inspectedAccountId == accountId)
                    ShowAccountInspector(accountId, focus: false);
                if (_section == "Accounts" && !_editing) _renderedPageState = PageState();
            }
        }
    }

    private void ObserveAccountQuotaRefresh(Control owner, string accountId, Action update)
    {
        void Changed(string changedId) { if (changedId == accountId) update(); }
        owner.AttachedToVisualTree += (_, _) => { AccountQuotaRefreshChanged += Changed; update(); };
        owner.DetachedFromVisualTree += (_, _) => AccountQuotaRefreshChanged -= Changed;
        update();
    }

    private void BindAccountQuotaRefreshButton(Button button, string accountId)
    {
        ObserveAccountQuotaRefresh(button, accountId, () =>
        {
            button.IsEnabled = !_busy && _hostAvailable && !IsAccountQuotaRefreshing(accountId);
            ToolTip.SetTip(button, IsAccountQuotaRefreshing(accountId) ? "Refreshing quota for this account…" : "Refresh quota");
        });
    }

    private Control AccountQuotaRefreshStatus(AccountDto account)
    {
        // Reserve the status line while idle/busy. A failure adds a readable
        // bounded cause; full text stays in the scrolling Inspector details.
        // Quota rows and observation/reset facts remain mounted during requests.
        var status = AccountCell("", 11, Ui.Muted);
        status.Height = 18;
        AutomationProperties.SetName(status, "Quota refresh status for " + account.Name);
        var reason = Ui.Text("", 11, Ui.Warning);
        reason.MaxLines = 3;
        reason.TextTrimming = TextTrimming.CharacterEllipsis;
        AutomationProperties.SetName(reason, "Quota refresh reason for " + account.Name);
        ObserveAccountQuotaRefresh(status, account.Id, () =>
        {
            var hasValues = _quotas.Any(quota => quota.AccountId == account.Id);
            status.Text = IsAccountQuotaRefreshing(account.Id)
                ? hasValues ? "Refreshing quota · showing last known values" : "Loading quota for this account…"
                : AccountQuotaError(account.Id) is not null
                    ? hasValues ? "Refresh failed · showing last known values" : "Quota could not be loaded · try Refresh quota"
                    : "";
            status.Foreground = AccountQuotaError(account.Id) is not null && !IsAccountQuotaRefreshing(account.Id) ? Ui.Warning : Ui.Muted;
            var error = AccountQuotaError(account.Id);
            reason.Text = error ?? "";
            reason.IsVisible = error is not null && !IsAccountQuotaRefreshing(account.Id);
            ToolTip.SetTip(status, error ?? status.Text);
            ToolTip.SetTip(reason, error);
        });
        return new StackPanel { Spacing = 3, Children = { status, reason } };
    }

    private Action CaptureAccountsNavigation()
    {
        var search = _accountSearch;
        var provider = _accountProvider;
        var status = _accountStatus;
        var filtersExpanded = _accountFiltersExpanded;
        var selected = _inspectedAccountId;
        var listOffset = _workspace.IsVisible ? _accountListScroll?.Offset ?? _accountInventoryOffset : _accountInventoryOffset;
        var detailOffset = _accountInspectorScroll?.Offset ?? _accountInspectorOffset;
        return () =>
        {
            _accountSearch = search;
            _accountProvider = provider;
            _accountStatus = status;
            _accountFiltersExpanded = filtersExpanded;
            _accountNavigationId = null;
            _pendingAccountInspection = selected;
            _restoringAccountNavigation = true;
            _accountInventoryOffset = listOffset;
            _accountInspectorOffset = detailOffset;
        };
    }

    private Control AccountsPage()
    {
        var options = PresentationFor("Accounts");
        var panel = Ui.Stack(Ui.PageHeader("Accounts", null,
            Ui.Button("Discover providers", ShowProviderCatalog),
            Ui.PrimaryButton("Add account", ShowAddAccountChooser)));
        panel.Spacing = options.Density == ViewDensity.Compact ? 8 : 12;
        if (LoginPending(_login)) panel.Children.Add(Action("Continue " + ConnectProviderName(_login!.Provider) + " sign-in", () => { ShowLoginPage(); return Task.CompletedTask; },
            "Complete sign-in in your browser.", stayOnPage: true));
        if (_configuration is not { } cfg)
        {
            var state = new StackPanel { Spacing = 10, MinHeight = 240 };
            var failed = _configurationError is not null;
            state.Children.Add(Ui.Text(failed ? "Account inventory could not be loaded" : "Loading account inventory…", 15));
            state.Children.Add(Ui.Text(failed
                ? "Reload status to try again. You can discover providers while the host is unavailable."
                : "Waiting for the local host. Accounts and their quota windows will appear here after the first snapshot.", 13, Ui.Muted));
            if (failed) state.Children.Add(Ui.Text(_configurationError!, 12, Ui.Warning));
            AutomationProperties.SetName(state, failed ? "Account inventory load failed" : "Account inventory loading");
            panel.Children.Add(Ui.Card(state));
            return AccountInventoryViewport(panel);
        }
        if (cfg.Accounts.Length == 0)
        {
            var empty = new StackPanel { Spacing = 10, MinHeight = 240 };
            empty.Children.Add(Ui.Text("No accounts configured", 15));
            empty.Children.Add(Ui.Text("Connect your first account to choose models and create a client profile. Add account offers browser sign-in, an API key or an existing setup import.", 13, Ui.Muted));
            AutomationProperties.SetName(empty, "Account inventory empty");
            panel.Children.Add(Ui.Card(empty));
            return AccountInventoryViewport(panel);
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
        if (IsMonitorMode) panel.Children.Add(MonitorBudgetTableHeader());
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
                && $"{account.Name} {AccountDisplayName(account)} {account.Provider} {ProviderName(account)} {account.Id}".Contains(_accountSearch.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
            _visibleAccountIds = found.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
            if (_inspectedAccountId is { } selected && !_visibleAccountIds.Contains(selected))
            {
                CloseAccountInspector(false);
                _notice.Text = "The selected account is outside these filters. Clear filters to show it again.";
                search.Focus();
            }
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
                + " · " + (_preferences.AccountSort switch { "name" => "Quota readings first · ordered by name", "provider" => "Quota readings first · ordered by provider and name", "five-hour" => "5h usage · order held until updated", _ => "Pinned first · quota readings before unavailable" });
            if (found.Length == 0)
            {
                inventory.Content = Empty("No accounts match these filters. Clear filters to see the full inventory.");
                return;
            }
            if (!_preferences.GroupAccountsByProvider)
            {
                inventory.Content = AccountAvailabilityRows(cfg, found, Populate);
                previousInventoryActions.AddRange(_actions.Where(action => !actionsBefore.Contains(action)));
                return;
            }
            var groups = new StackPanel { Spacing = options.Density == ViewDensity.Compact ? 12 : 18 };
            var pinned = _preferences.AccountSort == "favorites" ? found.Where(IsAccountPinned).ToArray() : [];
            if (pinned.Length > 0) groups.Children.Add(Ui.Section("Pinned", AccountInventoryRows(cfg, pinned, Populate)));
            var providerGroups = found.Where(account => !pinned.Contains(account))
                .GroupBy(account => (Provider: ProviderName(account), HasReading: _preferences.AccountSort == "five-hour" || HasAccountQuotaReading(account)));
            // In 5h mode the captured rank remains authoritative. Other modes
            // keep all measured groups above groups with no usable reading.
            if (_preferences.AccountSort != "five-hour") providerGroups = providerGroups
                .OrderByDescending(providerGroup => providerGroup.Key.HasReading)
                .ThenBy(providerGroup => providerGroup.Key.Provider, StringComparer.OrdinalIgnoreCase);
            var splitProviders = providerGroups.GroupBy(providerGroup => providerGroup.Key.Provider, StringComparer.OrdinalIgnoreCase)
                .Where(providerGroup => providerGroup.Count() > 1).Select(providerGroup => providerGroup.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var accounts in providerGroups)
            {
                var label = accounts.Key.Provider + (!accounts.Key.HasReading && splitProviders.Contains(accounts.Key.Provider) ? " · No quota readings" : "");
                groups.Children.Add(Ui.Section(label, AccountInventoryRows(cfg, accounts.ToArray(), Populate)));
            }
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
        return AccountInventoryViewport(panel);
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
        if (_preferences.AccountSort == "name") return accounts.OrderByDescending(HasAccountQuotaReading)
            .ThenBy(AccountDisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(ProviderName, StringComparer.OrdinalIgnoreCase).ThenBy(account => account.Id, StringComparer.Ordinal);
        if (_preferences.AccountSort == "provider") return accounts.OrderByDescending(HasAccountQuotaReading)
            .ThenBy(ProviderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(AccountDisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(account => account.Id, StringComparer.Ordinal);
        var pins = _preferences.PinnedAccountIds.Select((id, position) => (id, position))
            .GroupBy(item => item.id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First().position, StringComparer.Ordinal);
        // Explicit pins retain their chosen order. Unpinned accounts with actual
        // readings precede setup-only or missing quota; consumption is not ranked.
        return accounts.OrderBy(account => pins.TryGetValue(account.Id, out var position) ? position : int.MaxValue)
            .ThenByDescending(HasAccountQuotaReading)
            .ThenBy(ProviderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(AccountDisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(account => account.Id, StringComparer.Ordinal);
    }

    private bool HasAccountQuotaReading(AccountDto account) => QuotaPresentation.HasQuotaReading(
        _quotas.Where(quota => quota.AccountId == account.Id));

    private Control AccountAvailabilityRows(ConfigurationDto configuration, IReadOnlyList<AccountDto> accounts, Action inventoryChanged)
    {
        if (_preferences.AccountSort == "five-hour") return AccountInventoryRows(configuration, accounts, inventoryChanged);
        var pinned = _preferences.AccountSort == "favorites" ? accounts.Where(IsAccountPinned).ToArray() : [];
        var unpinned = accounts.Where(account => !pinned.Contains(account)).ToArray();
        var measured = unpinned.Where(HasAccountQuotaReading).ToArray();
        var missing = unpinned.Where(account => !HasAccountQuotaReading(account)).ToArray();
        if (pinned.Length == 0 && (measured.Length == 0 || missing.Length == 0))
            return AccountInventoryRows(configuration, accounts, inventoryChanged);
        // Independent masonry columns must not lift a short missing-data tile
        // above a taller measured tile in the preceding group.
        var sections = new StackPanel { Spacing = PresentationFor("Accounts").Density == ViewDensity.Compact ? 12 : 18 };
        if (pinned.Length > 0) sections.Children.Add(Ui.Section("Pinned", AccountInventoryRows(configuration, pinned, inventoryChanged)));
        if (measured.Length > 0) sections.Children.Add(AccountInventoryRows(configuration, measured, inventoryChanged));
        if (missing.Length > 0) sections.Children.Add(Ui.Section("Quota not available", AccountInventoryRows(configuration, missing, inventoryChanged)));
        return sections;
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
        if (!account.Enabled || AccountCredentialWarning(account) is not null || AccountQuotaError(account.Id) is not null) return true;
        var quotas = _quotas.Where(quota => quota.AccountId == account.Id).ToArray();
        return quotas.Any(quota => !QuotaUnavailable(quota) && (LastKnownQuota(quota) || QuotaUsedPercent(quota) >= 90
            || quota.State is "error" or "unavailable" || !string.IsNullOrWhiteSpace(quota.Error)));
    }

    private static string? AccountCredentialWarning(AccountDto account)
    {
        if (account.ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow) return "Credential expired · reconnect this account.";
        return account.State.ToLowerInvariant() switch
        {
            "ready" or "ok" or "available" or "unverified" or "not-checked" or "unknown" => null,
            "missing" or "missing-credential" => "Credential missing · reconnect this account.",
            "expired" => "Credential expired · reconnect this account.",
            "error" or "invalid" or "unauthorized" or "forbidden" or "auth-failed" or "authentication-failed" => "Credential access failed · check account details.",
            _ => null
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
        var commands = AccountCommands(account, revision, inventoryChanged);
        var actions = OverviewActions();
        foreach (var command in commands.Where(command => command.Key is "refresh" or "usage" or "models" or "discover-team"
            || command.Key == "reconnect" && AccountCredentialWarning(account) is not null))
        {
            var button = Ui.Button(command.Label, command.Execute);
            button.IsEnabled = command.Available;
            if (command.Key == "refresh") BindAccountQuotaRefreshButton(button, account.Id);
            if (!command.Available) ToolTip.SetTip(button, command.Reason);
            actions.Children.Add(button);
        }
        var menu = ObjectCommandMenu(() => AccountCommands(account, revision, inventoryChanged));
        var more = Ui.Button("More account actions…", () => menu.Open()); more.ContextMenu = menu;
        actions.Children.Add(more);
        content.Children.Add(actions);

        if (account.Provider == "xai")
        {
            content.Children.Add(Ui.Text(account.Binding is { ResourceType: "team", ResourceId: { } team }
                ? "Team: " + team : "Team identity has not been discovered.", 12, Ui.Muted));
            content.Children.Add(Ui.Text("Discover team uses the saved xAI key to read its identity. It does not read billing or a remaining budget.", 11, Ui.Muted));
        }

        if (account.ExpiresAt is { } expiry) content.Children.Add(Ui.Text($"Credential expires {expiry.ToLocalTime():dd MMM · HH:mm} local time", 11, expiry <= DateTimeOffset.UtcNow ? Ui.Warning : Ui.Muted));
        var boundRoutes = _configuration?.Routes.Count(route => route.AccountIds.Contains(account.Id, StringComparer.Ordinal)) ?? 0;
        if (boundRoutes > 0) content.Children.Add(Ui.Text($"Used by {boundRoutes} model {(boundRoutes == 1 ? "route" : "routes")}. Remove those bindings before removing the account.", 11, Ui.Muted));
        if (!_hostAvailable || _quotaError is not null) content.Children.Add(Ui.Text(BudgetSnapshotNote(), 11, Ui.Warning));
        if (_quotaError is not null) content.Children.Add(Ui.Text(_quotaError, 11, Ui.Warning));

        if (AccountQuotaError(account.Id) is { } refreshError)
        {
            content.Children.Add(Ui.Text("Quota refresh failed · use Refresh quota to try again.", 12, Ui.Warning));
            var reason = new SelectableTextBlock { Text = refreshError, FontSize = 12, Foreground = Ui.Warning, TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetName(reason, "Complete quota refresh reason for " + account.Name);
            content.Children.Add(reason);
        }
        content.Children.Add(QuotaTechnicalField("Saved name", account.Name));
        var technical = new StackPanel { Spacing = 9 };
        technical.Children.Add(AccountPinControls(account, inventoryChanged));
        technical.Children.Add(QuotaTechnicalField("Account ID", account.Id));
        technical.Children.Add(QuotaTechnicalField("Credential", $"{account.CredentialKind} · {(account.State == "unverified" ? "Not checked" : "local state: " + account.State)}"));
        technical.Children.Add(Ui.Text("Credential checks confirm local presence and expiry only; provider access is not verified.", 11, Ui.Muted));
        if (account.ExpiresAt is null) technical.Children.Add(Ui.Text("Credential expiry not supplied.", 11, Ui.Muted));
        if (account.BudgetPoolId is { } pool) technical.Children.Add(QuotaTechnicalField("Account pool", pool));
        if (quotas.Length == 0) technical.Children.Add(QuotaDetails(null));
        foreach (var quota in quotas) technical.Children.Add(QuotaDetails(quota));
        var technicalDetails = new Expander
        {
            Header = "Technical details", IsExpanded = _accountTechnicalExpansion.TryGetValue(account.Id, out var expanded) ? expanded : false,
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
        void Changed() { if (inventoryChanged is null) Render(); else { inventoryChanged(); ShowAccountInspector(account.Id, focus: false); } }
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
