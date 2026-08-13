using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Launcher.UI.Controls;

public sealed class MemoryUsageRing : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<MemoryUsageRing, double>(nameof(Value));

    private static readonly Pen BackgroundPen = new(
        new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)), 4);
    private static readonly Pen ProgressPen = new(
        new SolidColorBrush(Color.FromRgb(39, 113, 255)), 4,
        lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    private double displayedValue;
    private bool hasValue;

    public MemoryUsageRing()
    {
        Width = 50;
        Height = 50;

    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ValueProperty)
        {
            var target = Math.Clamp(change.NewValue is double value ? value : 0, 0, 100);
            if (!hasValue)
            {
                hasValue = true;
                displayedValue = target;
                InvalidateVisual();
                return;
            }

            displayedValue = target;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = Math.Max(1, Math.Min(Bounds.Width, Bounds.Height) / 2 - 4);
        context.DrawEllipse(null, BackgroundPen, center, radius, radius);

        var progress = Math.Clamp(displayedValue / 100, 0, 1);
        if (progress <= 0)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using var builder = geometry.Open();
        var segmentCount = Math.Max(2, (int)(progress * 48));

        builder.BeginFigure(GetPoint(center, radius, 0), false);
        for (var index = 1; index <= segmentCount; index++)
        {
            builder.LineTo(GetPoint(center, radius, progress * index / segmentCount));
        }

        builder.EndFigure(false);
        context.DrawGeometry(null, ProgressPen, geometry);
    }

    private static Point GetPoint(Point center, double radius, double progress)
    {
        var angle = -Math.PI / 2 + progress * Math.PI * 2;
        return new Point(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
    }
}
