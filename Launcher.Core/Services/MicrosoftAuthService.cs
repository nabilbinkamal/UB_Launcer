using System.Net.Http.Headers;
using System.Text.Json;

namespace Launcher.Core.Services;

/// <summary>
/// Minimal Microsoft OAuth 2.0 device-code flow for Minecraft.
/// Sequence: Azure device-code → MSA access token → Xbox Live → XSTS → Minecraft token → Profile + capes.
/// </summary>
public static class MicrosoftAuthService
{
    // Public Azure app client ID used by many open-source MC launchers (no secret required for device-code).
    private const string ClientId = "00000000402b5328";
    private const string DeviceCodeUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";
    private const string TokenUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
    private const string XblAuthUrl = "https://user.auth.xboxlive.com/user/authenticate";
    private const string XstsAuthUrl = "https://xsts.auth.xboxlive.com/xsts/authorize";
    private const string McAuthUrl = "https://api.minecraftservices.com/authentication/login_with_xbox";
    private const string McProfileUrl = "https://api.minecraftservices.com/minecraft/profile";
    private const string McCapeTextureBaseUrl = "https://textures.minecraft.net/texture/";

    private static readonly HttpClient Http = CreateClient();

    // -------------------------------------------------------------------------
    // Public surface
    // -------------------------------------------------------------------------

    public sealed class DeviceCodeChallenge
    {
        public string DeviceCode { get; init; } = string.Empty;
        public string UserCode { get; init; } = string.Empty;
        public string VerificationUri { get; init; } = string.Empty;
        public int ExpiresIn { get; init; }
        public int Interval { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    public sealed class MicrosoftSession
    {
        public string AccessToken { get; init; } = string.Empty;
        public string RefreshToken { get; init; } = string.Empty;
        public DateTimeOffset ExpiresAt { get; init; }
        public string MinecraftToken { get; init; } = string.Empty;
        public string MinecraftUuid { get; init; } = string.Empty;
        public string MinecraftUsername { get; init; } = string.Empty;
        public IReadOnlyList<MinecraftCape> Capes { get; init; } = [];
    }

    public sealed class MinecraftCape
    {
        public string Id { get; init; } = string.Empty;
        public string Alias { get; init; } = string.Empty;
        /// <summary>Direct URL to the cape texture PNG.</summary>
        public string TextureUrl { get; init; } = string.Empty;
        public string State { get; init; } = string.Empty;
    }

    /// <summary>Step 1 – request a device code from Microsoft.</summary>
    public static async Task<DeviceCodeChallenge> RequestDeviceCodeAsync(CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["scope"] = "XboxLive.signin offline_access"
        });

        using var response = await Http.PostAsync(DeviceCodeUrl, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        return new DeviceCodeChallenge
        {
            DeviceCode = root.GetProperty("device_code").GetString() ?? string.Empty,
            UserCode = root.GetProperty("user_code").GetString() ?? string.Empty,
            VerificationUri = root.GetProperty("verification_uri").GetString() ?? "https://microsoft.com/link",
            ExpiresIn = root.GetProperty("expires_in").GetInt32(),
            Interval = root.TryGetProperty("interval", out var iv) ? iv.GetInt32() : 5,
            Message = root.TryGetProperty("message", out var msg) ? msg.GetString() ?? string.Empty : string.Empty
        };
    }

    /// <summary>
    /// Step 2 – poll until the user completes sign-in, then exchange for a full <see cref="MicrosoftSession"/>.
    /// Throws <see cref="OperationCanceledException"/> if the token expires or the caller cancels.
    /// Throws <see cref="InvalidOperationException"/> with a user-readable message on auth failure.
    /// </summary>
    public static async Task<MicrosoftSession> PollForSessionAsync(
        DeviceCodeChallenge challenge,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var expiryCts = new CancellationTokenSource(TimeSpan.FromSeconds(challenge.ExpiresIn));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, expiryCts.Token);

        var pollInterval = TimeSpan.FromSeconds(Math.Max(challenge.Interval, 5));

        while (true)
        {
            await Task.Delay(pollInterval, linked.Token);

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["client_id"] = ClientId,
                ["device_code"] = challenge.DeviceCode
            });

            using var response = await Http.PostAsync(TokenUrl, content, linked.Token);
            var body = await response.Content.ReadAsStringAsync(linked.Token);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var error))
            {
                var errorCode = error.GetString();
                if (errorCode == "authorization_pending") continue;
                if (errorCode == "slow_down")
                {
                    pollInterval = pollInterval.Add(TimeSpan.FromSeconds(5));
                    continue;
                }
                var errorDesc = root.TryGetProperty("error_description", out var ed)
                    ? ed.GetString() ?? errorCode
                    : errorCode;
                throw new InvalidOperationException($"Microsoft sign-in failed: {errorDesc}");
            }

            // Got MSA tokens.
            var msaAccess = root.GetProperty("access_token").GetString()!;
            var msaRefresh = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? string.Empty : string.Empty;
            var expiresIn = root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;
            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);

            progress?.Report("Microsoft sign-in approved. Connecting to Xbox Live…");
            var (mcToken, uuid, username, capes) = await ExchangeForMinecraftAsync(msaAccess, linked.Token);

            return new MicrosoftSession
            {
                AccessToken = msaAccess,
                RefreshToken = msaRefresh,
                ExpiresAt = expiresAt,
                MinecraftToken = mcToken,
                MinecraftUuid = uuid,
                MinecraftUsername = username,
                Capes = capes
            };
        }
    }

    /// <summary>
    /// Refresh a stored session using its refresh token.
    /// Returns null if the refresh token is invalid or expired.
    /// </summary>
    public static async Task<MicrosoftSession?> TryRefreshSessionAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return null;

        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = ClientId,
                ["refresh_token"] = refreshToken
            });

            using var response = await Http.PostAsync(TokenUrl, content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out _)) return null;

            var msaAccess = root.GetProperty("access_token").GetString()!;
            var msaRefresh = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? refreshToken : refreshToken;
            var expiresIn = root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;
            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);

            var (mcToken, uuid, username, capes) = await ExchangeForMinecraftAsync(msaAccess, cancellationToken);
            return new MicrosoftSession
            {
                AccessToken = msaAccess,
                RefreshToken = msaRefresh,
                ExpiresAt = expiresAt,
                MinecraftToken = mcToken,
                MinecraftUuid = uuid,
                MinecraftUsername = username,
                Capes = capes
            };
        }
        catch
        {
            return null;
        }
    }

    // -------------------------------------------------------------------------
    // Internals
    // -------------------------------------------------------------------------

    private static async Task<(string mcToken, string uuid, string username, IReadOnlyList<MinecraftCape> capes)>
        ExchangeForMinecraftAsync(string msaAccessToken, CancellationToken cancellationToken)
    {
        // 1. Xbox Live auth
        var xblToken = await AuthXboxLiveAsync(msaAccessToken, cancellationToken);

        // 2. XSTS
        var (xstsToken, userHash) = await AuthXstsAsync(xblToken, cancellationToken);

        // 3. Minecraft token
        var mcToken = await AuthMinecraftAsync(xstsToken, userHash, cancellationToken);

        // 4. Profile
        var (uuid, username, capes) = await FetchMinecraftProfileAsync(mcToken, cancellationToken);

        return (mcToken, uuid, username, capes);
    }

    private static async Task<string> AuthXboxLiveAsync(string msaAccessToken, CancellationToken cancellationToken)
    {
        var payload = new
        {
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = $"d={msaAccessToken}"
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, XblAuthUrl);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("Token").GetString()!;
    }

    private static async Task<(string token, string userHash)> AuthXstsAsync(string xblToken, CancellationToken cancellationToken)
    {
        var payload = new
        {
            Properties = new
            {
                SandboxId = "RETAIL",
                UserTokens = new[] { xblToken }
            },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, XstsAuthUrl);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var token = root.GetProperty("Token").GetString()!;
        var userHash = root.GetProperty("DisplayClaims")
                           .GetProperty("xui")[0]
                           .GetProperty("uhs")
                           .GetString()!;
        return (token, userHash);
    }

    private static async Task<string> AuthMinecraftAsync(string xstsToken, string userHash, CancellationToken cancellationToken)
    {
        var payload = new { identityToken = $"XBL3.0 x={userHash};{xstsToken}" };

        using var request = new HttpRequestMessage(HttpMethod.Post, McAuthUrl);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }

    private static async Task<(string uuid, string username, IReadOnlyList<MinecraftCape> capes)>
        FetchMinecraftProfileAsync(string mcToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, McProfileUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mcToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var uuid = root.GetProperty("id").GetString() ?? string.Empty;
        var username = root.GetProperty("name").GetString() ?? string.Empty;

        var capes = new List<MinecraftCape>();
        if (root.TryGetProperty("capes", out var capesArray))
        {
            foreach (var capeElem in capesArray.EnumerateArray())
            {
                var id = capeElem.TryGetProperty("id", out var cid) ? cid.GetString() ?? string.Empty : string.Empty;
                var alias = capeElem.TryGetProperty("alias", out var al) ? al.GetString() ?? string.Empty : string.Empty;
                var state = capeElem.TryGetProperty("state", out var st) ? st.GetString() ?? string.Empty : string.Empty;

                // The texture URL is nested inside the "url" field.
                var textureUrl = string.Empty;
                if (capeElem.TryGetProperty("url", out var urlProp))
                {
                    textureUrl = urlProp.GetString() ?? string.Empty;
                }

                capes.Add(new MinecraftCape
                {
                    Id = id,
                    Alias = alias,
                    TextureUrl = textureUrl,
                    State = state
                });
            }
        }

        return (uuid, username, capes);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("UBLauncher/1.0");
        return client;
    }
}
