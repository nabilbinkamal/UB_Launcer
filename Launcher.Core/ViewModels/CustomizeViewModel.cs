using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Services;

namespace Launcher.Core.ViewModels;

public partial class SkinLibraryItem : ObservableObject
{
    public string Name { get; init; } = string.Empty;
    public string ImagePath { get; init; } = string.Empty;
    public bool IsSlim { get; init; }

    [JsonIgnore]
    [property: JsonIgnore]
    [ObservableProperty]
    private bool _isSelected;
}

public partial class CapeLibraryItem : ObservableObject
{
    public string Name { get; init; } = string.Empty;
    public string ImagePath { get; init; } = string.Empty;

    [property: JsonIgnore]
    [ObservableProperty]
    private bool _isSelected;
}

public partial class CustomizeViewModel : ViewModelBase
{
    private const string DefaultStevePath = "avares://Launcher.UI/Assets/skins/steve.png";
    private static readonly string CosmeticsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UBLauncher", "cosmetics");
    private static readonly string SavedSkinsDirectory = Path.Combine(CosmeticsDirectory, "saved-skins");
    private static readonly string SavedSkinsManifestPath = Path.Combine(CosmeticsDirectory, "saved-skins.json");
    private static readonly string SavedCapesDirectory = Path.Combine(CosmeticsDirectory, "saved-capes");
    private static readonly string SavedCapesManifestPath = Path.Combine(CosmeticsDirectory, "saved-capes.json");
    private static readonly string SavedCapePath = Path.Combine(CosmeticsDirectory, "cape.png");

    [ObservableProperty] private string _skinPath = DefaultStevePath;
    [ObservableProperty] private string _capePath = File.Exists(SavedCapePath) ? SavedCapePath : string.Empty;
    [ObservableProperty] private bool _useSlimModel;
    [ObservableProperty] private string _selectedSkinName = "Steve";
    [ObservableProperty] private string _statusText = "Steve is selected as the default preview.";

    public ObservableCollection<SkinLibraryItem> SavedSkins { get; } = [];
    public ObservableCollection<CapeLibraryItem> SavedCapes { get; } = [];

    public ObservableCollection<SkinLibraryItem> DefaultSkins { get; } =
    [
        new() { Name = "Steve", ImagePath = "avares://Launcher.UI/Assets/skins/steve.png" },
        new() { Name = "Alex", ImagePath = "avares://Launcher.UI/Assets/skins/alex.png", IsSlim = true },
        new() { Name = "Ari", ImagePath = "avares://Launcher.UI/Assets/skins/ari.png", IsSlim = true },
        new() { Name = "Efe", ImagePath = "avares://Launcher.UI/Assets/skins/efe.png", IsSlim = true },
        new() { Name = "Kai", ImagePath = "avares://Launcher.UI/Assets/skins/kai.png", IsSlim = true },
        new() { Name = "Makena", ImagePath = "avares://Launcher.UI/Assets/skins/makena.png", IsSlim = true },
        new() { Name = "Noor", ImagePath = "avares://Launcher.UI/Assets/skins/noor.png", IsSlim = true },
        new() { Name = "Sunny", ImagePath = "avares://Launcher.UI/Assets/skins/sunny.png", IsSlim = true },
        new() { Name = "Zuri", ImagePath = "avares://Launcher.UI/Assets/skins/zuri.png", IsSlim = true }
    ];

    public string Username => string.IsNullOrWhiteSpace(OfflineAccountStore.Username)
        ? "Player"
        : OfflineAccountStore.Username;

    public CustomizeViewModel()
    {
        LoadSavedSkins();
        LoadSavedCapes();
        DefaultSkins[0].IsSelected = true;
    }

    public void ImportSkin(string selectedPath, bool isSlim)
    {
        if (!File.Exists(selectedPath))
        {
            StatusText = "Select a valid skin PNG.";
            return;
        }

        var savedSkin = SaveSkinFile(
            selectedPath,
            Path.GetFileNameWithoutExtension(selectedPath),
            isSlim);
        ActivateSkin(savedSkin);
        StatusText = $"{savedSkin.Name} added to Saved skins.";
    }

    public void ImportCape(string selectedPath)
    {
        if (!File.Exists(selectedPath))
        {
            StatusText = "Select a valid cape PNG.";
            return;
        }

        CapePath = selectedPath;
        SaveCape();
        StatusText = $"{Path.GetFileNameWithoutExtension(selectedPath)} added to Saved capes.";
    }

    [RelayCommand]
    private void SelectDefaultSkin(SkinLibraryItem skin)
    {
        SkinPath = skin.ImagePath;
        SelectedSkinName = skin.Name;
        UseSlimModel = skin.IsSlim;
        SetSelectedSkin(skin);
        StatusText = $"{skin.Name} selected from default skins.";
    }

    [RelayCommand]
    private void SelectSavedSkin(SkinLibraryItem skin)
    {
        SkinPath = skin.ImagePath;
        SelectedSkinName = skin.Name;
        UseSlimModel = skin.IsSlim;
        SetSelectedSkin(skin);
        StatusText = $"{skin.Name} selected from saved skins.";
    }

    [RelayCommand]
    private void DeleteSavedSkin(SkinLibraryItem skin)
    {
        if (skin.IsSelected)
        {
            StatusText = "Select another skin before deleting this one.";
            return;
        }

        SavedSkins.Remove(skin);
        DeleteSavedSkinFile(skin.ImagePath);
        SaveSkinHistory();
        StatusText = $"{skin.Name} deleted from saved skins.";
    }

    [RelayCommand]
    private void SelectSavedCape(CapeLibraryItem cape)
    {
        foreach (var savedCape in SavedCapes)
            savedCape.IsSelected = ReferenceEquals(savedCape, cape);

        CapePath = cape.ImagePath;
        StatusText = $"{cape.Name} selected from saved capes.";
    }

    [RelayCommand]
    private void ApplyCosmetics()
    {
        if (SkinPath.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
        {
            var defaultSkin = DefaultSkins.First(skin =>
                skin.ImagePath.Equals(SkinPath, StringComparison.OrdinalIgnoreCase));
            AddToSavedSkins(new SkinLibraryItem
            {
                Name = defaultSkin.Name,
                ImagePath = defaultSkin.ImagePath,
                IsSlim = defaultSkin.IsSlim
            });
            SetSelectedSkin(defaultSkin);
            SaveCape();
            StatusText = $"{SelectedSkinName} saved and applied.";
            return;
        }

        if (!File.Exists(SkinPath))
        {
            StatusText = "Select a valid skin PNG before applying cosmetics.";
            return;
        }

        var sourcePath = Path.GetFullPath(SkinPath);
        var currentSavedSkin = SavedSkins.FirstOrDefault(skin =>
            !skin.ImagePath.StartsWith("avares://", StringComparison.OrdinalIgnoreCase)
            && Path.GetFullPath(skin.ImagePath).Equals(sourcePath, StringComparison.OrdinalIgnoreCase));
        var savedSkin = SaveSkinFile(
            sourcePath,
            currentSavedSkin?.Name ?? Path.GetFileNameWithoutExtension(sourcePath),
            UseSlimModel);
        ActivateSkin(savedSkin);
        SaveCape();
        StatusText = $"{SelectedSkinName} saved and applied.";
    }

    [RelayCommand]
    private void ResetCosmetics()
    {
        SkinPath = DefaultStevePath;
        CapePath = string.Empty;
        SelectedSkinName = "Steve";
        UseSlimModel = false;
        foreach (var cape in SavedCapes)
            cape.IsSelected = false;
        SetSelectedSkin(DefaultSkins[0]);
        if (File.Exists(SavedCapePath)) File.Delete(SavedCapePath);
        StatusText = "Reset to the default Steve preview.";
    }

    private void LoadSavedSkins()
    {
        if (File.Exists(SavedSkinsManifestPath))
        {
            try
            {
                var savedSkins = JsonSerializer.Deserialize<List<SkinLibraryItem>>(
                    File.ReadAllText(SavedSkinsManifestPath)) ?? [];
                foreach (var skin in savedSkins.Where(IsAvailableSkin))
                    SavedSkins.Add(skin);
                return;
            }
            catch (JsonException)
            {
                File.Delete(SavedSkinsManifestPath);
            }
        }

        if (!Directory.Exists(SavedSkinsDirectory)) return;
        foreach (var skinPath in Directory.EnumerateFiles(SavedSkinsDirectory, "*.png")
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            SavedSkins.Add(new SkinLibraryItem
            {
                Name = $"Saved {SavedSkins.Count + 1}",
                ImagePath = skinPath
            });
        }
        SaveSkinHistory();
    }

    private void LoadSavedCapes()
    {
        if (File.Exists(SavedCapesManifestPath))
        {
            try
            {
                var savedCapes = JsonSerializer.Deserialize<List<CapeLibraryItem>>(
                    File.ReadAllText(SavedCapesManifestPath)) ?? [];
                var activeCapeHash = File.Exists(SavedCapePath)
                    ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(SavedCapePath))).ToLowerInvariant()
                    : string.Empty;
                foreach (var cape in savedCapes.Where(IsAvailableCape))
                {
                    cape.IsSelected = cape.ImagePath.Equals(SavedCapePath, StringComparison.OrdinalIgnoreCase)
                        || Path.GetFileNameWithoutExtension(cape.ImagePath)
                            .Equals(activeCapeHash, StringComparison.OrdinalIgnoreCase);
                    SavedCapes.Add(cape);
                }
                return;
            }
            catch (JsonException)
            {
                File.Delete(SavedCapesManifestPath);
            }
        }

        if (!Directory.Exists(SavedCapesDirectory)) return;
        foreach (var capePath in Directory.EnumerateFiles(SavedCapesDirectory, "*.png")
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            SavedCapes.Add(new CapeLibraryItem
            {
                Name = $"Cape {SavedCapes.Count + 1}",
                ImagePath = capePath
            });
        }
        SaveCapeHistory();
    }

    private void AddToSavedSkins(SkinLibraryItem skin)
    {
        var existingSkin = SavedSkins.FirstOrDefault(savedSkin =>
            savedSkin.ImagePath.Equals(skin.ImagePath, StringComparison.OrdinalIgnoreCase));
        if (existingSkin is not null)
            SavedSkins.Remove(existingSkin);

        SavedSkins.Insert(0, skin);
        SaveSkinHistory();
    }

    private SkinLibraryItem SaveSkinFile(string sourcePath, string displayName, bool isSlim)
    {
        Directory.CreateDirectory(SavedSkinsDirectory);
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var skinHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fullSourcePath))).ToLowerInvariant();
        var savedPath = Path.Combine(SavedSkinsDirectory, $"{skinHash}.png");
        if (!fullSourcePath.Equals(Path.GetFullPath(savedPath), StringComparison.OrdinalIgnoreCase))
            File.Copy(fullSourcePath, savedPath, true);

        var existingSkin = SavedSkins.FirstOrDefault(skin =>
            skin.ImagePath.Equals(savedPath, StringComparison.OrdinalIgnoreCase));
        var savedSkin = new SkinLibraryItem
        {
            Name = existingSkin?.Name ?? displayName,
            ImagePath = savedPath,
            IsSlim = isSlim
        };
        AddToSavedSkins(savedSkin);
        return savedSkin;
    }

    private void ActivateSkin(SkinLibraryItem skin)
    {
        SkinPath = skin.ImagePath;
        SelectedSkinName = skin.Name;
        UseSlimModel = skin.IsSlim;
        SetSelectedSkin(skin);
    }

    private void SetSelectedSkin(SkinLibraryItem selectedSkin)
    {
        foreach (var skin in DefaultSkins)
            skin.IsSelected = ReferenceEquals(skin, selectedSkin);

        foreach (var skin in SavedSkins)
            skin.IsSelected = ReferenceEquals(skin, selectedSkin);
    }

    private static bool IsAvailableSkin(SkinLibraryItem skin)
    {
        return skin.ImagePath.StartsWith("avares://", StringComparison.OrdinalIgnoreCase)
            || File.Exists(skin.ImagePath);
    }

    private void SaveSkinHistory()
    {
        Directory.CreateDirectory(CosmeticsDirectory);
        File.WriteAllText(
            SavedSkinsManifestPath,
            JsonSerializer.Serialize(SavedSkins, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void DeleteSavedSkinFile(string imagePath)
    {
        if (imagePath.StartsWith("avares://", StringComparison.OrdinalIgnoreCase) || !File.Exists(imagePath))
            return;

        var savedSkinsRoot = Path.GetFullPath(SavedSkinsDirectory);
        var skinPath = Path.GetFullPath(imagePath);
        if (!skinPath.StartsWith(savedSkinsRoot, StringComparison.OrdinalIgnoreCase))
            return;

        File.Delete(skinPath);
    }

    private static bool IsAvailableCape(CapeLibraryItem cape)
    {
        return File.Exists(cape.ImagePath);
    }

    private void SaveCapeHistory()
    {
        Directory.CreateDirectory(CosmeticsDirectory);
        File.WriteAllText(
            SavedCapesManifestPath,
            JsonSerializer.Serialize(SavedCapes, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void SaveCape()
    {
        if (string.IsNullOrWhiteSpace(CapePath) || !File.Exists(CapePath)) return;

        Directory.CreateDirectory(CosmeticsDirectory);
        Directory.CreateDirectory(SavedCapesDirectory);

        var sourcePath = Path.GetFullPath(CapePath);
        var capeHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourcePath))).ToLowerInvariant();
        var savedPath = Path.Combine(SavedCapesDirectory, $"{capeHash}.png");
        if (!sourcePath.Equals(Path.GetFullPath(savedPath), StringComparison.OrdinalIgnoreCase))
            File.Copy(sourcePath, savedPath, true);

        if (!sourcePath.Equals(Path.GetFullPath(SavedCapePath), StringComparison.OrdinalIgnoreCase))
            File.Copy(sourcePath, SavedCapePath, true);

        var existingCape = SavedCapes.FirstOrDefault(cape =>
            cape.ImagePath.Equals(savedPath, StringComparison.OrdinalIgnoreCase));
        if (existingCape is null)
        {
            existingCape = new CapeLibraryItem
            {
                Name = Path.GetFileNameWithoutExtension(sourcePath),
                ImagePath = savedPath
            };
            SavedCapes.Insert(0, existingCape);
        }

        foreach (var cape in SavedCapes)
            cape.IsSelected = ReferenceEquals(cape, existingCape);

        CapePath = SavedCapePath;
        SaveCapeHistory();
    }
}
