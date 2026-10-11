using System.Security.Cryptography;

namespace Launcher.Security;

public static class ProtectedSessionFile
{
    public static void Write(string path, byte[] plaintext)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Persistent sign-in requires a platform credential store.");

        var encrypted = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporaryPath, encrypted);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public static byte[] Read(string path)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Persistent sign-in requires a platform credential store.");
        return ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
    }
}
