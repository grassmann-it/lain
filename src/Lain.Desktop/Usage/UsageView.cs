using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Lain.Client;
using Lain.Core;

namespace Lain.Desktop.Usage;

/// <summary>Read-only projection of host aggregates. Collection continues in the host.</summary>
public sealed partial class UsageView : UserControl
{
    private static IBrush Muted => Ui.Muted;
    private static IBrush Accent => Ui.Accent;
    private readonly LainApiClient _client;
    private readonly TextBox _from = new() { Width = 118, PlaceholderText = "yyyy-MM-dd" };
    private readonly TextBox _to = new() { Width = 118, PlaceholderText = "yyyy-MM-dd" };
    private readonly TextBox _zone = new() { Width = 172 };
    private readonly ComboBox _account = new() { Width = 172 };
    private readonly ComboBox _model = new() { Width = 172 };
    private readonly TextBox _status = new() { Width = 84, PlaceholderText = "Any" };
    private readonly TextBlock _connection = Label("Waiting for usage service", Muted);
    private readonly TextBlock _freshness = Label("No snapshot received", Muted);
    private readonly TextBlock _coverage = Label("Coverage unknown until a snapshot is received", Muted);
    private readonly TextBlock _scope = Label("No filters applied yet", Muted);
    private readonly TextBlock _error = Label("", Ui.Warning);
    private readonly TextBlock _filterError = Label("", Ui.Warning);
    private readonly UniformGrid _totals = new() { Columns = 4 };
    private readonly HashSet<string> _expandedAttempts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _attemptToggles = new(StringComparer.Ordinal);
    private readonly HashSet<(bool Account, string Key)> _expandedGroups = new();
    private readonly Dictionary<(bool Account, string Key, bool Filter), Button> _groupButtons = new();
    private readonly Expander _dailyDetails = new() { Header = "Daily totals", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _dailyMetric = new() { Width = 152, ItemsSource = new[] { "Attempts", "Total tokens", "Output tokens" }, SelectedIndex = 1 };
    private readonly StackPanel _daily = new() { Spacing = 4 };
    private readonly StackPanel _accounts = new();
    private readonly StackPanel _models = new();
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
    private long _generation;

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
        filters.Children.Add(Field("Account", _account));
        filters.Children.Add(Field("Actual model", _model));
        filters.Children.Add(Field("HTTP status", _status));
        filters.Children.Add(new StackPanel { Margin = new Thickness(0, 20, 0, 6), Children = { _apply } });
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

        var paging = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 8, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _previous, _next } };
        DockPanel.SetDock(buttons, Dock.Right);
        paging.Children.Add(buttons);
        _page.VerticalAlignment = VerticalAlignment.Center;
        paging.Children.Add(_page);
        RenderEmptyTotals();
        _daily.Children.Add(Label("Waiting for daily activity", Muted));
        _dailyDetails.Content = Label("Waiting for daily totals", Muted);
        _accounts.Children.Add(Label("Waiting for account totals", Muted));
        _models.Children.Add(Label("Waiting for model totals", Muted));
        _attempts.Children.Add(Label("Waiting for attempts", Muted));
        Content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new StackPanel { Spacing = 3, Children = { Ui.Title("Usage"), Label("Persistent activity · updates every 5 seconds", Muted) } },
                Panel(new StackPanel { Spacing = 4, Children = { ranges, filters, BuildDimensionFilters(), _filterError } }),
                _error,
                new StackPanel { Spacing = 3, Children = { _connection, _freshness, _scope, _coverage } },
                Panel(_totals, 0),
                BuildAnalysisSection(),
                Section("Daily activity", "Calendar days · unknown values stay —", new StackPanel { Spacing = 6, Children = { measure, _daily, _dailyDetails } }),
                Section("Accounts", "Expand for token details and filtering · historical identities retained", _accounts),
                Section("Actual models", "Expand for token details and filtering", _models),
                Section("Attempts & errors", "25 per page · expand a row for tokens, timing and identifiers", new StackPanel { Children = { _attempts, paging } }),
                Label("— means unknown. Cached and reasoning tokens are included in total tokens. Error percentages use attempts, including failed/canceled attempts and HTTP ≥400. Dates and times use the selected zone.", Muted)
            }
        };
        SizeChanged += (_, _) => _totals.Columns = Bounds.Width >= 850 ? 8 : 4;
        _apply.Click += async (_, _) => await ApplyAsync();
        _previous.Click += async (_, _) => await MovePageAsync(-1);
        _next.Click += async (_, _) => await MovePageAsync(1);
        _dailyMetric.SelectionChanged += (_, _) => { if (_summary is not null) RenderDaily(_summary.Daily); };
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
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
            if (!TryReadQuery(out var query)) return;
            _query = query;
            await RefreshAsync(token);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            while (await timer.WaitForNextTickAsync(token)) await RefreshAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private async Task ApplyAsync()
    {
        if (_lifetime is null || !TryReadQuery(out var query)) return;
        _query = query;
        await RefreshAsync(_lifetime.Token);
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
            Render(summary, await attemptsTask, query);
            var analysis = await analysisTask;
            RenderAnalysis(analysis.Value, analysis.Error);
            ShowError(null);
        }
        catch (OperationCanceledException) when (refresh.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (refresh.IsCancellationRequested || generation != _generation) return;
            _connection.Text = "Host unavailable · displayed snapshot may be stale";
            _connection.Foreground = Ui.Warning;
            ShowError($"Usage could not be refreshed: {ex.Message}");
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
        _summary = summary;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(summary.TimeZone);
        _connection.Text = "Connected · source: lain host / persistent usage API";
        _connection.Foreground = Accent;
        var age = DateTimeOffset.UtcNow - summary.ObservedAt;
        _freshness.Text = $"Observed {TimeZoneInfo.ConvertTime(summary.ObservedAt, zone):yyyy-MM-dd HH:mm:ss zzz} ({Math.Max(0, age.TotalSeconds):N0}s ago) · cursor {summary.Cursor:N0} · zone {summary.TimeZone}\nApplied range: {TimeZoneInfo.ConvertTime(summary.From, zone):yyyy-MM-dd HH:mm} → {TimeZoneInfo.ConvertTime(summary.To, zone):yyyy-MM-dd HH:mm} (exclusive)";
        _scope.Text = $"Applied filters: {(query.AccountId is { } accountId ? AccountName(accountId) : "all accounts")} · {query.Model ?? "all models"} · {(query.StatusCode is { } status ? $"HTTP {status}" : "all outcomes")}";
        _scope.Text += DimensionScope(query);
        _coverage.Text = summary.Complete
            ? $"Coverage: complete for selected range · {summary.Totals.UnknownUsageAttempts:N0} attempts with unknown token usage"
            : $"Coverage: incomplete — totals may omit activity · {summary.Totals.UnknownUsageAttempts:N0} attempts with unknown token usage";
        _coverage.Foreground = summary.Complete ? Muted : Ui.Warning;
        RenderTotals(summary.Totals);
        RenderDaily(summary.Daily);
        RenderGroups(_accounts, summary.Accounts, true);
        RenderGroups(_models, summary.Models, false);
        RenderAttempts(page, zone);
        RenderUnobservedAccounts(summary, query);
        // Never derive totals, group counts or token metrics from a page of attempts.
        _knownAccounts.UnionWith(summary.Accounts.Select(x => x.Key).Concat(_accountNames.Keys));
        _knownModels.UnionWith(summary.Models.Select(x => x.Key));
        // Polling must not discard unsubmitted filter selections.
        UpdateChoices(_account, "All accounts", _knownAccounts, (_account.SelectedItem as FilterChoice)?.Key, AccountName);
        UpdateChoices(_model, "All models", _knownModels, (_model.SelectedItem as FilterChoice)?.Key, x => x);
    }

    private static readonly string[] TotalTitles = { "Requests", "Attempts", "Errors", "Total tokens", "Input", "Output", "Cached", "Reasoning" };

    private void RenderEmptyTotals() => RenderStatStrip(Enumerable.Repeat("—", TotalTitles.Length).ToArray());

    private void RenderTotals(UsageTotals totals) => RenderStatStrip(
        new[]
        {
            Number(totals.Requests), Number(totals.Attempts), Number(totals.Errors), Number(totals.Tokens.Total),
            Number(totals.Tokens.Input), Number(totals.Tokens.Output), Number(totals.Tokens.Cached), Number(totals.Tokens.Reasoning)
        },
        new[] { "Client requests", "Upstream attempts", totals.Attempts > 0 ? $"{ErrorRate(totals)} of attempts" : "No attempts", "Reported total", "Reported input", "Reported output", "Included in total", "Included in total" });

    private void RenderStatStrip(string[] values, string[]? hints = null)
    {
        _totals.Children.Clear();
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
            _totals.Children.Add(new Border
            {
                BorderBrush = Ui.Edge, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(12, 9),
                Child = content
            });
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

    private void RenderGroups(StackPanel target, UsageGroup[] groups, bool accounts)
    {
        var focused = _groupButtons.FirstOrDefault(pair => pair.Key.Account == accounts && pair.Value.IsFocused).Key;
        foreach (var key in _groupButtons.Keys.Where(key => key.Account == accounts).ToArray()) _groupButtons.Remove(key);
        target.Children.Clear();
        target.Children.Add(GroupRow("Identity", "Requests", "Attempts", "Errors · %", "Tokens", "Unknown", true));
        if (groups.Length == 0) { target.Children.Add(Label("No activity in this range", Muted)); return; }
        foreach (var group in groups)
        {
            var identity = accounts ? AccountName(group.Key) : group.Key;
            if (accounts && identity != group.Key) identity += $"  ·  {group.Key}";
            var t = group.Totals;
            var expanded = _expandedGroups.Contains((accounts, group.Key));
            var toggle = DetailToggle(expanded, $"Details for {(accounts ? "account" : "model")} {group.Key}");
            var filter = new Button { Content = accounts ? "Filter to this account" : "Filter to this model", HorizontalAlignment = HorizontalAlignment.Left };
            Avalonia.Automation.AutomationProperties.SetName(filter, $"Filter to {(accounts ? "account" : "model")} {group.Key}");
            _groupButtons[(accounts, group.Key, false)] = toggle;
            _groupButtons[(accounts, group.Key, true)] = filter;
            var row = GroupCells(identity, Number(t.Requests), Number(t.Attempts), Errors(t), Number(t.Tokens.Total), Number(t.UnknownUsageAttempts));
            Put(row, toggle, 0);
            var details = new StackPanel
            {
                IsVisible = expanded, Spacing = 6, Margin = new Thickness(34, 5, 8, 10),
                Children =
                {
                    Label(TokenDetails(t.Tokens), Muted),
                    Label($"{ErrorRate(t)} error rate · {t.Errors:N0} of {t.Attempts:N0} attempts · {t.UnknownUsageAttempts:N0} with unknown usage or total", Muted),
                    filter
                }
            };
            toggle.Click += (_, _) =>
            {
                details.IsVisible = !details.IsVisible;
                toggle.Content = details.IsVisible ? "⌄" : "›";
                if (details.IsVisible) _expandedGroups.Add((accounts, group.Key)); else _expandedGroups.Remove((accounts, group.Key));
            };
            filter.Click += async (_, _) => await ApplyGroupAsync(group.Key, accounts);
            target.Children.Add(TableRow(new StackPanel { Children = { row, details } }));
        }
        if (focused.Key is not null && _groupButtons.TryGetValue(focused, out var focusedButton)) focusedButton.Focus();
    }

    private async Task ApplyGroupAsync(string key, bool account)
    {
        if (account) UpdateChoices(_account, "All accounts", _knownAccounts, key, AccountName);
        else UpdateChoices(_model, "All models", _knownModels, key, x => x);
        await ApplyAsync();
    }

    private static Border GroupRow(string identity, string requests, string attempts, string errors, string tokens, string unknown, bool header = false) =>
        TableRow(GroupCells(identity, requests, attempts, errors, tokens, unknown, header), header);

    private static Grid GroupCells(string identity, string requests, string attempts, string errors, string tokens, string unknown, bool header = false)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("28,2.5*,*,*,1.3*,1.2*,*"), MinHeight = 32 };
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
        _attemptToggles.Clear();
        _attempts.Children.Clear();
        _attempts.Children.Add(TableRow(AttemptCells("", "Time", "Account / model", "Status", "Tokens", "Duration", true), true));
        if (page.Items.Length == 0) _attempts.Children.Add(Label("No attempts match these filters", Muted));
        foreach (var attempt in page.Items)
        {
            var status = attempt.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? "—";
            var completion = attempt.CompletionState ?? attempt.State;
            var failed = attempt.State is "failed" or "canceled" || attempt.StatusCode >= 400;
            var expanded = _expandedAttempts.Contains(attempt.Id);
            var toggle = DetailToggle(expanded, $"Details for attempt {attempt.Id}");
            _attemptToggles[attempt.Id] = toggle;
            var row = AttemptCells("", TimeZoneInfo.ConvertTime(attempt.StartedAt, zone).ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                $"{AccountName(attempt.AccountId)} / {attempt.Model}", status, Number(attempt.Tokens.Total), Milliseconds(attempt.DurationMs));
            Put(row, toggle, 0);
            var statusCell = row.Children.OfType<TextBlock>().First(x => Grid.GetColumn(x) == 3);
            statusCell.Foreground = failed ? Ui.Warning : status == "—" ? Muted : Ui.Success;
            ToolTip.SetTip(statusCell, $"HTTP {status} · {completion}");
            var details = new StackPanel
            {
                IsVisible = expanded, Spacing = 4, Margin = new Thickness(32, 4, 8, 10),
                Children =
                {
                    Label($"{TimeZoneInfo.ConvertTime(attempt.StartedAt, zone):yyyy-MM-dd HH:mm:ss zzz} · HTTP {status} · {completion}", Muted),
                    Label($"{AccountName(attempt.AccountId)} · {attempt.AccountId} · {attempt.Provider} · {attempt.Model}" + (attempt.RouteId is null ? "" : $" · route {attempt.RouteId}"), Muted),
                    Label(TokenDetails(attempt.Tokens), Muted),
                    Label($"Duration {Milliseconds(attempt.DurationMs)} · first token {Milliseconds(attempt.FirstTokenMs)}", Muted),
                    Label($"Output / end-to-end: {OutputRate(attempt)} · reported output ÷ full attempt duration", Muted),
                    Label($"Request {attempt.RequestId} · attempt {attempt.Id}", Muted)
                }
            };
            if (attempt.Measurements is { } measured)
                details.Children.Add(Label($"Owned-core measurements: first output {Milliseconds(measured.FirstOutputLatencyMs)} · total {Milliseconds(measured.TotalDurationMs)} · source {measured.Source ?? "not supplied"}", Muted));
            if (attempt.Dimensions is { } dimensions)
            {
                var values = new (string Name, string? Value)[] { ("Family", dimensions.ModelFamily), ("Manufacturer", dimensions.Manufacturer),
                    ("Inference", dimensions.InferenceProvider), ("Gateway", dimensions.Gateway), ("Billing account", dimensions.BillingAccountId),
                    ("Requested reasoning", dimensions.ReasoningEffort), ("Effective reasoning", dimensions.EffectiveReasoningEffort), ("Mode", dimensions.Mode), ("Conversation", dimensions.ConversationId),
                    ("Turn", dimensions.TurnId), ("Worker", dimensions.WorkerId), ("Harness", dimensions.Harness) };
                var known = values.Where(x => x.Value is not null).Select(x => x.Name + ": " + x.Value).ToArray();
                if (known.Length > 0) details.Children.Add(Label(string.Join(" · ", known), Muted));
                if (dimensions.Evidence is { Length: > 0 } evidence)
                    details.Children.Add(new Expander { Header = "Attribution sources", Content = Label(string.Join("\n", evidence.Select(x => $"{x.Field} · {x.Source}" + (x.Key is null ? "" : " · " + x.Key))), Muted) });
            }
            if (!string.IsNullOrWhiteSpace(attempt.Error)) details.Children.Add(Label(attempt.Error, Ui.Warning));
            toggle.Click += (_, _) =>
            {
                details.IsVisible = !details.IsVisible;
                toggle.Content = details.IsVisible ? "⌄" : "›";
                if (details.IsVisible) _expandedAttempts.Add(attempt.Id); else _expandedAttempts.Remove(attempt.Id);
            };
            var entry = new StackPanel { Children = { row, details } };
            // Errors are visible without opening the detail row.
            if (!string.IsNullOrWhiteSpace(attempt.Error))
                entry.Children.Add(new Border { Padding = new Thickness(32, 0, 8, 6), Child = Cell(attempt.Error, Ui.Warning) });
            _attempts.Children.Add(TableRow(entry));
        }
        if (focusedAttempt is not null && _attemptToggles.TryGetValue(focusedAttempt, out var focusedToggle))
            focusedToggle.Focus();
        var first = page.Items.Length == 0 ? 0 : page.Offset + 1;
        _page.Text = $"{first:N0}–{page.Offset + page.Items.Length:N0} of {page.Total:N0}";
        _previous.IsEnabled = page.Offset > 0;
        _next.IsEnabled = (long)page.Offset + page.Items.Length < page.Total && page.Items.Length > 0;
    }

    private static Grid AttemptCells(string toggle, string time, string identity, string status, string tokens, string duration, bool header = false)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("28,1.25*,2.5*,.65*,.8*,1*"), MinHeight = 32 };
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
        combo.ItemsSource = choices;
        combo.SelectedItem = choices.FirstOrDefault(x => x.Key == selected) ?? choices[0];
    }

    private string AccountName(string id) => _accountNames.GetValueOrDefault(id, id);
    private static string Number(long? value) => value?.ToString("N0", CultureInfo.CurrentCulture) ?? "—";
    private static string Milliseconds(double? value)
    {
        if (value is not { } ms || !double.IsFinite(ms) || ms < 0) return "—";
        return ms < 1000 ? $"{ms:N0} ms" : ms < 60000 ? $"{ms / 1000:N1} s" : $"{ms / 60000:N1} min";
    }
    private static string ErrorRate(UsageTotals totals) => totals.Attempts > 0 ? $"{100d * totals.Errors / totals.Attempts:N1}%" : "—";
    private static string Errors(UsageTotals totals) => totals.Attempts > 0 ? $"{Number(totals.Errors)} · {ErrorRate(totals)}" : Number(totals.Errors);
    private static string TokenDetails(TokenCounts tokens) => $"Tokens: {Number(tokens.Total)} total · {Number(tokens.Input)} input · {Number(tokens.Output)} output · {Number(tokens.Cached)} cached · {Number(tokens.Reasoning)} reasoning";
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
    private static Border Section(string title, string description, Control body) => Panel(new StackPanel
    {
        Spacing = 8,
        Children =
        {
            new WrapPanel { Children = { new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Ui.Ink, Margin = new Thickness(0, 0, 12, 0), Classes = { "section-title" } }, Label(description, Muted) } },
            body
        }
    });
    private static void Put(Grid grid, Control control, int column) { Grid.SetColumn(control, column); grid.Children.Add(control); }
    private sealed record FilterChoice(string? Key, string Name) { public override string ToString() => Name; }
}
