using System.Text.Json;

namespace Launcher.Core.Services;

/// <summary>
/// Persists the Microsoft/Minecraft session to disk and provides access to the current session.
/// </summary>
public static class MicrosoftAccountStore
{
    public static event Action? SessionChanged;

    private static readonly string StoreDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UBLauncher");
    private static readonly string SessionFile = Path.Combine(StoreDir, "ms-session.json");

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private static PersistedSession? _current = TryLoad();

    public static bool HasSession => _current is not null;
    public static string? MinecraftUsername => _current?.MinecraftUsername;
    public static string? MinecraftUuid => _current?.MinecraftUuid;
    public static IReadOnlyList<PersistedCape> Capes => _current?.Capes ?? [];

    // -------------------------------------------------------------------------
    // Persisted data model
    // -------------------------------------------------------------------------

    public sealed class PersistedCape
    {
        public string Id { get; init; } = string.Empty;
        public string Alias { get; init; } = string.Empty;
        public string TextureUrl { get; init; } = string.Empty;
        public string State { get; init; } = string.Empty;
    }

    private sealed class PersistedSession
    {
        public string RefreshToken { get; set; } = string.Empty;
        public DateTimeOffset ExpiresAt { get; set; }
        public string MinecraftToken { get; set; } = string.Empty;
        public string MinecraftUuid { get; set; } = string.Empty;
        public string MinecraftUsername { get; set; } = string.Empty;
        public List<PersistedCape> Capes { get; set; } = [];
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>Save a freshly-obtained session from <see cref="MicrosoftAuthService"/>.</summary>
    public static void Save(MicrosoftAuthService.MicrosoftSession session)
    {
        _current = new PersistedSession
        {
            RefreshToken = session.RefreshToken,
            ExpiresAt = session.ExpiresAt,
            MinecraftToken = session.MinecraftToken,
            MinecraftUuid = session.MinecraftUuid,
            MinecraftUsername = session.MinecraftUsername,
            Capes = session.Capes.Select(cape => new PersistedCape
            {
                Id = cape.Id,
                Alias = cape.Alias,
                TextureUrl = cape.TextureUrl,
                State = cape.State
            }).ToList()
        };
        Flush();
        SessionChanged?.Invoke();
    }

    /// <summary>Remove the stored session and sign the user out.</summary>
    public static void Clear()
    {
        _current = null;
        if (File.Exists(SessionFile))
        {
            try { File.Delete(SessionFile); } catch { /* best-effort */ }
        }
        SessionChanged?.Invoke();
    }

    /// <summary>
    /// Try to refresh the stored session in the background.
    /// Returns true if the session was refreshed successfully.
    /// </summary>
    public static async Task<bool> TryRefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_current is null) return false;
        if (DateTimeOffset.UtcNow < _current.ExpiresAt.AddMinutes(-5)) return true; // still valid

        var refreshed = await MicrosoftAuthService.TryRefreshSessionAsync(_current.RefreshToken, cancellationToken);
        if (refreshed is null) return false;

        Save(refreshed);
        return true;
    }

    // -------------------------------------------------------------------------
    // Internals
    // -------------------------------------------------------------------------

    private static void Flush()
    {
        try
        {
            Directory.CreateDirectory(StoreDir);
            File.WriteAllText(SessionFile, JsonSerializer.Serialize(_current, SerializerOptions));
        }
        catch
        {
            // Best-effort — the in-memory session is still valid.
        }
    }

    private static PersistedSession? TryLoad()
    {
        try
        {
            if (!File.Exists(SessionFile)) return null;
            return JsonSerializer.Deserialize<PersistedSession>(File.ReadAllText(SessionFile));
        }
        catch
        {
            return null;
        }
    }
}
