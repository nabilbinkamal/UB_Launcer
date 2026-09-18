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
    private const double HeroAspectRatio = 836d / 279d;
    private const double RecentInstancesHorizontalPadding = 34d;
    private const double RecentInstancesCardSpacing = 10d;
    private const double RecentInstancesEdgeGuard = 3d;
    private bool? isCompact;
    private bool isAttached;
    private bool telemetryReadInProgress;
    private bool responsiveLayoutQueued;
    private bool isNavigationActive;
    private Size pendingResponsiveSize;
    private WindowBase? observedWindow;
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
        RecentInstancesPanel.SizeChanged += OnRecentInstancesPanelSizeChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        isAttached = true;
        ApplyResponsiveLayout(Bounds.Width, Bounds.Height);
        AttachToHostWindow();
        UpdateDisplayResolution();
        UpdateTelemetryState();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        isAttached = false;
        DetachFromHostWindow();
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
            UpdateDisplayResolution();
        }, DispatcherPriority.Render);
    }

    private void AttachToHostWindow()
    {
        var window = TopLevel.GetTopLevel(this) as WindowBase;
        if (ReferenceEquals(observedWindow, window))
        {
            return;
        }

        DetachFromHostWindow();
        observedWindow = window;
        if (observedWindow is not null)
        {
            observedWindow.PositionChanged += OnHostWindowPositionChanged;
        }
    }

    private void DetachFromHostWindow()
    {
        if (observedWindow is not null)
        {
            observedWindow.PositionChanged -= OnHostWindowPositionChanged;
            observedWindow = null;
        }
    }

    private void OnHostWindowPositionChanged(object? sender, PixelPointEventArgs eventArgs) =>
        UpdateDisplayResolution();

    private void UpdateDisplayResolution()
    {
        if (DataContext is not HomeViewModel viewModel || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        if (topLevel.Screens is not { } screens)
        {
            return;
        }

        var screen = screens.ScreenFromVisual(this);
        if (screen is not null)
        {
            viewModel.UpdateDisplayResolution(screen.Bounds.Width, screen.Bounds.Height);
        }
    }

    private void OnRecentInstancesPanelSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateRecentInstanceCardWidth(e.NewSize.Width);
    }

    private void UpdateRecentInstanceCardWidth(double panelWidth)
    {
        if (panelWidth <= 0)
        {
            return;
        }

        var cardsWidth = panelWidth
                         - RecentInstancesHorizontalPadding
                         - (RecentInstancesCardSpacing * 2d)
                         - RecentInstancesEdgeGuard;
        Resources["RecentInstanceCardWidth"] = Math.Max(140d, Math.Floor(cardsWidth / 3d));
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

    private void OnEditInstanceClick(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs) =>
        NavigateToProfiles();

    private void OnViewPerformanceDetailsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs) =>
        NavigateToPerformance();

    private void OnAddInstanceClick(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs) =>
        NavigateToProfiles();

    private void OnEditProfileClick(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs) =>
        NavigateToProfiles();

    private void OnInstallModsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs) =>
        NavigateToMods();

    private void OnLaunchOptionsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs) =>
        NavigateToSettings();

    private void OnJoinDiscordClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        e.Handled = true;
        OpenDiscordInvite();
    }

    private void OnDiscordCardPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            OpenDiscordInvite();
        }
    }

    private static void OpenDiscordInvite()
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
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Could not open Discord invite URL.");
        }
    }

    private MainWindowViewModel? GetMainWindowViewModel() =>
        TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel;

    private void NavigateToProfiles()
    {
        var viewModel = GetMainWindowViewModel();
        if (viewModel?.NavigateProfilesCommand.CanExecute(null) == true)
        {
            viewModel.NavigateProfilesCommand.Execute(null);
        }
    }

    private void NavigateToMods()
    {
        var viewModel = GetMainWindowViewModel();
        if (viewModel?.NavigateModsCommand.CanExecute(null) == true)
        {
            viewModel.NavigateModsCommand.Execute(null);
        }
    }

    private void NavigateToPerformance()
    {
        var viewModel = GetMainWindowViewModel();
        if (viewModel?.NavigatePerformanceCommand.CanExecute(null) == true)
        {
            viewModel.NavigatePerformanceCommand.Execute(null);
        }
    }

    private void NavigateToSettings()
    {
        var viewModel = GetMainWindowViewModel();
        if (viewModel?.NavigateSettingsCommand.CanExecute(null) == true)
        {
            viewModel.NavigateSettingsCommand.Execute(null);
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

    private async void OnPartnerServerClick(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        if (sender is not Button { DataContext: PartnerServerItem server } ||
            DataContext is not HomeViewModel viewModel)
        {
            return;
        }

        await viewModel.LaunchPartnerServerAsync(server);
    }

    private void ApplyResponsiveLayout(double width, double height)
    {
        var shouldUseCompactLayout = width < CompactBreakpoint;
        var fullscreenProgress = shouldUseCompactLayout
            ? 0d
            : Math.Clamp((width - 1210d) / 440d, 0d, 1d);
        var rightRailScale = 1d + (0.15d * fullscreenProgress);
        var verticalRailScale = 1d + (0.18d * fullscreenProgress);
        var rightRailWidth = 313d * rightRailScale;
        var dashboardColumnSpacing = 9d + (3d * fullscreenProgress);
        var dashboardWidth = Math.Max(0, width - 52);
        var primaryWidth = shouldUseCompactLayout
            ? dashboardWidth
            : Math.Max(0, dashboardWidth - rightRailWidth - dashboardColumnSpacing);
        if (primaryWidth > 0)
        {
            HeroCard.Height = primaryWidth / HeroAspectRatio;
        }
        DashboardHeader.ColumnDefinitions = new ColumnDefinitions("*,Auto");
        PrimaryColumn.RowSpacing = 12d + (2d * fullscreenProgress);
        LaunchGraph.Width = 175d * rightRailScale;
        CpuGraph.Width = 175d * rightRailScale;
        GpuGraph.Width = 175d * rightRailScale;
        LaunchGraph.Height = 44d * verticalRailScale;
        CpuGraph.Height = 44d * verticalRailScale;
        GpuGraph.Height = 44d * verticalRailScale;
        MemoryRingContainer.Width = 50d * rightRailScale;
        MemoryRingContainer.Height = 50d * rightRailScale;
        QuickActionsStack.Spacing = 7d * verticalRailScale;
        QuickActionsPanel.Padding = new Thickness(
            18d * rightRailScale,
            15d * verticalRailScale,
            18d * rightRailScale,
            19d * verticalRailScale);
        foreach (var child in QuickActionsStack.Children)
        {
            if (child is Button button)
            {
                button.Height = 38d * verticalRailScale;
            }
        }

        RightRail.RowSpacing = 12d * verticalRailScale;
        RightRail.RowDefinitions[0].Height = new GridLength(398d * verticalRailScale);
        RightRail.RowDefinitions[1].Height = new GridLength(239d * verticalRailScale);
        RightRail.RowDefinitions[2].Height = new GridLength(142d * rightRailScale);
        DiscordCard.Height = 142d * rightRailScale;
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
        DashboardGrid.ColumnSpacing = dashboardColumnSpacing;
        Grid.SetColumn(RightRail, 1);
        Grid.SetRow(RightRail, 0);
        RightRail.Width = double.NaN;
        RightRail.HorizontalAlignment = HorizontalAlignment.Stretch;
        RightRail.Margin = new Thickness(0);
    }
}
