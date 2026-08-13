using System;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.Core.ViewModels;
using Launcher.UI.Controls;
using Launcher.UI.Services;

namespace Launcher.UI.Views;

[SupportedOSPlatform("windows")]
public partial class PerformanceView : UserControl, INavigationAwarePage
{
    private readonly DispatcherTimer telemetryTimer;
    private readonly WindowsTelemetryReader telemetryReader;
    private bool isAttached;
    private bool isNavigationActive;
    private bool telemetryReadInProgress;

    public PerformanceView()
    {
        InitializeComponent();
        telemetryReader = WindowsTelemetryReader.Shared;
        telemetryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        telemetryTimer.Tick += OnTelemetryTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        isAttached = true;
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
            var snapshot = await Task.Run(telemetryReader.Read);
            if (isAttached && DataContext is PerformanceViewModel viewModel)
            {
                viewModel.UpdateTelemetry(
                    snapshot.CpuUsage,
                    snapshot.GpuUsage,
                    snapshot.UsedMemoryGb,
                    snapshot.TotalMemoryGb);
            }
        }
        finally
        {
            telemetryReadInProgress = false;
        }
    }
}
