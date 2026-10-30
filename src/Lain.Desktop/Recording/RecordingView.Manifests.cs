using Avalonia.Controls;
using Lain.Core;

namespace Lain.Desktop.Recording;

public sealed partial class RecordingView
{
    private string? _manifestLoadedFor;
    private async Task LoadManifestsAsync()
    {
        var id = _conversationId;
        var selection = _selectionLifetime;
        var generation = _generation;
        if (id is null || selection is null || _manifestLoadedFor == id) return;
        _manifestLoadedFor = id;
        try
        {
            var manifests = await _client.RecordingRunsAsync(id, selection.Token);
            if (selection.IsCancellationRequested || generation != _generation) return;
            RenderManifests(manifests);
            _variants.Children.Add(ManifestEditor(id, generation));
        }
        catch (Exception ex) { if (generation == _generation) { _manifestLoadedFor = null; Error(ex); } }
    }
    private Control ManifestEditor(string conversation, long generation)
    {
        var run = Ui.Input(hint: "Explicit run ID"); Name(run, "Manual run identity");
        var variant = Ui.Input(hint: "Variant label"); Name(variant, "Manual variant label");
        var revision = Ui.Input(hint: "Configured skill revision (optional)"); Name(revision, "Configured skill revision");
        var skillHash = Ui.Input(hint: "Configured skill hash (optional)"); Name(skillHash, "Configured skill hash");
        var promptHash = Ui.Input(hint: "Configured prompt hash (optional)"); Name(promptHash, "Configured prompt hash");
        var save = new Button { Content = "Save configured run label" };
        Name(save, "Save configured run label");
        save.Click += async (_, _) =>
        {
            var policy = _policy;
            var selection = _selectionLifetime;
            if (generation != _generation || conversation != _conversationId || policy is null || selection is null) return;
            if (string.IsNullOrWhiteSpace(run.Text) || string.IsNullOrWhiteSpace(variant.Text)) { _notice.Text = "Provide an explicit run ID and variant label."; return; }
            save.IsEnabled = false;
            try
            {
                var manifest = new RecordingRunManifest(run.Text.Trim(), variant.Text.Trim(), Optional(revision.Text), Optional(promptHash.Text), Optional(skillHash.Text), Evidence: "configured");
                await _client.LabelRecordingRunAsync(conversation, policy.Revision, manifest, selection.Token);
                if (generation != _generation || selection.IsCancellationRequested) return;
                _notice.Text = "Manual run label saved as configured provenance. Captured observations remain unchanged.";
                _manifestLoadedFor = null; await LoadManifestsAsync();
            }
            catch (Exception ex) { if (generation == _generation) Error(ex); }
            finally { save.IsEnabled = true; }
        };
        return new Expander { Header = "Add or edit configured run metadata…", Content = new StackPanel { Spacing = 6, Children = {
            Ui.Text("Manual metadata is configured provenance. It does not establish loaded skills, observed instructions or compliance.", 12, Ui.Muted), run, variant, revision, skillHash, promptHash, save } } };
    }
    private static string? Optional(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
