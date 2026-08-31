using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Launcher.Core.ViewModels;

namespace Launcher.UI.Views;

public partial class ProfilesView : UserControl
{
    public ProfilesView()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
        ApplyResponsiveLayout(Bounds.Width);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs eventArgs)
    {
        ApplyResponsiveLayout(eventArgs.NewSize.Width);
    }

    private void ApplyResponsiveLayout(double width)
    {
        if (width <= 0)
        {
            return;
        }

        // A 360px instance card plus the details panel cannot fit safely in the
        // narrow content area. Stack the panels before Avalonia has to clip them.
        var useStackedLayout = width < 1040;
        ProfilesWorkspaceGrid.ColumnDefinitions = useStackedLayout
            ? new ColumnDefinitions("*")
            : new ColumnDefinitions("*,340");
        ProfilesWorkspaceGrid.RowDefinitions = useStackedLayout
            ? new RowDefinitions("Auto,Auto")
            : new RowDefinitions("Auto");
        ProfilesWorkspaceGrid.ColumnSpacing = useStackedLayout ? 0 : 18;
        Grid.SetColumn(DetailsPanel, useStackedLayout ? 0 : 1);
        Grid.SetRow(DetailsPanel, useStackedLayout ? 1 : 0);
        DetailsPanel.Margin = useStackedLayout
            ? new Avalonia.Thickness(0, 18, 0, 0)
            : new Avalonia.Thickness(0);
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
