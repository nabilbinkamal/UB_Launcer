using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Launcher.Core.ViewModels;
using Launcher.UI.Controls;
using System.Threading.Tasks;

namespace Launcher.UI.Views;

public partial class CustomizeView : UserControl
{
    public CustomizeView()
    {
        InitializeComponent();
    }

    private async void OnBrowseSkinClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is CustomizeViewModel viewModel)
        {
            var selectedPath = await PickPngAsync("Choose Minecraft skin");
            if (selectedPath is null) return;

            viewModel.ImportSkin(selectedPath, SkinPreviewControl.DetectSlimModel(selectedPath));
        }
    }

    private async void OnBrowseCapeClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is CustomizeViewModel viewModel)
        {
            var selectedPath = await PickPngAsync("Choose Minecraft cape");
            if (selectedPath is null) return;

            viewModel.ImportCape(selectedPath);
        }
    }

    private async Task<string?> PickPngAsync(string title)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("PNG image") { Patterns = ["*.png"] }
            ]
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }
}
