using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Lain.Core;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    // Provider diagnostics remain available without competing with the budget
    // windows and account controls in the primary view.
    private static Control QuotaDetails(QuotaDto? quota)
    {
        if (quota is null) return Ui.Text("No quota snapshot available.", 11, Ui.Muted);
        var panel = new StackPanel { Spacing = 4 };
        var title = Ui.Text(QuotaWindowLabel(quota), 12);
        title.FontWeight = FontWeight.SemiBold;
        panel.Children.Add(title);
        panel.Children.Add(QuotaTechnicalField("Source", string.IsNullOrWhiteSpace(quota.Source) ? "Not supplied" : quota.Source));
        panel.Children.Add(QuotaTechnicalField("Snapshot", $"{quota.State} · observed {Ui.When(quota.ObservedAt)} · last success {Ui.When(quota.LastSuccessAt)}"));
        string Amount(double? value) => ValidAmount(value) is { } amount ? BudgetAmount(amount, quota.Unit) : "Not supplied";
        panel.Children.Add(QuotaTechnicalField("Amounts", $"Used: {Amount(quota.Used)} · limit: {Amount(quota.Limit)} · remaining: {Amount(quota.Remaining)}"));
        panel.Children.Add(QuotaTechnicalField("Consumption", QuotaUsedPercent(quota) is { } percent ? $"{BudgetNumber(percent)}% used" : "Percentage not available"));
        panel.Children.Add(QuotaTechnicalField("Reset", QuotaResetText(quota, QuotaUnavailable(quota))));
        if (!string.IsNullOrWhiteSpace(quota.BudgetPoolId)) panel.Children.Add(QuotaTechnicalField("Window pool", quota.BudgetPoolId));
        if (quota.SharedWithLegacy) panel.Children.Add(Ui.Text("Shared with the imported connection; this is the same provider budget.", 11, Ui.Muted));
        if (!string.IsNullOrWhiteSpace(quota.Error)) panel.Children.Add(Ui.Text(quota.Error, 11, QuotaUnavailable(quota) ? Ui.Muted : Ui.Warning));
        return new Border { BorderBrush = Ui.Edge, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 8, 0, 0), Child = panel };
    }

    private static Control QuotaTechnicalField(string label, string value)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("92,*"), ColumnSpacing = 12 };
        row.Children.Add(Ui.Text(label, 11, Ui.Muted));
        var text = new SelectableTextBlock { Text = value, FontSize = 11, Foreground = Ui.Muted, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(text, 1); row.Children.Add(text);
        return row;
    }
}
