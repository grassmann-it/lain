using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Lain.Core;
using Lain.Client;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    private static string ProviderName(AccountDto account) => account.Provider.ToLowerInvariant() switch
    {
        "codex" or "gpt" => "Codex",
        "openai" when account.CredentialKind.StartsWith("codex", StringComparison.OrdinalIgnoreCase) => "Codex",
        "openai" => "OpenAI",
        "commandcode" or "command-code" => "Command Code",
        "zai" or "z.ai" or "z-ai" => "Z.ai",
        "kimi" or "kimi-coding" or "moonshot" => "Kimi",
        "xiaomi-mimo" or "mimo" or "xiaomi" => "Xiaomi MiMo",
        _ => account.Provider
    };

    private string AccountDisplayName(AccountDto account) =>
        AccountDisplayNames.Get(account, _configuration?.Accounts ?? [account]);

    private static bool IsMiMo(AccountDto account) => account.Provider.ToLowerInvariant() is "xiaomi-mimo" or "mimo" or "xiaomi";

    private static string QuotaSupportMessage(QuotaDto? quota) => string.IsNullOrWhiteSpace(quota?.Support?.Message)
        ? "No separate quota lookup is configured. See provider setup for available access." : quota.Support.Message;

    private static bool QuotaUnavailable(QuotaDto? quota) => quota?.State is "unsupported" or "not-supported"
        // A successful monitor response without exported series is guidance, not an update failure.
        // Retained readings (stale) and any real error keep their diagnostic presentation.
        || quota is { State: "unavailable", Error: null, Used: null, Remaining: null, Limit: null, UsedPercent: null,
            Support: { Kind: "unavailable" } }
        || quota is not null && quota.State is not ("error" or "unavailable" or "stale") && quota.Used is null && quota.Remaining is null && quota.Limit is null && quota.UsedPercent is null
        && quota.Support?.Kind is "console" or "unsupported" or "unimplemented" or "setup-required" or "awaiting-observation" or "passive";

    private static double? ValidAmount(double? value) => value is { } number && double.IsFinite(number) && number >= 0 ? number : null;

    private static double? ReportedRemaining(QuotaDto? quota) => quota?.Remaining is { } remaining && double.IsFinite(remaining)
        && quota.MeasurementKind is not ("usage" or "limit" or "rate")
        && !QuotaDisplayFacts.IsElapsedFreeDay(quota, DateTimeOffset.UtcNow) ? remaining : null;

    private static bool QuotaExhausted(QuotaDto? quota) => quota is not null && !QuotaUnavailable(quota)
        && (QuotaUsedPercent(quota) >= 100 || ReportedRemaining(quota) <= 0);

    // Percentages describe consumption. Never paint a missing value as zero, clamp
    // an invalid percentage, or combine separate windows into an account balance.
    private static double? QuotaUsedPercent(QuotaDto? quota)
    {
        if (quota is null || QuotaUnavailable(quota) || quota.MeasurementKind is "balance" or "usage" or "limit" or "rate") return null;
        if (QuotaDisplayFacts.IsElapsedFreeDay(quota, DateTimeOffset.UtcNow)) return null;
        if (quota.UsedPercent is { } supplied)
            return double.IsFinite(supplied) && supplied is >= 0 and <= 100 ? supplied : null;
        if (ValidAmount(quota.Limit) is not { } limit || limit <= 0) return null;
        if (ValidAmount(quota.Used) is { } used)
            return used <= limit ? used / limit * 100 : null;
        if (ReportedRemaining(quota) is { } remaining && remaining >= 0 && remaining <= limit)
            return (limit - remaining) / limit * 100;
        return null;
    }

    private static bool PercentUnit(string unit) => unit.Equals("percent", StringComparison.OrdinalIgnoreCase) || unit == "%";

    private static string BudgetNumber(double value) => Lain.Client.QuotaDisplayFacts.Amount(value, CultureInfo.CurrentCulture);

    private static string BudgetPercent(double value) => Lain.Client.QuotaDisplayFacts.Percent(value, CultureInfo.CurrentCulture);

    private static string BudgetAmount(double value, string unit)
    {
        if (PercentUnit(unit)) return $"{BudgetPercent(value)}%";
        var suffix = string.IsNullOrWhiteSpace(unit) || unit == "unknown" ? "" : " " + unit;
        return BudgetNumber(value) + suffix;
    }

    private static string QuotaWindowLabel(QuotaDto? quota)
    {
        if (quota is null) return "Budget";
        if (!string.IsNullOrWhiteSpace(quota.WindowLabel)) return quota.WindowLabel;
        var fragment = quota.Source.Split('#').LastOrDefault() ?? "";
        return fragment switch
        {
            "primary_window" => "Primary window",
            "secondary_window" => "Secondary window",
            "fiveHour" => "5 hours",
            "weekly" => "Weekly",
            "monthlyCredits" => "Monthly credits",
            "purchasedCredits" => "Purchased credits",
            "freeCredits" => "Free credits",
            "model-monthly-remaining-unknown" => "Model allowance",
            var value when value.StartsWith("organization_model_", StringComparison.Ordinal) => "Model limit",
            var value when value.StartsWith("organization_", StringComparison.Ordinal) => "Organization limit",
            _ => QuotaUnavailable(quota) ? "Quota API" : "Budget"
        };
    }

    private static QuotaDto? MostLimitingWindow(IEnumerable<QuotaDto> quotas)
    {
        var rows = quotas.ToArray();
        return rows.FirstOrDefault(QuotaExhausted) ?? rows.Where(q => QuotaUsedPercent(q).HasValue)
            .OrderByDescending(q => QuotaUsedPercent(q)).FirstOrDefault()
            ?? rows.FirstOrDefault(q => ReportedRemaining(q).HasValue && !QuotaUnavailable(q))
            ?? rows.FirstOrDefault();
    }

    private static bool QuotaResetOverdue(QuotaDto quota) => quota.ResetsAt is { } reset && reset <= DateTimeOffset.UtcNow;

    private static bool IsFiveHourWindow(QuotaDto quota)
    {
        // These are the semantic labels emitted by CodexQuotaAdapter (18000
        // seconds), CommandCodeQuotaAdapter (fiveHour), KimiQuotaAdapter
        // (limit_5h), and ZaiQuotaAdapter (unit 3 / number 5). A primary Codex
        // window without its duration is deliberately not assumed to be 5h.
        if (quota.WindowLabel is { Length: > 0 } label)
        {
            if (label == "5 hours") return true;
            var parts = label.Split(" · ", StringSplitOptions.None);
            return (parts.Length is 2 or 3) && (parts[0] is "Credits" or "Tokens") && parts[1] == "5 hours"
                && (parts.Length == 2 || int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var occurrence) && occurrence > 1);
        }
        var separator = quota.Source.LastIndexOf('#');
        return separator >= 0 && (quota.Source[(separator + 1)..] is "fiveHour" or "limit_5h");
    }

    private bool CurrentFiveHourWindow(QuotaDto quota) => !LastKnownQuota(quota) && (quota.State is "ok" or "available");

    private QuotaDto? FiveHourWindow(string accountId) => _quotas.Where(quota => quota.AccountId == accountId && IsFiveHourWindow(quota))
        .OrderBy(quota => QuotaUsedPercent(quota).HasValue ? CurrentFiveHourWindow(quota) ? 0 : 1 : 2)
        .ThenByDescending(QuotaUsedPercent).ThenBy(quota => quota.Source, StringComparer.Ordinal).FirstOrDefault();

    private bool LastKnownQuota(QuotaDto quota) => !QuotaUnavailable(quota) && (!_hostAvailable || _quotaError is not null || AccountQuotaError(quota.AccountId) is not null
        || quota.State is "stale" or "error" or "unavailable" || QuotaResetOverdue(quota)
        || quota.FreshUntil is { } freshUntil && freshUntil <= DateTimeOffset.UtcNow);

    private string QuotaStateLabel(QuotaDto? quota)
    {
        if (quota is null) return "Not loaded";
        if (QuotaUnavailable(quota)) return "Not available";
        if (LastKnownQuota(quota) && (QuotaUsedPercent(quota).HasValue || ReportedRemaining(quota).HasValue || ValidAmount(quota.Used).HasValue)) return "Last known";
        return quota.State switch
        {
            "ok" or "available" => "",
            "stale" => "Stale",
            "error" => "Update failed",
            "unavailable" => "Unavailable",
            "refreshing" => "Refreshing…",
            "disabled" => "Disabled",
            _ => "Not reported"
        };
    }

    private static Grid BudgetWindowCells(params Control[] cells)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("0.85*,1.6*,0.9*"), ColumnSpacing = 14, RowSpacing = 5 };
        for (var i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); grid.Children.Add(cells[i]); }
        bool? stacked = null;
        grid.SizeChanged += (_, _) =>
        {
            if (grid.Bounds.Width <= 0) return;
            var narrow = grid.Bounds.Width < 500;
            if (stacked == narrow) return;
            stacked = narrow;
            // The selected 340-px inventory keeps its neighboring inspector.
            // Give numbers and the long bar a whole row instead of shortening
            // the bar to an inline indicator or clipping reset/scope facts.
            grid.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "0.85*,1.6*,0.9*");
            grid.RowDefinitions = new RowDefinitions(narrow ? "Auto,Auto,Auto" : "Auto");
            for (var i = 0; i < cells.Length; i++)
            {
                Grid.SetColumn(cells[i], narrow ? 0 : i);
                Grid.SetRow(cells[i], narrow ? i : 0);
            }
        };
        return grid;
    }

    private static Control MonitorBudgetTableHeader()
    {
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("0.85*,1.6*,0.9*"), ColumnSpacing = 14 };
        columns.Children.Add(Ui.Text("Window", 11, Ui.Muted));
        var consumption = Ui.Text("Consumption / remaining", 11, Ui.Muted);
        Grid.SetColumn(consumption, 1); columns.Children.Add(consumption);
        var reset = Ui.Text("Reset · local time", 11, Ui.Muted);
        Grid.SetColumn(reset, 2); columns.Children.Add(reset);
        var narrow = Ui.Text("Quota windows · consumption / remaining · reset", 11, Ui.Muted);
        var header = new Grid { Children = { columns, narrow } };
        header.SizeChanged += (_, _) =>
        {
            var stacked = header.Bounds.Width < 500;
            columns.IsVisible = !stacked; narrow.IsVisible = stacked;
        };
        AutomationProperties.SetName(header, "Monitor quota table columns");
        return new Border { Padding = new Thickness(10, 6), Background = Ui.Surface,
            BorderBrush = Ui.Edge, BorderThickness = new Thickness(0, 0, 0, 1), Child = header };
    }

    private static Grid BudgetAccountCells(Control identity, Control windows)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("1.15*,3.35*"), ColumnSpacing = 16 };
        grid.Children.Add(identity); Grid.SetColumn(windows, 1); grid.Children.Add(windows);
        return grid;
    }

    private static Control BudgetTableHeader() => new Border
    {
        Background = Ui.Surface, Padding = new Thickness(12, 7), BorderBrush = Ui.Edge,
        BorderThickness = new Thickness(0, 0, 0, 1),
        Child = BudgetAccountCells(Ui.Text("Account", 11, Ui.Muted),
            BudgetWindowCells(Ui.Text("Budget window", 11, Ui.Muted), Ui.Text("Consumption", 11, Ui.Muted), Ui.Text("Resets · local time", 11, Ui.Muted)))
    };

    private Control BudgetIdentity(AccountDto account)
    {
        var name = AccountCell(AccountDisplayName(account), 13);
        name.FontWeight = FontWeight.Medium;
        ToolTip.SetTip(name, account.Name);
        var provider = ProviderName(account);
        var detail = provider + (account.Enabled ? "" : " · Disabled");
        var panel = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(name);
        panel.Children.Add(AccountCell(detail, 11, Ui.Muted));
        return panel;
    }

    private Control BudgetWindowRow(AccountDto account, QuotaDto? quota, string? context = null)
    {
        var unavailable = QuotaUnavailable(quota) || IsMiMo(account) && quota?.Source == "unavailable";
        var label = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(Ui.Text(QuotaWindowLabel(quota), 12));
        var state = unavailable ? "" : QuotaStateLabel(quota);
        if (!string.IsNullOrWhiteSpace(state))
            label.Children.Add(Ui.Text(state, 11, state is "Last known" or "Stale" or "Update failed" or "Unavailable" ? Ui.Warning : Ui.Muted));
        else if (context is not null) label.Children.Add(Ui.Text(context, 11, Ui.Muted));

        var consumption = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var percent = unavailable ? null : QuotaUsedPercent(quota);
        if (quota?.MeasurementKind == "rate")
        {
            consumption.Children.Add(Ui.Text(quota.Used is { } rate && double.IsFinite(rate)
                ? BudgetAmount(rate, quota.Unit) + " observed" : "Rate not supplied", 12));
        }
        else if (percent is { } usedPercent && quota is not null)
        {
            var knownRemaining = ReportedRemaining(quota);
            var remaining = knownRemaining is { } amount
                ? BudgetAmount(amount, quota.Unit) + " left"
                : $"{BudgetPercent(100 - usedPercent)}% left";
            var values = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
            var used = Ui.Text($"{BudgetPercent(usedPercent)}% used", 12);
            used.FontWeight = FontWeight.Medium;
            values.Children.Add(used);
            var left = Ui.Text(remaining, 11, Ui.Muted);
            left.HorizontalAlignment = HorizontalAlignment.Right;
            left.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(left, 1); values.Children.Add(left);
            consumption.Children.Add(values);
            var bar = new ProgressBar
            {
                Minimum = 0, Maximum = 100, Value = usedPercent, Height = 6,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Foreground = LastKnownQuota(quota) ? Ui.Warning : usedPercent >= 95 ? Ui.Danger : usedPercent >= 80 ? Ui.Warning : Ui.Accent,
                Background = Ui.Edge
            };
            AutomationProperties.SetName(bar, $"{account.Name} · {QuotaWindowLabel(quota)} · consumed");
            AutomationProperties.SetHelpText(bar, $"{BudgetPercent(usedPercent)}% used. {remaining}. {state}");
            consumption.Children.Add(bar);
            if (!PercentUnit(quota.Unit) && ValidAmount(quota.Used) is { } amountUsed && ValidAmount(quota.Limit) is { } limit)
                consumption.Children.Add(Ui.Text($"{BudgetNumber(amountUsed)} of {BudgetAmount(limit, quota.Unit)} used", 11, Ui.Muted));
        }
        else if (!unavailable && quota is not null && (ReportedRemaining(quota).HasValue || ValidAmount(quota.Used).HasValue || ValidAmount(quota.Limit).HasValue))
        {
            if (ReportedRemaining(quota) is { } remaining)
                consumption.Children.Add(Ui.Text(BudgetAmount(remaining, quota.Unit) + " left", 12));
            if (ValidAmount(quota.Used) is { } used)
                consumption.Children.Add(Ui.Text(ValidAmount(quota.Limit) is { } limit
                    ? $"{BudgetNumber(used)} of {BudgetAmount(limit, quota.Unit)} used" : BudgetAmount(used, quota.Unit) + " used", 11, Ui.Muted));
            else if (ValidAmount(quota.Limit) is { } limit)
                consumption.Children.Add(Ui.Text(BudgetAmount(limit, quota.Unit) + " limit", 11, Ui.Muted));

        }
        else
        {
            consumption.Children.Add(Ui.Text(unavailable ? QuotaSupportMessage(quota) : quota is null
                ? "No quota snapshot reported · refresh this account"
                : quota.State is "error" or "unavailable" ? "Could not load budget" : "Usage not supplied", 12, Ui.Muted));
            consumption.Children.Add(QuotaAccessActions(account, quota));
        }

        if (account.SharedWithLegacy || quota?.SharedWithLegacy == true)
            consumption.Children.Add(Ui.Text("Shared provider budget · do not add balances", 11, Ui.Muted));
        else if (account.BudgetPoolId is not null || quota?.BudgetPoolId is not null)
            consumption.Children.Add(Ui.Text("Provider budget pool · do not add balances", 11, Ui.Muted));
        if (quota is not null && new[] { quota.Scope, quota.ScopeId, quota.Model }.Any(value => !string.IsNullOrWhiteSpace(value)))
            consumption.Children.Add(Ui.Text("Scope: " + string.Join(" · ", new[] { quota.Scope, quota.ScopeId, quota.Model }
                .Where(value => !string.IsNullOrWhiteSpace(value))), 11, Ui.Muted));
        if (!string.IsNullOrWhiteSpace(quota?.Error)) consumption.Children.Add(Ui.Text(quota.Error, 11, Ui.Warning));

        var reset = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        if (quota?.MeasurementKind == "rate")
            reset.Children.Add(Ui.Text("Sample " + Ui.When(quota.DataAsOf ?? quota.ObservedAt), 11, Ui.Muted));
        else if (!unavailable && quota?.ResetsAt is { } resetsAt)
        {
            reset.Children.Add(Ui.Text(resetsAt.ToLocalTime().ToString("dd MMM · HH:mm", CultureInfo.CurrentCulture), 12, Ui.Muted));
            if (resetsAt <= DateTimeOffset.UtcNow) reset.Children.Add(Ui.Text("Reset due · refresh to check", 11, Ui.Warning));
        }
        else reset.Children.Add(Ui.Text(unavailable ? "—" : "Not supplied", 11, Ui.Muted));
        if (!unavailable && quota?.LastSuccessAt is { } lastSuccess)
        {
            var localSuccess = lastSuccess.ToLocalTime();
            var format = localSuccess.Date == DateTimeOffset.Now.Date ? "HH:mm" : "dd MMM · HH:mm";
            var checkedAt = Ui.Text("Checked " + localSuccess.ToString(format, CultureInfo.CurrentCulture), 11, Ui.Muted);
            ToolTip.SetTip(checkedAt, $"Last successful quota update: {localSuccess:yyyy-MM-dd HH:mm:ss zzz}");
            reset.Children.Add(checkedAt);
        }
        var row = BudgetWindowCells(label, consumption, reset);
        row.MinHeight = 38;
        AutomationProperties.SetName(row, $"Monitor quota window for {account.Name} · {QuotaWindowLabel(quota)}");
        return row;
    }

    private string BudgetSnapshotNote() => !_hostAvailable ? "Host offline · showing last known budgets."
        : _quotaError is not null ? "Quota refresh unavailable · showing last known budgets."
        : "Each window is separate. Shared provider budgets are not added together.";
}
