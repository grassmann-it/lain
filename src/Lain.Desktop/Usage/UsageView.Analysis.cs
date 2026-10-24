using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Lain.Core;
using Lain.Desktop.Presentation;

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
    private readonly Expander _moreFilters = new() { Header = "More filters", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly Expander _performanceDetails = new() { Header = "Performance · whole filtered range", IsExpanded = true, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel _performance = new() { Spacing = 8 };
    private readonly StackPanel _dimensionGroups = new() { Spacing = 0 };
    private readonly StackPanel _unobservedAccounts = new() { Spacing = 5 };
    private readonly TextBlock _analysisStatus = Label("Waiting for complete-range analysis", Muted);
    private readonly TextBlock _analysisNotice = Label("Waiting for breakdown", Muted);
    private readonly HashSet<string> _configuredAccounts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _expandedAnalysisGroups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _analysisButtons = new(StringComparer.Ordinal);
    private UsageAnalysisDto? _analysis;
    private UsageQuery? _analysisQuery;
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
        _moreFilters.Content = filters;
        return _moreFilters;
    }

    private Control BuildAnalysisSection()
    {
        _dimension.ItemsSource = DimensionChoices.Select(x => new FilterChoice(x.Key, x.Name)).ToArray();
        _dimension.SelectedItem = _dimension.ItemsSource!.OfType<FilterChoice>().Single(choice => choice.Key == "accountId");
        AutomationProperties.SetName(_dimension, "Compare by");
        AutomationProperties.SetName(_dimensionOrder, "Group order");
        AutomationProperties.SetName(_analysisStatus, "Usage analysis status");
        AutomationProperties.SetName(_performance, "Usage performance");
        AutomationProperties.SetName(_performanceDetails, "Performance details");
        _dimension.SelectionChanged += async (_, _) =>
        {
            if (_restoringNavigation || _lifetime is null) return;
            _query = _query with { GroupBy = (_dimension.SelectedItem as FilterChoice)?.Key };
            await RefreshAsync(_lifetime.Token);
        };
        _dimensionOrder.SelectionChanged += (_, _) => { if (!_restoringNavigation && _analysis is not null) RenderAnalysis(_analysis, _analysisError); };
        var controls = new WrapPanel { Children = { Field("Compare by", _dimension), Field("Group order", _dimensionOrder) } };
        _performanceDetails.Content = new StackPanel { Spacing = 6, Children = { _performance,
            Label("Output rate is reported output tokens ÷ full attempt duration, including response startup. P50/P95 use all matching stored measurements; each metric reports measured/eligible attempts. Missing measurements stay —. Costs are not reported by this source.", Muted) } };
        _dimensionGroups.MinWidth = 640;
        return UsageSection("Breakdown", "Completion errors · expand a row for measurements and context", new StackPanel
        {
            Spacing = 6, Children = { controls, _analysisNotice,
                new ScrollViewer { Content = _dimensionGroups, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled },
                _unobservedAccounts }
        });
    }

    private UsageQuery ReadDimensionFilters(UsageQuery query)
    {
        string? Value(string key) => _dimensionFilters.TryGetValue(key, out var field) && !string.IsNullOrWhiteSpace(field.Text) ? field.Text.Trim() : null;
        return query with { RouteId = Value("routeId"), ModelFamily = Value("modelFamily"), Manufacturer = Value("manufacturer"),
            InferenceProvider = Value("inferenceProvider"), Gateway = Value("gateway"), BillingAccountId = Value("billingAccountId"),
            ReasoningEffort = Value("reasoningEffort"), EffectiveReasoningEffort = Value("effectiveReasoningEffort"), Mode = Value("mode"), ConversationId = Value("conversationId"), TurnId = Value("turnId"),
            WorkerId = Value("workerId"), Harness = Value("harness"), ProfileId = Value("profileId"),
            GroupBy = (_dimension.SelectedItem as FilterChoice)?.Key ?? "accountId", GroupLimit = 100 };
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

    private void RenderAnalysis(UsageAnalysisDto? analysis, string? error, UsageQuery? snapshotQuery = null)
    {
        _analysisError = error;
        _analysisNotice.IsVisible = analysis is null || error is not null || !analysis.Complete;
        _analysisNotice.Text = analysis is null || error is not null ? "Analysis unavailable · details under Source & freshness" : "Analysis journal coverage incomplete · groups may omit activity";
        _analysisNotice.Foreground = Ui.Warning;
        if (analysis is null)
        {
            // A retained snapshot is only usable for the same complete filter/group scope.
            // Otherwise the summary's current filter labels would mislabel old group metrics.
            var scope = (snapshotQuery ?? _query) with { Offset = 0 };
            if (_analysisQuery != scope)
            {
                _analysis = null;
                _analysisQuery = null;
                _dimensionGroups.Children.Clear();
                _dimensionGroups.Children.Add(Label("No analysis snapshot for the applied filters.", Muted));
                _performance.Children.Clear();
                _performance.Children.Add(Label("No performance snapshot for the applied filters.", Muted));
            }
            _analysisStatus.Text = "Analysis unavailable" + (error is null ? "" : " · " + error) + (_analysis is null ? "" : " · showing the last analysis snapshot\n" + AnalysisSnapshotLabel(_analysis));
            _analysisStatus.Foreground = Ui.Warning;
            if (_analysis is not null) _analysisNotice.Text += " · showing the last known analysis";
            return;
        }
        _analysis = analysis;
        if (snapshotQuery is not null) _analysisQuery = snapshotQuery with { Offset = 0 };
        _analysisStatus.Text = AnalysisSnapshotLabel(analysis);
        if (error is not null) _analysisStatus.Text = "Analysis unavailable · " + error + " · showing the last analysis snapshot · " + _analysisStatus.Text;
        _analysisStatus.Foreground = analysis.Complete && error is null ? Muted : Ui.Warning;
        _performance.Children.Clear();
        _performanceDetails.Header = ResultScope(_analysisQuery ?? _query) + " · first output & timing";
        var metrics = new UniformGrid { Columns = _wideResults || Bounds.Width < 620 ? 1 : 3 };
        metrics.Children.Add(PerformanceMetric("First output", analysis.Performance.FirstOutputLatencyMs, Milliseconds));
        metrics.Children.Add(PerformanceMetric("Total duration", analysis.Performance.TotalDurationMs, Milliseconds));
        metrics.Children.Add(PerformanceMetric("Output / total time", analysis.Performance.OutputTokensPerSecond, TokenRate));
        _performance.Children.Add(metrics);

        var dimensionKey = _analysisQuery?.GroupBy ?? (_dimension.SelectedItem as FilterChoice)?.Key;
        var dimension = analysis.Dimensions.FirstOrDefault(x => x.Dimension == dimensionKey);
        var focusedName = _dimensionGroups.GetVisualDescendants().OfType<Control>().Where(x => x.IsFocused).Select(AutomationProperties.GetName).FirstOrDefault();
        _analysisButtons.Clear();
        _dimensionGroups.Children.Clear();
        if (dimension is null) { _dimensionGroups.Children.Add(Label("This grouping is not reported by the host.", Muted)); return; }
        if (dimension.Truncated) _dimensionGroups.Children.Add(Label("Only a limited set of groups was returned; ordering applies to those groups.", Ui.Warning));
        var columns = AnalysisCells("Group", "Requests", "Attempts", "Completion errors", "Reported tokens", "Unknown", true);
        // Match the native disclosure header's content inset and trailing chevron.
        columns.Margin = new Thickness(16, 0, 108, 0);
        _dimensionGroups.Children.Add(TableRow(columns, true));
        IEnumerable<UsageDimensionGroupDto> groups = _dimensionOrder.SelectedIndex switch
        {
            1 => dimension.Groups.OrderBy(x => x.Totals.Attempts),
            2 => dimension.Groups.OrderByDescending(x => x.Totals.Errors),
            _ => dimension.Groups.OrderByDescending(x => x.Totals.Attempts)
        };
        foreach (var group in groups)
        {
            var stateKey = dimension.Dimension + "\n" + (group.Key is null ? "null" : "key:" + group.Key);
            var name = group.Key is null ? "Unknown" : dimension.Dimension == "accountId" ? AccountName(group.Key) : group.Key;
            var header = AnalysisCells(name, Number(group.Totals.Requests), Number(group.Totals.Attempts), Errors(group.Totals), ReportedTokens(group.Totals), Number(group.Totals.UnknownUsageAttempts));
            var detail = new StackPanel { Spacing = 5, Children =
            {
                Label(group.Key is null ? "Unknown identity · no identity filter can be inferred" : $"{dimension.Dimension}: {group.Key}", Muted),
                Label(TokenDetails(group.Totals.Tokens), Muted),
                Label($"{group.Totals.Requests:N0} requests · {group.Totals.Attempts:N0} attempts · completion errors {group.Totals.Errors:N0} · {ErrorRate(group.Totals)} error rate · {group.Totals.UnknownUsageAttempts:N0} with unknown usage or total", Muted),
                Label(PerformanceDetails("First output", group.Performance.FirstOutputLatencyMs, Milliseconds), Muted),
                Label(PerformanceDetails("Total duration", group.Performance.TotalDurationMs, Milliseconds), Muted),
                Label(PerformanceDetails("Output / total time", group.Performance.OutputTokensPerSecond, TokenRate), Muted)
            } };
            if (group.Totals.Measurements is { } measurements)
                foreach (var (component, value) in Lain.Client.UsageDisplayFacts.Components(measurements))
                    detail.Children.Add(Label(Lain.Client.UsageDisplayFacts.Describe(component, value), Muted));
            Button? groupFilter = null;
            if (group.Key is not null && dimension.Dimension != "provider")
            {
                var filter = new Button { Content = "Filter to this group", HorizontalAlignment = HorizontalAlignment.Left };
                groupFilter = filter;
                var filterName = dimension.Dimension switch { "accountId" => $"Filter to account {group.Key}", "model" => $"Filter to model {group.Key}", _ => $"Filter {dimension.Dimension} {group.Key}" };
                AutomationProperties.SetName(filter, filterName);
                _analysisButtons[filterName] = filter;
                filter.Click += async (_, _) =>
                {
                    if (dimension.Dimension == "accountId") { await ApplyGroupAsync(group.Key, true); return; }
                    if (dimension.Dimension == "model") { await ApplyGroupAsync(group.Key, false); return; }
                    if (_dimensionFilters.ContainsKey(dimension.Dimension))
                        await ApplyDrilldownAsync(query => WithDimension(query, dimension.Dimension, group.Key));
                };
                detail.Children.Add(filter);
            }
            if (group.Key is not null)
            {
                WorkspaceNavigationRequest? target = dimension.Dimension switch
                {
                    "accountId" => new("Accounts", AccountId: group.Key),
                    "routeId" => new("Models", RouteId: group.Key),
                    "model" => new("Models", Model: group.Key),
                    _ => null
                };
                if (target is not null)
                {
                    var caption = dimension.Dimension == "accountId" ? "Open account" : "Open model";
                    var automationName = $"Open {dimension.Dimension} {group.Key}";
                    var link = NavigationButton(caption, automationName, target);
                    _analysisButtons[automationName] = link;
                    detail.Children.Add(link);
                }
            }
            // Attach the completed header once. Giving its cells to the Expander first
            // and then moving them into a Grid breaks Avalonia's logical-parent lifecycle.
            var expander = new Expander { Content = detail, IsExpanded = _expandedAnalysisGroups.Contains(stateKey),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            var commands = new List<ObjectCommand> { new("details", "Token and measurement details", () => expander.IsExpanded = true) };
            commands.AddRange(detail.Children.OfType<Button>().Select((button, index) => new ObjectCommand("action-" + index, button.Content?.ToString() ?? "Open context", () => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)))));
            expander.ContextMenu = ObjectCommands.Menu(commands);
            var menu = ObjectCommands.Menu(commands);
            var more = Ui.Button("⋯", () => menu.Open()); more.ContextMenu = menu;
            AutomationProperties.SetName(more, $"Actions for usage group {name}");
            var groupHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            if (groupFilter is { } filterAction)
            {
                var drill = new Button { Content = "→", Padding = new Thickness(7, 2), MinWidth = 28 };
                AutomationProperties.SetName(drill, $"Drill into {dimension.Dimension} {group.Key}");
                ToolTip.SetTip(drill, "Filter the applied result to this group; preserve unsubmitted drafts");
                drill.Click += (_, _) => filterAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                _analysisButtons[AutomationProperties.GetName(drill)!] = drill;
                actions.Children.Add(drill);
            }
            actions.Children.Add(more);
            groupHeader.Children.Add(header); Grid.SetColumn(actions, 1); groupHeader.Children.Add(actions);
            expander.Header = groupHeader;
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
        if (focusedName is not null && _analysisButtons.TryGetValue(focusedName, out var focusedButton)) focusedButton.Focus();
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
    private static string AnalysisSnapshotLabel(UsageAnalysisDto analysis)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(analysis.TimeZone);
        return $"{TimeZoneInfo.ConvertTime(analysis.From, zone):dd MMM HH:mm} → {TimeZoneInfo.ConvertTime(analysis.To, zone):dd MMM HH:mm} · {(analysis.Complete ? "Complete journal coverage" : "Incomplete journal coverage")} · observed {TimeZoneInfo.ConvertTime(analysis.ObservedAt, zone):HH:mm:ss} · cursor {analysis.Cursor:N0} · {analysis.TimeZone}\nCompletion errors: {Errors(analysis.Totals)} · known completion state preferred, historical state/status used when unknown";
    }
    private static string PerformanceDetails(string name, MetricStatisticsDto metric, Func<double?, string> format) =>
        $"{name}: P50 {format(metric.P50)} · P95 {format(metric.P95)} · {metric.Samples:N0} of {metric.Eligible:N0} eligible measured";
    private static Control PerformanceMetric(string name, MetricStatisticsDto metric, Func<double?, string> format) => new Border
    {
        Padding = new Thickness(8), BorderBrush = Ui.Edge, BorderThickness = new Thickness(0, 0, 1, 0),
        Child = new StackPanel { Spacing = 4, Children = { Label(name, Muted),
            new TextBlock { Text = "P50 " + format(metric.P50), FontSize = 18, Foreground = Ui.Ink, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            Label($"P95 {format(metric.P95)}", Muted), Label($"{metric.Samples:N0} / {metric.Eligible:N0} measured", Muted) } }
    };
    private Grid AnalysisCells(string name, string requests, string attempts, string errors, string tokens, string unknown, bool header = false)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("1.8*,.9*,.9*,1.3*,1.2*,.85*"), MinHeight = RowHeight, Classes = { "usage-row" } };
        var values = new[] { name, requests, attempts, errors, tokens, unknown };
        for (var i = 0; i < values.Length; i++)
        {
            var cell = Cell(values[i], header ? Muted : Ui.Ink);
            if (header) { cell.TextWrapping = Avalonia.Media.TextWrapping.Wrap; cell.TextTrimming = Avalonia.Media.TextTrimming.None; cell.FontSize = 11; }
            if (header && i == 3) ToolTip.SetTip(cell, "Errors prefer known completion state, then historical state/status. Percentage uses all matching attempts. This basis can differ from the whole-window cards and daily totals.");
            if (header && i == 5) ToolTip.SetTip(cell, "Attempts with unknown usage or total. Individual token fields can also be unknown.");
            if (i > 0) cell.HorizontalAlignment = HorizontalAlignment.Right;
            Put(row, cell, i);
        }
        return row;
    }
}
