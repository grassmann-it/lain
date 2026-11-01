using Avalonia.Controls;
using Lain.Core;

namespace Lain.Desktop.Recording;

public sealed partial class RecordingView
{
    private Control ArchiveDeletionControls()
    {
        var confirmation = new CheckBox { Content = "Delete all retained recording conversations and payloads", IsChecked = false };
        SetName(confirmation, "Confirm deletion of all retained recording data");
        var remove = Ui.Button("Delete retained recording archive", async () =>
        {
            var lifetime = _lifetime;
            var policy = _policy;
            if (lifetime is null || policy is null || confirmation.IsChecked != true) return;
            confirmation.IsChecked = false;
            try
            {
                var result = await _client.ClearRecordingArchiveAsync(policy.Revision, lifetime.Token);
                if (_lifetime != lifetime || lifetime.IsCancellationRequested) return;
                if (result.Cleared)
                {
                    _generation++; _selectionLifetime?.Cancel(); _conversationId = null;
                    _timeline.Children.Clear(); _variants.Children.Clear(); _renderedRecords.Clear();
                    _list.Children.Clear(); _choices.Clear(); _summaries.Clear();
                    _notice.Text = "Retained recording archive deleted. Capture policy and inference are separate.";
                    _timelineCursor = null; _listCursor = null; _more.IsVisible = false; _moreTimeline.IsVisible = false;
                    ArrangeView(); await LoadListAsync(true);
                }
                else _notice.Text = $"Archive deletion was not applied ({result.ErrorCode ?? "conflict"}). Refresh before trying again.";
            }
            catch (Exception ex) { if (_lifetime == lifetime) Error(ex); }
            finally { confirmation.IsChecked = false; }
        });
        remove.IsEnabled = false;
        confirmation.IsCheckedChanged += (_, _) => remove.IsEnabled = confirmation.IsChecked == true;
        SetName(remove, "Delete retained recording archive");
        return new Expander { Header = "Archive deletion…", Content = new StackPanel { Spacing = 6, Children = {
            Ui.Text("This removes the saved recording archive. Turning capture off preserves history; pausing the display only affects this view. No control here stops inference.", 12, Ui.Muted), confirmation, remove } } };
    }
}
