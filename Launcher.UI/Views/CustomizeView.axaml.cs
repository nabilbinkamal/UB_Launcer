using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Launcher.Core.ViewModels;
using Launcher.UI.Controls;
using System.Threading.Tasks;

namespace Launcher.UI.Views;

public partial class CustomizeView : UserControl, INavigationAwarePage
{
    public CustomizeView()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
        AttachedToVisualTree += (_, _) => ApplyPreviewScale(Bounds.Width);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs eventArgs)
    {
        ApplyPreviewScale(eventArgs.NewSize.Width);
    }

    private void ApplyPreviewScale(double width)
    {
        if (width <= 0) return;

        var scale = Math.Clamp(width / 1120d, 1d, 1.35d);
        PreviewPanel.Width = 285 * scale;
    }

    public void SetNavigationActive(bool isActive)
    {
        PlayerPreview.SetNavigationActive(isActive);
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
