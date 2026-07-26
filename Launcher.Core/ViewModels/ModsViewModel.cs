using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Launcher.Core.ViewModels;

public partial class ContentItem : ObservableObject
{
    public string Name { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Accent { get; init; } = "#1E58A5";
    public string Mark { get; init; } = "UB";
    public string Tags { get; init; } = string.Empty;
    public string Downloads { get; init; } = string.Empty;
    public string Likes { get; init; } = string.Empty;
    public string Updated { get; init; } = string.Empty;

    [ObservableProperty]
    private bool _isInstalled;

    public string ActionLabel => IsInstalled ? "Installed" : "+  Install";

    partial void OnIsInstalledChanged(bool value) => OnPropertyChanged(nameof(ActionLabel));
}

public partial class ContentCategory : ObservableObject
{
    public string Name { get; init; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}

public partial class ModsViewModel : ViewModelBase
{
    private readonly ObservableCollection<ContentItem> _allContent;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _selectedCategory = "Modpacks";

    public IReadOnlyList<ContentCategory> Categories { get; } =
    [
        new() { Name = "Modpacks", IsSelected = true },
        new() { Name = "Mods" },
        new() { Name = "Resource Packs" },
        new() { Name = "Data Packs" },
        new() { Name = "Shaders" },
        new() { Name = "Servers" }
    ];

    public IReadOnlyList<ContentItem> VisibleContent => _allContent
        .Where(item =>
            (SelectedCategory == "Servers" || item.Category == SelectedCategory) &&
            (string.IsNullOrWhiteSpace(SearchQuery) ||
             item.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
             item.Author.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
             item.Tags.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)))
        .ToList();

    public ModsViewModel()
    {
        _allContent = new ObservableCollection<ContentItem>
        {
            new() { Name = "Fabulously Optimized", Author = "Fabulously Optimized", Description = "Beautiful graphics, speedy performance and familiar features in a simple package.", Category = "Modpacks", Accent = "#D8D1B1", Mark = "FO", Tags = "Client   Lightweight   Multiplayer   Optimization   Fabric", Downloads = "14.96M", Likes = "4,647", Updated = "9 hours ago" },
            new() { Name = "Zombie Invade 100 Days", Author = "FlameFire", Description = "Survive one hundred days in a world overrun by the zombie apocalypse.", Category = "Modpacks", Accent = "#AE3030", Mark = "100", Tags = "Client and server   Challenging   Combat   Forge", Downloads = "12.18M", Likes = "577", Updated = "7 months ago" },
            new() { Name = "Cobblemon Official Modpack [Fabric]", Author = "Cobbled Studios", Description = "The official modpack of the Cobblemon mod, built for Fabric.", Category = "Modpacks", Accent = "#715368", Mark = "CO", Tags = "Client and server   Adventure   Lightweight   Multiplayer   Fabric", Downloads = "9.08M", Likes = "2,480", Updated = "6 months ago" },
            new() { Name = "COBBLEVERSE - Pokemon Adventure", Author = "LUMYVERSE", Description = "Start a Minecraft Pokemon adventure with quests, battles, and more.", Category = "Modpacks", Accent = "#2D568E", Mark = "CV", Tags = "Client and server   Adventure   Multiplayer   Optimization   Fabric", Downloads = "5.39M", Likes = "1,740", Updated = "5 days ago" },
            new() { Name = "Better MC [FABRIC] - BMC2", Author = "Luna Pixel Studios", Description = "A proper vanilla-plus modpack for a bigger Minecraft experience.", Category = "Modpacks", Accent = "#217D86", Mark = "BMC", Tags = "Client and server   Adventure   Combat   Optimization   Fabric", Downloads = "2.84M", Likes = "1,320", Updated = "2 weeks ago" },
            new() { Name = "Sodium Plus", Author = "HappyRedstone Modding", Description = "A client-side optimization modpack with a few extra tweaks.", Category = "Modpacks", Accent = "#68B949", Mark = "S+", Tags = "Client   Optimization   Fabric", Downloads = "2.36M", Likes = "589", Updated = "3 days ago" },
            new() { Name = "Sodium", Author = "CaffeineMC", Description = "Modern rendering engine replacement that improves frame rates.", Category = "Mods", Accent = "#3E78C3", Mark = "Na", Tags = "Client   Optimization   Fabric", Downloads = "70.4M", Likes = "12,390", Updated = "1 day ago" },
            new() { Name = "Complementary Shaders", Author = "Complementary", Description = "A high-quality shader pack with thoughtful visual settings.", Category = "Shaders", Accent = "#8B57B7", Mark = "CS", Tags = "Shaders   Graphics   Iris", Downloads = "18.1M", Likes = "8,920", Updated = "6 days ago" },
            new() { Name = "Fresh Animations", Author = "FreshLX", Description = "Subtle, polished mob animations for your Minecraft world.", Category = "Resource Packs", Accent = "#CF8652", Mark = "FA", Tags = "Resource Pack   Animation", Downloads = "10.7M", Likes = "4,110", Updated = "4 days ago" },
            new() { Name = "Terralith", Author = "Stardust Labs", Description = "Overhauls Minecraft world generation with beautiful biomes.", Category = "Data Packs", Accent = "#4E9E77", Mark = "TL", Tags = "World Generation   Adventure", Downloads = "8.5M", Likes = "3,810", Updated = "1 week ago" }
        };
    }

    partial void OnSearchQueryChanged(string value) => OnPropertyChanged(nameof(VisibleContent));
    partial void OnSelectedCategoryChanged(string value) => OnPropertyChanged(nameof(VisibleContent));

    [RelayCommand]
    private void SelectCategory(ContentCategory category)
    {
        foreach (var item in Categories)
            item.IsSelected = item == category;

        SelectedCategory = category.Name;
    }

    [RelayCommand]
    private void Install(ContentItem item)
    {
        item.IsInstalled = !item.IsInstalled;
    }
}
