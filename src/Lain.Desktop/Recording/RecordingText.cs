using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lain.Desktop.Recording;

/// <summary>One bounded layout page. Content is already decoded by the recording API.</summary>
internal sealed class RecordingText : StackPanel
{
    internal const int PageCharacters = 8192;
    internal const int SectionCharacters = 4096;
    private readonly string _text;
    private readonly StackPanel _sections = new() { Spacing = 4 };
    private readonly TextBlock _position = Ui.Text("", 11, Ui.Muted);
    private readonly Button _previous;
    private readonly Button _next;
    private int _start;
    private readonly Stack<int> _previousPages = new();
    public RecordingText(string text, string name)
    {
        _text = text;
        Spacing = 5;
        _previous = Ui.Button("Previous text page", () => { if (_previousPages.TryPop(out var previous)) _start = previous; Render(); });
        _next = Ui.Button("Next text page", () => { _previousPages.Push(_start); _start = End(_text, _start, PageCharacters); Render(); });
        AutomationProperties.SetName(this, name);
        var copy = Ui.Button("Copy visible text page", async () =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null) await clipboard.SetTextAsync(VisibleText);
        });
        AutomationProperties.SetName(copy, "Copy visible text page");
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, Children = { _previous, _next, copy } };
        Children.Add(_position); Children.Add(actions); Children.Add(_sections);
        Render();
    }
    internal string VisibleText => _text[_start..End(_text, _start, PageCharacters)];
    private static int End(string text, int start, int limit)
    {
        var end = Math.Min(text.Length, start + limit);
        if (end < text.Length && end > start && char.IsHighSurrogate(text[end - 1]) && char.IsLowSurrogate(text[end])) end--;
        return end;
    }
    private void Render()
    {
        _sections.Children.Clear();
        var visible = VisibleText;
        for (var offset = 0; offset < visible.Length;)
        {
            var end = End(visible, offset, SectionCharacters);
            _sections.Children.Add(new SelectableTextBlock
            {
                Text = visible[offset..end],
                Foreground = Ui.Ink, FontSize = 13, TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Stretch
            });
            offset = end;
        }
        _previous.IsEnabled = _start > 0;
        _next.IsEnabled = _start + visible.Length < _text.Length;
        _position.Text = _text.Length == 0 ? "No retained text" : $"Characters {_start + 1}–{_start + visible.Length} of {_text.Length}. Copy includes this visible page only.";
        _position.IsVisible = _text.Length > PageCharacters;
    }
}
