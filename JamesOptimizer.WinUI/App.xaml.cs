using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using JamesOptimizer.Core;
using JamesOptimizer.WinUI.ViewModels;
using Microsoft.UI.Xaml;

namespace JamesOptimizer.WinUI;

public partial class App : Application
{
    private Window? _window;
    public static DashboardViewModel ViewModel { get; private set; } = null!;
    public static string? StartupFileLockTarget { get; set; }

    public App()
    {
        SharedLogger.ConfigureForUi();

        if (TryHandleHeadlessCli(Environment.GetCommandLineArgs(), out int exitCode))
        {
            Environment.Exit(exitCode);
            return;
        }

        InitializeComponent();
        RequestedTheme = ApplicationTheme.Dark;

        UnhandledException += (sender, e) =>
        {
            LogCrash("WinUI UnhandledException", e.Exception, e.Message, showUserDialog: true);
        };

        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                LogCrash("AppDomain UnhandledException", ex, ex.Message, showUserDialog: true);
        };

        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            LogCrash("TaskScheduler UnobservedTaskException", e.Exception, e.Exception.Message, showUserDialog: false);
            e.SetObserved();
        };
    }

    private static bool TryHandleHeadlessCli(string[] rawArgs, out int exitCode)
    {
        exitCode = 0;
        var args = rawArgs.Skip(1).ToArray();
        if (args.Length == 0) return false;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--file-lock", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                StartupFileLockTarget = args[i + 1];
                return false; // Launch UI directly to File Lock inspector
            }
        }

        if (args.Any(a => a.Equals("--revert-all", StringComparison.OrdinalIgnoreCase)))
        {
            var (ok, fail, errors) = TweakBackupService.RevertAllSnapshots();
            SharedLogger.Info("CLI", $"--revert-all completed: {ok} reverted, {fail} failed.");
            if (fail > 0) SharedLogger.Warn("CLI", string.Join("; ", errors));
            exitCode = fail == 0 ? 0 : 2;
            return true;
        }

        if (args.Any(a => a.Equals("--revert-last", StringComparison.OrdinalIgnoreCase)))
        {
            var latest = TweakBackupService.GetHistory().FirstOrDefault(s => !s.IsReverted);
            if (latest == null)
            {
                SharedLogger.Info("CLI", "--revert-last: nothing to revert.");
                exitCode = 0;
                return true;
            }
            bool ok = TweakBackupService.RevertSnapshot(latest.Id, out string msg);
            SharedLogger.Info("CLI", $"--revert-last: {msg}");
            exitCode = ok ? 0 : 2;
            return true;
        }

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--export", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                string outPath = args[i + 1];
                var profile = ProfileConfigService.ExportCurrentProfile();
                ProfileConfigService.SaveProfileToFile(outPath, profile);
                SharedLogger.Info("CLI", $"Exported profile ({profile.EnabledTweakIds.Count} tweaks) to '{outPath}'.");
                exitCode = 0;
                return true;
            }

            if (args[i].Equals("--apply", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                string path = args[i + 1];
                bool dryRun = args.Any(a => a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));
                if (!File.Exists(path))
                {
                    SharedLogger.Error("CLI", $"Profile file not found: {path}");
                    exitCode = 3;
                    return true;
                }

                var preview = ProfileConfigService.PreviewImport(File.ReadAllText(path));
                if (!preview.IsValid)
                {
                    SharedLogger.Error("CLI", $"Profile validation failed: {preview.ErrorMessage}");
                    exitCode = 4;
                    return true;
                }

                if (dryRun)
                {
                    SharedLogger.Info("CLI", $"--dry-run: {preview.ToApply.Count} to apply, {preview.AlreadyApplied.Count} already applied, {preview.SkippedUnknownOrIncompatible.Count} skipped.");
                    exitCode = 0;
                    return true;
                }

                var (applied, failed, messages) = ProfileConfigService.ApplyPreviewedProfile(preview);
                SharedLogger.Info("CLI", $"--apply completed: {applied} applied, {failed} failed. {string.Join(" | ", messages)}");
                exitCode = failed == 0 ? 0 : 5;
                return true;
            }
        }

        return false;
    }

    private static void LogCrash(string source, Exception? ex, string message, bool showUserDialog = false)
    {
        SharedLogger.Error(source, message, ex);
        string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), CoreConstants.ProductName, "logs");
        try
        {
            Directory.CreateDirectory(logDir);
            string logPath = Path.Combine(logDir, "winui_crash.log");
            if (File.Exists(logPath) && new FileInfo(logPath).Length > 2 * 1024 * 1024)
            {
                string rotated = Path.Combine(logDir, "winui_crash.1.log");
                if (File.Exists(rotated)) File.Delete(rotated);
                File.Move(logPath, rotated);
            }
            string content = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {message}\nException: {ex?.ToString()}\nInner: {ex?.InnerException?.ToString()}\n----------------------------------------\n";
            File.AppendAllText(logPath, content);
        }
        catch { }

        if (showUserDialog)
        {
            try
            {
                string summary = $"{CoreConstants.ProductName} encountered an unexpected error and wrote diagnostics to:\n{logDir}\n\nDetails: {ex?.Message ?? message}";
                MessageBoxW(IntPtr.Zero, summary, $"{CoreConstants.ProductName} — Error", 0x00000010 /* MB_ICONERROR */);
            }
            catch { }
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            ViewModel = new DashboardViewModel();
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception ex)
        {
            LogCrash("OnLaunched Exception", ex, ex.Message, showUserDialog: true);
            throw;
        }
    }
}
