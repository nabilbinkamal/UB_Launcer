using System.Text.Json;
using Minecraft.Runtime;

namespace Launcher.Core.Services;

public sealed record LauncherGameSettings
{
    public bool EnableUbClient { get; init; }
    public int MaximumMemoryMb { get; init; } = 4096;
    public bool FullScreen { get; init; }
    public int ScreenWidth { get; init; } = 854;
    public int ScreenHeight { get; init; } = 480;
    public string Java25Path { get; init; } = string.Empty;
    public string Java21Path { get; init; } = string.Empty;
    public string Java17Path { get; init; } = string.Empty;
    public string Java8Path { get; init; } = string.Empty;
    public string CustomJvmArguments { get; init; } = string.Empty;
    public bool MaxPerformanceMode { get; init; } = true;
    public bool EnableDiscordRpc { get; init; } = true;
}

public static class LauncherGameSettingsStore
{
    private static readonly object Gate = new();
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UBLauncher",
        "game-settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static LauncherGameSettings current = Load();
    private static Timer? saveTimer;

    public static event Action? Changed;

    public static LauncherGameSettings Current
    {
        get
        {
            lock (Gate)
            {
                return current;
            }
        }
    }

    public static void Update(LauncherGameSettings settings)
    {
        lock (Gate)
        {
            current = Normalize(settings);
            saveTimer ??= new Timer(_ => SaveSnapshot(), null, Timeout.Infinite, Timeout.Infinite);
            saveTimer.Change(300, Timeout.Infinite);
        }

        Changed?.Invoke();
    }

    public static string GetJavaPath(int majorVersion)
    {
        var settings = Current;
        return majorVersion switch
        {
            >= 25 => settings.Java25Path,
            >= 21 => settings.Java21Path,
            >= 17 => settings.Java17Path,
            _ => settings.Java8Path
        };
    }

    public static MinecraftLaunchConfiguration CreateLaunchConfiguration()
    {
        var settings = Current;
        return new MinecraftLaunchConfiguration
        {
            EnableUbClient = settings.EnableUbClient,
            MaximumRamMb = settings.MaximumMemoryMb,
            FullScreen = settings.FullScreen,
            ScreenWidth = settings.ScreenWidth,
            ScreenHeight = settings.ScreenHeight,
            Java25Path = settings.Java25Path,
            Java21Path = settings.Java21Path,
            Java17Path = settings.Java17Path,
            Java8Path = settings.Java8Path,
            CustomJvmArguments = settings.CustomJvmArguments,
            MaxPerformanceMode = settings.MaxPerformanceMode
        };
    }

    private static LauncherGameSettings Normalize(LauncherGameSettings settings) => settings with
    {
        EnableUbClient = false,
        MaximumMemoryMb = Math.Clamp(settings.MaximumMemoryMb, 512, 131072),
        ScreenWidth = Math.Clamp(settings.ScreenWidth, 320, 16384),
        ScreenHeight = Math.Clamp(settings.ScreenHeight, 240, 16384),
        Java25Path = settings.Java25Path?.Trim() ?? string.Empty,
        Java21Path = settings.Java21Path?.Trim() ?? string.Empty,
        Java17Path = settings.Java17Path?.Trim() ?? string.Empty,
        Java8Path = settings.Java8Path?.Trim() ?? string.Empty,
        CustomJvmArguments = settings.CustomJvmArguments?.Trim() ?? string.Empty
    };

    private static LauncherGameSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new LauncherGameSettings();
            }

            var json = File.ReadAllText(SettingsPath);
            return Normalize(JsonSerializer.Deserialize<LauncherGameSettings>(json) ?? new LauncherGameSettings());
        }
        catch
        {
            return new LauncherGameSettings();
        }
    }

    private static void SaveSnapshot()
    {
        LauncherGameSettings snapshot;
        lock (Gate)
        {
            snapshot = current;
        }

        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(snapshot, JsonOptions));
        }
        catch
        {
        }
    }
}
