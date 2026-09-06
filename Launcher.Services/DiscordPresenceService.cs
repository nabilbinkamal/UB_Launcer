using System.Diagnostics;
using DiscordRPC;
using Serilog;

namespace Launcher.Services;

public sealed record DiscordPresenceOptions(
    string ApplicationId,
    string LargeImageKey = "",
    string CommunityUrl = "");

public static class DiscordPresenceService
{
    private static readonly object Gate = new();
    private static readonly DateTime LauncherStartedAtUtc = DateTime.UtcNow;

    private static DiscordRpcClient? client;
    private static DiscordPresenceOptions options = new(string.Empty);
    private static PresenceState desiredPresence = PresenceState.Launcher("Player", LauncherStartedAtUtc);
    private static string launcherUsername = "Player";
    private static bool enabled = true;
    private static int gameSessionVersion;

    public static void Configure(DiscordPresenceOptions configuration)
    {
        var normalized = Normalize(configuration);
        lock (Gate)
        {
            var applicationChanged = !string.Equals(
                options.ApplicationId,
                normalized.ApplicationId,
                StringComparison.Ordinal);

            options = normalized;
            if (applicationChanged)
            {
                DisposeClientLocked(clearPresence: true);
            }

            ApplyDesiredPresenceLocked();
        }
    }

    public static void SetEnabled(bool value)
    {
        lock (Gate)
        {
            enabled = value;
            if (!enabled)
            {
                DisposeClientLocked(clearPresence: true);
                return;
            }

            ApplyDesiredPresenceLocked();
        }
    }

    public static void ShowLauncher(string username)
    {
        lock (Gate)
        {
            gameSessionVersion++;
            launcherUsername = NormalizeUsername(username);
            desiredPresence = PresenceState.Launcher(launcherUsername, LauncherStartedAtUtc);
            ApplyDesiredPresenceLocked();
        }
    }

    public static void UpdateUsername(string username)
    {
        lock (Gate)
        {
            launcherUsername = NormalizeUsername(username);
            if (desiredPresence.IsGame)
            {
                return;
            }

            desiredPresence = PresenceState.Launcher(launcherUsername, LauncherStartedAtUtc);
            ApplyDesiredPresenceLocked();
        }
    }

    public static void TrackGame(
        Process gameProcess,
        string instanceName,
        string minecraftVersion,
        string loaderType,
        string username)
    {
        if (gameProcess.HasExited)
        {
            return;
        }

        int sessionVersion;
        lock (Gate)
        {
            launcherUsername = NormalizeUsername(username);
            sessionVersion = ++gameSessionVersion;
            desiredPresence = PresenceState.Game(
                NormalizeText(instanceName, "Minecraft"),
                NormalizeText(minecraftVersion, "Unknown version"),
                NormalizeText(loaderType, "Minecraft"),
                launcherUsername,
                DateTime.UtcNow);
            ApplyDesiredPresenceLocked();
        }

        _ = RestoreLauncherWhenGameExitsAsync(gameProcess, sessionVersion);
    }

    public static void Shutdown()
    {
        lock (Gate)
        {
            gameSessionVersion++;
            DisposeClientLocked(clearPresence: true);
        }
    }

    private static async Task RestoreLauncherWhenGameExitsAsync(Process gameProcess, int sessionVersion)
    {
        try
        {
            await gameProcess.WaitForExitAsync();
        }
        catch
        {
        }

        lock (Gate)
        {
            if (sessionVersion != gameSessionVersion)
            {
                return;
            }

            desiredPresence = PresenceState.Launcher(launcherUsername, LauncherStartedAtUtc);
            ApplyDesiredPresenceLocked();
        }
    }

    private static void ApplyDesiredPresenceLocked()
    {
        if (!enabled || !TryEnsureClientLocked())
        {
            return;
        }

        try
        {
            var presence = new RichPresence
            {
                Type = ActivityType.Playing,
                Details = desiredPresence.Details,
                State = desiredPresence.State,
                Timestamps = new Timestamps { Start = desiredPresence.StartedAtUtc }
            };

            if (!string.IsNullOrWhiteSpace(options.LargeImageKey))
            {
                presence.Assets = new Assets
                {
                    LargeImageKey = options.LargeImageKey,
                    LargeImageText = desiredPresence.IsGame
                        ? $"Minecraft {desiredPresence.MinecraftVersion}"
                        : "UB Launcher"
                };
            }

            if (!string.IsNullOrWhiteSpace(options.CommunityUrl))
            {
                presence.Buttons =
                [
                    new Button
                    {
                        Label = "Join UB Community",
                        Url = options.CommunityUrl
                    }
                ];
            }

            client!.SetPresence(presence);
        }
        catch (Exception exception)
        {
            Log.Debug(exception, "Discord Rich Presence update was skipped.");
            DisposeClientLocked(clearPresence: false);
        }
    }

    private static bool TryEnsureClientLocked()
    {
        if (client is { IsDisposed: false, IsInitialized: true })
        {
            return true;
        }

        if (!IsValidApplicationId(options.ApplicationId))
        {
            return false;
        }

        try
        {
            client = new DiscordRpcClient(options.ApplicationId)
            {
                SkipIdenticalPresence = true
            };
            client.OnError += (_, eventArgs) =>
                Log.Debug("Discord Rich Presence error {Code}: {Message}", eventArgs.Code, eventArgs.Message);

            if (client.Initialize())
            {
                return true;
            }
        }
        catch (Exception exception)
        {
            Log.Debug(exception, "Discord Rich Presence could not connect.");
        }

        DisposeClientLocked(clearPresence: false);
        return false;
    }

    private static void DisposeClientLocked(bool clearPresence)
    {
        var existingClient = client;
        client = null;
        if (existingClient is null)
        {
            return;
        }

        try
        {
            if (clearPresence && existingClient.IsInitialized)
            {
                existingClient.ClearPresence();
            }

            existingClient.Dispose();
        }
        catch
        {
        }
    }

    private static DiscordPresenceOptions Normalize(DiscordPresenceOptions value)
    {
        var applicationId = value.ApplicationId?.Trim() ?? string.Empty;
        var imageKey = value.LargeImageKey?.Trim() ?? string.Empty;
        var communityUrl = Uri.TryCreate(value.CommunityUrl?.Trim(), UriKind.Absolute, out var uri) &&
                           uri.Scheme == Uri.UriSchemeHttps
            ? uri.AbsoluteUri
            : string.Empty;
        return new DiscordPresenceOptions(applicationId, imageKey, communityUrl);
    }

    private static bool IsValidApplicationId(string value) =>
        value.Length is >= 16 and <= 20 && ulong.TryParse(value, out _);

    private static string NormalizeUsername(string value) => NormalizeText(value, "Player");

    private static string NormalizeText(string value, string fallback)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return fallback;
        }

        return normalized[..Math.Min(normalized.Length, 120)];
    }

    private sealed record PresenceState(
        bool IsGame,
        string Details,
        string State,
        string MinecraftVersion,
        DateTime StartedAtUtc)
    {
        public static PresenceState Launcher(string username, DateTime startedAtUtc) =>
            new(false, "Playing in UBLauncher", $"Signed as {username}", string.Empty, startedAtUtc);

        public static PresenceState Game(
            string instanceName,
            string version,
            string loaderType,
            string username,
            DateTime startedAtUtc) =>
            new(true, $"Playing {instanceName}", $"{loaderType} {version} • {username}", version, startedAtUtc);
    }
}
