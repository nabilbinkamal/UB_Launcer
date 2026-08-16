using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using System.Diagnostics;

namespace Minecraft.Runtime;

public sealed record MinecraftLaunchConfiguration
{
    public int MaximumRamMb { get; init; } = 4096;
    public bool FullScreen { get; init; }
    public int ScreenWidth { get; init; } = 854;
    public int ScreenHeight { get; init; } = 480;
    public string Java25Path { get; init; } = string.Empty;
    public string Java21Path { get; init; } = string.Empty;
    public string Java17Path { get; init; } = string.Empty;
    public string Java8Path { get; init; } = string.Empty;
}

public sealed class MinecraftLaunchService
{
    private readonly MinecraftLauncher launcher;

    public MinecraftLaunchService(string? gameDirectory = null)
    {
        var root = string.IsNullOrWhiteSpace(gameDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UBLauncher", "minecraft")
            : gameDirectory;
        Directory.CreateDirectory(root);
        launcher = new MinecraftLauncher(new MinecraftPath(root));
    }

    public async Task<Process> LaunchOfflineAsync(
        string username,
        string version,
        MinecraftLaunchConfiguration configuration,
        IProgress<string>? progress = null,
        string? serverAddress = null)
    {
        if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("Enter an offline username first.", nameof(username));
        if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("Select a Minecraft version first.", nameof(version));

        (string Host, int Port)? serverEndpoint = string.IsNullOrWhiteSpace(serverAddress)
            ? null
            : ParseServerEndpoint(serverAddress);

        progress?.Report($"Installing Minecraft {version}...");
        await launcher.InstallAsync(version);
        progress?.Report("Preparing Minecraft process...");

        var requiredJavaMajor = await GetRequiredJavaMajorVersionAsync(version);
        var javaPath = requiredJavaMajor switch
        {
            >= 25 => configuration.Java25Path,
            >= 21 => configuration.Java21Path,
            >= 16 => configuration.Java17Path,
            _ => configuration.Java8Path
        };

        var launchOption = new MLaunchOption
        {
            Session = MSession.CreateOfflineSession(username),
            MaximumRamMb = Math.Max(512, configuration.MaximumRamMb),
            FullScreen = configuration.FullScreen,
            ScreenWidth = Math.Max(320, configuration.ScreenWidth),
            ScreenHeight = Math.Max(240, configuration.ScreenHeight)
        };

        if (serverEndpoint is not null)
        {
            launchOption.ServerIp = serverEndpoint.Value.Host;
            launchOption.ServerPort = serverEndpoint.Value.Port;
            progress?.Report($"Connecting to {FormatServerEndpoint(serverEndpoint.Value)}...");
        }

        if (!string.IsNullOrWhiteSpace(javaPath))
        {
            launchOption.JavaPath = javaPath;
        }

        var process = await launcher.BuildProcessAsync(version, launchOption);
        process.Start();
        return process;
    }

    private static (string Host, int Port) ParseServerEndpoint(string serverAddress)
    {
        const int defaultMinecraftPort = 25565;
        var address = serverAddress.Trim();
        const string minecraftScheme = "minecraft://";
        if (address.StartsWith(minecraftScheme, StringComparison.OrdinalIgnoreCase))
        {
            address = address[minecraftScheme.Length..];
        }

        if (string.IsNullOrWhiteSpace(address) ||
            address.Any(character => char.IsWhiteSpace(character) || character is '/' or '?' or '#'))
        {
            throw new ArgumentException("Enter a valid Minecraft server address, such as play.example.com:25565.", nameof(serverAddress));
        }

        string host;
        var port = defaultMinecraftPort;
        if (address[0] == '[')
        {
            var closingBracket = address.IndexOf(']');
            if (closingBracket <= 1)
            {
                throw new ArgumentException("The Minecraft server IPv6 address is invalid.", nameof(serverAddress));
            }

            host = address[1..closingBracket];
            var remainder = address[(closingBracket + 1)..];
            if (!string.IsNullOrEmpty(remainder))
            {
                if (remainder[0] != ':' || !TryParsePort(remainder[1..], out port))
                {
                    throw new ArgumentException("The Minecraft server port must be between 1 and 65535.", nameof(serverAddress));
                }
            }
        }
        else
        {
            var firstColon = address.IndexOf(':');
            var lastColon = address.LastIndexOf(':');
            if (firstColon >= 0 && firstColon == lastColon)
            {
                host = address[..firstColon];
                if (!TryParsePort(address[(firstColon + 1)..], out port))
                {
                    throw new ArgumentException("The Minecraft server port must be between 1 and 65535.", nameof(serverAddress));
                }
            }
            else
            {
                // An unbracketed address containing multiple colons is an IPv6 host
                // using Minecraft's default port. Use [host]:port for a custom port.
                host = address;
            }
        }

        if (string.IsNullOrWhiteSpace(host) || host.Any(character => character is '[' or ']'))
        {
            throw new ArgumentException("Enter a valid Minecraft server host.", nameof(serverAddress));
        }

        return (host, port);
    }

    private static bool TryParsePort(string value, out int port) =>
        int.TryParse(value, out port) && port is >= 1 and <= 65535;

    private static string FormatServerEndpoint((string Host, int Port) endpoint) =>
        endpoint.Host.Contains(':')
            ? $"[{endpoint.Host}]:{endpoint.Port}"
            : $"{endpoint.Host}:{endpoint.Port}";

    public async Task<int> GetRequiredJavaMajorVersionAsync(string version)
    {
        try
        {
            var versionInfo = await launcher.GetVersionAsync(version);
            if (int.TryParse(versionInfo.JavaVersion?.MajorVersion, out var majorVersion) && majorVersion > 0)
            {
                return majorVersion;
            }
        }
        catch
        {
        }

        return EstimateJavaMajorVersion(version);
    }

    public static int EstimateJavaMajorVersion(string version)
    {
        var numericVersion = new string(version.TakeWhile(character => char.IsDigit(character) || character == '.').ToArray());
        var parts = numericVersion.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !int.TryParse(parts[0], out var first))
        {
            return 21;
        }

        if (first >= 2)
        {
            return 21;
        }

        var minor = parts.Length > 1 && int.TryParse(parts[1], out var parsedMinor) ? parsedMinor : 0;
        var patch = parts.Length > 2 && int.TryParse(parts[2], out var parsedPatch) ? parsedPatch : 0;
        if (minor > 20 || minor == 20 && patch >= 5)
        {
            return 21;
        }

        if (minor >= 18)
        {
            return 17;
        }

        return minor == 17 ? 16 : 8;
    }

    public async Task InstallVersionAsync(string version, IProgress<string>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("A Minecraft version is required.", nameof(version));
        }

        progress?.Report($"Downloading Minecraft {version}...");
        await launcher.InstallAsync(version);
        progress?.Report($"Minecraft {version} is ready.");
    }

    public async Task<IReadOnlyList<string>> GetAvailableReleaseVersionsAsync()
    {
        var versions = await launcher.GetAllVersionsAsync();
        return versions
            .Where(version => string.Equals(version.Type?.ToString(), "release", StringComparison.OrdinalIgnoreCase))
            .Select(version => version.Name)
            .Where(version => !string.IsNullOrWhiteSpace(version))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
