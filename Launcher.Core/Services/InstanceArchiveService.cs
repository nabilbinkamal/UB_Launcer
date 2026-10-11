using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Launcher.Core.ViewModels;
using Launcher.Security;

namespace Launcher.Core.Services;

public sealed record InstanceArchiveProgress(double Percentage, string Message);

public sealed record InstanceArchiveDescriptor(
    string Name,
    string MinecraftVersion,
    string Loader,
    string Kind,
    string Format);

public sealed record InstanceArchiveImportResult(GameProfile Profile, string Format);

public sealed class InstanceArchiveService
{
    private const string ManifestEntryName = "ublauncher.instance.json";
    private const string InstancePrefix = "instance/";
    private const string ProfileIconPrefix = "profile/icon";
    private const string ImportedProfileIconDirectory = ".ublauncher";
    private const long MaximumProfileIconBytes = 8L * 1024 * 1024;
    private const long MaximumArchiveBytes = 16L * 1024 * 1024 * 1024;
    private const long MaximumEntryBytes = 2L * 1024 * 1024 * 1024;
    private const int MaximumEntries = 100_000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly HashSet<string> ExcludedTopLevelDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "assets",
        "backups",
        "crash-reports",
        "downloads",
        "libraries",
        "logs",
        "natives",
        "runtime",
        "saves",
        "screenshots",
        "server-resource-packs",
        "versions",
        "webcache2"
    };

    private static readonly HashSet<string> ExcludedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "launcher_accounts.json",
        "launcher_log.txt",
        "launcher_profiles.json",
        "usernamecache.json",
        "usercache.json"
    };

    private static readonly HttpClient HttpClient = CreateHttpClient();

    public async Task<InstanceArchiveDescriptor> InspectAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        ValidateArchivePath(archivePath);
        using var archive = ZipFile.OpenRead(archivePath);
        ValidateArchiveLimits(archive);

        if (FindEntry(archive, ManifestEntryName) is { } manifestEntry)
        {
            await using var manifestStream = manifestEntry.Open();
            var manifest = await JsonSerializer.DeserializeAsync<UbPackManifest>(
                               manifestStream,
                               JsonOptions,
                               cancellationToken) ??
                           throw new InvalidDataException("The UB pack manifest is invalid.");
            ValidateManifest(manifest);
            return new InstanceArchiveDescriptor(
                manifest.Name,
                manifest.MinecraftVersion,
                manifest.Loader,
                manifest.Kind,
                "UB Pack");
        }

        if (FindEntry(archive, "modrinth.index.json") is { } modrinthEntry)
        {
            var index = await ReadModrinthIndexAsync(modrinthEntry, cancellationToken);
            return new InstanceArchiveDescriptor(
                index.Name,
                GetMinecraftVersion(index.Dependencies),
                GetLoader(index.Dependencies),
                "Modpack",
                "Modrinth Pack");
        }

        throw new InvalidDataException("Choose a valid .ubpack or Modrinth .mrpack file.");
    }

    public async Task<string> ExportAsync(
        GameProfile profile,
        string destinationPath,
        IProgress<InstanceArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("Choose an export destination.", nameof(destinationPath));
        }

        destinationPath = Path.ChangeExtension(destinationPath, ".ubpack");
        var destinationDirectory = Path.GetDirectoryName(Path.GetFullPath(destinationPath)) ??
                                   throw new InvalidOperationException("The export destination is invalid.");
        Directory.CreateDirectory(destinationDirectory);

        var sourceRoot = Path.GetFullPath(profile.InstancePath);
        var files = Directory.Exists(sourceRoot)
            ? EnumeratePortableFiles(sourceRoot).ToList()
            : [];
        var temporaryPath = destinationPath + $".{Guid.NewGuid():N}.partial";

        try
        {
            progress?.Report(new InstanceArchiveProgress(0, "Preparing portable instance..."));
            var embeddedIcon = await LoadProfileIconAsync(profile.IconUrl, cancellationToken);
            var embeddedIconEntryName = embeddedIcon is null
                ? string.Empty
                : ProfileIconPrefix + embeddedIcon.Extension;
            await using (var output = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.ReadWrite,
                             FileShare.None,
                             131_072,
                             true))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false))
            {
                var manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
                await using (var manifestStream = manifestEntry.Open())
                {
                    var manifest = new UbPackManifest
                    {
                        FormatVersion = 1,
                        Name = profile.Name,
                        MinecraftVersion = profile.Version,
                        Loader = profile.LoaderType,
                        Kind = profile.Kind,
                        Accent = profile.Accent,
                        IconUrl = profile.IconUrl,
                        IconEntry = embeddedIconEntryName,
                        ModrinthProjectId = profile.ModrinthProjectId,
                        ExportedAtUtc = DateTimeOffset.UtcNow
                    };
                    await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions, cancellationToken);
                }

                if (embeddedIcon is not null)
                {
                    var iconEntry = archive.CreateEntry(embeddedIconEntryName, CompressionLevel.Optimal);
                    await using var iconStream = iconEntry.Open();
                    await iconStream.WriteAsync(embeddedIcon.Bytes, cancellationToken);
                }

                for (var index = 0; index < files.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var sourcePath = files[index];
                    var relativePath = Path.GetRelativePath(sourceRoot, sourcePath).Replace('\\', '/');
                    var entry = archive.CreateEntry(InstancePrefix + relativePath, CompressionLevel.Optimal);
                    await using var input = new FileStream(
                        sourcePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        131_072,
                        true);
                    await using var entryStream = entry.Open();
                    await input.CopyToAsync(entryStream, cancellationToken);

                    var percentage = 5 + 94d * (index + 1) / Math.Max(1, files.Count);
                    progress?.Report(new InstanceArchiveProgress(
                        percentage,
                        $"Packing {relativePath} ({index + 1}/{files.Count})"));
                }
            }

            File.Move(temporaryPath, destinationPath, true);
            progress?.Report(new InstanceArchiveProgress(100, "Instance export complete."));
            return destinationPath;
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    public async Task<InstanceArchiveImportResult> ImportAsync(
        string archivePath,
        string instanceName,
        IProgress<InstanceArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateArchivePath(archivePath);
        if (string.IsNullOrWhiteSpace(instanceName))
        {
            throw new ArgumentException("The imported instance needs a name.", nameof(instanceName));
        }

        using var archive = ZipFile.OpenRead(archivePath);
        ValidateArchiveLimits(archive);
        if (FindEntry(archive, ManifestEntryName) is { } manifestEntry)
        {
            return await ImportUbPackAsync(
                archive,
                manifestEntry,
                instanceName,
                progress,
                cancellationToken);
        }

        if (FindEntry(archive, "modrinth.index.json") is { } modrinthEntry)
        {
            return await ImportModrinthPackAsync(
                archive,
                modrinthEntry,
                instanceName,
                Path.GetFileName(archivePath),
                progress,
                cancellationToken);
        }

        throw new InvalidDataException("Choose a valid .ubpack or Modrinth .mrpack file.");
    }

    private static async Task<InstanceArchiveImportResult> ImportUbPackAsync(
        ZipArchive archive,
        ZipArchiveEntry manifestEntry,
        string instanceName,
        IProgress<InstanceArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        await using var manifestStream = manifestEntry.Open();
        var manifest = await JsonSerializer.DeserializeAsync<UbPackManifest>(
                           manifestStream,
                           JsonOptions,
                           cancellationToken) ??
                       throw new InvalidDataException("The UB pack manifest is invalid.");
        ValidateManifest(manifest);

        var profile = InstanceStore.CreateProfile(
            instanceName,
            manifest.MinecraftVersion,
            manifest.Loader,
            kind: manifest.Kind,
            accent: manifest.Accent,
            iconUrl: manifest.IconUrl,
            modrinthProjectId: manifest.ModrinthProjectId);
        var targetRoot = Path.GetFullPath(profile.InstancePath);

        EnsureEmptyImportTarget(targetRoot);
        try
        {
            var importedIconPath = await ExtractProfileIconAsync(
                archive,
                targetRoot,
                manifest.IconEntry,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(importedIconPath))
            {
                profile.IconUrl = importedIconPath;
            }

            var contentEntries = archive.Entries
                .Where(entry => entry.FullName.StartsWith(InstancePrefix, StringComparison.OrdinalIgnoreCase) &&
                                !string.IsNullOrEmpty(entry.Name))
                .ToList();
            for (var index = 0; index < contentEntries.Count; index++)
            {
                var entry = contentEntries[index];
                var relativePath = entry.FullName[InstancePrefix.Length..];
                await ExtractEntryAsync(entry, targetRoot, relativePath, cancellationToken);
                var percentage = 5 + 94d * (index + 1) / Math.Max(1, contentEntries.Count);
                progress?.Report(new InstanceArchiveProgress(
                    percentage,
                    $"Importing {relativePath} ({index + 1}/{contentEntries.Count})"));
            }

            profile.RefreshContentInventory();
            progress?.Report(new InstanceArchiveProgress(100, "UB Pack imported."));
            return new InstanceArchiveImportResult(profile, "UB Pack");
        }
        catch
        {
            TryDeleteImportTarget(targetRoot);
            throw;
        }
    }

    private static async Task<InstanceArchiveImportResult> ImportModrinthPackAsync(
        ZipArchive archive,
        ZipArchiveEntry indexEntry,
        string instanceName,
        string archiveFileName,
        IProgress<InstanceArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        var index = await ReadModrinthIndexAsync(indexEntry, cancellationToken);
        var profile = InstanceStore.CreateProfile(
            instanceName,
            GetMinecraftVersion(index.Dependencies),
            GetLoader(index.Dependencies),
            kind: "Modpack");
        var targetRoot = Path.GetFullPath(profile.InstancePath);

        EnsureEmptyImportTarget(targetRoot);
        try
        {
            var importedIconPath = await ExtractProfileIconAsync(
                archive,
                targetRoot,
                preferredEntryName: null,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(importedIconPath))
            {
                profile.IconUrl = importedIconPath;
            }
            else
            {
                var modrinthAppIcon = ModrinthAppProfileArtworkResolver.TryCopyToInstance(
                    index.Name,
                    targetRoot,
                    archiveFileName);
                if (!string.IsNullOrWhiteSpace(modrinthAppIcon))
                {
                    profile.IconUrl = modrinthAppIcon;
                }
            }

            var downloadableFiles = (index.Files ?? [])
                .Where(file => !string.Equals(file.Environment?.Client, "unsupported", StringComparison.OrdinalIgnoreCase))
                .ToList();
            for (var fileIndex = 0; fileIndex < downloadableFiles.Count; fileIndex++)
            {
                var file = downloadableFiles[fileIndex];
                if (file.Downloads is not { Count: > 0 })
                {
                    throw new InvalidDataException($"{file.Path} has no download source.");
                }

                var destinationPath = GetSafeDestinationPath(targetRoot, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                var basePercentage = 5 + 78d * fileIndex / Math.Max(1, downloadableFiles.Count);
                progress?.Report(new InstanceArchiveProgress(basePercentage, $"Downloading {file.Path}..."));
                await DownloadFileAsync(file.Downloads[0], destinationPath, file.Hashes, cancellationToken);
            }

            await ExtractOverridePrefixAsync(archive, "overrides/", targetRoot, 84, 92, progress, cancellationToken);
            await ExtractOverridePrefixAsync(archive, "client-overrides/", targetRoot, 92, 99, progress, cancellationToken);
            profile.RefreshContentInventory();
            progress?.Report(new InstanceArchiveProgress(100, "Modrinth pack imported."));
            return new InstanceArchiveImportResult(profile, "Modrinth Pack");
        }
        catch
        {
            TryDeleteImportTarget(targetRoot);
            throw;
        }
    }

    private static async Task ExtractOverridePrefixAsync(
        ZipArchive archive,
        string prefix,
        string targetRoot,
        double startPercentage,
        double endPercentage,
        IProgress<InstanceArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        var entries = archive.Entries
            .Where(entry => entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrEmpty(entry.Name))
            .ToList();
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var relativePath = entry.FullName[prefix.Length..];
            await ExtractEntryAsync(entry, targetRoot, relativePath, cancellationToken, overwrite: true);
            var percentage = startPercentage +
                             (endPercentage - startPercentage) * (index + 1) / Math.Max(1, entries.Count);
            progress?.Report(new InstanceArchiveProgress(percentage, $"Applying {relativePath}..."));
        }
    }

    private static async Task ExtractEntryAsync(
        ZipArchiveEntry entry,
        string targetRoot,
        string relativePath,
        CancellationToken cancellationToken,
        bool overwrite = false)
    {
        if (entry.Length > MaximumEntryBytes)
        {
            throw new InvalidDataException($"{entry.FullName} is too large to import safely.");
        }

        var destinationPath = GetSafeDestinationPath(targetRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await using var input = entry.Open();
        await using var output = new FileStream(
            destinationPath,
            overwrite ? FileMode.Create : FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            131_072,
            true);
        await BoundedStream.CopyAsync(input, output, Math.Min(entry.Length, MaximumEntryBytes), cancellationToken);
    }

    private static async Task DownloadFileAsync(
        string url,
        string destinationPath,
        IReadOnlyDictionary<string, string>? hashes,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("The modpack contains an invalid download URL.");
        }

        var temporaryPath = destinationPath + ".download";
        try
        {
            using var response = await HttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaximumEntryBytes)
            {
                throw new InvalidDataException("A modpack file is too large to import safely.");
            }

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var output = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             131_072,
                             true))
            {
                await BoundedStream.CopyAsync(input, output, MaximumEntryBytes, cancellationToken);
            }

            await VerifyHashAsync(temporaryPath, hashes, cancellationToken);
            File.Move(temporaryPath, destinationPath, true);
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private static async Task VerifyHashAsync(
        string path,
        IReadOnlyDictionary<string, string>? hashes,
        CancellationToken cancellationToken)
    {
        if (hashes is null)
        {
            throw new InvalidDataException("The modpack file has no integrity hash.");
        }

        HashAlgorithm? algorithm = null;
        string? expected = null;
        if (hashes.TryGetValue("sha512", out var sha512))
        {
            algorithm = SHA512.Create();
            expected = sha512;
        }
        else if (hashes.TryGetValue("sha1", out var sha1))
        {
            algorithm = SHA1.Create();
            expected = sha1;
        }

        if (algorithm is null || string.IsNullOrWhiteSpace(expected))
        {
            algorithm?.Dispose();
            throw new InvalidDataException("The modpack file has no supported integrity hash.");
        }

        using (algorithm)
        await using (var stream = File.OpenRead(path))
        {
            var actual = Convert.ToHexString(await algorithm.ComputeHashAsync(stream, cancellationToken));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"{Path.GetFileName(path)} failed its integrity check.");
            }
        }
    }

    private static async Task<EmbeddedProfileIcon?> LoadProfileIconAsync(
        string iconSource,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(iconSource))
        {
            return null;
        }

        try
        {
            byte[] bytes;
            string extension;
            if (File.Exists(iconSource))
            {
                var fileInfo = new FileInfo(iconSource);
                if (fileInfo.Length is <= 0 or > MaximumProfileIconBytes)
                {
                    return null;
                }

                bytes = await File.ReadAllBytesAsync(iconSource, cancellationToken);
                extension = NormalizeImageExtension(Path.GetExtension(iconSource));
            }
            else if (Uri.TryCreate(iconSource, UriKind.Absolute, out var uri) &&
                     (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            {
                using var response = await HttpClient.GetAsync(
                    uri,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is <= 0 or > MaximumProfileIconBytes)
                {
                    return null;
                }

                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var output = new MemoryStream();
                var buffer = new byte[32 * 1024];
                var totalBytes = 0;
                int bytesRead;
                while ((bytesRead = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    totalBytes += bytesRead;
                    if (totalBytes > MaximumProfileIconBytes)
                    {
                        return null;
                    }

                    await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                }

                bytes = output.ToArray();
                extension = NormalizeImageExtension(Path.GetExtension(uri.AbsolutePath));
            }
            else
            {
                return null;
            }

            return bytes.Length == 0 ? null : new EmbeddedProfileIcon(bytes, extension);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string> ExtractProfileIconAsync(
        ZipArchive archive,
        string targetRoot,
        string? preferredEntryName,
        CancellationToken cancellationToken)
    {
        var entry = FindEntry(archive, preferredEntryName)
                    ?? FindEntry(archive, ProfileIconPrefix + ".png")
                    ?? FindEntry(archive, ProfileIconPrefix + ".jpg")
                    ?? FindEntry(archive, ProfileIconPrefix + ".jpeg")
                    ?? FindEntry(archive, ProfileIconPrefix + ".webp")
                    ?? FindEntry(archive, "icon.png")
                    ?? FindEntry(archive, "icon.jpg")
                    ?? FindEntry(archive, "icon.jpeg")
                    ?? FindEntry(archive, "icon.webp")
                    ?? FindEntry(archive, "pack.png");
        if (entry is null || entry.Length is <= 0 or > MaximumProfileIconBytes)
        {
            return string.Empty;
        }

        var extension = NormalizeImageExtension(Path.GetExtension(entry.Name));
        var relativePath = $"{ImportedProfileIconDirectory}/profile-icon{extension}";
        await ExtractEntryAsync(entry, targetRoot, relativePath, cancellationToken, overwrite: true);
        return GetSafeDestinationPath(targetRoot, relativePath);
    }

    private static string NormalizeImageExtension(string extension) => extension.ToLowerInvariant() switch
    {
        ".jpg" => ".jpg",
        ".jpeg" => ".jpeg",
        ".webp" => ".webp",
        _ => ".png"
    };

    private static IEnumerable<string> EnumeratePortableFiles(string sourceRoot)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        foreach (var path in Directory.EnumerateFiles(sourceRoot, "*", options))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, path);
            var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var extension = Path.GetExtension(path);
            if (segments.Length == 0 ||
                ExcludedTopLevelDirectories.Contains(segments[0]) ||
                ExcludedFileNames.Contains(Path.GetFileName(path)) ||
                extension.Equals(".log", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".ubpack", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".mrpack", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) == 0)
            {
                yield return path;
            }
        }
    }

    private static void ValidateArchiveLimits(ZipArchive archive)
    {
        if (archive.Entries.Count > MaximumEntries)
        {
            throw new InvalidDataException("This pack contains too many files.");
        }

        long totalBytes = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.Length > MaximumEntryBytes)
            {
                throw new InvalidDataException($"{entry.FullName} is too large to import safely.");
            }

            totalBytes = checked(totalBytes + entry.Length);
            if (totalBytes > MaximumArchiveBytes)
            {
                throw new InvalidDataException("This pack is too large to import safely.");
            }
        }
    }

    private static void ValidateArchivePath(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
        {
            throw new FileNotFoundException("The selected instance pack could not be found.", archivePath);
        }
    }

    private static void ValidateManifest(UbPackManifest manifest)
    {
        if (manifest.FormatVersion != 1 ||
            string.IsNullOrWhiteSpace(manifest.Name) ||
            string.IsNullOrWhiteSpace(manifest.MinecraftVersion) ||
            string.IsNullOrWhiteSpace(manifest.Loader))
        {
            throw new InvalidDataException("The UB pack manifest is incomplete or unsupported.");
        }
    }

    private static async Task<ModrinthPackIndex> ReadModrinthIndexAsync(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        var index = await JsonSerializer.DeserializeAsync<ModrinthPackIndex>(stream, JsonOptions, cancellationToken) ??
                    throw new InvalidDataException("The Modrinth pack index is invalid.");
        if (!index.Game.Equals("minecraft", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(index.Name) ||
            string.IsNullOrWhiteSpace(GetMinecraftVersion(index.Dependencies)))
        {
            throw new InvalidDataException("The selected file is not a supported Minecraft Modrinth pack.");
        }

        return index;
    }

    private static string GetMinecraftVersion(IReadOnlyDictionary<string, string>? dependencies) =>
        dependencies?.GetValueOrDefault("minecraft") ?? string.Empty;

    private static string GetLoader(IReadOnlyDictionary<string, string>? dependencies)
    {
        if (dependencies is null)
        {
            return "Vanilla";
        }

        if (dependencies.ContainsKey("fabric-loader")) return "Fabric";
        if (dependencies.ContainsKey("neoforge")) return "NeoForge";
        if (dependencies.ContainsKey("forge")) return "Forge";
        if (dependencies.ContainsKey("quilt-loader")) return "Quilt";
        return "Vanilla";
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string? fullName) =>
        string.IsNullOrWhiteSpace(fullName)
            ? null
            :
        archive.Entries.FirstOrDefault(entry => entry.FullName.Equals(fullName, StringComparison.OrdinalIgnoreCase));

    private static string GetSafeDestinationPath(string rootPath, string relativePath)
    {
        return SafeArchivePath.Resolve(rootPath, relativePath);
    }

    private static void EnsureEmptyImportTarget(string targetRoot)
    {
        if (Directory.Exists(targetRoot) && Directory.EnumerateFileSystemEntries(targetRoot).Any())
        {
            throw new IOException("An existing instance folder already uses this name.");
        }

        Directory.CreateDirectory(targetRoot);
    }

    private static void TryDeleteImportTarget(string targetRoot)
    {
        try
        {
            if (Directory.Exists(targetRoot))
            {
                Directory.Delete(targetRoot, true);
            }

            var parent = Directory.GetParent(targetRoot);
            if (parent is not null && parent.Exists && !parent.EnumerateFileSystemInfos().Any())
            {
                parent.Delete();
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("UBLauncher", "0.1"));
        return client;
    }

    private sealed class UbPackManifest
    {
        public int FormatVersion { get; init; }
        public string Name { get; init; } = string.Empty;
        public string MinecraftVersion { get; init; } = string.Empty;
        public string Loader { get; init; } = string.Empty;
        public string Kind { get; init; } = "Custom";
        public string Accent { get; init; } = "#4B78C9";
        public string IconUrl { get; init; } = string.Empty;
        public string IconEntry { get; init; } = string.Empty;
        public string ModrinthProjectId { get; init; } = string.Empty;
        public DateTimeOffset ExportedAtUtc { get; init; }
    }

    private sealed record EmbeddedProfileIcon(byte[] Bytes, string Extension);

    private sealed class ModrinthPackIndex
    {
        [JsonPropertyName("game")]
        public string Game { get; init; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("versionId")]
        public string VersionId { get; init; } = string.Empty;

        [JsonPropertyName("files")]
        public List<ModrinthPackFile>? Files { get; init; }

        [JsonPropertyName("dependencies")]
        public Dictionary<string, string>? Dependencies { get; init; }
    }

    private sealed class ModrinthPackFile
    {
        [JsonPropertyName("path")]
        public string Path { get; init; } = string.Empty;

        [JsonPropertyName("hashes")]
        public Dictionary<string, string>? Hashes { get; init; }

        [JsonPropertyName("downloads")]
        public List<string>? Downloads { get; init; }

        [JsonPropertyName("env")]
        public ModrinthEnvironment? Environment { get; init; }
    }

    private sealed class ModrinthEnvironment
    {
        [JsonPropertyName("client")]
        public string? Client { get; init; }
    }
}
