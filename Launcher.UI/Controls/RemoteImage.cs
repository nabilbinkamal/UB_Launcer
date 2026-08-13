using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Launcher.UI.Controls;

/// <summary>
/// Loads catalog artwork without blocking the UI thread. Downloaded bytes are cached in memory
/// and on disk; the surrounding XAML remains visible as a fallback while an image is unavailable.
/// </summary>
public sealed class RemoteImage : Image
{
    public static readonly StyledProperty<string?> SourceUrlProperty =
        AvaloniaProperty.Register<RemoteImage, string?>(nameof(SourceUrl));

    public static readonly StyledProperty<int> DecodePixelWidthProperty =
        AvaloniaProperty.Register<RemoteImage, int>(nameof(DecodePixelWidth), 144);

    private const int MaximumImageBytes = 6 * 1024 * 1024;
    private static readonly ConcurrentDictionary<ImageCacheKey, Task<Bitmap?>> ImageCache = new();
    private static readonly SemaphoreSlim DecodeGate = new(2, 2);
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UBLauncher",
        "cache",
        "content-icons");

    private int loadRevision;

    public string? SourceUrl
    {
        get => GetValue(SourceUrlProperty);
        set => SetValue(SourceUrlProperty, value);
    }

    /// <summary>Decode size used for the image cache. Use a higher value for banners.</summary>
    public int DecodePixelWidth
    {
        get => GetValue(DecodePixelWidthProperty);
        set => SetValue(DecodePixelWidthProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceUrlProperty)
        {
            BeginLoad(change.NewValue as string);
        }
    }

    private async void BeginLoad(string? sourceUrl)
    {
        var revision = ++loadRevision;
        Source = null;

        if (!TryNormalizeSource(sourceUrl, out var normalizedSource))
        {
            return;
        }

        try
        {
            var cacheKey = new ImageCacheKey(normalizedSource, Math.Clamp(DecodePixelWidth, 32, 2560));
            var imageTask = ImageCache.GetOrAdd(cacheKey, LoadImageBitmapAsync);
            var bitmap = await imageTask;
            if (bitmap is null || revision != loadRevision)
            {
                if (bitmap is null)
                {
                    ImageCache.TryRemove(cacheKey, out _);
                }
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (revision != loadRevision)
                {
                    return;
                }

                Source = bitmap;
            });
        }
        catch (Exception exception)
        {
            ImageCache.TryRemove(new ImageCacheKey(normalizedSource, Math.Clamp(DecodePixelWidth, 32, 2560)), out _);
            System.Diagnostics.Debug.WriteLine($"Could not load catalog artwork: {exception.Message}");
        }
    }

    private static async Task<Bitmap?> LoadImageBitmapAsync(ImageCacheKey cacheKey)
    {
        var bytes = await LoadImageBytesAsync(cacheKey.Source).ConfigureAwait(false);
        if (bytes is null)
        {
            return null;
        }

        await DecodeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                using var stream = new MemoryStream(bytes, writable: false);
                return Bitmap.DecodeToWidth(stream, cacheKey.DecodePixelWidth, BitmapInterpolationMode.HighQuality);
            }).ConfigureAwait(false);
        }
        finally
        {
            DecodeGate.Release();
        }
    }

    private static async Task<byte[]?> LoadImageBytesAsync(string source)
    {
        if (File.Exists(source))
        {
            var fileInfo = new FileInfo(source);
            if (fileInfo.Length is <= 0 or > MaximumImageBytes)
            {
                return null;
            }

            return await File.ReadAllBytesAsync(source).ConfigureAwait(false);
        }

        var cachePath = GetCachePath(source);
        try
        {
            if (File.Exists(cachePath))
            {
                var cachedBytes = await File.ReadAllBytesAsync(cachePath).ConfigureAwait(false);
                if (cachedBytes.Length is > 0 and <= MaximumImageBytes)
                {
                    return cachedBytes;
                }
            }
        }
        catch
        {
            // A cache read failure should never prevent the network fallback.
        }

        using var response = await HttpClient
            .GetAsync(source, HttpCompletionOption.ResponseHeadersRead)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumImageBytes)
        {
            return null;
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = new byte[32 * 1024];
        var totalBytes = 0;
        int read;
        while ((read = await responseStream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            totalBytes += read;
            if (totalBytes > MaximumImageBytes)
            {
                return null;
            }
            await destination.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
        }

        var bytes = destination.ToArray();
        if (bytes.Length == 0)
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(CacheDirectory);
            await File.WriteAllBytesAsync(cachePath, bytes).ConfigureAwait(false);
        }
        catch
        {
            // The in-memory image is still usable when disk caching is unavailable.
        }

        return bytes;
    }

    private static bool TryNormalizeSource(string? value, out string source)
    {
        source = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (Path.IsPathFullyQualified(value) && File.Exists(value))
        {
            source = Path.GetFullPath(value);
            return true;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        source = uri.AbsoluteUri;
        return true;
    }

    private static string GetCachePath(string url)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        return Path.Combine(CacheDirectory, hash + ".icon");
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("UBLauncher/0.1");
        return client;
    }

    private readonly record struct ImageCacheKey(string Source, int DecodePixelWidth);
}
