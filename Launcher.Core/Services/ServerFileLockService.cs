using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Launcher.Core.Services;

internal static class ServerFileLockService
{
    private const int ErrorMoreData = 234;
    private const int CchRmSessionKey = 32;
    private const int CchRmMaxAppName = 255;
    private const int CchRmMaxSvcName = 63;

    public static IReadOnlyList<int> FindLockingProcessIds(string serverFolder)
    {
        if (!Directory.Exists(serverFolder))
        {
            return [];
        }

        var files = Directory.EnumerateFiles(serverFolder, "*.jar", SearchOption.AllDirectories)
            .Take(500)
            .ToArray();
        if (files.Length == 0)
        {
            return [];
        }

        var startResult = RmStartSession(out var session, 0, new StringBuilder(CchRmSessionKey));
        if (startResult != 0)
        {
            return [];
        }

        try
        {
            if (RmRegisterResources(session, (uint)files.Length, files, 0, null, 0, null) != 0)
            {
                return [];
            }

            uint needed = 0;
            uint count = 0;
            uint rebootReasons = 0;
            var result = RmGetList(session, out needed, ref count, null, ref rebootReasons);
            if (result != ErrorMoreData || needed == 0)
            {
                return [];
            }

            var processes = new RmProcessInfo[needed];
            count = needed;
            result = RmGetList(session, out needed, ref count, processes, ref rebootReasons);
            return result == 0
                ? processes.Take((int)count).Select(process => process.Process.dwProcessId).Distinct().ToArray()
                : [];
        }
        finally
        {
            RmEndSession(session);
        }
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint sessionHandle, int sessionFlags, StringBuilder sessionKey);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint sessionHandle,
        uint fileCount,
        string[] files,
        uint applicationCount,
        string[]? applications,
        uint serviceCount,
        string[]? services);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(
        uint sessionHandle,
        out uint processInfoNeeded,
        ref uint processInfo,
        [In, Out] RmProcessInfo[]? affectedApplications,
        ref uint rebootReasons);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint sessionHandle);

    [StructLayout(LayoutKind.Sequential)]
    private struct RmUniqueProcess
    {
        public int dwProcessId;
        public FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RmProcessInfo
    {
        public RmUniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxAppName + 1)] public string AppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxSvcName + 1)] public string ServiceShortName;
        public uint ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)] public bool Restartable;
    }
}
