using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace JamesOptimizer.Core;

public sealed class StaleDriverPackage
{
    public string PublishedName { get; set; } = ""; // e.g. oem12.inf
    public string OriginalName { get; set; } = "";
    public string Provider { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string VersionText { get; set; } = "";
    public DateTime DriverDate { get; set; }
    public bool IsSelected { get; set; }
}

[SupportedOSPlatform("windows")]
public static class DeepCleanupService
{
    public static Task<ToolRunResult> CleanWinSxSComponentStoreAsync(CancellationToken ct = default) =>
        NativeSystemHelper.RunSystem32ToolAsync("Dism.exe", new[] { "/Online", "/Cleanup-Image", "/StartComponentCleanup" }, ct);

    public static async Task<(long FreedBytes, string Message)> CleanWindowsUpdateCacheSafelyAsync(CancellationToken ct = default)
    {
        string downloadDir = @"C:\Windows\SoftwareDistribution\Download";
        if (!Directory.Exists(downloadDir))
            return (0, "Windows Update Download cache folder does not exist.");

        bool wasRunning = false;
        try
        {
            using var sc = new ServiceController("wuauserv");
            wasRunning = sc.Status == ServiceControllerStatus.Running;
            if (wasRunning && sc.CanStop)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
            }
        }
        catch (Exception ex)
        {
            return (0, $"Could not pause Windows Update service cleanly: {ex.Message}");
        }

        long freed = 0;
        try
        {
            await Task.Run(() =>
            {
                // BUG FIX: Use AttributesToSkip = ReparsePoint to avoid following directory junctions
                // outside the SoftwareDistribution\Download folder.
                var opts = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                };
                foreach (string file in Directory.EnumerateFiles(downloadDir, "*", opts))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        long len = new FileInfo(file).Length;
                        File.Delete(file);
                        freed += len;
                    }
                    catch { }
                }
            }, ct);
        }
        finally
        {
            if (wasRunning)
            {
                try
                {
                    using var sc = new ServiceController("wuauserv");
                    if (sc.Status != ServiceControllerStatus.Running)
                        sc.Start();
                }
                catch { }
            }
        }

        return (freed, $"Cleared {freed / (1024.0 * 1024.0):F1} MB from SoftwareDistribution\\Download and restored wuauserv.");
    }

    public static bool HasWindowsOld(out long sizeBytes)
    {
        sizeBytes = 0;
        string path = @"C:\Windows.old";
        if (!Directory.Exists(path)) return false;
        try
        {
            var opts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (string f in Directory.EnumerateFiles(path, "*", opts).Take(5000))
            {
                try { sizeBytes += new FileInfo(f).Length; } catch { }
            }
        }
        catch { }
        return true;
    }

    public static async Task<List<StaleDriverPackage>> FindStaleOemDriversAsync(CancellationToken ct = default)
    {
        var res = await NativeSystemHelper.RunSystem32ToolAsync("pnputil.exe", new[] { "/enum-drivers" }, ct);
        var allDrivers = ParsePnputilDrivers(res.StdOut);

        // Group by OriginalName + Provider and always keep the newest driver per group
        var stale = new List<StaleDriverPackage>();
        foreach (var grp in allDrivers.GroupBy(d => $"{d.OriginalName}|{d.Provider}", StringComparer.OrdinalIgnoreCase))
        {
            var ordered = grp.OrderByDescending(d => d.DriverDate).ThenByDescending(d => d.VersionText).ToList();
            if (ordered.Count <= 1) continue;
            stale.AddRange(ordered.Skip(1));
        }
        return stale;
    }

    public static async Task<(bool Ok, string Message)> RemoveOemDriverPackageAsync(string publishedInfName, CancellationToken ct = default)
    {
        if (!Regex.IsMatch(publishedInfName ?? "", @"^oem\d+\.inf$", RegexOptions.IgnoreCase))
            return (false, $"Invalid OEM driver package name '{publishedInfName}'.");

        var res = await NativeSystemHelper.RunSystem32ToolAsync("pnputil.exe", new[] { "/delete-driver", publishedInfName! }, ct);
        return res.ExitCode == 0
            ? (true, $"Removed stale driver '{publishedInfName}'.")
            : (false, $"pnputil returned {res.ExitCode} (driver may still be in use by a connected device).");
    }

    /// <summary>
    /// Measures total file bytes at <paramref name="path"/> without traversing directory or file reparse points.
    /// </summary>
    public static long MeasurePathBytesSafe(string path, CancellationToken ct = default)
    {
        if (File.Exists(path))
        {
            try
            {
                var fi = new FileInfo(path);
                return (fi.Attributes & FileAttributes.ReparsePoint) != 0 ? 0 : fi.Length;
            }
            catch { return 0; }
        }
        if (!Directory.Exists(path)) return 0;

        long size = 0;
        try
        {
            var opts = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            foreach (string file in Directory.EnumerateFiles(path, "*", opts))
            {
                ct.ThrowIfCancellationRequested();
                try { size += new FileInfo(file).Length; } catch { }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { }
        return size;
    }

    /// <summary>
    /// Deletes files and non-reparse subdirectories under <paramref name="path"/> without following junctions or symlinks.
    /// </summary>
    public static long CleanPathSafe(string path, CancellationToken ct = default)
    {
        long freed = 0;
        if (File.Exists(path))
        {
            try
            {
                var fi = new FileInfo(path);
                if ((fi.Attributes & FileAttributes.ReparsePoint) != 0) return 0;
                long len = fi.Length;
                fi.Delete();
                return len;
            }
            catch { return 0; }
        }
        if (!Directory.Exists(path)) return 0;

        try
        {
            var fileOpts = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            foreach (string file in Directory.EnumerateFiles(path, "*", fileOpts))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    long len = new FileInfo(file).Length;
                    File.Delete(file);
                    freed += len;
                }
                catch { }
            }

            // Delete empty non-reparse subdirectories bottom-up so nested junctions are never traversed.
            var dirOpts = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            var subdirs = Directory.EnumerateDirectories(path, "*", dirOpts)
                .OrderByDescending(d => d.Length)
                .ToList();
            foreach (string dir in subdirs)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var di = new DirectoryInfo(dir);
                    if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    di.Delete(false);
                }
                catch { }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { }
        return freed;
    }

    private static List<StaleDriverPackage> ParsePnputilDrivers(string output)
    {
        var list = new List<StaleDriverPackage>();
        StaleDriverPackage? cur = null;

        foreach (string rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            string key = line[..colon].Trim();
            string val = line[(colon + 1)..].Trim();

            if (key.Equals("Published Name", StringComparison.OrdinalIgnoreCase))
            {
                if (cur != null) list.Add(cur);
                cur = new StaleDriverPackage { PublishedName = val };
            }
            else if (cur != null)
            {
                if (key.Equals("Original Name", StringComparison.OrdinalIgnoreCase)) cur.OriginalName = val;
                else if (key.Equals("Provider Name", StringComparison.OrdinalIgnoreCase)) cur.Provider = val;
                else if (key.Equals("Class Name", StringComparison.OrdinalIgnoreCase)) cur.ClassName = val;
                else if (key.Equals("Driver Version", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = val.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0 && DateTime.TryParse(parts[0], out var dt)) cur.DriverDate = dt;
                    if (parts.Length > 1) cur.VersionText = parts[1];
                }
            }
        }
        if (cur != null) list.Add(cur);
        return list;
    }
}
