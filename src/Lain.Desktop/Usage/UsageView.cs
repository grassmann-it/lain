using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Lain.Client;
using Lain.Core;
using Lain.Desktop.Presentation;

namespace Lain.Desktop.Usage;

/// <summary>Read-only projection of host aggregates. Collection continues in the host.</summary>
public sealed partial class UsageView : UserControl, IPresentationView
{
    private static IBrush Muted => Ui.Muted;
    private static IBrush Accent => Ui.Accent;
    private readonly LainApiClient _client;
    private readonly TextBox _from = new() { Width = 118, PlaceholderText = "yyyy-MM-dd" };
    private readonly TextBox _to = new() { Width = 118, PlaceholderText = "yyyy-MM-dd" };
    private readonly TextBox _zone = new() { Width = 172 };
    private readonly ComboBox _account = new() { Width = 156 };
    private readonly ComboBox _model = new() { Width = 156 };
    private readonly TextBox _status = new() { Width = 84, PlaceholderText = "Any" };
    private readonly TextBlock _connection = Label("Waiting for usage service", Muted);
    private readonly TextBlock _freshness = Label("No snapshot received", Muted);
    private readonly TextBlock _coverage = Label("Coverage unknown until a snapshot is received", Muted);
    private readonly TextBlock _scope = Label("No filters applied yet", Muted);
    private readonly TextBlock _error = Label("", Ui.Warning);
    private readonly TextBlock _filterError = Label("", Ui.Warning);
    private readonly UniformGrid _totals = new() { Columns = 4 };
    private readonly TextBlock _reportedTotal = Label("", Muted);
    private readonly StackPanel _reportedComponents = new() { Spacing = 3 };
    private readonly UniformGrid _tokenTotals = new() { Columns = 4 };
    private readonly Expander _tokenDetails = new() { Header = "Token details", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Expander _filterDetails = new() { Header = "Filters & range", IsExpanded = false, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly Expander _sourceDetails = new() { Header = "Source & freshness", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Expander _historyDetails = new() { Header = "Attempt history & errors", IsExpanded = true, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel _layout = new() { Spacing = 8 };
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Grid _resultsLayout = new() { ColumnDefinitions = new ColumnDefinitions("*"), RowDefinitions = new RowDefinitions("Auto,Auto") };
    private readonly StackPanel _primaryResults = new() { Spacing = 8 };
    private readonly StackPanel _secondaryResults = new() { Spacing = 8 };
    private readonly TextBlock _context = Label("Waiting for the applied range", Muted);
    private readonly TextBlock _draftState = Label("Draft filters differ from the shown results. Apply to use them.", Ui.Warning);
    private readonly TextBlock _dailyScope = Label("All accounts · applied range", Muted);
    private readonly Button _showFilters = new() { Content = "More filters" };
    private readonly Button _usageBack = new() { Content = "← Previous result", IsVisible = false };
    private readonly Stack<Action> _drilldownHistory = new();
    private Vector? _restoreOffset;
    private string? _restoreFocus;
    private bool _wideResults;
    private bool _journalContinuityUnknown;
    private readonly List<Border> _sections = [];
    private readonly Dictionary<Expander, bool> _detailOverrides = [];
    private bool _settingDetails;
    private PresentationOptions _presentation = new();
    private readonly HashSet<string> _expandedAttempts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _attemptToggles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _attemptLinks = new(StringComparer.Ordinal);
    private readonly Expander _dailyDetails = new() { Header = "Daily totals", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _dailyMetric = new() { Width = 152, ItemsSource = new[] { "Attempts", "Total tokens", "Output tokens" }, SelectedIndex = 1 };
    private readonly StackPanel _daily = new() { Spacing = 4 };
    private readonly StackPanel _attempts = new();
    private readonly TextBlock _page = Label("No attempts loaded", Muted);
    private readonly Button _previous = new() { Content = "← Previous", IsEnabled = false };
    private readonly Button _next = new() { Content = "Next →", IsEnabled = false };
    private readonly Button _apply = new() { Content = "Apply" };
    private CancellationTokenSource? _lifetime;
    private CancellationTokenSource? _refresh;
    private UsageQuery _query = new(Limit: 25);
    private readonly Dictionary<string, string> _accountNames = new();
    private readonly HashSet<string> _knownAccounts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _knownModels = new(StringComparer.Ordinal);
    private UsageSummaryDto? _summary;
    private bool _hasAppliedQuery;
    private bool _restoringNavigation;
    private long _generation;

    public Action<WorkspaceNavigationRequest>? NavigateRequested { get; set; }

    public UsageView(LainApiClient client)
    {
        _client = client;
        var today = DateTime.Today;
        _from.Text = today.AddDays(-6).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _to.Text = today.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _zone.Text = TimeZoneInfo.Local.Id;
        _account.ItemsSource = new[] { new FilterChoice(null, "All accounts") };
        _account.SelectedIndex = 0;
        _model.ItemsSource = new[] { new FilterChoice(null, "All models") };
        _model.SelectedIndex = 0;
        _error.IsVisible = false;
        _filterError.IsVisible = false;

        var filters = new WrapPanel { Orientation = Orientation.Horizontal };
        filters.Children.Add(Field("From · inclusive", _from));
        filters.Children.Add(Field("To · exclusive", _to));
        filters.Children.Add(Field("Time zone", _zone));

        filters.Children.Add(Field("HTTP status", _status));

        _apply.Classes.Add("primary");
        ToolTip.SetTip(_status, "Exact HTTP code, 100–599. Leave blank for all outcomes.");
        var ranges = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (caption, days) in new (string, int?)[] { ("Today", 1), ("Last 7 days", 7), ("Last 30 days", 30), ("This month", null) })
        {
            var button = new Button { Content = caption, Margin = new Thickness(0, 0, 6, 6) };
            ToolTip.SetTip(button, "Apply this calendar range in the selected time zone, together with the filters below.");
            button.Click += async (_, _) => await ApplyRangeAsync(days);
            ranges.Children.Add(button);
        }
        Avalonia.Automation.AutomationProperties.SetName(_dailyMetric, "Daily measure");
        var measureLabel = Label("Show", Muted);
        measureLabel.VerticalAlignment = VerticalAlignment.Center;
        var measure = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { measureLabel, _dailyMetric } };
        ToolTip.SetTip(_dailyMetric, "Daily chart uses full reported totals from the applied server scope; partial values remain unknown here.");

        var paging = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 8, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _previous, _next } };
        DockPanel.SetDock(buttons, Dock.Right);
        paging.Children.Add(buttons);
        _page.VerticalAlignment = VerticalAlignment.Center;
        paging.Children.Add(_page);
        RenderEmptyTotals();
        _daily.Children.Add(Label("Waiting for daily activity", Muted));
        _dailyDetails.Content = Label("Waiting for daily totals", Muted);
        _attempts.Children.Add(Label("Waiting for attempts", Muted));
        _filterDetails.Content = new StackPanel { Spacing = 4, Children = { ranges, filters, BuildDimensionFilters(), _filterError } };
        _sourceDetails.Content = new StackPanel { Spacing = 5, Children = { _scope, _connection, _freshness, _analysisStatus } };
        _tokenDetails.Content = new StackPanel { Spacing = 6, Children = { _tokenTotals, _reportedComponents } };
        _historyDetails.Content = new StackPanel { Spacing = 6, Children =
        {
            Label("25 per page · expand a row for tokens, timing, identifiers and account/model links", Muted),
            new ScrollViewer { Content = _attempts, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }, paging
        } };
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        heading.Children.Add(Ui.Title("Usage"));
        _context.TextWrapping = TextWrapping.NoWrap; _context.TextTrimming = TextTrimming.CharacterEllipsis;
        _context.VerticalAlignment = VerticalAlignment.Center; _context.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(_context, 1); heading.Children.Add(_context);
        var coreFilters = new WrapPanel();
        foreach (var (caption, days) in new[] { ("7 days", 7), ("30 days", 30) })
        {
            var shortcut = new Button { Content = caption, Margin = new Thickness(0, 20, 6, 6) };
            shortcut.Click += async (_, _) => await ApplyRangeAsync(days);
            coreFilters.Children.Add(shortcut);
        }
        coreFilters.Children.Add(Field("Account", _account));
        coreFilters.Children.Add(Field("Actual model", _model));
        foreach (var button in new[] { _apply, _showFilters, _usageBack })
        { button.Margin = new Thickness(0, 20, 6, 6); coreFilters.Children.Add(button); }
        _showFilters.Click += (_, _) =>
        {
            _filterDetails.IsExpanded = !_filterDetails.IsExpanded;
            _detailOverrides[_filterDetails] = _filterDetails.IsExpanded;
        };
        _filterDetails.IsVisible = _filterDetails.IsExpanded;
        _filterDetails.PropertyChanged += (_, args) =>
        {
            if (args.Property == Expander.IsExpandedProperty)
            { _filterDetails.IsVisible = _filterDetails.IsExpanded; _showFilters.Content = _filterDetails.IsExpanded ? "Fewer filters" : "More filters"; }
        };
        _usageBack.Click += (_, _) => { if (_drilldownHistory.TryPop(out var restore)) restore(); };
        AutomationProperties.SetName(_showFilters, "Usage additional filters");
        AutomationProperties.SetName(_usageBack, "Back to previous usage result");
        AutomationProperties.SetName(_scroll, "Usage results scroll");
        AutomationProperties.SetName(_context, "Usage applied context");
        AutomationProperties.SetName(_coverage, "Usage journal coverage");
        AutomationProperties.SetName(_reportedTotal, "Usage token measurement coverage");
        AutomationProperties.SetName(_draftState, "Usage unapplied filters");
        _draftState.IsVisible = false;
        _layout.Children.Add(heading);
        _layout.Children.Add(coreFilters);
        _layout.Children.Add(_filterDetails);
        _layout.Children.Add(_draftState);
        _layout.Children.Add(_error);
        _layout.Children.Add(UsagePanel(new StackPanel { Spacing = 4, Children = { _totals, _reportedTotal } }, 0));
        _layout.Children.Add(_coverage);
        _primaryResults.Children.Add(BuildAnalysisSection());
        _primaryResults.Children.Add(UsagePanel(_historyDetails));
        _secondaryResults.Children.Add(UsageSection("Daily activity", "Historical errors · unknown values stay —", new StackPanel { Spacing = 6, Children = { _dailyScope, measure, _daily, _dailyDetails } }));
        _secondaryResults.Children.Add(UsagePanel(_performanceDetails));
        _secondaryResults.Children.Add(UsagePanel(_tokenDetails));
        _secondaryResults.Children.Add(UsagePanel(_sourceDetails));
        _resultsLayout.Children.Add(_primaryResults);
        Grid.SetRow(_secondaryResults, 1); _resultsLayout.Children.Add(_secondaryResults);
        _layout.Children.Add(_resultsLayout);
        _layout.Children.Add(new Expander
        {
            Header = "How to read these metrics", HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = Label("— means unknown. A known reported subtotal is only the measured part, not full consumption. Journal coverage and token measurement coverage are separate. Cached, cache-creation and reasoning tokens are never added to reported total tokens. Whole-window cards and daily totals count historical failed/canceled attempts or HTTP ≥400. Breakdown completion errors prefer the known completion state, then historical state/status, so counts can differ. Error percentages use attempts. Performance shows measured/eligible attempts. The end date is exclusive in the selected zone. Costs are not reported.", Muted)
        });
        _scroll.Content = _layout;
        var viewport = new Grid(); viewport.Children.Add(_scroll); Content = viewport;
        HorizontalAlignment = HorizontalAlignment.Stretch; VerticalAlignment = VerticalAlignment.Stretch;
        SizeChanged += (_, _) => ApplyResponsiveLayout();
        foreach (var input in new[] { _from, _to, _zone, _status }.Concat(_dimensionFilters.Values))
            input.TextChanged += (_, _) => UpdateDraftState();
        _account.SelectionChanged += (_, _) => UpdateDraftState();
        _model.SelectionChanged += (_, _) => UpdateDraftState();
        TrackDetailOverrides();
        SetPresentation(_presentation);
        _apply.Click += async (_, _) => await ApplyAsync();
        _previous.Click += async (_, _) => await MovePageAsync(-1);
        _next.Click += async (_, _) => await MovePageAsync(1);
        _dailyMetric.SelectionChanged += (_, _) => { if (!_restoringNavigation && _summary is not null) RenderDaily(_summary.Daily); };
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
    }

    /// <summary>Only changes disclosure and spacing; filter controls and draft input stay mounted.</summary>
    public void SetPresentation(PresentationOptions options)
    {
        var levelChanged = options.Level != _presentation.Level;
        _presentation = options;
        var compact = options.Density == ViewDensity.Compact;
        _layout.Spacing = compact ? 6 : 8;
        _primaryResults.Spacing = _secondaryResults.Spacing = compact ? 6 : 8;
        foreach (var panel in _sections)
            if (!Equals(panel.Tag, "stat-strip")) panel.Padding = new Thickness(compact ? 8 : 12);
        if (levelChanged)
        {
            PresentDetails(_filterDetails, _filterError.IsVisible);
            PresentDetails(_historyDetails, options.Level != InformationLevel.Barebones);
            PresentDetails(_tokenDetails, options.Level == InformationLevel.Advanced);
            PresentDetails(_sourceDetails, options.Level == InformationLevel.Advanced);
            PresentDetails(_moreFilters, false);
            PresentDetails(_performanceDetails, options.Level != InformationLevel.Barebones);
            PresentDetails(_dailyDetails, options.Level == InformationLevel.Advanced);
        }
        ApplyDensity();
        ApplyResponsiveLayout();
    }

    public async Task FilterToAccountAsync(string id)
    {
        UpdateChoices(_account, "All accounts", _knownAccounts, id, AccountName);
        await ApplyContextAsync(query => query with { AccountId = id, Offset = 0 });
    }

    public async Task FilterToRouteAsync(string id)
    {
        _dimensionFilters["routeId"].Text = id;
        await ApplyContextAsync(query => query with { RouteId = id, Offset = 0 });
    }

    public async Task FilterToModelAsync(string model)
    {
        UpdateChoices(_model, "All models", _knownModels, model, x => x);
        await ApplyContextAsync(query => query with { Model = model, Offset = 0 });
    }

    /// <summary>Captures UI context only. History never retains usage/attempt payloads.</summary>
    public Action CaptureNavigationState()
    {
        var query = _query;
        var scrollOffset = _scroll.Offset;
        var focused = this.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => control.IsFocused);
        var focusedName = focused is null ? null : AutomationProperties.GetName(focused);
        var drilldowns = _drilldownHistory.Reverse().ToArray();
        var hasAppliedQuery = _hasAppliedQuery;
        var from = _from.Text;
        var to = _to.Text;
        var zone = _zone.Text;
        var account = (_account.SelectedItem as FilterChoice)?.Key;
        var model = (_model.SelectedItem as FilterChoice)?.Key;
        var status = _status.Text;
        var dimensions = _dimensionFilters.ToDictionary(pair => pair.Key, pair => pair.Value.Text, StringComparer.Ordinal);
        var comparison = (_dimension.SelectedItem as FilterChoice)?.Key;
        var order = _dimensionOrder.SelectedIndex;
        var dailyMeasure = _dailyMetric.SelectedIndex;
        var filterError = _filterError.Text;
        var filterErrorVisible = _filterError.IsVisible;
        var attempts = _expandedAttempts.ToArray();
        var groups = _expandedAnalysisGroups.ToArray();
        var disclosures = DetailSections.Select(details => details.IsExpanded).ToArray();
        var detailOverrides = DetailSections.Select(details => _detailOverrides.TryGetValue(details, out var expanded) ? (bool?)expanded : null).ToArray();
        return () =>
        {
            _generation++;
            _refresh?.Cancel();
            _restoringNavigation = true;
            try
            {
                _query = query;
                _hasAppliedQuery = hasAppliedQuery;
                _restoreOffset = scrollOffset; _restoreFocus = focusedName;
                _drilldownHistory.Clear(); foreach (var restore in drilldowns) _drilldownHistory.Push(restore);
                _usageBack.IsVisible = _drilldownHistory.Count > 0;
                _from.Text = from;
                _to.Text = to;
                _zone.Text = zone;
                _status.Text = status;
                UpdateChoices(_account, "All accounts", _knownAccounts, account, AccountName);
                UpdateChoices(_model, "All models", _knownModels, model, value => value);
                foreach (var (key, value) in dimensions) _dimensionFilters[key].Text = value;
                _dimension.SelectedItem = _dimension.ItemsSource?.OfType<FilterChoice>().FirstOrDefault(choice => choice.Key == comparison);
                _dimensionOrder.SelectedIndex = order;
                _dailyMetric.SelectedIndex = dailyMeasure;
                _expandedAttempts.Clear();
                _expandedAttempts.UnionWith(attempts);
                _expandedAnalysisGroups.Clear();
                _expandedAnalysisGroups.UnionWith(groups);
                var details = DetailSections;
                _detailOverrides.Clear();
                for (var index = 0; index < details.Length; index++)
                {
                    details[index].IsExpanded = disclosures[index];
                    if (detailOverrides[index] is { } expanded) _detailOverrides[details[index]] = expanded;
                }
                _filterError.Text = filterError;
                _filterError.IsVisible = filterErrorVisible;
                ResetRestoredSnapshot();
                UpdateDraftState();
            }
            finally { _restoringNavigation = false; }
            if (_lifetime is { } lifetime)
                _ = hasAppliedQuery ? RefreshAsync(lifetime.Token) : ApplyAsync();
        };
    }

    private void ResetRestoredSnapshot()
    {
        // A prior visit's metrics must never appear beneath the restored filter scope.
        _summary = null;
        _analysis = null;
        _analysisQuery = null;
        _analysisError = null;
        _attemptToggles.Clear();
        _attemptLinks.Clear();
        _analysisButtons.Clear();
        RenderEmptyTotals();
        _daily.Children.Clear();
        _daily.Children.Add(Label("Waiting for the restored range", Muted));
        _dailyDetails.Content = Label("Waiting for the restored range", Muted);
        _attempts.Children.Clear();
        _attempts.Children.Add(Label("Waiting for restored attempt history", Muted));
        _dimensionGroups.Children.Clear();
        _dimensionGroups.Children.Add(Label("Waiting for the restored breakdown", Muted));
        _performance.Children.Clear();
        _performance.Children.Add(Label("Waiting for restored measurements", Muted));
        _unobservedAccounts.Children.Clear();
        _scope.Text = _hasAppliedQuery ? AppliedScopeText(_query) : "No filters applied yet";
        _context.Text = AppliedContextText(_query);
        _coverage.Text = "Journal coverage unknown until the restored query is refreshed";
        _coverage.Foreground = Muted;
        _connection.Text = "Restoring usage context";
        _connection.Foreground = Muted;
        _freshness.Text = "Waiting for a snapshot of the restored range";
        _analysisStatus.Text = "Waiting for restored complete-range analysis";
        _analysisStatus.Foreground = Muted;
        _analysisNotice.Text = "Waiting for restored breakdown";
        _analysisNotice.Foreground = Muted; _analysisNotice.IsVisible = true;
        _page.Text = $"Saved page {_query.Offset / _query.Limit + 1:N0} · refreshing";
        _previous.IsEnabled = false;
        _next.IsEnabled = false;
        ShowError(null);
    }

    private async Task ApplyContextAsync(Func<UsageQuery, UsageQuery> change)
    {
        if (!_hasAppliedQuery)
        {
            if (!TryReadQuery(out var initial)) return;
            _query = initial;
            _hasAppliedQuery = true;
        }
        _query = change(_query);
        UpdateDraftState();
        if (_lifetime is { } lifetime) await RefreshAsync(lifetime.Token);
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (_lifetime is not null) return;
        _lifetime = new CancellationTokenSource();
        _ = RunAsync(_lifetime.Token);
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        _generation++;
        _refresh?.Cancel();
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            // Returning from another workspace resumes the applied query and page;
            // unsubmitted inputs remain drafts until Apply is chosen.
            if (!_hasAppliedQuery)
            {
                if (!TryReadQuery(out var query)) return;
                _query = query;
                _hasAppliedQuery = true;
            }
            await RefreshAsync(token);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            while (await timer.WaitForNextTickAsync(token)) await RefreshAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private async Task ApplyAsync()
    {
        if (!TryReadQuery(out var query)) return;
        _query = query;
        _hasAppliedQuery = true;
        _drilldownHistory.Clear(); _usageBack.IsVisible = false;
        UpdateDraftState();
        if (_lifetime is { } lifetime) await RefreshAsync(lifetime.Token);
    }

    private async Task ApplyRangeAsync(int? days)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(_zone.Text?.Trim() ?? "");
            var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).Date;
            var from = days is { } count ? today.AddDays(1 - count) : new DateTime(today.Year, today.Month, 1);
            _from.Text = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            _to.Text = today.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            await ApplyAsync();
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            ShowFilterError(ex);
        }
    }

    private async Task MovePageAsync(int direction)
    {
        if (_lifetime is null) return;
        _query = _query with { Offset = Math.Max(0, _query.Offset + direction * _query.Limit) };
        await RefreshAsync(_lifetime.Token);
    }

    private bool TryReadQuery(out UsageQuery query)
    {
        query = _query;
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(_zone.Text?.Trim() ?? "");
            var from = ParseDate(_from.Text, zone);
            var to = ParseDate(_to.Text, zone);
            if (from >= to) throw new FormatException("The end date must follow the start date.");
            if (to - from > TimeSpan.FromDays(366)) throw new FormatException("Choose a range of at most 366 days.");
            int? status = null;
            if (!string.IsNullOrWhiteSpace(_status.Text))
            {
                if (!int.TryParse(_status.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value is < 100 or > 599)
                    throw new FormatException("Enter an HTTP status between 100 and 599, or leave it blank.");
                status = value;
            }
            query = new UsageQuery(from, to, zone.Id, (_account.SelectedItem as FilterChoice)?.Key,
                (_model.SelectedItem as FilterChoice)?.Key, status, 0, 25);
            query = ReadDimensionFilters(query);
            _filterError.Text = "";
            _filterError.IsVisible = false;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            ShowFilterError(ex);
            return false;
        }
    }

    private void ShowFilterError(Exception error)
    {
        _filterError.Text = error is TimeZoneNotFoundException ? "Unknown time zone. Use an IANA name such as Europe/Berlin or UTC." : error.Message;
        _filterError.IsVisible = true;
        SetAutomaticDetails(_filterDetails, true);
    }

    private static DateTimeOffset ParseDate(string? text, TimeZoneInfo zone)
    {
        if (!DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new FormatException("Use yyyy-MM-dd for both dates.");
        date = DateTime.SpecifyKind(date, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(date) || zone.IsAmbiguousTime(date))
            throw new FormatException("A selected midnight is ambiguous or does not exist in that time zone. Choose another date or UTC.");
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(date, zone), TimeSpan.Zero);
    }

    private async Task RefreshAsync(CancellationToken lifetime)
    {
        _refresh?.Cancel();
        var refresh = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        _refresh = refresh;
        var generation = ++_generation;
        var query = _query;
        try
        {
            _connection.Text = "Connecting to lain host…";
            var summaryTask = _client.UsageAsync(query, refresh.Token);
            var attemptsTask = _client.AttemptsAsync(query, refresh.Token);
            // Friendly account names are optional; historical IDs still render when configuration is unavailable.
            var namesTask = ReadAccountNamesAsync(refresh.Token);
            var analysisTask = ReadAnalysisAsync(query, refresh.Token);
            await Task.WhenAll(summaryTask, attemptsTask, namesTask, analysisTask);
            if (refresh.IsCancellationRequested || generation != _generation) return;
            var summary = await summaryTask;
            if (summary.ContractVersion != Api.Version)
                throw new InvalidOperationException($"Usage contract {summary.ContractVersion} is unsupported; expected {Api.Version}.");
            var offset = _restoreOffset ?? _scroll.Offset;
            Render(summary, await attemptsTask, query);
            var analysis = await analysisTask;
            RenderAnalysis(analysis.Value, analysis.Error, query);
            ShowError(null);
            RestoreViewport(offset);
        }
        catch (OperationCanceledException) when (refresh.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (refresh.IsCancellationRequested || generation != _generation) return;
            _connection.Text = "Host unavailable · displayed snapshot may be stale";
            _connection.Foreground = Ui.Warning;
            ShowError($"Usage could not be refreshed: {ex.Message}" + (_summary is null ? " · no snapshot available" : " · showing the last known snapshot for the context above"));
        }
        finally
        {
            if (ReferenceEquals(_refresh, refresh)) _refresh = null;
            refresh.Dispose();
        }
    }

    private async Task ReadAccountNamesAsync(CancellationToken token)
    {
        try
        {
            var accounts = await _client.AccountsAsync(token);
            token.ThrowIfCancellationRequested();
            _configuredAccounts.Clear();
            _configuredAccounts.UnionWith(accounts.Select(account => account.Id));
            foreach (var account in accounts) _accountNames[account.Id] = account.Name;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { /* Usage remains available when account configuration cannot be read. */ }
    }

    private void Render(UsageSummaryDto summary, PageDto<AttemptDto> page, UsageQuery query)
    {
        if (_summary is { } prior && (summary.Cursor < prior.Cursor || summary.ObservedAt < prior.ObservedAt)) _journalContinuityUnknown = true;
        _summary = summary;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(summary.TimeZone);
        _connection.Text = "Connected · source: lain host / persistent usage API";
        _connection.Foreground = Accent;
        var age = DateTimeOffset.UtcNow - summary.ObservedAt;
        _freshness.Text = $"Observed {TimeZoneInfo.ConvertTime(summary.ObservedAt, zone):yyyy-MM-dd HH:mm:ss zzz} ({Math.Max(0, age.TotalSeconds):N0}s ago) · cursor {summary.Cursor:N0} · zone {summary.TimeZone}\nApplied range: {TimeZoneInfo.ConvertTime(summary.From, zone):yyyy-MM-dd HH:mm} → {TimeZoneInfo.ConvertTime(summary.To, zone):yyyy-MM-dd HH:mm} (exclusive)";
        _scope.Text = AppliedScopeText(query with { From = summary.From, To = summary.To, TimeZone = summary.TimeZone });
        _context.Text = AppliedContextText(query with { From = summary.From, To = summary.To, TimeZone = summary.TimeZone });
        ToolTip.SetTip(_context, _scope.Text);
        _dailyScope.Text = ResultScope(query) + " · applied calendar range";
        _coverage.Text = _journalContinuityUnknown ? "Journal continuity unconfirmed · source cursor or observation time moved backwards"
            : summary.Complete ? "Journal coverage complete for the selected range" : "Journal coverage incomplete · stored totals may omit activity";
        _coverage.Foreground = summary.Complete && !_journalContinuityUnknown ? Muted : Ui.Warning;
        _coverage.Text += $" · observed {TimeZoneInfo.ConvertTime(summary.ObservedAt, zone):HH:mm:ss}";
        RenderTotals(summary.Totals);
        _reportedComponents.Children.Clear();
        _reportedTotal.IsVisible = true;
        _reportedTotal.Text = "Token measurement coverage is not supplied by this host";
        if (summary.Totals.Measurements is { } measurements)
        {
            _reportedTotal.Text = Lain.Client.UsageDisplayFacts.Describe("Total tokens", measurements.Total)
                + (summary.Totals.Tokens.Total is null ? " · full total unknown" : "");
            foreach (var (name, value) in Lain.Client.UsageDisplayFacts.Components(measurements))
                _reportedComponents.Children.Add(Label(Lain.Client.UsageDisplayFacts.Describe(name, value), Muted));
        }
        RenderDaily(summary.Daily);
        RenderAttempts(page, zone);
        RenderUnobservedAccounts(summary, query);
        // Never derive totals, group counts or token metrics from a page of attempts.
        _knownAccounts.UnionWith(summary.Accounts.Select(x => x.Key).Concat(_accountNames.Keys));
        _knownModels.UnionWith(summary.Models.Select(x => x.Key));
        // Polling must not discard unsubmitted filter selections.
        UpdateChoices(_account, "All accounts", _knownAccounts, (_account.SelectedItem as FilterChoice)?.Key, AccountName);
        UpdateChoices(_model, "All models", _knownModels, (_model.SelectedItem as FilterChoice)?.Key, x => x);
        UpdateDraftState();
    }

    private static readonly string[] TotalTitles = { "Requests", "Attempts", "Historical errors", "Reported tokens", "Input", "Output", "Cached", "Reasoning", "Cache creation" };

    private void RenderEmptyTotals()
    {
        RenderStatStrip(Enumerable.Repeat("—", TotalTitles.Length).ToArray());
        _reportedTotal.Text = "Token measurement coverage unknown";
        _reportedTotal.IsVisible = true;
        _reportedComponents.Children.Clear();
    }

    private void RenderTotals(UsageTotals totals) => RenderStatStrip(
        new[]
        {
            Number(totals.Requests), Number(totals.Attempts), Number(totals.Errors), Number(totals.Tokens.Total ?? totals.Measurements?.Total.KnownReported),
            Number(totals.Tokens.Input), Number(totals.Tokens.Output), Number(totals.Tokens.Cached), Number(totals.Tokens.Reasoning), Number(totals.Tokens.CacheCreation)
        },
        new[] { "Client requests", "Upstream attempts", totals.Attempts > 0 ? $"{ErrorRate(totals)} of attempts" : "No attempts", totals.Tokens.Total is not null ? "Full measured total" : totals.Measurements?.Total.KnownReported is not null ? "Partial · full total unknown" : "Full total unknown", "Reported input", "Reported output", "Not added to total", "Not added to total", "Not added to total" });

    private void RenderStatStrip(string[] values, string[]? hints = null)
    {
        _totals.Children.Clear();
        _tokenTotals.Children.Clear();
        for (var index = 0; index < TotalTitles.Length; index++)
        {
            var value = Label(values[index], index == 3 ? Accent : Ui.Ink);
            value.FontSize = 20;
            value.FontWeight = FontWeight.SemiBold;
            value.TextWrapping = TextWrapping.NoWrap;
            value.TextTrimming = TextTrimming.CharacterEllipsis;
            ToolTip.SetTip(value, index == 2 ? $"{values[index]} failed/canceled attempts or HTTP ≥400. Percentage uses attempts in the applied range." : values[index]);
            var content = new StackPanel { Spacing = 3, Children = { Label(TotalTitles[index], Muted), value } };
            if (hints is not null)
            {
                var hint = Label(hints[index], Muted);
                hint.FontSize = 11;
                content.Children.Add(hint);
            }
            var tile = new Border
            {
                BorderBrush = Ui.Edge, BorderThickness = new Thickness(0, 0, 1, 0), Padding = StatPadding,
                Child = content
            };
            tile.Classes.Add("usage-stat");
            (index < 4 ? _totals : _tokenTotals).Children.Add(tile);
        }
    }

    private void RenderDaily(UsageGroup[] days)
    {
        _daily.Children.Clear();
        var metric = _dailyMetric.SelectedItem as string ?? "Total tokens";
        var exactTotals = new StackPanel();
        exactTotals.Children.Add(GroupRow("Day", "Requests", "Attempts", "Errors · %", metric, "Unknown", true));
        foreach (var day in days)
        {
            var totals = day.Totals;
            exactTotals.Children.Add(GroupRow(day.Key, Number(totals.Requests), Number(totals.Attempts), Errors(totals), Number(ActivityValue(totals)), Number(totals.UnknownUsageAttempts)));
        }
        _dailyDetails.Content = exactTotals;
        if (days.Length == 0)
        {
            _daily.Children.Add(Label("No activity in this range", Muted));
            exactTotals.Children.Add(Label("No activity in this range", Muted));
            return;
        }
        var known = days.Select(day => ActivityValue(day.Totals)).Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        var max = known.DefaultIfEmpty(0).Max();
        var chart = new Grid { Height = 114, Margin = new Thickness(0, 6, 0, 0) };
        for (var index = 0; index < days.Length; index++)
        {
            var day = days[index];
            chart.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var amount = ActivityValue(day.Totals);
            var ratio = amount is { } value && max > 0 ? Math.Clamp((double)value / max, 0, 1) : 0;
            var slot = new Grid { RowDefinitions = new RowDefinitions("*,20"), Background = Brushes.Transparent };
            ToolTip.SetTip(slot, $"{day.Key} · {metric}: {Number(amount)}\n{day.Totals.Requests:N0} requests · {day.Totals.Attempts:N0} attempts · {Errors(day.Totals)} errors\n{TokenDetails(day.Totals.Tokens)}\n{day.Totals.UnknownUsageAttempts:N0} attempts with unknown usage or total");
            var bar = new Border
            {
                Background = Accent, Height = ratio * 88, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(days.Length > 40 ? 0.5 : 3, 0), MaxWidth = 50,
                CornerRadius = new CornerRadius(2, 2, 0, 0), IsVisible = ratio > 0
            };
            slot.Children.Add(bar);
            if (amount is null)
            {
                var unknown = Label("—", Muted);
                unknown.HorizontalAlignment = HorizontalAlignment.Center;
                unknown.VerticalAlignment = VerticalAlignment.Bottom;
                slot.Children.Add(unknown);
            }
            // Keep every host bucket; thin the labels only, so long ranges still fit the viewport.
            var showLabel = index % Math.Max(1, (int)Math.Ceiling(days.Length / 7d)) == 0;
            if (showLabel)
            {
                var label = Cell(day.Key.Length >= 10 ? day.Key[5..] : day.Key, Muted);
                label.FontSize = 11;
                label.HorizontalAlignment = HorizontalAlignment.Center;
                Grid.SetRow(label, 1);
                slot.Children.Add(label);
            }
            Put(chart, slot, index);
        }
        _daily.Children.Add(chart);
        var peak = known.Length == 0 ? $"{metric.ToLowerInvariant()} unknown" : $"{(known.Length < days.Length ? "known peak" : "peak")} {max:N0} {metric.ToLowerInvariant()} / day";
        _daily.Children.Add(Label($"{days[0].Key} → {days[^1].Key} · {peak}" + (known.Length < days.Length ? " · — marks unknown values" : ""), Muted));
    }

    private long? ActivityValue(UsageTotals totals) => _dailyMetric.SelectedIndex switch
    {
        0 => totals.Attempts,
        2 => totals.Tokens.Output,
        _ => totals.Tokens.Total
    };

    private async Task ApplyGroupAsync(string key, bool account)
    {
        await ApplyDrilldownAsync(query => account ? query with { AccountId = key, Offset = 0 } : query with { Model = key, Offset = 0 });
    }

    private Border GroupRow(string identity, string requests, string attempts, string errors, string tokens, string unknown, bool header = false) =>
        TableRow(GroupCells(identity, requests, attempts, errors, tokens, unknown, header), header);

    private Grid GroupCells(string identity, string requests, string attempts, string errors, string tokens, string unknown, bool header = false)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("28,2.5*,*,*,1.3*,1.2*,*"), MinWidth = 600, MinHeight = RowHeight, Classes = { "usage-row" } };
        var values = new[] { identity, requests, attempts, errors, tokens, unknown };
        for (var i = 0; i < values.Length; i++)
        {
            var label = Cell(values[i], header ? Muted : Ui.Ink);
            if (header) label.FontWeight = FontWeight.SemiBold;
            if (header && i == 3) ToolTip.SetTip(label, "Failed/canceled attempts or HTTP ≥400, with their percentage of all attempts in the applied range.");
            if (header && i == 5) ToolTip.SetTip(label, "Attempts without reported usage or a known total. Individual token fields may also be unknown.");
            if (i > 0) label.HorizontalAlignment = HorizontalAlignment.Right;
            Put(grid, label, i + 1);
        }
        return grid;
    }

    private void RenderAttempts(PageDto<AttemptDto> page, TimeZoneInfo zone)
    {
        // Polling may replace the rows, but must leave keyboard users on the same attempt.
        var focusedAttempt = _attemptToggles.FirstOrDefault(pair => pair.Value.IsFocused).Key;
        var focusedLink = _attemptLinks.FirstOrDefault(pair => pair.Value.IsFocused).Key;
        _attemptToggles.Clear();
        _attemptLinks.Clear();
        _attempts.Children.Clear();
        _attempts.Children.Add(TableRow(AttemptCells("", "Time", "Account / model", "Status", "Tokens", "Duration", true), true));
        if (page.Items.Length == 0) _attempts.Children.Add(Label("No attempts match these filters", Muted));
        foreach (var attempt in page.Items)
        {
            var identity = (attempt.SourceId ?? "") + "\n" + attempt.Id;
            var status = attempt.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? "—";
            var failed = attempt.State is "failed" or "canceled" || attempt.StatusCode >= 400;
            var expanded = _expandedAttempts.Contains(identity);
            var toggle = DetailToggle(expanded, $"Details for attempt {attempt.Id}");
            _attemptToggles[identity] = toggle;
            var row = AttemptCells("", TimeZoneInfo.ConvertTime(attempt.StartedAt, zone).ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                $"{AccountName(attempt.AccountId)} / {attempt.Model}", status, Number(attempt.Tokens.Total), Milliseconds(attempt.DurationMs));
            Put(row, toggle, 0);
            var statusCell = row.Children.OfType<TextBlock>().First(x => Grid.GetColumn(x) == 3);
            statusCell.Foreground = failed ? Ui.Warning : status == "—" ? Muted : Ui.Success;
            ToolTip.SetTip(statusCell, $"HTTP {status} · historical state: {attempt.State} · completion: {attempt.CompletionState ?? "unknown"}");
            var details = new StackPanel
            {
                IsVisible = expanded, Spacing = 4, Margin = new Thickness(32, 4, 8, 10),
                Children =
                {
                    Label($"{TimeZoneInfo.ConvertTime(attempt.StartedAt, zone):yyyy-MM-dd HH:mm:ss zzz} · HTTP {status} · historical state: {attempt.State} · completion: {attempt.CompletionState ?? "unknown"}", Muted),
                    Label($"{AccountName(attempt.AccountId)} · {attempt.AccountId} · {attempt.Provider} · {attempt.Model}" + (attempt.RouteId is null ? "" : $" · route {attempt.RouteId}"), Muted),
                    Label(TokenDetails(attempt.Tokens), Muted),
                    Label($"Duration {Milliseconds(attempt.DurationMs)} · first token {Milliseconds(attempt.FirstTokenMs)}", Muted),
                    Label($"Output / end-to-end: {OutputRate(attempt)} · reported output ÷ full attempt duration", Muted),
                    Label($"Request {attempt.RequestId} · attempt {attempt.Id} · source {attempt.SourceId ?? "not supplied"}", Muted)
                }
            };
            if (attempt.RequestedModel is { } requestedModel) details.Children.Add(Label("Requested model: " + requestedModel, Muted));
            if (attempt.Measurements is { } measured)
                details.Children.Add(Label($"Owned-core measurements: first output {Milliseconds(measured.FirstOutputLatencyMs)} · total {Milliseconds(measured.TotalDurationMs)} · source {measured.Source ?? "not supplied"}", Muted));
            if (attempt.Dimensions is { } dimensions)
            {
                var values = new (string Name, string? Value)[] { ("Family", dimensions.ModelFamily), ("Manufacturer", dimensions.Manufacturer),
                    ("Inference", dimensions.InferenceProvider), ("Gateway", dimensions.Gateway), ("Billing account", dimensions.BillingAccountId),
                    ("Requested reasoning", dimensions.ReasoningEffort), ("Effective reasoning", dimensions.EffectiveReasoningEffort), ("Mode", dimensions.Mode), ("Conversation", dimensions.ConversationId),
                    ("Turn", dimensions.TurnId), ("Worker", dimensions.WorkerId), ("Harness", dimensions.Harness),
                    ("Profile", dimensions.ProfileId), ("Parent worker", dimensions.ParentWorkerId), ("Parent request", dimensions.ParentRequestId),
                    ("Tool call", dimensions.ToolCallId), ("Response", dimensions.ResponseId), ("Previous response", dimensions.PreviousResponseId) };
                var known = values.Where(x => x.Value is not null).Select(x => x.Name + ": " + x.Value).ToArray();
                if (known.Length > 0) details.Children.Add(Label(string.Join(" · ", known), Muted));
                if (dimensions.Evidence is { Length: > 0 } evidence)
                    details.Children.Add(new Expander { Header = "Attribution sources", Content = Label(string.Join("\n", evidence.Select(x => $"{x.Field} · {x.Source}" + (x.Key is null ? "" : " · " + x.Key))), Muted) });
            }
            if (attempt.ErrorDetails is { } errorDetails)
                details.Children.Add(Label($"Error category: {errorDetails.Category} · evidence: {errorDetails.Evidence} · next action: {errorDetails.Action}", Ui.Warning));
            else if (!string.IsNullOrWhiteSpace(attempt.Error)) details.Children.Add(Label(attempt.Error, Ui.Warning));
            var accountLink = NavigationButton("Open account", $"Open account for attempt {attempt.Id}", new WorkspaceNavigationRequest("Accounts", AccountId: attempt.AccountId));
            var modelLink = NavigationButton("Open model", $"Open model for attempt {attempt.Id}", new WorkspaceNavigationRequest("Models", AccountId: attempt.AccountId, RouteId: attempt.RouteId, Model: attempt.Model));
            _attemptLinks[identity + "/account"] = accountLink;
            _attemptLinks[identity + "/model"] = modelLink;
            details.Children.Add(new WrapPanel { Children = { accountLink, modelLink } });
            toggle.Click += (_, _) =>
            {
                details.IsVisible = !details.IsVisible;
                toggle.Content = details.IsVisible ? "⌄" : "›";
                if (details.IsVisible) _expandedAttempts.Add(identity); else _expandedAttempts.Remove(identity);
            };
            ObjectCommand[] commands =
            [
                new("details", "Attempt details", () => { if (!details.IsVisible) toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }),
                new("account", "Open account", () => accountLink.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))),
                new("model", "Open model", () => modelLink.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)))
            ];
            var entry = new StackPanel { Children = { row, details }, ContextMenu = ObjectCommands.Menu(commands) };
            // Errors are visible without opening the detail row.
            if (!string.IsNullOrWhiteSpace(attempt.Error))
                entry.Children.Add(new Border { Padding = new Thickness(32, 0, 8, 6), Child = Cell(attempt.Error, Ui.Warning) });
            _attempts.Children.Add(TableRow(entry));
        }
        if (focusedAttempt is not null && _attemptToggles.TryGetValue(focusedAttempt, out var focusedToggle))
            focusedToggle.Focus();
        if (focusedLink is not null && _attemptLinks.TryGetValue(focusedLink, out var focusedButton))
            focusedButton.Focus();
        var first = page.Items.Length == 0 ? 0 : page.Offset + 1;
        _page.Text = $"{first:N0}–{page.Offset + page.Items.Length:N0} of {page.Total:N0}";
        _previous.IsEnabled = page.Offset > 0;
        _next.IsEnabled = (long)page.Offset + page.Items.Length < page.Total && page.Items.Length > 0;
    }

    private Grid AttemptCells(string toggle, string time, string identity, string status, string tokens, string duration, bool header = false)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("28,1.25*,2.5*,.65*,.8*,1*"), MinWidth = 600, MinHeight = RowHeight, Classes = { "usage-row" } };
        var values = new[] { toggle, time, identity, status, tokens, duration };
        for (var index = 1; index < values.Length; index++)
        {
            var cell = Cell(values[index], header ? Muted : Ui.Ink);
            if (header) cell.FontWeight = FontWeight.SemiBold;
            if (index >= 3) cell.HorizontalAlignment = HorizontalAlignment.Right;
            Put(row, cell, index);
        }
        return row;
    }

    private static void UpdateChoices(ComboBox combo, string all, IEnumerable<string> keys, string? selected, Func<string, string> name)
    {
        var values = keys.Append(selected ?? "").Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.Ordinal).OrderBy(name, StringComparer.OrdinalIgnoreCase);
        var choices = new[] { new FilterChoice(null, all) }.Concat(values.Select(x => new FilterChoice(x, name(x)))).ToArray();
        if (combo.ItemsSource is FilterChoice[] previous && previous.SequenceEqual(choices) && (combo.SelectedItem as FilterChoice)?.Key == selected) return;
        combo.ItemsSource = choices;
        combo.SelectedItem = choices.FirstOrDefault(x => x.Key == selected) ?? choices[0];
    }

    private void ApplyResponsiveLayout()
    {
        var wide = Bounds.Width >= 1000;
        if (_wideResults != wide)
        {
            _wideResults = wide;
            _resultsLayout.ColumnDefinitions = new ColumnDefinitions(wide ? "*,320" : "*");
            _resultsLayout.RowDefinitions = new RowDefinitions(wide ? "Auto" : "Auto,Auto");
            Grid.SetColumn(_secondaryResults, wide ? 1 : 0);
            Grid.SetRow(_secondaryResults, wide ? 0 : 1);
            _secondaryResults.Margin = wide ? new Thickness(12, 0, 0, 0) : new Thickness(0, 8, 0, 0);
        }
        _totals.Columns = Bounds.Width is > 0 and < 540 ? 2 : 4;
        _tokenTotals.Columns = Bounds.Width is > 0 and < 620 || wide ? 2 : 4;
        foreach (var metrics in _performance.Children.OfType<UniformGrid>()) metrics.Columns = wide || Bounds.Width < 620 ? 1 : 3;
    }

    private void RestoreViewport(Vector offset)
    {
        var focus = _restoreFocus;
        _restoreOffset = null; _restoreFocus = null;
        Dispatcher.UIThread.Post(() =>
        {
            if (_lifetime is null) return;
            if (focus is not null)
                this.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => AutomationProperties.GetName(control) == focus)?.Focus();
            _scroll.Offset = offset;
        }, DispatcherPriority.Loaded);
    }

    private async Task ApplyDrilldownAsync(Func<UsageQuery, UsageQuery> filter)
    {
        var restore = CaptureNavigationState();
        _drilldownHistory.Push(restore);
        _usageBack.IsVisible = true;
        // Drill into the displayed server snapshot, not unrelated inputs awaiting Apply.
        _query = filter(_analysisQuery ?? _query) with { Offset = 0 };
        _hasAppliedQuery = true;
        _restoreOffset = new Vector(0, 0);
        UpdateDraftState();
        if (_lifetime is { } lifetime) await RefreshAsync(lifetime.Token);
    }

    private static UsageQuery WithDimension(UsageQuery query, string dimension, string value) => dimension switch
    {
        "routeId" => query with { RouteId = value }, "modelFamily" => query with { ModelFamily = value },
        "manufacturer" => query with { Manufacturer = value }, "inferenceProvider" => query with { InferenceProvider = value },
        "gateway" => query with { Gateway = value }, "billingAccountId" => query with { BillingAccountId = value },
        "reasoningEffort" => query with { ReasoningEffort = value }, "effectiveReasoningEffort" => query with { EffectiveReasoningEffort = value },
        "mode" => query with { Mode = value }, "conversationId" => query with { ConversationId = value },
        "turnId" => query with { TurnId = value }, "workerId" => query with { WorkerId = value },
        "harness" => query with { Harness = value }, "profileId" => query with { ProfileId = value }, _ => query
    };

    private void UpdateDraftState()
    {
        if (_restoringNavigation || !_hasAppliedQuery || _dimensionFilters.Count == 0) return;
        // Reading draft dimensions is side-effect free; invalid dates remain editable without error banners until Apply.
        var draftDimensions = ReadDimensionFilters(_query);
        var sameDimensions = (draftDimensions with { GroupBy = _query.GroupBy, GroupLimit = _query.GroupLimit }) == _query;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(_query.TimeZone);
        var sameDates = _query.From is { } from && _query.To is { } to
            && _from.Text?.Trim() == TimeZoneInfo.ConvertTime(from, zone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            && _to.Text?.Trim() == TimeZoneInfo.ConvertTime(to, zone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            && _zone.Text?.Trim() == _query.TimeZone;
        _draftState.IsVisible = !sameDates || !sameDimensions || (_account.SelectedItem as FilterChoice)?.Key != _query.AccountId
            || (_model.SelectedItem as FilterChoice)?.Key != _query.Model || (_status.Text?.Trim() ?? "") != (_query.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? "");
    }

    private string AppliedContextText(UsageQuery query)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(query.TimeZone);
        var range = query.From is { } from && query.To is { } to
            ? $"{TimeZoneInfo.ConvertTime(from, zone):dd MMM} → {TimeZoneInfo.ConvertTime(to, zone):dd MMM} excl." : "Host default range";
        return range + " · " + query.TimeZone + " · " + ResultScope(query);
    }
    private string ResultScope(UsageQuery query) => (query.AccountId is { } id ? AccountName(id) : "All accounts")
        + (query.Model is { } model ? " · " + model : "") + (query.StatusCode is { } status ? $" · HTTP {status}" : "")
        + (DimensionScope(query).Length > 0 ? " · applied dimension filters" : "");
    private static string ReportedTokens(UsageTotals totals) => totals.Tokens.Total is { } full ? Number(full)
        : totals.Measurements?.Total.KnownReported is { } partial ? Number(partial) + " partial" : "—";

    private string AccountName(string id) => _accountNames.GetValueOrDefault(id, id);
    private string AppliedScopeText(UsageQuery query)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(query.TimeZone);
        var range = query.From is { } from && query.To is { } to
            ? $"{TimeZoneInfo.ConvertTime(from, zone):yyyy-MM-dd} → {TimeZoneInfo.ConvertTime(to, zone):yyyy-MM-dd} (exclusive)"
            : "Host default range";
        return $"Applied range: {range} · {query.TimeZone}\nApplied filters: {(query.AccountId is { } accountId ? $"{AccountName(accountId)} [{accountId}]" : "all accounts")} · {query.Model ?? "all models"} · {(query.StatusCode is { } status ? $"HTTP {status}" : "all outcomes")}" + DimensionScope(query);
    }
    private static string Number(long? value) => value?.ToString("N0", CultureInfo.CurrentCulture) ?? "—";
    private static string Milliseconds(double? value)
    {
        if (value is not { } ms || !double.IsFinite(ms) || ms < 0) return "—";
        return ms < 1000 ? $"{ms:N0} ms" : ms < 60000 ? $"{ms / 1000:N1} s" : $"{ms / 60000:N1} min";
    }
    private static string ErrorRate(UsageTotals totals) => totals.Attempts > 0 ? $"{100d * totals.Errors / totals.Attempts:N1}%" : "—";
    private static string Errors(UsageTotals totals) => totals.Attempts > 0 ? $"{Number(totals.Errors)} · {ErrorRate(totals)}" : Number(totals.Errors);
    private static string TokenDetails(TokenCounts tokens) => $"Tokens: {Number(tokens.Total)} total · {Number(tokens.Input)} input · {Number(tokens.Output)} output · {Number(tokens.Cached)} cached · {Number(tokens.CacheCreation)} cache creation · {Number(tokens.Reasoning)} reasoning";
    private static string OutputRate(AttemptDto attempt)
    {
        // This is a per-attempt display calculation, never an aggregate over the current page.
        if (attempt.FinishedAt is not { } finished || finished < attempt.StartedAt || attempt.State == "pending" ||
            attempt.CompletionState is "active" or "pending" or "streaming" or "unknown" ||
            attempt.Tokens.Output is not { } output || output < 0 ||
            (attempt.Measurements?.TotalDurationMs ?? attempt.DurationMs) is not { } duration || !double.IsFinite(duration) || duration <= 0) return "—";
        var rate = output * 1000d / duration;
        return double.IsFinite(rate) ? $"{rate:N1} tok/s" : "—";
    }
    private static Button DetailToggle(bool expanded, string name)
    {
        var button = new Button { Content = expanded ? "⌄" : "›", Width = 26, Height = 28, MinHeight = 28, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        ToolTip.SetTip(button, "Show details");
        Avalonia.Automation.AutomationProperties.SetName(button, name);
        return button;
    }
    private void ShowError(string? message) { _error.Text = message ?? ""; _error.IsVisible = message is not null; }
    private static TextBlock Label(string text, IBrush? brush = null) => new() { Text = text, Foreground = brush, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    private static TextBlock Cell(string text, IBrush brush)
    {
        var label = Label(text, brush);
        label.TextWrapping = TextWrapping.NoWrap;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = new Thickness(6, 0);
        ToolTip.SetTip(label, text);
        return label;
    }
    private static Border TableRow(Control child, bool header = false) => new()
    {
        Background = header ? Ui.Raised : Brushes.Transparent, BorderBrush = Ui.Edge,
        BorderThickness = new Thickness(0, 0, 0, 1), Child = child
    };
    private static Border Panel(Control child, double padding = 12) => new()
    {
        Background = Ui.Surface, BorderBrush = Ui.Edge, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6), Padding = new Thickness(padding), Child = child
    };
    private static StackPanel Field(string title, Control control)
    {
        Avalonia.Automation.AutomationProperties.SetName(control, title);
        return new StackPanel
        {
            Spacing = 3, Margin = new Thickness(0, 0, 8, 6), Children = { Label(title, Muted), control }
        };
    }
    private Border UsagePanel(Control child, double padding = 12)
    {
        var panel = Panel(child, padding == 0 ? 0 : _presentation.Density == ViewDensity.Compact ? 8 : padding);
        if (padding == 0) panel.Tag = "stat-strip";
        _sections.Add(panel);
        return panel;
    }

    private Border UsageSection(string title, string description, Control body) => UsagePanel(new StackPanel
    {
        Spacing = 8,
        Children =
        {
            new WrapPanel { Children = { new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Ui.Ink, Margin = new Thickness(0, 0, 12, 0), Classes = { "section-title" } }, Label(description, Muted) } },
            body
        }
    });

    private double RowHeight => _presentation.Density == ViewDensity.Compact ? 28 : 36;
    private Thickness StatPadding => _presentation.Density == ViewDensity.Compact ? new Thickness(8, 6) : new Thickness(12, 9);

    private void ApplyDensity()
    {
        foreach (var row in _layout.GetLogicalDescendants().OfType<Grid>().Where(x => x.Classes.Contains("usage-row")))
            row.MinHeight = RowHeight;
        foreach (var tile in _layout.GetLogicalDescendants().OfType<Border>().Where(x => x.Classes.Contains("usage-stat")))
            tile.Padding = StatPadding;
    }

    private Expander[] DetailSections => [_filterDetails, _moreFilters, _sourceDetails, _tokenDetails, _performanceDetails, _dailyDetails, _historyDetails];

    private void TrackDetailOverrides()
    {
        foreach (var details in DetailSections)
            details.PropertyChanged += (_, args) =>
            {
                if (args.Property == Expander.IsExpandedProperty && !_settingDetails && !_restoringNavigation)
                    _detailOverrides[details] = details.IsExpanded;
            };
    }

    private void PresentDetails(Expander details, bool expanded)
    {
        // Explicit local choices survive presets, independent of temporary focus protection.
        var chosen = _detailOverrides.GetValueOrDefault(details, expanded);
        // A preset must not close the disclosure containing the active keyboard control.
        SetAutomaticDetails(details, chosen || details.GetLogicalDescendants().OfType<Control>().Any(x => x.IsFocused));
    }

    private void SetAutomaticDetails(Expander details, bool expanded)
    {
        var settingDetails = _settingDetails;
        _settingDetails = true;
        try { details.IsExpanded = expanded; }
        finally { _settingDetails = settingDetails; }
    }

    private Button NavigationButton(string caption, string automationName, WorkspaceNavigationRequest target)
    {
        var button = new Button { Content = caption, Margin = new Thickness(0, 0, 6, 4) };
        Avalonia.Automation.AutomationProperties.SetName(button, automationName);
        button.Click += (_, _) => NavigateRequested?.Invoke(target);
        return button;
    }
    private static void Put(Grid grid, Control control, int column) { Grid.SetColumn(control, column); grid.Children.Add(control); }
    private sealed record FilterChoice(string? Key, string Name) { public override string ToString() => Name; }
}
