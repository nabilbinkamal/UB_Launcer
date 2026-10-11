using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Launcher.Core.Services;

public static class ServerSoftwareDownloadService
{
    private const string PaperProject = "paper";
    private const string HangarNamespace = "ViaVersion";
    private const string PaperPlatform = "PAPER";
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly DefaultPaperPlugin[] DefaultPaperPlugins =
    [
        new("ViaVersion", "ViaVersion.jar"),
        new("ViaBackwards", "ViaBackwards.jar"),
        new("ViaRewind", "ViaRewind.jar")
    ];
    private static readonly IReadOnlyDictionary<string, Uri> PaperDownloadUrls =
        new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase)
        {
            ["1.21.11"] = new("https://fill-data.papermc.io/v1/objects/e708e8c132dc143ffd73528cccb9532e2eb17628b1a0eee74469bf466c7003f8/paper-1.21.11-116.jar"),
            ["1.21.11-rc3"] = new("https://fill-data.papermc.io/v1/objects/213ceae4eb2268fc110a8605c00597ab56fe733ec41a59d06689de178bbec3f9/paper-1.21.11-rc3-31.jar"),
            ["1.21.11-rc2"] = new("https://fill-data.papermc.io/v1/objects/417e9e6fb7cd34245c6a2a5ad4479eea018dc373fe36e74a6224b3652c784723/paper-1.21.11-rc2-29.jar"),
            ["1.21.11-rc1"] = new("https://fill-data.papermc.io/v1/objects/bfd57d33c550fa70a493fc00f30eac275dc4a71fa7f8eaa990ca240ef7024f02/paper-1.21.11-rc1-19.jar"),
            ["1.21.11-pre5"] = new("https://fill-data.papermc.io/v1/objects/b9d820240254e2a2e2f98093d9624cbcb772c441ecdac93e9fb9a10da4ecbff7/paper-1.21.11-pre5-16.jar"),
            ["1.21.11-pre4"] = new("https://fill-data.papermc.io/v1/objects/160ade98b0e697537d47335f9bb8b7c74f4d0b1d9f18f6ad6aba0549d42dd98f/paper-1.21.11-pre4-13.jar"),
            ["1.21.11-pre3"] = new("https://fill-data.papermc.io/v1/objects/5f7079d6ec5862bc03195012eea2f6e0b3cba6922e7cefa0ac660ebf279decda/paper-1.21.11-pre3-6.jar"),
            ["1.21.10"] = new("https://fill-data.papermc.io/v1/objects/158703f75a26f842ea656b3dc6d75bf3d1ec176b97a2c36384d0b80b3871af53/paper-1.21.10-130.jar"),
            ["1.21.9"] = new("https://fill-data.papermc.io/v1/objects/aec002e77c7566e49494fdf05430b96078ffd1d7430e652d4f338fef951e7a10/paper-1.21.9-59.jar"),
            ["1.21.9-rc1"] = new("https://fill-data.papermc.io/v1/objects/f737c4ce0afd8ca897c5330188634859148419c6c2d2e172c65f581c47430ab1/paper-1.21.9-rc1-36.jar"),
            ["1.21.9-pre4"] = new("https://fill-data.papermc.io/v1/objects/59f2df043a9b186243439398c0dd8ed876e8eacd995f3f667a86406a699b1a27/paper-1.21.9-pre4-22.jar"),
            ["1.21.9-pre3"] = new("https://fill-data.papermc.io/v1/objects/b823c6ff6a11ccbab475bf4e5786ee271fea406331e27d0581387765ccc16bc8/paper-1.21.9-pre3-12.jar"),
            ["1.21.9-pre2"] = new("https://fill-data.papermc.io/v1/objects/f24f81421449bbcf125be5501223bf9faa9a3ef2006b16988be25601c015757e/paper-1.21.9-pre2-7.jar"),
            ["1.21.8"] = new("https://fill-data.papermc.io/v1/objects/8de7c52c3b02403503d16fac58003f1efef7dd7a0256786843927fa92ee57f1e/paper-1.21.8-60.jar"),
            ["1.21.7"] = new("https://fill-data.papermc.io/v1/objects/83838188699cb2837e55b890fb1a1d39ad0710285ed633fbf9fc14e9f47ce078/paper-1.21.7-32.jar"),
            ["1.21.6"] = new("https://fill-data.papermc.io/v1/objects/35e2dfa66b3491b9d2f0bb033679fa5aca1e1fdf097e7a06a80ce8afeda5c214/paper-1.21.6-48.jar"),
            ["1.21.5"] = new("https://fill-data.papermc.io/v1/objects/2ae6ae22adf417699746e0f89fc2ef6cb6ee050a5f6608cee58f0535d60b509e/paper-1.21.5-114.jar"),
            ["1.21.4"] = new("https://fill-data.papermc.io/v1/objects/5ee4f542f628a14c644410b08c94ea42e772ef4d29fe92973636b6813d4eaffc/paper-1.21.4-232.jar"),
            ["1.21.3"] = new("https://fill-data.papermc.io/v1/objects/87e973e1d338e869e7fdbc4b8fadc1579d7bb0246a0e0cf6e5700ace6c8bc17e/paper-1.21.3-83.jar"),
            ["1.21.1"] = new("https://fill-data.papermc.io/v1/objects/39bd8c00b9e18de91dcabd3cc3dcfa5328685a53b7187a2f63280c22e2d287b9/paper-1.21.1-133.jar"),
            ["1.21"] = new("https://fill-data.papermc.io/v1/objects/ab9bb1afc3cea6978a0c03ce8448aa654fe8a9c4dddf341e7cbda1b0edaa73f5/paper-1.21-130.jar"),
            ["1.20.6"] = new("https://fill-data.papermc.io/v1/objects/4b011f5adb5f6c72007686a223174fce82f31aeb4b34faf4652abc840b47e640/paper-1.20.6-151.jar"),
            ["1.20.5"] = new("https://fill-data.papermc.io/v1/objects/3cd7da2f8df92e082a501a39c674aab3c0343edd179b86f5baccaebfc9974132/paper-1.20.5-22.jar"),
            ["1.20.4"] = new("https://fill-data.papermc.io/v1/objects/cabed3ae77cf55deba7c7d8722bc9cfd5e991201c211665f9265616d9fe5c77b/paper-1.20.4-499.jar"),
            ["1.20.2"] = new("https://fill-data.papermc.io/v1/objects/ba340a835ac40b8563aa7eda1cd6479a11a7623409c89a2c35cd9d7490ed17a7/paper-1.20.2-318.jar"),
            ["1.20.1"] = new("https://fill-data.papermc.io/v1/objects/234a9b32098100c6fc116664d64e36ccdb58b5b649af0f80bcccb08b0255eaea/paper-1.20.1-196.jar"),
            ["1.20"] = new("https://fill-data.papermc.io/v1/objects/1e4ccfc0599f491ee6fee4455d3722332ac5d78584fccd55cbb3b51e11504505/paper-1.20-17.jar"),
            ["1.19.4"] = new("https://fill-data.papermc.io/v1/objects/e587d78cba3e99ef8c4bc24cf20cc3bdbbe89e33b0b572070446af4eb6be5ccf/paper-1.19.4-550.jar"),
            ["1.19.3"] = new("https://fill-data.papermc.io/v1/objects/3007f2c638d5f04ed32b6adaa33053fe3634ccfa74345c83d3ea4982d38db5dc/paper-1.19.3-448.jar"),
            ["1.19.2"] = new("https://fill-data.papermc.io/v1/objects/2eb5c7459ec94bcdc597ed711d549a3ab4b0fda13e412a0792a1a069b5903864/paper-1.19.2-307.jar"),
            ["1.19.1"] = new("https://fill-data.papermc.io/v1/objects/5afe23a1fade92c547124fa874bc7d908fa676f49f09879fa876224b62e9d51b/paper-1.19.1-111.jar"),
            ["1.19"] = new("https://fill-data.papermc.io/v1/objects/0d39cacc51a77b2b071e1ce862fcbf0b4a4bd668cc7e8b313598d84fa09fabac/paper-1.19-81.jar"),
            ["1.18.2"] = new("https://fill-data.papermc.io/v1/objects/0578f18f4d632b494b468ec56b3b414b5b56fea087ee7d39cf6dcdf4c9d01f05/paper-1.18.2-388.jar"),
            ["1.18.1"] = new("https://fill-data.papermc.io/v1/objects/a94917a4472c2cbc9907a15c666bbb784f95ecd7b53c77bc08fe71103e5487f5/paper-1.18.1-216.jar"),
            ["1.18"] = new("https://fill-data.papermc.io/v1/objects/3c995f20dae4e4e21d5554fac957a0a8a5c85bd5bf34915fac4b4f16e0ef101b/paper-1.18-66.jar"),
            ["1.17.1"] = new("https://fill-data.papermc.io/v1/objects/6cc1ee2f94253ce10b5374ed85fffc735a97d8f1b64db293683dfa24dd3cc05f/paper-1.17.1-411.jar"),
            ["1.17"] = new("https://fill-data.papermc.io/v1/objects/760a93b94a58d619bd647d71af84688617d0444d22b716500bc6b343858dc871/paper-1.17-79.jar"),
            ["1.16.5"] = new("https://fill-data.papermc.io/v1/objects/e67da4851d08cde378ab2b89be58849238c303351ed2482181a99c2c2b489276/paper-1.16.5-794.jar"),
            ["1.16.4"] = new("https://fill-data.papermc.io/v1/objects/963268ed564ac7d2ec076463e921ffa09570235f587bbd1a4d91a23ca4264b66/paper-1.16.4-416.jar"),
            ["1.16.3"] = new("https://fill-data.papermc.io/v1/objects/940303ee5f5bcc08377e388ea1c1daa109c1ac8c4d189dc67de1106853f2fc23/paper-1.16.3-253.jar"),
            ["1.16.2"] = new("https://fill-data.papermc.io/v1/objects/e5e10517daaa9bd6d54a8a0d22d866e31da7c1b47cb9e425ffaac236fde75ec9/paper-1.16.2-189.jar"),
            ["1.16.1"] = new("https://fill-data.papermc.io/v1/objects/929559ba1dfc6de2904e17289fb3d1ac95f0ab48c7540cf5b8c2f055fea9d59c/paper-1.16.1-138.jar"),
            ["1.15.2"] = new("https://fill-data.papermc.io/v1/objects/bd2dd6f2cc489cf9e2bb800cb4fb6d63e9d293945d3ac10b09dd9c6098fa9f34/paper-1.15.2-393.jar"),
            ["1.15.1"] = new("https://fill-data.papermc.io/v1/objects/22a7a19f378db8edf92cdba57d91ceea7e4fa6470b677e6bbe57e8f7e1d9a4dd/paper-1.15.1-62.jar"),
            ["1.15"] = new("https://fill-data.papermc.io/v1/objects/8b726c0deb6c3a265d679a3d3a2c0f8e5243fbc6ddcfcaf42e24209cb1f829b4/paper-1.15-21.jar"),
            ["1.14.4"] = new("https://fill-data.papermc.io/v1/objects/bd8ec5cdb22370d37816a6de26798df3d2b0d6f9c7c96c88ca45a1303fea50e8/paper-1.14.4-245.jar"),
            ["1.14.3"] = new("https://fill-data.papermc.io/v1/objects/b6d2d8ac67d685141697a8cecd99c47baf604900007eb0e270fd6ea86cbbc540/paper-1.14.3-134.jar"),
            ["1.14.2"] = new("https://fill-data.papermc.io/v1/objects/12034e578e014eb369e2929f3725bd409858bf94128e46d1f286d5be36c3cb0e/paper-1.14.2-107.jar"),
            ["1.14.1"] = new("https://fill-data.papermc.io/v1/objects/2bcf8017485cc41b3e72daa7285a46f26a85d055b9d638bc9a07f77632168ad7/paper-1.14.1-50.jar"),
            ["1.14"] = new("https://fill-data.papermc.io/v1/objects/338be77f5239c44cff3f80f5c107b5e61ac48fb39348bce7249303209201072a/paper-1.14-17.jar"),
            ["1.13.2"] = new("https://fill-data.papermc.io/v1/objects/11e828d0565ab76a0a0e180c056364a95de44958cfd6a6af3f9b1dc70b03e9cd/paper-1.13.2-657.jar"),
            ["1.13.1"] = new("https://fill-data.papermc.io/v1/objects/6637401d87d0f5db5aaee90d7103f52c5e1baaf6b6d4643a5793e7b02b5775cb/paper-1.13.1-386.jar"),
            ["1.13"] = new("https://fill-data.papermc.io/v1/objects/00db82d214242c9345266d44ff8d11a8e857a1a02edf7cb5fcc2d1d973283129/paper-1.13-173.jar"),
            ["1.13-pre7"] = new("https://fill-data.papermc.io/v1/objects/8c2c4dbc3a2842be8454b4c4b306266bc622e2db681233558fedf8230800940c/paper-1.13-pre7-12.jar"),
            ["1.12.2"] = new("https://fill-data.papermc.io/v1/objects/3a2041807f492dcdc34ebb324a287414946e3e05ec3df6fd03f5b5f7d9afc210/paper-1.12.2-1620.jar"),
            ["1.12.1"] = new("https://fill-data.papermc.io/v1/objects/dba2219d674ad85e4ef2c41931d34b6fa4be75a887973ecaaf286727a03812da/paper-1.12.1-1204.jar"),
            ["1.12"] = new("https://fill-data.papermc.io/v1/objects/1e7e88a2ed6f2b70fa3f6ec6611373458c5d72b2a8707e60921df601c791e60e/paper-1.12-1169.jar"),
            ["1.11.2"] = new("https://fill-data.papermc.io/v1/objects/3d0f40ec1f9630dfdbafa626cc20c266d7fb90fc22583dc1b995e7fbfb76830d/paper-1.11.2-1106.jar"),
            ["1.10.2"] = new("https://fill-data.papermc.io/v1/objects/83354d24a22b6265e76c089b3d17a568abb446c0ccd12c2452f5e148412b16c2/paper-1.10.2-918.jar"),
            ["1.9.4"] = new("https://fill-data.papermc.io/v1/objects/15a5821ddeacc596432c3fbf24262a2d264f556060ecd6f1838fb01ab5629a81/paper-1.9.4-775.jar"),
            ["1.8.8"] = new("https://fill-data.papermc.io/v1/objects/7ff6d2cec671ef0d95b3723b5c92890118fb882d73b7f8fa0a2cd31d97c55f86/paper-1.8.8-445.jar"),
            ["1.7.10"] = new("https://fill-data.papermc.io/v1/objects/33772078d92e9dbb027602da016524ef29af5b4c12eaddac1fe2465b01108185/paper-1.7.10-2025.jar")
        };

    public static async Task DownloadAsync(
        string serverType,
        string minecraftVersion,
        string destinationPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedType = serverType.Trim();
        var normalizedVersion = minecraftVersion.Trim();
        if (string.IsNullOrWhiteSpace(normalizedVersion))
        {
            throw new InvalidOperationException("Choose a Minecraft version before downloading server software.");
        }

        var destinationFolder = Path.GetDirectoryName(destinationPath) ?? throw new InvalidOperationException("Invalid server folder.");
        Directory.CreateDirectory(destinationFolder);

        if (normalizedType.Equals("forge", StringComparison.OrdinalIgnoreCase))
        {
            await InstallForgeServerAsync(normalizedVersion, destinationFolder, destinationPath, progress, cancellationToken);
            return;
        }

        if (normalizedType.Equals("neoforge", StringComparison.OrdinalIgnoreCase))
        {
            await InstallNeoForgeServerAsync(normalizedVersion, destinationFolder, destinationPath, progress, cancellationToken);
            return;
        }

        progress?.Report($"Finding the latest stable {normalizedType} build for Minecraft {normalizedVersion}...");
        var downloadUri = normalizedType.ToLowerInvariant() switch
        {
            "paper" => await ResolvePaperDownloadAsync(PaperProject, normalizedVersion, cancellationToken),
            "purpur" => new Uri($"https://api.purpurmc.org/v2/purpur/{Uri.EscapeDataString(normalizedVersion)}/latest/download"),
            "folia" => await ResolvePaperDownloadAsync("folia", normalizedVersion, cancellationToken),
            "fabric" => await ResolveFabricDownloadAsync(normalizedVersion, cancellationToken),
            "quilt" => await ResolveQuiltDownloadAsync(normalizedVersion, cancellationToken),
            "vanilla" => await ResolveVanillaDownloadAsync(normalizedVersion, cancellationToken),
            _ => throw new NotSupportedException($"Automatic downloads are not available for {normalizedType}.")
        };

        var temporaryPath = destinationPath + ".download";
        try
        {
            progress?.Report($"Downloading {normalizedType} server software...");
            using var response = await HttpClient.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous))
            {
                await Launcher.Security.BoundedStream.CopyAsync(source, destination, 2L * 1024 * 1024 * 1024, cancellationToken);
                await destination.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, destinationPath, true);
            progress?.Report($"{normalizedType} for Minecraft {normalizedVersion} is ready.");
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
    }

    public static async Task DownloadDefaultPluginsAsync(
        string serverType,
        string minecraftVersion,
        string serverFolder,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!serverType.Equals("Paper", StringComparison.OrdinalIgnoreCase) &&
            !serverType.Equals("Purpur", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var normalizedVersion = minecraftVersion.Trim();
        var pluginsFolder = Path.Combine(serverFolder, "plugins");
        Directory.CreateDirectory(pluginsFolder);

        foreach (var plugin in DefaultPaperPlugins)
        {
            try
            {
                progress?.Report($"Finding a compatible {plugin.Project} release...");
                var downloadUri = await ResolveHangarPluginDownloadAsync(
                    plugin.Project,
                    normalizedVersion,
                    cancellationToken);
                if (downloadUri is null)
                {
                    progress?.Report($"No compatible {plugin.Project} release was found for Minecraft {normalizedVersion}.");
                    continue;
                }

                progress?.Report($"Installing {plugin.Project}...");
                await DownloadFileAsync(
                    downloadUri,
                    Path.Combine(pluginsFolder, plugin.FileName),
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                progress?.Report($"Skipped {plugin.Project}: {exception.Message}");
            }
        }

        progress?.Report("Default multi-version plugins are ready.");
    }

    private static async Task<Uri> ResolvePaperDownloadAsync(string project, string version, CancellationToken cancellationToken)
    {
        if (PaperDownloadUrls.TryGetValue(version, out var knownDownloadUri))
        {
            return knownDownloadUri;
        }

        var buildsUri = $"https://fill.papermc.io/v3/projects/{project}/versions/{Uri.EscapeDataString(version)}/builds";
        using var response = await HttpClient.GetAsync(buildsUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        foreach (var build in document.RootElement.EnumerateArray())
        {
            if (!build.TryGetProperty("channel", out var channel) ||
                !string.Equals(channel.GetString(), "STABLE", StringComparison.OrdinalIgnoreCase) ||
                !build.TryGetProperty("downloads", out var downloads) ||
                !downloads.TryGetProperty("server:default", out var serverDownload) ||
                !serverDownload.TryGetProperty("url", out var url) ||
                string.IsNullOrWhiteSpace(url.GetString()))
            {
                continue;
            }

            return new Uri(url.GetString()!);
        }

        throw new InvalidOperationException($"No stable {project} build is available for Minecraft {version}.");
    }

    private static async Task<Uri> ResolveVanillaDownloadAsync(string version, CancellationToken cancellationToken)
    {
        using var manifestResponse = await HttpClient.GetAsync(
            "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json",
            cancellationToken);
        manifestResponse.EnsureSuccessStatusCode();

        await using var manifestStream = await manifestResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var manifest = await JsonDocument.ParseAsync(manifestStream, cancellationToken: cancellationToken);
        string? versionMetadataUrl = null;
        foreach (var manifestVersion in manifest.RootElement.GetProperty("versions").EnumerateArray())
        {
            if (string.Equals(manifestVersion.GetProperty("id").GetString(), version, StringComparison.OrdinalIgnoreCase))
            {
                versionMetadataUrl = manifestVersion.GetProperty("url").GetString();
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(versionMetadataUrl))
        {
            throw new InvalidOperationException($"Minecraft {version} was not found in the official version manifest.");
        }

        using var versionResponse = await HttpClient.GetAsync(versionMetadataUrl, cancellationToken);
        versionResponse.EnsureSuccessStatusCode();
        await using var versionStream = await versionResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var versionDocument = await JsonDocument.ParseAsync(versionStream, cancellationToken: cancellationToken);
        if (!versionDocument.RootElement.TryGetProperty("downloads", out var downloads) ||
            !downloads.TryGetProperty("server", out var serverDownload) ||
            !serverDownload.TryGetProperty("url", out var serverUrl) ||
            string.IsNullOrWhiteSpace(serverUrl.GetString()))
        {
            throw new InvalidOperationException($"Minecraft {version} does not provide an official Java server download.");
        }

        return new Uri(serverUrl.GetString()!);
    }

    private static async Task<Uri?> ResolveHangarPluginDownloadAsync(
        string project,
        string minecraftVersion,
        CancellationToken cancellationToken)
    {
        var versionsUri =
            $"https://hangar.papermc.io/api/v1/projects/{HangarNamespace}/{Uri.EscapeDataString(project)}/versions" +
            $"?limit=100&offset=0&channel=Release&platform={PaperPlatform}";
        using var response = await HttpClient.GetAsync(versionsUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("result", out var versions))
        {
            return null;
        }

        foreach (var version in versions.EnumerateArray())
        {
            if (!SupportsMinecraftVersion(version, minecraftVersion) ||
                !version.TryGetProperty("downloads", out var downloads) ||
                !downloads.TryGetProperty(PaperPlatform, out var paperDownload) ||
                !paperDownload.TryGetProperty("downloadUrl", out var downloadUrl) ||
                string.IsNullOrWhiteSpace(downloadUrl.GetString()))
            {
                continue;
            }

            return new Uri(downloadUrl.GetString()!);
        }

        return null;
    }

    private static bool SupportsMinecraftVersion(JsonElement version, string minecraftVersion)
    {
        if (!version.TryGetProperty("platformDependencies", out var platformDependencies) ||
            !platformDependencies.TryGetProperty(PaperPlatform, out var supportedVersions))
        {
            return false;
        }

        return supportedVersions.EnumerateArray().Any(supportedVersion =>
            string.Equals(supportedVersion.GetString(), minecraftVersion, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task DownloadFileAsync(
        Uri downloadUri,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var temporaryPath = destinationPath + ".download";
        try
        {
            using var response = await HttpClient.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous))
            {
                await Launcher.Security.BoundedStream.CopyAsync(source, destination, 2L * 1024 * 1024 * 1024, cancellationToken);
                await destination.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, destinationPath, true);
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
    }

    private static async Task<Uri> ResolveFabricDownloadAsync(string version, CancellationToken cancellationToken)
    {
        var normalizedVersion = version.Trim();
        string loaderVersion = "0.16.10";
        string installerVersion = "1.0.1";

        try
        {
            var loaderUrl = $"https://meta.fabricmc.net/v2/versions/loader/{Uri.EscapeDataString(normalizedVersion)}";
            using var loaderResponse = await HttpClient.GetAsync(loaderUrl, cancellationToken);
            if (loaderResponse.IsSuccessStatusCode)
            {
                await using var loaderStream = await loaderResponse.Content.ReadAsStreamAsync(cancellationToken);
                using var loaderDoc = await JsonDocument.ParseAsync(loaderStream, cancellationToken: cancellationToken);
                foreach (var item in loaderDoc.RootElement.EnumerateArray())
                {
                    if (item.TryGetProperty("loader", out var loaderObj) &&
                        loaderObj.TryGetProperty("version", out var v))
                    {
                        var isStable = loaderObj.TryGetProperty("stable", out var s) && s.GetBoolean();
                        loaderVersion = v.GetString() ?? loaderVersion;
                        if (isStable)
                        {
                            break;
                        }
                    }
                }
            }
        }
        catch
        {
        }

        try
        {
            var installerUrl = "https://meta.fabricmc.net/v2/versions/installer";
            using var installerResponse = await HttpClient.GetAsync(installerUrl, cancellationToken);
            if (installerResponse.IsSuccessStatusCode)
            {
                await using var installerStream = await installerResponse.Content.ReadAsStreamAsync(cancellationToken);
                using var installerDoc = await JsonDocument.ParseAsync(installerStream, cancellationToken: cancellationToken);
                foreach (var item in installerDoc.RootElement.EnumerateArray())
                {
                    if (item.TryGetProperty("version", out var v))
                    {
                        var isStable = item.TryGetProperty("stable", out var s) && s.GetBoolean();
                        installerVersion = v.GetString() ?? installerVersion;
                        if (isStable)
                        {
                            break;
                        }
                    }
                }
            }
        }
        catch
        {
        }

        return new Uri($"https://meta.fabricmc.net/v2/versions/loader/{Uri.EscapeDataString(normalizedVersion)}/{Uri.EscapeDataString(loaderVersion)}/{Uri.EscapeDataString(installerVersion)}/server/jar");
    }

    private static async Task<Uri> ResolveQuiltDownloadAsync(string version, CancellationToken cancellationToken)
    {
        var normalizedVersion = version.Trim();
        string loaderVersion = "0.26.4";
        string installerVersion = "0.28.0";

        try
        {
            var loaderUrl = $"https://meta.quiltmc.org/v3/versions/loader/{Uri.EscapeDataString(normalizedVersion)}";
            using var loaderResponse = await HttpClient.GetAsync(loaderUrl, cancellationToken);
            if (loaderResponse.IsSuccessStatusCode)
            {
                await using var loaderStream = await loaderResponse.Content.ReadAsStreamAsync(cancellationToken);
                using var loaderDoc = await JsonDocument.ParseAsync(loaderStream, cancellationToken: cancellationToken);
                foreach (var item in loaderDoc.RootElement.EnumerateArray())
                {
                    if (item.TryGetProperty("loader", out var loaderObj) &&
                        loaderObj.TryGetProperty("version", out var v))
                    {
                        loaderVersion = v.GetString() ?? loaderVersion;
                        break;
                    }
                }
            }
        }
        catch
        {
        }

        try
        {
            var installerUrl = "https://meta.quiltmc.org/v3/versions/installer";
            using var installerResponse = await HttpClient.GetAsync(installerUrl, cancellationToken);
            if (installerResponse.IsSuccessStatusCode)
            {
                await using var installerStream = await installerResponse.Content.ReadAsStreamAsync(cancellationToken);
                using var installerDoc = await JsonDocument.ParseAsync(installerStream, cancellationToken: cancellationToken);
                foreach (var item in installerDoc.RootElement.EnumerateArray())
                {
                    if (item.TryGetProperty("version", out var v))
                    {
                        installerVersion = v.GetString() ?? installerVersion;
                        break;
                    }
                }
            }
        }
        catch
        {
        }

        return new Uri($"https://meta.quiltmc.org/v3/versions/loader/{Uri.EscapeDataString(normalizedVersion)}/{Uri.EscapeDataString(loaderVersion)}/{Uri.EscapeDataString(installerVersion)}/server/jar");
    }

    private static async Task InstallForgeServerAsync(
        string minecraftVersion,
        string serverFolder,
        string destinationPath,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report($"Resolving Forge release for Minecraft {minecraftVersion}...");
        var metadataUrl = "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";
        using var metaResponse = await HttpClient.GetAsync(metadataUrl, cancellationToken);
        metaResponse.EnsureSuccessStatusCode();
        var xml = await metaResponse.Content.ReadAsStringAsync(cancellationToken);

        var pattern = @"<version>(" + Regex.Escape(minecraftVersion) + @"-([0-9\.]+.*?))<\/version>";
        var matches = Regex.Matches(xml, pattern, RegexOptions.IgnoreCase);
        if (matches.Count == 0)
        {
            var trimmedVersion = minecraftVersion.EndsWith(".0") ? minecraftVersion[..^2] : minecraftVersion;
            pattern = @"<version>(" + Regex.Escape(trimmedVersion) + @"-([0-9\.]+.*?))<\/version>";
            matches = Regex.Matches(xml, pattern, RegexOptions.IgnoreCase);
        }

        if (matches.Count == 0)
        {
            throw new InvalidOperationException($"No Forge release found for Minecraft {minecraftVersion}.");
        }

        var fullVersion = matches[^1].Groups[1].Value;
        progress?.Report($"Downloading Forge {fullVersion} installer...");

        var installerUri = new Uri($"https://maven.minecraftforge.net/net/minecraftforge/forge/{Uri.EscapeDataString(fullVersion)}/forge-{Uri.EscapeDataString(fullVersion)}-installer.jar");
        var installerPath = Path.Combine(serverFolder, "forge-installer.jar");

        try
        {
            await DownloadFileAsync(installerUri, installerPath, cancellationToken);

            progress?.Report("Setting up Forge server files...");
            await RunInstallerAsync(installerPath, minecraftVersion, progress, cancellationToken);

            var legacyJar = Directory.GetFiles(serverFolder, "forge-*.jar", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(f => !Path.GetFileName(f).Equals("forge-installer.jar", StringComparison.OrdinalIgnoreCase));

            if (legacyJar != null && !File.Exists(destinationPath))
            {
                File.Copy(legacyJar, destinationPath, true);
            }
            if (!ServerLaunchFiles.IsReady(serverFolder, Path.GetFileName(destinationPath), "Forge"))
                throw new IOException("Forge installation did not produce launchable server files.");

            progress?.Report($"Forge {fullVersion} server is ready.");
        }
        finally
        {
            try { if (File.Exists(installerPath)) File.Delete(installerPath); } catch { }
            try { var logPath = installerPath + ".log"; if (File.Exists(logPath)) File.Delete(logPath); } catch { }
        }
    }

    private static async Task InstallNeoForgeServerAsync(
        string minecraftVersion,
        string serverFolder,
        string destinationPath,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report($"Resolving NeoForge release for Minecraft {minecraftVersion}...");
        var metadataUrl = "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";
        using var metaResponse = await HttpClient.GetAsync(metadataUrl, cancellationToken);
        metaResponse.EnsureSuccessStatusCode();
        var xml = await metaResponse.Content.ReadAsStringAsync(cancellationToken);

        string prefix;
        var parts = minecraftVersion.Split('.');
        if (parts.Length >= 2 && parts[0] == "1")
        {
            var major = parts[1];
            var minor = parts.Length > 2 ? parts[2] : "0";
            prefix = $"{major}.{minor}.";
        }
        else
        {
            prefix = minecraftVersion + ".";
        }

        var pattern = @"<version>(" + Regex.Escape(prefix) + @"[0-9\.]+.*?)<\/version>";
        var matches = Regex.Matches(xml, pattern, RegexOptions.IgnoreCase);

        if (matches.Count == 0)
        {
            throw new InvalidOperationException($"No NeoForge release found for Minecraft {minecraftVersion}.");
        }

        var selectedVersion = matches
            .Select(m => m.Groups[1].Value)
            .LastOrDefault(v => !v.Contains("beta", StringComparison.OrdinalIgnoreCase) && !v.Contains("alpha", StringComparison.OrdinalIgnoreCase))
            ?? matches[^1].Groups[1].Value;

        progress?.Report($"Downloading NeoForge {selectedVersion} installer...");
        var installerUri = new Uri($"https://maven.neoforged.net/releases/net/neoforged/neoforge/{Uri.EscapeDataString(selectedVersion)}/neoforge-{Uri.EscapeDataString(selectedVersion)}-installer.jar");
        var installerPath = Path.Combine(serverFolder, "neoforge-installer.jar");

        try
        {
            await DownloadFileAsync(installerUri, installerPath, cancellationToken);

            progress?.Report("Setting up NeoForge server files...");
            await RunInstallerAsync(installerPath, minecraftVersion, progress, cancellationToken);

            if (!ServerLaunchFiles.IsReady(serverFolder, Path.GetFileName(destinationPath), "NeoForge"))
                throw new IOException("NeoForge installation did not produce launchable server files.");

            progress?.Report($"NeoForge {selectedVersion} server is ready.");
        }
        finally
        {
            try { if (File.Exists(installerPath)) File.Delete(installerPath); } catch { }
            try { var logPath = installerPath + ".log"; if (File.Exists(logPath)) File.Delete(logPath); } catch { }
        }
    }

    private static async Task RunInstallerAsync(string installerPath, string minecraftVersion,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ServerLaunchFiles.ResolveJava(minecraftVersion),
            WorkingDirectory = Path.GetDirectoryName(installerPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-jar");
        startInfo.ArgumentList.Add(Path.GetFileName(installerPath));
        startInfo.ArgumentList.Add("--installServer");
        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) progress?.Report(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) progress?.Report(e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
                throw new IOException($"Server installer failed with exit code {process.ExitCode}. Check the console log.");
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("UBLauncher/1.0 (https://github.com/iamnabil12/UB_Launcher)");
        return client;
    }

    private sealed record DefaultPaperPlugin(string Project, string FileName);
}
