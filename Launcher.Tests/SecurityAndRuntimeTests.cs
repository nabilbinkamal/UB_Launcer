using System.Text;
using Launcher.Core.Services;
using Launcher.Security;
using Minecraft.Runtime;
using Xunit;

namespace Launcher.Tests;

public sealed class SecurityAndRuntimeTests
{
    [Fact]
    public void FakeServerJarDoesNotCountAsAnInstalledServer()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ub-server-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "server.jar"), "# Forge server placeholder");
            Assert.False(ServerLaunchFiles.IsReady(root, "server.jar", "Forge"));
            var argsRoot = Path.Combine(root, "libraries", "net", "minecraftforge", "forge", "1.20.4-test");
            Directory.CreateDirectory(argsRoot);
            File.WriteAllText(Path.Combine(argsRoot, OperatingSystem.IsWindows() ? "win_args.txt" : "unix_args.txt"), "-cp libraries/server.jar");
            Assert.True(ServerLaunchFiles.IsReady(root, "server.jar", "Forge"));
            Assert.False(ServerLaunchFiles.IsReady(root, "server.jar", "Paper"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(17)]
    [InlineData(21)]
    [InlineData(25)]
    public void DefaultJvmFlagsUseOneSupportedCollector(int java)
    {
        var flags = MinecraftLaunchService.BuildOptimizedJvmArguments(null, java, true);
        Assert.Contains("-XX:+UseG1GC", flags);
        Assert.DoesNotContain("ZGenerational", flags);
        Assert.DoesNotContain("UnlockExperimental", flags);
    }

    [Theory]
    [InlineData("-XX:+UseZGC")]
    [InlineData("-XX:+UseSerialGC")]
    [InlineData("-XX:+UseParallelGC")]
    [InlineData("-XX:+UseG1GC")]
    public void CustomCollectorDoesNotConflictWithDefaults(string collector)
    {
        var flags = MinecraftLaunchService.BuildOptimizedJvmArguments(collector, 21, true);
        Assert.Equal(1, flags.Split(' ').Count(flag => flag.StartsWith("-XX:+Use") && flag.EndsWith("GC")));
        Assert.Contains(collector, flags);
    }

    [Fact]
    public void DisabledOptimizationPreservesOnlyCustomArguments() =>
        Assert.Equal("-Dexample=1", MinecraftLaunchService.BuildOptimizedJvmArguments(" -Dexample=1 ", 21, false));

    [Fact]
    public void UserPauseTargetIsNotDuplicated()
    {
        var flags = MinecraftLaunchService.BuildOptimizedJvmArguments("-XX:MaxGCPauseMillis=150", 17, true);
        Assert.DoesNotContain("MaxGCPauseMillis=100", flags);
    }

    [Theory]
    [InlineData(131072, 4096, 2048)]
    [InlineData(131072, 8192, 6144)]
    [InlineData(256, 8192, 512)]
    [InlineData(4096, 0, 4096)]
    public void MemoryBudgetLeavesRoomForTheOperatingSystem(int requested, long totalMb, int expected) =>
        Assert.Equal(expected, MinecraftLaunchService.ClampMemoryMb(requested, totalMb * 1024 * 1024));

    [Theory]
    [InlineData("../escape.jar")]
    [InlineData("..\\escape.jar")]
    [InlineData("/absolute.jar")]
    [InlineData("C:\\escape.jar")]
    [InlineData("mods/file.jar:payload")]
    [InlineData("mods/CON.jar")]
    [InlineData("mods/LPT1")]
    [InlineData("mods/trailing. ")]
    [InlineData("mods//file.jar")]
    public void ArchivePathsCannotEscapeOrUseWindowsSpecialNames(string path) =>
        Assert.Throws<InvalidDataException>(() => SafeArchivePath.Resolve(Path.GetTempPath(), path));

    [Fact]
    public void ValidArchivePathStaysInsideRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Assert.Equal(Path.Combine(root, "mods", "example.jar"), SafeArchivePath.Resolve(root, "mods/example.jar"));
    }

    [Fact]
    public async Task ChunkedResponseCannotExceedTheLimit()
    {
        using var source = new MemoryStream(new byte[65536]);
        using var destination = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => BoundedStream.CopyAsync(source, destination, 32000));
        Assert.True(destination.Length <= 32000);
    }

    [Fact]
    public async Task ExactLimitResponseIsAccepted()
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes("valid"));
        using var destination = new MemoryStream();
        await BoundedStream.CopyAsync(source, destination, 5);
        Assert.Equal("valid", Encoding.UTF8.GetString(destination.ToArray()));
    }

    [Fact]
    public async Task BodyReadsHonorCancellation()
    {
        using var source = new MemoryStream(new byte[8]);
        using var destination = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BoundedStream.CopyAsync(source, destination, 8, cancellation.Token));
    }

    [Fact]
    public void ConfigHandlesCaseDuplicateAssetsAndNullPartners()
    {
        var configuration = new LauncherPublicConfiguration
        {
            Assets = new() { ["logo"] = "https://example.com/one.png", ["LOGO"] = "https://example.com/two.png" },
            PartnerServers = [null!]
        }.Sanitize();
        Assert.Single(configuration.Assets);
        Assert.Equal("https://example.com/two.png", configuration.GetAsset("logo", "fallback"));
        Assert.Empty(configuration.PartnerServers);
    }

    [Fact]
    public void ConfigRejectsInsecureExecutableDestinations()
    {
        var configuration = new LauncherPublicConfiguration { UpdateUrl = "file:///C:/malware.exe", ForceUpdate = true }.Sanitize();
        Assert.Empty(configuration.UpdateUrl);
        Assert.False(configuration.ForceUpdate);
    }

    [Fact]
    public void PersistentSessionIsEncryptedAndTamperingFails()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(Path.GetTempPath(), $"ub-session-test-{Guid.NewGuid():N}.dat");
        var secret = Encoding.UTF8.GetBytes("test-only-refresh-token");
        try
        {
            ProtectedSessionFile.Write(path, secret);
            Assert.DoesNotContain("test-only-refresh-token", Encoding.UTF8.GetString(File.ReadAllBytes(path)));
            Assert.Equal(secret, ProtectedSessionFile.Read(path));
            File.WriteAllBytes(path, new byte[64]);
            Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => ProtectedSessionFile.Read(path));
        }
        finally { File.Delete(path); }
    }
}
