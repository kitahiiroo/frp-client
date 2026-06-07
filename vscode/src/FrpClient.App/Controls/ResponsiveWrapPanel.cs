using System.Windows;
using System.Windows.Controls;

namespace FrpClient.App.Controls;

public class ResponsiveWrapPanel : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty =
        DependencyProperty.Register(
            nameof(MinItemWidth),
            typeof(double),
            typeof(ResponsiveWrapPanel),
            new FrameworkPropertyMetadata(260.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ItemHeightProperty =
        DependencyProperty.Register(
            nameof(ItemHeight),
            typeof(double),
            typeof(ResponsiveWrapPanel),
            new FrameworkPropertyMetadata(156.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty HorizontalSpacingProperty =
        DependencyProperty.Register(
            nameof(HorizontalSpacing),
            typeof(double),
            typeof(ResponsiveWrapPanel),
            new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty VerticalSpacingProperty =
        DependencyProperty.Register(
            nameof(VerticalSpacing),
            typeof(double),
            typeof(ResponsiveWrapPanel),
            new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var itemCount = InternalChildren.Count;

        if (itemCount == 0)
        {
            return new Size(0, 0);
        }

        var width = GetUsableWidth(availableSize.Width);
        var columnCount = GetColumnCount(width);
        var rowCount = (int)Math.Ceiling(itemCount / (double)columnCount);

        MeasureRows(width, columnCount);

        return new Size(width, rowCount * ItemHeight + Math.Max(0, rowCount - 1) * VerticalSpacing);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = GetUsableWidth(finalSize.Width);
        var columnCount = GetColumnCount(width);
        var itemWidth = GetItemWidth(width, columnCount);
        var index = 0;
        var y = 0.0;

        while (index < InternalChildren.Count)
        {
            var rowItemCount = Math.Min(columnCount, InternalChildren.Count - index);
            var x = 0.0;

            for (var column = 0; column < rowItemCount; column++)
            {
                InternalChildren[index].Arrange(new Rect(x, y, itemWidth, ItemHeight));
                x += itemWidth + HorizontalSpacing;
                index++;
            }

            y += ItemHeight + VerticalSpacing;
        }

        return finalSize;
    }

    private void MeasureRows(double width, int columnCount)
    {
        var index = 0;
        var itemWidth = GetItemWidth(width, columnCount);

        while (index < InternalChildren.Count)
        {
            var rowItemCount = Math.Min(columnCount, InternalChildren.Count - index);

            for (var column = 0; column < rowItemCount; column++)
            {
                InternalChildren[index].Measure(new Size(itemWidth, ItemHeight));
                index++;
            }
        }
    }

    private int GetColumnCount(double width)
    {
        return Math.Max(1, (int)Math.Floor((width + HorizontalSpacing) / (MinItemWidth + HorizontalSpacing)));
    }

    private double GetItemWidth(double width, int rowItemCount)
    {
        return Math.Max(0, (width - Math.Max(0, rowItemCount - 1) * HorizontalSpacing) / rowItemCount);
    }

    private double GetUsableWidth(double width)
    {
        return double.IsInfinity(width) || double.IsNaN(width)
            ? MinItemWidth
            : Math.Max(0, width);
    }
}
