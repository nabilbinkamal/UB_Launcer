using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Services;

namespace Launcher.Core.ViewModels;

public partial class SavedAccountItem : ObservableObject
{
    public SavedAccountItem(string username, bool isActive)
    {
        Username = username;
        IsActive = isActive;
    }

    public string Username { get; }

    [ObservableProperty]
    private bool _isActive;
}

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;
    private string _activeBeforeSettings = nameof(IsHomeActive);
    private CancellationTokenSource? accountStatusCancellation;

    [ObservableProperty]
    private string _username = string.IsNullOrWhiteSpace(OfflineAccountStore.Username) ? "Player" : OfflineAccountStore.Username;

    [ObservableProperty]
    private string _userRank = "Offline";

    [ObservableProperty]
    private bool _isAccountMenuOpen;

    [ObservableProperty]
    private bool _hasSavedAccounts;

    [ObservableProperty]
    private double _accountArrowAngle;

    [ObservableProperty]
    private bool _isSettingsOpen;

    [ObservableProperty]
    private bool _isAccountDialogOpen;

    [ObservableProperty]
    private bool _isOfflineAccountEntryVisible;

    [ObservableProperty]
    private bool _isAccountDialogBusy;

    [ObservableProperty]
    private string _newOfflineUsername = string.Empty;

    [ObservableProperty]
    private string _accountDialogStatus = string.Empty;

    [ObservableProperty]
    private double _settingsBackdropBlurRadius;

    [ObservableProperty]
    private double _mainContentOpacity = 1;

    [ObservableProperty] private bool _isHomeActive = true;
    [ObservableProperty] private bool _isInstanceActive;
    [ObservableProperty] private bool _isModsActive;
    [ObservableProperty] private bool _isCustomizeActive;
    [ObservableProperty] private bool _isPerformanceActive;
    [ObservableProperty] private bool _isCreateServerActive;
    [ObservableProperty] private bool _isSettingsActive;

    public INavigationService Navigation => _navigation;
    public SettingsViewModel Settings { get; } = new();
    public ObservableCollection<SavedAccountItem> SavedAccounts { get; } = new();
    [ObservableProperty] private bool _isUpdatePopupVisible;
    [ObservableProperty] private bool _isMandatoryUpdate;
    [ObservableProperty] private string _updateTitle = string.Empty;
    [ObservableProperty] private string _updateMessage = string.Empty;
    [ObservableProperty] private string _updateUrl = string.Empty;
    [ObservableProperty] private string _remoteLogoImageUrl = string.Empty;

    public bool IsAnyModalOpen => IsSettingsOpen || IsAccountDialogOpen || IsUpdatePopupVisible;
    public bool IsAccountChoiceVisible => !IsOfflineAccountEntryVisible;
    public bool HasUpdateUrl => !string.IsNullOrWhiteSpace(UpdateUrl);
    public bool HasRemoteLogoImage => !string.IsNullOrWhiteSpace(RemoteLogoImageUrl);
    public string HomeIconSource => GetRemoteAsset("homeIcon", "/Assets/home.svg");
    public string InstanceIconSource => GetRemoteAsset("instanceIcon", "/Assets/instance.svg");
    public string ModsIconSource => GetRemoteAsset("modsIcon", "/Assets/mods.svg");
    public string CustomizeIconSource => GetRemoteAsset("customizeIcon", "/Assets/customize.svg");
    public string PerformanceIconSource => GetRemoteAsset("performanceIcon", "/Assets/performance.svg");
    public string ServerIconSource => GetRemoteAsset("serverIcon", "/Assets/createserver.svg");
    public string SettingsIconSource => GetRemoteAsset("settingsIcon", "/Assets/settings.svg");
    public string DiscordIconSource => GetRemoteAsset("discordIcon", "/Assets/discord.svg");
    public string TiktokIconSource => GetRemoteAsset("tiktokIcon", "/Assets/tiktok.svg");
    public string YoutubeIconSource => GetRemoteAsset("youtubeIcon", "/Assets/youtube.svg");
    public string WebIconSource => GetRemoteAsset("webIcon", "/Assets/web.svg");

    public MainWindowViewModel(INavigationService navigation)
    {
        _navigation = navigation;
        OfflineAccountStore.AccountChanged += RefreshAccounts;
        RefreshAccounts();
        // Default to Home View
        _navigation.NavigateTo<HomeViewModel>();
    }

    /// <summary>Applies a verified public configuration after it is downloaded at startup.</summary>
    public void ApplyRemoteConfiguration()
    {
        var configuration = LauncherRemoteConfiguration.Current;
        RemoteLogoImageUrl = configuration.GetAsset("logo", string.Empty);

        foreach (var propertyName in new[]
                 {
                     nameof(HomeIconSource), nameof(InstanceIconSource), nameof(ModsIconSource),
                     nameof(CustomizeIconSource), nameof(PerformanceIconSource), nameof(ServerIconSource),
                     nameof(SettingsIconSource), nameof(DiscordIconSource), nameof(TiktokIconSource),
                     nameof(YoutubeIconSource), nameof(WebIconSource), nameof(HasRemoteLogoImage)
                 })
        {
            OnPropertyChanged(propertyName);
        }

        if (LauncherRemoteConfiguration.IsNewerVersionAvailable() || LauncherRemoteConfiguration.IsUpdateRequired())
        {
            UpdateTitle = configuration.UpdateTitle;
            UpdateMessage = configuration.UpdateMessage;
            UpdateUrl = configuration.UpdateUrl;
            IsMandatoryUpdate = LauncherRemoteConfiguration.IsUpdateRequired();
            IsUpdatePopupVisible = true;
        }

        foreach (var home in _navigation.Views.OfType<HomeViewModel>())
        {
            home.RefreshRemoteBranding();
        }
    }

    [RelayCommand]
    private void OpenUpdate()
    {
        if (!Uri.TryCreate(UpdateUrl, UriKind.Absolute, out var updateUri) || updateUri.Scheme != Uri.UriSchemeHttps)
        {
            return;
        }

        Process.Start(new ProcessStartInfo { FileName = updateUri.AbsoluteUri, UseShellExecute = true });
    }

    [RelayCommand]
    private void DismissUpdate()
    {
        if (!IsMandatoryUpdate)
        {
            IsUpdatePopupVisible = false;
        }
    }

    private static string GetRemoteAsset(string name, string fallback) =>
        LauncherRemoteConfiguration.Current.GetAsset(name, fallback);

    partial void OnUpdateUrlChanged(string value) => OnPropertyChanged(nameof(HasUpdateUrl));
    partial void OnRemoteLogoImageUrlChanged(string value) => OnPropertyChanged(nameof(HasRemoteLogoImage));

    private void RefreshAccounts()
    {
        Username = string.IsNullOrWhiteSpace(OfflineAccountStore.Username) ? "Player" : OfflineAccountStore.Username;
        UserRank = "Offline";

        SavedAccounts.Clear();
        foreach (var account in OfflineAccountStore.Accounts)
        {
            SavedAccounts.Add(new SavedAccountItem(
                account,
                string.Equals(account, OfflineAccountStore.Username, StringComparison.OrdinalIgnoreCase)));
        }

        HasSavedAccounts = SavedAccounts.Count > 0;

        accountStatusCancellation?.Cancel();
        accountStatusCancellation?.Dispose();
        accountStatusCancellation = new CancellationTokenSource();
        _ = RefreshUserRankAsync(Username, accountStatusCancellation.Token);
    }

    private async Task RefreshUserRankAsync(string username, CancellationToken cancellationToken)
    {
        try
        {
            var isPremium = await MinecraftAccountStatusService.IsPremiumUsernameAsync(username, cancellationToken);
            if (!cancellationToken.IsCancellationRequested &&
                isPremium.HasValue &&
                string.Equals(Username, username, StringComparison.OrdinalIgnoreCase))
            {
                UserRank = isPremium.Value ? "Premium" : "Offline";
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    partial void OnIsAccountMenuOpenChanged(bool value)
    {
        AccountArrowAngle = value ? 180 : 0;
    }

    partial void OnIsSettingsOpenChanged(bool value)
    {
        UpdateModalBackdrop();
    }

    partial void OnIsAccountDialogOpenChanged(bool value)
    {
        UpdateModalBackdrop();
    }

    partial void OnIsOfflineAccountEntryVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAccountChoiceVisible));
    }

    private void UpdateModalBackdrop()
    {
        OnPropertyChanged(nameof(IsAnyModalOpen));
        SettingsBackdropBlurRadius = IsAnyModalOpen ? 10 : 0;
        MainContentOpacity = IsAnyModalOpen ? 0.56 : 1;
    }

    [RelayCommand]
    private void ToggleAccountMenu()
    {
        IsAccountMenuOpen = !IsAccountMenuOpen;
    }

    [RelayCommand]
    private void SelectSavedAccount(SavedAccountItem account)
    {
        OfflineAccountStore.SetUsername(account.Username);
        IsAccountMenuOpen = false;
    }

    [RelayCommand]
    private void AddAccount()
    {
        IsAccountMenuOpen = false;
        IsOfflineAccountEntryVisible = false;
        NewOfflineUsername = string.Empty;
        AccountDialogStatus = string.Empty;
        IsAccountDialogOpen = true;
    }

    [RelayCommand]
    private void CloseAccountDialog()
    {
        IsAccountDialogOpen = false;
        IsOfflineAccountEntryVisible = false;
        IsAccountDialogBusy = false;
        NewOfflineUsername = string.Empty;
        AccountDialogStatus = string.Empty;
    }

    [RelayCommand]
    private void ShowOfflineAccountEntry()
    {
        AccountDialogStatus = string.Empty;
        IsOfflineAccountEntryVisible = true;
    }

    [RelayCommand]
    private void ShowAccountChoices()
    {
        AccountDialogStatus = string.Empty;
        IsOfflineAccountEntryVisible = false;
    }

    [RelayCommand]
    private void SaveOfflineAccount()
    {
        var username = NewOfflineUsername.Trim();
        if (username.Length is < 3 or > 16)
        {
            AccountDialogStatus = "Username must contain 3 to 16 characters.";
            return;
        }

        if (username.Any(character => !char.IsLetterOrDigit(character) && character != '_'))
        {
            AccountDialogStatus = "Use only letters, numbers, and underscores.";
            return;
        }

        OfflineAccountStore.SetUsername(username);
        CloseAccountDialog();
    }

    [RelayCommand]
    private void SignInWithMicrosoft()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://www.minecraft.net/login",
                UseShellExecute = true
            });
            AccountDialogStatus = "Microsoft sign-in opened in your browser.";
        }
        catch
        {
            AccountDialogStatus = "Could not open Microsoft sign-in. Please try again.";
        }
    }

    [RelayCommand]
    private void NavigateHome()
    {
        SetActive(nameof(IsHomeActive));
        _navigation.NavigateTo<HomeViewModel>();
    }

    [RelayCommand]
    private void NavigateProfiles()
    {
        SetActive(nameof(IsInstanceActive));
        _navigation.NavigateTo<ProfilesViewModel>();
    }

    [RelayCommand]
    private void NavigateMods()
    {
        SetActive(nameof(IsModsActive));
        _navigation.NavigateTo<ModsViewModel>();
    }

    [RelayCommand]
    private void NavigateCustomize()
    {
        SetActive(nameof(IsCustomizeActive));
        _navigation.NavigateTo<CustomizeViewModel>();
    }

    [RelayCommand]
    private void NavigatePerformance()
    {
        SetActive(nameof(IsPerformanceActive));
        _navigation.NavigateTo<PerformanceViewModel>();
    }

    [RelayCommand]
    private void NavigateCreateServer()
    {
        SetActive(nameof(IsCreateServerActive));
        _navigation.NavigateTo<CreateServerViewModel>();
    }

    [RelayCommand]
    private void NavigateSettings()
    {
        if (IsSettingsOpen)
        {
            return;
        }

        _activeBeforeSettings = GetActiveContentProperty();
        SetActive(nameof(IsSettingsActive));
        IsSettingsOpen = true;
    }

    [RelayCommand]
    private void CloseSettings()
    {
        IsSettingsOpen = false;
        SetActive(_activeBeforeSettings);
    }

    private string GetActiveContentProperty()
    {
        if (IsInstanceActive) return nameof(IsInstanceActive);
        if (IsModsActive) return nameof(IsModsActive);
        if (IsCustomizeActive) return nameof(IsCustomizeActive);
        if (IsPerformanceActive) return nameof(IsPerformanceActive);
        if (IsCreateServerActive) return nameof(IsCreateServerActive);
        return nameof(IsHomeActive);
    }

    private void SetActive(string activeProperty)
    {
        IsHomeActive = activeProperty == nameof(IsHomeActive);
        IsInstanceActive = activeProperty == nameof(IsInstanceActive);
        IsModsActive = activeProperty == nameof(IsModsActive);
        IsCustomizeActive = activeProperty == nameof(IsCustomizeActive);
        IsPerformanceActive = activeProperty == nameof(IsPerformanceActive);
        IsCreateServerActive = activeProperty == nameof(IsCreateServerActive);
        IsSettingsActive = activeProperty == nameof(IsSettingsActive);
    }
}
