using fNbt;
using System;
using System.IO;
using System.Linq;

namespace Launcher.Core.Services;

public static class MinecraftServerInjector
{
    public static void InjectPartnerServers(string? gameDirectory)
    {
        try
        {
            var root = string.IsNullOrWhiteSpace(gameDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UBLauncher", "minecraft")
                : gameDirectory;
                
            var serversDatPath = Path.Combine(root, "servers.dat");
            NbtFile file;
            NbtList? serversList;

            if (File.Exists(serversDatPath))
            {
                try
                {
                    file = new NbtFile(serversDatPath);
                }
                catch
                {
                    // Preserve the player's server list if it cannot be parsed.
                    return;
                }
                
                serversList = file.RootTag.Get<NbtList>("servers");
                if (serversList == null)
                {
                    serversList = new NbtList("servers", NbtTagType.Compound);
                    file.RootTag.Add(serversList);
                }
            }
            else
            {
                file = new NbtFile(new NbtCompound(""));
                serversList = new NbtList("servers", NbtTagType.Compound);
                file.RootTag.Add(serversList);
            }

            var partnerServers = LauncherRemoteConfiguration.Current.PartnerServers
                .Where(s => s.Enabled)
                .ToList();

            if (!partnerServers.Any())
                return;

            var partnerIps = partnerServers.Select(p => p.Address.ToLowerInvariant()).ToHashSet();
            
            // Remove existing partner servers from the list
            for (int i = serversList.Count - 1; i >= 0; i--)
            {
                var serverTag = serversList[i] as NbtCompound;
                var ipTag = serverTag?.Get<NbtString>("ip");
                if (ipTag != null && partnerIps.Contains(ipTag.Value.ToLowerInvariant()))
                {
                    serversList.RemoveAt(i);
                }
            }

            // Insert partner servers at the top
            for (int i = partnerServers.Count - 1; i >= 0; i--)
            {
                var partner = partnerServers[i];
                var name = partner.Name;
                if (!name.Contains("★"))
                {
                    name = "§6★ §r" + name;
                }

                var newServerTag = new NbtCompound
                {
                    new NbtString("name", name),
                    new NbtString("ip", partner.Address)
                };
                
                serversList.Insert(0, newServerTag);
            }

            Directory.CreateDirectory(root);
            var temporaryPath = serversDatPath + ".tmp";
            file.SaveToFile(temporaryPath, NbtCompression.None);
            File.Move(temporaryPath, serversDatPath, overwrite: true);
        }
        catch (Exception)
        {
            // Do not crash if injection fails
        }
    }
}
