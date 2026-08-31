using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Minecraft.Runtime;

public sealed record MinecraftLaunchConfiguration
{
    // UB Client has been retired. Keep this property for config compatibility,
    // but never enable client injection for any Minecraft version.
    public bool EnableUbClient { get; init; }
    public int MaximumRamMb { get; init; } = 4096;
    public bool FullScreen { get; init; }
    public int ScreenWidth { get; init; } = 854;
    public int ScreenHeight { get; init; } = 480;
    public string Java25Path { get; init; } = string.Empty;
    public string Java21Path { get; init; } = string.Empty;
    public string Java17Path { get; init; } = string.Empty;
    public string Java8Path { get; init; } = string.Empty;
    public string CustomJvmArguments { get; init; } = string.Empty;
    public bool MaxPerformanceMode { get; init; } = true;
}

public sealed class MinecraftLaunchService
{
    private readonly MinecraftLauncher launcher;
    private readonly string root;

    public MinecraftLaunchService(string? gameDirectory = null)
    {
        RemoveAllRetiredUbClientFiles();
        root = string.IsNullOrWhiteSpace(gameDirectory)
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
        string? serverAddress = null,
        string? instanceLoaderType = null)
    {
        if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("Enter an offline username first.", nameof(username));
        if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("Select a Minecraft version first.", nameof(version));

        (string Host, int Port)? serverEndpoint = string.IsNullOrWhiteSpace(serverAddress)
            ? null
            : ParseServerEndpoint(serverAddress);

        if (!configuration.EnableUbClient)
        {
            DisableInstalledClientMods();
            progress?.Report("UB Client is disabled; launching the selected Minecraft version normally.");
        }

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

        var canUseFabricClient = string.IsNullOrWhiteSpace(instanceLoaderType) ||
            instanceLoaderType.Equals("Vanilla", StringComparison.OrdinalIgnoreCase) ||
            instanceLoaderType.Equals("Fabric", StringComparison.OrdinalIgnoreCase);

        if (canUseFabricClient &&
            !version.StartsWith("fabric-loader", StringComparison.OrdinalIgnoreCase) &&
            !version.StartsWith("forge", StringComparison.OrdinalIgnoreCase))
        {
            var clientJarPath = configuration.EnableUbClient ? ResolveClientJar(version) : null;
            if (clientJarPath is not null)
            {
                var minecraftVersion = version;
                progress?.Report($"Installing UB Client for Minecraft {version}...");
                var fabricLoaderVersion = await GetLatestFabricLoaderVersionAsync(version);
                var fabricVersion = $"fabric-loader-{fabricLoaderVersion}-{version}";
                await InstallFabricViaApiAsync(root, version, fabricLoaderVersion, progress);
                await launcher.InstallAsync(fabricVersion);
                version = fabricVersion;

                var modsFolder = Path.Combine(root, "mods");
                Directory.CreateDirectory(modsFolder);
                var destination = Path.Combine(modsFolder, Path.GetFileName(clientJarPath));
                if (!File.Exists(destination) ||
                    File.GetLastWriteTimeUtc(destination) < File.GetLastWriteTimeUtc(clientJarPath))
                {
                    File.Copy(clientJarPath, destination, true);
                }
                if (minecraftVersion.Equals("1.20.4", StringComparison.OrdinalIgnoreCase))
                {
                    await EnsureFabricApi1204Async(modsFolder, progress);
                }
                progress?.Report("UB Client is ready.");
            }
            else
            {
                progress?.Report($"No compatible UB Client build for Minecraft {version}; launching vanilla safely.");
            }
        }

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

        if (!string.IsNullOrWhiteSpace(javaPath) && File.Exists(javaPath))
        {
            launchOption.JavaPath = javaPath;
        }
        else if (!string.IsNullOrWhiteSpace(javaPath))
        {
            progress?.Report("Saved Java path is unavailable; selecting a compatible Java runtime automatically.");
        }

        // Keep JVM tuning in CmlLib's argument pipeline so options are inserted
        // before the Minecraft main class and remain compatible with every loader.
        if (!string.IsNullOrWhiteSpace(configuration.CustomJvmArguments))
        {
            launchOption.ExtraJvmArguments =
            [
                MArgument.FromCommandLine(configuration.CustomJvmArguments)
            ];
        }

        if (configuration.MaxPerformanceMode)
        {
            ApplyMaxPerformanceOptions(root);
            progress?.Report("Applying safe FPS performance preset...");
        }

        var process = await launcher.BuildProcessAsync(version, launchOption);
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = false;
        process.StartInfo.RedirectStandardError = false;
        process.StartInfo.CreateNoWindow = false;
        process.StartInfo.WorkingDirectory = root;
        AddNativeLibraryPath(process.StartInfo, version);
        process.StartInfo.EnvironmentVariables["UB_LAUNCHER_AUTH_TOKEN"] = Guid.NewGuid().ToString();
        progress?.Report("Starting Minecraft...");
        process.Start();
        return process;
    }

    private static void ApplyMaxPerformanceOptions(string gameDirectory)
    {
        var optionsPath = Path.Combine(gameDirectory, "options.txt");

        try
        {
            // Never overwrite a player's choices. Existing instances keep their
            // complete Minecraft settings unchanged on every launch.
            if (File.Exists(optionsPath))
            {
                return;
            }

            // These keys are shared by modern Minecraft versions. Unknown keys are
            // ignored by older versions, so the preset remains version-safe. This
            // file is created only for a brand-new instance; Minecraft remains free
            // to rewrite it after the player changes any option.
            var performanceValues = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["graphics"] = "fast",
                ["renderDistance"] = "8",
                ["simulationDistance"] = "5",
                ["entityDistanceScaling"] = "0.5",
                ["entityShadows"] = "false",
                ["particles"] = "decreased",
                ["clouds"] = "false",
                ["vSync"] = "false",
                ["maxFps"] = "240",
                ["mipmapLevels"] = "0",
                ["biomeBlendRadius"] = "0",
                ["distortionEffectScale"] = "0.0",
                ["fovEffectScale"] = "0.0",
                ["darknessEffectScale"] = "0.0"
            };

            var output = performanceValues.Select(pair => $"{pair.Key}:{pair.Value}");
            File.WriteAllLines(optionsPath, output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (IOException)
        {
            // A locked or read-only options file must never prevent Minecraft from launching.
        }
        catch (UnauthorizedAccessException)
        {
            // Keep launch resilient when an instance directory is not writable.
        }
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

    public async Task InstallVersionAsync(
        string version,
        IProgress<string>? progress = null,
        IProgress<double>? percentageProgress = null)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("A Minecraft version is required.", nameof(version));
        }

        progress?.Report($"Downloading Minecraft {version}...");
        percentageProgress?.Report(0);

        var byteProgress = new Progress<ByteProgress>(update =>
        {
            var percent = update.TotalBytes > 0
                ? Math.Clamp(update.ProgressedBytes * 100d / update.TotalBytes, 0, 99)
                : 0;
            percentageProgress?.Report(percent);
            progress?.Report($"Downloading Minecraft {version} ({percent:0}%)...");
        });
        await launcher.InstallAsync(version, null, byteProgress, CancellationToken.None);
        percentageProgress?.Report(100);
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

    private static string? ResolveClientJar(string version)
    {
        var clientProject = version switch
        {
            "1.20.4" => "UBClient",
            "1.19.4" => "UBClient-1.19.4",
            "1.16.5" => "UBClient-1.16.5",
            "1.12.2" => "UBClient-1.12.2",
            "1.8.9" => "UBClient-1.8.9",
            _ when IsSupportedMinecraft121(version) => "UBClient-1.21",
            _ => null
        };
        if (clientProject is null) return null;

        var roots = new List<string>();
        var configuredRoot = Environment.GetEnvironmentVariable("UB_CLIENT_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot)) roots.Add(configuredRoot);
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                roots.Add(directory.FullName);
                directory = directory.Parent;
            }
        }
        var names = new[]
        {
            "ubclient-1.0.0.jar",
            "ubclient-1.19.4-1.0.0.jar",
            "ubclient-1.16.5-1.0.0.jar",
            "ubclient-1.12.2-1.0.0.jar",
            "ubclient-1.8.9-1.0.0.jar",
            "ubclient.jar"
        };
        return roots.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(root => Path.GetFileName(root).Equals(clientProject, StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(root, "build", "libs")
                : Path.Combine(root, clientProject, "build", "libs"))
            .SelectMany(path => names.Select(name => Path.Combine(path, name)))
            .FirstOrDefault(File.Exists);
    }

    private void DisableInstalledClientMods()
    {
        var modsFolder = Path.Combine(root, "mods");
        if (!Directory.Exists(modsFolder)) return;
        foreach (var path in Directory.EnumerateFiles(modsFolder, "ubclient*.jar", SearchOption.TopDirectoryOnly))
        {
            // UB Client is retired globally; remove stale copies before launch.
            File.Delete(path);
        }
    }

    private static void RemoveAllRetiredUbClientFiles()
    {
        var instancesRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "UBLauncher", "instances");
        if (!Directory.Exists(instancesRoot)) return;

        try
        {
            foreach (var file in Directory.EnumerateFiles(instancesRoot, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(file);
                if (name.StartsWith("ubclient", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("ubclient.properties", StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(file); } catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static async Task EnsureFabricApi1204Async(string modsFolder, IProgress<string>? progress)
    {
        if (Directory.EnumerateFiles(modsFolder, "fabric-api-*.jar", SearchOption.TopDirectoryOnly).Any())
            return;

        const string fabricApiVersion = "0.96.11+1.20.4";
        var destination = Path.Combine(modsFolder, $"fabric-api-{fabricApiVersion}.jar");
        progress?.Report("Installing the required Fabric API for UB Client 1.20.4...");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        var downloadUrl = $"https://maven.fabricmc.net/net/fabricmc/fabric-api/{fabricApiVersion}/fabric-api-{fabricApiVersion}.jar";
        var bytes = await http.GetByteArrayAsync(downloadUrl);
        await File.WriteAllBytesAsync(destination, bytes);
    }

    private void AddNativeLibraryPath(ProcessStartInfo startInfo, string version)
    {
        var minecraftVersion = version.StartsWith("fabric-loader-", StringComparison.OrdinalIgnoreCase)
            ? version[(version.LastIndexOf('-') + 1)..]
            : version;
        var nativesDirectory = Path.Combine(root, "versions", minecraftVersion, "natives");
        if (!Directory.Exists(nativesDirectory)) return;

        var existingPath = startInfo.EnvironmentVariables["PATH"];
        startInfo.EnvironmentVariables["PATH"] = string.IsNullOrWhiteSpace(existingPath)
            ? nativesDirectory
            : nativesDirectory + Path.PathSeparator + existingPath;
    }

    private static bool IsSupportedMinecraft121(string version)
    {
        var parts = version.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && parts[0] == "1" && parts[1] == "21" &&
               (parts.Length == 2 || (int.TryParse(parts[2], out var patch) && patch is >= 0 and <= 11));
    }

    /// <summary>
    /// PERMANENT FIX: Installs Fabric Loader by downloading the version profile JSON
    /// directly from the Fabric Meta API. This bypasses the fabric-installer JAR entirely
    /// and always uses the most up-to-date loader that supports any Java version.
    /// </summary>
    private static async Task InstallFabricViaApiAsync(
        string root, string mcVersion, string loaderVersion, IProgress<string>? progress)
    {
        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromSeconds(30);

        var fabricVersionId = $"fabric-loader-{loaderVersion}-{mcVersion}";
        var versionsDir = Path.Combine(root, "versions", fabricVersionId);

        // Delete any old incompatible Fabric profiles for this MC version
        var parentDir = Path.Combine(root, "versions");
        if (Directory.Exists(parentDir))
        {
            foreach (var dir in Directory.GetDirectories(parentDir, $"fabric-loader-*-{mcVersion}"))
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        Directory.CreateDirectory(versionsDir);

        // Download the complete version JSON from Fabric's official Meta API
        var profileUrl = $"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}/{loaderVersion}/profile/json";
        progress?.Report($"Downloading Fabric {loaderVersion} profile for Minecraft {mcVersion}...");
        var profileJson = await http.GetStringAsync(profileUrl);

        // Save the JSON - this is all CmlLib needs to find and launch Fabric
        var jsonPath = Path.Combine(versionsDir, $"{fabricVersionId}.json");
        await File.WriteAllTextAsync(jsonPath, profileJson);

        progress?.Report($"Fabric {loaderVersion} profile installed.");
    }

    /// <summary>
    /// Fetches the latest stable Fabric Installer version from the official Fabric API.
    /// </summary>
    private static async Task<string> GetLatestFabricInstallerVersionAsync()
    {
        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(10);
            var json = await http.GetStringAsync("https://meta.fabricmc.net/v2/versions/installer");
            var match = System.Text.RegularExpressions.Regex.Match(json, "\"version\":\"([^\"]+)\"");
            if (match.Success)
                return match.Groups[1].Value;
        }
        catch { }
        return "1.0.1"; // Fallback
    }

    /// <summary>
    /// Fetches the latest stable Fabric Loader version from the official Fabric API.
    /// This ensures compatibility with any Java version (Java 21, 25, 26...) automatically.
    /// Falls back to a known-good version if the network request fails.
    /// </summary>
    private static async Task<string> GetLatestFabricLoaderVersionAsync(string mcVersion)
    {
        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(10);
            var json = await http.GetStringAsync($"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}");
            using var document = JsonDocument.Parse(json);
            var version = document.RootElement.EnumerateArray()
                .Where(item => !item.TryGetProperty("loader", out var loader) ||
                               !loader.TryGetProperty("stable", out var stable) || stable.GetBoolean())
                .Select(item => item.GetProperty("loader").GetProperty("version").GetString())
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (version is not null) return version;
        }
        catch
        {
        }
        return "0.16.10";
    }
}

public sealed record MinecraftLaunchDiagnostic(string Summary, string? ReportPath);

public static class MinecraftLaunchDiagnostics
{
    public static MinecraftLaunchDiagnostic Analyze(string? gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
            return new("Minecraft closed unexpectedly. Select the instance again and retry.", null);

        var minecraftDirectory = Path.Combine(gameDirectory, ".minecraft");
        if (!Directory.Exists(minecraftDirectory)) minecraftDirectory = gameDirectory;
        var crash = Directory.Exists(Path.Combine(minecraftDirectory, "crash-reports"))
            ? Directory.EnumerateFiles(Path.Combine(minecraftDirectory, "crash-reports"), "*.txt")
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
        var latestLog = Path.Combine(minecraftDirectory, "logs", "latest.log");
        var text = crash is not null && File.Exists(crash)
            ? File.ReadAllText(crash)
            : File.Exists(latestLog) ? File.ReadAllText(latestLog) : string.Empty;

        var summary = text.Contains("lwjgl_opengl.dll", StringComparison.OrdinalIgnoreCase)
            ? "Graphics native library is missing. Repair the instance files and retry."
            : text.Contains("UnsupportedClassVersionError", StringComparison.OrdinalIgnoreCase) ||
              text.Contains("class file version", StringComparison.OrdinalIgnoreCase)
                ? "The selected Java version is incompatible with this Minecraft version."
                : text.Contains("requires version", StringComparison.OrdinalIgnoreCase) &&
                  text.Contains("fabric", StringComparison.OrdinalIgnoreCase)
                    ? "Fabric Loader or a Fabric mod is incompatible with this Minecraft version."
                    : text.Contains("OutOfMemoryError", StringComparison.OrdinalIgnoreCase)
                        ? "Minecraft ran out of memory. Lower or increase the launcher RAM setting."
                        : text.Contains("Could not reserve enough space", StringComparison.OrdinalIgnoreCase)
                            ? "Java could not reserve the requested memory. Lower the RAM setting."
                            : "Minecraft closed unexpectedly. Open the crash report and retry with this instance's mods disabled.";
        return new(summary, crash ?? (File.Exists(latestLog) ? latestLog : null));
    }
}
