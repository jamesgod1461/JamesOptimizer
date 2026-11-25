using System;
using System.Collections.Generic;

namespace JamesOptimizer.Core;

public static class CoreConstants
{
    public const string ProductName = "JamesOptimizer";
    public const string AppVersion = "3.0.0";
    public const string PublisherName = "James God";
    public const string ServiceName = "JamesOptimizer Background Service";
    public const string PipeName = "JamesOptimizerPipe";
    public const string ProtocolVersion = "3.1";

    public const string UltimatePerformanceGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    public const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    public const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
    public const string PowerSaverGuid = "a1841308-3541-4fab-bc81-f71556f20b4a";

    public const string RegistryConfigRoot = @"HKEY_LOCAL_MACHINE\SOFTWARE\JamesOptimizer";
    public const string UserConfigSubKey = @"Software\JamesOptimizer";

    /// <summary>
    /// Critical Windows security, update, and core OS services that NO code path may disable.
    /// </summary>
    public static readonly IReadOnlySet<string> ProtectedServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "WinDefend",
        "Sense",
        "WdNisSvc",
        "SecurityHealthService",
        "wscsvc",
        "mpssvc",
        "BFE",
        "EventLog",
        "RpcSs",
        "RpcEptMapper",
        "DcomLaunch",
        "TrustedInstaller",
        "wuauserv",
        "UsoSvc",
        "WaaSMedicSvc",
        "CryptSvc",
        "SamSs",
        "LSM",
        "PlugPlay",
        "Power",
        "ProfSvc",
        "Winmgmt",
        "Schedule",
        "Dhcp",
        "Dnscache",
        "nsi",
        "LanmanWorkstation",
        "LanmanServer",
        ServiceName
    };

    /// <summary>
    /// Critical Windows processes that MUST NOT be terminated or suspended.
    /// </summary>
    public static readonly IReadOnlySet<string> ProtectedProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "System",
        "Idle",
        "Registry",
        "Memory Compression",
        "smss",
        "csrss",
        "wininit",
        "services",
        "lsass",
        "lsm",
        "winlogon",
        "svchost",
        "dwm",
        "fontdrvhost",
        "audiodg",
        "MsMpEng",
        "NisSrv",
        "SecurityHealthService",
        "JamesOptimizer.Service"
    };

    /// <summary>
    /// Built-in Windows executables/commands that Win+R alias creation must never shadow.
    /// </summary>
    public static readonly IReadOnlySet<string> ProtectedRunAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "cmd", "powershell", "pwsh", "regedit", "reg", "sc", "netsh", "bcdedit",
        "fsutil", "taskmgr", "control", "explorer", "mmc", "msiexec", "wscript",
        "cscript", "mshta", "rundll32", "conhost", "services", "diskpart", "format",
        "shutdown", "sethc", "utilman", "winver", "msconfig", "dxdiag", "cleanmgr",
        "notepad", "calc", "mspaint", "mstsc", "secpol", "gpedit", "lusrmgr", "devmgmt",
        "compmgmt", "perfmon", "resmon", "eventvwr", "dism", "sfc", "winget"
    };

    public static bool IsProtectedService(string? serviceName) =>
        !string.IsNullOrWhiteSpace(serviceName) && ProtectedServices.Contains(serviceName.Trim());

    public static bool IsProtectedProcess(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return true;
        string clean = processName.Trim();
        if (clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            clean = clean[..^4];
        return ProtectedProcesses.Contains(clean);
    }
}
