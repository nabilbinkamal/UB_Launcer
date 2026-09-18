using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Launcher.UI.Controls;

public class Cape2DPreviewControl : Control
{
    public Cape2DPreviewControl()
    {
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
    }

    public static readonly StyledProperty<string?> SourceProperty =
        AvaloniaProperty.Register<Cape2DPreviewControl, string?>(nameof(Source));

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
            LoadImage();
        }
    }

    private IImage? _image;

    private void LoadImage()
    {
        _image = null;
        if (!string.IsNullOrWhiteSpace(Source))
        {
            try
            {
                if (Source.StartsWith("avares://"))
                {
                    using var s = AssetLoader.Open(new Uri(Source));
                    _image = new Bitmap(s);
                }
                else if (System.IO.File.Exists(Source))
                {
                    _image = new Bitmap(Source);
                }
            }
            catch { }
        }
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_image == null) return;

        var size = _image.Size;
        var ratio = size.Width / size.Height;
        
        var isStandardAtlas = Math.Abs(ratio - 2.0) <= 0.05; // e.g., 64x32
        var isOptiFineAtlas = Math.Abs(ratio - 2.09) <= 0.05; // e.g., 46x22
        
        double scaleX = 1.0;
        double scaleY = 1.0;
        bool isCapeAtlas = false;

        if (isStandardAtlas && size.Width >= 64)
        {
            scaleX = size.Width / 64.0;
            scaleY = size.Height / 32.0;
            isCapeAtlas = true;
        }
        else if (isOptiFineAtlas && size.Width >= 46)
        {
            scaleX = size.Width / 46.0;
            scaleY = size.Height / 22.0;
            isCapeAtlas = true;
        }
        else if (size.Width >= 22 && size.Height >= 17 && ratio >= 1.5 && ratio <= 2.5)
        {
            // Fallback for weird sizes that still resemble an atlas
            scaleX = size.Width / 64.0;
            scaleY = size.Height / 32.0;
            isCapeAtlas = true;
        }

        Rect srcRect;
        if (isCapeAtlas)
        {
            srcRect = new Rect(1 * scaleX, 1 * scaleY, 10 * scaleX, 16 * scaleY);
        }
        else
        {
            srcRect = new Rect(0, 0, size.Width, size.Height);
        }

        var destRatio = srcRect.Width / srcRect.Height;
        var boundsRatio = Bounds.Width / Bounds.Height;
        
        double drawWidth, drawHeight;
        if (boundsRatio > destRatio)
        {
            drawHeight = Bounds.Height;
            drawWidth = drawHeight * destRatio;
        }
        else
        {
            drawWidth = Bounds.Width;
            drawHeight = drawWidth / destRatio;
        }

        var drawRect = new Rect((Bounds.Width - drawWidth) / 2, (Bounds.Height - drawHeight) / 2, drawWidth, drawHeight);
        
        context.DrawImage(_image, srcRect, drawRect);
    }
}
