using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Launcher.Core.Services;

/// <summary>
/// Sends privacy-safe usage events only after the user enables anonymous telemetry.
/// It never sends a Minecraft username, email address, hardware identifier, or raw IP.
/// </summary>
public static class LauncherAnalyticsService
{
    private static readonly Uri Endpoint = new("https://launcher.unitedbangla.top/analytics/heartbeat");
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UBLauncher");
    private static readonly string InstallationIdPath = Path.Combine(DataDirectory, "anonymous-install-id.txt");
    private static int heartbeatLoopStarted;

    public static Task SendLauncherOpenAsync() => SendEventAsync("launcher_open");
    public static Task SendGameLaunchAsync() => SendEventAsync("game_launch");

    public static void StartHeartbeatLoop()
    {
        if (Interlocked.Exchange(ref heartbeatLoopStarted, 1) == 1)
        {
            return;
        }

        _ = RunHeartbeatLoopAsync();
    }

    private static async Task RunHeartbeatLoopAsync()
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromMinutes(5));
            await SendEventAsync("heartbeat");
        }
    }

    private static async Task SendEventAsync(string eventName)
    {
        if (!LauncherAnalyticsConsentStore.IsEnabled)
        {
            return;
        }

        try
        {
            using var response = await HttpClient.PostAsJsonAsync(Endpoint, new
            {
                installId = GetOrCreateInstallationId(),
                @event = eventName,
                launcherVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0",
                os = RuntimeInformation.OSDescription.Trim(),
                architecture = RuntimeInformation.OSArchitecture.ToString(),
                locale = CultureInfo.CurrentUICulture.Name,
                accountType = "Anonymous",
                displayName = string.Empty,
                email = string.Empty
            });
            response.EnsureSuccessStatusCode();
        }
        catch
        {
            // Analytics is optional and must never affect launcher startup or gameplay.
        }
    }

    private static string GetOrCreateInstallationId()
    {
        try
        {
            if (File.Exists(InstallationIdPath) &&
                Guid.TryParse(File.ReadAllText(InstallationIdPath).Trim(), out var existing))
            {
                return existing.ToString();
            }

            Directory.CreateDirectory(DataDirectory);
            var generated = Guid.NewGuid().ToString();
            File.WriteAllText(InstallationIdPath, generated);
            return generated;
        }
        catch
        {
            // This session remains anonymous even when the identifier cannot be persisted.
            return Guid.NewGuid().ToString();
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("UBLauncher/1.0");
        return client;
    }
}

public static class LauncherAnalyticsConsentStore
{
    private static readonly string PreferencePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UBLauncher",
        "privacy.json");

    public static bool IsEnabled { get; private set; } = Load();

    public static void SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencePath)!);
            File.WriteAllText(PreferencePath, JsonSerializer.Serialize(new PrivacyPreference { AnonymousTelemetry = enabled }));
        }
        catch
        {
            // The UI setting still applies for this process when persistence is unavailable.
        }
    }

    private static bool Load()
    {
        try
        {
            return File.Exists(PreferencePath) &&
                   JsonSerializer.Deserialize<PrivacyPreference>(File.ReadAllText(PreferencePath))?.AnonymousTelemetry == true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class PrivacyPreference
    {
        public bool AnonymousTelemetry { get; init; }
    }
}
