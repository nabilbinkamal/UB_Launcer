using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Launcher.Core.ViewModels;

namespace Launcher.UI.Views;

public partial class CreateServerView : UserControl
{
    public CreateServerView()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
        ApplyResponsiveLayout(Bounds.Width);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs eventArgs) =>
        ApplyResponsiveLayout(eventArgs.NewSize.Width);

    private void ApplyResponsiveLayout(double width)
    {
        if (width <= 0)
        {
            return;
        }

        // The original fixed columns overflowed when the launcher content area
        // was narrower than the list plus the manager panel.
        var stackServerPanels = width < 1040;
        ServerWorkspaceGrid.ColumnDefinitions = stackServerPanels
            ? new ColumnDefinitions("*")
            : new ColumnDefinitions("360,*");
        ServerWorkspaceGrid.RowDefinitions = stackServerPanels
            ? new RowDefinitions("Auto,Auto")
            : new RowDefinitions("Auto");
        ServerWorkspaceGrid.ColumnSpacing = stackServerPanels ? 0 : 18;
        Grid.SetColumn(ServerManagerPanel, stackServerPanels ? 0 : 1);
        Grid.SetRow(ServerManagerPanel, stackServerPanels ? 1 : 0);
        ServerManagerPanel.Margin = stackServerPanels
            ? new Avalonia.Thickness(0, 18, 0, 0)
            : new Avalonia.Thickness(0);

        var stackConsoleControls = width < 760;
        ConsoleWorkspaceGrid.ColumnDefinitions = stackConsoleControls
            ? new ColumnDefinitions("*")
            : new ColumnDefinitions("*,260");
        ConsoleWorkspaceGrid.RowDefinitions = stackConsoleControls
            ? new RowDefinitions("Auto,Auto")
            : new RowDefinitions("Auto");
        ConsoleWorkspaceGrid.ColumnSpacing = stackConsoleControls ? 0 : 16;
        Grid.SetColumn(ConsoleControlsPanel, stackConsoleControls ? 0 : 1);
        Grid.SetRow(ConsoleControlsPanel, stackConsoleControls ? 1 : 0);
        ConsoleControlsPanel.Margin = stackConsoleControls
            ? new Avalonia.Thickness(0, 16, 0, 0)
            : new Avalonia.Thickness(0);
    }

    private void StartServer_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is CreateServerViewModel viewModel &&
            viewModel.ToggleSelectedServerCommand.CanExecute(null))
        {
            viewModel.ToggleSelectedServerCommand.Execute(null);
        }
    }

    private void StopServer_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is CreateServerViewModel viewModel &&
            viewModel.ToggleSelectedServerCommand.CanExecute(null))
        {
            viewModel.ToggleSelectedServerCommand.Execute(null);
        }
    }

    private void RestartServer_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is CreateServerViewModel viewModel &&
            viewModel.RestartSelectedServerCommand.CanExecute(null))
        {
            viewModel.RestartSelectedServerCommand.Execute(null);
        }
    }

    private void KillServer_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is CreateServerViewModel viewModel &&
            viewModel.KillSelectedServerCommand.CanExecute(null))
        {
            viewModel.KillSelectedServerCommand.Execute(null);
        }
    }

    private async void CopyLanAddress_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is not CreateServerViewModel viewModel ||
            viewModel.LanJoinAddress == "LAN address unavailable" ||
            TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        await clipboard.SetTextAsync(viewModel.LanJoinAddress);
        viewModel.StatusText = $"Copied {viewModel.LanJoinAddress}.";
    }

    private async void CopyTunnelAddress_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is not CreateServerViewModel viewModel ||
            viewModel.TunnelRemotePort == 0 ||
            TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        await clipboard.SetTextAsync(viewModel.TunnelJoinAddress);
        viewModel.StatusText = $"Copied {viewModel.TunnelJoinAddress}.";
    }

    private void ConsoleCommand_OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.Enter || DataContext is not CreateServerViewModel viewModel)
        {
            return;
        }

        if (viewModel.SendConsoleCommandCommand.CanExecute(null))
        {
            viewModel.SendConsoleCommandCommand.Execute(null);
            eventArgs.Handled = true;
        }
    }
}
