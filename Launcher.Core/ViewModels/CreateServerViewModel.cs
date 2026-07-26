using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Launcher.Core.ViewModels;

public sealed class LocalServerItem
{
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
}

public partial class CreateServerViewModel : ViewModelBase
{
    [ObservableProperty] private string _serverName = "UB Community Server";
    [ObservableProperty] private string _minecraftVersion = "1.20.4";
    [ObservableProperty] private string _serverType = "Fabric";
    [ObservableProperty] private int _allocatedRamGb = 4;
    [ObservableProperty] private int _serverPort = 25565;
    [ObservableProperty] private bool _onlineMode = true;
    [ObservableProperty] private string _statusText = "Configure a server and create its local files.";

    public ObservableCollection<string> ServerTypes { get; } = ["Vanilla", "Fabric", "Forge", "NeoForge"];
    public ObservableCollection<LocalServerItem> Servers { get; } =
    [
        new() { Name = "United Bangla SMP", Version = "1.20.4 Fabric", Status = "Stopped" },
        new() { Name = "UB Practice", Version = "1.8.9 Vanilla", Status = "Stopped" }
    ];

    [RelayCommand]
    private void CreateServer()
    {
        if (string.IsNullOrWhiteSpace(ServerName))
        {
            StatusText = "Enter a server name first.";
            return;
        }

        Servers.Add(new LocalServerItem
        {
            Name = ServerName.Trim(),
            Version = $"{MinecraftVersion} {ServerType}",
            Status = "Ready"
        });
        StatusText = $"{ServerName} is ready on port {ServerPort}.";
    }
}
