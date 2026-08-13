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

    public static readonly StyledProperty<Color> AccentColorProperty =
        AvaloniaProperty.Register<SparklineControl, Color>(nameof(AccentColor), Color.FromRgb(99, 139, 255));

    private static readonly Pen GridPen = new(
        new SolidColorBrush(Color.FromArgb(32, 137, 154, 169)),
        1);
    private SolidColorBrush fillBrush = null!;
    private SolidColorBrush dotGlowBrush = null!;
    private SolidColorBrush dotBrush = null!;
    private Pen glowPen = null!;
    private Pen strokePen = null!;
    private double[]? displayedValues;

    public SparklineControl()
    {
        MinWidth = 120;
        MinHeight = 64;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        ClipToBounds = true;
        RebuildPalette(AccentColor);

    }

    public IReadOnlyList<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Color AccentColor
    {
        get => GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ValuesProperty)
        {
            BeginAnimation(change.NewValue as IReadOnlyList<double>);
        }
        else if (change.Property == AccentColorProperty)
        {
            RebuildPalette(change.NewValue is Color color ? color : AccentColor);
            InvalidateVisual();
        }
    }

    private void BeginAnimation(IReadOnlyList<double>? targetValues)
    {
        if (targetValues is null || targetValues.Count == 0)
        {
            displayedValues = null;
            InvalidateVisual();
            return;
        }

        displayedValues = CopyValues(targetValues);
        InvalidateVisual();
    }

    private static double[] CopyValues(IReadOnlyList<double> values)
    {
        var result = new double[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            result[index] = Math.Clamp(values[index], 0, 1);
        }

        return result;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var values = displayedValues ?? Values;
        if (values is null || values.Count < 2 || Bounds.Width < 20 || Bounds.Height < 12)
        {
            return;
        }

        var chartLeft = 3d;
        var chartRight = Math.Max(chartLeft, Bounds.Width - 3);
        for (var line = 1; line <= 3; line++)
        {
            var y = 4 + (Bounds.Height - 8) * line / 4d;
            context.DrawLine(GridPen, new Point(chartLeft, y), new Point(chartRight, y));
        }

        var lineGeometry = CreateGeometry(values, false);
        var fillGeometry = CreateGeometry(values, true);

        context.DrawGeometry(fillBrush, null, fillGeometry);
        context.DrawGeometry(null, glowPen, lineGeometry);
        context.DrawGeometry(null, strokePen, lineGeometry);

        var lastPoint = GetPoint(values, values.Count - 1);
        context.DrawEllipse(dotGlowBrush, null, lastPoint, 4, 4);
        context.DrawEllipse(dotBrush, null, lastPoint, 1.5, 1.5);
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

    private void RebuildPalette(Color accent)
    {
        fillBrush = new SolidColorBrush(Color.FromArgb(32, accent.R, accent.G, accent.B));
        dotGlowBrush = new SolidColorBrush(Color.FromArgb(115, accent.R, accent.G, accent.B));
        dotBrush = new SolidColorBrush(accent);
        glowPen = new Pen(
            new SolidColorBrush(Color.FromArgb(65, accent.R, accent.G, accent.B)),
            5,
            lineCap: PenLineCap.Round,
            lineJoin: PenLineJoin.Round);
        strokePen = new Pen(
            new SolidColorBrush(accent),
            1.8,
            lineCap: PenLineCap.Round,
            lineJoin: PenLineJoin.Round);
    }
}
