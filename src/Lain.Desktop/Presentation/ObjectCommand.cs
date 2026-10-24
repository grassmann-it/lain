using Avalonia.Controls;

namespace Lain.Desktop.Presentation;

/// <summary>One frozen object action shared by visible, pointer and keyboard entry points.</summary>
internal sealed record ObjectCommand(string Key, string Label, Action Execute, bool Available = true, string? Reason = null);

internal static class ObjectCommands
{
    public static ContextMenu Menu(IEnumerable<ObjectCommand> commands)
    {
        var menu = new ContextMenu();
        foreach (var command in commands)
        {
            var item = new MenuItem { Header = command.Label, IsEnabled = command.Available };
            if (!command.Available) ToolTip.SetTip(item, command.Reason ?? "Unavailable in this host mode.");
            item.Click += (_, _) => command.Execute();
            menu.Items.Add(item);
        }
        return menu;
    }
}
