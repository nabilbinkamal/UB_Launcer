using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Launcher.Core.Services;
using Launcher.Core.ViewModels;
using Launcher.UI.Views;

namespace Launcher.UI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Func<Type, ViewModelBase> factory = type =>
            {
                if (type == typeof(HomeViewModel)) return new HomeViewModel();
                if (type == typeof(ProfilesViewModel)) return new ProfilesViewModel();
                if (type == typeof(SettingsViewModel)) return new SettingsViewModel();
                if (type == typeof(ModsViewModel)) return new ModsViewModel();
                if (type == typeof(CustomizeViewModel)) return new CustomizeViewModel();
                if (type == typeof(PerformanceViewModel)) return new PerformanceViewModel();
                if (type == typeof(CreateServerViewModel)) return new CreateServerViewModel();
                throw new ArgumentException($"Unknown ViewModel type: {type}");
            };

            var navigationService = new NavigationService(factory);
            var mainViewModel = new MainWindowViewModel(navigationService);

            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel,
            };

            _ = RefreshPublicConfigurationAsync(mainViewModel);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task RefreshPublicConfigurationAsync(MainWindowViewModel mainViewModel)
    {
        if (!await LauncherRemoteConfiguration.RefreshAsync())
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(mainViewModel.ApplyRemoteConfiguration);
    }
}
