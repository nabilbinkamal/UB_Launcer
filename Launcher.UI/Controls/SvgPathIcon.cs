using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;

namespace Launcher.UI.Controls;

public class SvgPathIcon : Control
{
    public static readonly StyledProperty<string?> SourceProperty =
        AvaloniaProperty.Register<SvgPathIcon, string?>(nameof(Source));

    private readonly List<SvgPath> paths = new();
    private double canvasWidth = 24;
    private double canvasHeight = 24;

    public SvgPathIcon()
    {
        Width = 28;
        Height = 28;
    }

    public string? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SourceProperty)
        {
            LoadIcon();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (paths.Count == 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var scale = Math.Min(Bounds.Width / canvasWidth, Bounds.Height / canvasHeight);
        var offsetX = (Bounds.Width / scale - canvasWidth) / 2;
        var offsetY = (Bounds.Height / scale - canvasHeight) / 2;

        using (context.PushTransform(Matrix.CreateTranslation(offsetX, offsetY)))
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            foreach (var path in paths)
            {
                context.DrawGeometry(path.Fill, path.Pen, path.Geometry);
            }
        }
    }

    private void LoadIcon()
    {
        paths.Clear();

        if (string.IsNullOrWhiteSpace(Source))
        {
            InvalidateVisual();
            return;
        }

        var assetUri = CreateAssetUri(Source);

        using var stream = AssetLoader.Open(assetUri);
        var document = XDocument.Load(stream);
        var root = document.Root;

        if (root is null)
        {
            InvalidateVisual();
            return;
        }

        (canvasWidth, canvasHeight) = GetViewBoxSize(root);

        foreach (var pathElement in root.Descendants().Where(element => element.Name.LocalName == "path"))
        {
            var data = pathElement.Attribute("d")?.Value;

            if (string.IsNullOrWhiteSpace(data))
            {
                continue;
            }

            var geometry = Geometry.Parse(data);
            var fill = ParseBrush(pathElement.Attribute("fill")?.Value);
            var stroke = ParseBrush(pathElement.Attribute("stroke")?.Value);
            var thickness = ParseDouble(pathElement.Attribute("stroke-width")?.Value) ?? 1;
            var pen = stroke is null
                ? null
                : new Pen(
                    stroke,
                    thickness,
                    lineCap: ParseLineCap(pathElement.Attribute("stroke-linecap")?.Value),
                    lineJoin: ParseLineJoin(pathElement.Attribute("stroke-linejoin")?.Value));

            paths.Add(new SvgPath(geometry, fill, pen));
        }

        foreach (var circleElement in root.Descendants().Where(element => element.Name.LocalName == "circle"))
        {
            var centerX = ParseDouble(circleElement.Attribute("cx")?.Value) ?? 0;
            var centerY = ParseDouble(circleElement.Attribute("cy")?.Value) ?? 0;
            var radius = ParseDouble(circleElement.Attribute("r")?.Value) ?? 0;

            if (radius <= 0)
            {
                continue;
            }

            var geometry = new EllipseGeometry(new Rect(
                centerX - radius,
                centerY - radius,
                radius * 2,
                radius * 2));
            var fill = ParseBrush(circleElement.Attribute("fill")?.Value);
            var stroke = ParseBrush(circleElement.Attribute("stroke")?.Value);
            var thickness = ParseDouble(circleElement.Attribute("stroke-width")?.Value) ?? 1;
            var pen = stroke is null
                ? null
                : new Pen(
                    stroke,
                    thickness,
                    lineCap: ParseLineCap(circleElement.Attribute("stroke-linecap")?.Value),
                    lineJoin: ParseLineJoin(circleElement.Attribute("stroke-linejoin")?.Value));

            paths.Add(new SvgPath(geometry, fill, pen));
        }

        InvalidateVisual();
    }

    private static Uri CreateAssetUri(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri;
        }

        var assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
        var normalizedSource = source.StartsWith('/') ? source : $"/{source}";

        return new Uri($"avares://{assemblyName}{normalizedSource}");
    }

    private static (double Width, double Height) GetViewBoxSize(XElement root)
    {
        var viewBox = root.Attribute("viewBox")?.Value;

        if (!string.IsNullOrWhiteSpace(viewBox))
        {
            var parts = viewBox.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 4 &&
                double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var width) &&
                double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var height))
            {
                return (width, height);
            }
        }

        return (24, 24);
    }

    private static IBrush? ParseBrush(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || value.Equals("none", StringComparison.OrdinalIgnoreCase)
            ? null
            : Brush.Parse(value);
    }

    private static double? ParseDouble(string? value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static PenLineCap ParseLineCap(string? value) => value?.ToLowerInvariant() switch
    {
        "round" => PenLineCap.Round,
        "square" => PenLineCap.Square,
        _ => PenLineCap.Flat,
    };

    private static PenLineJoin ParseLineJoin(string? value) => value?.ToLowerInvariant() switch
    {
        "round" => PenLineJoin.Round,
        "bevel" => PenLineJoin.Bevel,
        _ => PenLineJoin.Miter,
    };

    private sealed record SvgPath(Geometry Geometry, IBrush? Fill, IPen? Pen);
}
