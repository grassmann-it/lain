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
    private readonly Dictionary<string, bool> _accountTechnicalExpansion = new(StringComparer.Ordinal);
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
            var limiting = MostLimitingWindow(quotas);
            var percent = QuotaUsedPercent(limiting);
            var lastKnown = limiting is not null && LastKnownQuota(limiting);
            var badge = !account.Enabled ? Ui.Badge("Disabled")
                : QuotaExhausted(limiting) ? Ui.Badge(lastKnown ? "Window empty · last known" : "Window empty", lastKnown ? "warning" : "danger")
                : percent >= 90 ? Ui.Badge(lastKnown ? "Window near limit · last known" : "Window near limit", "warning")
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
                    ? $"5h usage · {(current ? "" : "Last known · ")}{BudgetNumber(used)}% used"
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
            // Every reported window stays in the closed tile, including balance-only
            // windows beside percentage windows and explicitly unknown windows.
            if (quotas.Length == 0) header.Children.Add(AccountTileWindow(account, null));
            foreach (var quota in quotas) header.Children.Add(AccountTileWindow(account, quota));
            var commands = AccountCommands(account, configuration.Revision, inventoryChanged);
            var details = Ui.Button("Details", () => ShowAccountInspector(account.Id));
            AutomationProperties.SetName(details, $"Open account details for {account.Name}");
            var refresh = Ui.Button("Refresh quota", () => commands.First(command => command.Key == "refresh").Execute());
            AutomationProperties.SetName(refresh, $"Refresh quota for {account.Name}");
            var menu = ObjectCommandMenu(commands);
            var more = Ui.Button("⋯", () => menu.Open());
            more.ContextMenu = menu;
            AutomationProperties.SetName(more, $"Actions for account {account.Name}");
            ToolTip.SetTip(more, "Account actions (also available by right-click or Shift+F10)");
            header.Children.Add(OverviewActions(details, refresh, more));
            var tile = Ui.Card(header);
            tile.Classes.Add("account-tile");
            tile.ContextMenu = ObjectCommandMenu(commands);
            tile.BorderBrush = Ui.Edge;
            tile.Focusable = true;
            AutomationProperties.SetName(tile, $"Account budgets for {account.Name}");
            AutomationProperties.SetHelpText(tile, "All reported windows. A window's empty balance does not prove that the whole account is exhausted.");
            tile.Loaded += (_, _) =>
            {
                if (_accountNavigationId != account.Id) return;
                _accountNavigationId = null;
                Dispatcher.UIThread.Post(() => { tile.BringIntoView(); ShowAccountInspector(account.Id, details); }, DispatcherPriority.Background);
            };
            details.Click += (_, _) => _accountInspectorOrigin = details;
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
        var compact = options.Density == ViewDensity.Compact || options.Level == InformationLevel.Barebones;
        var panel = new StackPanel { Spacing = compact ? 3 : 4, HorizontalAlignment = HorizontalAlignment.Stretch };
        var unavailable = QuotaUnavailable(quota);
        if (unavailable)
        {
            panel.Children.Add(TileText(QuotaSupportMessage(quota), 12, Ui.Muted));
            if (Uri.TryCreate(quota?.Support?.ActionUrl, UriKind.Absolute, out var actionUri) && actionUri.Scheme == Uri.UriSchemeHttps)
                panel.Children.Add(Ui.Button("View quota access guidance", async () => await OpenProviderLinkAsync(actionUri)));
            return panel;
        }
        var percent = QuotaUsedPercent(quota);
        var lastKnown = quota is not null && LastKnownQuota(quota);
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        heading.Children.Add(TileText(QuotaWindowLabel(quota), 12));
        var value = percent is { } used ? $"{BudgetNumber(used)}% used" : unavailable ? "Not available"
            : ReportedRemaining(quota) is { } balance ? BudgetAmount(balance, quota!.Unit) + " left"
            : quota is null ? "No quota snapshot" : "Usage";
        var metric = TileText(value, 12, lastKnown ? Ui.Warning : percent >= 100 ? Ui.Danger : Ui.Ink);
        metric.FontWeight = FontWeight.Medium;
        Grid.SetColumn(metric, 1); heading.Children.Add(metric);
        panel.Children.Add(heading);
        var remaining = !unavailable && ReportedRemaining(quota) is { } amount ? BudgetAmount(amount, quota!.Unit) + " left"
            : percent is { } consumed ? $"{BudgetNumber(100 - consumed)}% left"
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
            AutomationProperties.SetHelpText(bar, $"{BudgetNumber(usedPercent)}% used. {remaining}. {resetText}. {QuotaStateLabel(quota)}. Last success {Ui.When(quota!.LastSuccessAt)}.");
            panel.Children.Add(bar);
        }
        // Remaining and reset are explicit even when the provider supplies neither.
        var amounts = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        amounts.Children.Add(TileText(remaining, 11, Ui.Muted));
        var resetLabel = TileText(resetText, 11, quota is not null && QuotaResetOverdue(quota) ? Ui.Warning : Ui.Muted);
        if (quota?.ResetsAt is not null) { Grid.SetColumn(resetLabel, 1); amounts.Children.Add(resetLabel); }
        if (quota?.Remaining is not null || percent is not null || quota?.ResetsAt is not null) panel.Children.Add(amounts);
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
                var source = Uri.TryCreate(quota.Source, UriKind.Absolute, out var sourceUri) && sourceUri.Host.Length > 0 ? sourceUri.Host : quota.Source;
                var note = TileText((string.IsNullOrWhiteSpace(state) ? "" : state + " · ") + freshness + " · Source " + source, 10, lastKnown ? Ui.Warning : Ui.Muted);
                ToolTip.SetTip(note, $"Last successful quota update: {quota.LastSuccessAt?.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}");
                panel.Children.Add(note);
                if (quota.DataAsOf is { } dataAsOf) panel.Children.Add(TileText("Data as of " + dataAsOf.ToLocalTime().ToString("dd MMM · HH:mm", CultureInfo.CurrentCulture), 10, Ui.Muted));
            }
        }
        if (quota is not null)
            foreach (var fact in QuotaPresentationFacts(quota)) panel.Children.Add(TileText(fact, 11, Ui.Muted));
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
