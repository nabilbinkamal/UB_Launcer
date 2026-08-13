using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Minecraft.ModManager;

public enum CatalogContentType
{
    Modpacks,
    Mods,
    Plugins,
    ResourcePacks,
    DataPacks,
    Shaders
}

public enum CatalogSource
{
    Modrinth,
    Smithed
}

public sealed record CatalogSearchRequest(
    string Query,
    CatalogContentType ContentType,
    string GameVersion,
    string Loader,
    int Limit = 40);

public sealed record InstanceInstallTarget(
    string Name,
    string GameVersion,
    string Loader,
    string RootPath);

public sealed record CatalogInstallProgress(double Percent, string Message);

public sealed record CatalogInstallResult(
    string FilePath,
    string GameVersion,
    string Loader,
    string Message);

public sealed class CatalogProject
{
    public required string Name { get; set; }
    public required string Author { get; set; }
    public required string Description { get; set; }
    public required CatalogContentType ContentType { get; set; }
    public required string Slug { get; set; }
    public required string IconUrl { get; set; }
    public required string Accent { get; set; }
    public required string Mark { get; set; }
    public required string Tags { get; set; }
    public required long Downloads { get; set; }
    public required long Followers { get; set; }
    public required DateTimeOffset UpdatedAt { get; set; }
    public Dictionary<CatalogSource, string> SourceIds { get; } = new();

    public string StoreLabel => string.Join(" + ", SourceIds.Keys.OrderBy(source => source).Select(GetSourceName));

    private static string GetSourceName(CatalogSource source) => source switch
    {
        CatalogSource.Modrinth => "Modrinth",
        CatalogSource.Smithed => "Smithed",
        _ => source.ToString()
    };
}

public sealed class CatalogSearchResult
{
    public required IReadOnlyList<CatalogProject> Projects { get; init; }
    public required string SourcesText { get; init; }
    public required IReadOnlyList<string> Notices { get; init; }
}

public sealed class UnifiedContentCatalogService
{
    private const string ModrinthBaseUrl = "https://api.modrinth.com/v2";
    private const string SmithedBaseUrl = "https://api.smithed.dev/v2";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient httpClient;

    public UnifiedContentCatalogService(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        if (!this.httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            this.httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("UBLauncher", "0.1"));
        }
    }

    public async Task<CatalogSearchResult> SearchAsync(
        CatalogSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var notices = new List<string>();
        var projects = new List<CatalogProject>();

        var modrinthTask = SearchModrinthAsync(request, cancellationToken);
        Task<IReadOnlyList<CatalogProject>>? smithedTask = request.ContentType is
            CatalogContentType.ResourcePacks or CatalogContentType.DataPacks
            ? SearchSmithedAsync(request, cancellationToken)
            : null;

        try
        {
            projects.AddRange(await modrinthTask);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            notices.Add($"Modrinth unavailable: {exception.Message}");
        }

        if (smithedTask is not null)
        {
            try
            {
                projects.AddRange(await smithedTask);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                notices.Add($"Smithed unavailable: {exception.Message}");
            }
        }

        var allDeduplicated = Deduplicate(projects)
            .OrderByDescending(project => project.Downloads)
            .ToList();
        var reservedSmithedCount = request.ContentType is CatalogContentType.ResourcePacks or CatalogContentType.DataPacks
            ? Math.Min(10, Math.Max(1, request.Limit / 4))
            : 0;
        var reservedSmithed = allDeduplicated
            .Where(project => project.SourceIds.ContainsKey(CatalogSource.Smithed))
            .Take(reservedSmithedCount)
            .ToList();
        var deduplicated = reservedSmithed
            .Concat(allDeduplicated.Where(project => !reservedSmithed.Contains(project)))
            .Take(request.Limit)
            .OrderByDescending(project => project.Downloads)
            .ToList();

        var sourceNames = deduplicated
            .SelectMany(project => project.SourceIds.Keys)
            .Distinct()
            .OrderBy(source => source)
            .Select(source => source == CatalogSource.Modrinth ? "Modrinth" : "Smithed")
            .ToList();

        return new CatalogSearchResult
        {
            Projects = deduplicated,
            SourcesText = sourceNames.Count == 0 ? "No catalog source available" : string.Join(" + ", sourceNames),
            Notices = notices
        };
    }

    public async Task<CatalogInstallResult> InstallAsync(
        CatalogProject project,
        InstanceInstallTarget target,
        IProgress<CatalogInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(target.RootPath);

        if (project.SourceIds.TryGetValue(CatalogSource.Modrinth, out var modrinthId))
        {
            return await InstallModrinthAsync(project, modrinthId, target, progress, cancellationToken);
        }

        if (project.SourceIds.TryGetValue(CatalogSource.Smithed, out var smithedId))
        {
            return await InstallSmithedAsync(project, smithedId, target, progress, cancellationToken);
        }

        throw new InvalidOperationException("No installable source is available for this project.");
    }

    public async Task<CatalogInstallResult> InstallStandaloneModpackAsync(
        CatalogProject project,
        InstanceInstallTarget target,
        IProgress<CatalogInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (project.ContentType != CatalogContentType.Modpacks)
        {
            throw new InvalidOperationException("Standalone installation is only available for modpacks.");
        }

        if (!project.SourceIds.TryGetValue(CatalogSource.Modrinth, out var projectId))
        {
            throw new InvalidOperationException("This modpack does not provide a standalone Modrinth package.");
        }

        Directory.CreateDirectory(target.RootPath);
        progress?.Report(new CatalogInstallProgress(0, "Finding the latest compatible Minecraft release..."));
        var url = $"{ModrinthBaseUrl}/project/{Uri.EscapeDataString(projectId)}/version?include_changelog=false";
        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var versions = await JsonSerializer.DeserializeAsync<List<ModrinthVersion>>(
            stream,
            JsonOptions,
            cancellationToken) ?? [];
        var installableVersions = versions
            .Where(version => GetPrimaryPackFile(version) is not null)
            .ToList();
        var releaseVersions = installableVersions
            .Where(version => string.Equals(version.VersionType, "release", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var selectedVersion = (releaseVersions.Count > 0 ? releaseVersions : installableVersions)
            .OrderByDescending(version => GetHighestGameVersion(version.GameVersions))
            .ThenByDescending(version => version.DatePublished)
            .FirstOrDefault() ??
            throw new InvalidOperationException("No installable Modrinth modpack release was found.");
        var packFile = GetPrimaryPackFile(selectedVersion)!;
        var resolvedTarget = target with
        {
            GameVersion = GetLatestGameVersionName(selectedVersion.GameVersions) ?? target.GameVersion,
            Loader = GetVersionLoader(selectedVersion.Loaders) ?? target.Loader
        };

        return await InstallModrinthPackAsync(
            project,
            packFile,
            resolvedTarget,
            progress,
            cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetModpackGameVersionsAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return [];
        }

        var url = $"{ModrinthBaseUrl}/project/{Uri.EscapeDataString(projectId)}/version?include_changelog=false";
        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var versions = await JsonSerializer.DeserializeAsync<List<ModrinthVersion>>(
            stream,
            JsonOptions,
            cancellationToken) ?? [];

        return versions
            .Where(version => GetPrimaryPackFile(version) is not null)
            .SelectMany(version => version.GameVersions ?? [])
            .Where(version => ParseMinecraftVersion(version) > new Version(0, 0))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(ParseMinecraftVersion)
            .ToList();
    }

    private async Task<IReadOnlyList<CatalogProject>> SearchModrinthAsync(
        CatalogSearchRequest request,
        CancellationToken cancellationToken)
    {
        var facets = BuildModrinthFacets(request);
        var query = new StringBuilder($"{ModrinthBaseUrl}/search?limit={Math.Clamp(request.Limit, 1, 100)}&index=downloads");
        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            query.Append("&query=").Append(Uri.EscapeDataString(request.Query.Trim()));
        }

        query.Append("&facets=").Append(Uri.EscapeDataString(JsonSerializer.Serialize(facets)));
        using var response = await httpClient.GetAsync(query.ToString(), cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await JsonSerializer.DeserializeAsync<ModrinthSearchResponse>(stream, JsonOptions, cancellationToken);

        return (result?.Hits ?? [])
            .Select(hit => MapModrinthProject(hit, request.ContentType))
            .ToList();
    }

    private async Task<IReadOnlyList<CatalogProject>> SearchSmithedAsync(
        CatalogSearchRequest request,
        CancellationToken cancellationToken)
    {
        var url = new StringBuilder($"{SmithedBaseUrl}/packs?limit={Math.Clamp(request.Limit, 1, 100)}")
            .Append("&version=").Append(Uri.EscapeDataString(request.GameVersion))
            .Append("&scope=data.display&scope=data.versions&scope=meta");
        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            url.Append("&search=").Append(Uri.EscapeDataString(request.Query.Trim()));
        }

        using var response = await httpClient.GetAsync(url.ToString(), cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await JsonSerializer.DeserializeAsync<List<SmithedPack>>(stream, JsonOptions, cancellationToken);
        var output = new List<CatalogProject>();

        foreach (var pack in result ?? [])
        {
            var compatibleVersions = pack.Data?.Versions?
                .Where(version => version.Supports?.Contains(request.GameVersion, StringComparer.OrdinalIgnoreCase) == true)
                .ToList() ?? [];
            var hasRequestedDownload = compatibleVersions.Any(version => request.ContentType switch
            {
                CatalogContentType.DataPacks => !string.IsNullOrWhiteSpace(version.Downloads?.DataPack),
                CatalogContentType.ResourcePacks => !string.IsNullOrWhiteSpace(version.Downloads?.ResourcePack),
                _ => false
            });
            if (!hasRequestedDownload)
            {
                continue;
            }

            var name = pack.Data?.Display?.Name ?? pack.DisplayName ?? pack.Meta?.RawId ?? "Smithed pack";
            var project = new CatalogProject
            {
                Name = name,
                Author = "Smithed creator",
                Description = pack.Data?.Display?.Description ?? "A version-aware Minecraft pack from Smithed.",
                ContentType = request.ContentType,
                Slug = pack.Meta?.RawId ?? pack.Id ?? NormalizeIdentity(name),
                IconUrl = pack.Data?.Display?.Icon ?? string.Empty,
                Accent = "#2E7D68",
                Mark = CreateMark(name),
                Tags = request.ContentType == CatalogContentType.DataPacks
                    ? "Data Pack   Smithed   Compatible"
                    : "Resource Pack   Smithed   Compatible",
                Downloads = pack.Meta?.Stats?.Downloads?.Total ?? 0,
                Followers = 0,
                UpdatedAt = FromUnixMilliseconds(pack.Meta?.Stats?.Updated)
            };
            project.SourceIds[CatalogSource.Smithed] = pack.Meta?.RawId ?? pack.Id ?? project.Slug;
            output.Add(project);
        }

        return output;
    }

    private async Task<CatalogInstallResult> InstallModrinthAsync(
        CatalogProject project,
        string projectId,
        InstanceInstallTarget target,
        IProgress<CatalogInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new CatalogInstallProgress(0, $"Finding a compatible {target.GameVersion} file..."));
        var loaders = GetCompatibleLoaders(project.ContentType, target.Loader);
        var url = new StringBuilder($"{ModrinthBaseUrl}/project/{Uri.EscapeDataString(projectId)}/version")
            .Append("?game_versions=").Append(Uri.EscapeDataString(JsonSerializer.Serialize(new[] { target.GameVersion })))
            .Append("&include_changelog=false");
        if (loaders.Length > 0)
        {
            url.Append("&loaders=").Append(Uri.EscapeDataString(JsonSerializer.Serialize(loaders)));
        }

        using var response = await httpClient.GetAsync(url.ToString(), cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var versions = await JsonSerializer.DeserializeAsync<List<ModrinthVersion>>(stream, JsonOptions, cancellationToken) ?? [];
        var version = versions.FirstOrDefault(item => item.VersionType == "release") ?? versions.FirstOrDefault();
        if (version is null)
        {
            throw new InvalidOperationException(
                $"No {target.GameVersion} {target.Loader} compatible version was found.");
        }

        var primaryFile = version.Files?.FirstOrDefault(file => file.Primary) ?? version.Files?.FirstOrDefault();
        if (primaryFile is null || string.IsNullOrWhiteSpace(primaryFile.Url))
        {
            throw new InvalidOperationException("The compatible version has no downloadable file.");
        }

        if (project.ContentType == CatalogContentType.Modpacks &&
            primaryFile.FileName.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
        {
            return await InstallModrinthPackAsync(project, primaryFile, target, progress, cancellationToken);
        }

        var destinationDirectory = GetDestinationDirectory(project.ContentType, target.RootPath);
        Directory.CreateDirectory(destinationDirectory);
        var destinationPath = GetSafeDestinationPath(destinationDirectory, primaryFile.FileName);
        await DownloadToFileAsync(primaryFile.Url, destinationPath, primaryFile.Hashes, progress, cancellationToken);

        return new CatalogInstallResult(
            destinationPath,
            target.GameVersion,
            target.Loader,
            $"Installed {project.Name} from Modrinth into {target.Name}.");
    }

    private async Task<CatalogInstallResult> InstallSmithedAsync(
        CatalogProject project,
        string packId,
        InstanceInstallTarget target,
        IProgress<CatalogInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var mode = project.ContentType == CatalogContentType.DataPacks ? "datapack" : "resourcepack";
        var downloadUrl = $"{SmithedBaseUrl}/download?pack={Uri.EscapeDataString(packId)}" +
                          $"&version={Uri.EscapeDataString(target.GameVersion)}&mode={mode}";
        var destinationDirectory = GetDestinationDirectory(project.ContentType, target.RootPath);
        Directory.CreateDirectory(destinationDirectory);
        var destinationPath = GetSafeDestinationPath(destinationDirectory, SanitizeFileName(project.Name) + ".zip");
        await DownloadToFileAsync(downloadUrl, destinationPath, null, progress, cancellationToken);

        return new CatalogInstallResult(
            destinationPath,
            target.GameVersion,
            target.Loader,
            $"Installed the {target.GameVersion} compatible {project.Name} pack from Smithed into {target.Name}.");
    }

    private async Task<CatalogInstallResult> InstallModrinthPackAsync(
        CatalogProject project,
        ModrinthFile packFile,
        InstanceInstallTarget target,
        IProgress<CatalogInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var temporaryPath = Path.Combine(Path.GetTempPath(), $"ublauncher-{Guid.NewGuid():N}.mrpack");
        try
        {
            await DownloadToFileAsync(packFile.Url, temporaryPath, packFile.Hashes, progress, cancellationToken);
            using var archive = ZipFile.OpenRead(temporaryPath);
            var indexEntry = archive.GetEntry("modrinth.index.json") ??
                             throw new InvalidDataException("The modpack is missing modrinth.index.json.");
            await using var indexStream = indexEntry.Open();
            var index = await JsonSerializer.DeserializeAsync<ModrinthPackIndex>(indexStream, JsonOptions, cancellationToken) ??
                        throw new InvalidDataException("The modpack index is invalid.");

            var files = index.Files ?? [];
            for (var fileIndex = 0; fileIndex < files.Count; fileIndex++)
            {
                var file = files[fileIndex];
                if (string.Equals(file.Environment?.Client, "unsupported", StringComparison.OrdinalIgnoreCase) ||
                    file.Downloads is not { Count: > 0 })
                {
                    continue;
                }

                var destinationPath = GetSafeDestinationPath(target.RootPath, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                var basePercent = 10 + 75d * fileIndex / Math.Max(1, files.Count);
                progress?.Report(new CatalogInstallProgress(basePercent, $"Installing {file.Path}..."));
                await DownloadToFileAsync(file.Downloads[0], destinationPath, file.Hashes, null, cancellationToken);
            }

            await CopyOverridesAsync(archive, "overrides/", target.RootPath, cancellationToken);
            await CopyOverridesAsync(archive, "client-overrides/", target.RootPath, cancellationToken);
            progress?.Report(new CatalogInstallProgress(100, "Modpack installed."));

            var minecraftVersion = index.Dependencies?.GetValueOrDefault("minecraft") ?? target.GameVersion;
            var loader = GetPackLoader(index.Dependencies) ?? target.Loader;
            return new CatalogInstallResult(
                target.RootPath,
                minecraftVersion,
                loader,
                $"Installed {project.Name} from Modrinth into {target.Name}.");
        }
        finally
        {
            try { File.Delete(temporaryPath); } catch { }
        }
    }

    private async Task DownloadToFileAsync(
        string url,
        string destinationPath,
        IReadOnlyDictionary<string, string>? hashes,
        IProgress<CatalogInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var temporaryPath = destinationPath + ".download";
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        try
        {
            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var destination = new FileStream(
                             temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long copied = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    copied += read;
                    if (totalBytes is > 0)
                    {
                        progress?.Report(new CatalogInstallProgress(
                            Math.Clamp(copied * 100d / totalBytes.Value, 0, 99),
                            $"Downloading {FormatBytes(copied)} / {FormatBytes(totalBytes.Value)}"));
                    }
                }

                await destination.FlushAsync(cancellationToken);
            }

            if (hashes is not null)
            {
                await VerifyHashAsync(temporaryPath, hashes, cancellationToken);
            }

            File.Move(temporaryPath, destinationPath, true);
            progress?.Report(new CatalogInstallProgress(100, "Download complete."));
        }
        finally
        {
            try { File.Delete(temporaryPath); } catch { }
        }
    }

    private static async Task VerifyHashAsync(
        string path,
        IReadOnlyDictionary<string, string> hashes,
        CancellationToken cancellationToken)
    {
        string? expected;
        HashAlgorithm? algorithm;
        if (hashes.TryGetValue("sha512", out expected))
        {
            algorithm = SHA512.Create();
        }
        else if (hashes.TryGetValue("sha1", out expected))
        {
            algorithm = SHA1.Create();
        }
        else
        {
            return;
        }

        using (algorithm)
        await using (var stream = File.OpenRead(path))
        {
            var actual = Convert.ToHexString(await algorithm.ComputeHashAsync(stream, cancellationToken));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Downloaded file failed its integrity check.");
            }
        }
    }

    private static async Task CopyOverridesAsync(
        ZipArchive archive,
        string prefix,
        string targetRoot,
        CancellationToken cancellationToken)
    {
        foreach (var entry in archive.Entries.Where(entry =>
                     entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                     !string.IsNullOrEmpty(entry.Name)))
        {
            var relativePath = entry.FullName[prefix.Length..];
            var destinationPath = GetSafeDestinationPath(targetRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using var input = entry.Open();
            await using var output = new FileStream(
                destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await input.CopyToAsync(output, cancellationToken);
        }
    }

    private static string[][] BuildModrinthFacets(CatalogSearchRequest request)
    {
        var facets = new List<string[]>();
        if (request.ContentType != CatalogContentType.Modpacks &&
            !string.IsNullOrWhiteSpace(request.GameVersion))
        {
            facets.Add([$"versions:{request.GameVersion}"]);
        }

        switch (request.ContentType)
        {
            case CatalogContentType.Modpacks:
                facets.Add(["project_type:modpack"]);
                break;
            case CatalogContentType.Mods:
                facets.Add(["project_type:mod"]);
                break;
            case CatalogContentType.Plugins:
                facets.Add(["project_type:plugin"]);
                facets.Add(["categories:paper"]);
                break;
            case CatalogContentType.ResourcePacks:
                facets.Add(["project_type:resourcepack"]);
                break;
            case CatalogContentType.DataPacks:
                facets.Add(["project_type:mod"]);
                facets.Add(["categories:datapack"]);
                break;
            case CatalogContentType.Shaders:
                facets.Add(["project_type:shader"]);
                break;
        }

        if (request.ContentType == CatalogContentType.Mods &&
            !string.Equals(request.Loader, "Vanilla", StringComparison.OrdinalIgnoreCase))
        {
            facets.Add([$"categories:{request.Loader.ToLowerInvariant()}"]);
        }

        return facets.ToArray();
    }

    private static CatalogProject MapModrinthProject(ModrinthSearchHit hit, CatalogContentType contentType)
    {
        var project = new CatalogProject
        {
            Name = hit.Title ?? "Untitled project",
            Author = hit.Author ?? "Unknown author",
            Description = hit.Description ?? string.Empty,
            ContentType = contentType,
            Slug = hit.Slug ?? hit.ProjectId ?? NormalizeIdentity(hit.Title ?? "project"),
            IconUrl = hit.IconUrl ?? string.Empty,
            Accent = hit.Color is { } color ? $"#{color & 0xFFFFFF:X6}" : "#356AA0",
            Mark = CreateMark(hit.Title ?? "Project"),
            Tags = string.Join("   ", (hit.DisplayCategories ?? hit.Categories ?? []).Take(6).Select(ToDisplayName)),
            Downloads = hit.Downloads,
            Followers = hit.Follows,
            UpdatedAt = hit.DateModified ?? DateTimeOffset.UtcNow
        };
        project.SourceIds[CatalogSource.Modrinth] = hit.ProjectId ?? project.Slug;
        return project;
    }

    private static IReadOnlyList<CatalogProject> Deduplicate(IEnumerable<CatalogProject> projects)
    {
        var deduplicated = new Dictionary<string, CatalogProject>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in projects)
        {
            var key = NormalizeIdentity(project.Name);
            if (!deduplicated.TryGetValue(key, out var existing))
            {
                deduplicated[key] = project;
                continue;
            }

            foreach (var source in project.SourceIds)
            {
                existing.SourceIds.TryAdd(source.Key, source.Value);
            }

            existing.Downloads = Math.Max(existing.Downloads, project.Downloads);
            existing.Followers = Math.Max(existing.Followers, project.Followers);
            existing.UpdatedAt = existing.UpdatedAt > project.UpdatedAt ? existing.UpdatedAt : project.UpdatedAt;

            if (!existing.SourceIds.ContainsKey(CatalogSource.Modrinth) &&
                project.SourceIds.ContainsKey(CatalogSource.Modrinth))
            {
                existing.Author = project.Author;
                existing.Description = project.Description;
                existing.IconUrl = project.IconUrl;
                existing.Accent = project.Accent;
                existing.Tags = project.Tags;
            }
        }

        return deduplicated.Values.ToList();
    }

    private static string[] GetCompatibleLoaders(CatalogContentType contentType, string loader)
    {
        return contentType switch
        {
            CatalogContentType.ResourcePacks => ["minecraft"],
            CatalogContentType.DataPacks => ["datapack", "minecraft"],
            CatalogContentType.Plugins => ["paper", "purpur", "spigot", "bukkit"],
            CatalogContentType.Mods or CatalogContentType.Modpacks
                when !string.Equals(loader, "Vanilla", StringComparison.OrdinalIgnoreCase)
                => [loader.ToLowerInvariant()],
            _ => []
        };
    }

    private static string GetDestinationDirectory(CatalogContentType contentType, string rootPath) =>
        Path.Combine(rootPath, contentType switch
        {
            CatalogContentType.Mods => "mods",
            CatalogContentType.Plugins => "plugins",
            CatalogContentType.ResourcePacks => "resourcepacks",
            CatalogContentType.DataPacks => "datapacks",
            CatalogContentType.Shaders => "shaderpacks",
            CatalogContentType.Modpacks => "modpacks",
            _ => "downloads"
        });

    private static string GetSafeDestinationPath(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The package contains an unsafe file path.");
        }

        return fullPath;
    }

    private static string? GetPackLoader(IReadOnlyDictionary<string, string>? dependencies)
    {
        if (dependencies is null) return null;
        if (dependencies.ContainsKey("fabric-loader")) return "Fabric";
        if (dependencies.ContainsKey("neoforge")) return "NeoForge";
        if (dependencies.ContainsKey("forge")) return "Forge";
        if (dependencies.ContainsKey("quilt-loader")) return "Quilt";
        return "Vanilla";
    }

    private static ModrinthFile? GetPrimaryPackFile(ModrinthVersion version) =>
        version.Files?.FirstOrDefault(file =>
            file.Primary && file.FileName.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase)) ??
        version.Files?.FirstOrDefault(file =>
            file.FileName.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase));

    private static Version GetHighestGameVersion(IReadOnlyList<string>? gameVersions) =>
        (gameVersions ?? [])
        .Select(ParseMinecraftVersion)
        .OrderByDescending(version => version)
        .FirstOrDefault() ?? new Version(0, 0);

    private static string? GetLatestGameVersionName(IReadOnlyList<string>? gameVersions)
    {
        return (gameVersions ?? [])
            .Select(value => new { Value = value, Version = ParseMinecraftVersion(value) })
            .Where(item => item.Version > new Version(0, 0))
            .OrderByDescending(item => item.Version)
            .Select(item => item.Value)
            .FirstOrDefault() ?? gameVersions?.FirstOrDefault();
    }

    private static Version ParseMinecraftVersion(string value)
    {
        return Regex.IsMatch(value, @"^\d+(?:\.\d+){1,3}$") && Version.TryParse(value, out var version)
            ? version
            : new Version(0, 0);
    }

    private static string? GetVersionLoader(IReadOnlyList<string>? loaders)
    {
        foreach (var loader in loaders ?? [])
        {
            switch (loader.ToLowerInvariant())
            {
                case "fabric": return "Fabric";
                case "neoforge": return "NeoForge";
                case "forge": return "Forge";
                case "quilt": return "Quilt";
            }
        }

        return null;
    }

    private static string NormalizeIdentity(string value)
    {
        value = Regex.Replace(value, @"\[(fabric|forge|neoforge|quilt)\]", string.Empty, RegexOptions.IgnoreCase);
        return string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();
    }

    private static string CreateMark(string name)
    {
        var words = name.Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 2)
        {
            return string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word[0])));
        }

        return name.Length <= 3 ? name.ToUpperInvariant() : name[..2].ToUpperInvariant();
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(name.Select(character => invalid.Contains(character) ? '-' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(result) ? "content" : result;
    }

    private static string ToDisplayName(string value) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Replace('-', ' '));

    private static DateTimeOffset FromUnixMilliseconds(long? value)
    {
        if (value is null or <= 0) return DateTimeOffset.UtcNow;
        try { return DateTimeOffset.FromUnixTimeMilliseconds(value.Value); }
        catch { return DateTimeOffset.UtcNow; }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var size = (double)bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.#} {units[unit]}";
    }

    private sealed class ModrinthSearchResponse
    {
        public List<ModrinthSearchHit>? Hits { get; set; }
    }

    private sealed class ModrinthSearchHit
    {
        public string? Slug { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public List<string>? Categories { get; set; }
        [JsonPropertyName("display_categories")] public List<string>? DisplayCategories { get; set; }
        [JsonPropertyName("project_id")] public string? ProjectId { get; set; }
        public string? Author { get; set; }
        public long Downloads { get; set; }
        public long Follows { get; set; }
        [JsonPropertyName("icon_url")] public string? IconUrl { get; set; }
        public int? Color { get; set; }
        [JsonPropertyName("date_modified")] public DateTimeOffset? DateModified { get; set; }
    }

    private sealed class ModrinthVersion
    {
        [JsonPropertyName("version_type")] public string? VersionType { get; set; }
        [JsonPropertyName("game_versions")] public List<string>? GameVersions { get; set; }
        [JsonPropertyName("date_published")] public DateTimeOffset? DatePublished { get; set; }
        public List<string>? Loaders { get; set; }
        public List<ModrinthFile>? Files { get; set; }
    }

    private sealed class ModrinthFile
    {
        public required string Url { get; set; }
        [JsonPropertyName("filename")] public required string FileName { get; set; }
        public bool Primary { get; set; }
        public Dictionary<string, string>? Hashes { get; set; }
    }

    private sealed class ModrinthPackIndex
    {
        public List<ModrinthPackFile>? Files { get; set; }
        public Dictionary<string, string>? Dependencies { get; set; }
    }

    private sealed class ModrinthPackFile
    {
        public required string Path { get; set; }
        public List<string>? Downloads { get; set; }
        public Dictionary<string, string>? Hashes { get; set; }
        public ModrinthPackEnvironment? Environment { get; set; }
    }

    private sealed class ModrinthPackEnvironment
    {
        public string? Client { get; set; }
    }

    private sealed class SmithedPack
    {
        public string? Id { get; set; }
        public string? DisplayName { get; set; }
        public SmithedData? Data { get; set; }
        public SmithedMeta? Meta { get; set; }
    }

    private sealed class SmithedData
    {
        public SmithedDisplay? Display { get; set; }
        public List<SmithedVersion>? Versions { get; set; }
    }

    private sealed class SmithedDisplay
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Icon { get; set; }
    }

    private sealed class SmithedVersion
    {
        public string? Name { get; set; }
        public List<string>? Supports { get; set; }
        public SmithedDownloads? Downloads { get; set; }
    }

    private sealed class SmithedDownloads
    {
        [JsonPropertyName("datapack")] public string? DataPack { get; set; }
        [JsonPropertyName("resourcepack")] public string? ResourcePack { get; set; }
    }

    private sealed class SmithedMeta
    {
        public string? RawId { get; set; }
        public SmithedStats? Stats { get; set; }
    }

    private sealed class SmithedStats
    {
        public long? Updated { get; set; }
        public SmithedDownloadStats? Downloads { get; set; }
    }

    private sealed class SmithedDownloadStats
    {
        public long Total { get; set; }
    }
}
