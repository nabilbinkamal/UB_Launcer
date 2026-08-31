using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Services;
using Minecraft.ModManager;

namespace Launcher.Core.ViewModels;

public partial class LocalServerItem : ObservableObject
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _version = "1.20.4";
    [ObservableProperty] private string _serverType = "Paper";
    [ObservableProperty] private int _port = 25565;
    [ObservableProperty] private int _memoryGb = 4;
    [ObservableProperty] private string _folderPath = string.Empty;
    [ObservableProperty] private string _launchJarName = "server.jar";
    [ObservableProperty] private bool _onlineMode;
    [ObservableProperty] private bool _eulaAccepted;
    [ObservableProperty] private int _maxPlayers = 20;
    [ObservableProperty] private int _viewDistance = 10;
    [ObservableProperty] private string _motd = "A UB Launcher Minecraft Server";
    [ObservableProperty] private string _status = "Setup required";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _logText = "Server console is idle.";
    [ObservableProperty] private DateTimeOffset? _lastActivityAtUtc;

    public string Initials => string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2)
        .Select(part => char.ToUpperInvariant(part[0])));
    public string Endpoint => $"localhost:{Port}";
    public string MemoryLabel => $"{MemoryGb} GB RAM";
    public string LaunchJarPath => Path.Combine(FolderPath, LaunchJarName);
    public bool HasServerJar => File.Exists(LaunchJarPath);
    public bool IsSetupRequired => !HasServerJar;
    public string ActionLabel => IsRunning ? "Stop server" : HasServerJar ? "Start server" : "Download & start";
    public string RecentActivityLabel => LastActivityAtUtc is null
        ? "Not opened yet"
        : FormatRelativeTime(DateTimeOffset.UtcNow - LastActivityAtUtc.Value);

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(Initials));

    partial void OnPortChanged(int value) => OnPropertyChanged(nameof(Endpoint));

    partial void OnMemoryGbChanged(int value) => OnPropertyChanged(nameof(MemoryLabel));

    partial void OnFolderPathChanged(string value) => NotifySetupChanged();

    partial void OnLaunchJarNameChanged(string value) => NotifySetupChanged();

    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(ActionLabel));

    partial void OnLastActivityAtUtcChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(RecentActivityLabel));

    public void RefreshRelativeTime() => OnPropertyChanged(nameof(RecentActivityLabel));

    public void RefreshSetupState()
    {
        NotifySetupChanged();
        if (!IsRunning)
        {
            Status = HasServerJar ? "Stopped" : "Setup required";
        }
    }

    private void NotifySetupChanged()
    {
        OnPropertyChanged(nameof(LaunchJarPath));
        OnPropertyChanged(nameof(HasServerJar));
        OnPropertyChanged(nameof(IsSetupRequired));
        OnPropertyChanged(nameof(ActionLabel));
    }

    private static string FormatRelativeTime(TimeSpan elapsed) => elapsed.TotalMinutes switch
    {
        < 1 => "Just now",
        < 60 => $"{Math.Max(1, (int)elapsed.TotalMinutes)}m ago",
        < 1_440 => $"{Math.Max(1, (int)elapsed.TotalHours)}h ago",
        < 10_080 => $"{Math.Max(1, (int)elapsed.TotalDays)}d ago",
        _ => LastActivityDate(elapsed)
    };

    private static string LastActivityDate(TimeSpan elapsed) =>
        DateTimeOffset.UtcNow.Subtract(elapsed).ToLocalTime().ToString("MMM d");
}

public partial class ServerPluginSearchItem : ObservableObject
{
    public required CatalogProject Project { get; init; }
    public string Name => Project.Name;
    public string Author => $"by {Project.Author}";
    public string Description => Project.Description;
    public string DownloadsLabel => Project.Downloads switch
    {
        >= 1_000_000 => $"{Project.Downloads / 1_000_000d:0.#}M downloads",
        >= 1_000 => $"{Project.Downloads / 1_000d:0.#}K downloads",
        _ => $"{Project.Downloads:N0} downloads"
    };

    [ObservableProperty] private bool _isInstalling;
    [ObservableProperty] private bool _isInstalled;
    public string ActionLabel => IsInstalling ? "Installing..." : IsInstalled ? "Installed" : "Install";
    public bool CanInstall => !IsInstalling && !IsInstalled;

    partial void OnIsInstallingChanged(bool value)
    {
        OnPropertyChanged(nameof(ActionLabel));
        OnPropertyChanged(nameof(CanInstall));
    }

    partial void OnIsInstalledChanged(bool value)
    {
        OnPropertyChanged(nameof(ActionLabel));
        OnPropertyChanged(nameof(CanInstall));
    }
}

public sealed class InstalledServerPlugin
{
    public required string Name { get; init; }
    public required string FileName { get; init; }
    public required string FilePath { get; init; }
}

public partial class CreateServerViewModel : ViewModelBase
{
    private readonly Dictionary<string, Process> runningProcesses = [];
    private readonly SynchronizationContext? uiContext = SynchronizationContext.Current;
    private readonly UnifiedContentCatalogService pluginCatalogService = new();
    private UbRelayClient? tunnelClient;
    private string? tunnelServerId;

    [ObservableProperty] private LocalServerItem? _selectedServer;
    [ObservableProperty] private bool _isCreatePanelVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCreateStep1))]
    [NotifyPropertyChangedFor(nameof(IsCreateStep2))]
    [NotifyPropertyChangedFor(nameof(IsCreateStep3))]
    [NotifyPropertyChangedFor(nameof(IsCreateStep4))]
    private int _createStep = 1;

    public bool IsCreateStep1 => CreateStep == 1;
    public bool IsCreateStep2 => CreateStep == 2;
    public bool IsCreateStep3 => CreateStep == 3;
    public bool IsCreateStep4 => CreateStep == 4;

    [ObservableProperty] private string _serverName = "UB Community Server";
    [ObservableProperty] private string _minecraftVersion = "1.20.4";
    [ObservableProperty] private string _serverType = "Paper";
    [ObservableProperty] private int _allocatedRamGb;

    public int MaxSystemRamGb { get; } = GetTotalSystemMemoryGb();



    private static int GetTotalSystemMemoryGb()
    {
        try
        {
            var bytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            var gb = (int)Math.Round((double)bytes / 1024 / 1024 / 1024);
            return Math.Clamp(gb, 2, 128); // Sanity limits
        }
        catch
        {
            return 8; // Safe fallback
        }
    }
    [ObservableProperty] private int _serverPort = 25565;
    [ObservableProperty] private bool _onlineMode;
    [ObservableProperty] private bool _newServerEulaAccepted;
    [ObservableProperty] private string _editServerName = string.Empty;
    [ObservableProperty] private string _editMinecraftVersion = string.Empty;
    [ObservableProperty] private string _editServerType = string.Empty;
    [ObservableProperty] private int _editMemoryGb = 4;
    [ObservableProperty] private int _editPort = 25565;
    [ObservableProperty] private bool _editOnlineMode;
    [ObservableProperty] private bool _editEulaAccepted;
    [ObservableProperty] private int _editMaxPlayers = 20;
    [ObservableProperty] private int _editViewDistance = 10;
    [ObservableProperty] private string _editMotd = string.Empty;
    [ObservableProperty] private string _editLaunchJarName = "server.jar";
    [ObservableProperty] private string _consoleCommand = string.Empty;
    [ObservableProperty] private string _localIpAddress = "Not detected";
    [ObservableProperty] private string _statusText = "Select a server to manage it or create a new workspace.";
    [ObservableProperty] private int _tunnelRemotePort;
    [ObservableProperty] private string _tunnelStatus = "Relay stopped";
    [ObservableProperty] private bool _isTunnelRunning;
    [ObservableProperty] private bool _isDeleteConfirmationVisible;
    [ObservableProperty] private string _pluginSearchQuery = string.Empty;
    [ObservableProperty] private bool _isPluginBusy;
    [ObservableProperty] private string _pluginStatusText = "Search Modrinth for compatible server addons.";
    [ObservableProperty] private int _currentTab = 0;

    public ObservableCollection<string> ServerTypes { get; } =
    [
        "Paper",
        "Purpur",
        "Fabric",
        "Forge",
        "NeoForge",
        "Quilt",
        "Folia",
        "Vanilla"
    ];
    public ObservableCollection<LocalServerItem> Servers => ServerStore.Servers;
    public ObservableCollection<ServerPluginSearchItem> PluginResults { get; } = [];
    public ObservableCollection<InstalledServerPlugin> InstalledPlugins { get; } = [];
    public int TotalMemoryGb => Servers.Sum(server => server.MemoryGb);
    public int RunningServerCount => Servers.Count(server => server.IsRunning);
    public string ServerStatusSummary => RunningServerCount == 0
        ? "All servers stopped"
        : RunningServerCount == 1 ? "1 server online" : $"{RunningServerCount} servers online";
    public string LanJoinAddress => SelectedServer is null || LocalIpAddress == "Not detected"
        ? "LAN address unavailable"
        : $"{LocalIpAddress}:{SelectedServer.Port}";
    public string InternetJoinHint => SelectedServer is null
        ? "Select a server to see its network instructions."
        : LocalIpAddress == "Not detected"
            ? "Connect to your network and refresh the LAN address before configuring router port forwarding."
            : $"Forward TCP port {SelectedServer.Port} to {LocalIpAddress}:{SelectedServer.Port}. Then friends join using YOUR_PUBLIC_IP:{SelectedServer.Port}.";
    public string TunnelJoinAddress => TunnelRemotePort == 0
        ? "Assigned when relay starts"
        : $"213.35.106.78:{TunnelRemotePort}";
    public string TunnelActionLabel => IsTunnelRunning ? "Stop relay" : "Start relay";
    public bool IsManagerVisible => !IsCreatePanelVisible && SelectedServer is not null;
    public bool IsPluginSupported => SelectedServer is not null &&
        (EditServerType.Equals("Paper", StringComparison.OrdinalIgnoreCase) ||
         EditServerType.Equals("Purpur", StringComparison.OrdinalIgnoreCase) ||
         EditServerType.Equals("Folia", StringComparison.OrdinalIgnoreCase));
    public bool IsModSupported => SelectedServer is not null &&
        (EditServerType.Equals("Fabric", StringComparison.OrdinalIgnoreCase) ||
         EditServerType.Equals("Forge", StringComparison.OrdinalIgnoreCase) ||
         EditServerType.Equals("NeoForge", StringComparison.OrdinalIgnoreCase) ||
         EditServerType.Equals("Quilt", StringComparison.OrdinalIgnoreCase));
    public bool IsExtensionSupported => IsPluginSupported || IsModSupported;
    public string ExtensionTypeTitle => IsModSupported ? "Mods" : "Plugins";
    public string ExtensionHeaderTitle => IsModSupported ? "Mod manager" : "Plugin manager";
    public string ExtensionSearchPlaceholder => IsModSupported
        ? $"Search {EditServerType} mods, e.g. JEI, Waystones, Sodium..."
        : "Search plugins, e.g. Essentials, WorldEdit...";
    public string ExtensionFolderButtonText => IsModSupported ? "Open mods folder" : "Open plugins folder";
    public string PluginSupportText => ExtensionSupportText;
    public string ExtensionSupportText => IsModSupported
        ? $"Install compatible {EditServerType} mods with one click. Restart the server after changing mods."
        : IsPluginSupported
            ? "Install compatible plugins with one click. Restart the server after changing plugins."
            : "Vanilla cannot load mods or plugins. Choose Fabric/Forge/NeoForge for mods, or Paper/Purpur for plugins.";
    public string AccountModeText => EditOnlineMode
        ? "Premium accounts only"
        : "Offline / cracked accounts allowed";

    public CreateServerViewModel()
    {
        _allocatedRamGb = Math.Min(4, MaxSystemRamGb);
        LocalIpAddress = FindLocalIpv4Address();
        foreach (var server in Servers)
        {
            server.RefreshSetupState();
        }

        ServerStore.SelectionChanged += OnStoredServerSelectionChanged;
        SelectedServer = ServerStore.SelectedServer ?? Servers.FirstOrDefault();
    }

    partial void OnSelectedServerChanged(LocalServerItem? value)
    {
        IsDeleteConfirmationVisible = false;
        foreach (var server in Servers)
        {
            server.IsSelected = ReferenceEquals(server, value);
        }

        if (value is not null)
        {
            ServerStore.SelectedServer = value;
            IsCreatePanelVisible = false;
            LoadEditor(value);
            PluginResults.Clear();
            PluginStatusText = IsModSupported
                ? $"Search Modrinth for compatible {EditServerType} mods."
                : IsPluginSupported
                    ? "Search Modrinth for compatible plugins."
                    : "Addons are not supported on Vanilla.";
            RefreshInstalledPlugins();
            StatusText = $"Managing {value.Name}.";
        }

        ConsoleCommand = string.Empty;
        OnPropertyChanged(nameof(LanJoinAddress));
        OnPropertyChanged(nameof(InternetJoinHint));
        OnPropertyChanged(nameof(IsManagerVisible));
        OnPropertyChanged(nameof(IsPluginSupported));
        OnPropertyChanged(nameof(IsModSupported));
        OnPropertyChanged(nameof(IsExtensionSupported));
        OnPropertyChanged(nameof(ExtensionTypeTitle));
        OnPropertyChanged(nameof(ExtensionHeaderTitle));
        OnPropertyChanged(nameof(ExtensionSearchPlaceholder));
        OnPropertyChanged(nameof(ExtensionFolderButtonText));
        OnPropertyChanged(nameof(ExtensionSupportText));
        OnPropertyChanged(nameof(PluginSupportText));
    }

    partial void OnIsCreatePanelVisibleChanged(bool value) => OnPropertyChanged(nameof(IsManagerVisible));

    partial void OnLocalIpAddressChanged(string value)
    {
        OnPropertyChanged(nameof(LanJoinAddress));
        OnPropertyChanged(nameof(InternetJoinHint));
    }

    partial void OnTunnelRemotePortChanged(int value) => OnPropertyChanged(nameof(TunnelJoinAddress));

    partial void OnIsTunnelRunningChanged(bool value) => OnPropertyChanged(nameof(TunnelActionLabel));

    partial void OnEditServerTypeChanged(string value)
    {
        PluginResults.Clear();
        PluginStatusText = IsModSupported
            ? $"Search Modrinth for compatible {EditServerType} mods."
            : IsPluginSupported
                ? "Search Modrinth for compatible plugins."
                : "Addons are not supported on Vanilla.";
        RefreshInstalledPlugins();
        OnPropertyChanged(nameof(IsPluginSupported));
        OnPropertyChanged(nameof(IsModSupported));
        OnPropertyChanged(nameof(IsExtensionSupported));
        OnPropertyChanged(nameof(ExtensionTypeTitle));
        OnPropertyChanged(nameof(ExtensionHeaderTitle));
        OnPropertyChanged(nameof(ExtensionSearchPlaceholder));
        OnPropertyChanged(nameof(ExtensionFolderButtonText));
        OnPropertyChanged(nameof(ExtensionSupportText));
        OnPropertyChanged(nameof(PluginSupportText));
    }

    partial void OnEditMinecraftVersionChanged(string value)
    {
        PluginResults.Clear();
        PluginStatusText = "Search again to see plugins compatible with this Minecraft version.";
    }

    partial void OnEditOnlineModeChanged(bool value) => OnPropertyChanged(nameof(AccountModeText));

    [RelayCommand]
    private void NextCreateStep()
    {
        if (CreateStep < 4) CreateStep++;
    }

    [RelayCommand]
    private void PreviousCreateStep()
    {
        if (CreateStep > 1) CreateStep--;
    }

    [RelayCommand]
    private void SelectServer(LocalServerItem server)
    {
        ServerStore.Touch(server);
        if (ReferenceEquals(SelectedServer, server))
        {
            IsCreatePanelVisible = false;
            LoadEditor(server);
            RefreshInstalledPlugins();
            StatusText = $"Managing {server.Name}.";
            return;
        }

        SelectedServer = server;
    }

    [RelayCommand]
    private void ShowCreatePanel()
    {
        ServerName = "New Minecraft Server";
        MinecraftVersion = SelectedServer?.Version ?? "1.20.4";
        ServerType = "Paper";
        AllocatedRamGb = 4;
        ServerPort = FindAvailablePort();
        OnlineMode = false;
        NewServerEulaAccepted = false;
        CreateStep = 1;
        IsCreatePanelVisible = true;
        StatusText = "Configure the new server workspace.";
    }

    [RelayCommand]
    private void CancelCreate()
    {
        IsCreatePanelVisible = false;
        StatusText = SelectedServer is null ? "Select a server to manage it." : $"Managing {SelectedServer.Name}.";
    }

    [RelayCommand]
    private async Task CreateServerAsync()
    {
        try
        {
            var requestedName = ServerName?.Trim();
            if (string.IsNullOrWhiteSpace(requestedName))
            {
                StatusText = "Enter a server name first.";
                return;
            }

            if (Servers.Any(server => server.Port == ServerPort))
            {
                StatusText = $"Port {ServerPort} is already used by another saved server.";
                return;
            }

            if (string.IsNullOrWhiteSpace(MinecraftVersion))
            {
                StatusText = "Choose a Minecraft version first.";
                return;
            }

            if (!NewServerEulaAccepted)
            {
                StatusText = "Accept the Minecraft EULA before creating and starting the server.";
                return;
            }

            var folderPath = ServerStore.GetUniqueServerFolder(requestedName);
            Directory.CreateDirectory(folderPath);
            var server = new LocalServerItem
            {
                Name = requestedName,
                Version = MinecraftVersion.Trim(),
                ServerType = ServerType ?? "Paper",
                Port = ServerPort,
                MemoryGb = AllocatedRamGb,
                FolderPath = folderPath,
                OnlineMode = OnlineMode,
                EulaAccepted = true,
                Status = "Setup required",
                LastActivityAtUtc = DateTimeOffset.UtcNow
            };

            await Task.Run(() => 
            {
                WriteConfigurationFiles(server);
                SaveServers();
            });
            server.RefreshSetupState();
            Servers.Add(server);
            NotifyOverviewChanged();
            SelectedServer = server;
            await ToggleSelectedServerAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Could not create server: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveSelectedServerAsync()
    {
        var server = SelectedServer;
        if (server is null)
        {
            return;
        }

        if (server.IsRunning)
        {
            StatusText = "Stop the server before changing its launch settings.";
            return;
        }

        var result = await TryApplyEditorSettingsAsync(server);
        if (result.Success)
        {
            StatusText = result.SoftwareChanged
                ? "Settings saved. The correct server software will download automatically when you press Start."
                : $"Saved settings for {server.Name}.";
        }
    }

    [RelayCommand]
    private async Task ToggleSelectedServerAsync()
    {
        var server = SelectedServer;
        if (server is null)
        {
            return;
        }

        if (server.IsRunning)
        {
            await StopServerAsync(server);
            return;
        }

        var applyResult = await TryApplyEditorSettingsAsync(server);
        if (!applyResult.Success)
        {
            return;
        }

        var softwareChanged = applyResult.SoftwareChanged;

        server.RefreshSetupState();
        if (softwareChanged || !server.HasServerJar)
        {
            await Task.Run(() => WriteConfigurationFiles(server));
            await DownloadServerSoftwareAsync(server);
            server.RefreshSetupState();
            if (!server.HasServerJar)
            {
                return;
            }
        }

        if (!server.EulaAccepted)
        {
            StatusText = "Accept the Minecraft EULA in Server settings before starting.";
            AppendLog(server, "Start blocked until the Minecraft EULA is accepted.");
            return;
        }

        await Task.Run(() => WriteConfigurationFiles(server));
        try
        {
            var startInfo = BuildServerStartInfo(server);
            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, eventArgs) => PostLog(server, eventArgs.Data);
            process.ErrorDataReceived += (_, eventArgs) => PostLog(server, eventArgs.Data);
            process.Exited += (_, _) => RunOnUi(() => HandleServerExit(server, process));

            if (!process.Start())
            {
                throw new InvalidOperationException("Java did not start the server process.");
            }

            runningProcesses[server.Id] = process;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            server.IsRunning = true;
            server.Status = "Running";
            server.LastActivityAtUtc = DateTimeOffset.UtcNow;
            await Task.Run(() => SaveServers());
            AppendLog(server, $"Starting {server.Name} with {server.MemoryGb} GB RAM...");
            NotifyOverviewChanged();
            StatusText = $"{server.Name} is starting on {server.Endpoint}.";
            await StartTunnelAsync(server, automatic: true);
        }
        catch (Exception exception)
        {
            server.IsRunning = false;
            server.Status = "Start failed";
            AppendLog(server, $"Start failed: {exception.Message}");
            NotifyOverviewChanged();
            StatusText = $"Could not start the server: {exception.Message}";
        }
    }

    private static ProcessStartInfo BuildServerStartInfo(LocalServerItem server)
    {
        var serverFolder = server.FolderPath;
        var userJvmArgs = Path.Combine(serverFolder, "user_jvm_args.txt");
        string? argsFile = null;

        var librariesDir = Path.Combine(serverFolder, "libraries");
        if (Directory.Exists(librariesDir))
        {
            var winArgs = Directory.GetFiles(librariesDir, "win_args.txt", SearchOption.AllDirectories).FirstOrDefault();
            var unixArgs = Directory.GetFiles(librariesDir, "unix_args.txt", SearchOption.AllDirectories).FirstOrDefault();
            var generalArgs = Directory.GetFiles(librariesDir, "args.txt", SearchOption.AllDirectories).FirstOrDefault();
            argsFile = winArgs ?? unixArgs ?? generalArgs;
        }

        string arguments;
        if (argsFile != null && (server.ServerType.Equals("Forge", StringComparison.OrdinalIgnoreCase) || server.ServerType.Equals("NeoForge", StringComparison.OrdinalIgnoreCase)))
        {
            var relativeArgsPath = Path.GetRelativePath(serverFolder, argsFile);
            var userArgsPrefix = File.Exists(userJvmArgs) ? "@user_jvm_args.txt " : string.Empty;
            arguments = $"-Xms1G -Xmx{server.MemoryGb}G {userArgsPrefix}@{relativeArgsPath} nogui";
        }
        else
        {
            arguments = $"-Xms1G -Xmx{server.MemoryGb}G -jar \"{server.LaunchJarName}\" nogui";
        }

        return new ProcessStartInfo
        {
            FileName = "java",
            Arguments = arguments,
            WorkingDirectory = serverFolder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
    }

    [RelayCommand]
    private void RefreshSelectedServer()
    {
        if (SelectedServer is null)
        {
            return;
        }

        SelectedServer.RefreshSetupState();
        SelectedServer.LastActivityAtUtc = DateTimeOffset.UtcNow;
        SaveServers();
        StatusText = SelectedServer.HasServerJar
            ? $"{SelectedServer.LaunchJarName} is ready."
            : $"Still waiting for {SelectedServer.LaunchJarName}.";
    }

    [RelayCommand]
    private async Task DownloadSelectedServerSoftwareAsync()
    {
        if (SelectedServer is not null)
        {
            await DownloadServerSoftwareAsync(SelectedServer);
        }
    }

    [RelayCommand]
    private void OpenSelectedServerFolder()
    {
        if (SelectedServer is not null)
        {
            OpenServerFolder(SelectedServer);
        }
    }

    private async Task DownloadServerSoftwareAsync(LocalServerItem server)
    {
        if (server.IsRunning)
        {
            StatusText = "Stop the server before replacing its server software.";
            return;
        }

        server.Status = "Downloading";
        AppendLog(server, $"Downloading {server.ServerType} for Minecraft {server.Version}...");
        SaveServers();
        try
        {
            var progress = new Progress<string>(message => StatusText = message);
            await ServerSoftwareDownloadService.DownloadAsync(
                server.ServerType,
                server.Version,
                server.LaunchJarPath,
                progress);
            await ServerSoftwareDownloadService.DownloadDefaultPluginsAsync(
                server.ServerType,
                server.Version,
                server.FolderPath,
                progress);

            server.RefreshSetupState();
            server.Status = server.EulaAccepted ? "Stopped" : "EULA required";
            server.LastActivityAtUtc = DateTimeOffset.UtcNow;
            AppendLog(server, $"{server.ServerType} server software is ready at {server.LaunchJarName}.");
            StatusText = server.EulaAccepted
                ? $"{server.ServerType} for Minecraft {server.Version} is ready."
                : $"{server.ServerType} is ready. Accept the Minecraft EULA before starting.";
            RefreshInstalledPlugins();
        }
        catch (Exception exception)
        {
            server.RefreshSetupState();
            server.Status = server.HasServerJar ? "Stopped" : "Setup required";
            if (server.HasServerJar)
            {
                AppendLog(server, $"Server software is ready, but default plugin installation failed: {exception.Message}");
                StatusText = $"{server.ServerType} is ready, but ViaVersion plugin installation failed: {exception.Message}";
            }
            else
            {
                AppendLog(server, $"Server software download failed: {exception.Message}");
                StatusText = $"Could not download {server.ServerType}: {exception.Message}";
            }
        }

        SaveServers();
        NotifyOverviewChanged();
    }

    private string GetActiveExtensionFolder(LocalServerItem server) =>
        Path.Combine(server.FolderPath, IsModSupported ? "mods" : "plugins");

    [RelayCommand]
    private async Task SearchPluginsAsync()
    {
        var server = SelectedServer;
        if (server is null)
        {
            return;
        }

        if (!IsExtensionSupported)
        {
            PluginStatusText = "Vanilla cannot load mods or plugins. Choose Fabric, Forge, NeoForge, Quilt, Paper, or Purpur.";
            return;
        }

        IsPluginBusy = true;
        var addonType = IsModSupported ? "mods" : "plugins";
        PluginStatusText = $"Finding compatible {EditServerType} {addonType}...";
        try
        {
            var contentType = IsModSupported ? CatalogContentType.Mods : CatalogContentType.Plugins;
            var loader = IsModSupported ? EditServerType : "Paper";
            var result = await pluginCatalogService.SearchAsync(new CatalogSearchRequest(
                PluginSearchQuery?.Trim() ?? string.Empty,
                contentType,
                EditMinecraftVersion?.Trim() ?? string.Empty,
                loader,
                12));

            PluginResults.Clear();
            foreach (var project in result.Projects)
            {
                PluginResults.Add(new ServerPluginSearchItem
                {
                    Project = project,
                    IsInstalled = IsLikelyInstalled(project.Name)
                });
            }

            PluginStatusText = PluginResults.Count == 0
                ? $"No compatible {addonType} found. Try a different search."
                : $"{PluginResults.Count} compatible {addonType} found on Modrinth.";
            if (result.Notices.Count > 0)
            {
                PluginStatusText += $" {string.Join(" ", result.Notices)}";
            }
        }
        catch (Exception exception)
        {
            PluginStatusText = $"Could not load {addonType}: {exception.Message}";
        }
        finally
        {
            IsPluginBusy = false;
        }
    }

    [RelayCommand]
    private async Task InstallPluginAsync(ServerPluginSearchItem item)
    {
        var server = SelectedServer;
        if (server is null || item.IsInstalling || item.IsInstalled)
        {
            return;
        }

        if (server.IsRunning)
        {
            PluginStatusText = $"Stop the server before installing {ExtensionTypeTitle.ToLowerInvariant()}.";
            return;
        }

        if (!IsExtensionSupported)
        {
            PluginStatusText = "Mods/plugins are not supported on Vanilla.";
            return;
        }

        item.IsInstalling = true;
        PluginStatusText = $"Installing {item.Name}...";
        try
        {
            var result = await pluginCatalogService.InstallAsync(
                item.Project,
                new InstanceInstallTarget(server.Name, EditMinecraftVersion?.Trim() ?? string.Empty, EditServerType, server.FolderPath));
            item.IsInstalled = true;
            RefreshInstalledPlugins();
            PluginStatusText = result.Message.Replace("instance", "server", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception)
        {
            PluginStatusText = $"Could not install {item.Name}: {exception.Message}";
        }
        finally
        {
            item.IsInstalling = false;
        }
    }

    [RelayCommand]
    private void RemovePlugin(InstalledServerPlugin plugin)
    {
        var server = SelectedServer;
        if (server is null)
        {
            return;
        }

        if (server.IsRunning)
        {
            PluginStatusText = $"Stop the server before removing {ExtensionTypeTitle.ToLowerInvariant()}.";
            return;
        }

        try
        {
            var extensionFolder = GetActiveExtensionFolder(server);
            var extensionRoot = Path.GetFullPath(extensionFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var pluginPath = Path.GetFullPath(plugin.FilePath);
            if (!pluginPath.StartsWith(extensionRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The selected file is outside this server's addon folder.");
            }

            File.Delete(pluginPath);
            RefreshInstalledPlugins();
            PluginStatusText = $"Removed {plugin.Name}. Start the server to apply the change.";
        }
        catch (Exception exception)
        {
            PluginStatusText = $"Could not remove {plugin.Name}: {exception.Message}";
        }
    }

    [RelayCommand]
    private void OpenPluginsFolder()
    {
        var server = SelectedServer;
        if (server is null)
        {
            return;
        }

        try
        {
            var folder = GetActiveExtensionFolder(server);
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            PluginStatusText = $"Could not open folder: {exception.Message}";
        }
    }

    private void RefreshInstalledPlugins()
    {
        InstalledPlugins.Clear();
        var server = SelectedServer;
        if (server is null)
        {
            return;
        }

        var folder = GetActiveExtensionFolder(server);
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*.jar", SearchOption.TopDirectoryOnly)
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(file);
            InstalledPlugins.Add(new InstalledServerPlugin
            {
                Name = Path.GetFileNameWithoutExtension(fileName),
                FileName = fileName,
                FilePath = file
            });
        }
    }

    private bool IsLikelyInstalled(string pluginName)
    {
        var normalizedName = NormalizePluginName(pluginName);
        return InstalledPlugins.Any(plugin =>
            NormalizePluginName(plugin.Name).Contains(normalizedName, StringComparison.OrdinalIgnoreCase) ||
            normalizedName.Contains(NormalizePluginName(plugin.Name), StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizePluginName(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private async Task<(bool Success, bool SoftwareChanged)> TryApplyEditorSettingsAsync(LocalServerItem server)
    {
        if (string.IsNullOrWhiteSpace(EditServerName) || string.IsNullOrWhiteSpace(EditMinecraftVersion))
        {
            StatusText = "Server name and Minecraft version are required.";
            return (false, false);
        }

        if (Servers.Any(item => !ReferenceEquals(item, server) && item.Port == EditPort))
        {
            StatusText = $"Port {EditPort} is already used by another saved server.";
            return (false, false);
        }

        var launchJarName = EditLaunchJarName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(launchJarName) ||
            launchJarName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            !launchJarName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
        {
            StatusText = "Launch JAR must be a valid .jar filename, such as server.jar.";
            return (false, false);
        }

        var softwareChanged = !server.Version.Equals(EditMinecraftVersion.Trim(), StringComparison.OrdinalIgnoreCase) ||
                          !server.ServerType.Equals(EditServerType, StringComparison.OrdinalIgnoreCase);
        server.Name = EditServerName?.Trim() ?? string.Empty;
        server.Version = EditMinecraftVersion?.Trim() ?? string.Empty;
        server.ServerType = EditServerType;
        server.MemoryGb = Math.Clamp(EditMemoryGb, 1, 32);
        server.Port = Math.Clamp(EditPort, 1024, 65535);
        server.OnlineMode = EditOnlineMode;
        server.EulaAccepted = EditEulaAccepted;
        server.MaxPlayers = Math.Clamp(EditMaxPlayers, 1, 200);
        server.ViewDistance = Math.Clamp(EditViewDistance, 2, 32);
        server.Motd = string.IsNullOrWhiteSpace(EditMotd) ? "A UB Launcher Minecraft Server" : EditMotd.Trim();
        server.LaunchJarName = Path.GetFileName(launchJarName);
        server.LastActivityAtUtc = DateTimeOffset.UtcNow;
        await Task.Run(() => 
        {
            WriteConfigurationFiles(server);
            SaveServers();
        });
        server.RefreshSetupState();
        NotifyOverviewChanged();
        OnPropertyChanged(nameof(LanJoinAddress));
        OnPropertyChanged(nameof(InternetJoinHint));
        OnPropertyChanged(nameof(IsPluginSupported));
        OnPropertyChanged(nameof(PluginSupportText));
        OnPropertyChanged(nameof(PluginSupportText));
        return (true, softwareChanged);
    }

    [RelayCommand]
    private void RefreshNetworkAddress()
    {
        LocalIpAddress = FindLocalIpv4Address();
        StatusText = LocalIpAddress == "Not detected"
            ? "Could not detect a LAN IPv4 address. Check your network connection."
            : $"LAN join address refreshed: {LanJoinAddress}.";
    }

    [RelayCommand]
    private async Task ToggleTunnelAsync()
    {
        if (IsTunnelRunning)
        {
            await StopTunnelAsync();
            return;
        }

        var server = SelectedServer;
        if (server is null)
        {
            TunnelStatus = "Select a server first.";
            return;
        }

        if (!server.IsRunning)
        {
            TunnelStatus = "Start the Minecraft server before starting the relay.";
            return;
        }

        await StartTunnelAsync(server, automatic: false);
    }

    private async Task StartTunnelAsync(LocalServerItem server, bool automatic)
    {
        if (IsTunnelRunning && string.Equals(tunnelServerId, server.Id, StringComparison.Ordinal))
        {
            return;
        }

        if (IsTunnelRunning)
        {
            await StopTunnelAsync();
        }

        try
        {
            TunnelStatus = automatic
                ? "Server started. Getting a public address automatically..."
                : "Requesting a free public port...";
            var relay = new UbRelayClient();
            relay.Disconnected += message => RunOnUi(() => HandleTunnelDisconnected(relay, message));
            await relay.StartAsync(server.Port);
            tunnelClient = relay;
            tunnelServerId = server.Id;
            TunnelRemotePort = relay.PublicPort;
            IsTunnelRunning = true;
            TunnelStatus = $"Online — share {TunnelJoinAddress} with friends.";
            StatusText = $"UB Relay is online at {TunnelJoinAddress}.";
        }
        catch (Exception exception)
        {
            IsTunnelRunning = false;
            TunnelRemotePort = 0;
            TunnelStatus = automatic
                ? $"Server is running, but auto relay failed: {exception.Message}"
                : $"Could not start relay: {exception.Message}";
        }
    }

    private async Task StopTunnelAsync()
    {
        var relay = tunnelClient;
        tunnelClient = null;
        tunnelServerId = null;
        IsTunnelRunning = false;
        TunnelRemotePort = 0;
        if (relay is not null)
        {
            await relay.DisposeAsync();
        }

        TunnelStatus = "Relay stopped";
        StatusText = "UB Relay stopped.";
    }

    private void HandleTunnelDisconnected(UbRelayClient relay, string details)
    {
        if (!ReferenceEquals(tunnelClient, relay))
        {
            return;
        }

        tunnelClient = null;
        tunnelServerId = null;
        IsTunnelRunning = false;
        TunnelRemotePort = 0;
        TunnelStatus = details;
        StatusText = "UB Relay disconnected.";
    }

    [RelayCommand]
    private async Task SendConsoleCommandAsync()
    {
        var server = SelectedServer;
        var command = ConsoleCommand.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (server is null || string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        if (!server.IsRunning ||
            !runningProcesses.TryGetValue(server.Id, out var process) ||
            process.HasExited)
        {
            StatusText = "Start the server before sending console commands.";
            return;
        }

        try
        {
            await process.StandardInput.WriteLineAsync(command);
            await process.StandardInput.FlushAsync();
            AppendLog(server, $"> {command}");
            ConsoleCommand = string.Empty;
            StatusText = $"Sent '{command}' to {server.Name}.";
        }
        catch (Exception exception)
        {
            StatusText = $"Could not send the console command: {exception.Message}";
            AppendLog(server, $"Command failed: {exception.Message}");
        }
    }

    [RelayCommand]
    private void RequestDeleteSelectedServer()
    {
        var server = SelectedServer;
        if (server is null)
        {
            return;
        }

        IsDeleteConfirmationVisible = true;
        StatusText = $"Confirm permanent deletion of {server.Name}.";
    }

    [RelayCommand]
    private void CancelDeleteSelectedServer()
    {
        IsDeleteConfirmationVisible = false;
        StatusText = SelectedServer is null ? "Select a server." : $"Deletion canceled for {SelectedServer.Name}.";
    }

    [RelayCommand]
    private async Task ConfirmDeleteSelectedServerAsync()
    {
        var server = SelectedServer;
        if (server is null)
        {
            IsDeleteConfirmationVisible = false;
            return;
        }

        try
        {
            StatusText = $"Deleting {server.Name}...";
            await StopProcessesLockingServerAsync(server);
            DeleteServerFolderAndEntry(server);
        }
        catch (Exception exception)
        {
            StatusText = $"Could not delete the server: {exception.Message}";
        }
    }

    private static async Task StopProcessesLockingServerAsync(LocalServerItem server)
    {
        var lockingProcessIds = ServerFileLockService.FindLockingProcessIds(server.FolderPath)
            .Where(processId => processId != Environment.ProcessId)
            .ToArray();
        foreach (var processId in lockingProcessIds)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
            catch (ArgumentException)
            {
                // The process exited between Restart Manager's scan and the stop request.
            }
        }
    }

    private void DeleteServerFolderAndEntry(LocalServerItem server)
    {
        var serversRoot = Path.GetFullPath(ServerStore.ServersRoot)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var serverFolder = Path.GetFullPath(server.FolderPath)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!serverFolder.StartsWith(serversRoot, StringComparison.OrdinalIgnoreCase) ||
            serverFolder.Equals(serversRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("For safety, UB Launcher only deletes server folders inside its managed servers directory.");
        }

        if (Directory.Exists(server.FolderPath))
        {
            Directory.Delete(server.FolderPath, recursive: true);
        }

        var serverName = server.Name;
        var index = Servers.IndexOf(server);
        Servers.Remove(server);
        SelectedServer = Servers.Count == 0 ? null : Servers[Math.Clamp(index, 0, Servers.Count - 1)];
        IsCreatePanelVisible = Servers.Count == 0;
        IsDeleteConfirmationVisible = false;
        SaveServers();
        NotifyOverviewChanged();
        StatusText = $"Deleted {serverName} and its server files.";
    }

    [RelayCommand]
    private void RemoveSelectedServer()
    {
        var server = SelectedServer;
        if (server is null)
        {
            return;
        }

        if (server.IsRunning)
        {
            StatusText = "Stop the server before removing it from the launcher.";
            return;
        }

        var index = Servers.IndexOf(server);
        Servers.Remove(server);
        SelectedServer = Servers.Count == 0 ? null : Servers[Math.Clamp(index, 0, Servers.Count - 1)];
        IsCreatePanelVisible = Servers.Count == 0;
        SaveServers();
        NotifyOverviewChanged();
        StatusText = $"Removed {server.Name} from UB Launcher. Its files were kept.";
    }

    private async Task StopServerAsync(LocalServerItem server)
    {
        if (string.Equals(tunnelServerId, server.Id, StringComparison.Ordinal))
        {
            await StopTunnelAsync();
        }

        if (!runningProcesses.TryGetValue(server.Id, out var process) || process.HasExited)
        {
            server.IsRunning = false;
            server.Status = "Stopped";
            server.LastActivityAtUtc = DateTimeOffset.UtcNow;
            SaveServers();
            NotifyOverviewChanged();
            return;
        }

        server.Status = "Stopping";
        AppendLog(server, "Sending the stop command...");
        try
        {
            await process.StandardInput.WriteLineAsync("stop");
            await process.StandardInput.FlushAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            AppendLog(server, "The server did not stop in time and was closed safely.");
        }
        catch (Exception exception)
        {
            AppendLog(server, $"Error stopping gracefully: {exception.Message}");
        }
    }

    [RelayCommand]
    private async Task KillSelectedServerAsync()
    {
        var server = SelectedServer;
        if (server is null || !server.IsRunning) return;

        if (runningProcesses.TryGetValue(server.Id, out var process) && !process.HasExited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                AppendLog(server, "Server process was killed forcefully.");
                StatusText = $"Killed {server.Name}.";
            }
            catch (Exception ex)
            {
                AppendLog(server, $"Failed to kill process: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    private async Task RestartSelectedServerAsync()
    {
        var server = SelectedServer;
        if (server is null) return;

        if (server.IsRunning)
        {
            await StopServerAsync(server);
        }

        // Wait a brief moment to ensure ports are freed
        await Task.Delay(1000);
        await ToggleSelectedServerAsync();
    }

    private void HandleServerExit(LocalServerItem server, Process process)
    {
        if (string.Equals(tunnelServerId, server.Id, StringComparison.Ordinal))
        {
            _ = StopTunnelAsync();
        }

        runningProcesses.Remove(server.Id);
        server.IsRunning = false;
        server.Status = process.ExitCode == 0 ? "Stopped" : $"Exited ({process.ExitCode})";
        server.LastActivityAtUtc = DateTimeOffset.UtcNow;
        AppendLog(server, process.ExitCode == 0 ? "Server stopped." : $"Server process exited with code {process.ExitCode}.");
        process.Dispose();
        SaveServers();
        NotifyOverviewChanged();
        StatusText = $"{server.Name} is no longer running.";
    }

    private void LoadEditor(LocalServerItem server)
    {
        EditServerName = server.Name;
        EditMinecraftVersion = server.Version;
        EditServerType = server.ServerType;
        EditMemoryGb = server.MemoryGb;
        EditPort = server.Port;
        EditOnlineMode = server.OnlineMode;
        EditEulaAccepted = server.EulaAccepted;
        EditMaxPlayers = server.MaxPlayers;
        EditViewDistance = server.ViewDistance;
        EditMotd = server.Motd;
        EditLaunchJarName = server.LaunchJarName;
    }

    private void OpenServerFolder(LocalServerItem server)
    {
        try
        {
            Directory.CreateDirectory(server.FolderPath);
            Process.Start(new ProcessStartInfo("explorer.exe", server.FolderPath) { UseShellExecute = true });
            StatusText = $"Opened {server.Name}.";
        }
        catch (Exception exception)
        {
            StatusText = $"Could not open the server folder: {exception.Message}";
        }
    }

    private static void WriteConfigurationFiles(LocalServerItem server)
    {
        Directory.CreateDirectory(server.FolderPath);
        var propertiesPath = Path.Combine(server.FolderPath, "server.properties");
        var properties = LoadProperties(propertiesPath);
        properties["server-port"] = server.Port.ToString();
        properties["server-ip"] = string.Empty;
        properties["online-mode"] = server.OnlineMode.ToString().ToLowerInvariant();
        properties["enforce-secure-profile"] = server.OnlineMode.ToString().ToLowerInvariant();
        properties["max-players"] = server.MaxPlayers.ToString();
        properties["view-distance"] = server.ViewDistance.ToString();
        properties["motd"] = server.Motd.Replace('\r', ' ').Replace('\n', ' ');
        File.WriteAllLines(propertiesPath, ["# Managed by UB Launcher", .. properties.Select(pair => $"{pair.Key}={pair.Value}")]);
        File.WriteAllText(Path.Combine(server.FolderPath, "eula.txt"), $"eula={server.EulaAccepted.ToString().ToLowerInvariant()}{Environment.NewLine}");
    }

    private static Dictionary<string, string> LoadProperties(string path)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
        {
            return properties;
        }

        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separator = trimmed.IndexOf('=');
            if (separator > 0)
            {
                properties[trimmed[..separator]] = trimmed[(separator + 1)..];
            }
        }

        return properties;
    }

    private static void SaveServers() => ServerStore.Save();

    private int FindAvailablePort()
    {
        var port = 25565;
        while (Servers.Any(server => server.Port == port))
        {
            port++;
        }

        return port;
    }

    private static string FindLocalIpv4Address()
    {
        try
        {
            var candidates = NetworkInterface.GetAllNetworkInterfaces()
                .Where(network =>
                    network.OperationalStatus == OperationalStatus.Up &&
                    network.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
                .Select(network => new
                {
                    Network = network,
                    Properties = network.GetIPProperties()
                })
                .SelectMany(item => item.Properties.UnicastAddresses
                    .Where(address =>
                        address.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(address.Address) &&
                        !address.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                    .Select(address => new
                    {
                        Address = address.Address,
                        HasGateway = item.Properties.GatewayAddresses.Any(gateway =>
                            gateway.Address.AddressFamily == AddressFamily.InterNetwork &&
                            !gateway.Address.Equals(IPAddress.Any))
                    }))
                .OrderByDescending(candidate => candidate.HasGateway)
                .Select(candidate => candidate.Address.ToString())
                .FirstOrDefault();

            return candidates ?? "Not detected";
        }
        catch
        {
            return "Not detected";
        }
    }

    private void NotifyOverviewChanged()
    {
        OnPropertyChanged(nameof(TotalMemoryGb));
        OnPropertyChanged(nameof(RunningServerCount));
        OnPropertyChanged(nameof(ServerStatusSummary));
    }

    private void OnStoredServerSelectionChanged()
    {
        if (!ReferenceEquals(SelectedServer, ServerStore.SelectedServer))
        {
            SelectedServer = ServerStore.SelectedServer;
        }
    }

    private void PostLog(LocalServerItem server, string? line)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            RunOnUi(() => AppendLog(server, line));
        }
    }

    private static void AppendLog(LocalServerItem server, string line)
    {
        IEnumerable<string> existing = server.LogText == "Server console is idle." ? [] : server.LogText.Split(Environment.NewLine);
        server.LogText = string.Join(Environment.NewLine, existing.Append(line).TakeLast(80));
    }

    private void RunOnUi(Action action)
    {
        if (uiContext is null || SynchronizationContext.Current == uiContext)
        {
            action();
        }
        else
        {
            uiContext.Post(_ => action(), null);
        }
    }
}
