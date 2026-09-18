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
#if DEBUG
    private const string LocalDevelopmentConfigUrl = "http://127.0.0.1:8602/config.json";
#endif

    private const int MaximumConfigBytes = 128 * 1024;
    // Mandatory updates are intentionally disabled. The server may still publish
    // ForceUpdate for backwards compatibility, but this launcher always lets the
    // user dismiss the update prompt for the current session.
    private const bool MandatoryUpdatesEnabled = false;
    private static readonly HttpClient HttpClient = CreateHttpClient();

    public static LauncherPublicConfiguration Current { get; private set; } = new();

    public static string InstalledVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

    public static async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        return await TryRefreshFromUrlAsync(DefaultConfigUrl, cancellationToken);
    }

    private static async Task<bool> TryRefreshFromUrlAsync(
        string configUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await HttpClient.GetAsync(
                configUrl,
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
        MandatoryUpdatesEnabled &&
        Current.ForceUpdate &&
        TryParseVersion(Current.LatestVersion, out var latest) &&
        TryParseVersion(InstalledVersion, out var installed) &&
        latest > installed;

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
    public string CurrentVersion { get; init; } = string.Empty;
    public string LatestVersion { get; init; } = string.Empty;
    public bool ForceUpdate { get; init; }
    public string UpdateTitle { get; init; } = "Update Available";
    public string UpdateMessage { get; init; } = "A new version of UB Launcher is available. Update now to get the latest features, improvements, and bug fixes.";
    public string UpdateUrl { get; init; } = string.Empty;
    public string ReleaseNotesUrl { get; init; } = string.Empty;
    public const string DefaultDiscordCommunityUrl = "https://discord.gg/8Cge9nBqVh";
    public string DiscordApplicationId { get; init; } = string.Empty;
    public string DiscordLargeImage { get; init; } = string.Empty;
    public string DiscordCommunityUrl { get; init; } = DefaultDiscordCommunityUrl;
    public Dictionary<string, string> Assets { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<LauncherPartnerServer> PartnerServers { get; init; } = [];

    public string GetAsset(string name, string fallback) =>
        Assets.TryGetValue(name, out var value) && IsHttpUrl(value) ? value : fallback;

    public LauncherPublicConfiguration Sanitize() => new()
    {
        SchemaVersion = 1,
        CurrentVersion = CurrentVersion?.Trim() ?? string.Empty,
        LatestVersion = LatestVersion?.Trim() ?? string.Empty,
        // Never lock users behind a mandatory dialog unless it has a usable HTTPS destination.
        ForceUpdate = ForceUpdate && IsHttpUrl(UpdateUrl),
        UpdateTitle = Trim(UpdateTitle, 120, "Update Available"),
        UpdateMessage = Trim(UpdateMessage, 500, "A new version of UB Launcher is available. Update now to get the latest features, improvements, and bug fixes."),
        UpdateUrl = IsHttpUrl(UpdateUrl) ? UpdateUrl.Trim() : string.Empty,
        ReleaseNotesUrl = IsHttpUrl(ReleaseNotesUrl) ? ReleaseNotesUrl.Trim() : string.Empty,
        DiscordApplicationId = IsDiscordApplicationId(DiscordApplicationId)
            ? DiscordApplicationId.Trim()
            : string.Empty,
        DiscordLargeImage = NormalizeDiscordImage(DiscordLargeImage),
        DiscordCommunityUrl = IsHttpUrl(DiscordCommunityUrl) ? DiscordCommunityUrl.Trim() : DefaultDiscordCommunityUrl,
        Assets = (Assets ?? new Dictionary<string, string>())
            .Where(pair => pair.Key.Length is > 0 and <= 64 && IsHttpUrl(pair.Value))
            .Take(40)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Trim(), StringComparer.OrdinalIgnoreCase),
        PartnerServers = (PartnerServers ?? [])
            .Select(server => server.Sanitize())
            .Where(server => server is not null)
            .Cast<LauncherPartnerServer>()
            .Take(30)
            .ToList()
    };

    private static string Trim(string? value, int maximumLength, string fallback)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed[..Math.Min(trimmed.Length, maximumLength)];
    }

    private static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps;

    private static bool IsDiscordApplicationId(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.Length is >= 16 and <= 20 && ulong.TryParse(normalized, out _);
    }

    private static string NormalizeDiscordImage(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (IsHttpUrl(normalized))
        {
            return normalized;
        }

        return normalized.Length is > 0 and <= 128 &&
               normalized.All(character => char.IsLetterOrDigit(character) || character is '_' or '-')
            ? normalized.ToLowerInvariant()
            : string.Empty;
    }
}

public sealed class LauncherPartnerServer
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Badge { get; init; } = "PARTNER";
    public string IconUrl { get; init; } = string.Empty;
    public string WebsiteUrl { get; init; } = string.Empty;
    public bool Enabled { get; init; } = true;

    internal LauncherPartnerServer? Sanitize()
    {
        var name = Trim(Name, 80);
        var address = Address?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name) || !IsValidServerAddress(address))
        {
            return null;
        }

        return new LauncherPartnerServer
        {
            Id = Trim(Id, 50, $"server-{Guid.NewGuid():N}"),
            Name = name,
            Address = address,
            Description = Trim(Description, 180, "Join this partner Minecraft server."),
            Version = Trim(Version, 32, "Any version"),
            Badge = Trim(Badge, 24, "PARTNER"),
            IconUrl = IsTrustedImageUrl(IconUrl) ? IconUrl.Trim() : string.Empty,
            WebsiteUrl = IsHttpsUrl(WebsiteUrl) ? WebsiteUrl.Trim() : string.Empty,
            Enabled = Enabled
        };
    }

    private static bool IsValidServerAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 255 || value.Contains(' '))
        {
            return false;
        }

        var host = value;
        var colonIndex = value.LastIndexOf(':');
        if (colonIndex > 0 && value.Count(character => character == ':') == 1)
        {
            host = value[..colonIndex];
            if (!int.TryParse(value[(colonIndex + 1)..], out var port) || port is < 1 or > 65535)
            {
                return false;
            }
        }

        return Uri.CheckHostName(host) != UriHostNameType.Unknown;
    }

    private static bool IsHttpsUrl(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static bool IsTrustedImageUrl(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return true;
        }

#if DEBUG
        return uri.Scheme == Uri.UriSchemeHttp &&
               (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase));
#else
        return false;
#endif
    }

    private static string Trim(string? value, int maximumLength, string fallback = "")
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed)) return fallback;
        return trimmed[..Math.Min(trimmed.Length, maximumLength)];
    }
}
