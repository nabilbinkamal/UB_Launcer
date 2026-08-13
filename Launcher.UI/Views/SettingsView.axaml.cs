using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Launcher.Core.ViewModels;

namespace Launcher.UI.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void OnCloseSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.DataContext is not MainWindowViewModel mainViewModel)
        {
            return;
        }

        if (mainViewModel.CloseSettingsCommand.CanExecute(null))
        {
            mainViewModel.CloseSettingsCommand.Execute(null);
        }
    }

    private async void OnBrowseAppDirectoryClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storageProvider ||
            DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select a new app directory",
            AllowMultiple = false
        });

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            viewModel.AppDirectory = path;
        }
    }

    private async void OnBrowseJavaClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string version } ||
            TopLevel.GetTopLevel(this)?.StorageProvider is not { } storageProvider ||
            DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Select Java {version} executable",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Java executable")
                {
                    Patterns = ["java.exe", "javaw.exe"]
                }
            ]
        });

        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
        {
            return;
        }

        path = path.EndsWith("java.exe", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(Path.GetDirectoryName(path)!, "javaw.exe")
            : path;

        switch (version)
        {
            case "25": viewModel.Java25Path = path; break;
            case "21": viewModel.Java21Path = path; break;
            case "17": viewModel.Java17Path = path; break;
            case "8": viewModel.Java8Path = path; break;
        }
    }

    private void OnOpenBackupsFolderClick(object? sender, RoutedEventArgs e)
    {
        var backupsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "UBLauncher",
            "backups");
        Directory.CreateDirectory(backupsDirectory);
        Process.Start(new ProcessStartInfo(backupsDirectory) { UseShellExecute = true });
    }
}
