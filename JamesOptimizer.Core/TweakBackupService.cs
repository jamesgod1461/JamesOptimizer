using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Text.Json;
using Microsoft.Win32;

namespace JamesOptimizer.Core;

public enum SnapshotStepKind
{
    RegistryValue,
    WindowsService,
    PowerPlan,
    BcdValue,
    HostsFile,
    ScheduledTask,
    EnvironmentPath,
    PowerSetting
}

public sealed class SnapshotStep
{
    public SnapshotStepKind Kind { get; set; }
    public string KeyPath { get; set; } = "";
    public string ValueName { get; set; } = "";
    public string? OriginalValueText { get; set; }
    public string ValueKind { get; set; } = "DWord";
    public bool WasAbsent { get; set; }
    public bool KeyWasAbsent { get; set; }
    public string AppliedValueDisplay { get; set; } = "";

    // Service metadata
    public string ServiceName { get; set; } = "";
    public string OriginalStartMode { get; set; } = "";
    public bool OriginalRunning { get; set; }

    // Power / BCD / Hosts / Task metadata
    public string ExtraTarget { get; set; } = "";
}

public sealed class TweakSnapshot
{
    public int SchemaVersion { get; set; } = 2;
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string TweakId { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Category { get; set; } = "SYSTEM";
    public string Title { get; set; } = "";
    public string KeyPath { get; set; } = "";
    public string ValueName { get; set; } = "";
    public string? OriginalValueText { get; set; }
    public string ValueKind { get; set; } = "DWord";
    public bool WasAbsent { get; set; }
    public string AppliedValueDisplay { get; set; } = "";
    public bool IsReverted { get; set; }
    public List<SnapshotStep> Steps { get; set; } = new();

    public string TimeLabel => Timestamp.ToString("HH:mm:ss");
    public string StatusLabel => IsReverted ? "REVERTED" : "ACTIVE";
}

public sealed class SnapshotStore
{
    public int SchemaVersion { get; set; } = 2;
    public List<TweakSnapshot> Snapshots { get; set; } = new();
}

/// <summary>
/// Full-spectrum atomic snapshot & revert store covering Registry (with tombstones),
/// Windows Services, Power Plans, BCD flags, HOSTS files, Scheduled Tasks, and PATH.
/// </summary>
[SupportedOSPlatform("windows")]
public static class TweakBackupService
{
    private static readonly object _sync = new();
    private static string _backupFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        CoreConstants.ProductName, "backups", "tweak_history.json");
    private static string? _cachedPath;
    private static DateTime _cachedWriteUtc;
    private static long _cachedLength;
    private static List<TweakSnapshot>? _cachedList;

    public static event EventHandler? HistoryChanged;

    public static void ConfigurePath(string path)
    {
        lock (_sync)
        {
            _backupFile = path;
            _cachedPath = null;
            _cachedList = null;
        }
    }

    public static IReadOnlyList<TweakSnapshot> GetHistory()
    {
        lock (_sync)
        {
            return LoadInternal().OrderByDescending(s => s.Timestamp).ToList();
        }
    }

    public static TweakSnapshot? GetLatestActiveForTweak(string tweakId)
    {
        lock (_sync)
        {
            return LoadInternal()
                .OrderByDescending(s => s.Timestamp)
                .FirstOrDefault(s => !s.IsReverted && s.TweakId.Equals(tweakId, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Returns the set of TweakIds that currently have an active (non-reverted) snapshot.
    /// Reads the snapshot store once for batch UI/catalog queries.
    /// </summary>
    public static HashSet<string> GetActiveTweakIds()
    {
        lock (_sync)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in LoadInternal())
            {
                if (!s.IsReverted && !string.IsNullOrEmpty(s.TweakId))
                    ids.Add(s.TweakId);
            }
            return ids;
        }
    }

    public static SnapshotStep CaptureRegistryStep(string fullKeyPath, string valueName, object newValue, RegistryValueKind kind)
    {
        var (root, sub) = SplitRegistryPath(fullKeyPath);
        bool keyWasAbsent = false;
        object? oldVal = null;
        bool wasAbsent = false;
        RegistryValueKind oldKind = kind;

        if (root != null)
        {
            using var existingKey = root.OpenSubKey(sub, false);
            if (existingKey == null)
            {
                keyWasAbsent = true;
                wasAbsent = true;
            }
            else
            {
                oldVal = existingKey.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (oldVal == null)
                {
                    wasAbsent = true;
                }
                else
                {
                    oldKind = existingKey.GetValueKind(valueName);
                }
            }
        }
        else
        {
            wasAbsent = true;
        }

        string serialized = SerializeRegistryValue(oldVal, oldKind);
        return new SnapshotStep
        {
            Kind = SnapshotStepKind.RegistryValue,
            KeyPath = fullKeyPath,
            ValueName = valueName,
            OriginalValueText = serialized,
            ValueKind = oldKind.ToString(),
            WasAbsent = wasAbsent,
            KeyWasAbsent = keyWasAbsent,
            AppliedValueDisplay = newValue?.ToString() ?? ""
        };
    }

    public static bool ApplyRegistryWithBackup(
        string category,
        string title,
        string fullKeyPath,
        string valueName,
        object newValue,
        RegistryValueKind kind,
        string tweakId = "")
    {
        ApplyMultiRegistryWithBackup(
            category,
            title,
            tweakId,
            new[] { (fullKeyPath, valueName, newValue, kind) },
            appliedDisplay: newValue.ToString() ?? "",
            journalOperation: "APPLY_REG");
        return true;
    }

    public static IReadOnlyList<(string Target, string Before, string After)> ApplyMultiRegistryWithBackup(
        string category,
        string title,
        string tweakId,
        IReadOnlyList<(string KeyPath, string ValueName, object Value, RegistryValueKind Kind)> ops,
        string? appliedDisplay = null,
        string journalOperation = "APPLY_TWEAK")
    {
        if (ops.Count == 0) return Array.Empty<(string, string, string)>();

        var steps = new List<SnapshotStep>(ops.Count);
        var changes = new List<(string Target, string Before, string After)>(ops.Count);
        foreach (var op in ops)
        {
            var step = CaptureRegistryStep(op.KeyPath, op.ValueName, op.Value, op.Kind);
            steps.Add(step);
            changes.Add((
                $"{op.KeyPath}\\{op.ValueName}",
                step.WasAbsent ? "<absent>" : (step.OriginalValueText ?? ""),
                op.Value.ToString() ?? ""));
        }

        var snap = new TweakSnapshot
        {
            TweakId = tweakId,
            Category = category,
            Title = title,
            KeyPath = ops[0].KeyPath,
            ValueName = ops[0].ValueName,
            OriginalValueText = steps[0].OriginalValueText,
            ValueKind = steps[0].ValueKind,
            WasAbsent = steps[0].WasAbsent,
            AppliedValueDisplay = appliedDisplay ?? $"{ops.Count} value(s)",
            Steps = steps
        };

        var journal = WriteAheadJournal.Begin(journalOperation, tweakId, snap);
        int appliedCount = 0;
        try
        {
            foreach (var op in ops)
            {
                Registry.SetValue(op.KeyPath, op.ValueName, op.Value, op.Kind);
                appliedCount++;
            }
            RecordSnapshot(snap);
            WriteAheadJournal.MarkCommitted(journal.Id);
            return changes;
        }
        catch (Exception ex)
        {
            for (int i = appliedCount - 1; i >= 0; i--)
            {
                try { RevertStep(steps[i]); } catch { }
            }
            WriteAheadJournal.MarkFailed(journal.Id, ex.Message);
            SharedLogger.Error("TweakBackup", $"{journalOperation} failed for {ops[0].KeyPath}\\{ops[0].ValueName}", ex);
            throw;
        }
    }

    public static void RecordSnapshot(TweakSnapshot snap)
    {
        lock (_sync)
        {
            var list = LoadInternal();
            list.Add(snap);
            if (list.Count > 250)
                list.RemoveRange(0, list.Count - 250);
            SaveInternal(list);
        }
        HistoryChanged?.Invoke(null, EventArgs.Empty);
    }

    public static bool RevertSnapshot(string id, out string message)
    {
        bool ok;
        lock (_sync)
        {
            var list = LoadInternal();
            var snap = list.FirstOrDefault(x => x.Id == id);
            if (snap == null)
            {
                message = "Snapshot not found.";
                return false;
            }

            ok = RevertSnapshotInternal(snap, list, out message, raiseEvent: false);
        }
        if (ok) HistoryChanged?.Invoke(null, EventArgs.Empty);
        return ok;
    }

    public static bool RevertTweakById(string tweakId, out string message)
    {
        bool ok;
        lock (_sync)
        {
            var list = LoadInternal();
            var snap = list.OrderByDescending(x => x.Timestamp)
                .FirstOrDefault(x => !x.IsReverted && x.TweakId.Equals(tweakId, StringComparison.OrdinalIgnoreCase));
            if (snap == null)
            {
                message = $"No active backup snapshot found for '{tweakId}'.";
                return false;
            }

            ok = RevertSnapshotInternal(snap, list, out message, raiseEvent: false);
        }
        if (ok) HistoryChanged?.Invoke(null, EventArgs.Empty);
        return ok;
    }

    public static (int Reverted, int Failed, List<string> Errors) RevertAllSnapshots()
    {
        int ok = 0, fail = 0;
        var errors = new List<string>();
        lock (_sync)
        {
            var list = LoadInternal();
            foreach (var snap in list.Where(s => !s.IsReverted).OrderByDescending(s => s.Timestamp))
            {
                if (RevertSnapshotInternal(snap, list, out string msg, saveNow: false, raiseEvent: false))
                    ok++;
                else
                {
                    fail++;
                    errors.Add($"{snap.Title}: {msg}");
                }
            }
            SaveInternal(list);
        }
        HistoryChanged?.Invoke(null, EventArgs.Empty);
        return (ok, fail, errors);
    }

    public static bool RevertStep(SnapshotStep step)
    {
        switch (step.Kind)
        {
            case SnapshotStepKind.RegistryValue:
                if (step.WasAbsent)
                {
                    DeleteRegistryValue(step.KeyPath, step.ValueName, step.KeyWasAbsent);
                }
                else if (Enum.TryParse<RegistryValueKind>(step.ValueKind, out var kind))
                {
                    object restored = DeserializeRegistryValue(step.OriginalValueText, kind);
                    Registry.SetValue(step.KeyPath, step.ValueName, restored, kind);
                }
                return true;

            case SnapshotStepKind.WindowsService:
                if (CoreConstants.IsProtectedService(step.ServiceName)) return false;
                if (!string.IsNullOrEmpty(step.OriginalStartMode))
                {
                    SetServiceStartMode(step.ServiceName, step.OriginalStartMode);
                }
                using (var sc = new ServiceController(step.ServiceName))
                {
                    if (step.OriginalRunning && sc.Status != ServiceControllerStatus.Running)
                    {
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(5));
                    }
                    else if (!step.OriginalRunning && sc.CanStop && sc.Status == ServiceControllerStatus.Running)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(5));
                    }
                }
                return true;

            case SnapshotStepKind.PowerPlan:
                if (!string.IsNullOrWhiteSpace(step.OriginalValueText))
                {
                    var res = NativeSystemHelper.RunSystem32ToolSync("powercfg.exe", new[] { "/setactive", step.OriginalValueText });
                    if (res.ExitCode != 0)
                        throw new InvalidOperationException($"powercfg /setactive failed (exit {res.ExitCode}): {res.StdErr}".Trim());
                }
                return true;

            case SnapshotStepKind.PowerSetting:
                if (!string.IsNullOrWhiteSpace(step.KeyPath) &&
                    !string.IsNullOrWhiteSpace(step.ExtraTarget) &&
                    !string.IsNullOrWhiteSpace(step.OriginalValueText))
                {
                    var r1 = NativeSystemHelper.RunSystem32ToolSync("powercfg.exe", new[] { "/setacvalueindex", "SCHEME_CURRENT", step.KeyPath, step.ExtraTarget, step.OriginalValueText });
                    if (r1.ExitCode != 0)
                        throw new InvalidOperationException($"powercfg /setacvalueindex failed (exit {r1.ExitCode}): {r1.StdErr}".Trim());
                    var r2 = NativeSystemHelper.RunSystem32ToolSync("powercfg.exe", new[] { "/setactive", "SCHEME_CURRENT" });
                    if (r2.ExitCode != 0)
                        throw new InvalidOperationException($"powercfg /setactive failed (exit {r2.ExitCode}): {r2.StdErr}".Trim());
                }
                return true;

            case SnapshotStepKind.BcdValue:
                if (step.WasAbsent)
                {
                    var rDel = NativeSystemHelper.RunSystem32ToolSync("bcdedit.exe", new[] { "/deletevalue", step.ExtraTarget });
                    if (rDel.ExitCode != 0)
                        throw new InvalidOperationException($"bcdedit /deletevalue {step.ExtraTarget} failed (exit {rDel.ExitCode}): {rDel.StdErr}".Trim());
                }
                else if (!string.IsNullOrEmpty(step.OriginalValueText))
                {
                    var rSet = NativeSystemHelper.RunSystem32ToolSync("bcdedit.exe", new[] { "/set", step.ExtraTarget, step.OriginalValueText });
                    if (rSet.ExitCode != 0)
                        throw new InvalidOperationException($"bcdedit /set {step.ExtraTarget} failed (exit {rSet.ExitCode}): {rSet.StdErr}".Trim());
                }
                return true;

            case SnapshotStepKind.HostsFile:
                if (!string.IsNullOrEmpty(step.ExtraTarget) && File.Exists(step.ExtraTarget))
                {
                    string hostsPath = Path.Combine(Environment.SystemDirectory, @"drivers\etc\hosts");
                    File.Copy(step.ExtraTarget, hostsPath, overwrite: true);
                    NativeSystemHelper.FlushDnsNative();
                }
                return true;

            case SnapshotStepKind.EnvironmentPath:
                if (step.KeyPath.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase))
                    Registry.SetValue(@"HKEY_CURRENT_USER\Environment", "Path", step.OriginalValueText ?? "", RegistryValueKind.ExpandString);
                else
                    Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Environment", "Path", step.OriginalValueText ?? "", RegistryValueKind.ExpandString);
                NativeSystemHelper.BroadcastEnvironmentChange();
                return true;

            case SnapshotStepKind.ScheduledTask:
                string flag = string.Equals(step.OriginalValueText, "True", StringComparison.OrdinalIgnoreCase) ? "/ENABLE" : "/DISABLE";
                var rTask = NativeSystemHelper.RunSystem32ToolSync("schtasks.exe", new[] { "/Change", "/TN", step.ExtraTarget, flag });
                if (rTask.ExitCode != 0)
                    throw new InvalidOperationException($"schtasks /Change failed (exit {rTask.ExitCode}): {rTask.StdErr}".Trim());
                return true;

            default:
                return false;
        }
    }

    private static bool RevertSnapshotInternal(TweakSnapshot snap, List<TweakSnapshot> list, out string message, bool saveNow = true, bool raiseEvent = true)
    {
        try
        {
            if (snap.Steps.Count > 0)
            {
                for (int i = snap.Steps.Count - 1; i >= 0; i--)
                {
                    if (!RevertStep(snap.Steps[i]))
                    {
                        message = $"Revert failed: step {i + 1} ({snap.Steps[i].Kind}) did not succeed.";
                        return false;
                    }
                }
            }
            else
            {
                // V1 single-registry snapshot compatibility
                var compatStep = new SnapshotStep
                {
                    Kind = SnapshotStepKind.RegistryValue,
                    KeyPath = snap.KeyPath,
                    ValueName = snap.ValueName,
                    OriginalValueText = snap.OriginalValueText,
                    ValueKind = snap.ValueKind,
                    WasAbsent = snap.WasAbsent
                };
                if (!RevertStep(compatStep))
                {
                    message = "Revert failed: legacy snapshot step did not succeed.";
                    return false;
                }
            }

            snap.IsReverted = true;
            if (saveNow)
            {
                SaveInternal(list);
                if (raiseEvent) HistoryChanged?.Invoke(null, EventArgs.Empty);
            }
            message = $"Reverted '{snap.Title}' cleanly.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Revert failed: {ex.Message}";
            SharedLogger.Error("TweakBackup", $"Revert failed for {snap.Title}", ex);
            return false;
        }
    }

    public static void SetServiceStartMode(string serviceName, string startMode)
    {
        if (CoreConstants.IsProtectedService(serviceName))
            throw new InvalidOperationException($"Service '{serviceName}' is protected and cannot be modified.");

        string scMode = startMode.ToLowerInvariant() switch
        {
            "auto" or "automatic" => "auto",
            "manual" or "demand" => "demand",
            "disabled" => "disabled",
            _ => "demand"
        };
        // BUG FIX: "start= disabled" as one argv is invalid; sc.exe requires "start=" and value as separate tokens.
        var res = NativeSystemHelper.RunSystem32ToolSync("sc.exe", new[] { "config", serviceName, "start=", scMode });
        if (res.ExitCode != 0)
        {
            string details = string.IsNullOrWhiteSpace(res.StdErr) ? res.StdOut : res.StdErr;
            throw new InvalidOperationException($"sc.exe config failed for '{serviceName}' (exit {res.ExitCode}): {details.Trim()}");
        }
    }

    private static string SerializeRegistryValue(object? val, RegistryValueKind kind)
    {
        if (val == null) return "";
        return kind switch
        {
            RegistryValueKind.Binary when val is byte[] bytes => Convert.ToBase64String(bytes),
            RegistryValueKind.MultiString when val is string[] arr => string.Join("\n", arr),
            _ => val.ToString() ?? ""
        };
    }

    private static object DeserializeRegistryValue(string? text, RegistryValueKind kind)
    {
        text ??= "";
        return kind switch
        {
            RegistryValueKind.DWord => int.TryParse(text, out int iv) ? iv : 0,
            RegistryValueKind.QWord => long.TryParse(text, out long lv) ? lv : 0L,
            RegistryValueKind.Binary => string.IsNullOrEmpty(text) ? Array.Empty<byte>() : Convert.FromBase64String(text),
            RegistryValueKind.MultiString => text.Split('\n', StringSplitOptions.None),
            _ => text
        };
    }

    private static (RegistryKey? Root, string SubKey) SplitRegistryPath(string fullKeyPath)
    {
        int split = fullKeyPath.IndexOf('\\');
        if (split <= 0) return (null, "");
        string hive = fullKeyPath[..split].ToUpperInvariant();
        string sub = fullKeyPath[(split + 1)..];
        RegistryKey? root = hive switch
        {
            "HKEY_CURRENT_USER" or "HKCU" => Registry.CurrentUser,
            "HKEY_LOCAL_MACHINE" or "HKLM" => Registry.LocalMachine,
            "HKEY_CLASSES_ROOT" or "HKCR" => Registry.ClassesRoot,
            _ => null
        };
        return (root, sub);
    }

    private static void DeleteRegistryValue(string fullKeyPath, string valueName, bool deleteKeyIfEmpty)
    {
        var (root, sub) = SplitRegistryPath(fullKeyPath);
        if (root == null) return;
        using (var key = root.OpenSubKey(sub, writable: true))
        {
            key?.DeleteValue(valueName, false);
        }

        if (deleteKeyIfEmpty)
        {
            try
            {
                using var check = root.OpenSubKey(sub, false);
                if (check != null && check.ValueCount == 0 && check.SubKeyCount == 0)
                    root.DeleteSubKey(sub, false);
            }
            catch { }
        }
    }

    private static List<TweakSnapshot> LoadInternal()
    {
        try
        {
            var fi = new FileInfo(_backupFile);
            if (!fi.Exists)
            {
                _cachedPath = null;
                _cachedList = null;
                return new();
            }

            if (_cachedList != null &&
                string.Equals(_cachedPath, _backupFile, StringComparison.OrdinalIgnoreCase) &&
                _cachedWriteUtc == fi.LastWriteTimeUtc &&
                _cachedLength == fi.Length)
            {
                return new List<TweakSnapshot>(_cachedList);
            }

            string json = File.ReadAllText(_backupFile);
            string trimmed = json.TrimStart();
            List<TweakSnapshot> loaded;
            if (trimmed.StartsWith("["))
            {
                // Schema v1 array migration -> v2
                loaded = JsonSerializer.Deserialize<List<TweakSnapshot>>(json) ?? new();
                foreach (var s in loaded) s.SchemaVersion = 2;
            }
            else
            {
                var store = JsonSerializer.Deserialize<SnapshotStore>(json);
                loaded = store?.Snapshots ?? new();
            }

            fi.Refresh();
            _cachedPath = _backupFile;
            _cachedWriteUtc = fi.LastWriteTimeUtc;
            _cachedLength = fi.Length;
            _cachedList = loaded;
            return new List<TweakSnapshot>(loaded);
        }
        catch (Exception ex)
        {
            _cachedPath = null;
            _cachedList = null;
            SharedLogger.Error("TweakBackup", $"Corrupt or unreadable backup file '{_backupFile}'; preserving copy before reset.", ex);
            try
            {
                if (File.Exists(_backupFile))
                {
                    string corruptCopy = $"{_backupFile}.corrupt.{DateTime.UtcNow:yyyyMMddHHmmssfff}";
                    File.Copy(_backupFile, corruptCopy, overwrite: true);
                }
            }
            catch { }
            return new();
        }
    }

    private static void SaveInternal(List<TweakSnapshot> list)
    {
        string tmp = $"{_backupFile}.{Guid.NewGuid():N}.tmp";
        try
        {
            string? dir = Path.GetDirectoryName(_backupFile);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var store = new SnapshotStore { SchemaVersion = 2, Snapshots = list };
            File.WriteAllText(tmp, JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, _backupFile, overwrite: true);

            var fi = new FileInfo(_backupFile);
            _cachedPath = _backupFile;
            _cachedWriteUtc = fi.LastWriteTimeUtc;
            _cachedLength = fi.Length;
            _cachedList = new List<TweakSnapshot>(list);
        }
        catch (Exception ex)
        {
            _cachedPath = null;
            _cachedList = null;
            SharedLogger.Error("TweakBackup", $"Failed saving backup store '{_backupFile}'", ex);
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }
}
