using CmlLib.Core.Auth;

namespace Launcher.Core.Services;

public static class GameAccountSession
{
    public static async Task<MSession?> GetAsync(string username, CancellationToken cancellationToken = default)
    {
        if (!MicrosoftAccountStore.HasSession ||
            !string.Equals(MicrosoftAccountStore.MinecraftUsername, username, StringComparison.OrdinalIgnoreCase))
            return null;

        var account = await MicrosoftAccountStore.GetLaunchSessionAsync(cancellationToken)
            ?? throw new InvalidOperationException("Your Microsoft session expired. Sign in again before launching.");
        return new MSession
        {
            Username = account.Username,
            UUID = account.Uuid,
            AccessToken = account.Token,
            UserType = "msa"
        };
    }
}
