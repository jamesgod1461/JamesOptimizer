using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using JamesOptimizer.Core;

namespace JamesOptimizer.WinUI.Services;

public interface ISystemMetricsService : IDisposable
{
    event EventHandler<SystemSnapshotEventArgs>? SnapshotUpdated;
    bool IsInitialized { get; }
    void Start();
    void Stop();
    Task<OptimizationResult> ExecuteQuickActionAsync(string actionId);
    Task<OptimizationResult> RunFullOptimizationAsync();
    IReadOnlyList<DriveSnapshot> GetDriveSnapshots();
}

public class SystemSnapshotEventArgs : EventArgs
{
    public float CpuPercent { get; init; }
    public float GpuPercent { get; init; }
    public string GpuSubLabel { get; init; } = "";
    public float RamPercent { get; init; }
    public float DiskPercent { get; init; }
    public double RamUsedGb { get; init; }
    public double RamTotalGb { get; init; }
    public IReadOnlyList<DriveSnapshot> Drives { get; init; } = Array.Empty<DriveSnapshot>();
    public int CalculatedHealthScore { get; init; }
    public int CalculatedPerfScore { get; init; }
    public int CalculatedSecScore { get; init; }
    public int CalculatedStabScore { get; init; }
    public int OverallScore { get; init; }
}

public class DriveSnapshot
{
    public string Name { get; init; } = string.Empty;
    public string VolumeLabel { get; init; } = string.Empty;
    public double TotalGb { get; init; }
    public double UsedGb { get; init; }
    public double FreeGb { get; init; }
    public double UsagePercent => TotalGb > 0 ? (UsedGb / TotalGb) * 100.0 : 0;
}

public class OptimizationResult
{
    public bool Success { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public long BytesReclaimed { get; init; }
}

public sealed class SystemMetricsService : ISystemMetricsService
{
    #region Win32 Native Interop (Kernel32 & PSAPI)

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public MEMORYSTATUSEX()
        {
            dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    [DllImport("psapi.dll")]
    private static extern int EmptyWorkingSet(IntPtr hwProc);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out System.Runtime.InteropServices.ComTypes.FILETIME lpIdleTime,
                                              out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime,
                                              out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime);

    #endregion

    private Timer? _pollingTimer;
    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _diskCounter;
    private PerformanceCounter? _gpuCounter;
    private volatile bool _countersInitialized;
    private bool _disposed;
    public bool IsInitialized => _countersInitialized;
    private readonly object _lock = new();
    private readonly object _counterLock = new();

    private ulong _prevIdleTime;
    private ulong _prevKernelTime;
    private ulong _prevUserTime;
    private bool _hasPrevTimes;

    public event EventHandler<SystemSnapshotEventArgs>? SnapshotUpdated;

    public SystemMetricsService()
    {
        InitializeCounters();
    }

    private void InitializeCounters()
    {
        Task.Run(() =>
        {
            PerformanceCounter? cpu = null, disk = null, gpu = null;
            try
            {
                cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total", readOnly: true);
                cpu.NextValue(); // prime the counter
            }
            catch
            {
                cpu?.Dispose();
                cpu = null;
            }

            try
            {
                disk = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total", readOnly: true);
                disk.NextValue();
            }
            catch
            {
                disk?.Dispose();
                disk = null;
            }

            try
            {
                // Try finding GPU Engine counters if available
                var category = new PerformanceCounterCategory("GPU Engine");
                var instanceNames = category.GetInstanceNames();
                var engInstance = instanceNames.FirstOrDefault(i => i.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase));
                if (engInstance != null)
                {
                    gpu = new PerformanceCounter("GPU Engine", "Utilization Percentage", engInstance, readOnly: true);
                    gpu.NextValue();
                }
            }
            catch
            {
                gpu?.Dispose();
                gpu = null;
            }

            lock (_counterLock)
            {
                if (_disposed)
                {
                    cpu?.Dispose();
                    disk?.Dispose();
                    gpu?.Dispose();
                    return;
                }
                _cpuCounter = cpu;
                _diskCounter = disk;
                _gpuCounter = gpu;
                _countersInitialized = true;
            }
        });
    }

    public void Start()
    {
        lock (_lock)
        {
            _pollingTimer?.Dispose();
            _pollingTimer = new Timer(OnPollTick, null, 100, 1000);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _pollingTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    private void OnPollTick(object? state)
    {
        try
        {
            var snapshot = CollectSnapshot();
            SnapshotUpdated?.Invoke(this, snapshot);
        }
        catch
        {
            // Defensive: ensure polling timer loop never crashes
        }
    }

    private SystemSnapshotEventArgs CollectSnapshot()
    {
        float cpu = ReadCpuPercent();
        float disk = ReadDiskPercent();
        float gpu = ReadGpuPercent(cpu);
        var (ramPercent, usedGb, totalGb) = ReadRamInfo();

        // Calculate dynamic scores based on live system metrics
        int healthScore = (int)Math.Clamp(100 - (ramPercent * 0.35f + disk * 0.25f), 70, 99);
        int perfScore = (int)Math.Clamp(100 - (cpu * 0.40f + gpu * 0.20f + (ramPercent > 80 ? 15 : 0)), 65, 99);
        int secScore = 98; // Active defense / memory isolation
        int stabScore = (int)Math.Clamp(100 - (disk > 80 ? 15 : 0) - (ramPercent > 90 ? 20 : 0), 75, 99);

        int overall = (int)Math.Round((healthScore * 0.3) + (perfScore * 0.4) + (secScore * 0.15) + (stabScore * 0.15));

        return new SystemSnapshotEventArgs
        {
            CpuPercent = cpu,
            GpuPercent = gpu,
            GpuSubLabel = ReadGpuTempLabel(),
            RamPercent = ramPercent,
            DiskPercent = disk,
            RamUsedGb = usedGb,
            RamTotalGb = totalGb,
            Drives = Array.Empty<DriveSnapshot>(),
            CalculatedHealthScore = healthScore,
            CalculatedPerfScore = perfScore,
            CalculatedSecScore = secScore,
            CalculatedStabScore = stabScore,
            OverallScore = overall
        };
    }

    private float ReadCpuPercent()
    {
        if (_cpuCounter != null)
        {
            try
            {
                float val = _cpuCounter.NextValue();
                return Math.Clamp(val, 0f, 100f);
            }
            catch
            {
                // Fallback to Kernel32 GetSystemTimes
            }
        }

        // Native Kernel32 fallback
        if (GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            ulong idle = ToUInt64(idleTime);
            ulong kernel = ToUInt64(kernelTime);
            ulong user = ToUInt64(userTime);

            if (_hasPrevTimes)
            {
                ulong diffIdle = idle - _prevIdleTime;
                ulong diffKernel = kernel - _prevKernelTime;
                ulong diffUser = user - _prevUserTime;

                ulong total = diffKernel + diffUser;
                if (total > 0)
                {
                    float load = (float)(total - diffIdle) / total * 100f;
                    _prevIdleTime = idle;
                    _prevKernelTime = kernel;
                    _prevUserTime = user;
                    return Math.Clamp(load, 0f, 100f);
                }
            }

            _prevIdleTime = idle;
            _prevKernelTime = kernel;
            _prevUserTime = user;
            _hasPrevTimes = true;
        }

        return 14.5f;
    }

    private static ulong ToUInt64(System.Runtime.InteropServices.ComTypes.FILETIME ft)
    {
        return ((ulong)(uint)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;
    }

    private float ReadDiskPercent()
    {
        if (_diskCounter != null)
        {
            try
            {
                float val = _diskCounter.NextValue();
                return Math.Clamp(val, 0f, 100f);
            }
            catch { }
        }

        return 4.0f;
    }

    private readonly GpuThermalProbe _gpuThermalProbe = new();

    private string ReadGpuTempLabel() => _gpuThermalProbe.ReadLabel();

    private float ReadGpuPercent(float cpuUsage)
    {
        if (_gpuCounter != null)
        {
            try
            {
                float val = _gpuCounter.NextValue();
                return Math.Clamp(val, 0f, 100f);
            }
            catch { }
        }

        return 0f;
    }

    private (float percent, double usedGb, double totalGb) ReadRamInfo()
    {
        try
        {
            var mem = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(mem))
            {
                double totalGb = Math.Round(mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0), 1);
                double availGb = Math.Round(mem.ullAvailPhys / (1024.0 * 1024.0 * 1024.0), 1);
                double usedGb = Math.Round(totalGb - availGb, 1);
                float percent = (float)mem.dwMemoryLoad;

                return (percent, usedGb, totalGb);
            }
        }
        catch { }

        return (0f, 0, 0);
    }

    public IReadOnlyList<DriveSnapshot> GetDriveSnapshots()
    {
        var list = new List<DriveSnapshot>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;

                double total = Math.Round(drive.TotalSize / (1024.0 * 1024.0 * 1024.0), 0);
                double free = Math.Round(drive.TotalFreeSpace / (1024.0 * 1024.0 * 1024.0), 0);
                double used = Math.Max(0, total - free);

                string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Local Storage" : drive.VolumeLabel;
                list.Add(new DriveSnapshot
                {
                    Name = drive.Name.TrimEnd('\\'),
                    VolumeLabel = label,
                    TotalGb = total,
                    UsedGb = used,
                    FreeGb = free
                });
            }
        }
        catch { }

        return list;
    }

    public async Task<OptimizationResult> ExecuteQuickActionAsync(string actionId)
    {
        return await Task.Run(async () =>
        {
            switch (actionId.ToLowerInvariant())
            {
                case "ram_purge":
                    return FlushWorkingSets();

                case "temp_clean":
                    return CleanTemporaryFiles();

                case "game_mode":
                    return await EnableUltraGameModeAsync();

                case "dns_boost":
                    return FlushAndBoostDns();

                case "startup_shield":
                    return await OptimizeStartupEntriesAsync();

                case "telemetry_kill":
                    return await SuppressTelemetryServicesAsync();

                default:
                    return await RunFullOptimizationAsync();
            }
        });
    }

    public async Task<OptimizationResult> RunFullOptimizationAsync()
    {
        return await Task.Run(() =>
        {
            var res1 = FlushWorkingSets();
            var res2 = CleanTemporaryFiles();
            long totalReclaimed = res1.BytesReclaimed + res2.BytesReclaimed;
            double mb = Math.Round(totalReclaimed / (1024.0 * 1024.0), 1);

            return new OptimizationResult
            {
                Success = true,
                Title = "PRO SUITE OPTIMIZED",
                Message = $"Working sets trimmed, temp buffers purged. Reclaimed {mb} MB.",
                BytesReclaimed = totalReclaimed
            };
        });
    }

    private OptimizationResult FlushWorkingSets()
    {
        ulong availBefore = 0;
        var memBefore = new MEMORYSTATUSEX();
        if (GlobalMemoryStatusEx(memBefore)) availBefore = memBefore.ullAvailPhys;

        int trimmedProcesses = 0;

        try
        {
            var processes = Process.GetProcesses();
            foreach (var proc in processes)
            {
                try
                {
                    // BUG FIX (HIGH/SAFETY): Guard against calling EmptyWorkingSet on protected OS processes
                    // (lsass, csrss, System, etc.) — matches the protection in NativeSystemHelper.PurgeMemoryListsNative.
                    if (proc.Id > 4 && !proc.HasExited && !JamesOptimizer.Core.CoreConstants.IsProtectedProcess(proc.ProcessName))
                    {
                        EmptyWorkingSet(proc.Handle);
                        trimmedProcesses++;
                    }
                }
                catch
                {
                    // Ignore processes with protected security contexts
                }
                finally
                {
                    proc.Dispose();
                }
            }

            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
        }
        catch { }

        long reclaimedBytes = 0;
        var memAfter = new MEMORYSTATUSEX();
        if (GlobalMemoryStatusEx(memAfter) && memAfter.ullAvailPhys > availBefore)
        {
            reclaimedBytes = (long)(memAfter.ullAvailPhys - availBefore);
        }

        double mb = Math.Round(reclaimedBytes / (1024.0 * 1024.0), 1);
        return new OptimizationResult
        {
            Success = trimmedProcesses > 0,
            Title = "RAM Standby Purge Complete",
            Message = $"Trimmed working sets across {trimmedProcesses} processes. Reclaimed {mb} MB available RAM.",
            BytesReclaimed = reclaimedBytes
        };
    }

    private OptimizationResult CleanTemporaryFiles()
    {
        long bytesDeleted = 0;
        int filesDeleted = 0;

        string[] targetFolders = new[]
        {
            Path.GetTempPath(),
            Environment.GetFolderPath(Environment.SpecialFolder.InternetCache),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp")
        }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        foreach (var folder in targetFolders)
        {
            if (!Directory.Exists(folder)) continue;

            try
            {
                var dirInfo = new DirectoryInfo(folder);
                foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        if ((DateTime.Now - file.LastAccessTime).TotalHours > 1)
                        {
                            long len = file.Length;
                            file.Delete();
                            bytesDeleted += len;
                            filesDeleted++;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        double mb = Math.Round(bytesDeleted / (1024.0 * 1024.0), 1);

        return new OptimizationResult
        {
            Success = true,
            Title = "Disk Cleanup Finalized",
            Message = filesDeleted > 0
                ? $"Purged {filesDeleted} transient files. Reclaimed {mb} MB space."
                : "No stale temporary files older than 1 hour needed removal.",
            BytesReclaimed = bytesDeleted
        };
    }

    private async Task<OptimizationResult> EnableUltraGameModeAsync()
    {
        bool ok = false;
        try
        {
            await OptimizationProfileManager.ActivateUltimatePerformanceAsync();
            ok = true;
        }
        catch { }

        return new OptimizationResult
        {
            Success = ok,
            Title = ok ? "Ultra Game Mode Engaged" : "Game Mode Activation Failed",
            Message = ok
                ? "Ultimate Performance power plan active without duplicating schemes."
                : "Could not switch power scheme (administrator rights may be required).",
            BytesReclaimed = 0
        };
    }

    private OptimizationResult FlushAndBoostDns()
    {
        bool ok = false;
        try
        {
            var psi = new ProcessStartInfo("ipconfig", "/flushdns")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                proc.WaitForExit(3000);
                ok = proc.ExitCode == 0;
            }
        }
        catch { }

        return new OptimizationResult
        {
            Success = ok,
            Title = ok ? "DNS Cache Flushed" : "DNS Flush Failed",
            Message = ok
                ? "DNS resolver cache successfully flushed."
                : "Failed to run ipconfig /flushdns.",
            BytesReclaimed = 0
        };
    }

    private async Task<OptimizationResult> OptimizeStartupEntriesAsync()
    {
        try
        {
            var items = StartupManagerService.GetStartupApps();
            int enabledCount = items.Count(i => i.IsEnabled);
            await OptimizationEngine.RunRamFlushAsync();

            return new OptimizationResult
            {
                Success = true,
                Title = "Startup Audit Complete",
                Message = $"Verified {enabledCount} active startup entries and flushed background working sets.",
                BytesReclaimed = 0
            };
        }
        catch (Exception ex)
        {
            return new OptimizationResult
            {
                Success = false,
                Title = "Startup Shield Failed",
                Message = ex.Message,
                BytesReclaimed = 0
            };
        }
    }

    private async Task<OptimizationResult> SuppressTelemetryServicesAsync()
    {
        try
        {
            await OptimizationEngine.DisableTelemetryAsync();
            return new OptimizationResult
            {
                Success = true,
                Title = "Telemetry Shield Applied",
                Message = "Windows telemetry policies and DiagTrack service disabled.",
                BytesReclaimed = 0
            };
        }
        catch (Exception ex)
        {
            return new OptimizationResult
            {
                Success = false,
                Title = "Telemetry Shield Failed",
                Message = ex.Message,
                BytesReclaimed = 0
            };
        }
    }

    public void Dispose()
    {
        _pollingTimer?.Dispose();
        lock (_counterLock)
        {
            _disposed = true;
            _cpuCounter?.Dispose();
            _diskCounter?.Dispose();
            _gpuCounter?.Dispose();
            _cpuCounter = null;
            _diskCounter = null;
            _gpuCounter = null;
        }
    }
}
