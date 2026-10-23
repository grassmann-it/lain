using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Lain.Desktop;

internal static class Ui
{
    public static readonly IBrush Ink = Brush.Parse("#E3EFF0");
    public static readonly IBrush Muted = Brush.Parse("#93AAB5");
    public static readonly IBrush Accent = Brush.Parse("#7BDBCE");
    public static readonly IBrush Surface = Brush.Parse("#121C25");
    public static readonly IBrush Edge = Brush.Parse("#263945");

    public static Styles CreateStyles()
    {
        var styles = new Styles();
        styles.Add(new Style(x => x.OfType<Window>()) { Setters = { new Setter(TemplatedControl.FontFamilyProperty, new FontFamily("Inter, Noto Sans, DejaVu Sans")), new Setter(TemplatedControl.ForegroundProperty, Ink), new Setter(TemplatedControl.FontSizeProperty, 14d) } });
        styles.Add(new Style(x => x.OfType<Button>()) { Setters = { new Setter(TemplatedControl.PaddingProperty, new Thickness(15, 10)), new Setter(TemplatedControl.BackgroundProperty, Brush.Parse("#1B2B37")), new Setter(TemplatedControl.BorderBrushProperty, Edge), new Setter(TemplatedControl.ForegroundProperty, Ink), new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(6)) } });
        styles.Add(new Style(x => x.OfType<TextBox>()) { Setters = { new Setter(TemplatedControl.PaddingProperty, new Thickness(10)), new Setter(TemplatedControl.BackgroundProperty, Brush.Parse("#0D151D")), new Setter(TemplatedControl.BorderBrushProperty, Edge), new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(5)) } });
        styles.Add(new Style(x => x.OfType<ComboBox>()) { Setters = { new Setter(Layoutable.MinHeightProperty, 40d), new Setter(Layoutable.HorizontalAlignmentProperty, HorizontalAlignment.Stretch) } });
        return styles;
    }

    public static TextBlock Text(string text, double size = 14, IBrush? color = null) => new() { Text = text, FontSize = size, Foreground = color ?? Ink, TextWrapping = TextWrapping.Wrap };
    public static TextBlock Title(string text) => new() { Text = text, FontSize = 28, FontWeight = FontWeight.SemiBold, Foreground = Ink };
    public static StackPanel Stack(params Control[] controls) { var panel = new StackPanel { Spacing = 14 }; foreach (var child in controls) panel.Children.Add(child); return panel; }
    public static StackPanel Row(params Control[] controls) { var panel = new StackPanel { Spacing = 10, Orientation = Orientation.Horizontal }; foreach (var child in controls) panel.Children.Add(child); return panel; }
    public static Border Card(Control content) => new() { Background = Surface, BorderBrush = Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(22), Child = content };
    public static Button Button(string label, Action click) { var b = new Button { Content = label }; b.Click += (_, _) => click(); return b; }
    public static TextBox Input(string? value = null, string? hint = null) => new() { Text = value ?? "", Watermark = hint, HorizontalAlignment = HorizontalAlignment.Stretch };
    public static Control Field(string label, Control input) => Stack(Text(label, 12, Muted), input);
    public static string When(DateTimeOffset? value) => value?.ToLocalTime().ToString("dd MMM · HH:mm:ss") ?? "Unknown";
}
