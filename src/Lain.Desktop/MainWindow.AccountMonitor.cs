using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Lain.Client;
using Lain.Core;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    private Control AccountInventoryRows(ConfigurationDto configuration, IReadOnlyList<AccountDto> accounts, Action inventoryChanged) =>
        IsMonitorMode ? MonitorAccountTable(configuration, accounts, inventoryChanged) : AccountTiles(configuration, accounts, inventoryChanged);

    private Control MonitorAccountTable(ConfigurationDto configuration, IReadOnlyList<AccountDto> accounts, Action inventoryChanged)
    {
        var table = new StackPanel { Spacing = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(table, "Monitor quota table");
        foreach (var account in accounts)
        {
            var name = Ui.Text(AccountDisplayName(account), 13);
            name.FontWeight = FontWeight.SemiBold;
            var identity = new StackPanel { Spacing = 3 };
            identity.Children.Add(name);
            identity.Children.Add(Ui.Text(ProviderName(account) + (account.Enabled ? "" : " · Disabled")
                + (IsAccountPinned(account) ? " · Pinned" : ""), 11, Ui.Muted));
            if (AccountCredentialWarning(account) is { } warning) identity.Children.Add(Ui.Text(warning, 11, Ui.Warning));
            Button? details = null;
            details = Ui.Button("Account details", () => ShowAccountInspector(account.Id, details));
            details.Content = identity;
            details.Padding = new Thickness(0);
            details.BorderThickness = new Thickness(0);
            details.Background = Brushes.Transparent;
            details.HorizontalAlignment = HorizontalAlignment.Stretch;
            details.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            AutomationProperties.SetName(details, "Open account details for " + account.Name);
            _accountDetailTriggers[account.Id] = details;

            var refresh = Ui.Button("Refresh quota", () => _ = RefreshAccountQuotaAsync(account.Id));
            refresh.Content = Ui.Icon("refresh", 14);
            refresh.Width = 32; refresh.MinWidth = 0; refresh.Padding = new Thickness(7);
            AutomationProperties.SetName(refresh, "Refresh quota for " + account.Name);
            BindAccountQuotaRefreshButton(refresh, account.Id);
            var menu = ObjectCommandMenu(() => AccountCommands(account, configuration.Revision, inventoryChanged));
            var more = Ui.Button("⋯", () => menu.Open()); more.ContextMenu = menu;
            more.Width = 32; more.MinWidth = 0; more.Padding = new Thickness(6);
            AutomationProperties.SetName(more, "Actions for account " + account.Name);
            var actions = Ui.Row(refresh, more); actions.Spacing = 4; actions.VerticalAlignment = VerticalAlignment.Top;
            var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            heading.Children.Add(details); Grid.SetColumn(actions, 1); heading.Children.Add(actions);
            var accountHeader = new StackPanel { Spacing = 3, Children = { heading, AccountQuotaRefreshStatus(account) } };
            var header = new Border { Padding = new Thickness(10, 9, 10, 3), Background = Ui.Surface, Child = accountHeader };

            var windows = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
            QuotaDto[]? displayedSnapshots = null;
            void UpdateWindows()
            {
                if (IsAccountQuotaRefreshing(account.Id) && windows.Content is not null) return;
                var snapshots = _quotas.Where(quota => quota.AccountId == account.Id).ToArray();
                if (displayedSnapshots is not null && snapshots.SequenceEqual(displayedSnapshots)) return;
                displayedSnapshots = snapshots;
                var rows = new StackPanel { Spacing = 0 };
                Control WindowRow(QuotaDto? quota)
                {
                    return new Border { Padding = new Thickness(10, 8), BorderBrush = Ui.Edge,
                        BorderThickness = new Thickness(0, 0, 0, 1), Child = BudgetWindowRow(account, quota) };
                }
                if (snapshots.Length == 0) rows.Children.Add(WindowRow(null));
                var summary = QuotaPresentation.ForSummary(snapshots);
                foreach (var quota in summary.Primary) rows.Children.Add(WindowRow(quota));
                if (summary.Primary.Count == 0 && summary.Missing.Count > 0) rows.Children.Add(WindowRow(summary.Missing[0]));
                var extra = summary.Additional.Concat(summary.Primary.Count == 0 ? summary.Missing.Skip(1) : summary.Missing).ToArray();
                if (extra.Length > 0)
                {
                    var body = new StackPanel { Spacing = 0 };
                    foreach (var quota in extra) body.Children.Add(WindowRow(quota));
                    var moreBudgets = AdditionalAccountBudgets(account, body, extra.Length);
                    moreBudgets.Margin = new Thickness(0, 4, 0, 8);
                    moreBudgets.Resources["ExpanderHeaderPadding"] = new Thickness(10, 8);
                    rows.Children.Add(moreBudgets);
                }
                if (snapshots.Length > 0 && summary.Primary.Count == 0 && summary.Missing.Count == 0)
                    rows.Children.Add(new Border { Padding = new Thickness(10, 8), Child = Ui.Text("No active quota balance reported · see account details", 12, Ui.Muted) });
                windows.Content = rows;
            }
            ObserveAccountQuotaRefresh(windows, account.Id, UpdateWindows);
            var row = new Border { BorderBrush = Ui.Edge, BorderThickness = new Thickness(1),
                Child = new StackPanel { Spacing = 0, Children = { header, windows } }, Focusable = true };
            row.Classes.Add("account-monitor-row");
            AutomationProperties.SetName(row, "Account budgets for " + account.Name);
            AutomationProperties.SetHelpText(row, "Important quota windows first; expand additional budgets for further limits and credits. Consumption bars share a 0 to 100 percent scale. Shared budgets are not added.");
            row.ContextMenu = ObjectCommandMenu(() => AccountCommands(account, configuration.Revision, inventoryChanged));
            _accountCards[account.Id] = row;
            row.Loaded += (_, _) =>
            {
                if (_accountNavigationId != account.Id) return;
                _accountNavigationId = null;
                Dispatcher.UIThread.Post(() => { row.BringIntoView(); ShowAccountInspector(account.Id, details); }, DispatcherPriority.Background);
            };
            row.KeyDown += (_, args) =>
            {
                if (args.Key == Key.Enter && ReferenceEquals(args.Source, row))
                { ShowAccountInspector(account.Id, details); args.Handled = true; }
                else if ((args.Key == Key.F10 && args.KeyModifiers.HasFlag(KeyModifiers.Shift)) || args.Key == Key.Apps)
                { row.ContextMenu?.Open(); args.Handled = true; }
            };
            table.Children.Add(row);
        }
        return table;
    }
}
