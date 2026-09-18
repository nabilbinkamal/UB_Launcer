using System.Collections.ObjectModel;
using System.Net.Http;
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

    /// <summary>True for capes fetched from the Microsoft/Mojang profile (not user-imported files).</summary>
    [JsonIgnore]
    public bool IsOwned { get; init; }

    /// <summary>Remote texture URL (only set for owned capes; empty for local file capes).</summary>
    [JsonIgnore]
    public string TextureUrl { get; init; } = string.Empty;

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
    private string CosmeticsSelectionPath => Path.Combine(CosmeticsDirectory, $"selection_{Username}.json");
    private static readonly string OwnedCapesDirectory = Path.Combine(CosmeticsDirectory, "owned-capes");

    private static readonly HttpClient TextureClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    private sealed class CosmeticSelectionState
    {
        public string? SkinPath { get; set; }
        public string? CapePath { get; set; }
        public bool? UseSlimModel { get; set; }
    }

    [ObservableProperty] private string _skinPath = DefaultStevePath;
    [ObservableProperty] private string _capePath = File.Exists(SavedCapePath) ? SavedCapePath : string.Empty;
    [ObservableProperty] private bool _useSlimModel;
    [ObservableProperty] private string _selectedSkinName = "Steve";
    [ObservableProperty] private string _statusText = "Steve is selected as the default preview.";

    public ObservableCollection<SkinLibraryItem> SavedSkins { get; } = [];
    public ObservableCollection<CapeLibraryItem> SavedCapes { get; } = [];
    public ObservableCollection<CapeLibraryItem> MicrosoftOwnedCapes { get; } = [];

    [ObservableProperty]
    private bool _hasMicrosoftOwnedCapes;

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
        OfflineAccountStore.AccountChanged += RefreshUsername;
        MicrosoftAccountStore.SessionChanged += OnMicrosoftSessionChanged;
        LoadSavedSkins();
        LoadSavedCapes();
        RestoreSelection();
        _ = LoadMicrosoftOwnedCapesAsync();
        _ = AutoDetectPremiumSkinAsync();
    }

    private void RefreshUsername()
    {
        OnPropertyChanged(nameof(Username));
        RestoreSelection();
        _ = LoadMicrosoftOwnedCapesAsync();
        _ = AutoDetectPremiumSkinAsync();
    }

    private void OnMicrosoftSessionChanged() 
    {
        _ = LoadMicrosoftOwnedCapesAsync();
        _ = AutoDetectPremiumSkinAsync();
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
        SaveSelection();
        StatusText = $"{Path.GetFileNameWithoutExtension(selectedPath)} added to Saved capes.";
    }

    [RelayCommand]
    private void SelectDefaultSkin(SkinLibraryItem skin)
    {
        SkinPath = skin.ImagePath;
        SelectedSkinName = skin.Name;
        UseSlimModel = skin.IsSlim;
        SetSelectedSkin(skin);
        SaveSelection();
        StatusText = $"{skin.Name} selected from default skins.";
    }

    [RelayCommand]
    private void SelectSavedSkin(SkinLibraryItem skin)
    {
        SkinPath = skin.ImagePath;
        SelectedSkinName = skin.Name;
        UseSlimModel = skin.IsSlim;
        SetSelectedSkin(skin);
        SaveSelection();
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
        // Toggle: clicking the already-selected cape deselects it.
        if (cape.IsSelected)
        {
            cape.IsSelected = false;
            CapePath = string.Empty;
            // Also deselect any owned cape that might have been selected.
            foreach (var oc in MicrosoftOwnedCapes)
                oc.IsSelected = false;
            SaveSelection();
            StatusText = $"{cape.Name} deselected.";
            return;
        }

        // Deselect everything, then select the tapped cape.
        foreach (var c in SavedCapes) c.IsSelected = false;
        foreach (var c in MicrosoftOwnedCapes) c.IsSelected = false;
        cape.IsSelected = true;
        CapePath = cape.ImagePath;
        SaveSelection();
        StatusText = $"{cape.Name} selected from saved capes.";
    }

    [RelayCommand]
    private void SelectOwnedCape(CapeLibraryItem cape)
    {
        // Toggle: clicking the already-selected owned cape deselects it.
        if (cape.IsSelected)
        {
            cape.IsSelected = false;
            CapePath = string.Empty;
            foreach (var sc in SavedCapes) sc.IsSelected = false;
            SaveSelection();
            StatusText = $"{cape.Name} deselected.";
            return;
        }

        foreach (var c in SavedCapes) c.IsSelected = false;
        foreach (var c in MicrosoftOwnedCapes) c.IsSelected = false;
        cape.IsSelected = true;
        CapePath = cape.ImagePath;
        SaveSelection();
        StatusText = $"{cape.Name} selected from owned capes.";
    }

    private async Task AutoDetectPremiumSkinAsync()
    {
        if (!MicrosoftAccountStore.HasSession || !string.Equals(MicrosoftAccountStore.MinecraftUsername, OfflineAccountStore.Username, StringComparison.OrdinalIgnoreCase)) return;
        var uuid = MicrosoftAccountStore.MinecraftUuid;
        if (string.IsNullOrWhiteSpace(uuid)) return;

        // If the user explicitly saved a custom skin, don't overwrite it
        var selection = LoadSelection();
        if (selection is not null && !string.IsNullOrWhiteSpace(selection.SkinPath) && !selection.SkinPath.Equals(DefaultStevePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var profileUrl = $"https://sessionserver.mojang.com/session/minecraft/profile/{uuid}";
            var profileJson = await TextureClient.GetStringAsync(profileUrl);
            using var doc = JsonDocument.Parse(profileJson);
            
            var properties = doc.RootElement.GetProperty("properties");
            string? texturesBase64 = null;
            foreach (var prop in properties.EnumerateArray())
            {
                if (prop.GetProperty("name").GetString() == "textures")
                {
                    texturesBase64 = prop.GetProperty("value").GetString();
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(texturesBase64)) return;
            
            var texturesJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(texturesBase64));
            using var texDoc = JsonDocument.Parse(texturesJson);
            
            if (texDoc.RootElement.GetProperty("textures").TryGetProperty("SKIN", out var skinElement))
            {
                var skinUrl = skinElement.GetProperty("url").GetString();
                var isSlim = false;
                
                if (skinElement.TryGetProperty("metadata", out var metaElement) && 
                    metaElement.TryGetProperty("model", out var modelElement))
                {
                    isSlim = modelElement.GetString() == "slim";
                }
                
                if (string.IsNullOrWhiteSpace(skinUrl)) return;
                
                var skinBytes = await TextureClient.GetByteArrayAsync(skinUrl);
                var hash = Convert.ToHexString(SHA256.HashData(skinBytes)).ToLowerInvariant();
                
                Directory.CreateDirectory(CosmeticsDirectory);
                var localPath = Path.Combine(CosmeticsDirectory, $"auto_{hash}.png");
                
                if (!File.Exists(localPath))
                {
                    await File.WriteAllBytesAsync(localPath, skinBytes);
                }

                // Apply to UI (only if the user hasn't manually selected something else while this was downloading)
                var currentSelection = LoadSelection();
                if (currentSelection is null || string.IsNullOrWhiteSpace(currentSelection.SkinPath) || currentSelection.SkinPath.Equals(DefaultStevePath, StringComparison.OrdinalIgnoreCase))
                {
                    SkinPath = localPath;
                    SelectedSkinName = "Premium Skin";
                    UseSlimModel = isSlim;

                    // Deselect default skins in the UI
                    foreach (var s in DefaultSkins) s.IsSelected = false;
                    foreach (var s in SavedSkins) s.IsSelected = false;

                    SaveSelection();

                    StatusText = $"Detected premium skin for {MicrosoftAccountStore.MinecraftUsername}.";
                }
            }
        }
        catch 
        {
            // Ignore network errors, just fallback to Steve
        }
    }

    /// <summary>
    /// Downloads and caches cape textures from the stored Microsoft session,
    /// then populates <see cref="MicrosoftOwnedCapes"/>.
    /// Safe to call multiple times — rebuilds the list from scratch each time.
    /// </summary>
    private async Task LoadMicrosoftOwnedCapesAsync()
    {
        MicrosoftOwnedCapes.Clear();

        if (!MicrosoftAccountStore.HasSession || !string.Equals(MicrosoftAccountStore.MinecraftUsername, OfflineAccountStore.Username, StringComparison.OrdinalIgnoreCase))
        {
            HasMicrosoftOwnedCapes = false;
            return;
        }

        var capes = MicrosoftAccountStore.Capes;
        if (capes.Count == 0)
        {
            HasMicrosoftOwnedCapes = false;
            return;
        }

        Directory.CreateDirectory(OwnedCapesDirectory);

        foreach (var cape in capes)
        {
            var localPath = await DownloadCapeTextureAsync(cape.Id, cape.TextureUrl);
            if (localPath is null) continue;

            // Use the alias as the display name, falling back to the cape ID.
            var displayName = string.IsNullOrWhiteSpace(cape.Alias) ? cape.Id : cape.Alias;

            var item = new CapeLibraryItem
            {
                Name = displayName,
                ImagePath = localPath,
                IsOwned = true,
                TextureUrl = cape.TextureUrl
            };

            // Restore selection: if this cape's local file is the current CapePath, select it.
            item.IsSelected = !string.IsNullOrWhiteSpace(CapePath)
                && localPath.Equals(CapePath, StringComparison.OrdinalIgnoreCase);

            MicrosoftOwnedCapes.Add(item);
        }

        // If nothing was previously selected from owned capes but the active cape was
        // originally an owned one (matched by file content), restore the selection.
        if (MicrosoftOwnedCapes.All(c => !c.IsSelected) && File.Exists(CapePath))
        {
            foreach (var item in MicrosoftOwnedCapes)
            {
                if (!File.Exists(item.ImagePath)) continue;
                try
                {
                    var a = SHA256.HashData(File.ReadAllBytes(item.ImagePath));
                    var b = SHA256.HashData(File.ReadAllBytes(CapePath));
                    if (a.SequenceEqual(b))
                    {
                        item.IsSelected = true;
                        break;
                    }
                }
                catch { /* ignore */ }
            }
        }

        HasMicrosoftOwnedCapes = MicrosoftOwnedCapes.Count > 0;
    }

    /// <summary>
    /// Download a cape texture from <paramref name="textureUrl"/> and cache it under
    /// <see cref="OwnedCapesDirectory"/>/<paramref name="capeId"/>.png.
    /// Returns the local path, or null on failure.
    /// </summary>
    private static async Task<string?> DownloadCapeTextureAsync(string capeId, string textureUrl)
    {
        if (string.IsNullOrWhiteSpace(textureUrl)) return null;

        var safeName = string.Concat(capeId.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));
        if (string.IsNullOrWhiteSpace(safeName)) safeName = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(textureUrl))).ToLowerInvariant();

        var localPath = Path.Combine(OwnedCapesDirectory, $"{safeName}.png");

        // Use cached file if it is less than 24 hours old to avoid hammering the API.
        if (File.Exists(localPath) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(localPath)).TotalHours < 24)
            return localPath;

        try
        {
            var bytes = await TextureClient.GetByteArrayAsync(textureUrl);
            await File.WriteAllBytesAsync(localPath, bytes);
            return localPath;
        }
        catch
        {
            // If download fails but we have an old cached copy, use it.
            return File.Exists(localPath) ? localPath : null;
        }
    }

    [RelayCommand]
    private async Task ApplyCosmeticsAsync()
    {
        if (SkinPath.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
        {
            var defaultSkin = DefaultSkins.First(skin =>
                skin.ImagePath.Equals(SkinPath, StringComparison.OrdinalIgnoreCase));
            AddToSavedSkins(new SkinLibraryItem
            {
                Name = defaultSkin.Name,
                ImagePath = defaultSkin.ImagePath,
                IsSlim = UseSlimModel
            });
            SetSelectedSkin(defaultSkin);
            SaveCape();
            SaveSelection();
            
            await UploadSkinToMojangAsync(defaultSkin.ImagePath, UseSlimModel);
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
        SaveSelection();
        
        await UploadSkinToMojangAsync(sourcePath, UseSlimModel);
    }
    
    private async Task UploadSkinToMojangAsync(string skinPath, bool isSlim)
    {
        if (!MicrosoftAccountStore.HasSession || !string.Equals(MicrosoftAccountStore.MinecraftUsername, OfflineAccountStore.Username, StringComparison.OrdinalIgnoreCase))
        {
            StatusText = $"{SelectedSkinName} saved and applied locally.";
            return;
        }

        StatusText = $"{SelectedSkinName} saved. Uploading to Mojang...";
        var session = await MicrosoftAccountStore.GetLaunchSessionAsync();
        if (session is null)
        {
            StatusText = $"{SelectedSkinName} saved locally. Mojang upload failed (No session).";
            return;
        }

        try
        {
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(isSlim ? "slim" : "classic"), "variant");

            if (skinPath.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
            {
                var resetRequest = new HttpRequestMessage(HttpMethod.Delete, "https://api.minecraftservices.com/minecraft/profile/skins");
                resetRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.Token);
                var resetResponse = await TextureClient.SendAsync(resetRequest);
                if (resetResponse.IsSuccessStatusCode)
                {
                    StatusText = $"{SelectedSkinName} saved. Mojang skin reset to default successfully.";
                }
                else
                {
                    StatusText = $"{SelectedSkinName} saved locally, but failed to reset Mojang skin (HTTP {(int)resetResponse.StatusCode}).";
                }
                return;
            }

            var skinBytes = await File.ReadAllBytesAsync(skinPath);

            var fileContent = new ByteArrayContent(skinBytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            content.Add(fileContent, "file", "skin.png");

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.minecraftservices.com/minecraft/profile/skins")
            {
                Content = content
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.Token);

            var response = await TextureClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                StatusText = $"{SelectedSkinName} saved and applied to Mojang successfully.";
            }
            else
            {
                StatusText = $"{SelectedSkinName} saved locally, but failed to upload to Mojang (HTTP {(int)response.StatusCode}).";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"{SelectedSkinName} saved locally. Error uploading to Mojang: {ex.Message}";
        }
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
        foreach (var cape in MicrosoftOwnedCapes)
            cape.IsSelected = false;
        SetSelectedSkin(DefaultSkins[0]);
        if (File.Exists(SavedCapePath)) File.Delete(SavedCapePath);
        SaveSelection();
        StatusText = "Reset to the default Steve preview.";
    }

    private void RestoreSelection()
    {
        var selection = LoadSelection();
        var selectedSkin = selection is not null
            ? DefaultSkins.Concat(SavedSkins).FirstOrDefault(skin =>
                skin.ImagePath.Equals(selection.SkinPath, StringComparison.OrdinalIgnoreCase)
                && IsAvailableSkin(skin))
            : null;

        selectedSkin ??= DefaultSkins[0];
        SkinPath = selectedSkin.ImagePath;
        SelectedSkinName = selectedSkin.Name;
        UseSlimModel = selection?.UseSlimModel ?? selectedSkin.IsSlim;
        SetSelectedSkin(selectedSkin);

        var selectedCapePath = selection?.CapePath;
        if (string.IsNullOrWhiteSpace(selectedCapePath) || !File.Exists(selectedCapePath))
            selectedCapePath = File.Exists(SavedCapePath) ? SavedCapePath : string.Empty;

        CapePath = selectedCapePath;
        SetSelectedCape(selectedCapePath);
    }

    private CosmeticSelectionState? LoadSelection()
    {
        if (!File.Exists(CosmeticsSelectionPath)) return null;

        try
        {
            return JsonSerializer.Deserialize<CosmeticSelectionState>(
                File.ReadAllText(CosmeticsSelectionPath));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void SaveSelection()
    {
        Directory.CreateDirectory(CosmeticsDirectory);
        File.WriteAllText(
            CosmeticsSelectionPath,
            JsonSerializer.Serialize(new CosmeticSelectionState
            {
                SkinPath = SkinPath,
                CapePath = CapePath,
                UseSlimModel = UseSlimModel
            }, new JsonSerializerOptions { WriteIndented = true }));
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
        SaveSelection();
    }

    private void SetSelectedSkin(SkinLibraryItem selectedSkin)
    {
        foreach (var skin in DefaultSkins)
            skin.IsSelected = ReferenceEquals(skin, selectedSkin);

        foreach (var skin in SavedSkins)
            skin.IsSelected = ReferenceEquals(skin, selectedSkin);
    }

    private void SetSelectedCape(string? selectedCapePath)
    {
        foreach (var cape in SavedCapes)
        {
            cape.IsSelected = !string.IsNullOrWhiteSpace(selectedCapePath)
                && (cape.ImagePath.Equals(selectedCapePath, StringComparison.OrdinalIgnoreCase)
                    || (File.Exists(cape.ImagePath)
                        && File.Exists(selectedCapePath)
                        && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(cape.ImagePath)))
                            .Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(selectedCapePath))), StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var cape in MicrosoftOwnedCapes)
        {
            cape.IsSelected = !string.IsNullOrWhiteSpace(selectedCapePath)
                && cape.ImagePath.Equals(selectedCapePath, StringComparison.OrdinalIgnoreCase);
        }
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
