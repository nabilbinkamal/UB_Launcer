using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.UI.Services;
using Launcher.Core.ViewModels;
using System.Runtime.Versioning;

namespace Launcher.UI.Views;

[SupportedOSPlatform("windows")]
public partial class HomeView : UserControl
{
    private const double CompactBreakpoint = 1180;
    private bool? isCompact;
    private readonly DispatcherTimer telemetryTimer;
    private readonly WindowsTelemetryReader telemetryReader;

    public HomeView()
    {
        InitializeComponent();
        if (RightRail.Children[0] is Border { Child: Grid performanceGrid })
        {
            performanceGrid.RowDefinitions = new RowDefinitions("Auto,*,*,*,*");
        }

        telemetryReader = new WindowsTelemetryReader();
        telemetryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        telemetryTimer.Tick += OnTelemetryTick;

        SizeChanged += OnSizeChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ApplyResponsiveLayout(Bounds.Width, Bounds.Height);
        OnTelemetryTick(this, EventArgs.Empty);
        telemetryTimer.Start();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        telemetryTimer.Stop();
    }

    private void OnTelemetryTick(object? sender, EventArgs e)
    {
        if (DataContext is not HomeViewModel viewModel)
        {
            return;
        }

        var snapshot = telemetryReader.Read();
        viewModel.UpdateTelemetry(snapshot.CpuUsage, snapshot.GpuUsage, snapshot.UsedMemoryGb, snapshot.TotalMemoryGb);
        viewModel.RefreshTotalPlaytime();
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveLayout(e.NewSize.Width, e.NewSize.Height);
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
