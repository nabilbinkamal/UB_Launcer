using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Services;

namespace Launcher.Core.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;

    [ObservableProperty]
    private string _username = string.IsNullOrWhiteSpace(OfflineAccountStore.Username) ? "Player" : OfflineAccountStore.Username;

    [ObservableProperty]
    private string _userRank = "VIP Premium Member";

    [ObservableProperty] private bool _isHomeActive = true;
    [ObservableProperty] private bool _isInstanceActive;
    [ObservableProperty] private bool _isModsActive;
    [ObservableProperty] private bool _isCustomizeActive;
    [ObservableProperty] private bool _isPerformanceActive;
    [ObservableProperty] private bool _isCreateServerActive;
    [ObservableProperty] private bool _isSettingsActive;

    public INavigationService Navigation => _navigation;

    public MainWindowViewModel(INavigationService navigation)
    {
        _navigation = navigation;
        OfflineAccountStore.AccountChanged += RefreshUsername;
        // Default to Home View
        _navigation.NavigateTo<HomeViewModel>();
    }

    private void RefreshUsername()
    {
        Username = string.IsNullOrWhiteSpace(OfflineAccountStore.Username) ? "Player" : OfflineAccountStore.Username;
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
        SetActive(nameof(IsSettingsActive));
        _navigation.NavigateTo<SettingsViewModel>();
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
