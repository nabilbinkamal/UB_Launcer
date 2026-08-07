using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Launcher.UI.Controls;

public sealed class SparklineControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<SparklineControl, IReadOnlyList<double>?>(nameof(Values));

    private static readonly Color GlowColor = Color.FromArgb(70, 75, 128, 255);
    private static readonly Color FillColor = Color.FromArgb(32, 56, 104, 212);
    private static readonly Color StrokeColor = Color.FromRgb(99, 139, 255);

    public SparklineControl()
    {
        Width = 122;
        Height = 42;
        ClipToBounds = true;
    }

    public IReadOnlyList<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ValuesProperty)
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var values = Values;
        if (values is null || values.Count < 2 || Bounds.Width < 20 || Bounds.Height < 12)
        {
            return;
        }

        var lineGeometry = CreateGeometry(values, false);
        var fillGeometry = CreateGeometry(values, true);

        context.DrawGeometry(new SolidColorBrush(FillColor), null, fillGeometry);
        context.DrawGeometry(null, new Pen(new SolidColorBrush(GlowColor), 4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), lineGeometry);
        context.DrawGeometry(null, new Pen(new SolidColorBrush(StrokeColor), 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), lineGeometry);

        var lastPoint = GetPoint(values, values.Count - 1);
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(100, 99, 139, 255)), null, lastPoint, 3.5, 3.5);
        context.DrawEllipse(Brushes.White, null, lastPoint, 1.25, 1.25);
    }

    private StreamGeometry CreateGeometry(IReadOnlyList<double> values, bool fill)
    {
        var geometry = new StreamGeometry();
        using var builder = geometry.Open();
        var firstPoint = GetPoint(values, 0);

        builder.BeginFigure(fill ? new Point(firstPoint.X, Bounds.Height - 2) : firstPoint, fill);

        if (fill)
        {
            builder.LineTo(firstPoint);
        }

        AppendSmoothCurve(builder, values);

        if (fill)
        {
            builder.LineTo(new Point(GetPoint(values, values.Count - 1).X, Bounds.Height - 2));
            builder.EndFigure(true);
        }
        else
        {
            builder.EndFigure(false);
        }

        return geometry;
    }

    private void AppendSmoothCurve(StreamGeometryContext builder, IReadOnlyList<double> values)
    {
        for (var index = 0; index < values.Count - 1; index++)
        {
            var previous = GetPoint(values, index == 0 ? index : index - 1);
            var current = GetPoint(values, index);
            var next = GetPoint(values, index + 1);
            var following = GetPoint(values, index + 2 < values.Count ? index + 2 : index + 1);

            var controlPointOne = new Point(
                current.X + (next.X - previous.X) / 6,
                current.Y + (next.Y - previous.Y) / 6);
            var controlPointTwo = new Point(
                next.X - (following.X - current.X) / 6,
                next.Y - (following.Y - current.Y) / 6);

            builder.CubicBezierTo(controlPointOne, controlPointTwo, next);
        }
    }

    private Point GetPoint(IReadOnlyList<double> values, int index)
    {
        const double horizontalPadding = 3;
        const double verticalPadding = 4;
        var width = Math.Max(1, Bounds.Width - horizontalPadding * 2);
        var height = Math.Max(1, Bounds.Height - verticalPadding * 2);
        var x = horizontalPadding + width * index / (values.Count - 1);
        var y = verticalPadding + (1 - Math.Clamp(values[index], 0, 1)) * height;
        return new Point(x, y);
    }
}
