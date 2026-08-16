using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.IO;
using Launcher.Core.Services;
using Launcher.Services;

namespace Launcher.Core.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private bool isApplyingGameSettings;

    [ObservableProperty]
    private string _selectedSection = "Appearance";

    [ObservableProperty]
    private bool _enableAdvancedRendering = true;

    [ObservableProperty]
    private bool _useSystemWindowFrame;

    [ObservableProperty]
    private bool _keepLauncherOpen = true;

    [ObservableProperty]
    private bool _showNotifications = true;

    [ObservableProperty]
    private bool _enableAnonymousTelemetry = LauncherAnalyticsConsentStore.IsEnabled;

    [ObservableProperty]
    private bool _shareCrashReports = true;

    [ObservableProperty]
    private bool _forceFullscreen;

    [ObservableProperty]
    private string _gameWindowWidth = "854";

    [ObservableProperty]
    private string _gameWindowHeight = "480";

    [ObservableProperty]
    private double _maximumMemoryMb = 4096;

    [ObservableProperty]
    private double _maxMemoryLimitMb = 32768;

    [ObservableProperty]
    private int _allocatedRamGb = 4; // Default to 4GB RAM

    [ObservableProperty]
    private int _minRamGb = 2;

    [ObservableProperty]
    private int _maxRamGb = 16;

    [ObservableProperty]
    private string _customJvmArguments = "-XX:+UseG1GC -XX:+ParallelRefProcEnabled -XX:MaxGCPauseMillis=200 -XX:+UnlockExperimentalVMOptions -XX:+AlwaysPreTouch";

    [ObservableProperty]
    private string _environmentVariables = string.Empty;

    [ObservableProperty]
    private string _preLaunchHook = string.Empty;

    [ObservableProperty]
    private string _wrapperHook = string.Empty;

    [ObservableProperty]
    private string _postExitHook = string.Empty;

    [ObservableProperty]
    private string _java25Path = string.Empty;

    [ObservableProperty]
    private string _java21Path = string.Empty;

    [ObservableProperty]
    private string _java17Path = string.Empty;

    [ObservableProperty]
    private string _java8Path = string.Empty;

    [ObservableProperty]
    private string _appDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UBLauncher");

    [ObservableProperty]
    private bool _alwaysShowCopyDetails;

    [ObservableProperty]
    private double _maximumConcurrentDownloads = 5;

    [ObservableProperty]
    private double _maximumConcurrentWrites = 10;

    [ObservableProperty]
    private bool _isCachePurgeConfirmationVisible;

    [ObservableProperty]
    private string _resourceStatusText = string.Empty;

    [ObservableProperty]
    private bool _enableDiscordRpc = true;

    [ObservableProperty]
    private bool _enableOverlay = true;

    public bool IsAppearanceSelected => SelectedSection == "Appearance";
    public bool IsBehaviorSelected => SelectedSection == "Behavior";
    public bool IsPrivacySelected => SelectedSection == "Privacy";
    public bool IsGameOptionsSelected => SelectedSection == "GameOptions";
    public bool IsJavaSelected => SelectedSection == "Java";
    public bool IsResourcesSelected => SelectedSection == "Resources";

    public SettingsViewModel()
    {
        var settings = LauncherGameSettingsStore.Current;
        isApplyingGameSettings = true;
        _maximumMemoryMb = settings.MaximumMemoryMb;
        _allocatedRamGb = Math.Max(1, (int)Math.Round(settings.MaximumMemoryMb / 1024d));
        _forceFullscreen = settings.FullScreen;
        _gameWindowWidth = settings.ScreenWidth.ToString();
        _gameWindowHeight = settings.ScreenHeight.ToString();
        _java25Path = settings.Java25Path;
        _java21Path = settings.Java21Path;
        _java17Path = settings.Java17Path;
        _java8Path = settings.Java8Path;
        _enableDiscordRpc = settings.EnableDiscordRpc;
        isApplyingGameSettings = false;
    }

    partial void OnSelectedSectionChanged(string value)
    {
        OnPropertyChanged(nameof(IsAppearanceSelected));
        OnPropertyChanged(nameof(IsBehaviorSelected));
        OnPropertyChanged(nameof(IsPrivacySelected));
        OnPropertyChanged(nameof(IsGameOptionsSelected));
        OnPropertyChanged(nameof(IsJavaSelected));
        OnPropertyChanged(nameof(IsResourcesSelected));
    }

    partial void OnEnableAnonymousTelemetryChanged(bool value)
    {
        LauncherAnalyticsConsentStore.SetEnabled(value);
        if (value)
        {
            _ = LauncherAnalyticsService.SendLauncherOpenAsync();
        }
    }

    partial void OnMaximumMemoryMbChanged(double value)
    {
        AllocatedRamGb = Math.Max(1, (int)Math.Round(value / 1024d));
        PublishGameSettings();
    }

    partial void OnForceFullscreenChanged(bool value) => PublishGameSettings();
    partial void OnGameWindowWidthChanged(string value) => PublishGameSettings();
    partial void OnGameWindowHeightChanged(string value) => PublishGameSettings();
    partial void OnJava25PathChanged(string value) => PublishGameSettings();
    partial void OnJava21PathChanged(string value) => PublishGameSettings();
    partial void OnJava17PathChanged(string value) => PublishGameSettings();
    partial void OnJava8PathChanged(string value) => PublishGameSettings();
    partial void OnEnableDiscordRpcChanged(bool value)
    {
        DiscordPresenceService.SetEnabled(value);
        PublishGameSettings();
    }

    [RelayCommand]
    private void SelectSection(string? section)
    {
        if (!string.IsNullOrWhiteSpace(section))
        {
            SelectedSection = section;
        }
    }

    [RelayCommand]
    private void RequestCachePurge()
    {
        ResourceStatusText = string.Empty;
        IsCachePurgeConfirmationVisible = true;
    }

    [RelayCommand]
    private void CancelCachePurge() => IsCachePurgeConfirmationVisible = false;

    [RelayCommand]
    private void ConfirmCachePurge()
    {
        try
        {
            var cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "UBLauncher",
                "cache");

            if (Directory.Exists(cacheDirectory))
            {
                Directory.Delete(cacheDirectory, true);
            }

            Directory.CreateDirectory(cacheDirectory);
            ResourceStatusText = "App cache purged successfully.";
        }
        catch (Exception exception)
        {
            ResourceStatusText = $"Could not purge cache: {exception.Message}";
        }
        finally
        {
            IsCachePurgeConfirmationVisible = false;
        }
    }

    [RelayCommand]
    private void ResetToDefault()
    {
        isApplyingGameSettings = true;
        AllocatedRamGb = 4;
        ForceFullscreen = false;
        GameWindowWidth = "854";
        GameWindowHeight = "480";
        MaximumMemoryMb = 4096;
        CustomJvmArguments = "-XX:+UseG1GC -XX:+ParallelRefProcEnabled -XX:MaxGCPauseMillis=200 -XX:+UnlockExperimentalVMOptions -XX:+AlwaysPreTouch";
        EnvironmentVariables = string.Empty;
        PreLaunchHook = string.Empty;
        WrapperHook = string.Empty;
        PostExitHook = string.Empty;
        Java25Path = string.Empty;
        Java21Path = string.Empty;
        Java17Path = string.Empty;
        Java8Path = string.Empty;
        AppDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UBLauncher");
        AlwaysShowCopyDetails = false;
        MaximumConcurrentDownloads = 5;
        MaximumConcurrentWrites = 10;
        IsCachePurgeConfirmationVisible = false;
        ResourceStatusText = string.Empty;
        EnableDiscordRpc = true;
        EnableOverlay = true;
        EnableAdvancedRendering = true;
        UseSystemWindowFrame = false;
        KeepLauncherOpen = true;
        ShowNotifications = true;
        EnableAnonymousTelemetry = false;
        ShareCrashReports = true;
        isApplyingGameSettings = false;
        PublishGameSettings();
        Log.Information("Launcher settings reverted to default values.");
    }

    [RelayCommand]
    private void SaveSettings()
    {
        PublishGameSettings();
        Log.Information("Launcher settings successfully saved: RAM={Ram}GB, DiscordRPC={Rpc}, Overlay={Overlay}", 
            AllocatedRamGb, EnableDiscordRpc, EnableOverlay);
    }

    private void PublishGameSettings()
    {
        if (isApplyingGameSettings)
        {
            return;
        }

        var current = LauncherGameSettingsStore.Current;
        var width = int.TryParse(GameWindowWidth, out var parsedWidth) ? parsedWidth : current.ScreenWidth;
        var height = int.TryParse(GameWindowHeight, out var parsedHeight) ? parsedHeight : current.ScreenHeight;

        LauncherGameSettingsStore.Update(new LauncherGameSettings
        {
            MaximumMemoryMb = Math.Max(512, (int)Math.Round(MaximumMemoryMb)),
            FullScreen = ForceFullscreen,
            ScreenWidth = width,
            ScreenHeight = height,
            Java25Path = Java25Path,
            Java21Path = Java21Path,
            Java17Path = Java17Path,
            Java8Path = Java8Path,
            EnableDiscordRpc = EnableDiscordRpc
        });
    }
}
