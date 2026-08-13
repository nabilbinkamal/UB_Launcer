using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Launcher.Core.ViewModels;

namespace Launcher.UI.Views;

public partial class CreateServerView : UserControl
{
    public CreateServerView()
    {
        InitializeComponent();
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
