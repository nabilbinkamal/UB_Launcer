namespace Launcher.Core.Services;

public static class OfflineAccountStore
{
    public static event Action? AccountChanged;
    private static readonly string AccountFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UBLauncher", "offline-account.txt");
    public static string Username { get; private set; } = Load();

    public static void SetUsername(string username)
    {
        Username = username.Trim();
        Directory.CreateDirectory(Path.GetDirectoryName(AccountFile)!);
        File.WriteAllText(AccountFile, Username);
        AccountChanged?.Invoke();
    }

    public static void Clear()
    {
        Username = string.Empty;
        if (File.Exists(AccountFile)) File.Delete(AccountFile);
        AccountChanged?.Invoke();
    }

    private static string Load()
    {
        try { return File.Exists(AccountFile) ? File.ReadAllText(AccountFile).Trim() : string.Empty; }
        catch { return string.Empty; }
    }
}
