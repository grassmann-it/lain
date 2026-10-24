using Avalonia;
using Avalonia.Controls;

namespace Lain.Desktop;

/// <summary>Packs independent account columns without sharing the tallest row height.</summary>
internal sealed class MasonryPanel : Panel
{
    private const double TwoColumnWidth = 900;
    public double ItemGap { get; init; } = 12;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : Math.Max(TwoColumnWidth, Bounds.Width);
        return new Size(width, LayoutColumns(width, measure: true));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        LayoutColumns(finalSize.Width, measure: false);
        return finalSize;
    }

    private double LayoutColumns(double width, bool measure)
    {
        var columns = width >= TwoColumnWidth ? 2 : 1;
        var columnWidth = Math.Max(0, (width - ItemGap * (columns - 1)) / columns);
        var bottoms = new double[columns];
        var position = 0;
        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;

            // Keep each account in its stable column when a quota snapshot adds
            // data or changes height. Details live outside the inventory layout.
            var column = position++ % columns;
            var top = bottoms[column];
            var childWidth = columnWidth;
            if (measure) child.Measure(new Size(childWidth, double.PositiveInfinity));
            var height = child.DesiredSize.Height;
            if (!measure) child.Arrange(new Rect(column * (columnWidth + ItemGap), top, childWidth, height));
            bottoms[column] = top + height + ItemGap;
        }
        return Math.Max(0, bottoms.Max() - ItemGap);
    }
}
