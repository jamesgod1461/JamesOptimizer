using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;

namespace JamesOptimizer.Core;

public sealed class HostsEntry
{
    public string IpAddress { get; set; } = "0.0.0.0";
    public string Hostname { get; set; } = "";
    public string Comment { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string? Tag { get; set; }
    public int LineIndex { get; set; } = -1;
}

[SupportedOSPlatform("windows")]
public static partial class HostsManagerService
{
    public static string HostsFilePath => Path.Combine(Environment.SystemDirectory, @"drivers\etc\hosts");

    private static string BackupDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        CoreConstants.ProductName, "backups", "hosts");

    private static readonly HashSet<string> SensitiveMicrosoftDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "windowsupdate.microsoft.com",
        "update.microsoft.com",
        "download.windowsupdate.com",
        "activation.sls.microsoft.com",
        "validation.sls.microsoft.com",
        "login.live.com",
        "storeedgefd.dsx.mp.microsoft.com"
    };

    public static List<HostsEntry> ReadEntries(string? pathOverride = null)
    {
        string path = pathOverride ?? HostsFilePath;
        var entries = new List<HostsEntry>();
        if (!File.Exists(path)) return entries;

        string[] lines = File.ReadAllLines(path);
        string? activeTag = null;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("# BEGIN JamesOptimizer:", StringComparison.OrdinalIgnoreCase))
            {
                activeTag = line["# BEGIN JamesOptimizer:".Length..].Trim();
                continue;
            }
            if (line.StartsWith("# END JamesOptimizer:", StringComparison.OrdinalIgnoreCase) || line.Equals("# END", StringComparison.OrdinalIgnoreCase))
            {
                activeTag = null;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line)) continue;

            bool commented = line.StartsWith('#');
            string raw = commented ? line.TrimStart('#').Trim() : line;

            string comment = "";
            int hashIdx = raw.IndexOf('#');
            if (hashIdx >= 0)
            {
                comment = raw[(hashIdx + 1)..].Trim();
                raw = raw[..hashIdx].Trim();
            }

            var tokens = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length >= 2 && IPAddress.TryParse(tokens[0], out _) && IsValidHostname(tokens[1]))
            {
                entries.Add(new HostsEntry
                {
                    IpAddress = tokens[0],
                    Hostname = tokens[1],
                    Comment = comment,
                    Enabled = !commented,
                    Tag = activeTag,
                    LineIndex = i
                });
            }
        }

        return entries;
    }

    public static bool IsValidHostname(string? host) =>
        !string.IsNullOrWhiteSpace(host) &&
        host.Length <= 253 &&
        HostnameRegex().IsMatch(host.Trim());

    public static bool IsSensitiveDomain(string? host) =>
        !string.IsNullOrWhiteSpace(host) && SensitiveMicrosoftDomains.Contains(host.Trim());

    public static (bool Ok, string Message) AddOrUpdateEntry(string ip, string hostname, string comment, string? tag = null, string? pathOverride = null)
    {
        if (!IPAddress.TryParse(ip?.Trim(), out _))
            return (false, $"Invalid IP address '{ip}'.");
        if (!IsValidHostname(hostname))
            return (false, $"Invalid hostname '{hostname}'.");

        string path = pathOverride ?? HostsFilePath;
        BackupHostsFile(path);

        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
        string formatted = string.IsNullOrWhiteSpace(comment)
            ? $"{ip!.Trim()}\t{hostname.Trim()}"
            : $"{ip!.Trim()}\t{hostname.Trim()} # {comment.Trim()}";

        if (!string.IsNullOrWhiteSpace(tag))
        {
            string beginMarker = $"# BEGIN JamesOptimizer:{tag.Trim()}";
            string endMarker = $"# END JamesOptimizer:{tag.Trim()}";
            int beginIdx = lines.FindIndex(l => l.Trim().Equals(beginMarker, StringComparison.OrdinalIgnoreCase));
            int endIdx = beginIdx >= 0 ? lines.FindIndex(beginIdx + 1, l => l.Trim().Equals(endMarker, StringComparison.OrdinalIgnoreCase)) : -1;

            if (beginIdx >= 0 && endIdx > beginIdx)
                lines.Insert(endIdx, formatted);
            else
            {
                lines.Add("");
                lines.Add(beginMarker);
                lines.Add(formatted);
                lines.Add(endMarker);
            }
        }
        else
        {
            lines.Add(formatted);
        }

        return WriteHostsSafely(path, lines);
    }

    public static (bool Ok, string Message) SetEntryEnabled(int lineIndex, bool enabled, string? pathOverride = null)
    {
        string path = pathOverride ?? HostsFilePath;
        if (!File.Exists(path)) return (false, "HOSTS file not found.");
        var lines = File.ReadAllLines(path).ToList();
        if (lineIndex < 0 || lineIndex >= lines.Count) return (false, "Invalid HOSTS line index.");

        BackupHostsFile(path);
        string current = lines[lineIndex].TrimStart();
        if (enabled && current.StartsWith('#'))
            lines[lineIndex] = current.TrimStart('#').TrimStart();
        else if (!enabled && !current.StartsWith('#'))
            lines[lineIndex] = "# " + current;

        return WriteHostsSafely(path, lines);
    }

    public static (bool Ok, string Message) RemoveEntry(int lineIndex, string? pathOverride = null)
    {
        string path = pathOverride ?? HostsFilePath;
        if (!File.Exists(path)) return (false, "HOSTS file not found.");
        var lines = File.ReadAllLines(path).ToList();
        if (lineIndex < 0 || lineIndex >= lines.Count) return (false, "Invalid HOSTS line index.");

        BackupHostsFile(path);
        lines.RemoveAt(lineIndex);
        return WriteHostsSafely(path, lines);
    }

    public static (bool Ok, string Message) RemoveTagBlock(string tag, string? pathOverride = null)
    {
        string path = pathOverride ?? HostsFilePath;
        if (!File.Exists(path)) return (false, "HOSTS file not found.");
        var lines = File.ReadAllLines(path).ToList();
        string beginMarker = $"# BEGIN JamesOptimizer:{tag.Trim()}";
        string endMarker = $"# END JamesOptimizer:{tag.Trim()}";

        int beginIdx = lines.FindIndex(l => l.Trim().Equals(beginMarker, StringComparison.OrdinalIgnoreCase));
        if (beginIdx < 0) return (false, $"Tag block '{tag}' not found.");
        int endIdx = lines.FindIndex(beginIdx + 1, l => l.Trim().Equals(endMarker, StringComparison.OrdinalIgnoreCase));
        if (endIdx < 0) endIdx = beginIdx;

        BackupHostsFile(path);
        lines.RemoveRange(beginIdx, endIdx - beginIdx + 1);
        return WriteHostsSafely(path, lines);
    }

    public static (bool Ok, string Message) RestoreLatestBackup(string? pathOverride = null)
    {
        string path = pathOverride ?? HostsFilePath;
        if (!Directory.Exists(BackupDir)) return (false, "No HOSTS backups exist yet.");
        string? latest = Directory.EnumerateFiles(BackupDir, "hosts_*.bak").OrderByDescending(f => f).FirstOrDefault();
        if (latest == null) return (false, "No HOSTS backup file found.");

        File.Copy(latest, path, overwrite: true);
        if (pathOverride == null) NativeSystemHelper.FlushDnsNative();
        return (true, $"Restored HOSTS from {Path.GetFileName(latest)}.");
    }

    private static void BackupHostsFile(string sourcePath)
    {
        if (!File.Exists(sourcePath)) return;
        try
        {
            Directory.CreateDirectory(BackupDir);
            string dest = Path.Combine(BackupDir, $"hosts_{DateTime.Now:yyyyMMdd_HHmmss_fff}.bak");
            File.Copy(sourcePath, dest, overwrite: true);
        }
        catch { }
    }

    private static (bool Ok, string Message) WriteHostsSafely(string path, List<string> lines)
    {
        try
        {
            File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (path.Equals(HostsFilePath, StringComparison.OrdinalIgnoreCase))
                NativeSystemHelper.FlushDnsNative();
            return (true, "HOSTS file updated and DNS cache flushed.");
        }
        catch (UnauthorizedAccessException)
        {
            return (false, "Access denied writing HOSTS. Run as Administrator, or check if Windows Defender / security software is protecting the HOSTS file.");
        }
        catch (IOException ex)
        {
            return (false, $"HOSTS write blocked ({ex.Message}). Windows Defender may protect edits targeting telemetry/update domains.");
        }
    }

    [GeneratedRegex(@"^[a-zA-Z0-9]([a-zA-Z0-9\-\.]*[a-zA-Z0-9])?$")]
    private static partial Regex HostnameRegex();
}
