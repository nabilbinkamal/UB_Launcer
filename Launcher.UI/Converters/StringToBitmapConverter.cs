using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Launcher.UI.Converters;

public class StringToBitmapConverter : IValueConverter
{
    public static readonly StringToBitmapConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrWhiteSpace(path))
        {
            try
            {
                if (path.StartsWith("avares://"))
                {
                    var uri = new Uri(path);
                    using var stream = AssetLoader.Open(uri);
                    return new Bitmap(stream);
                }
                
                if (File.Exists(path))
                {
                    using var stream = File.OpenRead(path);
                    return new Bitmap(stream);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load bitmap from path {path}: {ex.Message}");
            }
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
