using System;
using System.Windows;
using System.Windows.Controls;
using OsrsWorldPing.Core;

namespace OsrsWorldPing.Desktop;

public sealed class WorldColumnsPanel : Panel
{
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(nameof(Columns), typeof(int),
        typeof(WorldColumnsPanel), new FrameworkPropertyMetadata(3, FrameworkPropertyMetadataOptions.AffectsMeasure), value => (int)value >= 1);
    public int Columns { get => (int)GetValue(ColumnsProperty); set => SetValue(ColumnsProperty, value); }
    private double _rowHeight;
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? Columns * 280 : availableSize.Width;
        _rowHeight = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(width / Columns, double.PositiveInfinity));
            _rowHeight = Math.Max(_rowHeight, child.DesiredSize.Height);
        }
        return new Size(width, Math.Ceiling((double)InternalChildren.Count / Columns) * _rowHeight);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var (row, column) = WorldColumnLayout.Position(i, InternalChildren.Count, Columns);
            InternalChildren[i].Arrange(new Rect(column * finalSize.Width / Columns, row * _rowHeight, finalSize.Width / Columns, _rowHeight));
        }
        return finalSize;
    }
}
