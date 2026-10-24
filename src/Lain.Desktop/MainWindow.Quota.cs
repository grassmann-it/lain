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
        if (quota.Support is { } support) panel.Children.Add(QuotaTechnicalField("Quota access", support.Message));
        if (quota.MeasurementKind is not null) panel.Children.Add(QuotaTechnicalField("Measurement", quota.MeasurementKind));
        if (quota.ApprovedLimit is { } approved) panel.Children.Add(QuotaTechnicalField("Approved limit", BudgetAmount(approved, quota.Unit)));
        if (quota.SourceUrl is { } sourceUrl && sourceUrl != quota.Source) panel.Children.Add(QuotaTechnicalField("Source URL", sourceUrl));
        if (quota.WindowStart is not null || quota.WindowEnd is not null) panel.Children.Add(QuotaTechnicalField("Window", Ui.When(quota.WindowStart) + " – " + Ui.When(quota.WindowEnd)));
        if (quota.Scope is not null) panel.Children.Add(QuotaTechnicalField("Scope", string.Join(" · ", new[] { quota.Scope, quota.ScopeId, quota.Model }.Where(value => !string.IsNullOrWhiteSpace(value)))));
        if (quota.DataAsOf is { } dataAsOf) panel.Children.Add(QuotaTechnicalField("Data as of", Ui.When(dataAsOf)));
        if (quota.ProviderUpdatedAt is { } updated) panel.Children.Add(QuotaTechnicalField("Provider update", Ui.When(updated)));
        panel.Children.Add(QuotaTechnicalField("Snapshot", $"{quota.State} · observed {Ui.When(quota.ObservedAt)} · last success {Ui.When(quota.LastSuccessAt)}"));
        string Amount(double? value) => ValidAmount(value) is { } amount ? BudgetAmount(amount, quota.Unit) : "Not supplied";
        panel.Children.Add(QuotaTechnicalField("Amounts", $"Used: {Amount(quota.Used)} · limit: {Amount(quota.Limit)} · remaining: {(ReportedRemaining(quota) is { } remaining ? BudgetAmount(remaining, quota.Unit) : "Not supplied")}"));
        panel.Children.Add(QuotaTechnicalField("Consumption", QuotaUsedPercent(quota) is { } percent ? $"{BudgetPercent(percent)}% used" : "Percentage not available"));
        panel.Children.Add(QuotaTechnicalField("Reset", QuotaResetText(quota, QuotaUnavailable(quota))));
        if (!string.IsNullOrWhiteSpace(quota.BudgetPoolId)) panel.Children.Add(QuotaTechnicalField("Window pool", quota.BudgetPoolId));
        if (quota.ExactUsed is not null || quota.ExactLimit is not null || quota.ExactRemaining is not null)
            panel.Children.Add(QuotaTechnicalField("Exact amounts", $"Used: {quota.ExactUsed ?? "Not supplied"} · limit: {quota.ExactLimit ?? "Not supplied"} · remaining: {quota.ExactRemaining ?? "Not supplied"} {quota.Unit}"));
        if (quota.RequestyKey is { } key)
            panel.Children.Add(QuotaTechnicalField("Key scope", $"{key.KeyId} · group {key.GroupId} · manage {key.ManagePermission} · completions {key.CompletionsPermission} · uncapped {key.IsUncapped} · expires {Ui.When(key.ExpiresAt)}"));
        foreach (var fact in QuotaPresentationFacts(quota)) panel.Children.Add(Ui.Text(fact, 11, Ui.Muted));
        if (quota.SharedWithLegacy) panel.Children.Add(Ui.Text("Shared with the imported connection; this is the same provider budget.", 11, Ui.Muted));
        if (!string.IsNullOrWhiteSpace(quota.Error)) panel.Children.Add(Ui.Text(quota.Error, 11, QuotaUnavailable(quota) ? Ui.Muted : Ui.Warning));
        return new Border { BorderBrush = Ui.Edge, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 8, 0, 0), Child = panel };
    }

    private static IEnumerable<string> QuotaPresentationFacts(QuotaDto quota) => Lain.Client.QuotaDisplayFacts.Describe(quota);

    private static Control QuotaTechnicalField(string label, string value)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("92,*"), ColumnSpacing = 12 };
        row.Children.Add(Ui.Text(label, 11, Ui.Muted));
        var text = new SelectableTextBlock { Text = value, FontSize = 11, Foreground = Ui.Muted, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(text, 1); row.Children.Add(text);
        return row;
    }
}
