using System;
using System.Collections.ObjectModel;
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

    public string BannerPlaceholderPath { get; } = @"F:\UBLauncher\UB_Launcher\Launcher.UI\Assets\hero-banner.png";

    [ObservableProperty]
    private string _username = string.IsNullOrWhiteSpace(OfflineAccountStore.Username) ? "Player" : OfflineAccountStore.Username;

    public ObservableCollection<string> MinecraftVersions { get; }

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
    }

    private void RefreshUsername()
    {
        Username = string.IsNullOrWhiteSpace(OfflineAccountStore.Username) ? "Player" : OfflineAccountStore.Username;
    }

    [RelayCommand]
    private async Task LaunchGameAsync()
    {
        if (IsLaunching) return;

        IsLaunching = true;
        StatusText = "Checking file integrity...";
        LaunchProgress = 10;
        Log.Information("Starting launch process for version: {Version}", SelectedVersion);

        try
        {
            var launchService = new MinecraftLaunchService();
            var versionMatch = Regex.Match(SelectedVersion, @"\d+\.\d+(?:\.\d+)?");
            var version = versionMatch.Success ? versionMatch.Value : SelectedVersion.Trim();
            await launchService.LaunchOfflineAsync(Username, version, 8192, new Progress<string>(message => StatusText = message));
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
}
