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
    private readonly List<PerformanceCounter> gpuCounters = new();
    private long lastGpuSampleTimestamp;
    private double cachedGpuUsage;

    public static WindowsTelemetryReader Shared { get; } = new();

    public WindowsTelemetryReader()
    {
        try
        {
            cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
            _ = cpuCounter.NextValue();
        }
        catch
        {
            cpuCounter = null;
        }

        try
        {
            var gpuCategory = new PerformanceCounterCategory("GPU Engine");
            foreach (var instanceName in gpuCategory.GetInstanceNames())
            {
                if (!instanceName.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instanceName, true);
                    _ = counter.NextValue();
                    gpuCounters.Add(counter);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    public WindowsTelemetrySnapshot Read()
    {
        lock (readLock)
        {
            var cpuUsage = ReadCounter(cpuCounter);
            var now = Stopwatch.GetTimestamp();
            if (lastGpuSampleTimestamp == 0 ||
                Stopwatch.GetElapsedTime(lastGpuSampleTimestamp, now) >= GpuSampleInterval)
            {
                var gpuUsage = 0d;
                foreach (var counter in gpuCounters)
                {
                    gpuUsage += ReadCounter(counter);
                }

                cachedGpuUsage = Math.Clamp(gpuUsage, 0, 100);
                lastGpuSampleTimestamp = now;
            }

            var memory = ReadMemory();
            return new WindowsTelemetrySnapshot(cpuUsage, cachedGpuUsage, memory.UsedGb, memory.TotalGb);
        }
    }

    public void Dispose()
    {
        cpuCounter?.Dispose();
        foreach (var counter in gpuCounters)
        {
            counter.Dispose();
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
