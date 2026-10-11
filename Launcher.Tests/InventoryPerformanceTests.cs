using System.Diagnostics;
using Launcher.Core.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace Launcher.Tests;

public sealed class InventoryPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public void InventoryIncludesNestedAndHiddenFiles()
    {
        var root = CreateRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "mods"));
            Directory.CreateDirectory(Path.Combine(root, ".minecraft", "saves", "world"));
            File.WriteAllBytes(Path.Combine(root, "mods", "example.jar"), new byte[64]);
            File.WriteAllBytes(Path.Combine(root, "mods", "example.jar.disabled"), new byte[32]);
            File.WriteAllBytes(Path.Combine(root, ".minecraft", "saves", "world", "level.dat"), new byte[128]);
            var hidden = Path.Combine(root, ".hidden");
            File.WriteAllBytes(hidden, new byte[16]);
            File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);

            var profile = new GameProfile { InstancePath = root };
            profile.RefreshContentInventory();

            Assert.Equal(1, profile.ModCount);
            Assert.Equal("240 B", profile.StorageUsed);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void MissingInstanceHasEmptyInventory()
    {
        var profile = new GameProfile { InstancePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")) };
        profile.RefreshContentInventory();
        Assert.Equal("Empty", profile.StorageUsed);
        Assert.Equal(0, profile.TotalInstalledContent);
    }

    [Fact]
    public void LargeInventoryReportsMatchingSizeAndScanMeasurements()
    {
        var root = CreateRoot();
        try
        {
            const int fileCount = 2048;
            for (var folder = 0; folder < 32; folder++)
            {
                var directory = Path.Combine(root, "saves", $"world-{folder}");
                Directory.CreateDirectory(directory);
                for (var file = 0; file < 64; file++)
                    File.WriteAllBytes(Path.Combine(directory, $"region-{file}.dat"), new byte[64]);
            }

            var profile = new GameProfile { InstancePath = root };
            long baselineSize = 0;
            Action baseline = () => baselineSize = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Sum(path => new FileInfo(path).Length);
            Action optimized = profile.RefreshContentInventory;
            baseline();
            optimized();
            var oldScan = Measure(baseline);
            var newScan = Measure(optimized);

            Assert.Equal(fileCount * 64L, baselineSize);
            Assert.Equal("128 KB", profile.StorageUsed);
            output.WriteLine($"Inventory scan, {fileCount} files, average of 5 warm runs:");
            output.WriteLine($"Original: {oldScan.Milliseconds:0.00} ms, {oldScan.Bytes:N0} bytes allocated/run");
            output.WriteLine($"Optimized: {newScan.Milliseconds:0.00} ms, {newScan.Bytes:N0} bytes allocated/run");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static (double Milliseconds, long Bytes) Measure(Action action)
    {
        const int repetitions = 5;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var run = 0; run < repetitions; run++)
            action();
        return (Stopwatch.GetElapsedTime(started).TotalMilliseconds / repetitions,
            (GC.GetAllocatedBytesForCurrentThread() - allocatedBefore) / repetitions);
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ub-inventory-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
