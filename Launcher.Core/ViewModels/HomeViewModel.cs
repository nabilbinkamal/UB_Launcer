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
using Launcher.Services;
using Serilog;

namespace Launcher.Core.ViewModels;

public sealed class PartnerServerItem
{
    public required string Name { get; init; }
    public required string Address { get; init; }
    public required string Description { get; init; }
    public required string Version { get; init; }
    public required string Badge { get; init; }
    public string IconUrl { get; init; } = string.Empty;
    public string WebsiteUrl { get; init; } = string.Empty;
    public bool HasIcon => !string.IsNullOrWhiteSpace(IconUrl);
}

public partial class HomeViewModel : ViewModelBase
{
    private GameProfile? observedProfile;
    private readonly HashSet<LocalServerItem> observedServers = [];
    private int javaRuntimeRefreshVersion;
    private string resolvedJavaMinecraftVersion = string.Empty;
    private int resolvedJavaMajorVersion;
    private int displayWidth;
    private int displayHeight;
    private int? activeGameProcessId;

    [ObservableProperty]
    private string _statusText = "Ready to Play";

    [ObservableProperty]
    private bool _isLaunching;

    [ObservableProperty]
    private bool _isGameRunning;

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
    private string _allocatedMemoryText = "4 GB RAM";

    [ObservableProperty]
    private string _javaRuntimeText = "Java 21";

    [ObservableProperty]
    private string _screenResolutionText = "854x480";

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

    [ObservableProperty]
    private string _remoteDiscordBannerImageUrl = string.Empty;

    public bool HasRemoteBannerImage => !string.IsNullOrWhiteSpace(RemoteBannerImageUrl);
    public bool HasRemoteDiscordBannerImage => !string.IsNullOrWhiteSpace(RemoteDiscordBannerImageUrl);

    [ObservableProperty]
    private string _username = string.IsNullOrWhiteSpace(OfflineAccountStore.Username) ? "Player" : OfflineAccountStore.Username;

    [ObservableProperty]
    private GameProfile? _selectedInstance;

    public ObservableCollection<string> MinecraftVersions { get; }
    public ObservableCollection<GameProfile> Instances => InstanceStore.Profiles;
    public ObservableCollection<GameProfile> RecentInstances { get; } = [];
    public ObservableCollection<LocalServerItem> RecentServers { get; } = [];
    public ObservableCollection<PartnerServerItem> PartnerServers { get; } = [];
    public bool CanLaunchGame => !IsLaunching && !IsGameRunning;
    public bool IsFabricSelected => SelectedInstance?.LoaderType.Equals("Fabric", StringComparison.OrdinalIgnoreCase) == true;
    public bool HasRecentInstances => RecentInstances.Count > 0;
    public bool HasRecentServers => RecentServers.Count > 0;
    public bool HasPartnerServers => PartnerServers.Count > 0;

    public HomeViewModel()
    {
        OfflineAccountStore.AccountChanged += RefreshUsername;
        LauncherGameSettingsStore.Changed += RefreshLaunchConfiguration;
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
        RefreshLaunchConfiguration();
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

    public void UpdateDisplayResolution(int width, int height)
    {
        if (width <= 0 || height <= 0 || (displayWidth == width && displayHeight == height))
        {
            return;
        }

        displayWidth = width;
        displayHeight = height;
        ScreenResolutionText = $"{displayWidth}x{displayHeight}";
    }

    partial void OnIsLaunchingChanged(bool value) => OnPropertyChanged(nameof(CanLaunchGame));

    partial void OnIsGameRunningChanged(bool value) => OnPropertyChanged(nameof(CanLaunchGame));

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
        RemoteDiscordBannerImageUrl = LauncherRemoteConfiguration.Current.GetAsset("discordBanner", string.Empty);
        PartnerServers.Clear();
        foreach (var server in LauncherRemoteConfiguration.Current.PartnerServers.Where(server => server.Enabled))
        {
            PartnerServers.Add(new PartnerServerItem
            {
                Name = server.Name,
                Address = server.Address,
                Description = server.Description,
                Version = server.Version,
                Badge = server.Badge,
                IconUrl = server.IconUrl,
                WebsiteUrl = server.WebsiteUrl
            });
        }
        OnPropertyChanged(nameof(HasPartnerServers));
    }

    partial void OnRemoteBannerImageUrlChanged(string value) =>
        OnPropertyChanged(nameof(HasRemoteBannerImage));

    partial void OnRemoteDiscordBannerImageUrlChanged(string value) =>
        OnPropertyChanged(nameof(HasRemoteDiscordBannerImage));

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
        RefreshLaunchConfiguration();
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
            RefreshLaunchConfiguration();
        }
    }

    [RelayCommand]
    private Task LaunchGameAsync() => LaunchSelectedInstanceAsync();

    public Task LaunchPartnerServerAsync(PartnerServerItem server) =>
        LaunchSelectedInstanceAsync(server);

    private async Task LaunchSelectedInstanceAsync(PartnerServerItem? partnerServer = null)
    {
        if (IsLaunching)
        {
            ActionStatusText = "Minecraft is already being prepared.";
            return;
        }

        if (HasActiveGameProcess())
        {
            StatusText = "Minecraft is already running";
            ActionStatusText = "Close the running game before starting another one.";
            return;
        }

        var selectedInstance = InstanceStore.SelectedProfile;
        if (partnerServer is not null && selectedInstance is null)
        {
            StatusText = "Select an instance first";
            ActionStatusText = $"Select or create an instance before joining {partnerServer.Name}.";
            return;
        }

        IsLaunching = true;
        var launchStopwatch = Stopwatch.StartNew();
        StatusText = partnerServer is null
            ? "Checking file integrity..."
            : $"Preparing to join {partnerServer.Name}...";
        ActionStatusText = partnerServer is null
            ? "Preparing the selected instance."
            : $"Launching {selectedInstance!.Name} for {partnerServer.Name}.";
        LaunchProgress = 10;
        Log.Information(
            "Starting launch process for version {Version}{PartnerServer}",
            SelectedVersion,
            partnerServer is null ? string.Empty : $" and partner server {partnerServer.Address}");

        try
        {
            var launchService = new MinecraftLaunchService(selectedInstance?.InstancePath);
            var versionMatch = Regex.Match(SelectedVersion, @"\d+\.\d+(?:\.\d+)?");
            var version = selectedInstance?.Version ??
                          (versionMatch.Success ? versionMatch.Value : SelectedVersion.Trim());
            var gameProcess = await launchService.LaunchOfflineAsync(
                Username,
                version,
                LauncherGameSettingsStore.CreateLaunchConfiguration(),
                new Progress<string>(message => StatusText = message),
                partnerServer?.Address);
            activeGameProcessId = gameProcess.Id;
            IsGameRunning = true;
            _ = MonitorActiveGameProcessAsync(gameProcess.Id);
            PlaytimeStore.TrackSession(Username, gameProcess);
            DiscordPresenceService.TrackGame(
                gameProcess,
                selectedInstance?.Name ?? "Minecraft",
                version,
                selectedInstance?.LoaderType ?? "Minecraft",
                Username);
            if (selectedInstance is not null)
            {
                selectedInstance.LastPlayed = "Today";
                selectedInstance.LastPlayedAtUtc = DateTimeOffset.UtcNow;
            }
            RefreshTotalPlaytime();
            LaunchProgress = 35;
            StatusText = "Minecraft is starting...";
            LaunchProgress = 65;

            var windowTimeout = TimeSpan.FromSeconds(15);
            var windowStart = DateTime.UtcNow;
            while (!gameProcess.HasExited && (DateTime.UtcNow - windowStart) < windowTimeout)
            {
                try
                {
                    gameProcess.Refresh();
                    if (gameProcess.MainWindowHandle != IntPtr.Zero)
                    {
                        break;
                    }
                }
                catch
                {
                    break;
                }

                await Task.Delay(200);
            }

            if (!HasActiveGameProcess())
            {
                throw new InvalidOperationException(
                    "Minecraft closed before startup finished. Check the selected instance and Java runtime.");
            }

            launchStopwatch.Stop();
            LaunchTimeText = $"{launchStopwatch.Elapsed.TotalSeconds:0.00}s";
            LaunchHistory = AppendSample(LaunchHistory, Math.Clamp(launchStopwatch.Elapsed.TotalSeconds / 30, 0, 1));

            LaunchProgress = 90;
            LaunchProgress = 100;
            StatusText = partnerServer is null
                ? "Game launched! Enjoy UB Launcher."
                : $"Minecraft is joining {partnerServer.Name}...";
            ActionStatusText = partnerServer is null
                ? $"{selectedInstance?.Name ?? "Minecraft"} is running."
                : $"Joining {partnerServer.Name} at {partnerServer.Address}.";
            Log.Information(
                "Minecraft successfully initialized{PartnerServer}.",
                partnerServer is null ? string.Empty : $" for {partnerServer.Name}");
            _ = LauncherAnalyticsService.SendGameLaunchAsync();

            await Task.Delay(2000);
            if (IsGameRunning)
            {
                StatusText = partnerServer is null
                    ? "Minecraft is running"
                    : $"Playing on {partnerServer.Name}";
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to launch game.");
            StatusText = $"Launch failed: {ex.Message}";
            ActionStatusText = partnerServer is null
                ? "Minecraft could not be launched."
                : $"Could not join {partnerServer.Name}.";
        }
        finally
        {
            IsLaunching = false;
            LaunchProgress = 0;
        }
    }

    private bool HasActiveGameProcess()
    {
        if (!IsGameRunning || activeGameProcessId is not { } processId)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.HasExited)
            {
                return true;
            }
        }
        catch
        {
        }

        activeGameProcessId = null;
        IsGameRunning = false;
        return false;
    }

    private async Task MonitorActiveGameProcessAsync(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            await process.WaitForExitAsync();
        }
        catch
        {
        }
        finally
        {
            if (activeGameProcessId == processId)
            {
                activeGameProcessId = null;
                IsGameRunning = false;
                StatusText = "Ready to Play";
                ActionStatusText = "Minecraft closed. Ready for another launch.";
                RefreshTotalPlaytime();
            }
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

    private void RefreshLaunchConfiguration()
    {
        var settings = LauncherGameSettingsStore.Current;
        var memoryGb = settings.MaximumMemoryMb / 1024d;
        AllocatedMemoryText = Math.Abs(memoryGb - Math.Round(memoryGb)) < 0.01
            ? $"{Math.Round(memoryGb):0} GB RAM"
            : $"{memoryGb:0.#} GB RAM";
        ScreenResolutionText = displayWidth > 0 && displayHeight > 0
            ? $"{displayWidth}x{displayHeight}"
            : settings.FullScreen
                ? "Fullscreen"
                : $"{settings.ScreenWidth}x{settings.ScreenHeight}";

        var version = SelectedInstance?.Version;
        var estimatedMajor = MinecraftLaunchService.EstimateJavaMajorVersion(version ?? string.Empty);
        var hasResolvedVersion = string.Equals(resolvedJavaMinecraftVersion, version, StringComparison.OrdinalIgnoreCase) &&
                                 resolvedJavaMajorVersion > 0;
        JavaRuntimeText = $"Java {(hasResolvedVersion ? resolvedJavaMajorVersion : estimatedMajor)}";

        if (!string.IsNullOrWhiteSpace(version) && !hasResolvedVersion)
        {
            var refreshVersion = ++javaRuntimeRefreshVersion;
            _ = RefreshJavaRuntimeAsync(version, SelectedInstance?.InstancePath, refreshVersion);
        }
    }

    private async Task RefreshJavaRuntimeAsync(string version, string? instancePath, int refreshVersion)
    {
        var launchService = new MinecraftLaunchService(instancePath);
        var majorVersion = await launchService.GetRequiredJavaMajorVersionAsync(version);
        if (refreshVersion == javaRuntimeRefreshVersion &&
            string.Equals(SelectedInstance?.Version, version, StringComparison.OrdinalIgnoreCase))
        {
            resolvedJavaMinecraftVersion = version;
            resolvedJavaMajorVersion = majorVersion;
            JavaRuntimeText = $"Java {majorVersion}";
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
