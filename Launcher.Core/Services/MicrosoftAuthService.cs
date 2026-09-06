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
    private const string ClientId = "c36a9fb6-4f2a-41ff-90bd-ae7cc92031eb";
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

    /// <summary>
    /// Authenticates using the OAuth 2.0 Authorization Code flow with a localhost loopback.
    /// Opens the user's default browser to log in securely.
    /// </summary>
    public static async Task<MicrosoftSession> AuthenticateInteractiveAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        int port = 59125;
        try
        {
            var tcpListener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            tcpListener.Start();
            port = ((System.Net.IPEndPoint)tcpListener.LocalEndpoint).Port;
            tcpListener.Stop();
        }
        catch { }

        var redirectUri = $"http://localhost:{port}/";
        var authorizeUrl = $"https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize?client_id={ClientId}&response_type=code&redirect_uri={Uri.EscapeDataString(redirectUri)}&scope={Uri.EscapeDataString("XboxLive.signin offline_access")}";

        using var listener = new System.Net.HttpListener();
        listener.Prefixes.Add(redirectUri);
        try
        {
            listener.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not start local web server for authentication. Is port {port} in use? ({ex.Message})");
        }

        // Open the browser
        progress?.Report("Waiting for you to sign in via your browser…");
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = authorizeUrl, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not open the browser. Please manually navigate to: {authorizeUrl}", ex);
        }

        // Wait for the redirect
        System.Net.HttpListenerContext context;
        using (cancellationToken.Register(() => listener.Stop()))
        {
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception)
            {
                throw new OperationCanceledException("Authentication was canceled.");
            }
        }

        var requestUrl = context.Request.Url;
        string? code = null;
        string? errorMsg = null;

        if (requestUrl != null)
        {
            var query = requestUrl.Query.TrimStart('?').Split('&')
                .Select(q => q.Split('='))
                .Where(q => q.Length >= 2)
                .ToDictionary(q => q[0], q => Uri.UnescapeDataString(q[1]));

            code = query.TryGetValue("code", out var c) ? c : null;
            errorMsg = query.TryGetValue("error_description", out var ed) ? ed : (query.TryGetValue("error", out var e) ? e : null);
        }

        // Send a response to the browser
        var responseString = string.IsNullOrEmpty(code)
            ? $"<html><body style='font-family: sans-serif; text-align: center; margin-top: 50px; background: #0E1A25; color: #fff;'><h1>Authentication Failed</h1><p>{errorMsg}</p></body></html>"
            : "<html><body style='font-family: sans-serif; text-align: center; margin-top: 50px; background: #0E1A25; color: #fff;'><h1>Authentication Successful!</h1><p style='color: #68DDA8;'>You can now safely close this window and return to UB Launcher.</p><script>window.close();</script></body></html>";

        var buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
        context.Response.ContentLength64 = buffer.Length;
        try
        {
            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            context.Response.OutputStream.Close();
        }
        catch { /* Ignore if browser closes connection early */ }

        listener.Stop();

        if (string.IsNullOrEmpty(code))
        {
            throw new InvalidOperationException($"Microsoft sign-in failed: {errorMsg ?? "No code received."}");
        }

        progress?.Report("Microsoft sign-in approved. Exchanging code for tokens…");

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = ClientId,
            ["code"] = code,
            ["redirect_uri"] = redirectUri
        });

        using var tokenResponse = await Http.PostAsync(TokenUrl, content, cancellationToken);
        var body = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var tokenError))
        {
            var errorDesc = root.TryGetProperty("error_description", out var td)
                ? td.GetString() ?? tokenError.GetString()
                : tokenError.GetString();
            throw new InvalidOperationException($"Microsoft sign-in failed during token exchange: {errorDesc}");
        }

        var msaAccess = root.GetProperty("access_token").GetString()!;
        var msaRefresh = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? string.Empty : string.Empty;
        var expiresIn = root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);

        progress?.Report("Connecting to Xbox Live…");
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
