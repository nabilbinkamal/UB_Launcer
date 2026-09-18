using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Services;
using Launcher.Services;

namespace Launcher.Core.ViewModels;

public partial class SavedAccountItem : ObservableObject
{
    public SavedAccountItem(string username, bool isActive, bool isMicrosoftAccount)
    {
        Username = username;
        IsActive = isActive;
        IsMicrosoftAccount = isMicrosoftAccount;
    }

    public string Username { get; }
    public bool IsMicrosoftAccount { get; }
    public string AccountTypeLabel => IsMicrosoftAccount ? "Microsoft" : "Offline";

    [ObservableProperty]
    private bool _isActive;
}

public partial class MainWindowViewModel : ViewModelBase
{
    private const string DefaultDiscordApplicationId = "1538201318798397550";
    private readonly INavigationService _navigation;
    private string _activeBeforeSettings = nameof(IsHomeActive);
    private CancellationTokenSource? accountStatusCancellation;
    private CancellationTokenSource? _microsoftSignInCts;

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
    private bool _isMicrosoftSignInBusy;

    [ObservableProperty]
    private bool _isMicrosoftSignedIn = MicrosoftAccountStore.HasSession;

    [ObservableProperty]
    private bool _isMicrosoftSignInPanelVisible;

    [ObservableProperty]
    private string _deviceCodeStatus = string.Empty;

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
    [ObservableProperty] private string _releaseNotesUrl = string.Empty;
    [ObservableProperty] private string _remoteLogoImageUrl = string.Empty;

    public bool IsAnyModalOpen => IsSettingsOpen || IsAccountDialogOpen || IsUpdatePopupVisible;
    public bool IsAccountChoiceVisible => !IsOfflineAccountEntryVisible && !IsMicrosoftSignInPanelVisible;
    public bool HasUpdateUrl => !string.IsNullOrWhiteSpace(UpdateUrl);
    public bool HasRemoteLogoImage => !string.IsNullOrWhiteSpace(RemoteLogoImageUrl);
    public string UpdatePopupHeading => IsMandatoryUpdate ? "Update Required" : UpdateTitle;
    public bool HasReleaseNotesUrl => !string.IsNullOrWhiteSpace(ReleaseNotesUrl) || HasUpdateUrl;
    public string InstalledVersionText => FormatLauncherVersion(LauncherRemoteConfiguration.InstalledVersion);
    public string LatestVersionText => FormatLauncherVersion(LauncherRemoteConfiguration.Current.LatestVersion);
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
        IsAccountMenuOpen = false;
        AccountArrowAngle = 0;
        ConfigureDiscordPresence();
        DiscordPresenceService.SetEnabled(LauncherGameSettingsStore.Current.EnableDiscordRpc);
        OfflineAccountStore.AccountChanged += RefreshAccounts;
        RefreshAccounts();
        DiscordPresenceService.ShowLauncher(Username);
        // Default to Home View
        _navigation.NavigateTo<HomeViewModel>();
    }

    /// <summary>Applies a verified public configuration after it is downloaded at startup.</summary>
    public void ApplyRemoteConfiguration()
    {
        var configuration = LauncherRemoteConfiguration.Current;
        ConfigureDiscordPresence();
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

        // Show every available update once per running session. Dismissing this
        // only changes this view-model instance, so a fresh launcher process will
        // show the prompt again while the newer version is still available.
        if (ShouldShowUpdatePopup() && LauncherRemoteConfiguration.IsNewerVersionAvailable())
        {
            UpdateTitle = configuration.UpdateTitle;
            UpdateMessage = configuration.UpdateMessage;
            UpdateUrl = configuration.UpdateUrl;
            ReleaseNotesUrl = configuration.ReleaseNotesUrl;
            IsMandatoryUpdate = false;
            IsUpdatePopupVisible = true;
            OnPropertyChanged(nameof(InstalledVersionText));
            OnPropertyChanged(nameof(LatestVersionText));
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

    [RelayCommand]
    private void OpenReleaseNotes()
    {
        var target = string.IsNullOrWhiteSpace(ReleaseNotesUrl) ? UpdateUrl : ReleaseNotesUrl;
        if (!Uri.TryCreate(target, UriKind.Absolute, out var releaseNotesUri) ||
            releaseNotesUri.Scheme != Uri.UriSchemeHttps)
        {
            return;
        }

        Process.Start(new ProcessStartInfo { FileName = releaseNotesUri.AbsoluteUri, UseShellExecute = true });
    }

    private static string GetRemoteAsset(string name, string fallback) =>
        LauncherRemoteConfiguration.Current.GetAsset(name, fallback);

    private static bool ShouldShowUpdatePopup()
    {
#if DEBUG
        return string.Equals(
            Environment.GetEnvironmentVariable("UB_SHOW_DEV_UPDATES"),
            "1",
            StringComparison.Ordinal);
#else
        return true;
#endif
    }

    private static void ConfigureDiscordPresence()
    {
        var configuration = LauncherRemoteConfiguration.Current;
        var applicationId = string.IsNullOrWhiteSpace(configuration.DiscordApplicationId)
            ? Environment.GetEnvironmentVariable("UB_DISCORD_APPLICATION_ID") ?? DefaultDiscordApplicationId
            : configuration.DiscordApplicationId;
        var largeImage = string.IsNullOrWhiteSpace(configuration.DiscordLargeImage)
            ? Environment.GetEnvironmentVariable("UB_DISCORD_LARGE_IMAGE") ?? string.Empty
            : configuration.DiscordLargeImage;
        var communityUrl = string.IsNullOrWhiteSpace(configuration.DiscordCommunityUrl)
            ? Environment.GetEnvironmentVariable("UB_DISCORD_COMMUNITY_URL") ?? string.Empty
            : configuration.DiscordCommunityUrl;

        DiscordPresenceService.Configure(new DiscordPresenceOptions(
            applicationId,
            largeImage,
            communityUrl));
    }

    private static string FormatLauncherVersion(string? version)
    {
        var normalized = version?.Trim().TrimStart('v', 'V') ?? string.Empty;
        return string.IsNullOrWhiteSpace(normalized) ? "Unknown" : $"v{normalized}";
    }

    partial void OnUpdateUrlChanged(string value)
    {
        OnPropertyChanged(nameof(HasUpdateUrl));
        OnPropertyChanged(nameof(HasReleaseNotesUrl));
    }
    partial void OnReleaseNotesUrlChanged(string value) => OnPropertyChanged(nameof(HasReleaseNotesUrl));
    partial void OnUpdateTitleChanged(string value) => OnPropertyChanged(nameof(UpdatePopupHeading));
    partial void OnIsMandatoryUpdateChanged(bool value) => OnPropertyChanged(nameof(UpdatePopupHeading));
    partial void OnIsUpdatePopupVisibleChanged(bool value) => UpdateModalBackdrop();
    partial void OnRemoteLogoImageUrlChanged(string value) => OnPropertyChanged(nameof(HasRemoteLogoImage));

    private void RefreshAccounts()
    {
        Username = string.IsNullOrWhiteSpace(OfflineAccountStore.Username) ? "Player" : OfflineAccountStore.Username;
        DiscordPresenceService.UpdateUsername(Username);
        UserRank = IsMicrosoftAccountUsername(Username) ? "Microsoft" : "Offline";

        SavedAccounts.Clear();
        foreach (var account in OfflineAccountStore.Accounts)
        {
            SavedAccounts.Add(new SavedAccountItem(
                account,
                string.Equals(account, OfflineAccountStore.Username, StringComparison.OrdinalIgnoreCase),
                IsMicrosoftAccountUsername(account)));
        }

        HasSavedAccounts = SavedAccounts.Count > 0;

        accountStatusCancellation?.Cancel();
        accountStatusCancellation?.Dispose();
        accountStatusCancellation = new CancellationTokenSource();
        _ = RefreshUserRankAsync(Username, accountStatusCancellation.Token);
    }

    private async Task RefreshUserRankAsync(string username, CancellationToken cancellationToken)
    {
        if (IsMicrosoftAccountUsername(username))
        {
            UserRank = "Microsoft";
            return;
        }

        try
        {
            var isPremium = await MinecraftAccountStatusService.IsPremiumUsernameAsync(username, cancellationToken);
            if (!cancellationToken.IsCancellationRequested &&
                isPremium.HasValue &&
                string.Equals(Username, username, StringComparison.OrdinalIgnoreCase))
            {
                UserRank = isPremium.Value ? "Premium name" : "Offline";
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

    partial void OnIsMicrosoftSignInPanelVisibleChanged(bool value)
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
    private void DeleteSavedAccount(SavedAccountItem? account)
    {
        if (account is null) return;
        OfflineAccountStore.Remove(account.Username);
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
        IsMicrosoftSignInPanelVisible = false;
        IsMicrosoftSignInBusy = false;
        _microsoftSignInCts?.Cancel();
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
        var username = NewOfflineUsername?.Trim() ?? string.Empty;
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
    private async Task SignInWithMicrosoft()
    {
        // Cancel any previous in-progress sign-in.
        _microsoftSignInCts?.Cancel();
        _microsoftSignInCts?.Dispose();
        _microsoftSignInCts = new CancellationTokenSource();
        var ct = _microsoftSignInCts.Token;

        IsMicrosoftSignInBusy = true;
        IsMicrosoftSignInPanelVisible = true;
        DeviceCodeStatus = "Connecting to Microsoft…";

        try
        {
            var progress = new Progress<string>(msg => DeviceCodeStatus = msg);
            var session = await MicrosoftAuthService.AuthenticateInteractiveAsync(progress, ct);

            // Persist + update local username.
            MicrosoftAccountStore.Save(session);
            OfflineAccountStore.SetUsername(session.MinecraftUsername);

            IsMicrosoftSignedIn = true;
            DeviceCodeStatus = $"Signed in as {session.MinecraftUsername} ✓";
            await Task.Delay(1200, ct);
            IsMicrosoftSignInPanelVisible = false;
            CloseAccountDialog();
        }
        catch (OperationCanceledException)
        {
            DeviceCodeStatus = "Sign-in cancelled.";
            await Task.Delay(900, CancellationToken.None);
            IsMicrosoftSignInPanelVisible = false;
        }
        catch (InvalidOperationException ex)
        {
            DeviceCodeStatus = ex.Message;
        }
        catch (Exception)
        {
            DeviceCodeStatus = "Sign-in failed. Check your connection and try again.";
        }
        finally
        {
            IsMicrosoftSignInBusy = false;
        }
    }

    [RelayCommand]
    private void CancelMicrosoftSignIn()
    {
        _microsoftSignInCts?.Cancel();
    }

    /// <summary>Raised when the ViewModel wants text copied to the clipboard. The View subscribes to handle it.</summary>
    public event Action<string>? CopyToClipboardRequested;

    [RelayCommand]
    private void SignOutMicrosoft()
    {
        MicrosoftAccountStore.Clear();
        IsMicrosoftSignedIn = false;
        IsAccountMenuOpen = false;
        RefreshAccounts();
    }

    private static bool IsMicrosoftAccountUsername(string username) =>
        MicrosoftAccountStore.HasSession &&
        string.Equals(MicrosoftAccountStore.MinecraftUsername, username, StringComparison.OrdinalIgnoreCase);

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
        if (_navigation.CurrentView is ModsViewModel modsVm)
        {
            _ = modsVm.EnsureLoadedAsync();
        }
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
