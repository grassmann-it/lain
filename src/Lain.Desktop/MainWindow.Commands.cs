using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using Lain.Core;
using Lain.Desktop.Presentation;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    private readonly Grid _workspaceRegion = new() { ColumnDefinitions = new ColumnDefinitions("*,0") };
    private Control? _accountInventoryPage;
    private Control? _accountInspectorDetails;
    private Vector _accountInventoryOffset;
    private bool _accountDetailPage;
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

    private static ContextMenu ObjectCommandMenu(IEnumerable<ObjectCommand> commands) => ObjectCommands.Menu(commands);

    // The two menus and visible buttons use the same commands, frozen to an ID
    // and revision. Polling cannot redirect a pending click to a different row.
    private ObjectCommand[] AccountCommands(AccountDto account, long revision, System.Action? inventoryChanged)
    {
        void Run(Func<Task> action, string success, bool stay = false)
        { CloseAccountInspector(false); _ = RunAsync(action, success, stay); }
        var canModel = !IsMonitorMode && (_status?.Capabilities?.Inference ?? true);
        return
        [
            new("detail", "Account details", () => ShowAccountInspector(account.Id)),
            new("refresh", "Refresh quota", () => Run(async () => _quotas = await _client.RefreshQuotasAsync(account.Id, _lifetime.Token), "Quota refresh complete.")),
            new("usage", "Recorded usage", () => ShowUsageForAccount(account.Id), HasHistory, "This host has no recorded history source."),
            new("models", "Models for this account", () => ShowModelsForAccount(account.Id), canModel, "Choose Gateway to configure model access."),
            new("enabled", account.Enabled ? "Disable account" : "Enable account", () => Run(async () => _configuration = await _client.SetAccountEnabledAsync(revision, account.Id, !account.Enabled, _lifetime.Token), "Account updated.")),
            new("credential", "Check local credential", () => Run(async () => { var check = await _client.CheckAccountAsync(account.Id, _lifetime.Token); _notice.Text = $"Local credential: {check.State}. Provider access was not tested."; }, "Local credential checked.")),
            new("reconnect", "Reconnect account…", () => { CloseAccountInspector(false); _ = BeginProviderConnectionAsync(account.Provider, account); }),
            new("monitor", "Monitoring access", () => Run(() => OpenMonitorSetupAsync(account), "Review monitoring access.", true)),
            new("pin", IsAccountPinned(account) ? "Unpin account" : "Pin account", () => {
                SavePreferences(p => p with { PinnedAccountIds = IsAccountPinned(account) ? p.PinnedAccountIds.Where(id => id != account.Id).ToArray() : p.PinnedAccountIds.Append(account.Id).ToArray() }, false);
                CloseAccountInspector(false);
                if (inventoryChanged is null) Render(); else inventoryChanged();
            }),
            new("remove", "Remove account…", () => ShowAccountRemoval(account.Id, revision))
        ];
    }

    private void ShowAccountInspector(string accountId, Control? origin = null)
    {
        if (_configuration is not { } cfg || cfg.Accounts.FirstOrDefault(a => a.Id == accountId) is not { } account) return;
        if (_accountDetailPage)
        {
            _page.Content = _accountInventoryPage;
            _accountInventoryPage = null;
            _accountDetailPage = false;
            _workspace.Offset = _accountInventoryOffset;
        }
        DetachInspectorContent();
        _inspectedAccountId = accountId;
        _accountInspectorOrigin = origin ?? _accountInspectorOrigin;
        var close = Ui.Button("Close", () => CloseAccountInspector());
        AutomationProperties.SetName(close, $"Close account details for {account.Name}");
        var heading = Ui.PageHeader(AccountDisplayName(account), ProviderName(account), close);
        var content = Ui.Stack(heading);
        foreach (var quota in _quotas.Where(q => q.AccountId == accountId)) content.Children.Add(AccountTileWindow(account, quota));
        content.Children.Add(AccountDetail(account, cfg.Revision));
        _accountInspectorDetails = content;
        UpdateAccountInspectorLayout();
        AutomationProperties.SetName(_accountInspector, $"Account inspector for {account.Name}");
        Dispatcher.UIThread.Post(() => close.Focus(), DispatcherPriority.Loaded);
    }

    private void UpdateAccountInspectorLayout()
    {
        if (_workspaceRegion is null || _workspace is null || _updatingAccountInspectorLayout) return;
        _updatingAccountInspectorLayout = true;
        try
        {
        var adjacent = (Bounds.Width > 0 ? Bounds.Width : Width) >= 1000 && _section == "Accounts" && !_editing;
        _workspaceRegion.ColumnDefinitions = new ColumnDefinitions(adjacent ? "*,400" : "*,0");
        if (adjacent)
        {
            if (_accountDetailPage)
            {
                _page.Content = null;
                _page.Content = _accountInventoryPage;
                _accountInventoryPage = null;
                _accountDetailPage = false;
                _workspace.Offset = _accountInventoryOffset;
            }
            DetachInspectorContent();
            _accountInspector.Child = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _accountInspectorDetails ?? Ui.Stack(Ui.Text("Account details", 15),
                    Ui.Text("Choose Details on an account. All reported windows remain visible in the inventory.", 12, Ui.Muted)) };
            _accountInspector.IsVisible = true;
        }
        else
        {
            DetachInspectorContent();
            _accountInspector.IsVisible = false;
            if (_inspectedAccountId is not null && _accountInspectorDetails is not null && !_accountDetailPage)
            {
                _accountInventoryPage = _page.Content as Control;
                _accountInventoryOffset = _workspace.Offset;
                _page.Content = _accountInspectorDetails;
                _accountDetailPage = true;
                _workspace.Offset = default;
            }
        }
        }
        finally { _updatingAccountInspectorLayout = false; }
    }

    private void DetachInspectorContent()
    {
        if (_accountInspector.Child is ScrollViewer scroll) scroll.Content = null;
        _accountInspector.Child = null;
    }

    private void CloseAccountInspector(bool restoreFocus = true)
    {
        if (_inspectedAccountId is null) return;
        DetachInspectorContent();
        _accountInspectorDetails = null;
        _inspectedAccountId = null;
        if (_accountDetailPage)
        {
            _page.Content = _accountInventoryPage;
            _accountInventoryPage = null;
            _accountDetailPage = false;
            var offset = _accountInventoryOffset;
            Dispatcher.UIThread.Post(() => _workspace.Offset = offset, DispatcherPriority.Loaded);
        }
        UpdateAccountInspectorLayout();
        if (restoreFocus) _accountInspectorOrigin?.Focus();
    }

    private void ShowAccountRemoval(string id, long revision)
    {
        if (_configuration?.Accounts.FirstOrDefault(a => a.Id == id) is not { } account) return;
        ShowAccountInspector(id);
        var references = _configuration.Routes.Where(r => r.AccountIds.Contains(id, StringComparer.Ordinal)).Select(r => r.Alias).ToArray();
        var remove = Action("Remove account", async () => {
            _configuration = await _client.RemoveAccountAsync(revision, id, _lifetime.Token);
            CloseAccountInspector(false);
        }, "Account removed. Historical usage remains available.");
        remove.IsEnabled = references.Length == 0;
        _accountInspectorDetails = Ui.Stack(Ui.Title($"Remove {account.Name}?"),
            Ui.Text("Historical usage remains available. Remove model bindings before removing this account.", 13, Ui.Muted),
            Ui.Text(references.Length == 0 ? "No model bindings." : "Used by: " + string.Join(", ", references), 13, Ui.Warning),
            OverviewActions(remove, Ui.Button("Cancel", () => ShowAccountInspector(id))));
        if (_accountDetailPage) _page.Content = _accountInspectorDetails;
        else { DetachInspectorContent(); _accountInspector.Child = new ScrollViewer { Content = _accountInspectorDetails, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; }
    }
}
