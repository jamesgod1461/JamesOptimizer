using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
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

        private DateTime _lastOptimizationTime = DateTime.MinValue;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("James Optimizer Background Service started.");

            // Start Named Pipe listener for communication with WinUI
            _ = Task.Run(() => ListenToNamedPipeAsync(stoppingToken), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                int intervalMinutes = 0;
                try
                {
                    // Read the AutoTimer value from HKLM so SYSTEM account can access it easily
                    object? regValue = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\JamesOptimizer", "AutoTimer", 0);
                    if (regValue != null)
                    {
                        intervalMinutes = Convert.ToInt32(regValue);
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
                        _lastOptimizationTime = DateTime.Now;
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

        private async Task ListenToNamedPipeAsync(CancellationToken stoppingToken)
        {
            const string pipeName = "JamesOptimizerPipe";
            _logger.LogInformation("Named Pipe server listening on {PipeName}", pipeName);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    NamedPipeServerStream? server = null;
                    try
                    {
                        var pipeSecurity = new System.IO.Pipes.PipeSecurity();
                        var sid = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.AuthenticatedUserSid, null);
                        pipeSecurity.AddAccessRule(new System.IO.Pipes.PipeAccessRule(sid, System.IO.Pipes.PipeAccessRights.ReadWrite, System.Security.AccessControl.AccessControlType.Allow));

                        server = System.IO.Pipes.NamedPipeServerStreamAcl.Create(
                            pipeName,
                            System.IO.Pipes.PipeDirection.InOut,
                            System.IO.Pipes.NamedPipeServerStream.MaxAllowedServerInstances,
                            System.IO.Pipes.PipeTransmissionMode.Byte,
                            System.IO.Pipes.PipeOptions.Asynchronous,
                            0, 0, pipeSecurity);
                    }
                    catch
                    {
                        server = new System.IO.Pipes.NamedPipeServerStream(
                            pipeName,
                            System.IO.Pipes.PipeDirection.InOut,
                            System.IO.Pipes.NamedPipeServerStream.MaxAllowedServerInstances,
                            System.IO.Pipes.PipeTransmissionMode.Byte,
                            System.IO.Pipes.PipeOptions.Asynchronous);
                    }

                    if (server == null) continue;
                    using (server)
                    {
                        await server.WaitForConnectionAsync(stoppingToken);

                        using var reader = new StreamReader(server, System.Text.Encoding.UTF8, leaveOpen: true);
                        using var writer = new StreamWriter(server, System.Text.Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

                        string? request = await reader.ReadLineAsync(stoppingToken);
                        if (!string.IsNullOrEmpty(request))
                        {
                            _logger.LogInformation("Received IPC command: {Request}", request);
                            string response = await ProcessIpcCommandAsync(request, stoppingToken);
                            await writer.WriteLineAsync(response.AsMemory(), stoppingToken);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Named Pipe error occurred.");
                    await Task.Delay(1000, stoppingToken);
                }
            }
        }

        private async Task<string> ProcessIpcCommandAsync(string command, CancellationToken stoppingToken)
        {
            var parts = command.Trim().Split(':', 2);
            var action = parts[0].ToUpperInvariant();
            var arg = parts.Length > 1 ? parts[1] : "";

            switch (action)
            {
                case "PING":
                    return "PONG:JamesOptimizer.Service Active";

                case "STATUS":
                    int timer = 0;
                    try
                    {
                        var val = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\JamesOptimizer", "AutoTimer", 0);
                        if (val != null) timer = Convert.ToInt32(val);
                    }
                    catch { }
                    return $"STATUS:AutoTimer={timer}|LastRun={_lastOptimizationTime:HH:mm:ss}|PID={Environment.ProcessId}";

                case "OPTIMIZE":
                    try
                    {
                        await RunRamFlushAsync(stoppingToken);
                        await RunFastCleanAsync(stoppingToken);
                        _lastOptimizationTime = DateTime.Now;
                        return "SUCCESS:Optimization executed via Background Service.";
                    }
                    catch (Exception ex)
                    {
                        return $"ERROR:{ex.Message}";
                    }

                case "SET_TIMER":
                    if (int.TryParse(arg, out int minutes))
                    {
                        try
                        {
                            using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\JamesOptimizer", true);
                            key?.SetValue("AutoTimer", minutes, RegistryValueKind.DWord);
                            return $"SUCCESS:AutoTimer updated to {minutes}m.";
                        }
                        catch (Exception ex)
                        {
                            return $"ERROR:Registry write failed: {ex.Message}";
                        }
                    }
                    return "ERROR:Invalid minutes argument.";

                default:
                    return "ERROR:Unknown command.";
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
