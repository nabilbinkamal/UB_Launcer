using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json;
using Launcher.Core.ViewModels;

namespace Launcher.Core.Services;

public static class ServerStore
{
    private static readonly object StateLock = new();
    private static readonly string RegistryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UB Launcher",
        "servers",
        "servers.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static LocalServerItem? selectedServer;

    public static string ServersRoot => Path.GetDirectoryName(RegistryPath)!;

    public static ObservableCollection<LocalServerItem> Servers { get; } = new(LoadServers());

    public static LocalServerItem? SelectedServer
    {
        get => selectedServer ??= Servers.FirstOrDefault();
        set
        {
            if (ReferenceEquals(selectedServer, value))
            {
                return;
            }

            selectedServer = value;
            SelectionChanged?.Invoke();
        }
    }

    public static event Action? SelectionChanged;

    static ServerStore()
    {
        Directory.CreateDirectory(ServersRoot);
        selectedServer = Servers.FirstOrDefault();
        Servers.CollectionChanged += OnServersCollectionChanged;
    }

    public static void Touch(LocalServerItem server)
    {
        server.LastActivityAtUtc = DateTimeOffset.UtcNow;
        Save();
    }

    public static void Save()
    {
        try
        {
            lock (StateLock)
            {
                Directory.CreateDirectory(ServersRoot);
                var definitions = Servers.Select(server => new SavedServerDefinition
                {
                    Id = server.Id,
                    Name = server.Name,
                    Version = server.Version,
                    ServerType = server.ServerType,
                    Port = server.Port,
                    MemoryGb = server.MemoryGb,
                    FolderPath = server.FolderPath,
                    LaunchJarName = server.LaunchJarName,
                    OnlineMode = server.OnlineMode,
                    EulaAccepted = server.EulaAccepted,
                    MaxPlayers = server.MaxPlayers,
                    ViewDistance = server.ViewDistance,
                    Motd = server.Motd,
                    LastActivityAtUtc = server.LastActivityAtUtc
                }).ToList();
                var temporaryPath = RegistryPath + ".tmp";
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(definitions, JsonOptions));
                File.Move(temporaryPath, RegistryPath, true);
            }
        }
        catch
        {
        }
    }

    public static string GetUniqueServerFolder(string name)
    {
        var basePath = GetServerFolder(name);
        var candidate = basePath;
        var suffix = 2;
        while (Directory.Exists(candidate))
        {
            candidate = $"{basePath}-{suffix++}";
        }

        return candidate;
    }

    private static void OnServersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        if (selectedServer is not null && !Servers.Contains(selectedServer))
        {
            selectedServer = Servers.FirstOrDefault();
            SelectionChanged?.Invoke();
        }
        else if (selectedServer is null && Servers.Count > 0)
        {
            selectedServer = Servers[0];
            SelectionChanged?.Invoke();
        }

        Save();
    }

    private static IReadOnlyList<LocalServerItem> LoadServers()
    {
        try
        {
            if (File.Exists(RegistryPath))
            {
                var definitions = JsonSerializer.Deserialize<List<SavedServerDefinition>>(
                                      File.ReadAllText(RegistryPath),
                                      JsonOptions) ?? [];
                return definitions.Select(definition => new LocalServerItem
                {
                    Id = definition.Id,
                    Name = definition.Name,
                    Version = definition.Version,
                    ServerType = definition.ServerType,
                    Port = definition.Port,
                    MemoryGb = definition.MemoryGb,
                    FolderPath = definition.FolderPath,
                    LaunchJarName = definition.LaunchJarName,
                    OnlineMode = definition.OnlineMode,
                    EulaAccepted = definition.EulaAccepted,
                    MaxPlayers = definition.MaxPlayers,
                    ViewDistance = definition.ViewDistance,
                    Motd = definition.Motd,
                    LastActivityAtUtc = definition.LastActivityAtUtc,
                    IsRunning = false,
                    IsSelected = false,
                    LogText = "Server console is idle."
                }).ToList();
            }
        }
        catch
        {
        }

        return
        [
            CreateSampleServer("United Bangla SMP", "1.20.4", "Paper", 25565, 6, DateTimeOffset.UtcNow.AddDays(-1)),
            CreateSampleServer("UB Practice", "1.8.9", "Purpur", 25566, 3, DateTimeOffset.UtcNow.AddDays(-2))
        ];
    }

    private static LocalServerItem CreateSampleServer(
        string name,
        string version,
        string serverType,
        int port,
        int memoryGb,
        DateTimeOffset lastActivityAtUtc) => new()
        {
            Name = name,
            Version = version,
            ServerType = serverType,
            Port = port,
            MemoryGb = memoryGb,
            FolderPath = GetServerFolder(name),
            Status = "Setup required",
            LastActivityAtUtc = lastActivityAtUtc
        };

    private static string GetServerFolder(string name)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var safeName = new string(name.Select(character => invalidCharacters.Contains(character) ? '-' : character).ToArray());
        return Path.Combine(ServersRoot, safeName);
    }

    private sealed class SavedServerDefinition
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Name { get; init; } = string.Empty;
        public string Version { get; init; } = "1.20.4";
        public string ServerType { get; init; } = "Paper";
        public int Port { get; init; } = 25565;
        public int MemoryGb { get; init; } = 4;
        public string FolderPath { get; init; } = string.Empty;
        public string LaunchJarName { get; init; } = "server.jar";
        public bool OnlineMode { get; init; }
        public bool EulaAccepted { get; init; }
        public int MaxPlayers { get; init; } = 20;
        public int ViewDistance { get; init; } = 10;
        public string Motd { get; init; } = "A UB Launcher Minecraft Server";
        public DateTimeOffset? LastActivityAtUtc { get; init; }
    }
}
