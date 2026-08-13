using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Services;
using Minecraft.ModManager;
using Minecraft.Runtime;

namespace Launcher.Core.ViewModels;

public partial class ContentItem : ObservableObject
{
    public required CatalogProject Project { get; init; }
    public string Name => Project.Name;
    public string Author => Project.Author;
    public string Description => Project.Description;
    public string IconUrl => Project.IconUrl;
    public string Accent => Project.Accent;
    public string Mark => Project.Mark;
    public string Tags => Project.Tags;
    public string StoreLabel => Project.StoreLabel;
    public string Downloads => FormatMetric(Project.Downloads);
    public string Likes => Project.Followers <= 0 ? "—" : FormatMetric(Project.Followers);
    public string Updated => FormatRelativeTime(Project.UpdatedAt);

    [ObservableProperty] private bool _isInstalled;
    [ObservableProperty] private bool _isInstalling;
    [ObservableProperty] private double _installProgress;
    [ObservableProperty] private string _installStatus = string.Empty;

    public string ActionLabel => IsInstalling
        ? $"Installing {InstallProgress:0}%"
        : IsInstalled ? "Installed" : "Install";

    public string ActionIcon => IsInstalling ? "↓" : IsInstalled ? "✓" : "+";
    public bool CanInstall => !IsInstalling && !IsInstalled;

    partial void OnIsInstalledChanged(bool value)
    {
        OnPropertyChanged(nameof(ActionLabel));
        OnPropertyChanged(nameof(ActionIcon));
        OnPropertyChanged(nameof(CanInstall));
    }

    partial void OnIsInstallingChanged(bool value)
    {
        OnPropertyChanged(nameof(ActionLabel));
        OnPropertyChanged(nameof(ActionIcon));
        OnPropertyChanged(nameof(CanInstall));
    }
    partial void OnInstallProgressChanged(double value) => OnPropertyChanged(nameof(ActionLabel));

    private static string FormatMetric(long value)
    {
        return value switch
        {
            >= 1_000_000_000 => $"{value / 1_000_000_000d:0.#}B",
            >= 1_000_000 => $"{value / 1_000_000d:0.##}M",
            >= 1_000 => $"{value / 1_000d:0.#}K",
            _ => value.ToString("N0")
        };
    }

    private static string FormatRelativeTime(DateTimeOffset value)
    {
        var elapsed = DateTimeOffset.UtcNow - value;
        if (elapsed.TotalMinutes < 1) return "just now";
        if (elapsed.TotalHours < 1) return $"{Math.Max(1, (int)elapsed.TotalMinutes)}m ago";
        if (elapsed.TotalDays < 1) return $"{Math.Max(1, (int)elapsed.TotalHours)}h ago";
        if (elapsed.TotalDays < 30) return $"{Math.Max(1, (int)elapsed.TotalDays)}d ago";
        if (elapsed.TotalDays < 365) return $"{Math.Max(1, (int)(elapsed.TotalDays / 30))}mo ago";
        return $"{Math.Max(1, (int)(elapsed.TotalDays / 365))}y ago";
    }
}

public partial class ContentCategory : ObservableObject
{
    public required string Name { get; init; }
    public required CatalogContentType Type { get; init; }
    [ObservableProperty] private bool _isSelected;
}

public partial class ModsViewModel : ViewModelBase
{
    private static readonly TimeSpan CatalogCacheLifetime = TimeSpan.FromMinutes(10);
    private readonly UnifiedContentCatalogService catalogService = new();
    private readonly List<ContentItem> relevanceOrder = [];
    private readonly Dictionary<CatalogCacheKey, CatalogCacheEntry> catalogCache = [];
    private readonly Dictionary<CatalogCacheKey, Task<CatalogSearchResult>> inFlightSearches = [];
    private readonly HashSet<string> warmedCatalogs = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? searchCancellation;
    private long loadRevision;

    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private ContentCategory _selectedCategory;
    [ObservableProperty] private GameProfile? _selectedInstance;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "Loading compatible content...";
    [ObservableProperty] private string _sourcesText = "Modrinth";
    [ObservableProperty] private string _selectedSortOption = "Relevance";
    [ObservableProperty] private ObservableCollection<ContentItem> _visibleContent = [];

    public IReadOnlyList<string> SortOptions { get; } =
    [
        "Relevance",
        "Most downloaded",
        "Most followed",
        "Recently updated"
    ];
    public string ResultCountText => VisibleContent.Count == 1
        ? "1 compatible project"
        : $"{VisibleContent.Count} compatible projects";

    public IReadOnlyList<ContentCategory> Categories { get; } =
    [
        new() { Name = "Modpacks", Type = CatalogContentType.Modpacks, IsSelected = true },
        new() { Name = "Mods", Type = CatalogContentType.Mods },
        new() { Name = "Resource Packs", Type = CatalogContentType.ResourcePacks },
        new() { Name = "Data Packs", Type = CatalogContentType.DataPacks },
        new() { Name = "Shaders", Type = CatalogContentType.Shaders }
    ];

    public IReadOnlyList<GameProfile> Instances => InstanceStore.Profiles;
    public bool IsStandaloneModpackCategory => SelectedCategory.Type == CatalogContentType.Modpacks;
    public string InstallTargetHeading => IsStandaloneModpackCategory
        ? "Standalone installation"
        : "Install to instance";
    public string CompatibilityHeading => IsStandaloneModpackCategory ? "Install mode" : "Compatible with";
    public string InstallCompatibilityText => IsStandaloneModpackCategory
        ? "New individual instance  ·  latest supported Minecraft"
        : SelectedCompatibility;
    public string SelectedCompatibility => SelectedInstance is null
        ? "Select an instance"
        : $"{SelectedInstance.Name}  ·  {SelectedInstance.LoaderType} {SelectedInstance.Version}";

    public ModsViewModel()
    {
        _selectedCategory = Categories[0];
        _selectedInstance = InstanceStore.SelectedProfile;
        InstanceStore.SelectionChanged += OnSharedInstanceSelectionChanged;
        _ = ReloadAsync();
    }

    partial void OnSearchQueryChanged(string value)
    {
        searchCancellation?.Cancel();
        var cancellation = searchCancellation = new CancellationTokenSource();
        _ = DebouncedReloadAsync(cancellation.Token);
    }

    partial void OnSelectedInstanceChanged(GameProfile? value)
    {
        InstanceStore.SelectedProfile = value;
        OnPropertyChanged(nameof(SelectedCompatibility));
        OnPropertyChanged(nameof(InstallCompatibilityText));
        if (!IsStandaloneModpackCategory)
        {
            _ = ReloadAsync();
        }
    }

    partial void OnSelectedSortOptionChanged(string value) => ApplyCurrentSort();

    [RelayCommand]
    private void SelectCategory(ContentCategory category)
    {
        if (ReferenceEquals(SelectedCategory, category))
        {
            return;
        }

        foreach (var item in Categories)
        {
            item.IsSelected = ReferenceEquals(item, category);
        }

        SelectedCategory = category;
        OnPropertyChanged(nameof(IsStandaloneModpackCategory));
        OnPropertyChanged(nameof(InstallTargetHeading));
        OnPropertyChanged(nameof(CompatibilityHeading));
        OnPropertyChanged(nameof(InstallCompatibilityText));
        _ = ReloadAsync();
    }

    [RelayCommand]
    private Task RefreshAsync() => ReloadAsync();

    [RelayCommand]
    private async Task InstallAsync(ContentItem item)
    {
        var targetInstance = SelectedInstance;
        var isStandaloneModpack = item.Project.ContentType == CatalogContentType.Modpacks;
        if (item.IsInstalling || item.IsInstalled)
        {
            return;
        }

        if (!isStandaloneModpack && targetInstance is null)
        {
            StatusText = "Choose an instance before installing content.";
            return;
        }

        item.IsInstalling = true;
        item.InstallProgress = 0;
        item.InstallStatus = isStandaloneModpack
            ? "Preparing a new standalone instance..."
            : $"Preparing {targetInstance!.Name}...";
        StatusText = isStandaloneModpack
            ? $"Creating a standalone instance for {item.Name}..."
            : $"Installing {item.Name} into {targetInstance!.Name}...";

        try
        {
            if (isStandaloneModpack)
            {
                await InstallStandaloneModpackAsync(item);
                return;
            }

            var target = new InstanceInstallTarget(
                targetInstance!.Name,
                targetInstance.Version,
                targetInstance.LoaderType,
                targetInstance.InstancePath);
            var progress = new Progress<CatalogInstallProgress>(update =>
            {
                item.InstallProgress = update.Percent;
                item.InstallStatus = update.Message;
            });
            var result = await catalogService.InstallAsync(item.Project, target, progress);

            targetInstance.RefreshContentInventory();

            item.IsInstalled = true;
            item.InstallStatus = result.Message;
            StatusText = result.Message;
        }
        catch (Exception exception)
        {
            item.InstallStatus = exception.Message;
            StatusText = $"Install failed: {exception.Message}";
        }
        finally
        {
            item.IsInstalling = false;
        }
    }

    private async Task InstallStandaloneModpackAsync(ContentItem item)
    {
        var instanceName = CreateUniqueInstanceName(item.Name);
        var profile = InstanceStore.CreateProfile(instanceName, "Latest", "Auto", kind: "Modpack");
        profile.IconUrl = item.IconUrl;
        if (item.Project.SourceIds.TryGetValue(CatalogSource.Modrinth, out var modrinthProjectId))
        {
            profile.ModrinthProjectId = modrinthProjectId;
        }

        try
        {
            var target = new InstanceInstallTarget(
                profile.Name,
                profile.Version,
                profile.LoaderType,
                profile.InstancePath);
            var packProgress = new Progress<CatalogInstallProgress>(update =>
            {
                item.InstallProgress = Math.Clamp(update.Percent * 0.82, 0, 82);
                item.InstallStatus = update.Message;
            });
            var result = await catalogService.InstallStandaloneModpackAsync(item.Project, target, packProgress);

            profile.Version = result.GameVersion;
            profile.LoaderType = result.Loader;
            item.InstallProgress = 86;
            item.InstallStatus = $"Preparing Minecraft {profile.Version}...";

            var runtimeProgress = new Progress<string>(message =>
            {
                item.InstallProgress = Math.Max(item.InstallProgress, 92);
                item.InstallStatus = message;
            });
            var launchService = new MinecraftLaunchService(profile.InstancePath);
            await launchService.InstallVersionAsync(profile.Version, runtimeProgress);

            profile.RefreshContentInventory();
            InstanceStore.Profiles.Add(profile);
            InstanceStore.SelectedProfile = profile;
            item.IsInstalled = true;
            item.InstallProgress = 100;
            item.InstallStatus = $"Created {profile.Name}.";
            StatusText = $"Created {profile.Name} with Minecraft {profile.Version} and {profile.LoaderType}.";
            OnPropertyChanged(nameof(SelectedCompatibility));
            OnPropertyChanged(nameof(InstallCompatibilityText));
        }
        catch
        {
            await DeleteIncompleteInstanceAsync(profile.InstancePath);
            throw;
        }
    }

    private static string CreateUniqueInstanceName(string requestedName)
    {
        var baseName = string.IsNullOrWhiteSpace(requestedName) ? "Modpack" : requestedName.Trim();
        var candidate = baseName;
        var suffix = 2;
        while (InstanceStore.Profiles.Any(profile =>
                   profile.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)) ||
               InstanceDirectoryExists(candidate))
        {
            candidate = $"{baseName} {suffix++}";
        }

        return candidate;
    }

    private static bool InstanceDirectoryExists(string name)
    {
        var minecraftPath = InstanceStore.GetInstancePath(name);
        var instanceDirectory = Directory.GetParent(minecraftPath)?.FullName;
        return instanceDirectory is not null && Directory.Exists(instanceDirectory);
    }

    private static Task DeleteIncompleteInstanceAsync(string minecraftPath)
    {
        var instanceDirectory = Directory.GetParent(minecraftPath)?.FullName;
        if (instanceDirectory is null || !Directory.Exists(instanceDirectory))
        {
            return Task.CompletedTask;
        }

        return Task.Run(() => Directory.Delete(instanceDirectory, recursive: true));
    }

    private async Task DebouncedReloadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(350, cancellationToken);
            await LoadCatalogAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ReloadAsync()
    {
        searchCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        searchCancellation = cancellation;
        await LoadCatalogAsync(cancellation.Token);
    }

    private async Task LoadCatalogAsync(CancellationToken cancellationToken)
    {
        var revision = Interlocked.Increment(ref loadRevision);
        var selectedInstance = SelectedInstance;
        var selectedCategory = SelectedCategory;
        var isStandaloneModpack = selectedCategory.Type == CatalogContentType.Modpacks;
        if (!isStandaloneModpack && selectedInstance is null)
        {
            relevanceOrder.Clear();
            VisibleContent = [];
            OnPropertyChanged(nameof(ResultCountText));
            StatusText = "Select an instance to see compatible content.";
            return;
        }

        var request = new CatalogSearchRequest(
            SearchQuery,
            selectedCategory.Type,
            isStandaloneModpack ? string.Empty : selectedInstance!.Version,
            isStandaloneModpack ? string.Empty : selectedInstance!.LoaderType,
            40);
        var cacheKey = CreateCacheKey(request);

        if (TryGetCachedResult(cacheKey, out var cachedResult))
        {
            ApplyCatalogResult(cachedResult);
            IsBusy = false;
            if (selectedInstance is not null)
            {
                StartCatalogWarmup(selectedInstance, selectedCategory.Type);
            }
            return;
        }

        IsBusy = true;
        StatusText = isStandaloneModpack
            ? "Loading standalone modpacks for the latest Minecraft releases..."
            : $"Loading {selectedInstance!.Version} {selectedInstance.LoaderType} compatible content...";
        try
        {
            var result = await GetOrCreateSearchAsync(cacheKey, request).WaitAsync(cancellationToken);
            if (revision != loadRevision)
            {
                return;
            }

            ApplyCatalogResult(result);
            if (selectedInstance is not null)
            {
                StartCatalogWarmup(selectedInstance, selectedCategory.Type);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            relevanceOrder.Clear();
            VisibleContent = [];
            OnPropertyChanged(nameof(ResultCountText));
            StatusText = $"Could not load catalog: {exception.Message}";
        }
        finally
        {
            if (revision == loadRevision)
            {
                IsBusy = false;
            }
        }
    }

    private Task<CatalogSearchResult> GetOrCreateSearchAsync(
        CatalogCacheKey cacheKey,
        CatalogSearchRequest request)
    {
        if (TryGetCachedResult(cacheKey, out var cachedResult))
        {
            return Task.FromResult(cachedResult);
        }

        if (inFlightSearches.TryGetValue(cacheKey, out var inFlightSearch))
        {
            return inFlightSearch;
        }

        var search = SearchAndCacheAsync(cacheKey, request);
        inFlightSearches[cacheKey] = search;
        return search;
    }

    private async Task<CatalogSearchResult> SearchAndCacheAsync(
        CatalogCacheKey cacheKey,
        CatalogSearchRequest request)
    {
        try
        {
            var result = await Task.Run(() => catalogService.SearchAsync(request, CancellationToken.None));
            catalogCache[cacheKey] = new CatalogCacheEntry(result, DateTimeOffset.UtcNow);
            return result;
        }
        finally
        {
            inFlightSearches.Remove(cacheKey);
        }
    }

    private bool TryGetCachedResult(CatalogCacheKey cacheKey, out CatalogSearchResult result)
    {
        if (catalogCache.TryGetValue(cacheKey, out var entry))
        {
            if (DateTimeOffset.UtcNow - entry.StoredAt <= CatalogCacheLifetime)
            {
                result = entry.Result;
                return true;
            }

            catalogCache.Remove(cacheKey);
        }

        result = null!;
        return false;
    }

    private void ApplyCatalogResult(CatalogSearchResult result)
    {
        relevanceOrder.Clear();
        foreach (var project in result.Projects)
        {
            relevanceOrder.Add(new ContentItem { Project = project });
        }

        ApplyCurrentSort();
        SourcesText = result.SourcesText;
        StatusText = result.Notices.Count > 0
            ? string.Join("  ", result.Notices)
            : $"{VisibleContent.Count} compatible projects · duplicates merged";
    }

    private void StartCatalogWarmup(GameProfile instance, CatalogContentType activeCategory)
    {
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            return;
        }

        var warmupKey = $"{instance.InstancePath}|{instance.Version}|{instance.LoaderType}";
        if (!warmedCatalogs.Add(warmupKey))
        {
            return;
        }

        _ = WarmCatalogAsync(instance, activeCategory);
    }

    private Task WarmCatalogAsync(GameProfile instance, CatalogContentType activeCategory) =>
        Task.WhenAll(Categories
            .Where(category => category.Type != activeCategory)
            .Select(category => WarmCategoryAsync(instance, category.Type)));

    private async Task WarmCategoryAsync(GameProfile instance, CatalogContentType category)
    {
        var isStandaloneModpack = category == CatalogContentType.Modpacks;
        var request = new CatalogSearchRequest(
            string.Empty,
            category,
            isStandaloneModpack ? string.Empty : instance.Version,
            isStandaloneModpack ? string.Empty : instance.LoaderType,
            40);

        try
        {
            await GetOrCreateSearchAsync(CreateCacheKey(request), request);
        }
        catch
        {
        }
    }

    private static CatalogCacheKey CreateCacheKey(CatalogSearchRequest request) => new(
        request.Query.Trim().ToLowerInvariant(),
        request.ContentType,
        request.GameVersion.Trim().ToLowerInvariant(),
        request.Loader.Trim().ToLowerInvariant(),
        request.Limit);

    private void ApplyCurrentSort()
    {
        var source = relevanceOrder.Count > 0
            ? relevanceOrder
            : VisibleContent.ToList();
        IEnumerable<ContentItem> sorted = SelectedSortOption switch
        {
            "Most downloaded" => source.OrderByDescending(item => item.Project.Downloads),
            "Most followed" => source.OrderByDescending(item => item.Project.Followers),
            "Recently updated" => source.OrderByDescending(item => item.Project.UpdatedAt),
            _ => source
        };

        VisibleContent = new ObservableCollection<ContentItem>(sorted);

        OnPropertyChanged(nameof(ResultCountText));
    }

    private void OnSharedInstanceSelectionChanged()
    {
        if (!ReferenceEquals(SelectedInstance, InstanceStore.SelectedProfile))
        {
            SelectedInstance = InstanceStore.SelectedProfile;
        }
    }

    private readonly record struct CatalogCacheKey(
        string Query,
        CatalogContentType ContentType,
        string GameVersion,
        string Loader,
        int Limit);

    private sealed record CatalogCacheEntry(CatalogSearchResult Result, DateTimeOffset StoredAt);
}
