using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace Launcher.Core.Services;

/// <summary>
/// Public, read-only settings published by the UB Launcher admin panel.  A bad or
/// unavailable response never prevents the launcher from opening.
/// </summary>
public static class LauncherRemoteConfiguration
{
    // This is the only address compiled into the launcher. It will be served by
    // the admin panel once the UB Launcher subdomain is pointed at the Hostinger VPS.
    public const string DefaultConfigUrl = "https://launcher.unitedbangla.top/config.json";

    private const int MaximumConfigBytes = 128 * 1024;
    private static readonly HttpClient HttpClient = CreateHttpClient();

    public static LauncherPublicConfiguration Current { get; private set; } = new();

    public static string InstalledVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

    public static async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await HttpClient.GetAsync(
                DefaultConfigUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is > MaximumConfigBytes)
            {
                return false;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            if (buffer.Length is 0 or > MaximumConfigBytes)
            {
                return false;
            }

            var configuration = JsonSerializer.Deserialize<LauncherPublicConfiguration>(
                buffer.ToArray(),
                JsonOptions);
            if (configuration is null || configuration.SchemaVersion != 1)
            {
                return false;
            }

            Current = configuration.Sanitize();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsNewerVersionAvailable() =>
        TryParseVersion(Current.LatestVersion, out var latest) &&
        TryParseVersion(InstalledVersion, out var installed) &&
        latest > installed;

    public static bool IsUpdateRequired() =>
        Current.ForceUpdate &&
        TryParseVersion(Current.MinimumVersion, out var minimum) &&
        TryParseVersion(InstalledVersion, out var installed) &&
        minimum > installed;

    private static bool TryParseVersion(string? value, out Version version) =>
        Version.TryParse(value?.Trim(), out version!);

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("UBLauncher/1.0");
        return client;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}

public sealed class LauncherPublicConfiguration
{
    public int SchemaVersion { get; init; } = 1;
    public string LatestVersion { get; init; } = string.Empty;
    public string MinimumVersion { get; init; } = string.Empty;
    public bool ForceUpdate { get; init; }
    public string UpdateTitle { get; init; } = "A new UB Launcher update is ready";
    public string UpdateMessage { get; init; } = "Update now to get the latest fixes and improvements.";
    public string UpdateUrl { get; init; } = string.Empty;
    public Dictionary<string, string> Assets { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public string GetAsset(string name, string fallback) =>
        Assets.TryGetValue(name, out var value) && IsHttpUrl(value) ? value : fallback;

    public LauncherPublicConfiguration Sanitize() => new()
    {
        SchemaVersion = 1,
        LatestVersion = LatestVersion?.Trim() ?? string.Empty,
        MinimumVersion = MinimumVersion?.Trim() ?? string.Empty,
        ForceUpdate = ForceUpdate,
        UpdateTitle = Trim(UpdateTitle, 120, "A new UB Launcher update is ready"),
        UpdateMessage = Trim(UpdateMessage, 500, "Update now to get the latest fixes and improvements."),
        UpdateUrl = IsHttpUrl(UpdateUrl) ? UpdateUrl.Trim() : string.Empty,
        Assets = Assets
            .Where(pair => pair.Key.Length is > 0 and <= 64 && IsHttpUrl(pair.Value))
            .Take(40)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Trim(), StringComparer.OrdinalIgnoreCase)
    };

    private static string Trim(string? value, int maximumLength, string fallback)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed[..Math.Min(trimmed.Length, maximumLength)];
    }

    private static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps;
}
