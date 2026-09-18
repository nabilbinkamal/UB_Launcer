using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.Core.Services;
using Launcher.Core.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Launcher.UI.Views;

public partial class MainWindow : Window
{
    private double appliedScale;
    private bool scaleUpdateQueued;
    private double pendingScaleWidth;
    private readonly Queue<Action<MainWindowViewModel>> pagePreloadQueue = new();
    private readonly DispatcherTimer pagePreloadTimer;

    public MainWindow()
    {
        InitializeComponent();

        pagePreloadTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        pagePreloadTimer.Tick += OnPagePreloadTick;

        SizeChanged += OnWindowSizeChanged;
        AttachedToVisualTree += (_, _) =>
        {
            ApplyResponsiveScale(Bounds.Width);
            BeginPagePreload();

            if (DataContext is MainWindowViewModel vm)
                vm.CopyToClipboardRequested += OnCopyToClipboardRequested;
        };
        DetachedFromVisualTree += (_, _) =>
        {
            pagePreloadTimer.Stop();
            if (DataContext is MainWindowViewModel vm)
                vm.CopyToClipboardRequested -= OnCopyToClipboardRequested;
        };
    }

    private async void OnCopyToClipboardRequested(string text)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
                await clipboard.SetTextAsync(text);
        }
        catch { /* clipboard unavailable */ }
    }

    private async void BeginPagePreload()
    {
        if (DataContext is not MainWindowViewModel)
        {
            return;
        }

        // Defer background page preloading so initial launcher window renders instantly
        await Task.Delay(3000);
        if (DataContext is not MainWindowViewModel)
        {
            return;
        }

        pagePreloadQueue.Enqueue(viewModel => viewModel.Navigation.Preload<ProfilesViewModel>());
        pagePreloadQueue.Enqueue(viewModel => viewModel.Navigation.Preload<CustomizeViewModel>());
        pagePreloadQueue.Enqueue(viewModel => viewModel.Navigation.Preload<ModsViewModel>());
        pagePreloadQueue.Enqueue(viewModel => viewModel.Navigation.Preload<PerformanceViewModel>());
        pagePreloadQueue.Enqueue(viewModel => viewModel.Navigation.Preload<CreateServerViewModel>());
        pagePreloadTimer.Start();
    }

    private void OnPagePreloadTick(object? sender, EventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel || pagePreloadQueue.Count == 0)
        {
            pagePreloadTimer.Stop();
            return;
        }

        pagePreloadQueue.Dequeue()(viewModel);
    }

    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        pendingScaleWidth = e.NewSize.Width;
        if (scaleUpdateQueued)
        {
            return;
        }

        scaleUpdateQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            scaleUpdateQueued = false;
            ApplyResponsiveScale(pendingScaleWidth);
        }, DispatcherPriority.Render);
    }

    private void ApplyResponsiveScale(double width)
    {
        if (width <= 0)
        {
            return;
        }

        var fullscreenProgress = Math.Clamp((width - 1440d) / 480d, 0d, 1d);
        var sidebarScale = 1d + (0.14d * fullscreenProgress);
        if (Math.Abs(sidebarScale - appliedScale) < 0.005d)
            return;

        appliedScale = sidebarScale;
        MainLayout.ColumnDefinitions[0].Width = new GridLength(230 * sidebarScale);
        LogoStack.Margin = new Thickness(33 * sidebarScale, 11, 20 * sidebarScale, 0);
        SidebarNavigation.Margin = new Thickness(30 * sidebarScale, 0, 1 * sidebarScale, 0);
        ProfilePanel.Width = 199 * sidebarScale;
        ProfilePanel.Margin = new Thickness(30 * sidebarScale, 18, 0, 18);

        foreach (var child in SidebarNavigation.Children)
        {
            if (child is Button button)
            {
                button.Width = 199 * sidebarScale;
            }
        }
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        e.Handled = true;
        BeginMoveDrag(e);
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnUpdateExitClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnMaximizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnDiscordSocialPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            try
            {
                var url = !string.IsNullOrWhiteSpace(LauncherRemoteConfiguration.Current.DiscordCommunityUrl)
                    ? LauncherRemoteConfiguration.Current.DiscordCommunityUrl
                    : "https://discord.gg/8Cge9nBqVh";

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}
