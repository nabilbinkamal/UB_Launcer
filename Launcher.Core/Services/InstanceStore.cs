using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Launcher.Core.ViewModels;
using Minecraft.ModManager;

namespace Launcher.Core.Services;

public static class InstanceStore
{
    private static readonly object StateLock = new();
    private static readonly string StatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UBLauncher",
        "instances.json");
    private static string? loadedSelectedPath;
    private static string? loadedDefaultPath;
    private static GameProfile? selectedProfile;
    private static GameProfile? defaultProfile;
    private static Task? iconRefreshTask;

    public static ObservableCollection<GameProfile> Profiles { get; } = LoadProfiles();

    static InstanceStore()
    {
        foreach (var profile in Profiles)
        {
            AttachProfile(profile);
        }

        Profiles.CollectionChanged += OnProfilesCollectionChanged;

        GameProfile? defProfile = null;
        if (!string.IsNullOrWhiteSpace(loadedDefaultPath))
        {
            defProfile = Profiles.FirstOrDefault(profile =>
                profile.InstancePath.Equals(loadedDefaultPath, StringComparison.OrdinalIgnoreCase));
        }

        if (defProfile is null)
        {
            defProfile = Profiles.FirstOrDefault(profile => profile.IsDefault) ??
                         Profiles.FirstOrDefault(profile =>
                             profile.InstancePath.Equals(loadedSelectedPath, StringComparison.OrdinalIgnoreCase)) ??
                         Profiles.FirstOrDefault();
        }

        defaultProfile = defProfile;
        foreach (var profile in Profiles)
        {
            profile.IsDefault = ReferenceEquals(profile, defaultProfile);
        }

        selectedProfile = Profiles.FirstOrDefault(profile =>
                              profile.InstancePath.Equals(loadedSelectedPath, StringComparison.OrdinalIgnoreCase)) ??
                          defaultProfile;
    }

    public static GameProfile? DefaultProfile
    {
        get => defaultProfile ??= Profiles.FirstOrDefault(p => p.IsDefault) ?? Profiles.FirstOrDefault();
        set
        {
            if (value is not null)
            {
                SetDefaultProfile(value);
            }
        }
    }

    public static GameProfile? SelectedProfile
    {
        get => selectedProfile ??= defaultProfile ?? Profiles.FirstOrDefault();
        set
        {
            if (ReferenceEquals(selectedProfile, value))
            {
                return;
            }

            selectedProfile = value;
            SaveState();
            SelectionChanged?.Invoke();
        }
    }

    public static void SetDefaultProfile(GameProfile profile)
    {
        if (profile is null)
        {
            return;
        }

        defaultProfile = profile;
        foreach (var p in Profiles)
        {
            p.IsDefault = ReferenceEquals(p, profile);
        }

        SaveState();
        DefaultProfileChanged?.Invoke();
        ProfilesChanged?.Invoke();
    }

    public static event Action? SelectionChanged;
    public static event Action? DefaultProfileChanged;
    public static event Action? ProfilesChanged;

    public static Task EnsureMissingIconsAsync()
    {
        lock (StateLock)
        {
            if (iconRefreshTask is null || iconRefreshTask.IsCompleted)
            {
                iconRefreshTask = RefreshMissingIconsAsync();
            }

            return iconRefreshTask;
        }
    }

    private static async Task RefreshMissingIconsAsync()
    {
        var missingProfiles = Profiles
            .Where(profile => profile.Kind.Equals("Modpack", StringComparison.OrdinalIgnoreCase) &&
                              (!profile.HasIcon || string.IsNullOrWhiteSpace(profile.ModrinthProjectId)))
            .ToList();
        if (missingProfiles.Count == 0)
        {
            return;
        }

        var catalogService = new UnifiedContentCatalogService();
        foreach (var profile in missingProfiles)
        {
            if (!profile.HasIcon)
            {
                var localIcon = ModrinthAppProfileArtworkResolver.TryCopyToInstance(
                    profile.Name,
                    profile.InstancePath);
                if (!string.IsNullOrWhiteSpace(localIcon))
                {
                    profile.IconUrl = localIcon;
                }
            }

            var result = await catalogService.SearchAsync(new CatalogSearchRequest(
                profile.Name,
                CatalogContentType.Modpacks,
                string.Empty,
                string.Empty,
                8));
            var identity = NormalizeProfileIdentity(profile.Name);
            var matchingProject = result.Projects.FirstOrDefault(project =>
                                      !string.IsNullOrWhiteSpace(project.IconUrl) &&
                                      NormalizeProfileIdentity(project.Name).Equals(identity, StringComparison.OrdinalIgnoreCase));
            if (matchingProject is not null)
            {
                if (!profile.HasIcon)
                {
                    profile.IconUrl = matchingProject.IconUrl;
                }

                if (string.IsNullOrWhiteSpace(profile.ModrinthProjectId) &&
                    matchingProject.SourceIds.TryGetValue(CatalogSource.Modrinth, out var projectId))
                {
                    profile.ModrinthProjectId = projectId;
                }
            }
        }
    }

    private static string NormalizeProfileIdentity(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    private static ObservableCollection<GameProfile> LoadProfiles()
    {
        try
        {
            if (File.Exists(StatePath))
            {
                var state = JsonSerializer.Deserialize<InstanceStoreState>(File.ReadAllText(StatePath));
                if (state?.Profiles is not null)
                {
                    loadedSelectedPath = state.SelectedInstancePath;
                    loadedDefaultPath = state.DefaultInstancePath;
                    return new ObservableCollection<GameProfile>(state.Profiles.Select(ToGameProfile));
                }
            }
        }
        catch
        {
        }

        return [];
    }

    private static void OnProfilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        if (eventArgs.OldItems is not null)
        {
            foreach (GameProfile profile in eventArgs.OldItems)
            {
                profile.PropertyChanged -= OnProfilePropertyChanged;
            }
        }

        if (eventArgs.NewItems is not null)
        {
            foreach (GameProfile profile in eventArgs.NewItems)
            {
                AttachProfile(profile);
            }
        }

        var defMissing = defaultProfile is null || !Profiles.Contains(defaultProfile);
        if (defMissing)
        {
            defaultProfile = Profiles.FirstOrDefault(p => p.IsDefault) ?? Profiles.FirstOrDefault();
            if (defaultProfile is not null)
            {
                foreach (var p in Profiles)
                {
                    p.IsDefault = ReferenceEquals(p, defaultProfile);
                }
            }
        }

        var selectionMissing = (selectedProfile is null && Profiles.Count > 0) ||
                               (selectedProfile is not null && !Profiles.Contains(selectedProfile));
        if (selectionMissing)
        {
            selectedProfile = defaultProfile ?? Profiles.FirstOrDefault();
        }

        SaveState();
        ProfilesChanged?.Invoke();
        if (selectionMissing)
        {
            SelectionChanged?.Invoke();
        }
    }

    private static void AttachProfile(GameProfile profile) => profile.PropertyChanged += OnProfilePropertyChanged;

    private static void OnProfilePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (!IsStoredProfileProperty(eventArgs.PropertyName))
        {
            return;
        }

        SaveState();
        ProfilesChanged?.Invoke();
    }

    private static bool IsStoredProfileProperty(string? propertyName) => propertyName is
        nameof(GameProfile.Name) or
        nameof(GameProfile.Version) or
        nameof(GameProfile.LoaderType) or
        nameof(GameProfile.LastPlayed) or
        nameof(GameProfile.InstancePath) or
        nameof(GameProfile.Kind) or
        nameof(GameProfile.ModCount) or
        nameof(GameProfile.ResourcePackCount) or
        nameof(GameProfile.ShaderPackCount) or
        nameof(GameProfile.DataPackCount) or
        nameof(GameProfile.StorageUsed) or
        nameof(GameProfile.Accent) or
        nameof(GameProfile.IsDefault) or
        nameof(GameProfile.IconUrl) or
        nameof(GameProfile.ModrinthProjectId) or
        nameof(GameProfile.LastPlayedAtUtc);

    private static void SaveState()
    {
        try
        {
            lock (StateLock)
            {
                var directory = Path.GetDirectoryName(StatePath)!;
                Directory.CreateDirectory(directory);
                var state = new InstanceStoreState
                {
                    SelectedInstancePath = selectedProfile?.InstancePath,
                    DefaultInstancePath = (defaultProfile ?? Profiles.FirstOrDefault(p => p.IsDefault))?.InstancePath,
                    Profiles = Profiles.Select(ToStoredProfile).ToList()
                };
                var temporaryPath = StatePath + ".tmp";
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
                File.Move(temporaryPath, StatePath, overwrite: true);
            }
        }
        catch
        {
        }
    }

    private static StoredProfile ToStoredProfile(GameProfile profile) => new()
    {
        Name = profile.Name,
        Version = profile.Version,
        LoaderType = profile.LoaderType,
        LastPlayed = profile.LastPlayed,
        InstancePath = profile.InstancePath,
        Kind = profile.Kind,
        ModCount = profile.ModCount,
        ResourcePackCount = profile.ResourcePackCount,
        ShaderPackCount = profile.ShaderPackCount,
        DataPackCount = profile.DataPackCount,
        StorageUsed = profile.StorageUsed,
        Accent = profile.Accent,
        IconUrl = profile.IconUrl,
        ModrinthProjectId = profile.ModrinthProjectId,
        IsDefault = profile.IsDefault,
        LastPlayedAtUtc = profile.LastPlayedAtUtc
    };

    private static GameProfile ToGameProfile(StoredProfile profile) => new()
    {
        Name = profile.Name,
        Version = profile.Version,
        LoaderType = profile.LoaderType,
        LastPlayed = profile.LastPlayed,
        InstancePath = profile.InstancePath,
        Kind = profile.Kind,
        ModCount = profile.ModCount,
        ResourcePackCount = profile.ResourcePackCount,
        ShaderPackCount = profile.ShaderPackCount,
        DataPackCount = profile.DataPackCount,
        StorageUsed = profile.StorageUsed,
        Accent = profile.Accent,
        IconUrl = profile.IconUrl,
        ModrinthProjectId = profile.ModrinthProjectId,
        IsDefault = profile.IsDefault,
        LastPlayedAtUtc = profile.LastPlayedAtUtc
    };

    public static GameProfile CreateProfile(
        string name,
        string version,
        string loader,
        string lastPlayed = "Never",
        string? kind = null,
        int modCount = 0,
        string storageUsed = "Empty",
        string? accent = null,
        int resourcePackCount = 0,
        int shaderPackCount = 0,
        int dataPackCount = 0,
        string? iconUrl = null,
        string? modrinthProjectId = null)
    {
        return new GameProfile
        {
            Name = name,
            Version = version,
            LoaderType = loader,
            LastPlayed = lastPlayed,
            InstancePath = GetInstancePath(name),
            Kind = kind ?? (loader.Equals("Vanilla", StringComparison.OrdinalIgnoreCase) ? "Vanilla" : "Custom"),
            ModCount = modCount,
            ResourcePackCount = resourcePackCount,
            ShaderPackCount = shaderPackCount,
            DataPackCount = dataPackCount,
            StorageUsed = storageUsed,
            Accent = accent ?? GetLoaderAccent(loader),
            IconUrl = iconUrl ?? string.Empty,
            ModrinthProjectId = modrinthProjectId ?? string.Empty
        };
    }

    private static string GetLoaderAccent(string loader) => loader.ToLowerInvariant() switch
    {
        "fabric" => "#2E9B78",
        "forge" => "#D97B43",
        "neoforge" => "#7357D8",
        _ => "#4B78C9"
    };

    public static string GetInstancePath(string name)
    {
        var safeName = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            safeName.Append(char.IsLetterOrDigit(character) || character is '-' or '_'
                ? character
                : '-');
        }

        var folderName = safeName.ToString().Trim('-');
        if (string.IsNullOrWhiteSpace(folderName))
        {
            folderName = "instance";
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "UBLauncher",
            "instances",
            folderName,
            ".minecraft");
    }

    private sealed class InstanceStoreState
    {
        public string? SelectedInstancePath { get; init; }
        public string? DefaultInstancePath { get; init; }
        public List<StoredProfile> Profiles { get; init; } = [];
    }

    private sealed class StoredProfile
    {
        public string Name { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
        public string LoaderType { get; init; } = string.Empty;
        public string LastPlayed { get; init; } = "Never";
        public string InstancePath { get; init; } = string.Empty;
        public string Kind { get; init; } = "Custom";
        public int ModCount { get; init; }
        public int ResourcePackCount { get; init; }
        public int ShaderPackCount { get; init; }
        public int DataPackCount { get; init; }
        public string StorageUsed { get; init; } = "Empty";
        public string Accent { get; init; } = "#4B78C9";
        public string IconUrl { get; init; } = string.Empty;
        public string ModrinthProjectId { get; init; } = string.Empty;
        public bool IsDefault { get; init; }
        public DateTimeOffset? LastPlayedAtUtc { get; init; }
    }
}
