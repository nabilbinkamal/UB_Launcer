using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using System.Diagnostics;

namespace Minecraft.Runtime;

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

    public async Task<Process> LaunchOfflineAsync(string username, string version, int maximumRamMb, IProgress<string>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("Enter an offline username first.", nameof(username));
        if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("Select a Minecraft version first.", nameof(version));

        progress?.Report($"Installing Minecraft {version}...");
        await launcher.InstallAsync(version);
        progress?.Report("Preparing Minecraft process...");

        var process = await launcher.BuildProcessAsync(version, new MLaunchOption
        {
            Session = MSession.CreateOfflineSession(username),
            MaximumRamMb = maximumRamMb
        });
        process.Start();
        return process;
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
