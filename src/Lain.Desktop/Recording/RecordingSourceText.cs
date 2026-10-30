using Avalonia.Controls;
using Avalonia.Input.Platform;
using Lain.Client;
using Lain.Core;

namespace Lain.Desktop.Recording;

/// <summary>Explicit selected-source reads only. The backend returns bounded retained/sanitized JSON pages.</summary>
internal sealed class RecordingSourceText : UserControl
{
    private readonly LainApiClient client;
    private readonly RecordingSourceReference source;
    private readonly TextBlock status = Ui.Text("Retained API body is loaded only on request.", 11, Ui.Muted);
    private readonly SelectableTextBlock text = new() { FontSize = 11, Foreground = Ui.Ink, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly Button load = new() { Content = "Open retained API body" };
    private readonly Button previous = new() { Content = "Previous API page", IsEnabled = false };
    private readonly Button next = new() { Content = "Next API page", IsEnabled = false };
    private readonly Button copy = new() { Content = "Copy this API page", IsEnabled = false };
    private CancellationTokenSource? operation;
    private RecordingSourceTextPage? page;
    private long generation;
    public RecordingSourceText(LainApiClient client, RecordingSourceReference source)
    {
        this.client = client; this.source = source;
        Content = new StackPanel { Spacing = 4, Children = { load, status, new WrapPanel { Children = { previous, next, copy } }, text } };
        load.Click += (_, _) => _ = LoadAsync(0);
        previous.Click += (_, _) => _ = LoadAsync(Math.Max(0, (page?.Offset ?? 0) - 8192));
        next.Click += (_, _) => { if (page is { HasMore: true }) _ = LoadAsync(page.Offset + (page.Text?.Length ?? 0)); };
        copy.Click += async (_, _) => { if (page?.Text is { } body && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(body); };
        DetachedFromVisualTree += (_, _) => { generation++; operation?.Cancel(); operation?.Dispose(); operation = null; page = null; text.Text = null; copy.IsEnabled = false; };
    }
    private async Task LoadAsync(int offset)
    {
        var current = ++generation; operation?.Cancel(); operation?.Dispose(); operation = new();
        load.IsEnabled = previous.IsEnabled = next.IsEnabled = copy.IsEnabled = false;
        status.Text = "Reading selected retained API source…";
        try
        {
            var result = await client.RecordingSourceTextAsync(source, offset, ct: operation.Token);
            if (generation != current) return;
            page = result; text.Text = result.Text;
            status.Text = $"{result.Boundary} · {result.Protocol} · {result.RedactionState} · characters {result.Offset + 1:N0}–{result.Offset + (result.Text?.Length ?? 0):N0} of {result.TotalCharacters:N0}"
                + (result.GapReason is null ? " · retained transformed JSON, not original wire bytes" : " · " + result.GapReason);
            previous.IsEnabled = result.Offset > 0; next.IsEnabled = result.HasMore; copy.IsEnabled = result.Text is not null;
        }
        catch (OperationCanceledException) { }
        catch { if (generation == current) { page = null; text.Text = null; status.Text = "The retained source is unavailable or expired."; } }
        finally { if (generation == current) load.IsEnabled = true; }
    }
}
