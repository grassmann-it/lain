using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Lain.Core;
using Lain.Desktop.Presentation;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    private readonly Grid _workspaceRegion = new() { ColumnDefinitions = new ColumnDefinitions("*,0") };
    private Control? _accountInspectorDetails;
    private ScrollViewer? _accountListScroll;
    private ScrollViewer? _accountInspectorScroll;
    private Vector _accountInspectorOffset;
    private string? _pendingAccountInspection;
    private bool _restoringAccountNavigation;
    private HashSet<string> _visibleAccountIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _accountDetailTriggers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Border> _accountCards = new(StringComparer.Ordinal);
    private int _openObjectMenus;
    private Button? _accountInspectorClose;
    private bool _accountRemovalVisible;
    private Vector _accountInventoryOffset;
    private bool _updatingAccountInspectorLayout;
    private readonly Border _accountInspector = new()
    {
        IsVisible = false, HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch, Background = Ui.Surface,
        BorderBrush = Ui.Accent, BorderThickness = new Thickness(1, 0, 0, 0),
        Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 0)
    };
    private Control? _accountInspectorOrigin;
    private string? _inspectedAccountId;

    private ContextMenu ObjectCommandMenu(IEnumerable<ObjectCommand> commands)
    {
        var menu = ObjectCommands.Menu(commands);
        menu.Opened += (_, _) => _openObjectMenus++;
        menu.Closed += (_, _) => _openObjectMenus = Math.Max(0, _openObjectMenus - 1);
        return menu;
    }

    private ContextMenu ObjectCommandMenu(Func<IEnumerable<ObjectCommand>> commands)
    {
        var menu = ObjectCommandMenu(commands());
        menu.Opening += (_, _) => ObjectCommands.Populate(menu, commands());
        return menu;
    }

    private Control AccountInventoryViewport(Control content)
    {
        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        // The narrow inventory remains beside details. Its own heading must fit
        // that column rather than forcing the two top-level actions offscreen.
        if (content is StackPanel { Children.Count: > 0 } page && page.Children[0] is Grid { Children.Count: 2 } header)
        {
            var toolbar = header.Children[1];
            bool? stacked = null;
            void ReflowHeader()
            {
                if (header.Bounds.Width <= 0) return;
                var narrow = header.Bounds.Width < 440;
                if (stacked == narrow) return;
                stacked = narrow;
                header.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "*,Auto");
                header.RowDefinitions = new RowDefinitions(narrow ? "Auto,Auto" : "Auto");
                header.ColumnSpacing = narrow ? 0 : 18;
                header.RowSpacing = narrow ? 8 : 0;
                Grid.SetColumn(toolbar, narrow ? 0 : 1);
                Grid.SetRow(toolbar, narrow ? 1 : 0);
            }
            header.SizeChanged += (_, _) => ReflowHeader();
        }
        Control viewport = scroll;
        if (content is StackPanel panel && panel.Children.LastOrDefault() is ContentControl inventory)
        {
            panel.Children.Remove(inventory);
            scroll.Content = inventory;
            var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 10 };
            layout.Children.Add(panel);
            Grid.SetRow(scroll, 1); layout.Children.Add(scroll);
            viewport = layout;
        }
        else scroll.Content = content;
        AutomationProperties.SetName(scroll, "Account inventory");
        _accountListScroll = scroll;
        scroll.ScrollChanged += (_, _) => _accountInventoryOffset = scroll.Offset;
        var offset = _accountInventoryOffset;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(_accountListScroll, scroll) || _section != "Accounts" || _editing) return;
            scroll.Offset = offset;
            if (_pendingAccountInspection is not { } id) { _restoringAccountNavigation = false; return; }
            _pendingAccountInspection = null;
            if (!_visibleAccountIds.Contains(id) || _configuration?.Accounts.Any(a => a.Id == id) != true)
            {
                _notice.Text = "The previous account is no longer visible. Clear filters or select another account.";
                _restoringAccountNavigation = false;
                return;
            }
            var detailOffset = _accountInspectorOffset;
            var focus = _restoringAccountNavigation;
            _restoringAccountNavigation = false;
            ShowAccountInspector(id, _accountDetailTriggers.GetValueOrDefault(id), focus: focus, restoreOffset: detailOffset);
        }, DispatcherPriority.Loaded);
        return Ui.Viewport(viewport);
    }

    private void PrepareAccountsRender()
    {
        if (!_restoringAccountNavigation && _accountListScroll is { } list) _accountInventoryOffset = list.Offset;
        if (_inspectedAccountId is { } id)
        {
            _pendingAccountInspection = id;
            _accountInspectorOffset = _accountInspectorScroll?.Offset ?? default;
            CloseAccountInspector(false);
        }
        _accountCards.Clear();
        _accountDetailTriggers.Clear();
    }

    private void UpdateAccountSelection()
    {
        foreach (var (id, card) in _accountCards)
        {
            var selected = id == _inspectedAccountId;
            card.BorderBrush = selected ? Ui.Accent : Ui.Edge;
            card.Background = selected ? Ui.AccentSurface : Ui.Surface;
            AutomationProperties.SetHelpText(card, selected
                ? "Selected account. Details are open; all reported windows remain in the inventory."
                : "All reported windows. An empty window does not prove that the whole account is exhausted.");
        }
    }

    // The two menus and visible buttons use the same commands, frozen to an ID
    // and revision. Polling cannot redirect a pending click to a different row.
    private ObjectCommand[] AccountCommands(AccountDto account, long revision, System.Action? inventoryChanged)
    {
        void Run(Func<Task> action, string success, bool stay = false)
        { if (stay) CloseAccountInspector(false); _ = RunAsync(action, success, stay); }
        var canModel = !IsMonitorMode && (_status?.Capabilities?.Inference ?? true);
        ObjectCommand[] commands =
        [
            new("detail", "Account details", () => ShowAccountInspector(account.Id)),
            new("refresh", IsAccountQuotaRefreshing(account.Id) ? "Refreshing quota…" : "Refresh quota",
                () => _ = RefreshAccountQuotaAsync(account.Id), !IsAccountQuotaRefreshing(account.Id), "This account quota refresh is already running."),
            new("usage", "Recorded usage", () => ShowUsageForAccount(account.Id), HasHistory, "This host has no recorded history source."),
            new("models", "Models for this account", () => ShowModelsForAccount(account.Id), canModel, "Choose Gateway to configure model access."),
            new("enabled", account.Enabled ? "Disable account" : "Enable account", () => Run(async () => _configuration = await _client.SetAccountEnabledAsync(revision, account.Id, !account.Enabled, _lifetime.Token), "Account updated.")),
            new("credential", "Check local credential", () => Run(async () => { var check = await _client.CheckAccountAsync(account.Id, _lifetime.Token); _notice.Text = $"Local credential: {check.State}. Provider access was not tested."; }, "Local credential checked.")),
            new("reconnect", "Reconnect account…", () => { CloseAccountInspector(false); _ = BeginProviderConnectionAsync(account.Provider, account); }),
            new("monitor", "Monitoring access", () => Run(() => OpenMonitorSetupAsync(account), "Review monitoring access.", true)),
            .. (account.Provider == "xai" ? new ObjectCommand[] {
                new("discover-team", "Discover team", () => Run(async () =>
                    _configuration = await _client.DiscoverXaiTeamAsync(revision, account.Id, account.Generation, _lifetime.Token),
                    "xAI team identity checked. No budget was read."), account.Enabled, "Enable this account before discovering its team.")
            } : []),
            new("pin", IsAccountPinned(account) ? "Unpin account" : "Pin account", () => {
                SavePreferences(p => p with { PinnedAccountIds = IsAccountPinned(account) ? p.PinnedAccountIds.Where(id => id != account.Id).ToArray() : p.PinnedAccountIds.Append(account.Id).ToArray() }, false);
                if (inventoryChanged is null) Render();
                else { inventoryChanged(); ShowAccountInspector(account.Id, focus: false); }
            }),
            new("remove", "Remove account…", () => ShowAccountRemoval(account.Id, revision))
        ];
        return commands.Select(command => command with { Execute = () =>
        {
            if (_busy) return;
            if (_configuration?.Accounts.Any(a => a.Id == account.Id) != true)
            {
                CloseAccountInspector();
                _notice.Text = "This account is no longer configured. Reload the inventory before choosing another action.";
                return;
            }
            if (command.Key is "enabled" or "remove" or "reconnect" or "monitor" or "discover-team" && _configuration.Revision != revision)
            {
                _notice.Text = "Configuration changed while these actions were open. Reopen account details to review the current account before changing it.";
                return;
            }
            if (command.Key is "refresh" or "enabled" or "credential" or "reconnect" or "monitor" or "remove" or "discover-team" && !_hostAvailable)
            { _notice.Text = "The host is offline. The last known account remains visible; reload status before changing it."; return; }
            if (command.Key == "models" && IsMonitorMode) { _notice.Text = "Choose Gateway to configure model access."; return; }
            if (command.Key == "refresh" && IsAccountQuotaRefreshing(account.Id)) return;
            if (command.Available) command.Execute();
        }, Available = command.Available && (_hostAvailable || command.Key is "detail" or "usage" or "models" or "pin"),
            Reason = !_hostAvailable && command.Key is "refresh" or "enabled" or "credential" or "reconnect" or "monitor" or "remove" or "discover-team"
                ? "The local host is offline." : command.Reason }).ToArray();
    }

    private void ShowAccountInspector(string accountId, Control? origin = null, bool focus = true, Vector? restoreOffset = null)
    {
        if (_configuration is not { } cfg || cfg.Accounts.FirstOrDefault(a => a.Id == accountId) is not { } account) return;
        var previousId = _inspectedAccountId;
        var offset = restoreOffset ?? (previousId == accountId ? _accountInspectorScroll?.Offset ?? _accountInspectorOffset : default);
        DetachInspectorContent();
        _inspectedAccountId = accountId;
        _accountRemovalVisible = false;
        _accountInspectorOrigin = origin ?? _accountDetailTriggers.GetValueOrDefault(accountId);
        var close = Ui.Button("Close", () => CloseAccountInspector());
        _accountInspectorClose = close;
        AutomationProperties.SetName(close, $"Close account details for {account.Name}");
        var menu = ObjectCommandMenu(() => AccountCommands(account, cfg.Revision, null));
        var more = Ui.Button("⋯", () => menu.Open()); more.ContextMenu = menu;
        AutomationProperties.SetName(more, $"Inspector actions for account {account.Name}");
        var heading = Ui.PageHeader(AccountDisplayName(account), ProviderName(account), more, close);
        var content = new StackPanel { Spacing = 12 };
        if (!_quotas.Any(quota => quota.AccountId == accountId)) content.Children.Add(AccountTileWindow(account, null));
        foreach (var quota in _quotas.Where(q => q.AccountId == accountId)) content.Children.Add(AccountTileWindow(account, quota));
        content.Children.Add(AccountDetail(account, cfg.Revision));
        _accountInspectorScroll = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetName(_accountInspectorScroll, $"Account detail content for {account.Name}");
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 12 };
        layout.Children.Add(Ui.Stack(heading, AccountQuotaRefreshStatus(account)));
        Grid.SetRow(_accountInspectorScroll, 1); layout.Children.Add(_accountInspectorScroll);
        _accountInspectorDetails = Ui.Viewport(layout);
        UpdateAccountInspectorLayout();
        UpdateAccountSelection();
        AutomationProperties.SetName(_accountInspector, $"Account inspector for {account.Name}");
        Dispatcher.UIThread.Post(() =>
        {
            if (_inspectedAccountId != accountId) return;
            if (_accountInspectorScroll is { } scroll) scroll.Offset = offset;
            if (focus) close.Focus();
        }, DispatcherPriority.Loaded);
    }

    private void UpdateAccountInspectorLayout()
    {
        if (_workspaceRegion is null || _workspace is null || _updatingAccountInspectorLayout) return;
        _updatingAccountInspectorLayout = true;
        try
        {
            var accountPage = _section == "Accounts" && !_editing;
            var narrow = (Bounds.Width > 0 ? Bounds.Width : Width) < 1000;
            // Accounts A keeps inventory and detail mounted side by side at both
            // agreed sizes. Each pane retains its own independent scroll surface.
            _workspaceRegion.ColumnDefinitions = new ColumnDefinitions(!accountPage ? "*,0" : narrow ? "340,*" : "*,400");
            if (_accountInspectorClose is { } close) close.Content = "Close";
            if (accountPage)
            {
                if (!ReferenceEquals(_accountInspector.Child, _accountInspectorDetails) || _accountInspector.Child is null)
                {
                    DetachInspectorContent();
                    _accountInspector.Child = _accountInspectorDetails ?? Ui.Stack(Ui.Text("Account details", 15),
                        Ui.Text("Select an account to inspect its windows and actions. The inventory stays in place.", 13, Ui.Muted));
                }
                _accountInspector.IsVisible = true;
            }
            else
            {
                DetachInspectorContent();
                _accountInspector.IsVisible = false;
            }
        }
        finally { _updatingAccountInspectorLayout = false; }
    }

    private void DetachInspectorContent() => _accountInspector.Child = null;

    private void CloseAccountInspector(bool restoreFocus = true)
    {
        if (_inspectedAccountId is null) return;
        var id = _inspectedAccountId;
        var origin = _accountDetailTriggers.GetValueOrDefault(id) ?? _accountInspectorOrigin;
        _accountInspectorOffset = _accountInspectorScroll?.Offset ?? _accountInspectorOffset;
        DetachInspectorContent();
        _accountInspectorDetails = null;
        _accountInspectorScroll = null;
        _accountInspectorClose = null;
        _inspectedAccountId = null;
        _accountRemovalVisible = false;
        UpdateAccountInspectorLayout();
        UpdateAccountSelection();
        if (restoreFocus) Dispatcher.UIThread.Post(() =>
        {
            if (_configuration?.Accounts.Any(a => a.Id == id) == true) origin?.Focus();
            else if (_page.Content is Control current)
                current.GetLogicalDescendants().OfType<Control>().FirstOrDefault(c => c is Button or TextBox && c.IsVisible && c.IsEnabled)?.Focus();
        }, DispatcherPriority.Loaded);
    }

    private void ShowAccountRemoval(string id, long revision)
    {
        if (_configuration?.Accounts.FirstOrDefault(a => a.Id == id) is not { } account) return;
        ShowAccountInspector(id);
        _accountRemovalVisible = true;
        var references = _configuration.Routes.Where(r => r.AccountIds.Contains(id, StringComparer.Ordinal)).Select(r => r.Alias).ToArray();
        var remove = Action("Remove account", async () => {
            if (_configuration?.Revision != revision) throw new InvalidOperationException("Configuration changed. Reopen the removal review before removing this account.");
            _configuration = await _client.RemoveAccountAsync(revision, id, _lifetime.Token);
            CloseAccountInspector(false);
        }, "Account removed. Historical usage remains available.");
        remove.IsEnabled = references.Length == 0 && _hostAvailable;
        var cancel = Ui.Button("Cancel", () => ShowAccountInspector(id));
        if (_accountInspectorScroll is { } scroll)
        {
            scroll.Content = Ui.Stack(Ui.Title($"Remove {account.Name}?"),
                Ui.Text("Historical usage remains available. Remove model bindings before removing this account.", 13, Ui.Muted),
                Ui.Text(references.Length == 0 ? "No model bindings." : "Used by: " + string.Join(", ", references), 13, Ui.Warning),
                OverviewActions(remove, cancel));
            scroll.Offset = default;
        }
        Dispatcher.UIThread.Post(() => cancel.Focus(), DispatcherPriority.Loaded);
    }
}
