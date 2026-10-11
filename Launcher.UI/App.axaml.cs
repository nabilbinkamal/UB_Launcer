using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Launcher.Core.Services;
using Launcher.Core.ViewModels;
using Launcher.UI.Views;
using Launcher.Services;

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
            desktop.Exit += (_, _) =>
            {
                LauncherGameSettingsStore.Flush();
                DiscordPresenceService.Shutdown();
            };

            _ = RefreshPublicConfigurationAsync(mainViewModel);
            LauncherAnalyticsService.StartHeartbeatLoop();
            _ = Task.Run(ExtractDefaultSkins);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ExtractDefaultSkins()
    {
        try
        {
            var cosmeticsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UBLauncher", "cosmetics");
            var defaultSkinsDir = Path.Combine(cosmeticsDir, "default-skins");
            Directory.CreateDirectory(defaultSkinsDir);

            string[] skins = ["steve", "alex", "ari", "efe", "kai", "makena", "noor", "sunny", "zuri"];
            foreach (var skin in skins)
            {
                var uri = new Uri($"avares://Launcher.UI/Assets/skins/{skin}.png");
                var destPath = Path.Combine(defaultSkinsDir, $"{skin}.png");
                if (!File.Exists(destPath))
                {
                    using var assetStream = Avalonia.Platform.AssetLoader.Open(uri);
                    using var fileStream = File.Create(destPath);
                    assetStream.CopyTo(fileStream);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to extract default skins: {ex}");
        }
    }

    private static async Task RefreshPublicConfigurationAsync(MainWindowViewModel mainViewModel)
    {
        try
        {
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
            if (await LauncherRemoteConfiguration.RefreshAsync(cts.Token))
            {
                await Dispatcher.UIThread.InvokeAsync(mainViewModel.ApplyRemoteConfiguration);
            }
        }
        catch
        {
        }

        _ = Task.Run(LauncherAnalyticsService.SendLauncherOpenAsync);
    }
}
