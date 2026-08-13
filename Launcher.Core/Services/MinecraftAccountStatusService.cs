using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace Launcher.Core.Services;

public static class MinecraftAccountStatusService
{
    private static readonly ConcurrentDictionary<string, bool> ProfileCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HttpClient HttpClient = CreateHttpClient();

    public static async Task<bool?> IsPremiumUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        var normalizedUsername = username.Trim();
        if (string.IsNullOrWhiteSpace(normalizedUsername))
        {
            return false;
        }

        if (ProfileCache.TryGetValue(normalizedUsername, out var cachedResult))
        {
            return cachedResult;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));

        try
        {
            var requestUri = $"https://api.minecraftservices.com/minecraft/profile/lookup/name/{Uri.EscapeDataString(normalizedUsername)}";
            using var response = await HttpClient.GetAsync(requestUri, timeout.Token);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                ProfileCache[normalizedUsername] = false;
                return false;
            }

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var profileDocument = await JsonDocument.ParseAsync(responseStream, cancellationToken: timeout.Token);
            var profile = profileDocument.RootElement;
            var hasProfile = profile.TryGetProperty("id", out var id) && !string.IsNullOrWhiteSpace(id.GetString()) &&
                             profile.TryGetProperty("name", out var name) && !string.IsNullOrWhiteSpace(name.GetString());
            var isDemo = profile.TryGetProperty("demo", out var demo) && demo.ValueKind == JsonValueKind.True;
            var isPremium = hasProfile && !isDemo;

            ProfileCache[normalizedUsername] = isPremium;
            return isPremium;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("UBLauncher/1.0");
        return client;
    }
}
