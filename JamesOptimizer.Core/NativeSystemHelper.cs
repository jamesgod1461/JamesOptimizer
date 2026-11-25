using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JamesOptimizer.Core;

public readonly record struct ToolRunResult(int ExitCode, string StdOut, string StdErr);

public readonly record struct MemoryBreakdown(
    double TotalPhysicalMb,
    double AvailablePhysicalMb,
    double SystemCacheMb,
    double CommittedMb,
    double CommitLimitMb,
    double StandbyEstimateMb);

[SupportedOSPlatform("windows")]
public static class NativeSystemHelper
{
    private const int SystemMemoryListInformation = 80;
    private const int MemoryFlushModifiedList = 3;
    private const int MemoryPurgeStandbyList = 4;
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const uint SePrivilegeEnabled = 2;

    public static string GetSystem32Exe(string exeName)
    {
        if (string.IsNullOrWhiteSpace(exeName) || exeName.IndexOfAny(new[] { '\\', '/', ':' }) >= 0)
            throw new ArgumentException("Executable name must be a bare System32 file name.", nameof(exeName));
        return Path.Combine(Environment.SystemDirectory, exeName);
    }

    public static ToolRunResult RunSystem32ToolSync(string exeName, IReadOnlyList<string> args, int timeoutMs = 15000)
    {
        string exePath = GetSystem32Exe(exeName);
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to launch {exePath}");
        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        if (!proc.WaitForExit(timeoutMs))
        {
            try { proc.Kill(true); } catch { }
            return new ToolRunResult(-1, stdout, "Process timed out.");
        }
        return new ToolRunResult(proc.ExitCode, stdout, stderr);
    }

    public static async Task<ToolRunResult> RunSystem32ToolAsync(string exeName, IReadOnlyList<string> args, CancellationToken ct = default)
    {
        string exePath = GetSystem32Exe(exeName);
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.Start();

        using var reg = ct.Register(() =>
        {
            try { if (!proc.HasExited) proc.Kill(true); } catch { }
        });

        Task<string> outTask = proc.StandardOutput.ReadToEndAsync(ct);
        Task<string> errTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        return new ToolRunResult(proc.ExitCode, await outTask, await errTask);
    }

    public static bool FlushDnsNative()
    {
        try
        {
            return DnsFlushResolverCache() != 0;
        }
        catch
        {
            return false;
        }
    }

    public static void BroadcastEnvironmentChange()
    {
        try
        {
            SendMessageTimeoutW(new IntPtr(0xFFFF), 0x001A, UIntPtr.Zero, "Environment", 0x0002, 2000, out _);
        }
        catch { }
    }

    public static bool MoveToRecycleBin(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            return false;

        var op = new SHFILEOPSTRUCTW
        {
            wFunc = FO_DELETE,
            pFrom = fullPath + "\0\0",
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT
        };
        return SHFileOperationW(ref op) == 0 && !op.fAnyOperationsAborted;
    }

    public static bool ScheduleDeleteOnReboot(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        return MoveFileExW(path, null, MOVEFILE_DELAY_UNTIL_REBOOT);
    }

    public static (bool Success, string Message) CreateRestorePointNative(string description)
    {
        try
        {
            var info = new RESTOREPOINTINFOW
            {
                dwEventType = BEGIN_SYSTEM_CHANGE,
                dwRestorePtType = MODIFY_SETTINGS,
                llSequenceNumber = 0,
                szDescription = description.Length > 255 ? description[..255] : description
            };

            if (!SRSetRestorePointW(ref info, out var status))
            {
                int err = status.nStatus;
                if (err == 1058)
                    return (false, "System Restore service is disabled for this system.");
                if (err == 1450 || err == 112)
                    return (false, "Insufficient disk space or shadow-copy quota for System Restore.");
                return (false, $"SRSetRestorePoint failed (status {err}). Run as Administrator or check System Protection.");
            }

            return (true, $"Restore point '{description}' created (seq #{status.llSequenceNumber}).");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static MemoryBreakdown GetMemoryBreakdown()
    {
        var ms = new MEMORYSTATUSEX();
        GlobalMemoryStatusEx(ms);

        double totalMb = ms.ullTotalPhys / (1024.0 * 1024.0);
        double availMb = ms.ullAvailPhys / (1024.0 * 1024.0);
        double cacheMb = 0;
        double committedMb = 0;
        double commitLimitMb = 0;

        var pi = new PERFORMANCE_INFORMATION { cb = (uint)Marshal.SizeOf<PERFORMANCE_INFORMATION>() };
        if (GetPerformanceInfo(out pi, pi.cb))
        {
            double pageMb = pi.PageSize.ToUInt64() / (1024.0 * 1024.0);
            cacheMb = pi.SystemCache.ToUInt64() * pageMb;
            committedMb = pi.CommitTotal.ToUInt64() * pageMb;
            commitLimitMb = pi.CommitLimit.ToUInt64() * pageMb;
        }

        double standbyEst = Math.Max(0, availMb - (cacheMb * 0.25));
        return new MemoryBreakdown(totalMb, availMb, cacheMb, committedMb, commitLimitMb, Math.Min(availMb, Math.Max(cacheMb, standbyEst * 0.6)));
    }

    public static (int TrimmedProcesses, long ReclaimedBytes) PurgeMemoryListsNative(
        bool emptyWorkingSets,
        bool flushModified,
        bool purgeStandby,
        CancellationToken ct = default)
    {
        var before = new MEMORYSTATUSEX();
        GlobalMemoryStatusEx(before);

        int trimmed = 0;
        if (emptyWorkingSets)
        {
            var procs = Process.GetProcesses();
            try
            {
                foreach (var p in procs)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        if (p.Id > 4 && !CoreConstants.IsProtectedProcess(p.ProcessName))
                        {
                            EmptyWorkingSet(p.Handle);
                            trimmed++;
                        }
                    }
                    catch { }
                }
            }
            finally
            {
                foreach (var p in procs) p.Dispose();
            }
        }

        if (flushModified || purgeStandby)
        {
            ExecuteMemoryListCommands(flushModified, purgeStandby);
        }

        var after = new MEMORYSTATUSEX();
        GlobalMemoryStatusEx(after);
        long reclaimed = after.ullAvailPhys > before.ullAvailPhys
            ? (long)(after.ullAvailPhys - before.ullAvailPhys)
            : 0;

        return (trimmed, reclaimed);
    }

    private static void ExecuteMemoryListCommands(bool flushModified, bool purgeStandby)
    {
        if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TokenAdjustPrivileges | TokenQuery, out IntPtr tokenHandle))
            return;

        try
        {
            if (!LookupPrivilegeValueW(null, "SeProfileSingleProcessPrivilege", out LUID luid))
                return;

            var tp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Privileges = new LUID_AND_ATTRIBUTES[] { new() { Luid = luid, Attributes = SePrivilegeEnabled } }
            };
            AdjustTokenPrivileges(tokenHandle, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);

            var cmds = new List<int>();
            if (flushModified) cmds.Add(MemoryFlushModifiedList);
            if (purgeStandby) cmds.Add(MemoryPurgeStandbyList);

            foreach (int cmd in cmds)
            {
                IntPtr ptr = Marshal.AllocHGlobal(sizeof(int));
                try
                {
                    Marshal.WriteInt32(ptr, cmd);
                    NtSetSystemInformation(SystemMemoryListInformation, ptr, sizeof(int));
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }
        }
        finally
        {
            CloseHandle(tokenHandle);
        }
    }

    public static bool VerifyAuthenticodeSignature(string filePath, out string? signerSubject) =>
        VerifyAuthenticodeDetails(filePath, out int status, out signerSubject, out _) && status == 0;

    internal static bool VerifyAuthenticodeDetails(
        string filePath,
        out int winTrustStatus,
        out string? signerSubject,
        out string? signerThumbprint)
    {
        winTrustStatus = -1;
        signerSubject = null;
        signerThumbprint = null;
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return false;

        try
        {
            var fileInfo = new WINTRUST_FILE_INFO
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
                pcwszFilePath = filePath
            };

            IntPtr fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
            try
            {
                Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);
                var trustData = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                    dwUIChoice = 2, // WTD_UI_NONE
                    fdwRevocationChecks = 0,
                    dwUnionChoice = 1, // WTD_CHOICE_FILE
                    pFile = fileInfoPtr,
                    dwStateAction = 1 // WTD_STATEACTION_VERIFY
                };

                Guid action = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
                int status = WinVerifyTrust(IntPtr.Zero, ref action, ref trustData);

                trustData.dwStateAction = 2; // WTD_STATEACTION_CLOSE
                WinVerifyTrust(IntPtr.Zero, ref action, ref trustData);

                winTrustStatus = status;

                // Resolve the leaf Authenticode signer via CMSG_SIGNER_CERT_INFO_PARAM + CertFindCertificateInStore.
                if (CryptQueryObject(
                        1, // CERT_QUERY_OBJECT_FILE
                        filePath,
                        1 << 10, // CERT_QUERY_CONTENT_FLAG_PKCS7_SIGNED_EMBED
                        1 << 1, // CERT_QUERY_FORMAT_FLAG_BINARY
                        0,
                        out _, out _, out _, out IntPtr certStore, out IntPtr msg, out _))
                {
                    try
                    {
                        if (certStore != IntPtr.Zero && msg != IntPtr.Zero)
                        {
                            uint signerInfoSize = 0;
                            if (CryptMsgGetParam(msg, 7 /* CMSG_SIGNER_CERT_INFO_PARAM */, 0, IntPtr.Zero, ref signerInfoSize) && signerInfoSize > 0)
                            {
                                IntPtr signerInfoPtr = Marshal.AllocHGlobal((int)signerInfoSize);
                                try
                                {
                                    if (CryptMsgGetParam(msg, 7, 0, signerInfoPtr, ref signerInfoSize))
                                    {
                                        const uint X509_AND_PKCS7 = 0x00010001;
                                        const uint CERT_FIND_SUBJECT_CERT = 0x000B0000;
                                        IntPtr certCtx = CertFindCertificateInStore(certStore, X509_AND_PKCS7, 0, CERT_FIND_SUBJECT_CERT, signerInfoPtr, IntPtr.Zero);
                                        if (certCtx != IntPtr.Zero)
                                        {
                                            try
                                            {
                                                using var cert = new X509Certificate2(certCtx);
                                                signerSubject = cert.Subject;
                                                signerThumbprint = cert.Thumbprint;
                                            }
                                            finally
                                            {
                                                CertFreeCertificateContext(certCtx);
                                            }
                                        }
                                    }
                                }
                                finally
                                {
                                    Marshal.FreeHGlobal(signerInfoPtr);
                                }
                            }
                        }
                    }
                    finally
                    {
                        if (certStore != IntPtr.Zero) CertCloseStore(certStore, 0);
                        if (msg != IntPtr.Zero) CryptMsgClose(msg);
                    }
                }

                return status == 0;
            }
            finally
            {
                Marshal.FreeHGlobal(fileInfoPtr);
            }
        }
        catch
        {
            return false;
        }
    }

    public static string ResolvePublisherLabel(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return string.Empty;

        if (VerifyAuthenticodeSignature(exePath, out string? subject) && !string.IsNullOrWhiteSpace(subject))
        {
            string? o = null, cn = null;
            foreach (string part in subject.Split(", ", StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.StartsWith("O=", StringComparison.OrdinalIgnoreCase)) o = part[2..];
                else if (part.StartsWith("CN=", StringComparison.OrdinalIgnoreCase)) cn = part[3..];
            }
            return "[Verified] " + (o ?? cn ?? subject).Replace("\"", "");
        }

        try
        {
            var vi = FileVersionInfo.GetVersionInfo(exePath);
            return vi.CompanyName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    #region Native Interop

    [DllImport("dnsapi.dll")]
    private static extern int DnsFlushResolverCache();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeoutW(
        IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;
    private const uint MOVEFILE_DELAY_UNTIL_REBOOT = 0x00000004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCTW
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCTW FileOp);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileExW(string lpExistingFileName, string? lpNewFileName, uint dwFlags);

    private const uint BEGIN_SYSTEM_CHANGE = 100;
    private const uint MODIFY_SETTINGS = 12;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RESTOREPOINTINFOW
    {
        public uint dwEventType;
        public uint dwRestorePtType;
        public long llSequenceNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szDescription;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STATEMGRSTATUS
    {
        public int nStatus;
        public long llSequenceNumber;
    }

    [DllImport("srclient.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SRSetRestorePointW(ref RESTOREPOINTINFOW pRestorePtSpec, out STATEMGRSTATUS pSMgrStatus);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct PERFORMANCE_INFORMATION
    {
        public uint cb;
        public UIntPtr CommitTotal;
        public UIntPtr CommitLimit;
        public UIntPtr CommitPeak;
        public UIntPtr PhysicalTotal;
        public UIntPtr PhysicalAvailable;
        public UIntPtr SystemCache;
        public UIntPtr KernelTotal;
        public UIntPtr KernelPaged;
        public UIntPtr KernelNonpaged;
        public UIntPtr PageSize;
        public uint HandleCount;
        public uint ProcessCount;
        public uint ThreadCount;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPerformanceInfo(out PERFORMANCE_INFORMATION pPerformanceInformation, uint cb);

    [DllImport("psapi.dll")]
    private static extern int EmptyWorkingSet(IntPtr hwProc);

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID_AND_ATTRIBUTES { public LUID Luid; public uint Attributes; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
        public LUID_AND_ATTRIBUTES[] Privileges;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValueW(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, [MarshalAs(UnmanagedType.Bool)] bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int SystemInformationClass, IntPtr SystemInformation, int SystemInformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WINTRUST_DATA pWVTData);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptQueryObject(
        uint dwObjectType,
        [MarshalAs(UnmanagedType.LPWStr)] string pvObject,
        uint dwExpectedContentTypeFlags,
        uint dwExpectedFormatTypeFlags,
        uint dwFlags,
        out uint pdwMsgAndCertEncodingType,
        out uint pdwContentType,
        out uint pdwFormatType,
        out IntPtr phCertStore,
        out IntPtr phMsg,
        out IntPtr ppvContext);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptMsgGetParam(
        IntPtr hCryptMsg,
        uint dwParamType,
        uint dwIndex,
        IntPtr pvData,
        ref uint pcbData);

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern IntPtr CertFindCertificateInStore(
        IntPtr hCertStore,
        uint dwCertEncodingType,
        uint dwFindFlags,
        uint dwFindType,
        IntPtr pvFindPara,
        IntPtr pPrevCertContext);

    [DllImport("crypt32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CertFreeCertificateContext(IntPtr pCertContext);

    [DllImport("crypt32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CertCloseStore(IntPtr hCertStore, uint dwFlags);

    [DllImport("crypt32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptMsgClose(IntPtr hCryptMsg);

    #endregion
}
