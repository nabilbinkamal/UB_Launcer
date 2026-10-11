namespace Launcher.Security;

public static class SafeArchivePath
{
    public static string Resolve(string rootPath, string relativePath)
    {
        var parts = relativePath?.Replace('\\', '/').Split('/') ?? [];
        if (parts.Length == 0 || parts.Any(part => string.IsNullOrWhiteSpace(part) ||
            part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
            part.Any(c => c < 32 || "<>:\"|?*".Contains(c)) || IsDeviceName(part)))
            throw new InvalidDataException("The pack contains an unsafe file path.");

        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!destination.StartsWith(root, comparison))
            throw new InvalidDataException("The pack contains an unsafe file path.");

        var current = Path.TrimEndingDirectorySeparator(root);
        foreach (var part in parts.Prepend(string.Empty))
        {
            current = Path.Combine(current, part);
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Pack destinations cannot contain symbolic links.");
        }
        return destination;
    }

    private static bool IsDeviceName(string part)
    {
        var stem = part.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" ||
               (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) &&
                stem[3] is >= '0' and <= '9');
    }
}
