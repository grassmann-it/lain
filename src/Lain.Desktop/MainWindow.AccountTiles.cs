using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Lain.Client;
using Lain.Core;
using Lain.Desktop.Presentation;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, bool> _accountTechnicalExpansion = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _accountBudgetExpansion = new(StringComparer.Ordinal);
    private string? _accountNavigationId;

    private void ApplyAccountNavigation(WorkspaceNavigationRequest target)
    {
        if (target.AccountId is not { } id) return;
        _accountSearch = ""; _accountProvider = "All providers"; _accountStatus = "All states";
        _accountNavigationId = id;
        if (_configuration is { } configuration && !configuration.Accounts.Any(account => account.Id == id))
            _notice.Text = "This account is no longer configured. Historical usage remains available.";
    }

    private Control AccountTiles(ConfigurationDto configuration, IReadOnlyList<AccountDto>? accounts = null, Action? inventoryChanged = null)
    {
        var options = PresentationFor("Accounts");
        var compact = options.Density == ViewDensity.Compact || options.Level == InformationLevel.Barebones;
        var tiles = new MasonryPanel { HorizontalAlignment = HorizontalAlignment.Stretch, ItemGap = compact ? 8 : 12 };

        foreach (var account in accounts ?? OrderedAccounts(configuration.Accounts).ToArray())
        {
            var quotas = _quotas.Where(q => q.AccountId == account.Id).ToArray();
            var header = new StackPanel { Spacing = compact ? 7 : 10, HorizontalAlignment = HorizontalAlignment.Stretch };
            var identity = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            var name = AccountCell(AccountDisplayName(account), compact ? 14 : 15);
            name.FontWeight = FontWeight.SemiBold;
            name.Classes.Add("section-title");
            ToolTip.SetTip(name, account.Name);
            identity.Children.Add(name);
            // Window exhaustion belongs to that window, never the account identity.
            var badge = !account.Enabled ? Ui.Badge("Disabled")
                : IsAccountPinned(account) ? Ui.Badge("Pinned", "accent") : null;
            if (badge is not null) { Grid.SetColumn(badge, 1); identity.Children.Add(badge); }
            var provider = ProviderName(account) + (account.Enabled ? " · Enabled" : " · Disabled") + (IsAccountPinned(account) ? " · Pinned" : "");
            var heading = new StackPanel { Spacing = 3, Children = { identity, TileText(provider, 11, Ui.Muted) } };
            header.Children.Add(heading);
            if (_preferences.AccountSort == "five-hour")
            {
                var fiveHour = FiveHourWindow(account.Id);
                var fiveHourPercent = QuotaUsedPercent(fiveHour);
                var current = fiveHour is not null && CurrentFiveHourWindow(fiveHour);
                var fiveHourStatus = fiveHourPercent is { } used
                    ? $"5h usage · {(current ? "" : "Last known · ")}{BudgetPercent(used)}% used"
                    : fiveHour is null ? "5h usage · Window not reported" : "5h usage · Percentage unknown";
                var fiveHourColor = fiveHourPercent is null ? Ui.Muted : !current ? Ui.Warning
                    : fiveHourPercent >= 95 ? Ui.Danger : fiveHourPercent >= 80 ? Ui.Warning : Ui.Accent;
                var summary = TileText(fiveHourStatus, 11, fiveHourColor);
                AutomationProperties.SetName(summary, $"5h usage for {account.Name}");
                header.Children.Add(summary);
            }
            if (AccountCredentialWarning(account) is { } credentialWarning) header.Children.Add(TileText(credentialWarning, 11, Ui.Warning));
            if (account.SharedWithLegacy || quotas.Any(quota => quota.SharedWithLegacy))
                header.Children.Add(TileText("Shared provider budget", 11, Ui.Warning));
            else if (account.BudgetPoolId is not null || quotas.Any(quota => quota.BudgetPoolId is not null))
                header.Children.Add(TileText("Provider budget pool", 11, Ui.Muted));
            var windows = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
            QuotaDto[]? displayedSnapshots = null;
            void UpdateWindows()
            {
                // Keep existing controls and geometry through Busy. On completion
                // only this account's windows change; inventory scroll and B stay put.
                if (IsAccountQuotaRefreshing(account.Id) && windows.Content is not null) return;
                var snapshots = _quotas.Where(quota => quota.AccountId == account.Id).ToArray();
                if (displayedSnapshots is not null && snapshots.SequenceEqual(displayedSnapshots)) return;
                displayedSnapshots = snapshots;
                var budgets = AccountQuotaSummary(account, snapshots, compact);
                var observations = snapshots.Where(QuotaPresentation.HasReading).Select(quota => quota.LastSuccessAt).Distinct().ToArray();
                if (observations.Length == 1 && observations[0] is { } checkedAt)
                    budgets.Children.Add(TileText("Checked " + checkedAt.ToLocalTime().ToString("dd MMM · HH:mm", CultureInfo.CurrentCulture) + " local time", 11, Ui.Muted));
                windows.Content = budgets;
            }
            ObserveAccountQuotaRefresh(windows, account.Id, UpdateWindows);
            header.Children.Add(windows);
            header.Children.Add(AccountQuotaRefreshStatus(account));
            var commands = AccountCommands(account, configuration.Revision, inventoryChanged);
            Button? details = null;
            details = Ui.Button("Details", () => ShowAccountInspector(account.Id, details));
            _accountDetailTriggers[account.Id] = details;
            AutomationProperties.SetName(details, $"Open account details for {account.Name}");
            var refresh = Ui.Button("Refresh quota", () => commands.First(command => command.Key == "refresh").Execute());
            AutomationProperties.SetName(refresh, $"Refresh quota for {account.Name}");
            BindAccountQuotaRefreshButton(refresh, account.Id);
            var menu = ObjectCommandMenu(() => AccountCommands(account, configuration.Revision, inventoryChanged));
            var more = Ui.Button("⋯", () => menu.Open());
            more.ContextMenu = menu;
            AutomationProperties.SetName(more, $"Actions for account {account.Name}");
            ToolTip.SetTip(more, "Account actions (also available by right-click or Shift+F10)");
            // The title is the detail trigger; compact commands stay beside the identity.
            header.Children.Remove(heading);
            details.Content = heading;
            details.Padding = new Thickness(0);
            details.BorderThickness = new Thickness(0);
            details.Background = Brushes.Transparent;
            details.HorizontalAlignment = HorizontalAlignment.Stretch;
            details.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            refresh.Content = Ui.Icon("refresh", 14);
            refresh.Width = 32; refresh.MinWidth = 0; refresh.Padding = new Thickness(7);
            ToolTip.SetTip(refresh, "Refresh quota");
            more.Width = 32; more.MinWidth = 0; more.Padding = new Thickness(6);
            var cardHeading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            cardHeading.Children.Add(details);
            var cardActions = Ui.Row(refresh, more); cardActions.Spacing = 4;
            cardActions.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(cardActions, 1); cardHeading.Children.Add(cardActions);
            header.Children.Insert(0, cardHeading);
            var tile = Ui.Card(header);
            tile.Classes.Add("account-tile");
            _accountCards[account.Id] = tile;
            tile.ContextMenu = ObjectCommandMenu(() => AccountCommands(account, configuration.Revision, inventoryChanged));
            tile.BorderBrush = Ui.Edge;
            tile.Focusable = true;
            AutomationProperties.SetName(tile, $"Account budgets for {account.Name}");
            AutomationProperties.SetHelpText(tile, "Important quota windows first. Expand additional budgets for monthly limits, credits and other details. All reported values remain in account details. A window's empty balance does not prove that the whole account is exhausted.");
            tile.Loaded += (_, _) =>
            {
                if (_accountNavigationId != account.Id) return;
                _accountNavigationId = null;
                Dispatcher.UIThread.Post(() => { tile.BringIntoView(); ShowAccountInspector(account.Id, details); }, DispatcherPriority.Background);
            };
            tile.KeyDown += (_, args) =>
            {
                if (args.Key == Avalonia.Input.Key.Enter && ReferenceEquals(args.Source, tile))
                { ShowAccountInspector(account.Id, details); args.Handled = true; }
                else if ((args.Key == Avalonia.Input.Key.F10 && args.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift)) || args.Key == Avalonia.Input.Key.Apps)
                { tile.ContextMenu?.Open(); args.Handled = true; }
            };
            tiles.Children.Add(tile);
        }
        return tiles;
    }

    private StackPanel AccountQuotaSummary(AccountDto account, IReadOnlyList<QuotaDto> snapshots, bool compact)
    {
        var panel = new StackPanel { Spacing = compact ? 7 : 10 };
        if (snapshots.Count == 0)
        {
            panel.Children.Add(AccountTileWindow(account, null));
            return panel;
        }
        var summary = QuotaPresentation.ForSummary(snapshots);
        foreach (var quota in summary.Primary)
            panel.Children.Add(AccountTileWindow(account, quota, commonFreshness: true));

        // Accounts without readings still explain their connection state directly.
        // With readings, setup guidance belongs below the actionable windows.
        if (summary.Primary.Count == 0 && summary.Missing.Count > 0)
            panel.Children.Add(AccountTileWindow(account, summary.Missing[0], commonFreshness: true));
        var extra = summary.Additional.Concat(summary.Primary.Count == 0 ? summary.Missing.Skip(1) : summary.Missing).ToArray();
        if (extra.Length > 0)
        {
            var body = new StackPanel { Spacing = compact ? 8 : 12 };
            foreach (var quota in extra)
                body.Children.Add(AccountTileWindow(account, quota, commonFreshness: true));
            panel.Children.Add(AdditionalAccountBudgets(account, body, extra.Length));
        }
        if (summary.Primary.Count == 0 && summary.Missing.Count == 0)
            panel.Children.Add(TileText("No active quota balance reported · see account details", 12, Ui.Muted));
        return panel;
    }

    private Expander AdditionalAccountBudgets(AccountDto account, Control content, int count)
    {
        var more = new Expander
        {
            Header = $"More budgets & details ({count})",
            IsExpanded = _accountBudgetExpansion.GetValueOrDefault(account.Id),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new Border { Padding = new Thickness(0, 8, 0, 0), Child = content }
        };
        // Keep expanded bars on the same scale and alignment as primary windows.
        more.Resources["ExpanderHeaderPadding"] = new Thickness(0, 8);
        more.Resources["ExpanderHeaderMargin"] = new Thickness(0);
        more.Resources["ExpanderContentPadding"] = new Thickness(0);
        more.Resources["ExpanderContentMinLength"] = 0d;
        AutomationProperties.SetName(more, $"Additional budgets for {account.Name}");
        AutomationProperties.SetHelpText(more, "Monthly limits, additional credits and quota access details. Each budget is separate.");
        more.PropertyChanged += (_, args) =>
        {
            if (args.Property == Expander.IsExpandedProperty) _accountBudgetExpansion[account.Id] = more.IsExpanded;
        };
        return more;
    }

    private static TextBlock TileText(string value, double size, IBrush? color = null)
    {
        var text = Ui.Text(value, size, color);
        text.FontWeight = FontWeight.Normal;
        return text;
    }

    private Control AccountTileWindow(AccountDto account, QuotaDto? quota, bool commonFreshness = false)
    {
        var options = PresentationFor("Accounts");
        var compact = options.Density == ViewDensity.Compact || options.Level == InformationLevel.Barebones;
        var panel = new StackPanel { Spacing = compact ? 3 : 4, HorizontalAlignment = HorizontalAlignment.Stretch };
        var unavailable = QuotaUnavailable(quota);
        if (quota?.MeasurementKind == "rate")
        {
            panel.Children.Add(TileText(QuotaWindowLabel(quota), 12));
            panel.Children.Add(TileText(quota.Used is { } rate && double.IsFinite(rate)
                ? BudgetAmount(rate, quota.Unit) + " observed" : "Rate not supplied", 12));
            panel.Children.Add(TileText("Sample " + Ui.When(quota.DataAsOf ?? quota.ObservedAt), 11, Ui.Muted));
            if (!string.IsNullOrWhiteSpace(quota.Error)) panel.Children.Add(TileText(quota.Error, 11, Ui.Warning));
            if (LastKnownQuota(quota)) panel.Children.Add(TileText("Last known", 11, Ui.Warning));
            if (LastKnownQuota(quota)) panel.Children.Add(QuotaAccessActions(account, quota));
            return panel;
        }
        if (unavailable)
        {
            panel.Children.Add(TileText(QuotaWindowLabel(quota), 12));
            panel.Children.Add(TileText(QuotaSupportMessage(quota), 12, Ui.Muted));
            panel.Children.Add(QuotaAccessActions(account, quota));
            return panel;
        }
        var percent = QuotaUsedPercent(quota);
        var lastKnown = quota is not null && LastKnownQuota(quota);
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        heading.Children.Add(TileText(QuotaWindowLabel(quota), 12));
        var value = percent is { } used ? $"{BudgetPercent(used)}% used" : unavailable ? "Not available"
            : ReportedRemaining(quota) is { } balance ? BudgetAmount(balance, quota!.Unit) + " left"
            : quota is null ? "No quota snapshot" : "Not supplied";
        var metric = TileText(value, 12, lastKnown ? Ui.Warning : percent >= 100 ? Ui.Danger : Ui.Ink);
        metric.FontWeight = FontWeight.Medium;
        Grid.SetColumn(metric, 1); heading.Children.Add(metric);
        panel.Children.Add(heading);
        var remaining = !unavailable && ReportedRemaining(quota) is { } amount ?
            (ValidAmount(quota!.Limit) is { } total ? (PercentUnit(quota.Unit) ? BudgetPercent(amount) : BudgetNumber(amount)) + " of " + BudgetAmount(total, quota.Unit) : BudgetAmount(amount, quota.Unit)) + " left"
            : percent is { } consumed ? $"{BudgetPercent(100 - consumed)}% left"
            : unavailable ? "Remaining not available" : "Remaining not supplied";
        var resetText = QuotaResetText(quota, unavailable);
        if (percent is { } usedPercent)
        {
            var bar = new ProgressBar
            {
                Minimum = 0, Maximum = 100, Value = usedPercent, Height = compact ? 4 : 6,
                HorizontalAlignment = HorizontalAlignment.Stretch, Background = Ui.Edge,
                Foreground = lastKnown ? Ui.Warning : usedPercent >= 95 ? Ui.Danger : usedPercent >= 80 ? Ui.Warning : Ui.Accent
            };
            AutomationProperties.SetName(bar, $"{account.Name} · {QuotaWindowLabel(quota)} · consumed");
            AutomationProperties.SetHelpText(bar, $"{BudgetPercent(usedPercent)}% used. {remaining}. {resetText}. {QuotaStateLabel(quota)}. Last success {Ui.When(quota!.LastSuccessAt)}.");
            panel.Children.Add(bar);
        }
        // Remaining and reset are explicit even when the provider supplies neither.
        var amounts = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        if (percent is not null || ReportedRemaining(quota) is null) amounts.Children.Add(TileText(remaining, 11, Ui.Muted));
        var resetLabel = TileText(resetText, 11, quota is not null && QuotaResetOverdue(quota) ? Ui.Warning : Ui.Muted);
        if (quota?.ResetsAt is not null) { Grid.SetColumn(resetLabel, 1); amounts.Children.Add(resetLabel); }
        if (percent is not null || quota?.ResetsAt is not null || ReportedRemaining(quota) is null) panel.Children.Add(amounts);
        if (!unavailable && quota is not null && !PercentUnit(quota.Unit) && !(percent is not null && ReportedRemaining(quota) is not null))
        {
            if (ValidAmount(quota.Used) is { } amountUsed)
                panel.Children.Add(TileText(ValidAmount(quota.Limit) is { } suppliedLimit
                    ? $"{BudgetNumber(amountUsed)} of {BudgetAmount(suppliedLimit, quota.Unit)} used"
                    : BudgetAmount(amountUsed, quota.Unit) + " used", 11, Ui.Muted));
            else if (ValidAmount(quota.Limit) is { } limit)
                panel.Children.Add(TileText(BudgetAmount(limit, quota.Unit) + " limit · usage not supplied", 11, Ui.Muted));
        }
        if (unavailable) panel.Children.Add(TileText("Quota lookup is not supported here yet.", 11, Ui.Muted));
        if (quota is not null && !unavailable)
        {
            var state = QuotaStateLabel(quota);
            var stateLabel = TileText(QuotaExhausted(quota)
                ? QuotaWindowLabel(quota) + (lastKnown ? " empty · last known" : " empty") : state, 11,
                QuotaExhausted(quota) && !lastKnown ? Ui.Danger : lastKnown ? Ui.Warning : Ui.Muted);
            if (!string.IsNullOrWhiteSpace(stateLabel.Text)) panel.Children.Add(stateLabel);
            var differentFreshness = _quotas.Where(q => q.AccountId == account.Id && QuotaPresentation.HasReading(q)).Select(q => q.LastSuccessAt).Distinct().Count() > 1;
            if (!commonFreshness || differentFreshness)
                panel.Children.Add(TileText(quota.LastSuccessAt is { } success
                    ? "Checked " + success.ToLocalTime().ToString("dd MMM · HH:mm", CultureInfo.CurrentCulture) + " local time"
                    : "Check time not supplied", 11, Ui.Muted));
            if (quota.NextRegenerationAt is { } next)
                panel.Children.Add(TileText("Partial regeneration " + (quota.NextRegenerationPercent is { } portion ? BudgetPercent(portion) + "% · " : "")
                    + next.ToLocalTime().ToString("dd MMM · HH:mm", CultureInfo.CurrentCulture) + " local time", 11, Ui.Muted));
        }
        if (quota is not null && !string.IsNullOrWhiteSpace(quota.Error))
            panel.Children.Add(TileText(quota.Error, 11, unavailable ? Ui.Muted : Ui.Warning));
        if (quota is null) panel.Children.Add(TileText(IsAccountQuotaRefreshing(account.Id)
            ? "Waiting for this account’s first quota snapshot."
            : AccountQuotaError(account.Id) is not null || _quotaError is not null
                ? "Quota could not be loaded. Refresh quota to try again."
                : "No quota snapshot has been reported for this account. Refresh quota from account details.", 11, Ui.Muted));
        if (quota is null || lastKnown || quota.State is "error" or "unavailable") panel.Children.Add(QuotaAccessActions(account, quota));
        return panel;
    }

    private static string QuotaResetText(QuotaDto? quota, bool unavailable = false)
    {
        if (unavailable) return "Reset not available";
        if (quota?.ResetsAt is not { } reset) return "Reset not supplied";
        var value = reset.ToLocalTime().ToString("dd MMM · HH:mm", CultureInfo.CurrentCulture);
        return QuotaResetOverdue(quota) ? $"Reset due {value} · refresh to check" : "Resets " + value + " local time";
    }
}
