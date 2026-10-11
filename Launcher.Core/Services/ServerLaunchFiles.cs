namespace Launcher.Core.Services;

public static class ServerLaunchFiles
{
    public static string? FindArgumentFile(string folder, string serverType)
    {
        var vendor = serverType.ToLowerInvariant() switch
        {
            "forge" => Path.Combine("net", "minecraftforge", "forge"),
            "neoforge" => Path.Combine("net", "neoforged", "neoforge"),
            _ => null
        };
        if (vendor is null) return null;
        var root = Path.Combine(folder, "libraries", vendor);
        if (!Directory.Exists(root)) return null;
        var name = OperatingSystem.IsWindows() ? "win_args.txt" : "unix_args.txt";
        return Directory.EnumerateFiles(root, name, SearchOption.AllDirectories)
            .Where(path => new FileInfo(path).Length > 0)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    public static bool IsReady(string folder, string jarName, string type)
    {
        try
        {
            if (FindArgumentFile(folder, type) is not null) return true;
            using var file = File.OpenRead(Path.Combine(folder, jarName));
            return file.Length > 4 && file.ReadByte() == 'P' && file.ReadByte() == 'K';
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public static string ResolveJava(string minecraftVersion)
    {
        var configured = LauncherGameSettingsStore.GetJavaPath(Minecraft.Runtime.MinecraftLaunchService.EstimateJavaMajorVersion(minecraftVersion));
        return !string.IsNullOrWhiteSpace(configured) && File.Exists(configured) ? configured : "java";
    }
}
