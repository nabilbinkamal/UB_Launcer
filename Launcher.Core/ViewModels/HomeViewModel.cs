using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Services;
using Minecraft.Runtime;
using Serilog;

namespace Launcher.Core.ViewModels;

public partial class HomeViewModel : ViewModelBase
{
    private GameProfile? observedProfile;
    private readonly HashSet<LocalServerItem> observedServers = [];

    [ObservableProperty]
    private string _statusText = "Ready to Play";

    [ObservableProperty]
    private bool _isLaunching;

    [ObservableProperty]
    private double _launchProgress;

    [ObservableProperty]
    private string _selectedVersion;

    [ObservableProperty]
    private string _actionStatusText = "Cloud services ready";

    [ObservableProperty]
    private string _launchTimeText = "--";

    [ObservableProperty]
    private string _cpuUsageText = "000%";

    [ObservableProperty]
    private string _gpuUsageText = "000%";

    [ObservableProperty]
    private string _memoryUsedText = "0";

    [ObservableProperty]
    private string _memoryCapacityText = " GB/0 GB";

    [ObservableProperty]
    private string _memoryUsagePercentText = "0%";

    [ObservableProperty]
    private string _totalPlaytimeText = "0m";

    [ObservableProperty]
    private string _totalModsText = "0";

    [ObservableProperty]
    private double _memoryUsagePercent;

    [ObservableProperty]
    private IReadOnlyList<double> _launchHistory = new double[] { 0.18, 0.2, 0.16, 0.21, 0.19, 0.24, 0.2, 0.23, 0.22, 0.24, 0.25, 0.24 };

    [ObservableProperty]
    private IReadOnlyList<double> _cpuHistory = new double[] { 0.18, 0.3, 0.2, 0.42, 0.24, 0.34, 0.22, 0.38, 0.26, 0.45, 0.31, 0.36 };

    [ObservableProperty]
    private IReadOnlyList<double> _gpuHistory = new double[] { 0.14, 0.22, 0.18, 0.32, 0.2, 0.3, 0.24, 0.35, 0.27, 0.4, 0.31, 0.34 };

    [ObservableProperty]
    private string _remoteBannerImageUrl = string.Empty;

    public bool HasRemoteBannerImage => !string.IsNullOrWhiteSpace(RemoteBannerImageUrl);

    [ObservableProperty]
    private string _username = string.IsNullOrWhiteSpace(OfflineAccountStore.Username) ? "Player" : OfflineAccountStore.Username;

    [ObservableProperty]
    private GameProfile? _selectedInstance;

    public ObservableCollection<string> MinecraftVersions { get; }
    public ObservableCollection<GameProfile> Instances => InstanceStore.Profiles;
    public ObservableCollection<GameProfile> RecentInstances { get; } = [];
    public ObservableCollection<LocalServerItem> RecentServers { get; } = [];
    public bool CanLaunchGame => !IsLaunching;
    public bool IsFabricSelected => SelectedInstance?.LoaderType.Equals("Fabric", StringComparison.OrdinalIgnoreCase) == true;
    public bool HasRecentInstances => RecentInstances.Count > 0;
    public bool HasRecentServers => RecentServers.Count > 0;

    public HomeViewModel()
    {
        OfflineAccountStore.AccountChanged += RefreshUsername;
        MinecraftVersions = new ObservableCollection<string>
        {
            "Fabric 1.20.4 (UB Performance Pack)",
            "NeoForge 1.20.4 (Latest)",
            "Vanilla 1.20.4 (Stable)",
            "OptiFine 1.8.9 (Legacy PvP)"
        };
        _selectedVersion = MinecraftVersions[0];
        _selectedInstance = InstanceStore.SelectedProfile;
        InstanceStore.SelectionChanged += RefreshSelectedInstanceStats;
        InstanceStore.ProfilesChanged += RefreshRecentInstances;
        ServerStore.Servers.CollectionChanged += OnServersCollectionChanged;
        _ = InstanceStore.EnsureMissingIconsAsync();
        RefreshSelectedInstanceStats();
        RefreshRecentInstances();
        RefreshRecentServers();
        RefreshTotalPlaytime();
        RefreshRemoteBranding();
    }

    public void UpdateTelemetry(double cpuUsage, double gpuUsage, double usedMemoryGb, double totalMemoryGb)
    {
        var clampedCpu = Math.Clamp(cpuUsage, 0, 100);
        var clampedGpu = Math.Clamp(gpuUsage, 0, 100);
        var safeTotalMemory = Math.Max(0, totalMemoryGb);
        var safeUsedMemory = Math.Clamp(usedMemoryGb, 0, safeTotalMemory);
        var memoryPercent = safeTotalMemory <= 0 ? 0 : safeUsedMemory / safeTotalMemory * 100;

        CpuUsageText = $"{clampedCpu:000}%";
        GpuUsageText = $"{clampedGpu:000}%";
        MemoryUsedText = $"{safeUsedMemory:0}";
        MemoryCapacityText = $" GB/{safeTotalMemory:0} GB";
        MemoryUsagePercent = memoryPercent;
        MemoryUsagePercentText = $"{memoryPercent:0}%";
        CpuHistory = AppendSample(CpuHistory, clampedCpu / 100);
        GpuHistory = AppendSample(GpuHistory, clampedGpu / 100);
    }

    partial void OnIsLaunchingChanged(bool value) => OnPropertyChanged(nameof(CanLaunchGame));

    partial void OnSelectedInstanceChanged(GameProfile? value)
    {
        if (value is not null && !ReferenceEquals(value, InstanceStore.SelectedProfile))
        {
            InstanceStore.SelectedProfile = value;
            return;
        }

        RefreshSelectedInstanceStats();
    }

    public void RefreshTotalPlaytime()
    {
        var totalSeconds = PlaytimeStore.GetTotalSeconds(Username);
        var total = TimeSpan.FromSeconds(totalSeconds);

        TotalPlaytimeText = total.TotalHours >= 100
            ? $"{Math.Floor(total.TotalHours):N0}h"
            : total.TotalHours >= 1
                ? $"{total.TotalHours:0.0}h"
                : $"{Math.Floor(total.TotalMinutes):0}m";
    }

    public void RefreshRecentData()
    {
        foreach (var profile in RecentInstances)
        {
            profile.RefreshRelativeTime();
        }

        foreach (var server in RecentServers)
        {
            server.RefreshRelativeTime();
        }
    }

    /// <summary>Called after the public admin configuration has been refreshed.</summary>
    public void RefreshRemoteBranding()
    {
        RemoteBannerImageUrl = LauncherRemoteConfiguration.Current.GetAsset("heroBanner", string.Empty);
    }

    partial void OnRemoteBannerImageUrlChanged(string value) =>
        OnPropertyChanged(nameof(HasRemoteBannerImage));

    private void RefreshUsername()
    {
        Username = string.IsNullOrWhiteSpace(OfflineAccountStore.Username) ? "Player" : OfflineAccountStore.Username;
        RefreshTotalPlaytime();
    }

    private void RefreshSelectedInstanceStats()
    {
        var selectedProfile = InstanceStore.SelectedProfile;
        if (!ReferenceEquals(SelectedInstance, selectedProfile))
        {
            SelectedInstance = selectedProfile;
        }

        if (!ReferenceEquals(observedProfile, selectedProfile))
        {
            if (observedProfile is not null)
            {
                observedProfile.PropertyChanged -= OnSelectedProfilePropertyChanged;
            }

            observedProfile = selectedProfile;
            if (observedProfile is not null)
            {
                observedProfile.PropertyChanged += OnSelectedProfilePropertyChanged;
            }
        }

        observedProfile?.RefreshModInventory();
        TotalModsText = observedProfile?.ModCount.ToString("N0") ?? "0";
        if (observedProfile is not null)
        {
            SelectedVersion = $"{observedProfile.LoaderType} {observedProfile.Version}";
        }

        OnPropertyChanged(nameof(IsFabricSelected));
        RefreshRecentInstances();
    }

    private void OnSelectedProfilePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (sender is not GameProfile profile)
        {
            return;
        }

        if (eventArgs.PropertyName == nameof(GameProfile.ModCount))
        {
            TotalModsText = profile.ModCount.ToString("N0");
        }

        if (eventArgs.PropertyName is nameof(GameProfile.Version) or nameof(GameProfile.LoaderType))
        {
            SelectedVersion = $"{profile.LoaderType} {profile.Version}";
            OnPropertyChanged(nameof(IsFabricSelected));
        }
    }

    [RelayCommand]
    private async Task LaunchGameAsync()
    {
        if (IsLaunching) return;

        IsLaunching = true;
        var launchStopwatch = Stopwatch.StartNew();
        StatusText = "Checking file integrity...";
        LaunchProgress = 10;
        Log.Information("Starting launch process for version: {Version}", SelectedVersion);

        try
        {
            var selectedInstance = InstanceStore.SelectedProfile;
            var launchService = new MinecraftLaunchService(selectedInstance?.InstancePath);
            var versionMatch = Regex.Match(SelectedVersion, @"\d+\.\d+(?:\.\d+)?");
            var version = selectedInstance?.Version ??
                          (versionMatch.Success ? versionMatch.Value : SelectedVersion.Trim());
            var gameProcess = await launchService.LaunchOfflineAsync(Username, version, 8192, new Progress<string>(message => StatusText = message));
            PlaytimeStore.TrackSession(Username, gameProcess);
            if (selectedInstance is not null)
            {
                selectedInstance.LastPlayed = "Today";
                selectedInstance.LastPlayedAtUtc = DateTimeOffset.UtcNow;
            }
            RefreshTotalPlaytime();
            launchStopwatch.Stop();
            LaunchTimeText = $"{launchStopwatch.Elapsed.TotalSeconds:0.00}s";
            LaunchHistory = AppendSample(LaunchHistory, Math.Clamp(launchStopwatch.Elapsed.TotalSeconds / 30, 0, 1));
            LaunchProgress = 35;
            StatusText = "Minecraft is starting...";
            LaunchProgress = 65;
            await Task.Delay(500);
            LaunchProgress = 90;
            LaunchProgress = 100;
            StatusText = "Game launched! Enjoy UB Launcher.";
            Log.Information("Minecraft successfully initialized.");

            // Wait a moment and then reset launcher UI state (game runs in background)
            await Task.Delay(2000);
            StatusText = "Ready to Play";
            IsLaunching = false;
            LaunchProgress = 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to launch game.");
            StatusText = $"Launch failed: {ex.Message}";
            IsLaunching = false;
            LaunchProgress = 0;
        }
    }

    [RelayCommand]
    private void AddInstance() => ActionStatusText = "Instance creator is ready for your next profile.";

    [RelayCommand]
    private void InstallMods() => ActionStatusText = "Mod manager opened. Connect Modrinth or CurseForge next.";

    [RelayCommand]
    private void EditProfile() => ActionStatusText = "Profile editor selected.";

    [RelayCommand]
    private void LaunchOption() => ActionStatusText = "Launch options selected.";

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

    private void RefreshRecentInstances()
    {
        var recent = Instances
            .OrderByDescending(profile => profile.LastPlayedAtUtc ?? DateTimeOffset.MinValue)
            .ThenByDescending(profile => ReferenceEquals(profile, SelectedInstance))
            .ThenBy(profile => Instances.IndexOf(profile))
            .Take(3)
            .ToList();

        RecentInstances.Clear();
        foreach (var profile in recent)
        {
            RecentInstances.Add(profile);
        }

        OnPropertyChanged(nameof(HasRecentInstances));
    }

    private void RefreshRecentServers()
    {
        foreach (var server in observedServers.Where(server => !ServerStore.Servers.Contains(server)).ToList())
        {
            server.PropertyChanged -= OnRecentServerPropertyChanged;
            observedServers.Remove(server);
        }

        foreach (var server in ServerStore.Servers.Where(server => observedServers.Add(server)))
        {
            server.PropertyChanged += OnRecentServerPropertyChanged;
        }

        var recent = ServerStore.Servers
            .OrderByDescending(server => server.LastActivityAtUtc ?? DateTimeOffset.MinValue)
            .ThenBy(server => ServerStore.Servers.IndexOf(server))
            .Take(4)
            .ToList();

        RecentServers.Clear();
        foreach (var server in recent)
        {
            RecentServers.Add(server);
        }

        OnPropertyChanged(nameof(HasRecentServers));
    }

    private void OnServersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs) =>
        RefreshRecentServers();

    private void OnRecentServerPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(LocalServerItem.LastActivityAtUtc))
        {
            RefreshRecentServers();
        }
    }
}
