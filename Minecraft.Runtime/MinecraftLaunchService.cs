using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;

namespace Minecraft.Runtime;

public sealed class MinecraftLaunchService
{
    private readonly MinecraftLauncher launcher;

    public MinecraftLaunchService()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UBLauncher", "minecraft");
        launcher = new MinecraftLauncher(new MinecraftPath(root));
    }

    public async Task LaunchOfflineAsync(string username, string version, int maximumRamMb, IProgress<string>? progress = null)
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
    }
}
