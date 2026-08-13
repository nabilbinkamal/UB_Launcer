using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Launcher.Core.Services;

internal static partial class ModrinthAppProfileArtworkResolver
{
    private const long MaximumIconBytes = 8L * 1024 * 1024;
    private static int sqliteInitialized;

    public static string TryCopyToInstance(
        string profileName,
        string instancePath,
        string? importedArchiveName = null)
    {
        try
        {
            var sourcePath = TryResolveSourcePath(profileName, importedArchiveName);
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                return string.Empty;
            }

            var sourceInfo = new FileInfo(sourcePath);
            if (sourceInfo.Length is <= 0 or > MaximumIconBytes)
            {
                return string.Empty;
            }

            var extension = NormalizeImageExtension(sourceInfo.Extension);
            var destinationDirectory = Path.Combine(instancePath, ".ublauncher");
            Directory.CreateDirectory(destinationDirectory);
            var destinationPath = Path.Combine(destinationDirectory, "profile-icon" + extension);
            File.Copy(sourcePath, destinationPath, overwrite: true);
            return destinationPath;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string TryResolveSourcePath(string profileName, string? importedArchiveName)
    {
        var databasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ModrinthApp",
            "app.db");
        if (!File.Exists(databasePath))
        {
            return string.Empty;
        }

        EnsureSqliteInitialized();
        var baseName = ImportSuffixRegex().Replace(profileName.Trim(), string.Empty);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 2
        }.ToString();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = """
            SELECT i.icon_path
            FROM instances AS i
            LEFT JOIN instance_links AS links ON links.instance_id = i.id
            WHERE i.icon_path IS NOT NULL
              AND TRIM(i.icon_path) <> ''
              AND (
                    LOWER(i.name) = LOWER($profileName)
                 OR LOWER(i.name) = LOWER($baseName)
                 OR LOWER(links.imported_filename) = LOWER($archiveName)
              )
            ORDER BY CASE
                WHEN LOWER(i.name) = LOWER($profileName) THEN 0
                WHEN LOWER(links.imported_filename) = LOWER($archiveName) THEN 1
                ELSE 2
            END
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$profileName", profileName);
        command.Parameters.AddWithValue("$baseName", baseName);
        command.Parameters.AddWithValue("$archiveName", importedArchiveName ?? string.Empty);
        return command.ExecuteScalar() as string ?? string.Empty;
    }

    private static void EnsureSqliteInitialized()
    {
        if (Interlocked.Exchange(ref sqliteInitialized, 1) == 0)
        {
            SQLitePCL.Batteries_V2.Init();
        }
    }

    private static string NormalizeImageExtension(string extension) => extension.ToLowerInvariant() switch
    {
        ".jpg" => ".jpg",
        ".jpeg" => ".jpeg",
        ".webp" => ".webp",
        _ => ".png"
    };

    [GeneratedRegex(@"\s+\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex ImportSuffixRegex();
}
