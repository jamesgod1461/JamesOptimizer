using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JamesOptimizer.Core;

public sealed class LockingProcessInfo
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ServiceShortName { get; set; } = "";
    public string ApplicationType { get; set; } = "";
    public bool Restartable { get; set; }
    public bool IsCritical { get; set; }
    public string ExecutablePath { get; set; } = "";
}

[SupportedOSPlatform("windows")]
public static class FileLockService
{
    private const int RmRebootReasonNone = 0;
    private const int CCH_RM_MAX_APP_NAME = 255;
    private const int CCH_RM_MAX_SVC_NAME = 63;

    public static IReadOnlyList<LockingProcessInfo> FindLockingProcesses(string targetPath)
    {
        var results = new List<LockingProcessInfo>();
        if (string.IsNullOrWhiteSpace(targetPath)) return results;

        uint sessionHandle = 0;
        string sessionKey = Guid.NewGuid().ToString("N");
        int res = RmStartSession(out sessionHandle, 0, sessionKey);
        if (res != 0) return results;

        try
        {
            string[] resources = { targetPath };
            res = RmRegisterResources(sessionHandle, (uint)resources.Length, resources, 0, null, 0, null);
            if (res != 0) return results;

            uint pnProcInfoNeeded = 0;
            uint pnProcInfo = 0;
            uint lpdwRebootReasons = RmRebootReasonNone;

            res = RmGetList(sessionHandle, out pnProcInfoNeeded, ref pnProcInfo, null, ref lpdwRebootReasons);
            if (res == 234 && pnProcInfoNeeded > 0) // ERROR_MORE_DATA
            {
                var processInfo = new RM_PROCESS_INFO[pnProcInfoNeeded];
                pnProcInfo = pnProcInfoNeeded;
                res = RmGetList(sessionHandle, out pnProcInfoNeeded, ref pnProcInfo, processInfo, ref lpdwRebootReasons);
                if (res == 0)
                {
                    for (int i = 0; i < pnProcInfo; i++)
                    {
                        int pid = processInfo[i].Process.dwProcessId;
                        string procName = "";
                        string exePath = "";
                        try
                        {
                            using var p = Process.GetProcessById(pid);
                            procName = p.ProcessName;
                            try { exePath = p.MainModule?.FileName ?? ""; } catch { }
                        }
                        catch { }

                        results.Add(new LockingProcessInfo
                        {
                            ProcessId = pid,
                            ProcessName = string.IsNullOrEmpty(procName) ? processInfo[i].strAppName : procName,
                            DisplayName = processInfo[i].strAppName,
                            ServiceShortName = processInfo[i].strServiceShortName,
                            ApplicationType = processInfo[i].ApplicationType.ToString(),
                            Restartable = processInfo[i].bRestartable,
                            IsCritical = CoreConstants.IsProtectedProcess(procName) || processInfo[i].ApplicationType == RM_APP_TYPE.RmCritical,
                            ExecutablePath = exePath
                        });
                    }
                }
            }
        }
        finally
        {
            RmEndSession(sessionHandle);
        }

        return results;
    }

    public static (bool Ok, string Message) ShutdownGracefully(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath)) return (false, "Target path empty.");
        var locks = FindLockingProcesses(targetPath);
        foreach (var l in locks)
        {
            if (l.IsCritical)
                return (false, $"Blocked: critical OS process '{l.ProcessName}' (PID {l.ProcessId}) cannot be shut down.");
        }

        int res = RmStartSession(out uint sessionHandle, 0, Guid.NewGuid().ToString("N"));
        if (res != 0) return (false, $"RmStartSession failed ({res}).");
        try
        {
            string[] resources = { targetPath };
            res = RmRegisterResources(sessionHandle, 1, resources, 0, null, 0, null);
            if (res != 0) return (false, $"RmRegisterResources failed ({res}).");

            // BUG FIX: RmShutdown's callback parameter must be IntPtr (IntPtr.Zero), not Nullable<IntPtr> which throws MarshalDirectiveException.
            res = RmShutdown(sessionHandle, 0, IntPtr.Zero);
            return res == 0
                ? (true, "Restart Manager closed locking applications gracefully.")
                : (false, $"Graceful shutdown returned code {res}. Force close may be required.");
        }
        finally
        {
            RmEndSession(sessionHandle);
        }
    }

    public static (bool Ok, string Message) TerminateLockingProcess(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            if (CoreConstants.IsProtectedProcess(proc.ProcessName))
                return (false, $"Blocked: '{proc.ProcessName}' is a protected critical Windows process.");

            proc.Kill(entireProcessTree: true);
            proc.WaitForExit(3000);
            return (true, $"Terminated '{proc.ProcessName}' (PID {pid}).");
        }
        catch (Exception ex)
        {
            return (false, $"Terminate failed: {ex.Message}");
        }
    }

    #region Restart Manager P/Invoke

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    private enum RM_APP_TYPE
    {
        RmUnknownApp = 0,
        RmMainWindow = 1,
        RmOtherWindow = 2,
        RmService = 3,
        RmExplorer = 4,
        RmConsole = 5,
        RmCritical = 1000
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_APP_NAME + 1)]
        public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_SVC_NAME + 1)]
        public string strServiceShortName;
        public RM_APP_TYPE ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint pSessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint pSessionHandle,
        uint nFiles, string[] rgsFilenames,
        uint nApplications, [In] RM_UNIQUE_PROCESS[]? rgApplications,
        uint nServices, string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(
        uint dwSessionHandle,
        out uint pnProcInfoNeeded,
        ref uint pnProcInfo,
        [In, Out] RM_PROCESS_INFO[]? rgAffectedApps,
        ref uint lpdwRebootReasons);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmShutdown(uint pSessionHandle, uint lActionFlags, IntPtr fnStatus);

    #endregion
}
