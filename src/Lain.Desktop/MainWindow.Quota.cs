using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Lain.Core;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    private static Control QuotaDetails(QuotaDto? quota)
    {
        if (quota is null)
            return Ui.Text("Quota unknown · no snapshot available", 13, Ui.Muted);

        static string Amount(double? value, string unit)
        {
            if (value is not { } amount || !double.IsFinite(amount)) return "Unknown";
            var suffix = string.IsNullOrWhiteSpace(unit) || unit == "unknown" ? "" : $" {unit}";
            return amount.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture) + suffix;
        }

        static Control Metric(string label, string value)
        {
            var content = new StackPanel { Spacing = 3 };
            content.Children.Add(Ui.Text(label, 12, Ui.Muted));
            content.Children.Add(Ui.Text(value, 13));
            return content;
        }

        var panel = new StackPanel { Spacing = 10 };
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        heading.Children.Add(Ui.Text("Quota", 13));
        var state = Ui.Text(string.IsNullOrWhiteSpace(quota.State) ? "unknown" : quota.State, 12,
            quota.State == "ok" ? Ui.Accent : Ui.Muted);
        Grid.SetColumn(state, 1);
        heading.Children.Add(state);
        panel.Children.Add(heading);

        var amounts = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") };
        var metrics = new[]
        {
            Metric("Remaining", Amount(quota.Remaining, quota.Unit)),
            Metric("Used", Amount(quota.Used, quota.Unit)),
            Metric("Limit", Amount(quota.Limit, quota.Unit))
        };
        for (var i = 0; i < metrics.Length; i++)
        {
            Grid.SetColumn(metrics[i], i);
            metrics[i].Margin = new Thickness(0, 0, i < 2 ? 12 : 0, 0);
            amounts.Children.Add(metrics[i]);
        }
        panel.Children.Add(amounts);

        // Missing or inconsistent quantities must never look like an empty budget.
        var quantity = quota.Used ?? quota.Remaining;
        if (quota.Limit is { } limit && double.IsFinite(limit) && limit > 0
            && quantity is { } value && double.IsFinite(value) && value >= 0 && value <= limit)
        {
            panel.Children.Add(Ui.Text(quota.Used.HasValue ? "Used share of limit" : "Remaining share of limit", 12, Ui.Muted));
            panel.Children.Add(new ProgressBar
            {
                Minimum = 0, Maximum = limit, Value = value, Height = 4,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Foreground = Ui.Accent, Background = Ui.Edge
            });
        }

        var metadata = new StackPanel { Spacing = 4 };
        metadata.Children.Add(Ui.Text($"Resets {Ui.When(quota.ResetsAt)}", 12, Ui.Muted));
        metadata.Children.Add(Ui.Text($"Source · {(string.IsNullOrWhiteSpace(quota.Source) ? "Unknown" : quota.Source)}", 12, Ui.Muted));
        metadata.Children.Add(Ui.Text($"Observed {Ui.When(quota.ObservedAt)} · last success {Ui.When(quota.LastSuccessAt)}", 12, Ui.Muted));
        if (!string.IsNullOrWhiteSpace(quota.BudgetPoolId))
            metadata.Children.Add(Ui.Text($"Budget pool · {quota.BudgetPoolId}", 12, Ui.Muted));
        if (quota.SharedWithLegacy)
            metadata.Children.Add(Ui.Text("Shared with imported account · the same provider budget", 12, Ui.Muted));
        panel.Children.Add(metadata);

        if (!string.IsNullOrWhiteSpace(quota.Error))
            panel.Children.Add(Ui.Text($"Quota update · {quota.Error}", 13, Ui.Muted));

        return new Border
        {
            BorderBrush = Ui.Edge, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(12), Child = panel
        };
    }

}
