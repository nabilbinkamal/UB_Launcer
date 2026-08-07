using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Launcher.Core.Services;

public static class PlaytimeStore
{
    private static readonly object Sync = new();
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UBLauncher",
        "playtime.json");
    private static readonly Dictionary<string, double> CompletedSeconds = Load();
    private static readonly Dictionary<int, ActiveSession> ActiveSessions = new();

    public static double GetTotalSeconds(string username)
    {
        var key = NormalizeUsername(username);
        if (string.IsNullOrEmpty(key))
        {
            return 0;
        }

        lock (Sync)
        {
            var totalSeconds = CompletedSeconds.GetValueOrDefault(key);
            foreach (var session in ActiveSessions.Values)
            {
                if (session.Username.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    totalSeconds += Math.Max(0, (DateTimeOffset.UtcNow - session.StartedAt).TotalSeconds);
                }
            }

            return totalSeconds;
        }
    }

    public static void TrackSession(string username, Process gameProcess)
    {
        var key = NormalizeUsername(username);
        if (string.IsNullOrEmpty(key) || gameProcess.HasExited)
        {
            return;
        }

        var session = new ActiveSession(key, gameProcess.Id, gameProcess, DateTimeOffset.UtcNow);
        lock (Sync)
        {
            ActiveSessions[session.ProcessId] = session;
        }

        _ = CompleteSessionWhenGameExitsAsync(session);
    }

    private static async Task CompleteSessionWhenGameExitsAsync(ActiveSession session)
    {
        try
        {
            await session.Process.WaitForExitAsync();
        }
        catch
        {
        }
        finally
        {
            lock (Sync)
            {
                if (ActiveSessions.Remove(session.ProcessId))
                {
                    CompletedSeconds[session.Username] = CompletedSeconds.GetValueOrDefault(session.Username) +
                                                         Math.Max(0, (DateTimeOffset.UtcNow - session.StartedAt).TotalSeconds);
                    Save();
                }
            }

            session.Process.Dispose();
        }
    }

    private static Dictionary<string, double> Load()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            }

            var storedValues = JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(StorePath));
            return storedValues is null
                ? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, double>(storedValues, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, JsonSerializer.Serialize(CompletedSeconds));
    }

    private static string NormalizeUsername(string username) => username.Trim();

    private sealed record ActiveSession(string Username, int ProcessId, Process Process, DateTimeOffset StartedAt);
}
