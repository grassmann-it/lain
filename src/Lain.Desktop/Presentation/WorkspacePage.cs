using Avalonia.Controls;
using Avalonia.Layout;

namespace Lain.Desktop.Presentation;

/// <summary>A bounded page owns its scrolling. Ordinary editor controls retain the shell scroll surface.</summary>
internal sealed class WorkspacePage : ContentControl
{
    public WorkspacePage(Control content)
    {
        Content = content;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
    }
}
