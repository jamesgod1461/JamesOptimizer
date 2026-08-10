using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace JamesOptimizer.Service
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;

        [DllImport("psapi.dll")]
        static extern int EmptyWorkingSet(IntPtr hwProc);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr hObject);

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("James Optimizer Background Service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                int intervalMinutes = 0;
                try
                {
                    // Read the AutoTimer value from HKLM so SYSTEM account can access it easily
                    object? regValue = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\JamesOptimizer", "AutoTimer", 0);
                    if (regValue != null)
                    {
                        intervalMinutes = (int)regValue;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error reading registry for AutoTimer.");
                }

                if (intervalMinutes > 0)
                {
                    _logger.LogInformation($"AutoTimer is set to {intervalMinutes} minutes. Running optimization.");
                    
                    try 
                    {
                        await RunRamFlushAsync(stoppingToken);
                        await RunFastCleanAsync(stoppingToken);
                        _logger.LogInformation("Optimization cycle completed successfully.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error during optimization cycle.");
                    }

                    // Wait for the interval before checking and running again
                    await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
                }
                else
                {
                    // If disabled, check again in 1 minute
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
        }

        private async Task RunRamFlushAsync(CancellationToken cancellationToken)
        {
            await Task.Run(() => 
            {
                var processes = Process.GetProcesses();
                foreach (var process in processes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        EmptyWorkingSet(process.Handle);
                    }
                    catch (Exception ex) 
                    { 
                        _logger.LogDebug(ex, "EmptyWorkingSet failed for process {ProcessName}", process.ProcessName); 
                    }
                }
            }, cancellationToken);
        }

        private async Task RunFastCleanAsync(CancellationToken cancellationToken)
        {
            await Task.Run(() => CleanPath(Path.GetTempPath(), cancellationToken), cancellationToken);
            await Task.Run(() => CleanPath(@"C:\Windows\Temp", cancellationToken), cancellationToken);
            await Task.Run(() => CleanPath(@"C:\Windows\Prefetch", cancellationToken), cancellationToken);
        }

        private void CleanPath(string path, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(path)) return;
            try
            {
                var dir = new DirectoryInfo(path);
                foreach (var file in dir.GetFiles()) 
                { 
                    cancellationToken.ThrowIfCancellationRequested();
                    try { file.Delete(); } 
                    catch (Exception ex) { _logger.LogDebug(ex, "Failed to delete file {FilePath}", file.FullName); } 
                }
                foreach (var subdir in dir.GetDirectories()) 
                { 
                    cancellationToken.ThrowIfCancellationRequested();
                    try { subdir.Delete(true); } 
                    catch (Exception ex) { _logger.LogDebug(ex, "Failed to delete directory {DirPath}", subdir.FullName); } 
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "CleanPath failed for {Path}", path); }
        }
    }
}
