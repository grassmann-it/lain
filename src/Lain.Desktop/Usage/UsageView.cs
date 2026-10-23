using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Lain.Client;
using Lain.Core;

namespace Lain.Desktop.Usage;

/// <summary>Read-only projection of host aggregates. Collection continues in the host.</summary>
public sealed class UsageView : UserControl
{
    private static readonly IBrush Muted = Brush.Parse("#96aab3");
    private static readonly IBrush Accent = Brush.Parse("#78dbcf");
    private static readonly IBrush Card = Brush.Parse("#121b23");
    private readonly LainApiClient _client;
    private readonly TextBox _from = new() { Width = 128, Watermark = "yyyy-MM-dd" };
    private readonly TextBox _to = new() { Width = 128, Watermark = "yyyy-MM-dd" };
    private readonly TextBox _zone = new() { Width = 180 };
    private readonly ComboBox _account = new() { Width = 170 };
    private readonly ComboBox _model = new() { Width = 190 };
    private readonly TextBox _status = new() { Width = 90, Watermark = "Any" };
    private readonly TextBlock _connection = Label("Waiting for usage service", Muted);
    private readonly TextBlock _freshness = Label("No snapshot received", Muted);
    private readonly TextBlock _coverage = Label("Coverage unknown until a snapshot is received", Muted);
    private readonly TextBlock _error = Label("", Brush.Parse("#ffbd91"));
    private readonly TextBlock _filterError = Label("", Brush.Parse("#ffbd91"));
    private readonly WrapPanel _totals = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _daily = new() { Spacing = 8 };
    private readonly StackPanel _accounts = new() { Spacing = 4 };
    private readonly StackPanel _models = new() { Spacing = 4 };
    private readonly StackPanel _attempts = new() { Spacing = 5 };
    private readonly TextBlock _page = Label("No attempts loaded", Muted);
    private readonly Button _previous = new() { Content = "← Previous", IsEnabled = false };
    private readonly Button _next = new() { Content = "Next →", IsEnabled = false };
    private readonly Button _apply = new() { Content = "Apply filters" };
    private CancellationTokenSource? _lifetime;
    private CancellationTokenSource? _refresh;
    private UsageQuery _query = new(Limit: 25);
    private readonly Dictionary<string, string> _accountNames = new();
    private readonly HashSet<string> _knownAccounts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _knownModels = new(StringComparer.Ordinal);
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
        filters.Children.Add(Field("From (inclusive)", _from));
        filters.Children.Add(Field("To (exclusive)", _to));
        filters.Children.Add(Field("Time zone", _zone));
        filters.Children.Add(Field("Account", _account));
        filters.Children.Add(Field("Actual model", _model));
        filters.Children.Add(Field("HTTP status", _status));
        filters.Children.Add(new StackPanel { Margin = new Thickness(0, 21, 12, 8), Children = { _apply } });

        var paging = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { _previous, _page, _next } };
        _page.VerticalAlignment = VerticalAlignment.Center;
        var content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 18,
            Children =
            {
                new TextBlock { Text = "Usage", FontSize = 29, FontWeight = FontWeight.Bold },
                Label("Persistent activity across your accounts · refreshes every 5 seconds while open", Muted),
                filters,
                Label("Dates use the selected time zone. Status filters match an exact HTTP code; leave blank for all outcomes.", Muted),
                _filterError,
                _error,
                Panel(new StackPanel { Spacing = 6, Children = { _connection, _freshness, _coverage } }),
                _totals,
                Section("Daily activity", "Host totals per calendar day · cyan bars show attempts", _daily),
                Section("By account", "Historical account identities are retained", _accounts),
                Section("By actual model", "Requests and upstream attempts remain separate", _models),
                Section("Attempts & errors", "25 per page · times shown in the selected zone · timings only when reported", _attempts),
                paging,
                Label("Unknown token values are shown as —. Cached and reasoning tokens are reported subsets, never added to total.", Muted)
            }
        };
        Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        _apply.Click += async (_, _) => await ApplyAsync();
        _previous.Click += async (_, _) => await MovePageAsync(-1);
        _next.Click += async (_, _) => await MovePageAsync(1);
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
            int? status = null;
            if (!string.IsNullOrWhiteSpace(_status.Text))
            {
                if (!int.TryParse(_status.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value is < 100 or > 599)
                    throw new FormatException("Enter an HTTP status between 100 and 599, or leave it blank.");
                status = value;
            }
            query = new UsageQuery(from, to, zone.Id, (_account.SelectedItem as FilterChoice)?.Key,
                (_model.SelectedItem as FilterChoice)?.Key, status, 0, 25);
            _filterError.Text = "";
            _filterError.IsVisible = false;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            _filterError.Text = ex is TimeZoneNotFoundException ? "Unknown time zone. Use an IANA name such as Europe/Berlin or UTC." : ex.Message;
            _filterError.IsVisible = true;
            return false;
        }
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
            await Task.WhenAll(summaryTask, attemptsTask, namesTask);
            if (refresh.IsCancellationRequested || generation != _generation) return;
            var summary = await summaryTask;
            if (summary.ContractVersion != Api.Version)
                throw new InvalidOperationException($"Usage contract {summary.ContractVersion} is unsupported; expected {Api.Version}.");
            Render(summary, await attemptsTask, query);
            ShowError(null);
        }
        catch (OperationCanceledException) when (refresh.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (refresh.IsCancellationRequested || generation != _generation) return;
            _connection.Text = "Host unavailable · displayed snapshot may be stale";
            _connection.Foreground = Brush.Parse("#ffbd91");
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
            foreach (var account in accounts) _accountNames[account.Id] = account.Name;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { /* Usage remains available when account configuration cannot be read. */ }
    }

    private void Render(UsageSummaryDto summary, PageDto<AttemptDto> page, UsageQuery query)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(summary.TimeZone);
        _connection.Text = "Connected · source: lain host / persistent usage API";
        _connection.Foreground = Accent;
        var age = DateTimeOffset.UtcNow - summary.ObservedAt;
        _freshness.Text = $"Observed {TimeZoneInfo.ConvertTime(summary.ObservedAt, zone):yyyy-MM-dd HH:mm:ss zzz} ({Math.Max(0, age.TotalSeconds):N0}s ago) · cursor {summary.Cursor:N0} · zone {summary.TimeZone}\nApplied range: {TimeZoneInfo.ConvertTime(summary.From, zone):yyyy-MM-dd HH:mm} → {TimeZoneInfo.ConvertTime(summary.To, zone):yyyy-MM-dd HH:mm} (exclusive)";
        _coverage.Text = summary.Complete
            ? $"Coverage: complete for selected range · {summary.Totals.UnknownUsageAttempts:N0} attempts with unknown token usage"
            : $"Coverage: incomplete — totals may omit activity · {summary.Totals.UnknownUsageAttempts:N0} attempts with unknown token usage";
        _coverage.Foreground = summary.Complete ? Muted : Brush.Parse("#ffbd91");
        RenderTotals(summary.Totals);
        RenderDaily(summary.Daily);
        RenderGroups(_accounts, summary.Accounts, true);
        RenderGroups(_models, summary.Models, false);
        RenderAttempts(page, zone);
        // Never derive totals, group counts or token metrics from a page of attempts.
        _knownAccounts.UnionWith(summary.Accounts.Select(x => x.Key).Concat(_accountNames.Keys));
        _knownModels.UnionWith(summary.Models.Select(x => x.Key));
        // Polling must not discard unsubmitted filter selections.
        UpdateChoices(_account, "All accounts", _knownAccounts, (_account.SelectedItem as FilterChoice)?.Key, AccountName);
        UpdateChoices(_model, "All models", _knownModels, (_model.SelectedItem as FilterChoice)?.Key, x => x);
    }

    private void RenderTotals(UsageTotals totals)
    {
        _totals.Children.Clear();
        foreach (var (title, value) in new[]
        {
            ("Requests", Number(totals.Requests)), ("Attempts", Number(totals.Attempts)),
            ("Errors", Number(totals.Errors)), ("Total tokens", Number(totals.Tokens.Total)),
            ("Input", Number(totals.Tokens.Input)), ("Output", Number(totals.Tokens.Output)),
            ("Cached", Number(totals.Tokens.Cached)), ("Reasoning", Number(totals.Tokens.Reasoning))
        })
            _totals.Children.Add(new Border
            {
                Background = Card, CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 12),
                Margin = new Thickness(0, 0, 10, 10), MinWidth = 125,
                Child = new StackPanel { Spacing = 4, Children = { Label(title, Muted), new TextBlock { Text = value, FontSize = 25, Foreground = Accent } } }
            });
    }

    private void RenderDaily(UsageGroup[] days)
    {
        _daily.Children.Clear();
        if (days.Length == 0) { _daily.Children.Add(Label("No activity in this range", Muted)); return; }
        var max = days.Max(x => x.Totals.Attempts);
        foreach (var day in days)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*,225") };
            Put(row, Label(day.Key, Muted), 0);
            var track = new Grid { Height = 18, Margin = new Thickness(4, 4, 18, 4), Background = Brush.Parse("#22323d") };
            var ratio = max == 0 ? 0 : Math.Clamp((double)day.Totals.Attempts / max, 0, 1);
            track.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(ratio, GridUnitType.Star)));
            track.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1 - ratio, GridUnitType.Star)));
            track.Children.Add(new Border { Background = Accent, IsVisible = ratio > 0 });
            Put(row, track, 1);
            Put(row, Label($"{day.Totals.Attempts:N0} attempts · {Number(day.Totals.Tokens.Total)} tokens", Muted), 2);
            _daily.Children.Add(row);
        }
    }

    private void RenderGroups(StackPanel target, UsageGroup[] groups, bool accounts)
    {
        target.Children.Clear();
        target.Children.Add(GroupRow("Identity", "Requests", "Attempts", "Errors", "Tokens", "Unknown", true));
        if (groups.Length == 0) { target.Children.Add(Label("No activity in this range", Muted)); return; }
        foreach (var group in groups)
        {
            var identity = accounts ? AccountName(group.Key) : group.Key;
            if (accounts && identity != group.Key) identity += $"  ·  {group.Key}";
            var t = group.Totals;
            target.Children.Add(GroupRow(identity, Number(t.Requests), Number(t.Attempts), Number(t.Errors), Number(t.Tokens.Total), Number(t.UnknownUsageAttempts)));
        }
    }

    private static Grid GroupRow(string identity, string requests, string attempts, string errors, string tokens, string unknown, bool header = false)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("2.5*,*,*,*,1.2*,*"), Margin = new Thickness(0, 4) };
        var values = new[] { identity, requests, attempts, errors, tokens, unknown };
        for (var i = 0; i < values.Length; i++)
        {
            var label = Label(values[i], header ? Muted : Brushes.White);
            label.Margin = new Thickness(0, 0, 12, 0);
            if (header) label.FontWeight = FontWeight.SemiBold;
            Put(grid, label, i);
        }
        return grid;
    }

    private void RenderAttempts(PageDto<AttemptDto> page, TimeZoneInfo zone)
    {
        _attempts.Children.Clear();
        if (page.Items.Length == 0) _attempts.Children.Add(Label("No attempts match these filters", Muted));
        foreach (var attempt in page.Items)
        {
            var status = attempt.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? "unknown HTTP status";
            var failed = attempt.StatusCode >= 400 || !string.IsNullOrEmpty(attempt.Error);
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            Put(header, Label($"{TimeZoneInfo.ConvertTime(attempt.StartedAt, zone):yyyy-MM-dd HH:mm:ss zzz}  ·  {attempt.Model}", Brushes.White), 0);
            Put(header, Label($"{status} · {attempt.State}", failed ? Brush.Parse("#ffbd91") : Accent), 1);
            var details = new StackPanel
            {
                Spacing = 5,
                Children =
                {
                    header,
                    Label($"{AccountName(attempt.AccountId)} · {attempt.AccountId} · {attempt.Provider}" + (attempt.RouteId is null ? "" : $" · route {attempt.RouteId}"), Muted),
                    Label($"Tokens: {Number(attempt.Tokens.Total)} total · {Number(attempt.Tokens.Input)} input · {Number(attempt.Tokens.Output)} output · {Number(attempt.Tokens.Cached)} cached · {Number(attempt.Tokens.Reasoning)} reasoning", Muted),
                    Label($"Duration {Milliseconds(attempt.DurationMs)} · first token {Milliseconds(attempt.FirstTokenMs)}", Muted),
                    Label($"Request {attempt.RequestId} · attempt {attempt.Id}", Muted)
                }
            };
            if (!string.IsNullOrWhiteSpace(attempt.Error)) details.Children.Add(Label(attempt.Error, Brush.Parse("#ffbd91")));
            _attempts.Children.Add(Panel(details));
        }
        var first = page.Items.Length == 0 ? 0 : page.Offset + 1;
        _page.Text = $"{first:N0}–{page.Offset + page.Items.Length:N0} of {page.Total:N0}";
        _previous.IsEnabled = page.Offset > 0;
        _next.IsEnabled = (long)page.Offset + page.Items.Length < page.Total && page.Items.Length > 0;
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
    private static string Milliseconds(double? value) => value is { } ms && double.IsFinite(ms) && ms >= 0 ? $"{ms:N0} ms" : "—";
    private void ShowError(string? message) { _error.Text = message ?? ""; _error.IsVisible = message is not null; }
    private static TextBlock Label(string text, IBrush? brush = null) => new() { Text = text, Foreground = brush, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    private static Border Panel(Control child) => new() { Background = Card, CornerRadius = new CornerRadius(8), Padding = new Thickness(16), Child = child };
    private static StackPanel Field(string title, Control control) => new() { Spacing = 5, Margin = new Thickness(0, 0, 12, 8), Children = { Label(title, Muted), control } };
    private static Border Section(string title, string description, Control body) => Panel(new StackPanel
    {
        Spacing = 12,
        Children = { new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeight.SemiBold }, Label(description, Muted), body }
    });
    private static void Put(Grid grid, Control control, int column) { Grid.SetColumn(control, column); grid.Children.Add(control); }
    private sealed record FilterChoice(string? Key, string Name) { public override string ToString() => Name; }
}
