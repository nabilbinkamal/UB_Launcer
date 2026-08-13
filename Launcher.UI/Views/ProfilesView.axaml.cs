using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Launcher.Core.ViewModels;

namespace Launcher.UI.Views;

public partial class ProfilesView : UserControl
{
    public ProfilesView()
    {
        InitializeComponent();
    }

    private async void OnImportInstanceClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storageProvider ||
            DataContext is not ProfilesViewModel viewModel)
        {
            return;
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Minecraft instance",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Minecraft instance packs")
                {
                    Patterns = ["*.ubpack", "*.mrpack", "*.zip"]
                },
                new FilePickerFileType("UB Launcher pack") { Patterns = ["*.ubpack"] },
                new FilePickerFileType("Modrinth pack") { Patterns = ["*.mrpack"] }
            ]
        });

        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } archivePath)
        {
            return;
        }

        await viewModel.ImportInstanceArchiveAsync(archivePath);
    }

    private async void OnExportInstanceClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storageProvider ||
            DataContext is not ProfilesViewModel { SelectedProfile: not null } viewModel)
        {
            return;
        }

        var packType = new FilePickerFileType("UB Launcher pack")
        {
            Patterns = ["*.ubpack"]
        };
        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export portable Minecraft instance",
            SuggestedFileName = SanitizeFileName(viewModel.SelectedProfile.Name) + ".ubpack",
            DefaultExtension = "ubpack",
            FileTypeChoices = [packType],
            SuggestedFileType = packType,
            ShowOverwritePrompt = true
        });

        if (file?.TryGetLocalPath() is not { } destinationPath)
        {
            return;
        }

        await viewModel.ExportSelectedInstanceAsync(destinationPath);
    }

    private async void OnChangeInstanceIconClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storageProvider ||
            DataContext is not ProfilesViewModel { SelectedProfile: not null } viewModel)
        {
            return;
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Choose a picture for {viewModel.SelectedProfile.Name}",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Profile pictures")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"]
                }
            ]
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } imagePath)
        {
            viewModel.SetSelectedProfileIcon(imagePath);
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var safeName = string.Concat(value.Trim().Select(character =>
            Array.IndexOf(invalidCharacters, character) >= 0 ? '-' : character));
        return string.IsNullOrWhiteSpace(safeName) ? "UB-Launcher-Pack" : safeName;
    }
}
