using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Lain.Core;
using Lain.Desktop.Presentation;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    private readonly HashSet<string> _expandedAccountIds = [];
    private readonly Dictionary<string, bool> _accountTechnicalExpansion = new(StringComparer.Ordinal);
    private string? _accountNavigationId;

    private void ApplyAccountNavigation(WorkspaceNavigationRequest target)
    {
        if (target.AccountId is not { } id) return;
        _accountSearch = ""; _accountProvider = "All providers"; _accountStatus = "All states";
        _expandedAccountIds.Add(id);
        _accountNavigationId = id;
        if (_configuration is { } configuration && !configuration.Accounts.Any(account => account.Id == id))
            _notice.Text = "This account is no longer configured. Historical usage remains available.";
    }

    private Control AccountTiles(ConfigurationDto configuration, IReadOnlyList<AccountDto>? accounts = null, Action? inventoryChanged = null)
    {
        _expandedAccountIds.RemoveWhere(id => !configuration.Accounts.Any(a => a.Id == id));
        var options = PresentationFor("Accounts");
        var compact = options.Density == ViewDensity.Compact;
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
            var limiting = MostLimitingWindow(quotas);
            var percent = QuotaUsedPercent(limiting);
            var lastKnown = limiting is not null && LastKnownQuota(limiting);
            var badge = !account.Enabled ? Ui.Badge("Disabled")
                : percent >= 100 ? Ui.Badge(lastKnown ? "Last known exhausted" : "Exhausted", lastKnown ? "warning" : "danger")
                : percent >= 90 ? Ui.Badge(lastKnown ? "Last known near limit" : "Near limit", "warning")
                : IsAccountPinned(account) ? Ui.Badge("Pinned", "accent") : null;
            if (badge is not null) { Grid.SetColumn(badge, 1); identity.Children.Add(badge); }
            var provider = ProviderName(account) + (account.Enabled ? " · Enabled" : " · Disabled") + (IsAccountPinned(account) ? " · Pinned" : "");
            var heading = new StackPanel { Spacing = 3, Children = { identity, TileText(provider, 11, Ui.Muted) } };
            header.Children.Add(heading);
            if (AccountCredentialWarning(account) is { } credentialWarning) header.Children.Add(TileText(credentialWarning, 11, Ui.Warning));
            if (account.SharedWithLegacy || quotas.Any(quota => quota.SharedWithLegacy))
                header.Children.Add(TileText("Shared provider budget", 11, Ui.Warning));
            else if (account.BudgetPoolId is not null || quotas.Any(quota => quota.BudgetPoolId is not null))
                header.Children.Add(TileText("Provider budget pool", 11, Ui.Muted));
            // Every reported window stays in the closed tile, including balance-only
            // windows beside percentage windows and explicitly unknown windows.
            if (quotas.Length == 0) header.Children.Add(AccountTileWindow(account, null));
            foreach (var quota in quotas) header.Children.Add(AccountTileWindow(account, quota));
            var hint = TileText("Show details", 11, Ui.Accent);
            header.Children.Add(hint);

            var detail = AccountDetail(account, configuration.Revision, inventoryChanged);
            var tile = new Expander
            {
                Header = header, Content = detail, IsExpanded = _expandedAccountIds.Contains(account.Id),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top, BorderBrush = percent >= 100 ? lastKnown ? Ui.Warning : Ui.Danger : Ui.Edge,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6)
            };
            tile.Classes.Add("account-tile");
            if (this.TryFindResource("CardExpander", out var theme) && theme is Avalonia.Styling.ControlTheme controlTheme) tile.Theme = controlTheme;
            var padding = compact ? 9 : 12;
            tile.Resources["ExpanderHeaderPadding"] = new Thickness(padding);
            tile.Resources["ExpanderHeaderMargin"] = new Thickness(0);
            tile.Resources["ExpanderContentPadding"] = new Thickness(padding, 0, padding, padding);
            tile.Resources["ExpanderContentMinLength"] = 0d;
            AutomationProperties.SetName(tile, $"Account budgets for {account.Name}");
            AutomationProperties.SetHelpText(tile, "All reported budget windows are shown. Expand for account actions, recorded usage, models, pins and source details.");
            tile.TemplateApplied += (_, args) =>
            {
                if (args.NameScope.Find<ToggleButton>("ExpanderHeader") is not { } toggle) return;
                AutomationProperties.SetName(toggle, $"Toggle account budgets for {account.Name}");
                toggle.HorizontalAlignment = HorizontalAlignment.Stretch;
                toggle.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            };
            tile.PropertyChanged += (_, args) =>
            {
                if (args.Property != Expander.IsExpandedProperty) return;
                if (tile.IsExpanded) _expandedAccountIds.Add(account.Id); else _expandedAccountIds.Remove(account.Id);
                hint.Text = tile.IsExpanded ? "Hide details" : "Show details";
                tiles.InvalidateMeasure();
            };
            tile.Loaded += (_, _) =>
            {
                if (_accountNavigationId != account.Id) return;
                _accountNavigationId = null;
                Dispatcher.UIThread.Post(() => tile.BringIntoView(), DispatcherPriority.Background);
            };
            hint.Text = tile.IsExpanded ? "Hide details" : "Show details";
            tiles.Children.Add(tile);
        }
        return tiles;
    }

    private static TextBlock TileText(string value, double size, IBrush? color = null)
    {
        var text = Ui.Text(value, size, color);
        text.FontWeight = FontWeight.Normal;
        return text;
    }

    private Control AccountTileWindow(AccountDto account, QuotaDto? quota)
    {
        var options = PresentationFor("Accounts");
        var compact = options.Density == ViewDensity.Compact;
        var panel = new StackPanel { Spacing = compact ? 3 : 4, HorizontalAlignment = HorizontalAlignment.Stretch };
        var unavailable = QuotaUnavailable(quota) || IsMiMo(account) && quota?.Source == "unavailable";
        var percent = unavailable ? null : QuotaUsedPercent(quota);
        var lastKnown = quota is not null && LastKnownQuota(quota);
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        heading.Children.Add(TileText(QuotaWindowLabel(quota), 12));
        var value = percent is { } used ? $"{BudgetNumber(used)}% used" : unavailable ? "Not available"
            : quota is null ? "No quota snapshot" : "Percentage not available";
        var metric = TileText(value, 12, lastKnown ? Ui.Warning : percent >= 100 ? Ui.Danger : Ui.Ink);
        metric.FontWeight = FontWeight.Medium;
        Grid.SetColumn(metric, 1); heading.Children.Add(metric);
        panel.Children.Add(heading);
        var remaining = !unavailable && ValidAmount(quota?.Remaining) is { } amount ? BudgetAmount(amount, quota!.Unit) + " left"
            : percent is { } consumed ? $"{BudgetNumber(100 - consumed)}% left"
            : unavailable ? "Remaining not available" : "Remaining not supplied";
        var resetText = QuotaResetText(quota, unavailable);
        if (percent is { } usedPercent && options.Level != InformationLevel.Barebones)
        {
            var bar = new ProgressBar
            {
                Minimum = 0, Maximum = 100, Value = usedPercent, Height = compact ? 4 : 6,
                HorizontalAlignment = HorizontalAlignment.Stretch, Background = Ui.Edge,
                Foreground = lastKnown ? Ui.Warning : usedPercent >= 95 ? Ui.Danger : usedPercent >= 80 ? Ui.Warning : Ui.Accent
            };
            AutomationProperties.SetName(bar, $"{account.Name} · {QuotaWindowLabel(quota)} · consumed");
            AutomationProperties.SetHelpText(bar, $"{BudgetNumber(usedPercent)}% used. {remaining}. {resetText}. {QuotaStateLabel(quota)}. Last success {Ui.When(quota!.LastSuccessAt)}.");
            panel.Children.Add(bar);
        }
        // Remaining and reset are explicit even when the provider supplies neither.
        var amounts = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        amounts.Children.Add(TileText(remaining, 11, Ui.Muted));
        var resetLabel = TileText(resetText, 11, quota is not null && QuotaResetOverdue(quota) ? Ui.Warning : Ui.Muted);
        Grid.SetColumn(resetLabel, 1); amounts.Children.Add(resetLabel);
        panel.Children.Add(amounts);
        if (!unavailable && quota is not null && !PercentUnit(quota.Unit))
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
            var freshness = quota.LastSuccessAt is { } success
                ? "Checked " + success.ToLocalTime().ToString(success.ToLocalTime().Date == DateTimeOffset.Now.Date ? "HH:mm" : "dd MMM · HH:mm", CultureInfo.CurrentCulture) + " local time"
                : "Check time not supplied";
            // Even Barebones retains stale/error provenance. Normal success times
            // are supplementary context and remain available in local details.
            if (options.Level != InformationLevel.Barebones || !string.IsNullOrWhiteSpace(state))
            {
                var note = TileText((string.IsNullOrWhiteSpace(state) ? "" : state + " · ") + freshness, 10, lastKnown ? Ui.Warning : Ui.Muted);
                ToolTip.SetTip(note, $"Last successful quota update: {quota.LastSuccessAt?.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}");
                panel.Children.Add(note);
            }
        }
        if (quota is not null && !string.IsNullOrWhiteSpace(quota.Error))
            panel.Children.Add(TileText(quota.Error, 11, unavailable ? Ui.Muted : Ui.Warning));
        if (quota is null) panel.Children.Add(TileText("Quota has not been loaded. Refresh quota from account details.", 11, Ui.Muted));
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
