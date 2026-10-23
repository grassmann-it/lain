using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;

namespace Lain.Desktop;

internal static class Ui
{
    // Verified grassmann-it.de palette; see docs/brand-reference.md.
    public static readonly FontFamily HeadingFont = new("avares://Lain.Desktop/Assets/Fonts/IBMPlexSansCondensed#IBM Plex Sans Condensed");
    public static readonly FontFamily BrandFont = new("avares://Lain.Desktop/Assets/Fonts/Oxanium#Oxanium");
    public static readonly FontFamily NavigationFont = BrandFont;

    public static readonly IBrush Background = Brush.Parse("#050706");
    public static readonly IBrush Surface = Brush.Parse("#08100D");
    public static readonly IBrush Raised = Brush.Parse("#0C1511");
    public static readonly IBrush InputSurface = Brush.Parse("#0C1511");
    public static readonly IBrush Hover = Brush.Parse("#10281F");
    public static readonly IBrush Edge = Brush.Parse("#30493E");
    public static readonly IBrush Ink = Brush.Parse("#EDF2EF");
    public static readonly IBrush Muted = Brush.Parse("#89938E");
    public static readonly IBrush Accent = Brush.Parse("#73D7AE");
    public static readonly IBrush AccentSurface = Brush.Parse("#10281F");
    public static readonly IBrush Success = Brush.Parse("#9BCF9C");
    public static readonly IBrush Warning = Brush.Parse("#D9AF65");
    public static readonly IBrush Danger = Brush.Parse("#EA8087");

    public static Styles CreateStyles() => new()
    {
        new StyleInclude(new Uri("avares://Lain.Desktop/"))
        {
            Source = new Uri("avares://Lain.Desktop/Design/Workspace.axaml")
        }
    };

    public static TextBlock Text(string text, double size = 13, IBrush? color = null) => new() { Text = text, FontSize = size, Foreground = color ?? Ink, TextWrapping = TextWrapping.Wrap };
    public static TextBlock Title(string text) => new() { Text = text, FontFamily = HeadingFont, FontSize = 22, FontWeight = FontWeight.SemiBold, Foreground = Ink, TextWrapping = TextWrapping.Wrap };
    public static StackPanel Stack(params Control[] controls) { var panel = new StackPanel { Spacing = 12 }; foreach (var child in controls) panel.Children.Add(child); return panel; }
    public static StackPanel Row(params Control[] controls) { var panel = new StackPanel { Spacing = 8, Orientation = Orientation.Horizontal }; foreach (var child in controls) panel.Children.Add(child); return panel; }
    public static Border Card(Control content) => new() { Background = Surface, BorderBrush = Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(14), Child = content };
    public static Button Button(string label, Action click) { var b = new Button { Content = label }; b.Click += (_, _) => click(); return b; }
    public static TextBox Input(string? value = null, string? hint = null) => new() { Text = value ?? "", PlaceholderText = hint, HorizontalAlignment = HorizontalAlignment.Stretch };
    public static Control Field(string label, Control input)
    {
        AutomationProperties.SetName(input, label);
        return new StackPanel { Spacing = 5, Children = { Text(label, 12, Muted), input } };
    }

    public static Button PrimaryButton(string label, Action click)
    {
        var button = Button(label, click);
        button.Classes.Add("primary");
        return button;
    }

    public static Control PageHeader(string title, string? subtitle = null, params Control[] actions)
    {
        var heading = new StackPanel { Spacing = 5, Children = { Title(title) } };
        if (!string.IsNullOrWhiteSpace(subtitle)) heading.Children.Add(Text(subtitle, 13, Muted));
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 18 };
        header.Children.Add(heading);
        if (actions.Length > 0)
        {
            var toolbar = Row(actions);
            toolbar.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(toolbar, 1);
            header.Children.Add(toolbar);
        }
        return header;
    }

    public static Control Section(string title, Control content, string? description = null)
    {
        var heading = Text(title, 14);
        heading.FontWeight = FontWeight.SemiBold;
        heading.FontFamily = HeadingFont;
        var panel = new StackPanel { Spacing = 8, Children = { heading } };
        if (!string.IsNullOrWhiteSpace(description)) panel.Children.Add(Text(description, 12, Muted));
        panel.Children.Add(Separator());
        panel.Children.Add(content);
        return panel;
    }

    public static Ursa.Controls.Divider Separator() => new()
    {
        Margin = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch,
        Foreground = Edge
    };

    public static Border Badge(string text, string? tone = null)
    {
        var foreground = tone?.ToLowerInvariant() switch
        {
            "success" or "ready" => Success,
            "warning" or "stale" => Warning,
            "danger" or "error" => Danger,
            "accent" or "info" => Accent,
            _ => Muted
        };
        return new Border
        {
            Background = Raised, BorderBrush = Edge, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 3),
            VerticalAlignment = VerticalAlignment.Center, Child = Text(text, 11, foreground)
        };
    }

    public static Button IconButton(string name, string tooltip, Action click)
    {
        var button = new Ursa.Controls.IconButton { Icon = Icon(name), Width = 32, Height = 32, Padding = new Thickness(7) };
        button.Click += (_, _) => click();
        button.Classes.Add("toolbar-icon");
        ToolTip.SetTip(button, tooltip);
        AutomationProperties.SetName(button, tooltip);
        return button;
    }

    /// <summary>Original line icons; filled geometry inherits the control's foreground.</summary>
    public static PathIcon Icon(string name, double size = 16)
    {
        var path = name.ToLowerInvariant() switch
        {
            "overview" or "dashboard" or "home" => "M3 3H10V10H3Z M14 3H21V10H14Z M3 14H10V21H3Z M14 14H21V21H14Z",
            "accounts" or "user" => "M16 7A4 4 0 1 1 8 7A4 4 0 1 1 16 7 M4 21V18C4 14 20 14 20 18V21",
            "routes" or "network" => "M9 3H15V9H9Z M2 16H8V22H2Z M16 16H22V22H16Z M12 9V12H5V16 M12 12H19V16",
            "profiles" or "file" => "M5 2H14L20 8V22H5Z M14 2V8H20 M8 12H17 M8 16H17",
            "usage" or "chart" => "M3 3V21H22 M7 17V12 M12 17V7 M17 17V3",
            "setup" or "settings" => "M3 6H9 M15 6H21 M3 18H9 M15 18H21 M3 12H15 M9 3V9H15V3Z M9 15V21H15V15Z M15 9V15H21V9Z",
            "refresh" or "restart" => "M20 8A8.5 8.5 0 0 0 5 5L2 8 M2 3V8H7 M4 16A8.5 8.5 0 0 0 19 19L22 16 M17 16H22V21",
            "minimize" => "M4 12H20",
            "maximize" => "M4 4H20V20H4Z",
            "close" => "M5 5L19 19 M19 5L5 19",
            "add" or "plus" => "M12 4V20 M4 12H20",
            "search" => "M16 10A6 6 0 1 1 4 10A6 6 0 1 1 16 10 M15 15L21 21",
            "edit" => "M4 20L5 14L17 2L22 7L10 19Z M14 5L19 10",
            "remove" or "trash" => "M3 6H21 M9 6V3H15V6 M6 6L7 21H17L18 6 M10 10V17 M14 10V17",
            "copy" => "M9 9H21V21H9Z M15 5V2H2V15H5",
            "external" or "open" => "M14 3H21V10 M21 3L10 14 M10 4H4V20H20V14",
            "chevron-right" => "M8 4L16 12L8 20",
            "chevron-down" => "M4 8L12 16L20 8",
            "check" => "M3 12L9 18L21 5",
            "warning" => "M12 3L22 21H2Z M12 9V14 M12 17V18",
            "play" or "start" => "M6 3L21 12L6 21Z",
            "stop" => "M5 5H19V19H5Z",
            _ => "M21 12A9 9 0 1 1 3 12A9 9 0 1 1 21 12 M12 11V17 M12 7V8"
        };
        var pen = new Pen(Brushes.Black, 1.65) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        return new PathIcon
        {
            Data = Geometry.Parse(path).GetWidenedGeometry(pen), Width = size, Height = size,
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
        };
    }
    public static string When(DateTimeOffset? value) => value?.ToLocalTime().ToString("dd MMM · HH:mm:ss") ?? "Unknown";
}
