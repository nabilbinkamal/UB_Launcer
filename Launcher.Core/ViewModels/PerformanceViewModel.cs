using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Services;

namespace Launcher.Core.ViewModels;

public partial class PerformanceViewModel : ViewModelBase
{
    public PerformanceViewModel()
    {
        var savedProfile = LauncherGameSettingsStore.Current.PerformanceProfile;
        if (string.IsNullOrWhiteSpace(savedProfile))
        {
            // Auto-detect based on system specs for the very first time
            var ramGb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024.0 / 1024.0 / 1024.0;
            var cores = Environment.ProcessorCount;
            
            if (ramGb >= 14 && cores >= 8)
            {
                savedProfile = "High";
            }
            else if (ramGb <= 6 || cores <= 4)
            {
                savedProfile = "Efficiency";
            }
            else
            {
                savedProfile = "Balanced";
            }
        }
        SelectProfile(savedProfile);
    }

    [ObservableProperty] private string _selectedProfile = "Balanced";
    [ObservableProperty] private bool _enableOptimizationPack = true;
    [ObservableProperty] private bool _enableFastLaunch = true;
    [ObservableProperty] private bool _enableGameMode = true;
    [ObservableProperty] private bool _enableHardwareAcceleration = true;
    [ObservableProperty] private bool _useHighProcessPriority;
    [ObservableProperty] private bool _forceDedicatedGpu;
    [ObservableProperty] private bool _autoOptimize = true;

    [ObservableProperty] private string _cpuUsageText = "0%";
    [ObservableProperty] private string _gpuUsageText = "0%";
    [ObservableProperty] private double _cpuUsage;
    [ObservableProperty] private double _gpuUsage;
    [ObservableProperty] private string _memoryUsageText = "0 / 0 GB";
    [ObservableProperty] private string _memoryUsagePercentText = "0%";
    [ObservableProperty] private double _memoryUsagePercent;
    [ObservableProperty] private string _healthStatus = "ANALYZING";
    [ObservableProperty] private string _healthAccent = "#87E3F2";
    [ObservableProperty] private string _currentLoadText = "Waiting for live telemetry...";
    [ObservableProperty] private string _recommendationText = "UB Launcher is checking your system load.";
    [ObservableProperty] private string _lastAnalyzedText = "Live monitoring active";

    [ObservableProperty]
    private IReadOnlyList<double> _cpuHistory =
        new double[] { .12, .15, .14, .18, .16, .21, .17, .2, .16, .19, .18, .2, .17, .21, .18, .2 };

    [ObservableProperty]
    private IReadOnlyList<double> _gpuHistory =
        new double[] { .08, .11, .1, .14, .12, .16, .13, .17, .14, .18, .15, .17, .14, .18, .16, .19 };

    [ObservableProperty]
    private IReadOnlyList<double> _memoryHistory =
        new double[] { .38, .39, .39, .4, .4, .41, .41, .42, .42, .42, .43, .43, .43, .44, .44, .44 };

    private double latestCpuUsage;
    private double latestGpuUsage;
    private double latestMemoryPercent;

    public bool IsBalancedProfile => SelectedProfile == "Balanced";
    public bool IsHighProfile => SelectedProfile == "High";
    public bool IsEfficiencyProfile => SelectedProfile == "Efficiency";

    partial void OnSelectedProfileChanged(string value)
    {
        OnPropertyChanged(nameof(IsBalancedProfile));
        OnPropertyChanged(nameof(IsHighProfile));
        OnPropertyChanged(nameof(IsEfficiencyProfile));
    }

    public void UpdateTelemetry(double cpuUsage, double gpuUsage, double usedMemoryGb, double totalMemoryGb)
    {
        latestCpuUsage = Math.Clamp(cpuUsage, 0, 100);
        latestGpuUsage = Math.Clamp(gpuUsage, 0, 100);
        var safeTotalMemory = Math.Max(0, totalMemoryGb);
        var safeUsedMemory = Math.Clamp(usedMemoryGb, 0, safeTotalMemory);
        latestMemoryPercent = safeTotalMemory <= 0 ? 0 : safeUsedMemory / safeTotalMemory * 100;

        CpuUsageText = $"{latestCpuUsage:0}%";
        GpuUsageText = $"{latestGpuUsage:0}%";
        CpuUsage = latestCpuUsage;
        GpuUsage = latestGpuUsage;
        MemoryUsageText = $"{safeUsedMemory:0.0} / {safeTotalMemory:0} GB";
        MemoryUsagePercent = latestMemoryPercent;
        MemoryUsagePercentText = $"{latestMemoryPercent:0}%";

        CpuHistory = AppendSample(CpuHistory, latestCpuUsage / 100);
        GpuHistory = AppendSample(GpuHistory, latestGpuUsage / 100);
        MemoryHistory = AppendSample(MemoryHistory, latestMemoryPercent / 100);
        UpdateHealthSummary();
    }

    [RelayCommand]
    private void SelectProfile(string? profile)
    {
        if (string.IsNullOrWhiteSpace(profile))
        {
            return;
        }

        SelectedProfile = profile;

        var currentSettings = LauncherGameSettingsStore.Current;
        if (currentSettings.PerformanceProfile != profile)
        {
            LauncherGameSettingsStore.Update(currentSettings with { PerformanceProfile = profile });
        }

        switch (profile)
        {
            case "High":
                EnableOptimizationPack = true;
                EnableFastLaunch = true;
                EnableGameMode = true;
                EnableHardwareAcceleration = true;
                UseHighProcessPriority = true;
                ForceDedicatedGpu = true;
                AutoOptimize = true;
                break;
            case "Efficiency":
                EnableOptimizationPack = true;
                EnableFastLaunch = true;
                EnableGameMode = false;
                EnableHardwareAcceleration = true;
                UseHighProcessPriority = false;
                ForceDedicatedGpu = false;
                AutoOptimize = false;
                break;
            default:
                EnableOptimizationPack = true;
                EnableFastLaunch = true;
                EnableGameMode = true;
                EnableHardwareAcceleration = true;
                UseHighProcessPriority = false;
                ForceDedicatedGpu = false;
                AutoOptimize = true;
                break;
        }

    }

    [RelayCommand]
    private void AnalyzeSystem()
    {
        UpdateHealthSummary();
        LastAnalyzedText = $"Analyzed {DateTime.Now:h:mm tt}";
    }

    private void UpdateHealthSummary()
    {
        CurrentLoadText = $"CPU {latestCpuUsage:0}%   ·   GPU {latestGpuUsage:0}%   ·   Memory {latestMemoryPercent:0}%";

        if (latestCpuUsage >= 90 || latestMemoryPercent >= 92)
        {
            HealthStatus = "HIGH LOAD";
            HealthAccent = "#FF6B76";
            RecommendationText = "Close heavy background apps before launching Minecraft.";
        }
        else if (latestCpuUsage >= 72 || latestGpuUsage >= 85 || latestMemoryPercent >= 80)
        {
            HealthStatus = "MODERATE LOAD";
            HealthAccent = "#F3C969";
            RecommendationText = "The system is usable, but the Balanced profile is recommended.";
        }
        else
        {
            HealthStatus = "SYSTEM HEALTHY";
            HealthAccent = "#69E788";
            RecommendationText = "Current system load is suitable for an optimized game launch.";
        }
    }

    private static IReadOnlyList<double> AppendSample(IReadOnlyList<double> history, double sample)
    {
        var updatedHistory = new double[history.Count];
        for (var index = 1; index < history.Count; index++)
        {
            updatedHistory[index - 1] = history[index];
        }

        updatedHistory[^1] = Math.Clamp(sample, 0, 1);
        return updatedHistory;
    }
}
