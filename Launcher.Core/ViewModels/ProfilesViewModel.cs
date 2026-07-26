using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Services;

namespace Launcher.Core.ViewModels;

public class GameProfile
{
    public string Name { get; set; } = null!;
    public string Version { get; set; } = null!;
    public string LoaderType { get; set; } = null!; // Vanilla, Fabric, Forge, NeoForge
    public string LastPlayed { get; set; } = null!;
}

public partial class ProfilesViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _offlineUsername = OfflineAccountStore.Username;

    [ObservableProperty]
    private string _accountStatus = string.IsNullOrWhiteSpace(OfflineAccountStore.Username) ? "No offline account connected" : "Offline account connected";

    [ObservableProperty]
    private GameProfile? _selectedProfile;

    [ObservableProperty]
    private string _newProfileName = string.Empty;

    [ObservableProperty]
    private string _newProfileVersion = "1.20.4";

    [ObservableProperty]
    private string _newProfileLoader = "Fabric";

    public ObservableCollection<GameProfile> Profiles { get; }

    public ObservableCollection<string> Loaders { get; }

    public ProfilesViewModel()
    {
        Loaders = new ObservableCollection<string> { "Vanilla", "Fabric", "Forge", "NeoForge" };
        
        Profiles = new ObservableCollection<GameProfile>
        {
            new GameProfile { Name = "Main Optimization Pack", Version = "1.20.4", LoaderType = "Fabric", LastPlayed = "Today" },
            new GameProfile { Name = "Legacy Bedwars Core", Version = "1.8.9", LoaderType = "Vanilla", LastPlayed = "Yesterday" },
            new GameProfile { Name = "NeoForge Testing", Version = "1.20.4", LoaderType = "NeoForge", LastPlayed = "3 days ago" }
        };
        
        SelectedProfile = Profiles[0];
    }

    [RelayCommand]
    private void LoginOffline()
    {
        if (string.IsNullOrWhiteSpace(OfflineUsername)) { AccountStatus = "Enter a username first"; return; }
        OfflineAccountStore.SetUsername(OfflineUsername);
        OfflineUsername = OfflineAccountStore.Username;
        AccountStatus = $"Logged in as {OfflineUsername}";
    }

    [RelayCommand]
    private void LogoutOffline()
    {
        OfflineAccountStore.Clear();
        OfflineUsername = string.Empty;
        AccountStatus = "No offline account connected";
    }

    [RelayCommand]
    private void AddProfile()
    {
        if (string.IsNullOrWhiteSpace(NewProfileName)) return;

        var profile = new GameProfile
        {
            Name = NewProfileName,
            Version = NewProfileVersion,
            LoaderType = NewProfileLoader,
            LastPlayed = "Never"
        };

        Profiles.Add(profile);
        SelectedProfile = profile;

        // Clear input form
        NewProfileName = string.Empty;
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile != null)
        {
            Profiles.Remove(SelectedProfile);
            SelectedProfile = Profiles.Count > 0 ? Profiles[0] : null;
        }
    }

    [RelayCommand]
    private void SelectProfile(GameProfile profile)
    {
        SelectedProfile = profile;
    }

    [RelayCommand]
    private void LaunchProfile(GameProfile profile)
    {
        SelectedProfile = profile;
        AccountStatus = $"Selected {profile.Name}. Open Home and press PLAY to launch it.";
    }

    [RelayCommand]
    private void FocusCreate()
    {
        AccountStatus = "Enter the instance details below and create it.";
    }
}
