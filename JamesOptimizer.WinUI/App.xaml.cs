using System;
using System.IO;
using Microsoft.UI.Xaml;
using JamesOptimizer.WinUI.ViewModels;

namespace JamesOptimizer.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;
    public static DashboardViewModel ViewModel { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        RequestedTheme = ApplicationTheme.Dark;

        UnhandledException += (sender, e) =>
        {
            LogCrash("WinUI UnhandledException", e.Exception, e.Message);
        };

        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogCrash("AppDomain UnhandledException", ex, ex.Message);
            }
        };
    }

    private static void LogCrash(string source, Exception? ex, string message)
    {
        try
        {
            string logPath = Path.Combine(AppContext.BaseDirectory, "crash.log");
            string content = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {message}\nException: {ex?.ToString()}\nInner: {ex?.InnerException?.ToString()}\n----------------------------------------\n";
            File.AppendAllText(logPath, content);
        }
        catch { }
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
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
            LogCrash("OnLaunched Exception", ex, ex.Message);
            throw;
        }
    }
}
