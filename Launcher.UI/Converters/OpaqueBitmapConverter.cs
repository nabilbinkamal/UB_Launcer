using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Launcher.UI.Converters;

public sealed class OpaqueBitmapConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            using var stream = path.StartsWith("avares://", StringComparison.OrdinalIgnoreCase)
                ? Avalonia.Platform.AssetLoader.Open(new Uri(path))
                : File.OpenRead(path);
            using var source = SKBitmap.Decode(stream);
            if (source is null)
                return null;

            var minX = source.Width;
            var minY = source.Height;
            var maxX = -1;
            var maxY = -1;

            for (var y = 0; y < source.Height; y++)
            {
                for (var x = 0; x < source.Width; x++)
                {
                    if (source.GetPixel(x, y).Alpha <= 8)
                        continue;

                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }

            if (maxX < minX || maxY < minY)
                return null;

            var cropWidth = maxX - minX + 1;
            var cropHeight = maxY - minY + 1;
            using var cropped = new SKBitmap(new SKImageInfo(cropWidth, cropHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(cropped))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(
                    source,
                    new SKRect(minX, minY, maxX + 1, maxY + 1),
                    new SKRect(0, 0, cropWidth, cropHeight));
            }

            using var data = cropped.Encode(SKEncodedImageFormat.Png, 100);
            using var output = data.AsStream();
            return new Bitmap(output);
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
