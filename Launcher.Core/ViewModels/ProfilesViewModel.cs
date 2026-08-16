using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Services;
using Launcher.Services;
using Minecraft.ModManager;
using Minecraft.Runtime;

namespace Launcher.Core.ViewModels;

public partial class GameProfile : ObservableObject
{
    [ObservableProperty] private string _name = "New instance";
    [ObservableProperty] private string _version = "1.20.4";
    [ObservableProperty] private string _loaderType = "Vanilla";
    [ObservableProperty] private string _lastPlayed = "Never";
    [ObservableProperty] private string _instancePath = string.Empty;
    [ObservableProperty] private string _kind = "Custom";
    [ObservableProperty] private int _modCount;
    [ObservableProperty] private int _resourcePackCount;
    [ObservableProperty] private int _shaderPackCount;
    [ObservableProperty] private int _dataPackCount;
    [ObservableProperty] private string _storageUsed = "Empty";
    [ObservableProperty] private string _accent = "#4B7BEC";
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isDefault;
    [ObservableProperty] private string _iconUrl = string.Empty;
    [ObservableProperty] private string _modrinthProjectId = string.Empty;
    [ObservableProperty] private DateTimeOffset? _lastPlayedAtUtc;

    public string Initials
    {
        get
        {
            var parts = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length switch
            {
                0 => "UB",
                1 => parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant(),
                _ => string.Concat(parts.Take(2).Select(part => char.ToUpperInvariant(part[0])))
            };
        }
    }

    public string RuntimeLabel => $"{LoaderType} {Version}";
    public int TotalInstalledContent => ModCount + ResourcePackCount + ShaderPackCount + DataPackCount;
    public string ContentLabel => TotalInstalledContent == 1 ? "1 content item" : $"{TotalInstalledContent} content items";
    public string ActivityLabel => LastPlayed.Equals("Never", StringComparison.OrdinalIgnoreCase)
        ? "Not launched yet"
        : $"Played {LastPlayed.ToLowerInvariant()}";
    public string RecentActivityLabel => LastPlayedAtUtc is null
        ? "Not played yet"
        : FormatRelativeTime(DateTimeOffset.UtcNow - LastPlayedAtUtc.Value);
    public string StateBadgeLabel => IsDefault ? "DEFAULT" : "READY";
    public bool HasIcon => !string.IsNullOrWhiteSpace(IconUrl);

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(Initials));

    partial void OnVersionChanged(string value) => OnPropertyChanged(nameof(RuntimeLabel));

    partial void OnLoaderTypeChanged(string value) => OnPropertyChanged(nameof(RuntimeLabel));

    partial void OnModCountChanged(int value) => NotifyContentChanged();

    partial void OnResourcePackCountChanged(int value) => NotifyContentChanged();

    partial void OnShaderPackCountChanged(int value) => NotifyContentChanged();

    partial void OnDataPackCountChanged(int value) => NotifyContentChanged();

    partial void OnLastPlayedChanged(string value) => OnPropertyChanged(nameof(ActivityLabel));

    partial void OnLastPlayedAtUtcChanged(DateTimeOffset? value)
    {
        OnPropertyChanged(nameof(ActivityLabel));
        OnPropertyChanged(nameof(RecentActivityLabel));
    }

    partial void OnIsDefaultChanged(bool value) => OnPropertyChanged(nameof(StateBadgeLabel));

    partial void OnIconUrlChanged(string value) => OnPropertyChanged(nameof(HasIcon));

    public void RefreshRelativeTime() => OnPropertyChanged(nameof(RecentActivityLabel));

    public void RefreshContentInventory()
    {
        try
        {
            ModCount = CountFiles(Path.Combine(InstancePath, "mods"), "*.jar");
            ResourcePackCount = CountPackages(Path.Combine(InstancePath, "resourcepacks"));
            ShaderPackCount = CountPackages(Path.Combine(InstancePath, "shaderpacks"));
            DataPackCount = CountDataPacks(InstancePath);
            StorageUsed = FormatStorage(GetDirectorySize(InstancePath));
        }
        catch
        {
            // Keep the last known inventory if the instance directory is temporarily unavailable.
        }
    }

    public void RefreshModInventory()
    {
        try
        {
            ModCount = CountFiles(Path.Combine(InstancePath, "mods"), "*.jar");
        }
        catch
        {
        }
    }

    private void NotifyContentChanged()
    {
        OnPropertyChanged(nameof(TotalInstalledContent));
        OnPropertyChanged(nameof(ContentLabel));
    }

    private static int CountFiles(string path, string pattern) => Directory.Exists(path)
        ? Directory.EnumerateFiles(path, pattern, SearchOption.TopDirectoryOnly).Count()
        : 0;

    private static int CountPackages(string path) => Directory.Exists(path)
        ? Directory.EnumerateFileSystemEntries(path, "*", SearchOption.TopDirectoryOnly)
            .Count(entry => !entry.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
        : 0;

    private static int CountDataPacks(string instancePath)
    {
        var count = CountPackages(Path.Combine(instancePath, "datapacks"));
        var savesPath = Path.Combine(instancePath, "saves");
        if (!Directory.Exists(savesPath))
        {
            return count;
        }

        return count + Directory.EnumerateDirectories(savesPath, "*", SearchOption.TopDirectoryOnly)
            .Sum(worldPath => CountPackages(Path.Combine(worldPath, "datapacks")));
    }

    private static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

        return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Sum(file => new FileInfo(file).Length);
    }

    private static string FormatStorage(long bytes) => bytes switch
    {
        <= 0 => "Empty",
        >= 1_073_741_824 => $"{bytes / 1_073_741_824d:0.#} GB",
        >= 1_048_576 => $"{bytes / 1_048_576d:0} MB",
        >= 1_024 => $"{bytes / 1_024d:0} KB",
        _ => $"{bytes} B"
    };

    private static string FormatRelativeTime(TimeSpan elapsed) => elapsed.TotalMinutes switch
    {
        < 1 => "Just now",
        < 60 => $"{Math.Max(1, (int)elapsed.TotalMinutes)}m ago",
        < 1_440 => $"{Math.Max(1, (int)elapsed.TotalHours)}h ago",
        < 10_080 => $"{Math.Max(1, (int)elapsed.TotalDays)}d ago",
        _ => DateTimeOffset.UtcNow.Subtract(elapsed).ToLocalTime().ToString("MMM d")
    };
}

public partial class InstanceCategory : ObservableObject
{
    public required string Name { get; init; }
    public required string Key { get; init; }
    [ObservableProperty] private bool _isSelected;
}

public partial class ManagedContentCategory : ObservableObject
{
    public required string Name { get; init; }
    public required string Key { get; init; }
    [ObservableProperty] private int _count;
    [ObservableProperty] private bool _isSelected;
}

public partial class ManagedContentItem : ObservableObject
{
    public ManagedContentItem(
        string filePath,
        string name,
        string categoryKey,
        string categoryLabel,
        string iconLabel,
        string iconSource,
        string accent,
        string sizeText,
        string modifiedText,
        bool isEnabled)
    {
        _filePath = filePath;
        Name = name;
        CategoryKey = categoryKey;
        CategoryLabel = categoryLabel;
        IconLabel = iconLabel;
        IconSource = iconSource;
        Accent = accent;
        SizeText = sizeText;
        ModifiedText = modifiedText;
        _isEnabled = isEnabled;
    }

    [ObservableProperty] private string _filePath;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _isBusy;

    public string Name { get; }
    public string CategoryKey { get; }
    public string CategoryLabel { get; }
    public string IconLabel { get; }
    public string IconSource { get; }
    public bool HasIcon => !string.IsNullOrWhiteSpace(IconSource);
    public string Accent { get; }
    public string SizeText { get; }
    public string ModifiedText { get; }
    public string DetailsLabel => $"{CategoryLabel}  /  {SizeText}  /  {ModifiedText}";
}

public partial class ProfilesViewModel : ViewModelBase
{
    private readonly UnifiedContentCatalogService catalogService = new();
    private readonly InstanceArchiveService archiveService = new();
    private readonly Dictionary<string, IReadOnlyList<string>> versionOptionsCache = new(StringComparer.OrdinalIgnoreCase);
    [ObservableProperty] private GameProfile? _selectedProfile;
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private string _selectedSortOption = "Recent activity";
    [ObservableProperty] private string _selectedLoaderFilter = "All loaders";
    [ObservableProperty] private bool _isCreatePanelVisible;
    [ObservableProperty] private bool _isDeleteConfirmationVisible;
    [ObservableProperty] private bool _isLaunching;
    [ObservableProperty] private double _launchProgress;
    [ObservableProperty] private string _statusText = "All instances are ready";
    [ObservableProperty] private string _newProfileName = string.Empty;
    [ObservableProperty] private string _newProfileVersion = "1.20.4";
    [ObservableProperty] private string _newProfileLoader = "Fabric";
    [ObservableProperty] private bool _isVersionEditorVisible;
    [ObservableProperty] private string _versionDraft = string.Empty;
    [ObservableProperty] private bool _isLoadingVersions;
    [ObservableProperty] private bool _isChangingVersion;
    [ObservableProperty] private bool _isArchiveBusy;
    [ObservableProperty] private double _archiveProgress;
    [ObservableProperty] private bool _isContentManagerVisible;
    [ObservableProperty] private string _contentSearchQuery = string.Empty;
    [ObservableProperty] private string _contentManagerStatus = "Choose content to enable or disable";

    public ObservableCollection<GameProfile> Profiles { get; }
    public ObservableCollection<GameProfile> VisibleProfiles { get; } = [];
    public ObservableCollection<string> AvailableMinecraftVersions { get; } = [];
    public ObservableCollection<ManagedContentItem> ManagedContent { get; } = [];
    public ObservableCollection<ManagedContentItem> VisibleManagedContent { get; } = [];
    public IReadOnlyList<string> Loaders { get; } = ["Vanilla", "Fabric", "Forge", "NeoForge"];
    public IReadOnlyList<string> LoaderFilters { get; } = ["All loaders", "Vanilla", "Fabric", "Forge", "NeoForge"];
    public IReadOnlyList<string> SortOptions { get; } = ["Recent activity", "Name A-Z", "Minecraft version"];
    public IReadOnlyList<InstanceCategory> Categories { get; } =
    [
        new() { Name = "All", Key = "All", IsSelected = true },
        new() { Name = "Modpacks", Key = "Modpack" },
        new() { Name = "Vanilla", Key = "Vanilla" },
        new() { Name = "Custom", Key = "Custom" }
    ];
    public IReadOnlyList<ManagedContentCategory> ContentCategories { get; } =
    [
        new() { Name = "All content", Key = "All", IsSelected = true },
        new() { Name = "Mods", Key = "Mods" },
        new() { Name = "Resource packs", Key = "ResourcePacks" },
        new() { Name = "Shaders", Key = "Shaders" },
        new() { Name = "Data packs", Key = "DataPacks" }
    ];

    public int TotalInstances => Profiles.Count;
    public int ModdedInstances => Profiles.Count(profile => !profile.LoaderType.Equals("Vanilla", StringComparison.OrdinalIgnoreCase));
    public int TotalContent => Profiles.Sum(profile => profile.TotalInstalledContent);
    public bool HasSelectedProfile => SelectedProfile is not null;
    public bool IsDetailsPanelVisible => !IsCreatePanelVisible && HasSelectedProfile;
    public bool HasVisibleProfiles => VisibleProfiles.Count > 0;
    public bool IsEmptyStateVisible => !HasVisibleProfiles;
    public string ResultSummary => VisibleProfiles.Count == 1 ? "1 instance" : $"{VisibleProfiles.Count} instances";
    public string LaunchButtonLabel => IsLaunching ? $"Preparing {LaunchProgress:0}%" : "Launch instance";
    public string ActiveProfileSubtitle => SelectedProfile is null
        ? "Choose an instance to make it active"
        : SelectedProfile.Kind.Equals(SelectedProfile.LoaderType, StringComparison.OrdinalIgnoreCase)
            ? SelectedProfile.RuntimeLabel
            : $"{SelectedProfile.RuntimeLabel}  /  {SelectedProfile.Kind}";
    public bool IsSelectedProfileDefault => SelectedProfile?.IsDefault == true;
    public string DefaultActionLabel => IsSelectedProfileDefault ? "Default instance" : "Set as default";
    public string VersionActionLabel => IsChangingVersion ? "Installing..." : "Apply version";
    public string ArchiveProgressLabel => $"{ArchiveProgress:0}%";
    public bool IsArchiveProgressVisible => IsArchiveBusy;
    public int ManagedContentCount => ManagedContent.Count;
    public int EnabledManagedContentCount => ManagedContent.Count(item => item.IsEnabled);
    public int DisabledManagedContentCount => ManagedContentCount - EnabledManagedContentCount;
    public bool IsManagedContentEmpty => VisibleManagedContent.Count == 0;
    public string ManagedContentSummary => VisibleManagedContent.Count == 1
        ? "1 content item"
        : $"{VisibleManagedContent.Count} content items";
    public bool IsSelectedProfileModpack => SelectedProfile?.Kind.Equals("Modpack", StringComparison.OrdinalIgnoreCase) == true;
    public string VersionPickerStatus => IsLoadingVersions
        ? IsSelectedProfileModpack ? "Loading supported versions from Modrinth..." : "Loading official releases..."
        : IsSelectedProfileModpack ? "Choose a version supported by this Modrinth pack" : "Choose an official Minecraft release";

    public ProfilesViewModel()
    {
        Profiles = InstanceStore.Profiles;
        Profiles.CollectionChanged += OnProfilesCollectionChanged;
        InstanceStore.SelectionChanged += OnDefaultProfileChanged;
        SelectedProfile = InstanceStore.SelectedProfile ?? Profiles.FirstOrDefault();
        ApplyFilters();
        _ = InstanceStore.EnsureMissingIconsAsync();
    }

    private void OnProfilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => ApplyFilters();

    partial void OnSearchQueryChanged(string value) => ApplyFilters();

    partial void OnSelectedSortOptionChanged(string value) => ApplyFilters();

    partial void OnSelectedLoaderFilterChanged(string value) => ApplyFilters();

    partial void OnSelectedProfileChanged(GameProfile? value)
    {
        foreach (var profile in Profiles)
        {
            profile.IsSelected = ReferenceEquals(profile, value);
        }

        VersionDraft = value?.Version ?? string.Empty;
        AvailableMinecraftVersions.Clear();
        IsVersionEditorVisible = false;
        IsDeleteConfirmationVisible = false;
        OnPropertyChanged(nameof(HasSelectedProfile));
        OnPropertyChanged(nameof(IsDetailsPanelVisible));
        OnPropertyChanged(nameof(ActiveProfileSubtitle));
        OnPropertyChanged(nameof(IsSelectedProfileDefault));
        OnPropertyChanged(nameof(DefaultActionLabel));
        OnPropertyChanged(nameof(IsSelectedProfileModpack));
        OnPropertyChanged(nameof(VersionPickerStatus));
    }

    partial void OnIsCreatePanelVisibleChanged(bool value) => OnPropertyChanged(nameof(IsDetailsPanelVisible));

    partial void OnIsLaunchingChanged(bool value) => OnPropertyChanged(nameof(LaunchButtonLabel));

    partial void OnLaunchProgressChanged(double value) => OnPropertyChanged(nameof(LaunchButtonLabel));

    partial void OnIsChangingVersionChanged(bool value) => OnPropertyChanged(nameof(VersionActionLabel));

    partial void OnIsLoadingVersionsChanged(bool value) => OnPropertyChanged(nameof(VersionPickerStatus));

    partial void OnIsArchiveBusyChanged(bool value) => OnPropertyChanged(nameof(IsArchiveProgressVisible));

    partial void OnArchiveProgressChanged(double value) => OnPropertyChanged(nameof(ArchiveProgressLabel));

    partial void OnContentSearchQueryChanged(string value) => ApplyContentFilters();

    public async Task ImportInstanceArchiveAsync(string archivePath)
    {
        if (IsArchiveBusy)
        {
            return;
        }

        IsArchiveBusy = true;
        ArchiveProgress = 0;
        StatusText = "Reading instance pack...";
        try
        {
            var descriptor = await archiveService.InspectAsync(archivePath);
            var uniqueName = CreateUniqueName(descriptor.Name);
            var progress = new Progress<InstanceArchiveProgress>(update =>
            {
                ArchiveProgress = Math.Clamp(update.Percentage, 0, 100);
                StatusText = update.Message;
            });
            var result = await archiveService.ImportAsync(archivePath, uniqueName, progress);
            Profiles.Add(result.Profile);
            SelectedProfile = result.Profile;
            IsCreatePanelVisible = false;
            ApplyFilters();
            StatusText = $"Imported {result.Profile.Name} from {result.Format}";
        }
        catch (Exception exception)
        {
            StatusText = $"Import failed: {exception.Message}";
        }
        finally
        {
            IsArchiveBusy = false;
            ArchiveProgress = 0;
        }
    }

    public async Task ExportSelectedInstanceAsync(string destinationPath)
    {
        if (SelectedProfile is null || IsArchiveBusy)
        {
            return;
        }

        IsArchiveBusy = true;
        ArchiveProgress = 0;
        StatusText = $"Preparing {SelectedProfile.Name} for export...";
        try
        {
            var profile = SelectedProfile;
            profile.RefreshContentInventory();
            var progress = new Progress<InstanceArchiveProgress>(update =>
            {
                ArchiveProgress = Math.Clamp(update.Percentage, 0, 100);
                StatusText = update.Message;
            });
            var exportedPath = await archiveService.ExportAsync(profile, destinationPath, progress);
            StatusText = $"Exported {profile.Name} as {Path.GetFileName(exportedPath)}";
        }
        catch (Exception exception)
        {
            StatusText = $"Export failed: {exception.Message}";
        }
        finally
        {
            IsArchiveBusy = false;
            ArchiveProgress = 0;
        }
    }

    public void SetSelectedProfileIcon(string sourcePath)
    {
        if (SelectedProfile is null || string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            StatusText = "Choose a valid profile picture";
            return;
        }

        try
        {
            var sourceInfo = new FileInfo(sourcePath);
            if (sourceInfo.Length is <= 0 or > 8L * 1024 * 1024)
            {
                StatusText = "Profile pictures must be smaller than 8 MB";
                return;
            }

            var extension = sourceInfo.Extension.ToLowerInvariant() switch
            {
                ".jpg" => ".jpg",
                ".jpeg" => ".jpeg",
                ".webp" => ".webp",
                ".png" => ".png",
                _ => string.Empty
            };
            if (string.IsNullOrEmpty(extension))
            {
                StatusText = "Choose a PNG, JPG, JPEG or WebP image";
                return;
            }

            var artworkDirectory = Path.Combine(SelectedProfile.InstancePath, ".ublauncher");
            Directory.CreateDirectory(artworkDirectory);
            var destinationPath = Path.Combine(artworkDirectory, "profile-icon" + extension);
            if (!Path.GetFullPath(sourcePath).Equals(Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(sourcePath, destinationPath, overwrite: true);
            }

            foreach (var oldExtension in new[] { ".png", ".jpg", ".jpeg", ".webp" })
            {
                var oldPath = Path.Combine(artworkDirectory, "profile-icon" + oldExtension);
                if (!oldPath.Equals(destinationPath, StringComparison.OrdinalIgnoreCase) && File.Exists(oldPath))
                {
                    File.Delete(oldPath);
                }
            }

            SelectedProfile.IconUrl = destinationPath;
            StatusText = $"Updated the picture for {SelectedProfile.Name}";
        }
        catch (Exception exception)
        {
            StatusText = $"Could not update the profile picture: {exception.Message}";
        }
    }

    [RelayCommand]
    private void SelectCategory(InstanceCategory category)
    {
        foreach (var item in Categories)
        {
            item.IsSelected = ReferenceEquals(item, category);
        }

        ApplyFilters();
    }

    [RelayCommand]
    private void SelectProfile(GameProfile profile)
    {
        SelectedProfile = profile;
        InstanceStore.SelectedProfile = profile;
        IsCreatePanelVisible = false;
        IsContentManagerVisible = false;
        StatusText = profile.IsDefault
            ? $"{profile.Name} is the default launch instance"
            : $"Selected {profile.Name}";
    }

    [RelayCommand]
    private void OpenContentManager(GameProfile? profile)
    {
        profile ??= SelectedProfile;
        if (profile is null)
        {
            return;
        }

        SelectedProfile = profile;
        IsCreatePanelVisible = false;
        IsContentManagerVisible = true;
        ContentSearchQuery = string.Empty;
        SelectManagedContentCategory(ContentCategories[0]);
        LoadManagedContent();
        StatusText = $"Managing content for {profile.Name}";
    }

    [RelayCommand]
    private void CloseContentManager()
    {
        SelectedProfile?.RefreshContentInventory();
        IsContentManagerVisible = false;
        ApplyFilters();
        StatusText = SelectedProfile is null
            ? "All instances are ready"
            : $"Updated content for {SelectedProfile.Name}";
    }

    [RelayCommand]
    private void RefreshManagedContent()
    {
        LoadManagedContent();
        ContentManagerStatus = $"Refreshed {ManagedContentCount} installed items";
    }

    [RelayCommand]
    private void SelectManagedContentCategory(ManagedContentCategory category)
    {
        foreach (var item in ContentCategories)
        {
            item.IsSelected = ReferenceEquals(item, category);
        }

        ApplyContentFilters();
    }

    [RelayCommand]
    private void SetDefaultProfile()
    {
        if (SelectedProfile is null || SelectedProfile.IsDefault)
        {
            return;
        }

        InstanceStore.SelectedProfile = SelectedProfile;
        StatusText = $"{SelectedProfile.Name} is now the default launch instance";
    }

    [RelayCommand]
    private async Task ShowVersionEditorAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        VersionDraft = SelectedProfile.Version;
        IsVersionEditorVisible = true;
        StatusText = $"Loading available Minecraft versions for {SelectedProfile.Name}";
        await LoadAvailableVersionsAsync();
    }

    [RelayCommand]
    private void CancelVersionEdit()
    {
        VersionDraft = SelectedProfile?.Version ?? string.Empty;
        IsVersionEditorVisible = false;
        StatusText = "Version change cancelled";
    }

    [RelayCommand]
    private async Task SaveVersionAsync()
    {
        if (SelectedProfile is null || IsChangingVersion)
        {
            return;
        }

        var requestedVersion = VersionDraft.Trim();
        if (string.IsNullOrWhiteSpace(requestedVersion))
        {
            StatusText = "Enter a Minecraft version first";
            return;
        }

        var previousVersion = SelectedProfile.Version;
        if (previousVersion.Equals(requestedVersion, StringComparison.OrdinalIgnoreCase))
        {
            IsVersionEditorVisible = false;
            StatusText = $"{SelectedProfile.Name} already uses Minecraft {requestedVersion}";
            return;
        }

        IsChangingVersion = true;
        StatusText = $"Installing Minecraft {requestedVersion} into {SelectedProfile.Name}...";
        try
        {
            var profile = SelectedProfile;
            var launchService = new MinecraftLaunchService(profile.InstancePath);
            await launchService.InstallVersionAsync(
                requestedVersion,
                new Progress<string>(message => StatusText = message));
            profile.Version = requestedVersion;
            VersionDraft = requestedVersion;
            IsVersionEditorVisible = false;
            ApplyFilters();
            OnPropertyChanged(nameof(ActiveProfileSubtitle));
            StatusText = $"{profile.Name} now uses Minecraft {requestedVersion}";
        }
        catch (Exception exception)
        {
            StatusText = $"Version change failed: {exception.Message}";
        }
        finally
        {
            IsChangingVersion = false;
        }
    }

    private async Task LoadAvailableVersionsAsync()
    {
        if (AvailableMinecraftVersions.Count > 0 || IsLoadingVersions)
        {
            return;
        }

        IsLoadingVersions = true;
        try
        {
            var profile = SelectedProfile;
            if (profile is null)
            {
                return;
            }

            if (IsSelectedProfileModpack && string.IsNullOrWhiteSpace(profile.ModrinthProjectId))
            {
                await InstanceStore.EnsureMissingIconsAsync();
            }

            var cacheKey = IsSelectedProfileModpack
                ? $"modrinth:{profile.ModrinthProjectId}"
                : "minecraft:releases";
            if (!versionOptionsCache.TryGetValue(cacheKey, out var versions))
            {
                versions = IsSelectedProfileModpack
                    ? string.IsNullOrWhiteSpace(profile.ModrinthProjectId)
                        ? []
                        : await catalogService.GetModpackGameVersionsAsync(profile.ModrinthProjectId)
                    : await new MinecraftLaunchService().GetAvailableReleaseVersionsAsync();
                versionOptionsCache[cacheKey] = versions;
            }

            foreach (var version in versions)
            {
                AvailableMinecraftVersions.Add(version);
            }

            if (!string.IsNullOrWhiteSpace(VersionDraft) &&
                !AvailableMinecraftVersions.Contains(VersionDraft, StringComparer.OrdinalIgnoreCase))
            {
                AvailableMinecraftVersions.Insert(0, VersionDraft);
            }

            StatusText = AvailableMinecraftVersions.Count == 0
                ? IsSelectedProfileModpack
                    ? "No supported Modrinth versions were found for this pack"
                    : "No Minecraft releases are currently available"
                : IsSelectedProfileModpack
                    ? $"Choose from {AvailableMinecraftVersions.Count} versions supported by {profile.Name}"
                    : $"Choose from {AvailableMinecraftVersions.Count} official Minecraft releases";
        }
        catch (Exception exception)
        {
            StatusText = $"Could not load Minecraft versions: {exception.Message}";
        }
        finally
        {
            IsLoadingVersions = false;
        }
    }

    [RelayCommand]
    private void ShowCreatePanel()
    {
        IsDeleteConfirmationVisible = false;
        IsContentManagerVisible = false;
        IsCreatePanelVisible = true;
        StatusText = "Configure a fresh instance";
    }

    [RelayCommand]
    private void CancelCreate()
    {
        IsCreatePanelVisible = false;
        StatusText = "Creation cancelled";
    }

    [RelayCommand]
    private void AddProfile()
    {
        var requestedName = NewProfileName.Trim();
        if (string.IsNullOrWhiteSpace(requestedName))
        {
            StatusText = "Enter an instance name first";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewProfileVersion))
        {
            StatusText = "Enter a Minecraft version";
            return;
        }

        var uniqueName = CreateUniqueName(requestedName);
        var profile = InstanceStore.CreateProfile(
            uniqueName,
            NewProfileVersion.Trim(),
            NewProfileLoader,
            kind: NewProfileLoader == "Vanilla" ? "Vanilla" : "Custom");

        Profiles.Add(profile);
        SelectedProfile = profile;
        NewProfileName = string.Empty;
        IsCreatePanelVisible = false;
        ApplyFilters();
        StatusText = $"Created {profile.Name}";
    }

    [RelayCommand]
    private void DuplicateProfile()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var duplicate = InstanceStore.CreateProfile(
            CreateUniqueName($"{SelectedProfile.Name} Copy"),
            SelectedProfile.Version,
            SelectedProfile.LoaderType,
            kind: SelectedProfile.Kind,
            modCount: SelectedProfile.ModCount,
            storageUsed: SelectedProfile.StorageUsed,
            accent: SelectedProfile.Accent,
            resourcePackCount: SelectedProfile.ResourcePackCount,
            shaderPackCount: SelectedProfile.ShaderPackCount,
            dataPackCount: SelectedProfile.DataPackCount,
            iconUrl: SelectedProfile.IconUrl,
            modrinthProjectId: SelectedProfile.ModrinthProjectId);
        Profiles.Add(duplicate);
        SelectedProfile = duplicate;
        ApplyFilters();
        StatusText = $"Duplicated as {duplicate.Name}";
    }

    [RelayCommand]
    private void RequestDelete()
    {
        if (SelectedProfile is not null)
        {
            IsDeleteConfirmationVisible = true;
        }
    }

    [RelayCommand]
    private void CancelDelete() => IsDeleteConfirmationVisible = false;

    [RelayCommand]
    private void ConfirmDelete()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var removedName = SelectedProfile.Name;
        var removedIndex = Profiles.IndexOf(SelectedProfile);
        Profiles.Remove(SelectedProfile);
        SelectedProfile = Profiles.Count == 0 ? null : Profiles[Math.Clamp(removedIndex, 0, Profiles.Count - 1)];
        IsDeleteConfirmationVisible = false;
        ApplyFilters();
        StatusText = $"Removed {removedName} from the launcher; its game files were kept";
    }

    [RelayCommand]
    private void OpenInstanceFolder()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(SelectedProfile.InstancePath);
            Process.Start(new ProcessStartInfo("explorer.exe", SelectedProfile.InstancePath) { UseShellExecute = true });
            StatusText = "Opened the instance folder";
        }
        catch (Exception exception)
        {
            StatusText = $"Could not open folder: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task LaunchProfileAsync(GameProfile? profile)
    {
        profile ??= SelectedProfile;
        if (profile is null || IsLaunching)
        {
            return;
        }

        SelectedProfile = profile;
        IsLaunching = true;
        LaunchProgress = 8;
        var username = string.IsNullOrWhiteSpace(OfflineAccountStore.Username) ? "Player" : OfflineAccountStore.Username;

        try
        {
            var launchService = new MinecraftLaunchService(profile.InstancePath);
            var progress = new Progress<string>(message =>
            {
                LaunchProgress = Math.Min(LaunchProgress + 14, 88);
                StatusText = message;
            });
            var gameProcess = await launchService.LaunchOfflineAsync(
                username,
                profile.Version,
                LauncherGameSettingsStore.CreateLaunchConfiguration(),
                progress);
            PlaytimeStore.TrackSession(username, gameProcess);
            DiscordPresenceService.TrackGame(
                gameProcess,
                profile.Name,
                profile.Version,
                profile.LoaderType,
                username);
            LaunchProgress = 100;
            profile.LastPlayed = "Today";
            profile.LastPlayedAtUtc = DateTimeOffset.UtcNow;
            StatusText = $"{profile.Name} launched for {username}";
        }
        catch (Exception exception)
        {
            StatusText = $"Launch failed: {exception.Message}";
        }
        finally
        {
            await Task.Delay(500);
            IsLaunching = false;
            LaunchProgress = 0;
        }
    }

    private void LoadManagedContent()
    {
        ManagedContent.Clear();
        if (SelectedProfile is null)
        {
            ApplyContentFilters();
            return;
        }

        AddManagedFolder(Path.Combine(SelectedProfile.InstancePath, "mods"), "Mods", "Mod", "MOD", "#174C79", modsOnly: true);
        AddManagedFolder(Path.Combine(SelectedProfile.InstancePath, "resourcepacks"), "ResourcePacks", "Resource pack", "RP", "#285844");
        AddManagedFolder(Path.Combine(SelectedProfile.InstancePath, "shaderpacks"), "Shaders", "Shader", "SH", "#503C72");
        AddManagedFolder(Path.Combine(SelectedProfile.InstancePath, "datapacks"), "DataPacks", "Data pack", "DP", "#77502D");
        AddWorldDataPacks(SelectedProfile.InstancePath);

        SelectedProfile.RefreshContentInventory();
        UpdateManagedContentCounts();
        ApplyContentFilters();
        ContentManagerStatus = ManagedContentCount == 0
            ? "No installed content was found for this instance"
            : $"{EnabledManagedContentCount} enabled and {DisabledManagedContentCount} disabled";
    }

    private void AddManagedFolder(
        string folderPath,
        string categoryKey,
        string categoryLabel,
        string iconLabel,
        string accent,
        bool modsOnly = false)
    {
        if (!Directory.Exists(folderPath))
        {
            return;
        }

        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(folderPath, "*", SearchOption.TopDirectoryOnly).ToArray();
        }
        catch
        {
            return;
        }

        foreach (var entryPath in entries
                     .Where(path => IsManagedEntry(path, modsOnly))
                     .OrderBy(path => GetManagedDisplayName(path), StringComparer.OrdinalIgnoreCase))
        {
            var enabled = !entryPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            var isDirectory = Directory.Exists(entryPath);
            var sizeText = isDirectory ? "Folder" : FormatContentSize(new FileInfo(entryPath).Length);
            var modified = isDirectory
                ? new DirectoryInfo(entryPath).LastWriteTime
                : new FileInfo(entryPath).LastWriteTime;
            var item = new ManagedContentItem(
                entryPath,
                GetManagedDisplayName(entryPath),
                categoryKey,
                categoryLabel,
                iconLabel,
                ResolveManagedContentIcon(entryPath, categoryKey),
                accent,
                sizeText,
                $"Modified {modified:MMM d, yyyy}",
                enabled);
            ManagedContent.Add(item);
        }
    }

    private void AddWorldDataPacks(string instancePath)
    {
        var savesPath = Path.Combine(instancePath, "saves");
        if (!Directory.Exists(savesPath))
        {
            return;
        }

        string[] worldPaths;
        try
        {
            worldPaths = Directory.GetDirectories(savesPath, "*", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return;
        }

        foreach (var worldPath in worldPaths)
        {
            var worldName = Path.GetFileName(worldPath);
            AddManagedFolder(
                Path.Combine(worldPath, "datapacks"),
                "DataPacks",
                $"Data pack ({worldName})",
                "DP",
                "#77502D");
        }
    }

    [RelayCommand]
    private void ToggleManagedContent(ManagedContentItem item)
    {
        if (item.IsBusy)
        {
            return;
        }

        var shouldEnable = !item.IsEnabled;
        item.IsBusy = true;
        try
        {
            var sourcePath = item.FilePath;
            var targetPath = shouldEnable
                ? RemoveDisabledSuffix(sourcePath)
                : sourcePath + ".disabled";

            if (sourcePath.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
            {
                item.IsEnabled = shouldEnable;
                return;
            }

            if (File.Exists(targetPath) || Directory.Exists(targetPath))
            {
                throw new IOException($"{Path.GetFileName(targetPath)} already exists");
            }

            if (Directory.Exists(sourcePath))
            {
                Directory.Move(sourcePath, targetPath);
            }
            else if (File.Exists(sourcePath))
            {
                File.Move(sourcePath, targetPath);
            }
            else
            {
                throw new FileNotFoundException("The content file is no longer available", sourcePath);
            }

            item.FilePath = targetPath;
            item.IsEnabled = shouldEnable;
            SelectedProfile?.RefreshContentInventory();
            UpdateManagedContentCounts();
            ApplyContentFilters();
            ContentManagerStatus = $"{item.Name} is now {(shouldEnable ? "enabled" : "disabled")}";
        }
        catch (Exception exception)
        {
            ContentManagerStatus = $"Could not update {item.Name}: {exception.Message}";
            UpdateManagedContentCounts();
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    private void ApplyContentFilters()
    {
        var selectedCategory = ContentCategories.FirstOrDefault(category => category.IsSelected)?.Key ?? "All";
        IEnumerable<ManagedContentItem> filtered = ManagedContent;

        if (!selectedCategory.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(item => item.CategoryKey.Equals(selectedCategory, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(ContentSearchQuery))
        {
            filtered = filtered.Where(item =>
                item.Name.Contains(ContentSearchQuery, StringComparison.OrdinalIgnoreCase) ||
                item.CategoryLabel.Contains(ContentSearchQuery, StringComparison.OrdinalIgnoreCase));
        }

        VisibleManagedContent.Clear();
        foreach (var item in filtered
                     .OrderBy(item => item.CategoryKey, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            VisibleManagedContent.Add(item);
        }

        OnPropertyChanged(nameof(IsManagedContentEmpty));
        OnPropertyChanged(nameof(ManagedContentSummary));
    }

    private void UpdateManagedContentCounts()
    {
        foreach (var category in ContentCategories)
        {
            category.Count = category.Key.Equals("All", StringComparison.OrdinalIgnoreCase)
                ? ManagedContentCount
                : ManagedContent.Count(item => item.CategoryKey.Equals(category.Key, StringComparison.OrdinalIgnoreCase));
        }

        OnPropertyChanged(nameof(ManagedContentCount));
        OnPropertyChanged(nameof(EnabledManagedContentCount));
        OnPropertyChanged(nameof(DisabledManagedContentCount));
    }

    private static bool IsManagedEntry(string path, bool modsOnly)
    {
        var name = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('.'))
        {
            return false;
        }

        if (!modsOnly)
        {
            return true;
        }

        var enabledName = RemoveDisabledSuffix(name);
        return enabledName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetManagedDisplayName(string path)
    {
        var name = RemoveDisabledSuffix(Path.GetFileName(path));
        if (name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            name = Path.GetFileNameWithoutExtension(name);
        }

        return string.IsNullOrWhiteSpace(name) ? "Unnamed content" : name;
    }

    private static string ResolveManagedContentIcon(string entryPath, string categoryKey)
    {
        try
        {
            if (Directory.Exists(entryPath))
            {
                return FindDirectoryArtwork(entryPath);
            }

            if (!File.Exists(entryPath))
            {
                return string.Empty;
            }

            using var archive = ZipFile.OpenRead(entryPath);
            var artworkEntry = categoryKey.Equals("Mods", StringComparison.OrdinalIgnoreCase)
                ? FindModArtworkEntry(archive)
                : FindPackArtworkEntry(archive);
            return artworkEntry is null ? string.Empty : ExtractArtworkToCache(entryPath, artworkEntry);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FindDirectoryArtwork(string directoryPath)
    {
        var candidates = new[] { "pack.png", "icon.png", "logo.png" };
        foreach (var candidate in candidates)
        {
            var path = Path.Combine(directoryPath, candidate);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return string.Empty;
    }

    private static ZipArchiveEntry? FindModArtworkEntry(ZipArchive archive)
    {
        var metadataPath = ReadJsonArtworkPath(archive, "fabric.mod.json", document =>
            TryReadArtworkValue(document.RootElement, "icon"));
        metadataPath ??= ReadJsonArtworkPath(archive, "quilt.mod.json", document =>
        {
            if (!document.RootElement.TryGetProperty("quilt_loader", out var loader) ||
                !loader.TryGetProperty("metadata", out var metadata))
            {
                return null;
            }

            return TryReadArtworkValue(metadata, "icon");
        });
        metadataPath ??= ReadForgeArtworkPath(archive, "META-INF/mods.toml");
        metadataPath ??= ReadForgeArtworkPath(archive, "META-INF/neoforge.mods.toml");

        var metadataEntry = FindArchiveEntry(archive, metadataPath);
        if (metadataEntry is not null)
        {
            return metadataEntry;
        }

        return FindArchiveEntry(archive, "icon.png")
               ?? FindArchiveEntry(archive, "logo.png")
               ?? archive.Entries
                   .Where(entry => entry.FullName.EndsWith("/icon.png", StringComparison.OrdinalIgnoreCase) ||
                                   entry.FullName.EndsWith("/logo.png", StringComparison.OrdinalIgnoreCase))
                   .OrderBy(entry => entry.FullName.Count(character => character == '/'))
                   .ThenBy(entry => entry.FullName.Length)
                   .FirstOrDefault();
    }

    private static ZipArchiveEntry? FindPackArtworkEntry(ZipArchive archive) =>
        FindArchiveEntry(archive, "pack.png")
        ?? FindArchiveEntry(archive, "icon.png")
        ?? archive.Entries.FirstOrDefault(entry => entry.FullName.EndsWith("/pack.png", StringComparison.OrdinalIgnoreCase));

    private static string? ReadJsonArtworkPath(
        ZipArchive archive,
        string metadataName,
        Func<JsonDocument, string?> selector)
    {
        var entry = FindArchiveEntry(archive, metadataName);
        if (entry is null)
        {
            return null;
        }

        try
        {
            using var stream = entry.Open();
            using var document = JsonDocument.Parse(stream);
            return selector(document);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadArtworkValue(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return value.EnumerateObject()
            .OrderByDescending(property => int.TryParse(property.Name, out var size) ? size : 0)
            .Select(property => property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null)
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
    }

    private static string? ReadForgeArtworkPath(ZipArchive archive, string metadataName)
    {
        var entry = FindArchiveEntry(archive, metadataName);
        if (entry is null)
        {
            return null;
        }

        try
        {
            using var reader = new StreamReader(entry.Open());
            var match = Regex.Match(
                reader.ReadToEnd(),
                "logoFile\\s*=\\s*[\"'](?<path>[^\"']+)[\"']",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return match.Success ? match.Groups["path"].Value : null;
        }
        catch
        {
            return null;
        }
    }

    private static ZipArchiveEntry? FindArchiveEntry(ZipArchive archive, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalizedPath = path.Replace('\\', '/').TrimStart('/');
        return archive.Entries.FirstOrDefault(entry =>
            entry.FullName.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));
    }

    private static string ExtractArtworkToCache(string archivePath, ZipArchiveEntry entry)
    {
        var fileInfo = new FileInfo(archivePath);
        var cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{fileInfo.FullName}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}|{entry.FullName}")));
        var extension = Path.GetExtension(entry.Name).ToLowerInvariant();
        if (extension is not ".png" and not ".jpg" and not ".jpeg")
        {
            extension = ".png";
        }

        var cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UBLauncher",
            "cache",
            "content-icons");
        var cachePath = Path.Combine(cacheDirectory, cacheKey + extension);
        if (File.Exists(cachePath))
        {
            return cachePath;
        }

        Directory.CreateDirectory(cacheDirectory);
        using var source = entry.Open();
        using var destination = File.Create(cachePath);
        source.CopyTo(destination);
        return cachePath;
    }

    private static string RemoveDisabledSuffix(string value) => value.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
        ? value[..^".disabled".Length]
        : value;

    private static string FormatContentSize(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824d:0.##} GB",
        >= 1_048_576 => $"{bytes / 1_048_576d:0.##} MB",
        >= 1_024 => $"{bytes / 1_024d:0.#} KB",
        _ => $"{bytes} B"
    };

    private void ApplyFilters()
    {
        var selectedCategory = Categories.FirstOrDefault(category => category.IsSelected)?.Key ?? "All";
        IEnumerable<GameProfile> filtered = Profiles;

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            filtered = filtered.Where(profile =>
                profile.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                profile.Version.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                profile.LoaderType.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
        }

        if (selectedCategory != "All")
        {
            filtered = filtered.Where(profile => profile.Kind.Equals(selectedCategory, StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedLoaderFilter != "All loaders")
        {
            filtered = filtered.Where(profile => profile.LoaderType.Equals(SelectedLoaderFilter, StringComparison.OrdinalIgnoreCase));
        }

        filtered = SelectedSortOption switch
        {
            "Name A-Z" => filtered.OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase),
            "Minecraft version" => filtered.OrderByDescending(profile => ParseVersion(profile.Version)),
            _ => filtered
                .OrderByDescending(profile => profile.LastPlayedAtUtc ?? DateTimeOffset.MinValue)
                .ThenBy(profile => Profiles.IndexOf(profile))
        };

        VisibleProfiles.Clear();
        foreach (var profile in filtered)
        {
            VisibleProfiles.Add(profile);
        }

        NotifySummaryChanged();
    }

    private string CreateUniqueName(string requestedName)
    {
        var candidate = requestedName;
        var suffix = 2;
        while (Profiles.Any(profile => profile.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)) ||
               Directory.Exists(InstanceStore.GetInstancePath(candidate)))
        {
            candidate = $"{requestedName} {suffix++}";
        }

        return candidate;
    }

    private static Version ParseVersion(string value)
    {
        var normalized = string.Join('.', value.Split('.').Take(4));
        return Version.TryParse(normalized, out var parsed) ? parsed : new Version(0, 0);
    }

    private void NotifySummaryChanged()
    {
        OnPropertyChanged(nameof(TotalInstances));
        OnPropertyChanged(nameof(ModdedInstances));
        OnPropertyChanged(nameof(TotalContent));
        OnPropertyChanged(nameof(HasVisibleProfiles));
        OnPropertyChanged(nameof(IsEmptyStateVisible));
        OnPropertyChanged(nameof(ResultSummary));
    }

    private void OnDefaultProfileChanged()
    {
        if (!ReferenceEquals(SelectedProfile, InstanceStore.SelectedProfile))
        {
            SelectedProfile = InstanceStore.SelectedProfile;
        }

        OnPropertyChanged(nameof(IsSelectedProfileDefault));
        OnPropertyChanged(nameof(DefaultActionLabel));
    }
}
