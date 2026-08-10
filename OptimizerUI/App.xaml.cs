using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.IO.Pipes;
using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;

[assembly: SupportedOSPlatform("windows")]

namespace JamesOptimizer;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private static Mutex? _mutex;
    private const string MutexName = "Global\\JamesOptimizerMutex";
    private const string PipeName = "JamesOptimizerPipe";

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_RESTORE = 9;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // --- GLOBAL EXCEPTION HANDLING (Crash-Proof & User-Friendly) ---
        this.DispatcherUnhandledException += (s, args) =>
        {
            args.Handled = true;
            ShowErrorDialog("An unexpected UI error occurred.", args.Exception);
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                ShowErrorDialog("A critical application error occurred.", ex);
            }
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            args.SetObserved();
            ShowErrorDialog("A background task error occurred.", args.Exception);
        };
        // -------------------------------------------------------------

        bool createdNew;
        _mutex = new Mutex(true, MutexName, out createdNew);

        if (!createdNew)
        {
            // Another instance is running. Tell it to show itself, then exit.
            try
            {
                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    client.Connect(1000); // 1 second timeout
                    using (var writer = new StreamWriter(client))
                    {
                        writer.WriteLine("SHOW");
                        writer.Flush();
                    }
                }
            }
            catch 
            { 
                // If IPC fails, fallback to trying to find the window
                Process current = Process.GetCurrentProcess();
                foreach (Process process in Process.GetProcessesByName(current.ProcessName))
                {
                    if (process.Id != current.Id && process.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(process.MainWindowHandle, SW_RESTORE);
                        SetForegroundWindow(process.MainWindowHandle);
                        break;
                    }
                }
            }
            
            System.Windows.Application.Current.Shutdown();
            return;
        }

        bool isAdmin = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

        if (!isAdmin)
        {
            var processInfo = new System.Diagnostics.ProcessStartInfo(System.Environment.ProcessPath ?? "James Optimizer.exe")
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            if (e.Args.Contains("-background"))
            {
                processInfo.Arguments = "-background";
            }
            try
            {
                System.Diagnostics.Process.Start(processInfo);
            }
            catch { }
            System.Windows.Application.Current.Shutdown();
            return;
        }

        var mainWindow = new MainWindow();
        
        // Start listening for other instances
        Task.Run(() => StartIpcServer(mainWindow));

        if (!e.Args.Contains("-background"))
        {
            mainWindow.Show();
        }
    }

    private void StartIpcServer(Window mainWindow)
    {
        while (true)
        {
            try
            {
                using (var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous))
                {
                    server.WaitForConnection();
                    using (var reader = new StreamReader(server))
                    {
                        string? message = reader.ReadLine();
                        if (message == "SHOW")
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                mainWindow.Show();
                                if (mainWindow.WindowState == WindowState.Minimized)
                                    mainWindow.WindowState = WindowState.Normal;
                                mainWindow.Activate();
                                mainWindow.Topmost = true;
                                mainWindow.Topmost = false;
                                mainWindow.Focus();
                            });
                        }
                    }
                }
            }
            catch { }
        }
    }

    private void ShowErrorDialog(string message, Exception ex)
    {
        // Ignore task cancellations which are normal during async operations
        if (ex is TaskCanceledException || ex is OperationCanceledException) return;

        Application.Current.Dispatcher.Invoke(() =>
        {
            MessageBox.Show(
                $"{message}\n\nError Details:\n{ex.Message}\n\nWe apologize for the inconvenience. The application will attempt to continue running.", 
                "James Optimizer - Error", 
                MessageBoxButton.OK, 
                MessageBoxImage.Warning);
        });
    }
}
