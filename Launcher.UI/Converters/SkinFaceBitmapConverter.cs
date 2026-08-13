using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Launcher.UI.Converters;

public sealed class SkinFaceBitmapConverter : IValueConverter
{
    private readonly Dictionary<string, CroppedBitmap> cache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (cache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        try
        {
            Bitmap bitmap;
            if (path.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = AssetLoader.Open(new Uri(path));
                bitmap = new Bitmap(stream);
            }
            else
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                using var stream = File.OpenRead(path);
                bitmap = new Bitmap(stream);
            }

            var cropped = new CroppedBitmap
            {
                Source = bitmap,
                SourceRect = new PixelRect(8, 8, 8, 8)
            };
            cache[path] = cropped;
            return cropped;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
