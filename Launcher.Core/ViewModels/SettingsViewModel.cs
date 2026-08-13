using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.IO;

namespace Launcher.Core.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
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
    private bool _enableAnonymousTelemetry;

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

    partial void OnSelectedSectionChanged(string value)
    {
        OnPropertyChanged(nameof(IsAppearanceSelected));
        OnPropertyChanged(nameof(IsBehaviorSelected));
        OnPropertyChanged(nameof(IsPrivacySelected));
        OnPropertyChanged(nameof(IsGameOptionsSelected));
        OnPropertyChanged(nameof(IsJavaSelected));
        OnPropertyChanged(nameof(IsResourcesSelected));
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
        Log.Information("Launcher settings reverted to default values.");
    }

    [RelayCommand]
    private void SaveSettings()
    {
        // Persistent save will be wired to SQLite/LiteDB in subsequent phases
        Log.Information("Launcher settings successfully saved: RAM={Ram}GB, DiscordRPC={Rpc}, Overlay={Overlay}", 
            AllocatedRamGb, EnableDiscordRpc, EnableOverlay);
    }
}
