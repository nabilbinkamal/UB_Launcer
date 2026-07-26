using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Launcher.UI.Views;

public partial class HomeView : UserControl
{
    private const double CompactBreakpoint = 1180;
    private bool? isCompact;

    public HomeView()
    {
        InitializeComponent();
        if (RightRail.Children[0] is Border { Child: Grid performanceGrid })
        {
            performanceGrid.RowDefinitions = new RowDefinitions("Auto,*,*,*,*");
        }

        SizeChanged += OnSizeChanged;
        AttachedToVisualTree += (_, _) => ApplyResponsiveLayout(Bounds.Width, Bounds.Height);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveLayout(e.NewSize.Width, e.NewSize.Height);
    }

    private void ApplyResponsiveLayout(double width, double height)
    {
        var shouldUseCompactLayout = width < CompactBreakpoint;
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

        DashboardGrid.ColumnDefinitions = new ColumnDefinitions("*,313");
        DashboardGrid.RowDefinitions = new RowDefinitions("Auto");
        DashboardGrid.ColumnSpacing = 9;
        Grid.SetColumn(RightRail, 1);
        Grid.SetRow(RightRail, 0);
        RightRail.Width = double.NaN;
        RightRail.HorizontalAlignment = HorizontalAlignment.Stretch;
        RightRail.Margin = new Thickness(0);
    }
}
