using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace JamesOptimizer.Core;

public enum JournalStatus
{
    Pending,
    Committed,
    RolledBack,
    Failed
}

public sealed class JournalEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string Operation { get; set; } = "";
    public string TweakId { get; set; } = "";
    public JournalStatus Status { get; set; } = JournalStatus.Pending;
    public TweakSnapshot? Snapshot { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Crash-safe write-ahead journal: writes intended revert state to disk BEFORE mutating the OS.
/// </summary>
public static class WriteAheadJournal
{
    private static readonly object _sync = new();
    private static string _journalPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        CoreConstants.ProductName, "backups", "wal_journal.json");

    public static void ConfigurePath(string path)
    {
        lock (_sync)
        {
            _journalPath = path;
        }
    }

    public static JournalEntry Begin(string operation, string tweakId, TweakSnapshot? snapshot)
    {
        lock (_sync)
        {
            var entries = LoadInternal();
            var entry = new JournalEntry
            {
                Operation = operation,
                TweakId = tweakId,
                Status = JournalStatus.Pending,
                Snapshot = snapshot
            };
            entries.Add(entry);
            if (entries.Count > 200) entries.RemoveRange(0, entries.Count - 200);
            SaveInternal(entries);
            return entry;
        }
    }

    public static void MarkCommitted(string journalId) => UpdateStatus(journalId, JournalStatus.Committed, null);
    public static void MarkRolledBack(string journalId) => UpdateStatus(journalId, JournalStatus.RolledBack, null);
    public static void MarkFailed(string journalId, string error) => UpdateStatus(journalId, JournalStatus.Failed, error);

    public static IReadOnlyList<JournalEntry> GetIncompleteEntries()
    {
        lock (_sync)
        {
            return LoadInternal().Where(e => e.Status == JournalStatus.Pending).ToList();
        }
    }

    private static void UpdateStatus(string id, JournalStatus status, string? error)
    {
        lock (_sync)
        {
            var entries = LoadInternal();
            var entry = entries.FirstOrDefault(e => e.Id == id);
            if (entry == null) return;
            entry.Status = status;
            entry.ErrorMessage = error;
            SaveInternal(entries);
        }
    }

    private static List<JournalEntry> LoadInternal()
    {
        try
        {
            if (!File.Exists(_journalPath)) return new();
            return JsonSerializer.Deserialize<List<JournalEntry>>(File.ReadAllText(_journalPath)) ?? new();
        }
        catch (Exception ex)
        {
            SharedLogger.Error("WAL", $"Corrupt or unreadable journal file '{_journalPath}'; preserving copy before reset.", ex);
            try
            {
                if (File.Exists(_journalPath))
                {
                    string corruptCopy = $"{_journalPath}.corrupt.{DateTime.UtcNow:yyyyMMddHHmmssfff}";
                    File.Copy(_journalPath, corruptCopy, overwrite: true);
                }
            }
            catch { }
            return new();
        }
    }

    private static void SaveInternal(List<JournalEntry> entries)
    {
        string tmp = $"{_journalPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            string? dir = Path.GetDirectoryName(_journalPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(tmp, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, _journalPath, overwrite: true);
        }
        catch (Exception ex)
        {
            SharedLogger.Error("WAL", $"Failed saving write-ahead journal '{_journalPath}'", ex);
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }
}
