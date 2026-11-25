using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace JamesOptimizer.Core;

public sealed class RunAliasEntry
{
    public string Alias { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
}

public sealed class PathEntryItem
{
    public string Scope { get; set; } = "User"; // User or System
    public string RawEntry { get; set; } = "";
    public string ExpandedPath { get; set; } = "";
    public bool Exists { get; set; }
    public bool IsDuplicate { get; set; }
}

[SupportedOSPlatform("windows")]
public static partial class IntegratorService
{
    private const string AppPathsSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
    private const string DesktopContextSubKey = @"Directory\Background\shell";
    private const string AllFilesContextSubKey = @"*\shell";

    public static bool IsValidRunAliasName(string? alias, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(alias))
        {
            reason = "Alias cannot be empty.";
            return false;
        }

        string clean = alias.Trim();
        if (clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            clean = clean[..^4];

        if (!SafeAliasRegex().IsMatch(clean))
        {
            reason = "Alias may only contain letters, numbers, hyphens, and underscores (2-32 chars).";
            return false;
        }

        if (CoreConstants.ProtectedRunAliases.Contains(clean))
        {
            reason = $"Alias '{clean}' is a protected Windows command and cannot be overridden.";
            return false;
        }

        return true;
    }

    public static List<RunAliasEntry> GetCustomRunAliases()
    {
        var list = new List<RunAliasEntry>();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(AppPathsSubKey, false);
            if (key == null) return list;
            foreach (string sub in key.GetSubKeyNames())
            {
                using var sk = key.OpenSubKey(sub, false);
                if (sk?.GetValue("JamesOptimizerAlias") is int mark && mark == 1)
                {
                    list.Add(new RunAliasEntry
                    {
                        Alias = sub,
                        ExecutablePath = sk.GetValue("")?.ToString() ?? ""
                    });
                }
            }
        }
        catch { }
        return list.OrderBy(a => a.Alias).ToList();
    }

    public static (bool Ok, string Message) AddRunAlias(string alias, string targetExePath)
    {
        if (!IsValidRunAliasName(alias, out string reason))
            return (false, reason);

        if (string.IsNullOrWhiteSpace(targetExePath) || !File.Exists(targetExePath))
            return (false, "Target executable file does not exist.");

        string cleanAlias = alias.Trim();
        if (!cleanAlias.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            cleanAlias += ".exe";

        string fullKey = $@"HKEY_LOCAL_MACHINE\{AppPathsSubKey}\{cleanAlias}";
        using var existing = Registry.LocalMachine.OpenSubKey($@"{AppPathsSubKey}\{cleanAlias}", false);
        if (existing != null && existing.GetValue("JamesOptimizerAlias") == null)
            return (false, $"'{cleanAlias}' already exists in App Paths and belongs to another application.");

        // BUG FIX: Capture all sub-values/subkeys atomically so Revert removes the entire alias cleanly.
        TweakBackupService.ApplyMultiRegistryWithBackup(
            "INTEGRATOR",
            $"Win+R Alias: {cleanAlias}",
            $"alias_{cleanAlias}",
            new (string, string, object, RegistryValueKind)[]
            {
                (fullKey, "", targetExePath, RegistryValueKind.String),
                (fullKey, "Path", Path.GetDirectoryName(targetExePath) ?? "", RegistryValueKind.String),
                (fullKey, "JamesOptimizerAlias", 1, RegistryValueKind.DWord)
            },
            appliedDisplay: targetExePath);
        return (true, $"Win+R alias '{cleanAlias}' registered.");
    }

    public static (bool Ok, string Message) RemoveRunAlias(string alias)
    {
        string cleanAlias = alias.Trim();
        if (!cleanAlias.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            cleanAlias += ".exe";

        try
        {
            Registry.LocalMachine.DeleteSubKeyTree($@"{AppPathsSubKey}\{cleanAlias}", false);
            return (true, $"Removed alias '{cleanAlias}'.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static (bool Ok, string Message) AddDesktopContextMenuShortcut(string title, string executablePath, string arguments = "", string? iconPath = null)
    {
        if (string.IsNullOrWhiteSpace(title) || title.IndexOfAny(new[] { '\\', '/' }) >= 0)
            return (false, "Invalid shortcut title.");
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            return (false, "Target file must exist on disk.");

        string safeKeyName = "JO_" + Regex.Replace(title.Trim(), @"[^a-zA-Z0-9_\-]", "_");
        string regBase = $@"HKEY_CLASSES_ROOT\{DesktopContextSubKey}\{safeKeyName}";
        string cmdValue = string.IsNullOrWhiteSpace(arguments)
            ? $"\"{executablePath}\""
            : $"\"{executablePath}\" {arguments}";

        // BUG FIX: Capture Icon and \command subkey atomically so Revert removes the context-menu entry cleanly.
        TweakBackupService.ApplyMultiRegistryWithBackup(
            "INTEGRATOR",
            $"Desktop Menu: {title}",
            $"ctx_{safeKeyName}",
            new (string, string, object, RegistryValueKind)[]
            {
                (regBase, "", title.Trim(), RegistryValueKind.String),
                (regBase, "Icon", iconPath ?? executablePath, RegistryValueKind.String),
                ($@"{regBase}\command", "", cmdValue, RegistryValueKind.String)
            },
            appliedDisplay: title.Trim());
        return (true, $"Added Desktop context-menu shortcut '{title}'.");
    }

    public static (bool Ok, string Message) AddFileLockContextMenuShortcut(string winUiExePath)
    {
        if (!File.Exists(winUiExePath)) return (false, "App executable not found.");
        string regBase = $@"HKEY_CLASSES_ROOT\{AllFilesContextSubKey}\JO_WhatIsLocking";
        TweakBackupService.ApplyMultiRegistryWithBackup(
            "INTEGRATOR",
            "Explorer: What's locking this?",
            "ctx_filelock",
            new (string, string, object, RegistryValueKind)[]
            {
                (regBase, "", "What's locking this? (JamesOptimizer)", RegistryValueKind.String),
                (regBase, "Icon", winUiExePath, RegistryValueKind.String),
                ($@"{regBase}\command", "", $"\"{winUiExePath}\" --file-lock \"%1\"", RegistryValueKind.String)
            },
            appliedDisplay: winUiExePath);
        return (true, "Added Explorer 'What's locking this?' menu entry.");
    }

    public static List<PathEntryItem> InspectEnvironmentPaths()
    {
        var items = new List<PathEntryItem>();
        ReadPathScope("User", Registry.CurrentUser, "Environment", items);
        ReadPathScope("System", Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment", items);
        return items;
    }

    private static void ReadPathScope(string scope, RegistryKey root, string subKey, List<PathEntryItem> items)
    {
        try
        {
            using var key = root.OpenSubKey(subKey, false);
            string raw = key?.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? "";
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string entry in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string expanded = Environment.ExpandEnvironmentVariables(entry);
                bool exists = Directory.Exists(expanded);
                bool dup = !seen.Add(expanded.TrimEnd('\\'));
                items.Add(new PathEntryItem
                {
                    Scope = scope,
                    RawEntry = entry,
                    ExpandedPath = expanded,
                    Exists = exists,
                    IsDuplicate = dup
                });
            }
        }
        catch { }
    }

    public static (bool Ok, string Message) CleanDeadAndDuplicatePaths(string scope)
    {
        bool isUser = scope.Equals("User", StringComparison.OrdinalIgnoreCase);
        RegistryKey root = isUser ? Registry.CurrentUser : Registry.LocalMachine;
        string subKey = isUser ? "Environment" : @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
        string fullKey = isUser ? @"HKEY_CURRENT_USER\Environment" : @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Environment";

        using var key = root.OpenSubKey(subKey, writable: true);
        if (key == null) return (false, $"Could not open {scope} Environment registry key.");

        string raw = key.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? "";
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<string>();
        int removed = 0;

        foreach (string entry in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string expanded = Environment.ExpandEnvironmentVariables(entry);
            if (!Directory.Exists(expanded) || !seen.Add(expanded.TrimEnd('\\')))
            {
                removed++;
                continue;
            }
            kept.Add(entry);
        }

        if (removed == 0) return (true, $"No dead or duplicate entries found in {scope} PATH.");

        var step = new SnapshotStep
        {
            Kind = SnapshotStepKind.EnvironmentPath,
            KeyPath = isUser ? "HKCU" : "HKLM",
            OriginalValueText = raw
        };
        var snap = new TweakSnapshot
        {
            TweakId = $"path_clean_{scope.ToLowerInvariant()}",
            Category = "INTEGRATOR",
            Title = $"Clean {scope} PATH ({removed} removed)",
            KeyPath = fullKey,
            ValueName = "Path",
            OriginalValueText = raw,
            ValueKind = "ExpandString",
            AppliedValueDisplay = $"{kept.Count} entries",
            Steps = new List<SnapshotStep> { step }
        };

        string updated = string.Join(';', kept);
        key.SetValue("Path", updated, RegistryValueKind.ExpandString);
        TweakBackupService.RecordSnapshot(snap);
        NativeSystemHelper.BroadcastEnvironmentChange();
        return (true, $"Removed {removed} dead/duplicate entries from {scope} PATH (backup saved).");
    }

    [GeneratedRegex(@"^[a-zA-Z0-9_\-]{2,32}$")]
    private static partial Regex SafeAliasRegex();
}
