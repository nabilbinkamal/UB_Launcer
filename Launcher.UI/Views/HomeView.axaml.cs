using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.UI.Services;
using Launcher.UI.Controls;
using Launcher.Core.Services;
using Launcher.Core.ViewModels;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace Launcher.UI.Views;

[SupportedOSPlatform("windows")]
public partial class HomeView : UserControl, INavigationAwarePage
{
    private const double CompactBreakpoint = 1180;
    private bool? isCompact;
    private bool isAttached;
    private bool telemetryReadInProgress;
    private bool responsiveLayoutQueued;
    private bool isNavigationActive;
    private Size pendingResponsiveSize;
    private readonly DispatcherTimer telemetryTimer;
    private readonly WindowsTelemetryReader telemetryReader;

    public HomeView()
    {
        InitializeComponent();
        if (RightRail.Children[0] is Border { Child: Grid performanceGrid })
        {
            performanceGrid.RowDefinitions = new RowDefinitions("Auto,*,*,*,*");
        }

        telemetryReader = WindowsTelemetryReader.Shared;
        telemetryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        telemetryTimer.Tick += OnTelemetryTick;

        SizeChanged += OnSizeChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        isAttached = true;
        ApplyResponsiveLayout(Bounds.Width, Bounds.Height);
        UpdateTelemetryState();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        isAttached = false;
        telemetryTimer.Stop();
    }

    private void UpdateTelemetryState()
    {
        if (!isAttached || !isNavigationActive)
        {
            telemetryTimer.Stop();
            return;
        }

        OnTelemetryTick(this, EventArgs.Empty);
        telemetryTimer.Start();
    }

    public void SetNavigationActive(bool isActive)
    {
        isNavigationActive = isActive;
        UpdateTelemetryState();
    }

    private async void OnTelemetryTick(object? sender, EventArgs e)
    {
        if (!isAttached || !isNavigationActive || telemetryReadInProgress ||
            TopLevel.GetTopLevel(this) is Window { WindowState: WindowState.Minimized })
        {
            return;
        }

        telemetryReadInProgress = true;
        try
        {
            // PerformanceCounter.NextValue can briefly block. Keep it off the UI thread so
            // native window move/resize remains responsive.
            var snapshot = await Task.Run(telemetryReader.Read);
            if (!isAttached || DataContext is not HomeViewModel viewModel)
            {
                return;
            }

            viewModel.UpdateTelemetry(snapshot.CpuUsage, snapshot.GpuUsage, snapshot.UsedMemoryGb, snapshot.TotalMemoryGb);
            viewModel.RefreshTotalPlaytime();
            viewModel.RefreshRecentData();
        }
        finally
        {
            telemetryReadInProgress = false;
        }
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        pendingResponsiveSize = e.NewSize;
        if (responsiveLayoutQueued)
        {
            return;
        }

        responsiveLayoutQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            responsiveLayoutQueued = false;
            ApplyResponsiveLayout(pendingResponsiveSize.Width, pendingResponsiveSize.Height);
        }, DispatcherPriority.Render);
    }

    private void OnRecentInstanceClick(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        if (sender is not Button { DataContext: GameProfile profile } ||
            TopLevel.GetTopLevel(this)?.DataContext is not MainWindowViewModel mainViewModel)
        {
            return;
        }

        InstanceStore.SelectedProfile = profile;
        if (mainViewModel.NavigateProfilesCommand.CanExecute(null))
        {
            mainViewModel.NavigateProfilesCommand.Execute(null);
        }
    }

    private void OnRecentServerClick(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        if (sender is not Button { DataContext: LocalServerItem server } ||
            TopLevel.GetTopLevel(this)?.DataContext is not MainWindowViewModel mainViewModel)
        {
            return;
        }

        ServerStore.Touch(server);
        ServerStore.SelectedServer = server;
        if (mainViewModel.NavigateCreateServerCommand.CanExecute(null))
        {
            mainViewModel.NavigateCreateServerCommand.Execute(null);
        }
    }

    private void ApplyResponsiveLayout(double width, double height)
    {
        var shouldUseCompactLayout = width < CompactBreakpoint;
        var scale = Math.Clamp(width / 1210d, 1d, 1.35d);
        var rightRailWidth = 313 * scale;
        DashboardHeader.ColumnDefinitions = new ColumnDefinitions("*,Auto");
        LaunchGraph.Width = 180 * scale;
        CpuGraph.Width = 180 * scale;
        GpuGraph.Width = 180 * scale;
        DiscordCard.Height = 142 * scale;
        RightRail.RowDefinitions[2].Height = new GridLength(142 * scale);
        DashboardGrid.ColumnDefinitions = shouldUseCompactLayout
            ? new ColumnDefinitions("*")
            : new ColumnDefinitions($"*,{rightRailWidth}");
        DashboardGrid.MinHeight = !shouldUseCompactLayout && height >= 980
            ? Math.Max(0, height - 72)
            : 0;

        if (isCompact == shouldUseCompactLayout)
        {
            return;
        }

        isCompact = shouldUseCompactLayout;

        if (shouldUseCompactLayout)
        {
            DashboardGrid.ColumnDefinitions = new ColumnDefinitions("*");
            DashboardGrid.RowDefinitions = new RowDefinitions("Auto,Auto");
            DashboardGrid.ColumnSpacing = 0;
            Grid.SetColumn(RightRail, 0);
            Grid.SetRow(RightRail, 1);
            RightRail.Width = 313;
            RightRail.HorizontalAlignment = HorizontalAlignment.Left;
            RightRail.Margin = new Thickness(0);
            return;
        }

        DashboardGrid.RowDefinitions = new RowDefinitions("Auto");
        DashboardGrid.ColumnSpacing = 9;
        Grid.SetColumn(RightRail, 1);
        Grid.SetRow(RightRail, 0);
        RightRail.Width = double.NaN;
        RightRail.HorizontalAlignment = HorizontalAlignment.Stretch;
        RightRail.Margin = new Thickness(0);
    }
}
