using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Lain.Core;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    private readonly HashSet<string> _expandedAccountIds = [];

    private Control AccountTiles(ConfigurationDto configuration)
    {
        _expandedAccountIds.RemoveWhere(id => !configuration.Accounts.Any(a => a.Id == id));
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
        var tiles = new List<Expander>();
        var columns = 2;
        void ArrangeTiles()
        {
            grid.ColumnDefinitions = new ColumnDefinitions(columns == 2 ? "*,*" : "*");
            var row = 0;
            var column = 0;
            foreach (var tile in tiles)
            {
                if (tile.IsExpanded && column != 0) { row++; column = 0; }
                Grid.SetRow(tile, row);
                Grid.SetColumn(tile, column);
                Grid.SetColumnSpan(tile, tile.IsExpanded ? columns : 1);
                if (tile.IsExpanded || ++column == columns) { row++; column = 0; }
            }
            grid.RowDefinitions = new RowDefinitions(string.Join(",", Enumerable.Repeat("Auto", Math.Max(1, row + (column > 0 ? 1 : 0)))));
        }

        // Keep the configured order within each group. A changing percentage must
        // not move the tile the user is about to open.
        var accounts = configuration.Accounts.OrderBy(account =>
            _quotas.Any(q => q.AccountId == account.Id && (QuotaUsedPercent(q).HasValue || ValidAmount(q.Remaining).HasValue)) ? 0 : 1);
        foreach (var account in accounts)
        {
            var quotas = _quotas.Where(q => q.AccountId == account.Id).ToArray();
            var displayed = quotas.Where(q => QuotaUsedPercent(q).HasValue).ToArray();
            if (displayed.Length == 0) displayed = quotas.Where(q => !QuotaUnavailable(q) && ValidAmount(q.Remaining).HasValue).ToArray();
            if (displayed.Length == 0 && quotas.Length > 0) displayed = [quotas[0]];

            var header = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Stretch };
            var identity = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
            var name = AccountCell(AccountDisplayName(account), 15);
            name.FontWeight = FontWeight.SemiBold;
            name.Classes.Add("section-title");
            ToolTip.SetTip(name, account.Name);
            identity.Children.Add(name);
            var limiting = MostLimitingWindow(quotas);
            var percent = QuotaUsedPercent(limiting);
            var badge = !account.Enabled ? Ui.Badge("Disabled")
                : percent >= 100 ? Ui.Badge("Exhausted", "danger")
                : percent >= 90 ? Ui.Badge("Near limit", "warning") : null;
            if (badge is not null) { Grid.SetColumn(badge, 1); identity.Children.Add(badge); }
            var heading = new StackPanel { Spacing = 3, Children = { identity, TileText(ProviderName(account), 11, Ui.Muted) } };
            header.Children.Add(heading);
            if (displayed.Length == 0) header.Children.Add(AccountTileWindow(account, null));
            foreach (var quota in displayed) header.Children.Add(AccountTileWindow(account, quota));
            var hint = TileText("Show details", 11, Ui.Accent);
            header.Children.Add(hint);

            var detail = new StackPanel { Spacing = 10 };
            foreach (var quota in displayed.Where(q => !PercentUnit(q.Unit)))
            {
                if (ValidAmount(quota.Used) is not { } used || ValidAmount(quota.Limit) is not { } limit) continue;
                detail.Children.Add(TileText($"{QuotaWindowLabel(quota)} · {BudgetNumber(used)} of {BudgetAmount(limit, quota.Unit)} used", 12, Ui.Muted));
            }
            foreach (var quota in quotas.Except(displayed))
                detail.Children.Add(new Border { BorderBrush = Ui.Edge, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 10, 0, 0), Child = AccountTileWindow(account, quota) });
            detail.Children.Add(AccountDetail(account, configuration.Revision));
            var tile = new Expander
            {
                Header = header, Content = detail, IsExpanded = _expandedAccountIds.Contains(account.Id),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top, BorderBrush = percent >= 100 ? Ui.Danger : Ui.Edge,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6)
            };
            tile.Classes.Add("account-tile");
            if (this.TryFindResource("CardExpander", out var theme) && theme is Avalonia.Styling.ControlTheme controlTheme) tile.Theme = controlTheme;
            tile.Resources["ExpanderHeaderPadding"] = new Thickness(12);
            tile.Resources["ExpanderHeaderMargin"] = new Thickness(0);
            tile.Resources["ExpanderContentPadding"] = new Thickness(12, 0, 12, 12);
            tile.Resources["ExpanderContentMinLength"] = 0d;
            AutomationProperties.SetName(tile, $"Account budgets for {account.Name}");
            AutomationProperties.SetHelpText(tile, "Expand to see additional balances, account controls and source details.");
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
                ArrangeTiles();
            };
            hint.Text = tile.IsExpanded ? "Hide details" : "Show details";
            tiles.Add(tile);
            grid.Children.Add(tile);
        }
        grid.SizeChanged += (_, _) =>
        {
            var next = grid.Bounds.Width >= 700 ? 2 : 1;
            if (next == columns) return;
            columns = next;
            ArrangeTiles();
        };
        ArrangeTiles();
        return grid;
    }

    private static TextBlock TileText(string value, double size, IBrush? color = null)
    {
        var text = Ui.Text(value, size, color);
        text.FontWeight = FontWeight.Normal;
        return text;
    }

    private Control AccountTileWindow(AccountDto account, QuotaDto? quota)
    {
        var panel = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Stretch };
        var unavailable = QuotaUnavailable(quota) || IsMiMo(account) && quota?.Source == "unavailable";
        var percent = unavailable ? null : QuotaUsedPercent(quota);
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        var label = TileText(QuotaWindowLabel(quota), 12);
        heading.Children.Add(label);
        var value = percent is { } used ? $"{BudgetNumber(used)}% used" : unavailable ? "Not available"
            : quota is not null && ValidAmount(quota.Remaining) is { } remainingAmount ? BudgetAmount(remainingAmount, quota.Unit) + " left" : "No usage data";
        var metric = TileText(value, 12, percent >= 100 ? Ui.Danger : Ui.Ink);
        metric.FontWeight = FontWeight.Medium;
        Grid.SetColumn(metric, 1); heading.Children.Add(metric);
        panel.Children.Add(heading);
        if (percent is { } usedPercent)
        {
            var remaining = ValidAmount(quota!.Remaining) is { } amount ? BudgetAmount(amount, quota.Unit) + " left" : $"{BudgetNumber(100 - usedPercent)}% left";
            var resetText = quota.ResetsAt is { } reset ? reset.ToLocalTime().ToString("dd MMM · HH:mm", CultureInfo.CurrentCulture) : "Not supplied";
            var bar = new ProgressBar
            {
                Minimum = 0, Maximum = 100, Value = usedPercent, Height = 6,
                HorizontalAlignment = HorizontalAlignment.Stretch, Background = Ui.Edge,
                Foreground = LastKnownQuota(quota) ? Ui.Warning : usedPercent >= 95 ? Ui.Danger : usedPercent >= 80 ? Ui.Warning : Ui.Accent
            };
            AutomationProperties.SetName(bar, $"{account.Name} · {QuotaWindowLabel(quota)} · consumed");
            AutomationProperties.SetHelpText(bar, $"{BudgetNumber(usedPercent)}% used. {remaining}. Resets {resetText} local time. Last success {Ui.When(quota.LastSuccessAt)}.");
            panel.Children.Add(bar);
            var amounts = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
            amounts.Children.Add(TileText(remaining, 11, Ui.Muted));
            var resetLabel = TileText(quota.ResetsAt is null ? "Reset not supplied" : "Resets " + resetText, 11, Ui.Muted);
            Grid.SetColumn(resetLabel, 1); amounts.Children.Add(resetLabel);
            panel.Children.Add(amounts);
        }
        else if (unavailable) panel.Children.Add(TileText("Quota lookup is not supported here yet.", 11, Ui.Muted));
        else if (quota?.ResetsAt is { } otherReset)
            panel.Children.Add(TileText("Resets " + otherReset.ToLocalTime().ToString("dd MMM · HH:mm", CultureInfo.CurrentCulture) + " local time", 11, Ui.Muted));

        if (!unavailable && quota is not null)
        {
            var state = QuotaStateLabel(quota);
            var freshness = quota.LastSuccessAt is { } success
                ? "Checked " + success.ToLocalTime().ToString(success.ToLocalTime().Date == DateTimeOffset.Now.Date ? "HH:mm" : "dd MMM · HH:mm", CultureInfo.CurrentCulture)
                : "Check time not supplied";
            var note = TileText(string.IsNullOrWhiteSpace(state) ? freshness + " · local time" : state + " · " + freshness + " · local time", 10,
                LastKnownQuota(quota) ? Ui.Warning : Ui.Muted);
            ToolTip.SetTip(note, $"Last successful quota update: {quota.LastSuccessAt?.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}");
            panel.Children.Add(note);
        }
        return panel;
    }
}
