using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Lain.Core;

namespace Lain.Desktop.Usage;

public sealed partial class UsageView
{
    private static readonly (string Key, string Name)[] DimensionChoices =
    [
        ("modelFamily", "Model family"), ("routeId", "Route"), ("manufacturer", "Manufacturer"),
        ("inferenceProvider", "Inference provider"), ("gateway", "Gateway"), ("billingAccountId", "Billing account"),
        ("accountId", "Account"), ("model", "Actual model"), ("provider", "Provider"),
        ("reasoningEffort", "Requested reasoning effort"), ("effectiveReasoningEffort", "Effective reasoning effort"), ("mode", "Mode"), ("conversationId", "Conversation"),
        ("turnId", "Turn"), ("workerId", "Worker"), ("harness", "Harness"), ("profileId", "Profile")
    ];
    private readonly ComboBox _dimension = new() { Width = 190 };
    private readonly ComboBox _dimensionOrder = new() { Width = 154, ItemsSource = new[] { "Most attempts", "Least attempts", "Most errors" }, SelectedIndex = 0 };
    private readonly Dictionary<string, TextBox> _dimensionFilters = new(StringComparer.Ordinal);
    private readonly StackPanel _performance = new() { Spacing = 8 };
    private readonly StackPanel _dimensionGroups = new() { Spacing = 0 };
    private readonly StackPanel _unobservedAccounts = new() { Spacing = 5 };
    private readonly TextBlock _analysisStatus = Label("Waiting for complete-range analysis", Muted);
    private readonly HashSet<string> _configuredAccounts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _expandedAnalysisGroups = new(StringComparer.Ordinal);
    private UsageAnalysisDto? _analysis;
    private string? _analysisError;

    private Control BuildDimensionFilters()
    {
        var filters = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (key, name) in DimensionChoices.Where(x => x.Key is not ("accountId" or "model" or "provider")))
        {
            var input = new TextBox { Width = 160, PlaceholderText = "Any" };
            _dimensionFilters[key] = input;
            filters.Children.Add(Field(name, input));
        }
        return new Expander { Header = "More filters", HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = filters };
    }

    private Control BuildAnalysisSection()
    {
        _dimension.ItemsSource = DimensionChoices.Select(x => new FilterChoice(x.Key, x.Name)).ToArray();
        _dimension.SelectedIndex = 0;
        AutomationProperties.SetName(_dimension, "Compare by");
        AutomationProperties.SetName(_dimensionOrder, "Group order");
        AutomationProperties.SetName(_analysisStatus, "Usage analysis status");
        AutomationProperties.SetName(_performance, "Usage performance");
        _dimension.SelectionChanged += async (_, _) =>
        {
            if (_lifetime is null) return;
            _query = _query with { GroupBy = (_dimension.SelectedItem as FilterChoice)?.Key };
            await RefreshAsync(_lifetime.Token);
        };
        _dimensionOrder.SelectionChanged += (_, _) => { if (_analysis is not null) RenderAnalysis(_analysis, _analysisError); };
        var controls = new WrapPanel { Children = { Field("Compare by", _dimension), Field("Group order", _dimensionOrder) } };
        return Section("Usage & performance", "Whole filtered range · measured values and sample counts", new StackPanel
        {
            Spacing = 9, Children = { controls, _analysisStatus, _performance, _dimensionGroups, _unobservedAccounts,
                Label("Output rate is reported output tokens ÷ full attempt duration, including response startup. P50/P95 use all matching stored measurements; missing measurements stay —. Costs are not reported by this source.", Muted) }
        });
    }

    private UsageQuery ReadDimensionFilters(UsageQuery query)
    {
        string? Value(string key) => _dimensionFilters.TryGetValue(key, out var field) && !string.IsNullOrWhiteSpace(field.Text) ? field.Text.Trim() : null;
        return query with { RouteId = Value("routeId"), ModelFamily = Value("modelFamily"), Manufacturer = Value("manufacturer"),
            InferenceProvider = Value("inferenceProvider"), Gateway = Value("gateway"), BillingAccountId = Value("billingAccountId"),
            ReasoningEffort = Value("reasoningEffort"), EffectiveReasoningEffort = Value("effectiveReasoningEffort"), Mode = Value("mode"), ConversationId = Value("conversationId"), TurnId = Value("turnId"),
            WorkerId = Value("workerId"), Harness = Value("harness"), ProfileId = Value("profileId"),
            GroupBy = (_dimension.SelectedItem as FilterChoice)?.Key ?? "modelFamily", GroupLimit = 100 };
    }

    private static string DimensionScope(UsageQuery query)
    {
        var values = new (string Name, string? Value)[]
        {
            ("route", query.RouteId), ("family", query.ModelFamily), ("manufacturer", query.Manufacturer),
            ("inference provider", query.InferenceProvider), ("gateway", query.Gateway), ("billing account", query.BillingAccountId),
            ("requested reasoning", query.ReasoningEffort), ("effective reasoning", query.EffectiveReasoningEffort), ("mode", query.Mode), ("conversation", query.ConversationId),
            ("turn", query.TurnId), ("worker", query.WorkerId), ("harness", query.Harness), ("profile", query.ProfileId)
        };
        return string.Concat(values.Where(x => x.Value is not null).Select(x => $" · {x.Name}: {x.Value}"));
    }

    private async Task<(UsageAnalysisDto? Value, string? Error)> ReadAnalysisAsync(UsageQuery query, CancellationToken token)
    {
        try
        {
            var value = await _client.UsageAnalysisAsync(query, token);
            if (value.ContractVersion != Api.Version) throw new InvalidOperationException("The host returned an unsupported analysis contract.");
            return (value, null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) { return (null, error.Message); }
    }

    private void RenderAnalysis(UsageAnalysisDto? analysis, string? error)
    {
        _analysisError = error;
        if (analysis is null)
        {
            _analysisStatus.Text = "Analysis unavailable" + (error is null ? "" : " · " + error) + (_analysis is null ? "" : " · showing the last analysis snapshot");
            _analysisStatus.Foreground = Ui.Warning;
            return;
        }
        _analysis = analysis;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(analysis.TimeZone);
        _analysisStatus.Text = $"{TimeZoneInfo.ConvertTime(analysis.From, zone):dd MMM HH:mm} → {TimeZoneInfo.ConvertTime(analysis.To, zone):dd MMM HH:mm} · {(analysis.Complete ? "Complete coverage" : "Incomplete coverage")} · observed {TimeZoneInfo.ConvertTime(analysis.ObservedAt, zone):HH:mm:ss} · {analysis.TimeZone}";
        if (error is not null) _analysisStatus.Text = "Analysis unavailable · " + error + " · showing the last analysis snapshot · " + _analysisStatus.Text;
        _analysisStatus.Foreground = analysis.Complete && error is null ? Muted : Ui.Warning;
        _performance.Children.Clear();
        var metrics = new UniformGrid { Columns = 3 };
        metrics.Children.Add(PerformanceMetric("First output", analysis.Performance.FirstOutputLatencyMs, Milliseconds));
        metrics.Children.Add(PerformanceMetric("Total duration", analysis.Performance.TotalDurationMs, Milliseconds));
        metrics.Children.Add(PerformanceMetric("Output / total time", analysis.Performance.OutputTokensPerSecond, TokenRate));
        _performance.Children.Add(metrics);

        var dimensionKey = (_dimension.SelectedItem as FilterChoice)?.Key;
        var dimension = analysis.Dimensions.FirstOrDefault(x => x.Dimension == dimensionKey);
        var focusedName = _dimensionGroups.GetVisualDescendants().OfType<Control>().Where(x => x.IsFocused).Select(AutomationProperties.GetName).FirstOrDefault();
        _dimensionGroups.Children.Clear();
        if (dimension is null) { _dimensionGroups.Children.Add(Label("This grouping is not reported by the host.", Muted)); return; }
        if (dimension.Truncated) _dimensionGroups.Children.Add(Label("Only a limited set of groups was returned; ordering applies to those groups.", Ui.Warning));
        _dimensionGroups.Children.Add(TableRow(AnalysisCells("Group", "Attempts", "Tokens", "P50 first", "P50 total", "P50 output/s", true), true));
        IEnumerable<UsageDimensionGroupDto> groups = _dimensionOrder.SelectedIndex switch
        {
            1 => dimension.Groups.OrderBy(x => x.Totals.Attempts),
            2 => dimension.Groups.OrderByDescending(x => x.Totals.Errors),
            _ => dimension.Groups.OrderByDescending(x => x.Totals.Attempts)
        };
        foreach (var group in groups)
        {
            var stateKey = dimension.Dimension + "\n" + (group.Key ?? "<unknown>");
            var name = group.Key is null ? "Unknown" : dimension.Dimension is "accountId" or "billingAccountId" ? AccountName(group.Key) : group.Key;
            var header = AnalysisCells(name, Number(group.Totals.Attempts), Number(group.Totals.Tokens.Total),
                Milliseconds(group.Performance.FirstOutputLatencyMs.P50), Milliseconds(group.Performance.TotalDurationMs.P50), TokenRate(group.Performance.OutputTokensPerSecond.P50));
            var detail = new StackPanel { Spacing = 5, Children =
            {
                Label(TokenDetails(group.Totals.Tokens), Muted),
                Label($"{ErrorRate(group.Totals)} error rate · {group.Totals.Errors:N0} of {group.Totals.Attempts:N0} attempts · {group.Totals.UnknownUsageAttempts:N0} with unknown usage", Muted),
                Label(PerformanceDetails("First output", group.Performance.FirstOutputLatencyMs, Milliseconds), Muted),
                Label(PerformanceDetails("Total duration", group.Performance.TotalDurationMs, Milliseconds), Muted),
                Label(PerformanceDetails("Output / total time", group.Performance.OutputTokensPerSecond, TokenRate), Muted)
            } };
            if (group.Key is not null && dimension.Dimension != "provider")
            {
                var filter = new Button { Content = "Filter to this group" };
                AutomationProperties.SetName(filter, $"Filter {dimension.Dimension} {group.Key}");
                filter.Click += async (_, _) =>
                {
                    if (dimension.Dimension == "accountId") { await ApplyGroupAsync(group.Key, true); return; }
                    if (dimension.Dimension == "model") { await ApplyGroupAsync(group.Key, false); return; }
                    if (_dimensionFilters.TryGetValue(dimension.Dimension, out var input)) { input.Text = group.Key; await ApplyAsync(); }
                };
                detail.Children.Add(filter);
            }
            var expander = new Expander { Header = header, Content = detail, IsExpanded = _expandedAnalysisGroups.Contains(stateKey),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            expander.Classes.Add("account-tile");
            AutomationProperties.SetName(expander, $"Analysis group {dimension.Dimension} {name}");
            expander.TemplateApplied += (_, args) =>
            {
                if (args.NameScope.Find<ToggleButton>("ExpanderHeader") is not { } toggle) return;
                var toggleName = $"Toggle analysis group {dimension.Dimension} {name}";
                AutomationProperties.SetName(toggle, toggleName);
                if (focusedName == toggleName) toggle.Focus();
            };
            expander.PropertyChanged += (_, args) => { if (args.Property == Expander.IsExpandedProperty) { if (expander.IsExpanded) _expandedAnalysisGroups.Add(stateKey); else _expandedAnalysisGroups.Remove(stateKey); } };
            _dimensionGroups.Children.Add(TableRow(expander));
        }
        if (dimension.Groups.Length == 0) _dimensionGroups.Children.Add(Label("No matching groups in this range.", Muted));
    }

    private void RenderUnobservedAccounts(UsageSummaryDto summary, UsageQuery query)
    {
        _unobservedAccounts.Children.Clear();
        var absent = _configuredAccounts.Where(id => (query.AccountId is null || id == query.AccountId) && !summary.Accounts.Any(group => group.Key == id)).ToArray();
        if (absent.Length == 0) return;
        _unobservedAccounts.Children.Add(Label("Configured accounts without matching observed activity" + (summary.Complete ? "" : " · coverage is incomplete"), Muted));
        var names = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var id in absent)
        {
            var button = new Button { Content = AccountName(id), Margin = new Thickness(0, 0, 6, 4) };
            AutomationProperties.SetName(button, $"Inspect inactive account {id}");
            button.Click += async (_, _) => await ApplyGroupAsync(id, true);
            names.Children.Add(button);
        }
        _unobservedAccounts.Children.Add(names);
    }

    private static string TokenRate(double? value) => value is { } number && double.IsFinite(number) && number >= 0 ? $"{number:N1} tok/s" : "—";
    private static string PerformanceDetails(string name, MetricStatisticsDto metric, Func<double?, string> format) =>
        $"{name}: P50 {format(metric.P50)} · P95 {format(metric.P95)} · {metric.Samples:N0} of {metric.Eligible:N0} eligible measured";
    private static Control PerformanceMetric(string name, MetricStatisticsDto metric, Func<double?, string> format) => new Border
    {
        Padding = new Thickness(8), BorderBrush = Ui.Edge, BorderThickness = new Thickness(0, 0, 1, 0),
        Child = new StackPanel { Spacing = 4, Children = { Label(name, Muted),
            new TextBlock { Text = "P50 " + format(metric.P50), FontSize = 18, Foreground = Ui.Ink, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            Label($"P95 {format(metric.P95)}", Muted), Label($"{metric.Samples:N0} / {metric.Eligible:N0} measured", Muted) } }
    };
    private static Grid AnalysisCells(string name, string attempts, string tokens, string first, string total, string rate, bool header = false)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,0.8*,1*,1.1*,1.1*,1.1*"), MinHeight = 32 };
        var values = new[] { name, attempts, tokens, first, total, rate };
        for (var i = 0; i < values.Length; i++)
        {
            var cell = Cell(values[i], header ? Muted : Ui.Ink);
            if (i > 0) cell.HorizontalAlignment = HorizontalAlignment.Right;
            Put(row, cell, i);
        }
        return row;
    }
}
