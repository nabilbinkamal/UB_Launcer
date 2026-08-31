namespace Launcher.Core.Services;

public static class OfflineAccountStore
{
    public static event Action? AccountChanged;
    private static readonly string AccountDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UBLauncher");
    private static readonly string AccountFile = Path.Combine(AccountDirectory, "offline-account.txt");
    private static readonly string SavedAccountsFile = Path.Combine(AccountDirectory, "offline-accounts.txt");
    private static readonly List<string> SavedAccounts = LoadSavedAccounts();

    public static string Username { get; private set; } = Load();
    public static IReadOnlyList<string> Accounts => SavedAccounts;

    public static void SetUsername(string username)
    {
        var normalizedUsername = username.Trim();
        if (string.IsNullOrWhiteSpace(normalizedUsername))
            return;

        Username = normalizedUsername;
        Directory.CreateDirectory(AccountDirectory);
        File.WriteAllText(AccountFile, Username);

        if (!SavedAccounts.Contains(Username, StringComparer.OrdinalIgnoreCase))
        {
            SavedAccounts.Add(Username);
            SaveAccounts();
        }

        AccountChanged?.Invoke();
    }

    public static void Clear()
    {
        Username = string.Empty;
        if (File.Exists(AccountFile)) File.Delete(AccountFile);
        AccountChanged?.Invoke();
    }

    public static void Remove(string username)
    {
        var match = SavedAccounts.FirstOrDefault(account =>
            string.Equals(account, username, StringComparison.OrdinalIgnoreCase));
        if (match is null) return;
        SavedAccounts.Remove(match);
        if (string.Equals(Username, match, StringComparison.OrdinalIgnoreCase))
        {
            Username = SavedAccounts.FirstOrDefault() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(Username))
            {
                if (File.Exists(AccountFile)) File.Delete(AccountFile);
            }
            else
            {
                Directory.CreateDirectory(AccountDirectory);
                File.WriteAllText(AccountFile, Username);
            }
        }
        SaveAccounts();
        AccountChanged?.Invoke();
    }

    private static string Load()
    {
        try { return File.Exists(AccountFile) ? File.ReadAllText(AccountFile).Trim() : string.Empty; }
        catch { return string.Empty; }
    }

    private static List<string> LoadSavedAccounts()
    {
        try
        {
            var accounts = File.Exists(SavedAccountsFile)
                ? File.ReadAllLines(SavedAccountsFile)
                    .Select(account => account.Trim())
                    .Where(account => !string.IsNullOrWhiteSpace(account))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : new List<string>();

            var currentAccount = Load();
            if (!string.IsNullOrWhiteSpace(currentAccount) &&
                !accounts.Contains(currentAccount, StringComparer.OrdinalIgnoreCase))
            {
                accounts.Add(currentAccount);
                Directory.CreateDirectory(AccountDirectory);
                File.WriteAllLines(SavedAccountsFile, accounts);
            }

            return accounts;
        }
        catch
        {
            return new List<string>();
        }
    }

    private static void SaveAccounts()
    {
        try
        {
            Directory.CreateDirectory(AccountDirectory);
            File.WriteAllLines(SavedAccountsFile, SavedAccounts);
        }
        catch
        {
            // The active account remains usable even if the saved-account list cannot be persisted.
        }
    }
}
