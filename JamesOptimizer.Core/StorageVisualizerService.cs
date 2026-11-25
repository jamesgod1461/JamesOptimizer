using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace JamesOptimizer.Core;

public sealed class StorageNodeItem
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public long SizeBytes { get; set; }
    public bool IsDirectory { get; set; }
    public DateTime LastModified { get; set; }
    public List<StorageNodeItem> Children { get; set; } = new();
}

[SupportedOSPlatform("windows")]
public static class StorageVisualizerService
{
    public static async Task<(StorageNodeItem Root, List<StorageNodeItem> LargestFiles, List<StorageNodeItem> OldestFiles)> ScanPathAsync(
        string rootPath,
        int maxDepth = 4,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var largest = new List<StorageNodeItem>();
            var oldest = new List<StorageNodeItem>();
            var root = ScanDirectory(new DirectoryInfo(rootPath), 0, maxDepth, largest, oldest, ct);

            var topLargest = largest.OrderByDescending(f => f.SizeBytes).Take(30).ToList();
            var topOldest = oldest.Where(f => f.SizeBytes > 10 * 1024 * 1024).OrderBy(f => f.LastModified).Take(30).ToList();
            return (root, topLargest, topOldest);
        }, ct);
    }

    private static StorageNodeItem ScanDirectory(
        DirectoryInfo dir,
        int depth,
        int maxDepth,
        List<StorageNodeItem> largest,
        List<StorageNodeItem> oldest,
        CancellationToken ct)
    {
        var node = new StorageNodeItem
        {
            Name = dir.Name,
            FullPath = dir.FullName,
            IsDirectory = true,
            LastModified = dir.LastWriteTime
        };

        if ((dir.Attributes & FileAttributes.ReparsePoint) != 0)
            return node;

        long totalSize = 0;

        try
        {
            foreach (var fi in dir.EnumerateFiles())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if ((fi.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0) continue;

                    long len = fi.Length;
                    totalSize += len;

                    var fileItem = new StorageNodeItem
                    {
                        Name = fi.Name,
                        FullPath = fi.FullName,
                        SizeBytes = len,
                        IsDirectory = false,
                        LastModified = fi.LastWriteTime
                    };

                    if (len >= 50L * 1024 * 1024)
                        largest.Add(fileItem);
                    if (len >= 10L * 1024 * 1024 && fi.LastWriteTime < DateTime.Now.AddMonths(-6))
                        oldest.Add(fileItem);

                    if (depth < 2 && len >= 20L * 1024 * 1024)
                        node.Children.Add(fileItem);
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }

            foreach (var sub in dir.EnumerateDirectories())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if ((sub.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if (sub.Name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) ||
                        sub.Name.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var childNode = depth < maxDepth
                        ? ScanDirectory(sub, depth + 1, maxDepth, largest, oldest, ct)
                        : new StorageNodeItem { Name = sub.Name, FullPath = sub.FullName, IsDirectory = true, SizeBytes = MeasureFastSize(sub, ct) };

                    totalSize += childNode.SizeBytes;
                    if (depth < maxDepth && childNode.SizeBytes > 0)
                        node.Children.Add(childNode);
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (OperationCanceledException) { throw; }
        catch { }

        node.SizeBytes = totalSize;
        node.Children = node.Children.OrderByDescending(c => c.SizeBytes).Take(18).ToList();
        return node;
    }

    private static long MeasureFastSize(DirectoryInfo dir, CancellationToken ct)
    {
        long size = 0;
        try
        {
            var opts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Offline };
            foreach (string f in Directory.EnumerateFiles(dir.FullName, "*", opts))
            {
                ct.ThrowIfCancellationRequested();
                try { size += new FileInfo(f).Length; } catch { }
            }
        }
        catch { }
        return size;
    }
}
