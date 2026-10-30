using Avalonia;
using Avalonia.Controls;
using Lain.Core;

namespace Lain.Desktop.Recording;

public sealed partial class RecordingView
{
    private async Task RefreshStatusAsync()
    {
        var lifetime = _lifetime;
        if (lifetime is null) return;
        try
        {
            var health = await _client.RecordingStatusAsync(lifetime.Token);
            if (_lifetime != lifetime || lifetime.IsCancellationRequested) return;
            _policy = health.Policy;
            if (_consent.IsChecked != true) _mode.SelectedIndex = health.Policy.Mode == RecordingContract.Content ? 1 : 0;
            var scope = health.Policy.RouteIds is { Length: > 0 } routes ? $"Only configured routes: {string.Join(", ", routes)}" : "All models used through the owned gateway; no model-family or training-rights veto";
            _scope.Text = $"Scope: {scope}. Content mode: {health.Policy.Mode}. Recording revision: {health.Policy.Revision}.";
            _status.Text = $"Recording {(health.Policy.Enabled ? "enabled" : "off")} · {health.State} · archive {health.Storage?.State ?? "unknown"} · {health.Storage?.RecordCount ?? 0} committed records · {health.DroppedRecords} dropped{(health.UnknownLoss ? " · unknown loss interval" : "")}";
            if (health.Coverage is { Length: > 0 }) _status.Text += $" · coverage: {string.Join(", ", health.Coverage)}";
            if (health.ErrorCode is not null) _notice.Text = $"Capture health: {health.ErrorCode}. Committed history remains available independently.";
            UpdateControls();
        }
        catch (Exception ex) { if (_lifetime == lifetime) { _status.Text = "Recording source unavailable; use retained history below."; Error(ex); } }
    }
    private async Task ChangeCaptureAsync(bool enabled)
    {
        var lifetime = _lifetime;
        var policy = _policy;
        if (lifetime is null || policy is null || (enabled && _consent.IsChecked != true)) return;
        _enable.IsEnabled = false; _stop.IsEnabled = false;
        try
        {
            var result = await _client.UpdateRecordingPolicyAsync(new RecordingPolicyUpdate(policy.Revision, enabled,
                enabled ? (_mode.SelectedIndex == 1 ? RecordingContract.Content : RecordingContract.MetadataOnly) : policy.Mode,
                policy.Scope, enabled ? null : policy.RouteIds, policy.Limits), lifetime.Token);
            if (_lifetime != lifetime || lifetime.IsCancellationRequested) return;
            _policy = result;
            _notice.Text = enabled ? "Recording explicitly enabled. Capture includes all used models through this gateway." : "Capture is off. Pending uncommitted observations are discarded; retained history and inference are independent.";
            _consent.IsChecked = false;
            await RefreshStatusAsync();
        }
        catch (Exception ex) { if (_lifetime == lifetime) { Error(ex); await RefreshStatusAsync(); } }
        finally { if (_lifetime == lifetime) UpdateControls(); }
    }
    private long _listGeneration;
    private async Task LoadListAsync(bool reset)
    {
        var lifetime = _lifetime;
        if (lifetime is null) return;
        if (_loadingList && !reset) return;
        var generation = ++_listGeneration;
        var cursor = reset ? null : _listCursor;
        var filter = _modelFilter;
        _loadingList = true; _more.IsEnabled = false;
        try
        {
            var page = await _client.RecordingConversationsAsync(new RecordingConversationQuery(25, cursor, Model: filter), lifetime.Token);
            if (_lifetime != lifetime || lifetime.IsCancellationRequested || generation != _listGeneration) return;
            if (page.ErrorCode is not null) { _notice.Text = $"Conversation page unavailable ({page.ErrorCode}). Refresh the list."; return; }
            if (reset) { _list.Children.Clear(); _choices.Clear(); _summaries.Clear(); }
            foreach (var item in page.Items) AddConversation(item);
            if (reset && page.Items.Length == 0) _list.Children.Add(Ui.Text("No retained conversations match this filter. Recording is off by default; source outages do not erase the archive.", 12, Ui.Muted));
            _listCursor = page.NextCursor; _more.IsVisible = page.HasMore && page.NextCursor is not null;
        }
        catch (Exception ex) { if (_lifetime == lifetime && generation == _listGeneration) Error(ex); }
        finally { if (generation == _listGeneration) { _loadingList = false; _more.IsEnabled = true; } }
    }
    private async Task SelectConversationAsync(string id)
    {
        _generation++;
        _selectionLifetime?.Cancel(); _selectionLifetime?.Dispose();
        if (_lifetime is null) return;
        _selectionLifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _conversationId = id; _timelineCursor = null; _loadingTimeline = false;
        _timeline.Children.Clear(); _renderedRecords.Clear(); _variants.Children.Clear(); _manifestLoadedFor = null;
        _selected.Text = $"Conversation {id}"; _notice.Text = "";
        _scroll.Offset = default; ArrangeView();
        await LoadTimelineAsync(true);
    }
    private async Task LoadTimelineAsync(bool first)
    {
        var selection = _selectionLifetime;
        var id = _conversationId;
        var generation = _generation;
        if (selection is null || id is null || _loadingTimeline || (!first && _timelineCursor is null)) return;
        _loadingTimeline = true; _moreTimeline.IsEnabled = false;
        var offset = _scroll.Offset;
        try
        {
            var page = await _client.RecordingConversationAsync(id, first ? null : _timelineCursor, 25, selection.Token);
            if (selection.IsCancellationRequested || generation != _generation || id != _conversationId) return;
            _selected.Text = $"{page.Conversation.Title ?? page.Conversation.Id} · {page.Conversation.State} · {(page.Conversation.Complete ? "complete within captured coverage" : "partial / coverage unknown")}";
            if (page.GapReason is not null) _notice.Text = $"Archive gap: {page.GapReason}";
            foreach (var record in page.Records) AppendRecord(record);
            if (first && _variantPanel.IsExpanded) await LoadManifestsAsync();
            _timelineCursor = page.NextCursor;
            _moreTimeline.IsVisible = page.HasMore && page.NextCursor is not null;
            _adjustingScroll = true;
            try { if (_follow.IsChecked == true) _scroll.ScrollToEnd(); else _scroll.Offset = offset; }
            finally { _adjustingScroll = false; }
        }
        catch (Exception ex)
        {
            if (generation != _generation) return;
            if (ex is Lain.Client.LainApiException api && api.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone)
            {
                // Retention/deletion invalidates cached content, including text available for copy.
                _timeline.Children.Clear(); _renderedRecords.Clear(); _variants.Children.Clear(); _manifestLoadedFor = null;
                _notice.Text = "This conversation was deleted or expired. Visible retained content has been cleared.";
                _moreTimeline.IsVisible = false; _timelineCursor = null;
            }
            else Error(ex);
        }
        finally { if (generation == _generation) { _loadingTimeline = false; _moreTimeline.IsEnabled = true; } }
    }
}
