using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Launcher.UI.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsTelemetryReader : IDisposable
{
    private static readonly TimeSpan GpuSampleInterval = TimeSpan.FromSeconds(2);
    private readonly object readLock = new();
    private readonly PerformanceCounter? cpuCounter;
    private readonly PerformanceCounterCategory gpuCategory = new("GPU Engine");
    private Dictionary<string, CounterSample> previousGpuSamples = new(StringComparer.OrdinalIgnoreCase);
    private long lastGpuSampleTimestamp;
    private long lastCpuSampleTimestamp;
    private double cachedCpuUsage;
    private double cachedGpuUsage;

    public static WindowsTelemetryReader Shared { get; } = new();

    public WindowsTelemetryReader()
    {
        try
        {
            cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
            _ = cpuCounter.NextValue();
            lastCpuSampleTimestamp = Stopwatch.GetTimestamp();
        }
        catch
        {
            cpuCounter = null;
        }

    }

    public WindowsTelemetrySnapshot Read()
    {
        lock (readLock)
        {
            var now = Stopwatch.GetTimestamp();
            if (lastCpuSampleTimestamp == 0 ||
                Stopwatch.GetElapsedTime(lastCpuSampleTimestamp, now) >= TimeSpan.FromMilliseconds(750))
            {
                cachedCpuUsage = ReadCounter(cpuCounter);
                lastCpuSampleTimestamp = now;
            }
            if (lastGpuSampleTimestamp == 0 ||
                Stopwatch.GetElapsedTime(lastGpuSampleTimestamp, now) >= GpuSampleInterval)
            {
                cachedGpuUsage = ReadGpuUsage();
                lastGpuSampleTimestamp = now;
            }

            var memory = ReadMemory();
            return new WindowsTelemetrySnapshot(cachedCpuUsage, cachedGpuUsage, memory.UsedGb, memory.TotalGb);
        }
    }

    public void Dispose()
    {
        lock (readLock)
        {
            cpuCounter?.Dispose();
            previousGpuSamples.Clear();
        }
    }

    private double ReadGpuUsage()
    {
        try
        {
            // Fetch the category once instead of querying Windows for every GPU engine.
            var instances = gpuCategory.ReadCategory()["Utilization Percentage"];
            var samples = new Dictionary<string, CounterSample>(StringComparer.OrdinalIgnoreCase);
            var usage = 0d;
            foreach (InstanceData instance in instances.Values)
            {
                if (!instance.InstanceName.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                    continue;

                var sample = instance.Sample;
                samples[instance.InstanceName] = sample;
                if (!previousGpuSamples.TryGetValue(instance.InstanceName, out var previous))
                    continue;

                try
                {
                    var value = CounterSample.Calculate(previous, sample);
                    if (double.IsFinite(value))
                        usage += Math.Clamp(value, 0, 100);
                }
                catch
                {
                    // Restarted or removed processes can invalidate a single counter sample.
                }
            }
            previousGpuSamples = samples;
            return Math.Clamp(usage, 0, 100);
        }
        catch
        {
            previousGpuSamples.Clear();
            return 0;
        }
    }

    private static double ReadCounter(PerformanceCounter? counter)
    {
        if (counter is null)
        {
            return 0;
        }

        try
        {
            return Math.Clamp(counter.NextValue(), 0, 100);
        }
        catch
        {
            return 0;
        }
    }

    private static (double UsedGb, double TotalGb) ReadMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhys == 0)
        {
            return (0, 0);
        }

        const double bytesPerGb = 1024d * 1024d * 1024d;
        var totalGb = status.TotalPhys / bytesPerGb;
        var usedGb = (status.TotalPhys - status.AvailPhys) / bytesPerGb;
        return (Math.Max(0, usedGb), totalGb);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }
}

public readonly record struct WindowsTelemetrySnapshot(double CpuUsage, double GpuUsage, double UsedMemoryGb, double TotalMemoryGb);
