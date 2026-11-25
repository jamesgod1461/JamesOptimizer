using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JamesOptimizer.Core;
using IOEnumerationOptions = System.IO.EnumerationOptions;

namespace JamesOptimizer
{
    public sealed class BenchmarkHistoryRecord
    {
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
        public int CpuScore { get; set; }
        public int RamScore { get; set; }
        public int DiskScore { get; set; }
        public int TotalScore { get; set; }
        public string CpuDetail { get; set; } = "";
        public string RamDetail { get; set; } = "";
        public string DiskDetail { get; set; } = "";
        public string PowerPlanGuid { get; set; } = "";
        public string PowerPlanName { get; set; } = "";
        public int OsBuild { get; set; }
        public double CpuBackgroundPercent { get; set; }
    }

    [SupportedOSPlatform("windows")]
    public static class OptimizationEngine
    {
        public static Action<int, string>? OnProgress;

        internal static void ReportProgress(int percent, string message) => OnProgress?.Invoke(percent, message);

        private static string BenchmarksFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            CoreConstants.ProductName, "benchmarks.json");

        public static async Task RunGamingBoostAsync(CancellationToken cancellationToken = default)
        {
            ReportProgress(10, "Activating Ultimate Performance Power Plan...");
            await OptimizationProfileManager.ActivateUltimatePerformanceAsync(cancellationToken);

            ReportProgress(55, "Applying Multimedia QoS & Network Latency Tweaks...");
            await Task.Run(() =>
            {
                TweakCatalog.ApplyTweak("system_responsiveness");
                TweakCatalog.ApplyTweak("network_throttling");
                TweakCatalog.ApplyTweak("game_mode");
            }, cancellationToken);

            ReportProgress(100, "GAMING MODE ENABLED");
        }

        public static async Task RunEliteTweaksAsync(CancellationToken cancellationToken = default)
        {
            ReportProgress(0, "Applying BCD & NTFS Tweaks...");
            await Task.Run(() =>
            {
                TweakCatalog.ApplyTweak("elite_bcd_timers");
                ReportProgress(45, "Optimizing NTFS Metadata...");
                TweakCatalog.ApplyTweak("elite_fsutil");
                ReportProgress(80, "Tuning Memory Compression...");
                TweakCatalog.ApplyTweak("elite_mem_compression");
            }, cancellationToken);
            ReportProgress(100, "Elite System Optimization Applied!");
        }

        public static Task RunRegTweaksAsync(CancellationToken cancellationToken = default) =>
            Task.Run(() =>
            {
                TweakCatalog.ApplyTweak("system_responsiveness");
                TweakCatalog.ApplyTweak("network_throttling");
            }, cancellationToken);

        public static Task RunVisualOptimizeAsync(CancellationToken cancellationToken = default) =>
            Task.Run(() =>
            {
                ReportProgress(20, "Applying Visual Performance Tweaks...");
                TweakCatalog.ApplyTweak("visuals");
                ReportProgress(100, "Visual Effects Optimized!");
            }, cancellationToken);

        public static Task RunMouseFixAsync(CancellationToken cancellationToken = default) =>
            Task.Run(() =>
            {
                TweakCatalog.ApplyTweak("mouse_fix");
                ReportProgress(100, "Mouse Acceleration Removed.");
            }, cancellationToken);

        public static Task RunKeyboardFixAsync(CancellationToken cancellationToken = default) =>
            Task.Run(() =>
            {
                TweakCatalog.ApplyTweak("keyboard_fix");
                ReportProgress(100, "Keyboard Response Optimized.");
            }, cancellationToken);

        public static Task RunGpuTweaksAsync(CancellationToken cancellationToken = default) =>
            Task.Run(() =>
            {
                ReportProgress(20, "Inspecting GPU Vendor & Scheduling...");
                TweakCatalog.ApplyTweak("gpu_pipeline");
                TweakCatalog.ApplyTweak("gpu_amd_ulps");
                TweakCatalog.ApplyTweak("gpu_nvidia_power");
                ReportProgress(100, "GPU Pipeline Optimization Applied.");
            }, cancellationToken);

        public static async Task RunFastCleanAsync(CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                ReportProgress(0, "Scanning Temp Folders...");
                var total = DeepCleanupService.CleanPathDetailed(Path.GetTempPath(), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                ReportProgress(40, "Cleaning Windows Temp...");
                total += DeepCleanupService.CleanPathDetailed(@"C:\Windows\Temp", cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                ReportProgress(75, "Cleaning Browser Caches...");
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(localAppData))
                {
                    total += DeepCleanupService.CleanPathDetailed(Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Cache"), cancellationToken);
                    total += DeepCleanupService.CleanPathDetailed(Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Cache"), cancellationToken);
                    total += DeepCleanupService.CleanPathDetailed(Path.Combine(localAppData, @"BraveSoftware\Brave-Browser\User Data\Default\Cache"), cancellationToken);
                }
                int locked = total.FilesFailed + total.DirsFailed;
                if (locked > 0)
                    SharedLogger.Debug("CLEANUP", $"FastClean removed {total.FilesDeleted} file(s); skipped {locked} locked item(s).");
                ReportProgress(100, locked > 0
                    ? $"Junk Files Removed ({locked} locked skipped)."
                    : "Junk Files Removed!");
            }, cancellationToken);
        }

        public static async Task<(long BytesFreed, string Message)> RunShaderClearAsync(CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                ReportProgress(0, "Clearing DirectX & GPU Shader Caches...");
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string[] paths =
                {
                    Path.Combine(localAppData, "D3DSCache"),
                    Path.Combine(localAppData, @"NVIDIA\GLCache"),
                    Path.Combine(localAppData, @"NVIDIA\DXCache"),
                    Path.Combine(localAppData, @"AMD\DxCache")
                };

                var total = new DeepCleanupService.CleanResult(0, 0, 0, 0);
                foreach (string p in paths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    total += DeepCleanupService.CleanPathDetailed(p, cancellationToken);
                }
                int locked = total.FilesFailed + total.DirsFailed;
                if (locked > 0)
                    SharedLogger.Debug("CLEANUP", $"ShaderClear removed {total.FilesDeleted} file(s); skipped {locked} locked item(s).");
                ReportProgress(100, "Shader Cache Cleared!");
                string lockedSuffix = locked > 0 ? $" ({locked} locked item(s) skipped)" : "";
                return (total.BytesFreed, $"Cleared {FormatBytes(total.BytesFreed)} of shader caches{lockedSuffix}. First game launch may briefly stutter while shaders rebuild.");
            }, cancellationToken);
        }

        public static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1 && counter < suffixes.Length - 1)
            {
                number /= 1024;
                counter++;
            }
            return $"{number:n1} {suffixes[counter]}";
        }

        public static async Task RunRamFlushAsync(CancellationToken cancellationToken = default)
        {
            ReportProgress(0, "Flushing System RAM...");
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReportProgress(35, "Trimming non-critical working sets & standby lists...");
                NativeSystemHelper.PurgeMemoryListsNative(emptyWorkingSets: true, flushModified: true, purgeStandby: true, ct: cancellationToken);
                ReportProgress(85, "Running GC...");
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }, cancellationToken);
            ReportProgress(100, "RAM Purged Successfully!");
        }

        public static async Task RunNetworkFlushAsync(CancellationToken cancellationToken = default)
        {
            ReportProgress(10, "Applying Low-Latency TCP & Flushing DNS...");
            await Task.Run(() =>
            {
                TweakCatalog.ApplyTweak("network_throttling");
                TweakCatalog.ApplyTweak("net_tcp_low_latency");
                NativeSystemHelper.FlushDnsNative();
            }, cancellationToken);
            ReportProgress(100, "Network Optimized");
        }

        public static async Task<(int cpu, int ram, int disk, int total, string cpuDetail, string ramDetail, string diskDetail)> BenchmarkSystemAsync(CancellationToken cancellationToken = default)
        {
            ReportProgress(5, "Warming up CPU & checking system context...");

            int cpuScore = 0;
            string cpuDetail = "";
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                int cores = Math.Max(1, Environment.ProcessorCount);
                long opsPerCore = 15_000_000;
                long totalOps = opsPerCore * cores;

                // Discard warm-up run
                double warmup = 0;
                for (long i = 0; i < 2_000_000; i++) warmup += Math.Sqrt(i);
                GC.KeepAlive(warmup);

                var runTimesMs = new List<double>();
                for (int iter = 0; iter < 3; iter++)
                {
                    var sw = Stopwatch.StartNew();
                    Parallel.For(0, cores, new ParallelOptions { CancellationToken = cancellationToken }, _ =>
                    {
                        double result = 0;
                        for (long i = 0; i < opsPerCore; i++)
                            result += Math.Sqrt(i) * Math.Log(i + 1.0);
                        GC.KeepAlive(result);
                    });
                    sw.Stop();
                    runTimesMs.Add(Math.Max(sw.Elapsed.TotalMilliseconds, 1.0));
                }

                runTimesMs.Sort();
                double medianMs = runTimesMs[1];
                double mops = (totalOps / (medianMs / 1000.0)) / 1_000_000.0;
                double normalized = 35.0 + (65.0 * (mops / (mops + 700.0)));
                cpuScore = Math.Clamp((int)Math.Round(normalized), 20, 100);
                cpuDetail = $"{mops:F1} MOps/s (median {medianMs:F0}ms · {cores}T)";
            }, cancellationToken);

            ReportProgress(45, "Testing RAM Throughput...");
            int ramScore = 0;
            string ramDetail = "";
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                int bufferMb = 128;
                int bufferSize = bufferMb * 1024 * 1024;
                byte[] src = new byte[bufferSize];
                byte[] dst = new byte[bufferSize];
                new Random(42).NextBytes(src);

                Buffer.BlockCopy(src, 0, dst, 0, bufferSize); // warm-up

                var sw = Stopwatch.StartNew();
                for (int round = 0; round < 4; round++)
                    Buffer.BlockCopy(src, 0, dst, 0, bufferSize);
                sw.Stop();

                long ms = Math.Max(sw.ElapsedMilliseconds, 1);
                double gbps = ((bufferMb * 4.0) / 1024.0) / (ms / 1000.0);
                double normalized = 40.0 + (60.0 * (gbps / (gbps + 15.0)));
                ramScore = Math.Clamp((int)Math.Round(normalized), 20, 100);
                ramDetail = $"{gbps:F1} GB/s ({ms}ms)";
            }, cancellationToken);

            ReportProgress(75, "Testing Physical Storage (Direct I/O)...");
            int diskScore = 0;
            string diskDetail = "";
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                string path = Path.Combine(Path.GetTempPath(), $"james_bench_{Guid.NewGuid():N}.tmp");
                try
                {
                    int sizeMb = 64;
                    byte[] data = new byte[sizeMb * 1024 * 1024];
                    new Random(42).NextBytes(data);

                    var sw = Stopwatch.StartNew();
                    using (var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.WriteThrough))
                    {
                        fs.Write(data, 0, data.Length);
                        fs.Position = 0;
                        fs.ReadExactly(data, 0, data.Length);
                    }
                    sw.Stop();

                    long ms = Math.Max(sw.ElapsedMilliseconds, 1);
                    double diskMbps = (sizeMb * 2.0) / (ms / 1000.0);
                    double normalized = 35.0 + (65.0 * (diskMbps / (diskMbps + 900.0)));
                    diskScore = Math.Clamp((int)Math.Round(normalized), 20, 100);
                    diskDetail = $"{diskMbps:N0} MB/s ({ms}ms)";
                }
                finally
                {
                    try { if (File.Exists(path)) File.Delete(path); } catch { }
                }
            }, cancellationToken);

            int total = (int)Math.Clamp(Math.Round(cpuScore * 0.45 + ramScore * 0.35 + diskScore * 0.20), 1, 100);
            SaveBenchmarkHistory(cpuScore, ramScore, diskScore, total, cpuDetail, ramDetail, diskDetail);
            ReportProgress(100, "Benchmark Complete!");
            return (cpuScore, ramScore, diskScore, total, cpuDetail, ramDetail, diskDetail);
        }

        public static List<BenchmarkHistoryRecord> GetBenchmarkHistory()
        {
            try
            {
                if (!File.Exists(BenchmarksFile)) return new();
                return JsonSerializer.Deserialize<List<BenchmarkHistoryRecord>>(File.ReadAllText(BenchmarksFile)) ?? new();
            }
            catch { return new(); }
        }

        private static void SaveBenchmarkHistory(int cpu, int ram, int disk, int total, string cpuDetail, string ramDetail, string diskDetail)
        {
            try
            {
                var schemeOut = NativeSystemHelper.RunSystem32ToolSync("powercfg.exe", new[] { "/getactivescheme" }).StdOut;
                var guidMatch = System.Text.RegularExpressions.Regex.Match(schemeOut, @"[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}");
                string guid = guidMatch.Success ? guidMatch.Value : "";
                string planName = schemeOut.Contains("Ultimate", StringComparison.OrdinalIgnoreCase) ? "Ultimate Performance"
                    : schemeOut.Contains("High performance", StringComparison.OrdinalIgnoreCase) ? "High Performance"
                    : schemeOut.Contains("Power saver", StringComparison.OrdinalIgnoreCase) ? "Power Saver"
                    : "Balanced";

                var list = GetBenchmarkHistory();
                list.Add(new BenchmarkHistoryRecord
                {
                    Timestamp = DateTimeOffset.Now,
                    CpuScore = cpu,
                    RamScore = ram,
                    DiskScore = disk,
                    TotalScore = total,
                    CpuDetail = cpuDetail,
                    RamDetail = ramDetail,
                    DiskDetail = diskDetail,
                    PowerPlanGuid = guid,
                    PowerPlanName = planName,
                    OsBuild = OsEnvironment.Detect().BuildNumber
                });
                if (list.Count > 50) list.RemoveRange(0, list.Count - 50);

                Directory.CreateDirectory(Path.GetDirectoryName(BenchmarksFile)!);
                File.WriteAllText(BenchmarksFile, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        public static async Task<string> CreateRestorePointAsync(CancellationToken cancellationToken = default)
        {
            ReportProgress(10, "Creating System Restore Point...");
            var res = await Task.Run(() => NativeSystemHelper.CreateRestorePointNative("JamesOptimizer Pre-Tweak Snapshot"), cancellationToken);
            ReportProgress(100, "Done.");
            return res.Success
                ? $"✅ {res.Message}"
                : $"⚠️ {res.Message}";
        }

        public static async Task RunServiceDebloaterAsync(CancellationToken cancellationToken = default)
        {
            ReportProgress(0, "Disabling non-essential telemetry services...");
            string[] safeToDisable =
            {
                "SysMain", "DiagTrack", "dmwappushservice", "MapsBroker",
                "RetailDemo", "RemoteRegistry", "wisvc", "Fax"
            };

            await Task.Run(() =>
            {
                for (int i = 0; i < safeToDisable.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string svc = safeToDisable[i];
                    if (CoreConstants.IsProtectedService(svc)) continue;

                    ReportProgress((int)(((float)(i + 1) / safeToDisable.Length) * 100), $"Disabling {svc}...");
                    try
                    {
                        using var sc = new ServiceController(svc);
                        if (sc.CanStop && sc.Status == ServiceControllerStatus.Running)
                        {
                            sc.Stop();
                            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(4));
                        }
                        TweakBackupService.SetServiceStartMode(svc, "Disabled");
                    }
                    catch { }
                }
            }, cancellationToken);

            ReportProgress(100, "Non-essential Services Disabled!");
        }

        // --- STORAGE SCANNERS (with Two-Stage Partial->Full Hashing & Cloud Placeholder Safety) ---

        public class ScannedFile
        {
            public string FilePath { get; set; } = "";
            public string FileName { get; set; } = "";
            public long SizeBytes { get; set; }
            public string DisplaySize => FormatBytes(SizeBytes);
            public DateTime LastModified { get; set; }
            public string Hash { get; set; } = "";
            public bool IsSelected { get; set; }
        }

        private static bool IsSafeUserFile(FileInfo fi)
        {
            // Skip reparse points (junctions/symlinks) and OneDrive cloud-only placeholders (Offline / RecallOnDataAccess)
            var attrs = fi.Attributes;
            if ((attrs & FileAttributes.ReparsePoint) != 0) return false;
            if ((attrs & FileAttributes.Offline) != 0) return false;
            if (((int)attrs & 0x00400000) != 0) return false; // FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS
            if (((int)attrs & 0x00080000) != 0) return false; // FILE_ATTRIBUTE_RECALL_ON_OPEN
            if ((attrs & FileAttributes.System) != 0) return false;
            return true;
        }

        public static async Task<List<ScannedFile>> ScanDownloadsAsync(int minAgeDays = 0, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                var result = new List<ScannedFile>();
                string downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (!Directory.Exists(downloadsPath)) return result;

                var cutoff = minAgeDays > 0 ? DateTime.Now.AddDays(-minAgeDays) : DateTime.MaxValue;
                var options = new IOEnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System };

                foreach (string file in Directory.EnumerateFiles(downloadsPath, "*", options))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var fi = new FileInfo(file);
                        if (!IsSafeUserFile(fi)) continue;
                        if (minAgeDays > 0 && fi.LastWriteTime > cutoff) continue;

                        result.Add(new ScannedFile
                        {
                            FilePath = file,
                            FileName = fi.Name,
                            SizeBytes = fi.Length,
                            LastModified = fi.LastWriteTime
                        });
                    }
                    catch { }
                }

                return result.OrderByDescending(f => f.SizeBytes).ToList();
            }, cancellationToken);
        }

        public static async Task<List<List<ScannedFile>>> ScanDuplicatesAsync(
            IReadOnlyCollection<string>? excludedFolders = null,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                var duplicateGroups = new List<List<ScannedFile>>();
                string profilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string[] targetFolders = { "Documents", "Downloads", "Desktop", "Pictures", "Videos" };
                var excluded = new HashSet<string>(excludedFolders ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

                var allFiles = new List<FileInfo>();
                var options = new IOEnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
                };

                foreach (string folder in targetFolders)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string fullPath = Path.Combine(profilePath, folder);
                    if (!Directory.Exists(fullPath) || excluded.Contains(fullPath)) continue;

                    try
                    {
                        foreach (string f in Directory.EnumerateFiles(fullPath, "*", options))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (excluded.Any(ex => f.StartsWith(ex, StringComparison.OrdinalIgnoreCase))) continue;
                            try
                            {
                                var fi = new FileInfo(f);
                                if (fi.Length > 0 && IsSafeUserFile(fi))
                                    allFiles.Add(fi);
                            }
                            catch { }
                        }
                    }
                    catch { }
                }

                // Stage 1: Exact file size grouping
                var sizeGroups = allFiles.GroupBy(f => f.Length).Where(g => g.Count() > 1).ToList();
                int totalCandidates = sizeGroups.Sum(g => g.Count());
                int processed = 0;

                foreach (var sizeGroup in sizeGroups)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Stage 2: Fast partial hash (first 64KB + last 64KB)
                    var partialGroups = new Dictionary<string, List<FileInfo>>();
                    foreach (var fi in sizeGroup)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            string ph = ComputePartialHash(fi);
                            if (!partialGroups.TryGetValue(ph, out var bucket))
                            {
                                bucket = new List<FileInfo>();
                                partialGroups[ph] = bucket;
                            }
                            bucket.Add(fi);
                        }
                        catch { }
                    }

                    // Stage 3: Full SHA-256 only for partial-hash collisions
                    foreach (var bucket in partialGroups.Values)
                    {
                        if (bucket.Count < 2)
                        {
                            processed += bucket.Count;
                            progress?.Report(totalCandidates > 0 ? (int)((processed / (double)totalCandidates) * 100) : 100);
                            continue;
                        }

                        var fullHashGroups = new Dictionary<string, List<ScannedFile>>();
                        foreach (var file in bucket)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            try
                            {
                                using var stream = File.OpenRead(file.FullName);
                                string hashStr = Convert.ToHexString(SHA256.HashData(stream));
                                if (!fullHashGroups.TryGetValue(hashStr, out var list))
                                {
                                    list = new List<ScannedFile>();
                                    fullHashGroups[hashStr] = list;
                                }
                                list.Add(new ScannedFile
                                {
                                    FilePath = file.FullName,
                                    FileName = file.Name,
                                    SizeBytes = file.Length,
                                    LastModified = file.LastWriteTime,
                                    Hash = hashStr
                                });
                            }
                            catch { }
                            processed++;
                            progress?.Report(totalCandidates > 0 ? (int)((processed / (double)totalCandidates) * 100) : 100);
                        }

                        foreach (var grp in fullHashGroups.Values.Where(v => v.Count > 1))
                        {
                            duplicateGroups.Add(grp.OrderByDescending(f => f.LastModified).ToList());
                        }
                    }
                }

                return duplicateGroups.OrderByDescending(g => g.First().SizeBytes * (g.Count - 1)).ToList();
            }, cancellationToken);
        }

        private static string ComputePartialHash(FileInfo fi)
        {
            const int chunk = 65536;
            using var fs = File.OpenRead(fi.FullName);
            if (fi.Length <= chunk * 2)
                return Convert.ToHexString(SHA256.HashData(fs));

            byte[] buf = new byte[chunk * 2];
            fs.ReadExactly(buf, 0, chunk);
            fs.Seek(-chunk, SeekOrigin.End);
            fs.ReadExactly(buf, chunk, chunk);
            return Convert.ToHexString(SHA256.HashData(buf));
        }

        public static Task<int> DeleteFilesSafeAsync(IEnumerable<string> filePaths, bool permanentDelete = false, CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                int deleted = 0;
                var list = filePaths.ToList();
                for (int i = 0; i < list.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string p = list[i];
                    if (!File.Exists(p)) continue;
                    try
                    {
                        if (permanentDelete)
                        {
                            File.Delete(p);
                            deleted++;
                        }
                        else if (NativeSystemHelper.MoveToRecycleBin(p))
                        {
                            deleted++;
                        }
                    }
                    catch { }
                }
                return deleted;
            }, cancellationToken);
        }

        public static async Task<long> TestPingAsync(string host)
        {
            try
            {
                using var pinger = new Ping();
                var reply = await pinger.SendPingAsync(host, 1500);
                return reply.Status == IPStatus.Success ? reply.RoundtripTime : -1;
            }
            catch { return -1; }
        }

        public static async Task ApplyDnsAsync(string primaryDns, string secondaryDns, string? adapterNameOverride = null, CancellationToken cancellationToken = default)
        {
            ReportProgress(20, "Applying DNS Server Configuration...");
            string adapterName = adapterNameOverride ?? "";

            if (string.IsNullOrWhiteSpace(adapterName))
            {
                try
                {
                    var nic = NetworkInterface.GetAllNetworkInterfaces()
                        .FirstOrDefault(i => i.OperationalStatus == OperationalStatus.Up
                                             && i.NetworkInterfaceType != NetworkInterfaceType.Loopback
                                             && i.GetIPProperties().GatewayAddresses.Count > 0);
                    adapterName = nic?.Name ?? "Ethernet";
                }
                catch { adapterName = "Ethernet"; }
            }

            if (string.IsNullOrWhiteSpace(primaryDns))
            {
                await NativeSystemHelper.RunSystem32ToolAsync("netsh.exe", new[] { "interface", "ip", "set", "dns", $"name={adapterName}", "dhcp" }, cancellationToken);
            }
            else
            {
                await NativeSystemHelper.RunSystem32ToolAsync("netsh.exe", new[] { "interface", "ip", "set", "dns", $"name={adapterName}", "static", primaryDns }, cancellationToken);
                if (!string.IsNullOrEmpty(secondaryDns))
                {
                    await NativeSystemHelper.RunSystem32ToolAsync("netsh.exe", new[] { "interface", "ip", "add", "dns", $"name={adapterName}", secondaryDns, "index=2" }, cancellationToken);
                }
            }

            NativeSystemHelper.FlushDnsNative();
            ReportProgress(100, $"DNS Updated ({(string.IsNullOrEmpty(primaryDns) ? "DHCP" : primaryDns)}) on {adapterName}");
        }

        public static Task EnableUltimatePowerPlanAsync(CancellationToken cancellationToken = default) =>
            OptimizationProfileManager.ActivateUltimatePerformanceAsync(cancellationToken);

        public static Task DisableTelemetryAsync(CancellationToken cancellationToken = default) =>
            Task.Run(() => TweakCatalog.ApplyTweak("telemetry"), cancellationToken);

        public static Task OptimizeVisualEffectsAsync(CancellationToken cancellationToken = default) =>
            RunVisualOptimizeAsync(cancellationToken);

        public static Task DisableNetworkThrottlingAsync(CancellationToken cancellationToken = default) =>
            Task.Run(() =>
            {
                TweakCatalog.ApplyTweak("network_throttling");
                TweakCatalog.ApplyTweak("net_tcp_low_latency");
            }, cancellationToken);

        public static Task DisableCoreParkingAsync(CancellationToken cancellationToken = default) =>
            Task.Run(() => TweakCatalog.ApplyTweak("core_parking"), cancellationToken);

        public static Task OptimizeSystemResponsivenessAsync(CancellationToken cancellationToken = default) =>
            Task.Run(() => TweakCatalog.ApplyTweak("system_responsiveness"), cancellationToken);

        public static Task DisableSearchIndexingAsync(CancellationToken cancellationToken = default) =>
            Task.Run(() => TweakCatalog.ApplyTweak("indexing"), cancellationToken);

        public static Task DisableCortanaAndBingAsync(CancellationToken cancellationToken = default) =>
            Task.Run(() => TweakCatalog.ApplyTweak("cortana"), cancellationToken);

        public static Task FlushDnsAsync(CancellationToken cancellationToken = default) =>
            Task.Run(() =>
            {
                NativeSystemHelper.FlushDnsNative();
                ReportProgress(100, "DNS Resolver Cache Flushed");
            }, cancellationToken);

        public static async Task ResetTcpIpStackAsync(CancellationToken cancellationToken = default)
        {
            await NativeSystemHelper.RunSystem32ToolAsync("netsh.exe", new[] { "int", "ip", "reset" }, cancellationToken);
            await NativeSystemHelper.RunSystem32ToolAsync("netsh.exe", new[] { "winsock", "reset" }, cancellationToken);
            ReportProgress(100, "TCP/IP Stack and Winsock Reset");
        }
    }
}
