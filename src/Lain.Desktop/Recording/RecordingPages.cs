using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Lain.Desktop.Recording;

/// <summary>Builds only the selected content page; leaving a page unloads its controls and copy surfaces.</summary>
internal sealed class RecordingPages<T> : UserControl
{
    public RecordingPages(IReadOnlyList<T> items, int pageSize, string noun, string identity, Func<T, Control> render)
    {
        var offset = 0;
        var body = new StackPanel { Spacing = 6 };
        var count = Ui.Text("", 11, Ui.Muted);
        var previous = new Button { Content = $"Previous {noun} page" };
        var next = new Button { Content = $"Next {noun} page" };
        AutomationProperties.SetName(previous, $"Previous {noun} page {identity}");
        AutomationProperties.SetName(next, $"Next {noun} page {identity}");
        var first = new Button { Content = $"First {noun} page" };
        var last = new Button { Content = $"Last {noun} page" };
        AutomationProperties.SetName(first, $"First {noun} page {identity}");
        AutomationProperties.SetName(last, $"Last {noun} page {identity}");
        var navigation = new WrapPanel { Orientation = Orientation.Horizontal, Children = { first, previous, next, last } };
        navigation.IsVisible = items.Count > pageSize;
        Content = new StackPanel { Spacing = 5, Children = { count, navigation, body } };
        void Render()
        {
            // Removed controls no longer offer text selection or copy for off-page content.
            body.Children.Clear();
            var end = Math.Min(items.Count, offset + pageSize);
            for (var index = offset; index < end; index++) body.Children.Add(render(items[index]));
            count.Text = items.Count == 0 ? $"No retained {noun}" : $"{noun}: {offset + 1}–{end} of {items.Count} retained · only this page is displayed";
            first.IsEnabled = previous.IsEnabled = offset > 0;
            last.IsEnabled = next.IsEnabled = end < items.Count;
        }
        first.Click += (_, _) => { offset = 0; Render(); };
        last.Click += (_, _) => { offset = Math.Max(0, ((items.Count - 1) / pageSize) * pageSize); Render(); };
        previous.Click += (_, _) => { offset = Math.Max(0, offset - pageSize); Render(); };
        next.Click += (_, _) => { if (offset + pageSize < items.Count) offset += pageSize; Render(); };
        Render();
    }
}
