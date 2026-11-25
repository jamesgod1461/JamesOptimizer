using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JamesOptimizer.Core;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error
}

/// <summary>
/// Thread-safe rolling file logger shared across UI, Core, and Background Service.
/// </summary>
public static class SharedLogger
{
    private static readonly object _sync = new();
    private const long MaxFileBytes = 2 * 1024 * 1024;
    private const int MaxRetainedFiles = 5;

    private static string _logDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        CoreConstants.ProductName, "logs");

    private static string _filePrefix = "app";

    public static string LogDirectory => _logDir;

    public static event Action<LogLevel, string, string>? EntryWritten;

    public static void Configure(string logDirectory, string filePrefix)
    {
        lock (_sync)
        {
            _logDir = logDirectory;
            _filePrefix = string.IsNullOrWhiteSpace(filePrefix) ? "app" : filePrefix;
            EnsureDirectory();
        }
    }

    public static void ConfigureForService()
    {
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        Configure(Path.Combine(programData, CoreConstants.ProductName, "logs"), "service");
    }

    public static void ConfigureForUi()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Configure(Path.Combine(localAppData, CoreConstants.ProductName, "logs"), "winui");
    }

    public static void Log(LogLevel level, string category, string message, Exception? ex = null)
    {
        string line = ex == null
            ? $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level.ToString().ToUpperInvariant()}] [{category}] {message}"
            : $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level.ToString().ToUpperInvariant()}] [{category}] {message} | {ex.GetType().Name}: {ex.Message}";

        lock (_sync)
        {
            try
            {
                EnsureDirectory();
                string activePath = Path.Combine(_logDir, $"{_filePrefix}.log");
                RotateIfNeeded(activePath);
                File.AppendAllText(activePath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Logging must never crash caller
            }
        }

        EntryWritten?.Invoke(level, category, ex == null ? message : $"{message}: {ex.Message}");
    }

    public static void Info(string category, string message) => Log(LogLevel.Info, category, message);
    public static void Warn(string category, string message) => Log(LogLevel.Warn, category, message);
    public static void Error(string category, string message, Exception? ex = null) => Log(LogLevel.Error, category, message, ex);

    private static void EnsureDirectory()
    {
        if (!Directory.Exists(_logDir))
            Directory.CreateDirectory(_logDir);
    }

    private static void RotateIfNeeded(string activePath)
    {
        if (!File.Exists(activePath)) return;
        var fi = new FileInfo(activePath);
        if (fi.Length < MaxFileBytes) return;

        for (int i = MaxRetainedFiles - 1; i >= 1; i--)
        {
            string src = Path.Combine(_logDir, $"{_filePrefix}.{i}.log");
            string dst = Path.Combine(_logDir, $"{_filePrefix}.{i + 1}.log");
            if (File.Exists(dst)) File.Delete(dst);
            if (File.Exists(src)) File.Move(src, dst);
        }

        string firstArchive = Path.Combine(_logDir, $"{_filePrefix}.1.log");
        if (File.Exists(firstArchive)) File.Delete(firstArchive);
        File.Move(activePath, firstArchive);
    }

    /// <summary>
    /// Exports a support bundle (UI logs, Service logs, snapshot manifest, OS environment) without personal user files.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static async Task<string> ExportDiagnosticBundleAsync(string outputZipPath)
    {
        return await Task.Run(() =>
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"jo_diag_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                var env = OsEnvironment.Detect();
                string envReport = new StringBuilder()
                    .AppendLine($"Product: {CoreConstants.ProductName} v{CoreConstants.ProtocolVersion}")
                    .AppendLine($"Timestamp: {DateTimeOffset.Now:O}")
                    .AppendLine($"OS: {env.ProductName} ({env.DisplayVersion}) Build {env.BuildNumber}")
                    .AppendLine($"Edition: {env.EditionId} | Arch: {env.Architecture}")
                    .AppendLine($"Elevated: {env.IsElevated} | Laptop: {env.HasBattery}")
                    .AppendLine($"S3SleepSupported: {env.SupportsS3Sleep} | ModernStandby: {env.SupportsModernStandby}")
                    .ToString();
                File.WriteAllText(Path.Combine(tempDir, "environment.txt"), envReport, Encoding.UTF8);

                string uiLogs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), CoreConstants.ProductName, "logs");
                string svcLogs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), CoreConstants.ProductName, "logs");
                CopyLogFolder(uiLogs, Path.Combine(tempDir, "ui_logs"));
                CopyLogFolder(svcLogs, Path.Combine(tempDir, "service_logs"));

                string backupFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), CoreConstants.ProductName, "backups", "tweak_history.json");
                if (File.Exists(backupFile))
                    File.Copy(backupFile, Path.Combine(tempDir, "tweak_history.json"), overwrite: true);

                string fullZipPath = Path.GetFullPath(outputZipPath);
                if (File.Exists(fullZipPath)) File.Delete(fullZipPath);
                string? zipDir = Path.GetDirectoryName(fullZipPath);
                if (!string.IsNullOrEmpty(zipDir)) Directory.CreateDirectory(zipDir);
                ZipFile.CreateFromDirectory(tempDir, fullZipPath);
                return fullZipPath;
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
        });
    }

    private static void CopyLogFolder(string srcDir, string dstDir)
    {
        if (!Directory.Exists(srcDir)) return;
        Directory.CreateDirectory(dstDir);
        foreach (string file in Directory.EnumerateFiles(srcDir, "*.log").Take(10))
        {
            try { File.Copy(file, Path.Combine(dstDir, Path.GetFileName(file)), overwrite: true); } catch { }
        }
    }
}
