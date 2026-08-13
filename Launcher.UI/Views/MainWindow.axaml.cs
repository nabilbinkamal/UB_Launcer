using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.Core.ViewModels;
using System;
using System.Collections.Generic;

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
            Interval = TimeSpan.FromMilliseconds(160)
        };
        pagePreloadTimer.Tick += OnPagePreloadTick;

        SizeChanged += OnWindowSizeChanged;
        AttachedToVisualTree += (_, _) =>
        {
            ApplyResponsiveScale(Bounds.Width);
            BeginPagePreload();
        };
        DetachedFromVisualTree += (_, _) => pagePreloadTimer.Stop();
    }

    private void BeginPagePreload()
    {
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

        var scale = Math.Clamp(width / 1440d, 1d, 1.35d);
        if (Math.Abs(scale - appliedScale) < 0.005d)
            return;

        appliedScale = scale;
        MainLayout.ColumnDefinitions[0].Width = new GridLength(230 * scale);
        LogoStack.Margin = new Thickness(28 * scale, 34 * scale, 22 * scale, 1);
        SidebarNavigation.Margin = new Thickness(30 * scale, 0, 21 * scale, 0);
        ProfilePanel.Width = 179 * scale;
        ProfilePanel.Margin = new Thickness(30 * scale, 18, 0, 18);

        foreach (var child in SidebarNavigation.Children)
        {
            if (child is Button button)
            {
                button.Width = 179 * scale;
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

    private void OnMaximizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }
}
