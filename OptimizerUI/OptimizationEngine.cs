using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using IOEnumerationOptions = System.IO.EnumerationOptions;

namespace JamesOptimizer
{
    public static class OptimizationEngine
    {
        public static Action<int, string>? OnProgress;

        private const string UltimatePowerPlanGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";
        private const uint NetworkThrottlingIndexMax = 0xFFFFFFFF;
        private const int SystemMemoryListInformation = 80;
        private const int MemoryFlushModifiedList = 3;
        private const int MemoryPurgeStandbyList = 4;
        private const int MemoryPurgeLowPriorityStandbyList = 5;
        private const uint TokenAdjustPrivileges = 0x0020;
        private const uint TokenQuery = 0x0008;
        private const uint SePrivilegeEnabled = 2;

        internal static void ReportProgress(int percent, string message)
        {
            OnProgress?.Invoke(percent, message);
        }

        private static async Task RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken = default)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };

            try
            {
                process.Start();

                using (cancellationToken.Register(() =>
                {
                    try { if (!process.HasExited) process.Kill(); } catch (Exception ex) { HardwareLogger.LogError("Process kill on cancel failed", ex); }
                }))
                {
                    await process.WaitForExitAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(); } catch (Exception ex) { HardwareLogger.LogError("Process kill on cancel failed", ex); }
                throw;
            }
            catch (Exception ex)
            {
                HardwareLogger.LogError($"RunProcessAsync failed: {fileName} {arguments}", ex);
                throw;
            }
            finally
            {
                process.Dispose();
            }
        }

        private static async Task ExecuteRegTweakAsync(string regContent, string startMsg, string endMsg, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrEmpty(startMsg)) ReportProgress(0, startMsg);
            if (string.IsNullOrEmpty(regContent)) return;

            string regPath = Path.Combine(Path.GetTempPath(), $"temp_opt_{Guid.NewGuid():N}.reg");

            try
            {
                if (!string.IsNullOrEmpty(startMsg)) ReportProgress(30, "Writing Registry File...");
                await File.WriteAllTextAsync(regPath, regContent, cancellationToken);
                await RunProcessAsync("regedit.exe", $"/s \"{regPath}\"", cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                HardwareLogger.LogError("ExecuteRegTweakAsync failed", ex);
                throw;
            }
            finally
            {
                try { if (File.Exists(regPath)) File.Delete(regPath); } catch (Exception ex) { HardwareLogger.LogError("Cleanup reg file failed", ex); }
            }

            if (!string.IsNullOrEmpty(endMsg)) ReportProgress(100, endMsg);
        }

        private static async Task ExecuteBatTweakAsync(string batContent, string startMsg, string endMsg, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrEmpty(startMsg)) ReportProgress(0, startMsg);
            if (string.IsNullOrEmpty(batContent)) return;

            string batPath = Path.Combine(Path.GetTempPath(), $"temp_opt_{Guid.NewGuid():N}.bat");

            try
            {
                if (!string.IsNullOrEmpty(startMsg)) ReportProgress(30, "Writing Batch File...");
                await File.WriteAllTextAsync(batPath, batContent, cancellationToken);
                await RunProcessAsync("cmd.exe", $"/c \"{batPath}\"", cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                HardwareLogger.LogError("ExecuteBatTweakAsync failed", ex);
                throw;
            }
            finally
            {
                try { if (File.Exists(batPath)) File.Delete(batPath); } catch (Exception ex) { HardwareLogger.LogError("Cleanup bat file failed", ex); }
            }

            if (!string.IsNullOrEmpty(endMsg)) ReportProgress(100, endMsg);
        }

        public static async Task RunWindowsDebloatAsync(CancellationToken cancellationToken = default)
        {
            bool isWin11 = Environment.OSVersion.Version.Build >= 22000;
            string osName = isWin11 ? "Windows 11" : "Windows 10";

            ReportProgress(0, $"Preparing Interactive {osName} Debloater...");

            string resourceName = isWin11
                ? "JamesOptimizer.Resources.Windows11Debloater.ps1"
                : "JamesOptimizer.Resources.Windows 10 Debloater.ps1";
            string script = string.Empty;
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream(resourceName) ?? assembly.GetManifestResourceStream(resourceName.Replace(" ", "_")))
            {
                if (stream == null) throw new FileNotFoundException($"Could not find embedded resource: {resourceName}");
                using var reader = new StreamReader(stream);
                script = (await reader.ReadToEndAsync()).Replace('\u00A0', ' ');
            }

            string tempScript = Path.Combine(Path.GetTempPath(), $"optimizer_task_{Guid.NewGuid():N}.ps1");
            await File.WriteAllTextAsync(tempScript, script, new UTF8Encoding(true), cancellationToken);
            File.SetAttributes(tempScript, FileAttributes.Hidden | FileAttributes.Temporary);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoExit -NoProfile -ExecutionPolicy Bypass -Sta -File \"{tempScript}\"",
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Normal
                },
                EnableRaisingEvents = true
            };

            try
            {
                ReportProgress(30, "Waiting for Interactive Debloater window...");
                process.Start();
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(); } catch (Exception ex) { HardwareLogger.LogError("Process kill on cancel failed", ex); }
                throw;
            }
            finally
            {
                process.Dispose();
                try { File.SetAttributes(tempScript, FileAttributes.Normal); } catch (Exception ex) { HardwareLogger.LogError("SetAttributes failed", ex); }
                try { File.Delete(tempScript); } catch (Exception ex) { HardwareLogger.LogError("Delete temp script failed", ex); }
            }

            ReportProgress(100, "Interactive Debloater Finished.");
        }

        public static async Task RunGamingBoostAsync(CancellationToken cancellationToken = default)
        {
            ReportProgress(0, "Unlocking Ultimate Power Plan...");
            await RunProcessAsync("powercfg", "-duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61", cancellationToken);
            
            ReportProgress(30, "Activating Ultimate Performance...");
            await RunProcessAsync("powercfg", "/setactive e9a42b02-d5df-448d-aa00-03f14749eb61", cancellationToken);
            
            ReportProgress(60, "Disabling Hibernation...");
            await RunProcessAsync("powercfg", "-h off", cancellationToken);
            
            ReportProgress(80, "Applying Network Latency Fixes...");
            await RunProcessAsync("netsh", "int tcp set global autotuninglevel=normal", cancellationToken);
            
            ReportProgress(100, "GAMING MODE ENABLED");
        }

        public static async Task RunEliteTweaksAsync(CancellationToken cancellationToken = default)
        {
            ReportProgress(0, "Optimizing Boot Configuration (BCD)...");
            await RunProcessAsync("bcdedit", "/set useplatformclock No", cancellationToken);
            await RunProcessAsync("bcdedit", "/set disabledynamictick Yes", cancellationToken);
            await RunProcessAsync("bcdedit", "/set tscsyncpolicy Enhanced", cancellationToken);

            ReportProgress(33, "Optimizing SSD Performance (fsutil)...");
            await RunProcessAsync("fsutil", "behavior set disable8dot3 1", cancellationToken);
            await RunProcessAsync("fsutil", "behavior set disablelastaccess 1", cancellationToken);

            ReportProgress(66, "Disabling Memory Compression & Optimizing Network...");
            await RunProcessAsync("powershell.exe", "-Command \"Disable-MMAgent -MemoryCompression\"", cancellationToken);
            await RunProcessAsync("netsh", "int tcp set global rss=enabled", cancellationToken);

            ReportProgress(100, "Elite System Optimization Applied!");
        }

        public static async Task RunRegTweaksAsync(CancellationToken cancellationToken = default)
        {
            await Task.Delay(300, cancellationToken);

            string regContent = @"Windows Registry Editor Version 5.00
[HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile]
""NetworkThrottlingIndex""=dword:ffffffff
""SystemResponsiveness""=dword:00000000

[HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games]
""Affinity""=dword:00000000
""Background Only""=""False""
""Clock Rate""=dword:00002710
""GPU Priority""=dword:00000008
""Priority""=dword:00000006
""Scheduling Category""=""High""
""SFIO Priority""=""High""

[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\PriorityControl]
""Win32PrioritySeparation""=dword:00000026

[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\DiagTrack]
""Start""=dword:00000004
[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\WerSvc]
""Start""=dword:00000004
[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\WSearch]
""Start""=dword:00000004
[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\SysMain]
""Start""=dword:00000004
[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\MapsBroker]
""Start""=dword:00000004
";

            await ExecuteRegTweakAsync(regContent, "Loading Registry Data...", "Registry Optimized for Gaming!", cancellationToken);
        }

        public static async Task RunVisualOptimizeAsync(CancellationToken cancellationToken = default)
        {
            string regContent = @"Windows Registry Editor Version 5.00
[HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize]
""EnableTransparency""=dword:00000000
[HKEY_CURRENT_USER\Control Panel\Desktop]
""UserPreferencesMask""=hex:90,12,03,80,10,00,00,00
""DragFullWindows""=""0""
""FontSmoothing""=""2""
""MinAnimate""=""0""
""SmoothScroll""=dword:00000000
[HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced]
""TaskbarAnimations""=dword:00000000
""TaskbarSizeMove""=dword:00000000
""ListviewAlphaSelect""=dword:00000000
""ListviewShadow""=dword:00000000
[HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM]
""EnableAeroPeek""=dword:00000000
""AlwaysHibernateThumbnails""=dword:00000000
""Composition""=dword:00000001
""ColorizationGlassAttribute""=dword:00000000
";
            await ExecuteRegTweakAsync(regContent, "Preparing Visual Tweaks...", "Visual Registry Tweaks Applied!", cancellationToken);

            ReportProgress(80, "Hard Disabling Animations...");
            await RunProcessAsync("reg.exe", @"add ""HKCU\Control Panel\Desktop\WindowMetrics"" /v MinAnimate /t REG_SZ /d 0 /f", cancellationToken);
            
            ReportProgress(100, "Visual Effects Removed!");
        }

        public static async Task RunMouseFixAsync(CancellationToken cancellationToken = default)
        {
            string regContent = @"Windows Registry Editor Version 5.00
[HKEY_CURRENT_USER\Control Panel\Mouse]
""MouseSensitivity""=""10""
""MouseSpeed""=""0""
""MouseThreshold1""=""0""
""MouseThreshold2""=""0""
";
            await ExecuteRegTweakAsync(regContent, "Applying Raw Mouse Input Fix...", "Mouse Acceleration Removed.", cancellationToken);
        }

        public static async Task RunKeyboardFixAsync(CancellationToken cancellationToken = default)
        {
            string regContent = @"Windows Registry Editor Version 5.00
[HKEY_CURRENT_USER\Control Panel\Keyboard]
""KeyboardDelay""=""0""
""KeyboardSpeed""=""31""
";
            await ExecuteRegTweakAsync(regContent, "Applying Keyboard Latency Fix...", "Keyboard Response Optimized.", cancellationToken);
        }

        public static async Task RunGpuTweaksAsync(CancellationToken cancellationToken = default)
        {
            ReportProgress(0, "Detecting GPU Vendor...");
            bool isAmd = false, isNvidia = false, isIntel = false;

            try
            {
                using var searcher = new ManagementObjectSearcher("select Name from Win32_VideoController");
                using var collection = searcher.Get();
                foreach (ManagementObject obj in collection)
                {
                    string name = obj["Name"]?.ToString() ?? "";
                    if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
                        isAmd = true;
                    else if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || name.Contains("GeForce", StringComparison.OrdinalIgnoreCase))
                        isNvidia = true;
                    else if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase) || name.Contains("HD Graphics", StringComparison.OrdinalIgnoreCase))
                        isIntel = true;
                }
            }
            catch (Exception ex) { HardwareLogger.LogError("GPU vendor detection failed", ex); }

            if (isAmd)
            {
                string regContent = @"Windows Registry Editor Version 5.00
[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000]
""EnableUlps""=dword:00000000
[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0001]
""EnableUlps""=dword:00000000
";
                await ExecuteRegTweakAsync(regContent, "AMD GPU Detected. Disabling ULPS...", "AMD Maximum Performance Applied (ULPS Disabled).", cancellationToken);
            }
            if (isNvidia)
            {
                string regContent = @"Windows Registry Editor Version 5.00
[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NVTweak]
""DisplayPowerSaving""=dword:00000000
";
                await ExecuteRegTweakAsync(regContent, "NVIDIA GPU Detected. Optimizing Driver Power State...", "NVIDIA Maximum Performance Applied.", cancellationToken);
            }
            if (isIntel || (!isAmd && !isNvidia))
            {
                ReportProgress(100, "Intel/Other GPU Detected. Base optimizations applied.");
                return;
            }

            ReportProgress(80, "Configuring Virtual Memory...");
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                long totalRamBytes = 0;
                using (var cs = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
                using (var collection = cs.Get())
                {
                    foreach (ManagementObject obj in collection)
                        totalRamBytes = Convert.ToInt64(obj["TotalPhysicalMemory"]);
                }

                long maxMb = totalRamBytes / (1024 * 1024);
                long initMb = maxMb / 2;

                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", writable: true);
                if (key != null)
                {
                    key.SetValue("AutomaticManagedPagefile", 0, RegistryValueKind.DWord);
                    key.SetValue("PagingFiles", $@"C:\pagefile.sys {initMb} {maxMb}", RegistryValueKind.MultiString);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { HardwareLogger.LogError("Virtual memory configuration failed", ex); }

            ReportProgress(100, "GPU Max Performance + Virtual Memory configured! Reboot to apply pagefile.");
        }

        private static void CleanPath(string path)
        {
            if (!Directory.Exists(path)) return;
            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { File.Delete(file); } catch (Exception ex) { HardwareLogger.LogError($"Delete file failed: {file}", ex); }
                }
                foreach (var dir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories).Reverse())
                {
                    try { Directory.Delete(dir, true); } catch (Exception ex) { HardwareLogger.LogError($"Delete dir failed: {dir}", ex); }
                }
            }
            catch (Exception ex) { HardwareLogger.LogError($"CleanPath failed: {path}", ex); }
        }

        public static async Task RunFastCleanAsync(CancellationToken cancellationToken = default)
        {
            ReportProgress(0, "Scanning Temp Folders...");
            CleanPath(Path.GetTempPath());
            cancellationToken.ThrowIfCancellationRequested();

            ReportProgress(25, "Cleaning Windows Temp...");
            CleanPath(@"C:\Windows\Temp");
            cancellationToken.ThrowIfCancellationRequested();

            ReportProgress(50, "Cleaning Prefetch...");
            CleanPath(@"C:\Windows\Prefetch");
            cancellationToken.ThrowIfCancellationRequested();

            ReportProgress(75, "Cleaning Browser Cache...");
            string localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";
            if (!string.IsNullOrEmpty(localAppData))
            {
                CleanPath(Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Cache"));
                CleanPath(Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Cache"));
                CleanPath(Path.Combine(localAppData, @"BraveSoftware\Brave-Browser\User Data\Default\Cache"));
            }
            
            ReportProgress(100, "Junk Files Removed!");
        }

        public static async Task RunShaderClearAsync(CancellationToken cancellationToken = default)
        {
            ReportProgress(0, "Clearing DirectX Shader Cache...");
            string? localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (!string.IsNullOrEmpty(localAppData))
            {
                CleanPath(Path.Combine(localAppData, "D3DSCache"));
                CleanPath(Path.Combine(localAppData, @"NVIDIA\GLCache"));
                CleanPath(Path.Combine(localAppData, @"AMD\DxCache"));
            }
            await RunProcessAsync("cleanmgr", "/sagerun:64", cancellationToken);
            ReportProgress(100, "Shader Cache Cleared!");
        }

        public class DeepCleanupCategory
        {
            public string Id { get; set; } = "";
            public string Title { get; set; } = "";
            public string Description { get; set; } = "";
            public long SizeBytes { get; set; }
            public string DisplaySize => MainWindow.FormatBytes(SizeBytes);
            public bool DefaultSelected { get; set; }
            public List<string> TargetPaths { get; set; } = new();
        }

        private static long GetDirectorySize(string folderPath)
        {
            if (!Directory.Exists(folderPath)) return 0;
            long size = 0;
            try
            {
                foreach (var file in Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories))
                {
                    try { size += new FileInfo(file).Length; } catch (Exception ex) { HardwareLogger.LogError($"GetFileSize failed: {file}", ex); }
                }
            }
            catch (Exception ex) { HardwareLogger.LogError($"GetDirectorySize failed: {folderPath}", ex); }
            return size;
        }

        public static async Task<List<DeepCleanupCategory>> ScanDeepCleanupAsync()
        {
            var results = new List<DeepCleanupCategory>();
            string? localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var downloadsPath = Path.Combine(userProfile, "Downloads");
            results.Add(new DeepCleanupCategory {
                Id = "Downloads",
                Title = "Downloads",
                Description = "Warning: These are files in your personal Downloads folder. Select this if you'd like to delete everything. This does not respect your Storage Sense configuration.",
                SizeBytes = GetDirectorySize(downloadsPath),
                DefaultSelected = false,
                TargetPaths = new List<string> { downloadsPath }
            });

            var thumbPath = localAppData != null ? Path.Combine(localAppData, @"Microsoft\Windows\Explorer") : "";
            results.Add(new DeepCleanupCategory {
                Id = "Thumbnails",
                Title = "Thumbnails",
                Description = "Windows keeps a copy of all your picture, video, and document thumbnails so they can be displayed quickly when you open a folder. If you delete these thumbnails, they will be automatically recreated as needed.",
                SizeBytes = string.IsNullOrEmpty(thumbPath) ? 0 : GetDirectorySize(thumbPath),
                DefaultSelected = true,
                TargetPaths = string.IsNullOrEmpty(thumbPath) ? new List<string>() : new List<string> { thumbPath }
            });
            
            var deliveryOpt = @"C:\Windows\SoftwareDistribution\Download";
            results.Add(new DeepCleanupCategory {
                Id = "DeliveryOptimization",
                Title = "Delivery Optimization Files",
                Description = "Delivery Optimization is used to download updates from Microsoft. These files are stored in a dedicated cache to be uploaded to other devices on your local network (if your settings allow such use). You may safely delete these files if you need the space.",
                SizeBytes = GetDirectorySize(deliveryOpt),
                DefaultSelected = true,
                TargetPaths = new List<string> { deliveryOpt }
            });
            
            var dxCachePaths = new List<string>();
            if (localAppData != null)
            {
                dxCachePaths.Add(Path.Combine(localAppData, "D3DSCache"));
                dxCachePaths.Add(Path.Combine(localAppData, @"NVIDIA\GLCache"));
                dxCachePaths.Add(Path.Combine(localAppData, @"AMD\DxCache"));
            }
            long dxSize = dxCachePaths.Sum(GetDirectorySize);
            results.Add(new DeepCleanupCategory {
                Id = "DirectXShaderCache",
                Title = "DirectX Shader Cache",
                Description = "Clean up files created by the graphics system which can speed up application load time and improve responsiveness. They will be re-generated as needed.",
                SizeBytes = dxSize,
                DefaultSelected = true,
                TargetPaths = dxCachePaths
            });
            
            var inetCache = localAppData != null ? Path.Combine(localAppData, @"Microsoft\Windows\INetCache") : "";
            results.Add(new DeepCleanupCategory {
                Id = "TemporaryInternetFiles",
                Title = "Temporary Internet Files",
                Description = "The Temporary Internet Files folder contains webpages stored on your hard disk for quick viewing. Your personalized settings for webpages will be left intact.",
                SizeBytes = string.IsNullOrEmpty(inetCache) ? 0 : GetDirectorySize(inetCache),
                DefaultSelected = true,
                TargetPaths = string.IsNullOrEmpty(inetCache) ? new List<string>() : new List<string> { inetCache }
            });
            
            var errPath = @"C:\ProgramData\Microsoft\Windows\WER";
            results.Add(new DeepCleanupCategory {
                Id = "WindowsErrorReports",
                Title = "Windows error reports and feedback diagnostics",
                Description = "Diagnostics files generated from Windows errors and user feedback.",
                SizeBytes = GetDirectorySize(errPath),
                DefaultSelected = true,
                TargetPaths = new List<string> { errPath }
            });
            
            return results;
        }

        public static async Task ExecuteDeepCleanupAsync(List<DeepCleanupCategory> categoriesToClean)
        {
            int count = 0;
            int total = categoriesToClean.Count;
            if (total == 0) return;

            foreach (var cat in categoriesToClean)
            {
                ReportProgress((int)(((double)count / total) * 100), $"Cleaning {cat.Title}...");
                foreach (var path in cat.TargetPaths)
                {
                    CleanPath(path);
                }
                count++;
            }
            ReportProgress(100, "Deep Cleanup Finished!");
        }


        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID_AND_ATTRIBUTES
        {
            public LUID Luid;
            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
            public LUID_AND_ATTRIBUTES[] Privileges;
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

        [DllImport("ntdll.dll")]
        private static extern int NtSetSystemInformation(int SystemInformationClass, IntPtr SystemInformation, int SystemInformationLength);

        private static void ClearMemoryLists()
        {
            if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TokenAdjustPrivileges | TokenQuery, out IntPtr tokenHandle))
                return;

            try
            {
                if (!LookupPrivilegeValue(null, "SeProfileSingleProcessPrivilege", out LUID luid))
                    return;

                var tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Privileges = new LUID_AND_ATTRIBUTES[1]
                };
                tp.Privileges[0].Luid = luid;
                tp.Privileges[0].Attributes = SePrivilegeEnabled;

                AdjustTokenPrivileges(tokenHandle, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);

                int[] memoryCommands = { MemoryFlushModifiedList, MemoryPurgeStandbyList, MemoryPurgeLowPriorityStandbyList };
                foreach (int command in memoryCommands)
                {
                    IntPtr ptr = Marshal.AllocHGlobal(sizeof(int));
                    try
                    {
                        Marshal.WriteInt32(ptr, command);
                        NtSetSystemInformation(SystemMemoryListInformation, ptr, sizeof(int));
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(ptr);
                    }
                }
            }
            finally
            {
                CloseHandle(tokenHandle);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("psapi.dll")]
        private static extern int EmptyWorkingSet(IntPtr hwProc);

        public static async Task RunRamFlushAsync(CancellationToken cancellationToken = default)
        {
            ReportProgress(0, "Flushing System RAM...");

            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReportProgress(20, "Releasing process working sets...");
                var processes = Process.GetProcesses();
                try
                {
                    foreach (var process in processes)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try { EmptyWorkingSet(process.Handle); }
                        catch (Exception ex) { HardwareLogger.LogError("EmptyWorkingSet failed", ex); }
                    }
                }
                finally
                {
                    foreach (var p in processes) p.Dispose();
                }

                ReportProgress(60, "Flushing Modified and Standby Memory Lists...");
                cancellationToken.ThrowIfCancellationRequested();
                ClearMemoryLists();

                ReportProgress(80, "Running Garbage Collection...");
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }, cancellationToken);

            ReportProgress(100, "RAM Purged Successfully!");
        }

        public static async Task RunNetworkFlushAsync(CancellationToken cancellationToken = default)
        {
            ReportProgress(0, "Preparing Network Optimization...");
            
            string batContent = @"@echo off
net session >nul 2>&1
if %errorLevel% neq 0 exit /b 1
  
set BACKUP_DIR=%~dp0network_backup
if not exist ""%BACKUP_DIR%"" mkdir ""%BACKUP_DIR%"" >nul 2>&1
reg export ""HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters"" ""%BACKUP_DIR%\Tcpip_Parameters_backup.reg"" /y >nul 2>&1
reg export ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"" ""%BACKUP_DIR%\SystemProfile_backup.reg"" /y >nul 2>&1
  
netsh interface tcp set global autotuning=restricted >nul 2>&1
netsh int tcp set global autotuninglevel=normal >nul 2>&1
netsh int tcp set global chimney=enabled >nul 2>&1
netsh int tcp set global dca=enabled >nul 2>&1
netsh int tcp set global netdma=disabled >nul 2>&1
netsh int tcp set global congestionprovider=ctcp >nul 2>&1
netsh int tcp set global ecncapability=disabled >nul 2>&1
netsh int tcp set heuristics disabled >nul 2>&1
netsh int tcp set global rss=enabled >nul 2>&1
netsh int tcp set global fastopen=enabled >nul 2>&1
netsh int tcp set global nonsackrttresiliency=disabled >nul 2>&1
netsh int tcp set global rsc=enabled >nul 2>&1
  
reg add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"" /v NetworkThrottlingIndex /t REG_DWORD /d 4294967295 /f >nul 2>&1
  
for /f ""tokens=1"" %%K in ('reg query ""HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces"" ^^^| findstr ""HKEY""') do (
    reg add ""%%K"" /v TcpAckFrequency /t REG_DWORD /d 1 /f >nul 2>&1
    reg add ""%%K"" /v TCPNoDelay /t REG_DWORD /d 1 /f >nul 2>&1
)
  
netsh winsock reset >nul 2>&1
netsh int ip reset >nul 2>&1
  
ipconfig /flushdns >nul 2>&1
ipconfig /registerdns >nul 2>&1
  
for /f ""tokens=3 delims= "" %%A in ('netsh interface show interface ^^^| findstr /i ""Connected""') do (
    netsh interface ip set dns name=""%%A"" static 1.1.1.1 primary >nul 2>&1
    netsh interface ip add dns name=""%%A"" 8.8.8.8 index=2 >nul 2>&1
)
  
reg add ""HKLM\SOFTWARE\Policies\Microsoft\Windows\Psched"" /v NonBestEffortLimit /t REG_DWORD /d 0 /f >nul 2>&1
  
powershell -NoProfile -WindowStyle Hidden -Command ""Get-NetAdapter | ForEach-Object { try { Disable-NetAdapterPowerManagement -Name $_.Name -ErrorAction SilentlyContinue } catch {} }"" >nul 2>&1
  
powershell -NoProfile -WindowStyle Hidden -Command ""Get-NetAdapterAdvancedProperty | Where-Object {$_.DisplayName -like '*Interrupt Moderation*'} | Set-NetAdapterAdvancedProperty -DisplayValue 'Disabled' -ErrorAction SilentlyContinue"" >nul 2>&1
  
powershell -NoProfile -WindowStyle Hidden -Command ""Get-NetAdapterAdvancedProperty | Where-Object {$_.DisplayName -like '*Interrupt Moderation*'} | Set-NetAdapterAdvancedProperty -DisplayValue 'Disabled' -ErrorAction SilentlyContinue"" >nul 2>&1
  
exit /b 0";

            await ExecuteBatTweakAsync(batContent, "Applying Advanced Network Optimizations...", "Network Fully Optimized", cancellationToken);
        }

        public static async Task RunJamesRegeditAsync(CancellationToken cancellationToken = default)
        {
            ReportProgress(0, "Preparing James Custom Regedit...");
            await Task.Delay(300, cancellationToken);

            string regContent = @"Windows Registry Editor Version 5.00

[HKEY_CURRENT_USER\Control Panel\Mouse]
""MouseSensitivity""=""6""
""MouseTrails""=""0""
""MouseThreshold1""=""0""
""MouseThreshold2""=""0""
""MouseSpeed""=""0""
""DoubleClickSpeed""=""200""
""Beep""=""No""
""DoubleClickHeight""=""4""
""DoubleClickWidth""=""4""
""ExtendedSounds""=""No""
""MouseHoverHeight""=""100""
""MouseHoverTime""=""900""
""MouseHoverWidth""=""100""
""SnapToDefaultButton""=""0""
""SwapMouseButtons""=""0""
""SmoothMouseXCurve""=hex:65,ef,4f,d4,df,f0,77,69,90,36,37,35,36,35,39,36,39,36,\
  36,2d,5b,70,34,35,72,34,35,74,72,79,65,72,72,67,6c,6f,7e,b4,5d,3d,5d,2d,5d,\
  5b,6f,69,75,75,72,74,65,72,66,74,68,79,37,75,35,5d,5b,3d,2d,5d,f3,b4,7e,70,\
  e7,6f,70,7e,e7,e7,f5,70,7e,e7,6c,e7,7e,70,b4,5d,5b,5d,2d,70,70,68,67,66,74,\
  65,65,64,72,77,77
""SmoothMouseYCurve""=hex:66,6f,76,33,35,33,65,36,64,73,36,79,64,75,63,33,37,33,\
  63,72,34,72,67,35,66,66,98,09,00,00,00,00,00,00,00,00,00,00,00,00,dd,dd,ee,\
  f4,f0,00,00,03,34,00,00,00,00,00,00,00,00,00,00,00,00,00,00,35,79,37,69,69,\
  38,6f,70,b4,e7,b4,3d,e7,e7,70,3d,7e,b4,e7,3b,3d,e7,3b,7e,3d,b4,e7,7e,3d,b4,\
  e7,7e,b4,3d,77,73,66,75,64,75,68,66,64,67,66,7e,7e,5b,5d,5b,73,64,66,67,6a,\
  67,75,67,66,74,67,67,66,79,37,79,37,64,66,67,66,37,74,66,5d,5d,7e,b4,00,00,\
  00,00,00,00,05,00,00,00,00,00,00,00,00,0f,00,00,00,00,00,00,00,05,00,00,00,\
  00,00,00,00,05,f0,00,00,00,00,00,00,00
""ActiveUser""=""HS DUX CRIA""
""ActiveDevoloped""=""James God""
""Active""=""James God""
""ActiveAC""=""James God""
""ActiveFix""=""James God""
""DoubleClickSpeed2""=""0,5""
""DoubleClickWidth2""=""0,6""
""TcpWindowSize""=dword:0005ae4c
""TcpNoDelay""=dword:0000147f
""TCPDelAckTicks""=dword:00000004
""Tcp1323Opts""=dword:00000004
""TcpMaxDataRetransmissions""=dword:00000003
""SackOpts""=dword:00000001
""DefaultTTL""=dword:00007fff
""DoubleClickHeight2""=""0,7""
""MouseSpeed2""=""1""
""MouseTK""=""810""
""Mousetrack""=""908""
""Fov""=""20000""
""ActiveWindowTracking""=dword:00000001
""AimAssist""=dword:000003e8
""AimBot""=dword:000003e8
""AimBotLeft""=dword:000003e8
""AimBotHeadshot""=dword:000003e8
""AimBotSpeed""=dword:000003e8
""AimFov""=dword:000003e8
""AimHead""=dword:000003e8
""AimHeadRightC""=dword:000003e8
""AimHeadshot""=dword:000003e8
""AimLock""=dword:000003e8
""AimSpeed""=dword:000003e8
""DockTargetMouseDragOutWidth""=""1""
""DockTargetMouseSideMoveWidth""=""2""
""DockTargetMouseWidth""=""3""
""DockTargetPenDragOutWidth""=""4""
""DockTargetPenSideMoveWidth""=""2""
""DockTargetPenWidth""=""1""
""MouseSensibility2""=""6""
""MouseCP""=""55""
""Mousecrib""=""10""
""MouseGrab""=""908""
""MouseStickOn""=""10""
""ExtendedSounds2""=""No""
""FovAutoHeadshot""=dword:000003e8
""FovHead""=dword:000003e8
""Headshot""=dword:000003e8
""Mousecontrolusb""=""1""
";

            await ExecuteRegTweakAsync(regContent, "Preparing James Custom Regedit...", "James Custom Regedit Applied Successfully!", cancellationToken);
        }

        // --- TOOLBOX ---

        public static async Task<(int cpu, int ram, int disk, int total)> BenchmarkSystemAsync(CancellationToken cancellationToken = default)
        {
            ReportProgress(10, "Starting CPU Benchmark...");

            int cpuScore = 0;
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sw = Stopwatch.StartNew();
                double result = 0;
                for (long i = 0; i < 30_000_000; i++)
                {
                    if ((i & 0xFFFFF) == 0) cancellationToken.ThrowIfCancellationRequested();
                    result += Math.Sqrt(i) * Math.Log(i + 1);
                }
                sw.Stop();
                cpuScore = Math.Clamp((int)(3000.0 / Math.Max(sw.ElapsedMilliseconds, 1) * 100), 0, 999);
            }, cancellationToken);
            ReportProgress(40, "CPU done. Testing Memory...");

            int ramScore = 0;
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sw = Stopwatch.StartNew();
                // Use smaller buffer to avoid OOM on low-memory systems
                int bufferSize = Math.Min(256 * 1024 * 1024, (int)(GC.GetTotalMemory(false) / 2));
                var buf = new byte[bufferSize];
                for (int i = 0; i < buf.Length; i++) buf[i] = (byte)(i & 0xFF);
                sw.Stop();
                ramScore = Math.Clamp((int)(2500.0 / Math.Max(sw.ElapsedMilliseconds, 1) * 100), 0, 999);
            }, cancellationToken);
            ReportProgress(70, "Memory done. Testing Disk...");

            int diskScore = 0;
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                string path = Path.Combine(Path.GetTempPath(), "optimizer_bench.tmp");
                try
                {
                    // 32MB instead of 64MB
                    byte[] data = new byte[32 * 1024 * 1024];
                    new Random(42).NextBytes(data);
                    var sw = Stopwatch.StartNew();
                    File.WriteAllBytes(path, data);
                    File.ReadAllBytes(path);
                    sw.Stop();
                    diskScore = Math.Clamp((int)(4000.0 / Math.Max(sw.ElapsedMilliseconds, 1) * 100), 0, 999);
                }
                finally { if (File.Exists(path)) File.Delete(path); }
            }, cancellationToken);
            ReportProgress(100, "Benchmark Complete!");

            int total = (int)(cpuScore * 0.5 + ramScore * 0.3 + diskScore * 0.2);
            return (cpuScore, ramScore, diskScore, total);
        }

        public static async Task<string> CreateRestorePointAsync(CancellationToken cancellationToken = default)
        {
            ReportProgress(10, "Creating System Restore Point...");
            string result;
            try
            {
                // Enable System Restore on C: first (may already be on)
                await RunProcessAsync("powershell.exe", "-NoProfile -Command \"Enable-ComputerRestore -Drive 'C:\\'\"", cancellationToken);
                await RunProcessAsync("powershell.exe", "-NoProfile -Command \"Checkpoint-Computer -Description 'Optimizer Pre-Tweak Snapshot' -RestorePointType MODIFY_SETTINGS\"", cancellationToken);
                result = "✅ Restore point created: 'Optimizer Pre-Tweak Snapshot'\nYou can restore it via: Control Panel → Recovery → Open System Restore";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                HardwareLogger.LogError("CreateRestorePointAsync failed", ex);
                result = $"⚠️ Failed: {ex.Message}\nTry running the optimizer as Administrator.";
            }
            ReportProgress(100, "Done.");
            return result;
        }

        public static async Task<string> CheckDriversAsync(CancellationToken cancellationToken = default)
        {
            ReportProgress(10, "Querying GPU drivers via WMI...");
            var sb = new StringBuilder();
            try
            {
                await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
                    using var collection = searcher.Get();
                    foreach (ManagementObject obj in collection)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string name = obj["Name"]?.ToString() ?? "Unknown GPU";
                        string driver = obj["DriverVersion"]?.ToString() ?? "N/A";
                        string dateRaw = obj["DriverDate"]?.ToString() ?? "";
                        string date = "N/A";
                        if (dateRaw.Length >= 8)
                        {
                            if (DateTime.TryParseExact(dateRaw.Substring(0, 8), "yyyyMMdd",
                                System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.None, out var dt))
                            {
                                date = dt.ToString("dd MMM yyyy");
                                if ((DateTime.Now - dt).TotalDays > 180)
                                    sb.AppendLine($"⚠️ {name}");
                                else
                                    sb.AppendLine($"✅ {name}");
                            }
                            else sb.AppendLine($"❓ {name}");
                        }
                        sb.AppendLine($"   Driver Version: {driver}");
                        sb.AppendLine($"   Driver Date:    {date}");
                        sb.AppendLine();
                    }
                }, cancellationToken);
                if (sb.Length == 0) sb.AppendLine("No video controllers found.");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                HardwareLogger.LogError("CheckDriversAsync failed", ex);
                sb.AppendLine($"Error: {ex.Message}");
            }
            ReportProgress(100, "Driver check complete.");
            return sb.ToString().Trim();
        }

        public static async Task ApplyGameProfileAsync(string game, CancellationToken cancellationToken = default)
        {
            ReportProgress(10, $"Applying {game} profile...");

            string regContent = game switch
            {
                "FreeFire" => @"Windows Registry Editor Version 5.00
; Free Fire Optimized Profile
[HKEY_CURRENT_USER\Software\Classes\Local Settings\Software\Microsoft\Windows\GameUX\GameStatistics]
""GameID""=""FreeFire""
[HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games]
""Affinity""=dword:00000000
""Background Only""=""False""
""Clock Rate""=dword:00002710
""GPU Priority""=dword:00000008
""Priority""=dword:00000006
""Scheduling Category""=""High""
""SFIO Priority""=""High""
[HKEY_CURRENT_USER\Control Panel\Desktop]
""MenuShowDelay""=""0""
""WaitToKillAppTimeout""=""2000""
""HungAppTimeout""=""1000""",
                "PUBG" => @"Windows Registry Editor Version 5.00
; PUBG Optimized Profile
[HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games]
""Affinity""=dword:00000000
""Background Only""=""False""
""Clock Rate""=dword:00002710
""GPU Priority""=dword:00000008
""Priority""=dword:00000006
""Scheduling Category""=""High""
""SFIO Priority""=""High""
[HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize]
""StartupDelayInMSec""=dword:00000000
[HKEY_CURRENT_USER\Control Panel\Desktop]
""MenuShowDelay""=""0""",
                _ => ""
            };

            if (!string.IsNullOrEmpty(regContent))
            {
                string path = Path.Combine(Path.GetTempPath(), $"optimizer_{game}.reg");
                await File.WriteAllTextAsync(path, regContent, System.Text.Encoding.Unicode, cancellationToken);
                await RunProcessAsync("regedit.exe", $"/s \"{path}\"", cancellationToken);
                if (File.Exists(path)) File.Delete(path);
            }
            ReportProgress(100, $"{game} profile applied!");
        }

        public static async Task RunServiceDebloaterAsync(CancellationToken cancellationToken = default)
        {
            ReportProgress(0, "Preparing to debloat services...");

            string[] servicesToDisable = new string[]
            {
                "SysMain", "DiagTrack", "SecurityHealthService", "wscsvc",
                "WSearch", "Spooler", "TabletInputService", "MapsBroker",
                "XblAuthManager", "XblGameSave", "XboxNetApiSvc", "RemoteRegistry",
                "wisvc", "Fax"
            };

            for (int i = 0; i < servicesToDisable.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string service = servicesToDisable[i];
                int percent = (int)(((float)(i + 1) / servicesToDisable.Length) * 100);
                ReportProgress(percent, $"Disabling {service}...");

                try
                {
                    await RunProcessAsync("sc.exe", $"config \"{service}\" start=disabled", cancellationToken);
                    await RunProcessAsync("sc.exe", $"stop \"{service}\"", cancellationToken);
                }
                catch (Exception ex) { HardwareLogger.LogError($"Service debloat failed for {service}", ex); }
            }

            ReportProgress(100, "Services Debloated Successfully!");
        }

        public static async Task<string> ScheduleTaskAsync(CancellationToken cancellationToken = default)
        {
            string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrEmpty(exePath))
                return "⚠️ Could not determine executable path.";

            string taskName = "OptimizerAutoCleanup";
            await RunProcessAsync("schtasks.exe", $"/delete /tn \"{taskName}\" /f", cancellationToken);
            await RunProcessAsync("schtasks.exe",
                $"/create /tn \"{taskName}\" /tr \"\\\"{exePath}\\\" -silent\" /sc HOURLY /mo 3 /ru SYSTEM /rl HIGHEST /f", cancellationToken);

            string result = "";
            try
            {
                using var p = new Process
                {
                    StartInfo = new ProcessStartInfo("schtasks.exe", $"/query /tn \"{taskName}\"")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    }
                };
                p.Start();
                result = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync(cancellationToken);
            }
            catch (Exception ex) { HardwareLogger.LogError("ScheduleTaskAsync verification failed", ex); }

            if (result.Contains(taskName))
                return $"✅ Scheduled Task '{taskName}' created!\nRuns every 3 hours silently.\nView in: Task Scheduler → Task Scheduler Library";
            else
                return "⚠️ Task may not have been created. Try running as Administrator.";
        }

        public static async Task<string> SetAutoRunAsync(bool enable, CancellationToken cancellationToken = default)
        {
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrEmpty(exePath))
                    return "⚠️ Could not determine executable path.";

                string taskName = "JamesOptimizerAutoStart";

                if (enable)
                {
                    await RunProcessAsync("schtasks.exe", $"/delete /tn \"{taskName}\" /f", cancellationToken);

                    string xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <Triggers><LogonTrigger><Enabled>true</Enabled></LogonTrigger></Triggers>
  <Principals><Principal id=""Author""><GroupId>S-1-5-32-545</GroupId><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
  <Settings><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries></Settings>
  <Actions Context=""Author""><Exec><Command>{exePath}</Command><Arguments>-background</Arguments></Exec></Actions>
</Task>";
                    string xmlPath = Path.Combine(Path.GetTempPath(), "JamesOptTask.xml");
                    await File.WriteAllTextAsync(xmlPath, xml, cancellationToken);

                    await RunProcessAsync("schtasks.exe", $"/create /tn \"{taskName}\" /xml \"{xmlPath}\" /f", cancellationToken);

                    string serviceExePath = Path.Combine(Path.GetDirectoryName(exePath) ?? "", "JamesOptimizer.Service.exe");
                    if (File.Exists(serviceExePath))
                    {
                        await RunProcessAsync("sc.exe", $"stop \"JamesOptimizerService\"", cancellationToken);
                        await RunProcessAsync("sc.exe", $"delete \"JamesOptimizerService\"", cancellationToken);
                        await Task.Delay(1000, cancellationToken);
                        await RunProcessAsync("sc.exe", $"create \"JamesOptimizerService\" binPath= \"\\\"{serviceExePath}\\\"\" start= auto obj= LocalSystem DisplayName= \"James Optimizer Background Service\"", cancellationToken);
                        await RunProcessAsync("sc.exe", $"start \"JamesOptimizerService\"", cancellationToken);
                    }

                    try
                    {
                        using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                        {
                            key?.DeleteValue("JamesOptimizer", false);
                        }
                    }
                    catch (Exception ex) { HardwareLogger.LogError("Cleanup registry run key failed", ex); }

                    return "✅ Auto-run at startup enabled via Task Scheduler!";
                }
                else
                {
                    await RunProcessAsync("schtasks.exe", $"/delete /tn \"{taskName}\" /f", cancellationToken);

                    await RunProcessAsync("sc.exe", $"stop \"JamesOptimizerService\"", cancellationToken);
                    await RunProcessAsync("sc.exe", $"delete \"JamesOptimizerService\"", cancellationToken);

                    try
                    {
                        using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                        {
                            key?.DeleteValue("JamesOptimizer", false);
                        }
                    }
                    catch (Exception ex) { HardwareLogger.LogError("Cleanup registry run key failed", ex); }

                    return "✅ Auto-run at startup disabled.";
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                HardwareLogger.LogError("SetAutoRunAsync failed", ex);
                return $"⚠️ Failed to configure Auto-run: {ex.Message}";
            }
        }

        public static async Task<bool> IsAutoRunEnabledAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var p = new Process();
                p.StartInfo.FileName = "schtasks.exe";
                p.StartInfo.Arguments = "/query /tn \"JamesOptimizerAutoStart\"";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.CreateNoWindow = true;
                p.Start();
                string output = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync(cancellationToken);
                return output.Contains("JamesOptimizerAutoStart");
            }
            catch
            {
                return false;
            }
        }

        // --- ADVANCED STORAGE SCANNERS ---

        public class ScannedFile
        {
            public string FilePath { get; set; } = "";
            public string FileName { get; set; } = "";
            public long SizeBytes { get; set; }
            public DateTime LastModified { get; set; }
            public string Hash { get; set; } = "";
        }

        public static async Task<List<ScannedFile>> ScanDownloadsAsync(CancellationToken cancellationToken = default)
        {
            var result = new List<ScannedFile>();
            string downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(downloadsPath)) return result;

            try
            {
                var options = new IOEnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true };
                foreach (var file in Directory.EnumerateFiles(downloadsPath, "*", options))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var fi = new FileInfo(file);
                        result.Add(new ScannedFile
                        {
                            FilePath = file,
                            FileName = fi.Name,
                            SizeBytes = fi.Length,
                            LastModified = fi.LastWriteTime
                        });
                    }
                    catch (Exception ex) { HardwareLogger.LogError($"ScanDownloadsAsync file error: {file}", ex); }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { HardwareLogger.LogError("ScanDownloadsAsync failed", ex); }
            
            return result.OrderByDescending(f => f.LastModified).ToList();
        }

        public static async Task<List<ScannedFile>> ScanLargeFilesAsync(long minSizeBytes = 50 * 1024 * 1024, CancellationToken cancellationToken = default)
        {
            var result = new List<ScannedFile>();
            string profilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!Directory.Exists(profilePath)) return result;

            try
            {
                var options = new IOEnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true };
                foreach (var file in Directory.EnumerateFiles(profilePath, "*", options))
                {
                    cancellationToken.ThrowIfCancellationRequested();
try
                        {
                            var fi = new FileInfo(file);
                            if (fi.Length >= minSizeBytes)
                            {
                                result.Add(new ScannedFile
                                {
                                    FilePath = file,
                                    FileName = fi.Name,
                                    SizeBytes = fi.Length,
                                    LastModified = fi.LastWriteTime
                                });
                            }
                        }
                        catch (Exception ex) { HardwareLogger.LogError($"ScanLargeFilesAsync file error: {file}", ex); }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { HardwareLogger.LogError("ScanLargeFilesAsync failed", ex); }
            
            return result.OrderByDescending(f => f.SizeBytes).ToList();
        }

        public static async Task<List<List<ScannedFile>>> ScanDuplicatesAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            var duplicateGroups = new List<List<ScannedFile>>();
            string profilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] targetFolders = { "Documents", "Downloads", "Desktop", "Pictures", "Videos" };

            var allFiles = new List<FileInfo>();
            var options = new IOEnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true };

            foreach (var folder in targetFolders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string fullPath = Path.Combine(profilePath, folder);
if (Directory.Exists(fullPath))
                    {
                        try
                        {
                            allFiles.AddRange(Directory.EnumerateFiles(fullPath, "*", options).Select(f => new FileInfo(f)));
                        }
                        catch (Exception ex) { HardwareLogger.LogError($"ScanDuplicatesAsync folder error: {fullPath}", ex); }
                    }
            }

            var sizeGroups = allFiles.GroupBy(f => f.Length).Where(g => g.Count() > 1 && g.Key > 0).ToList();
            int totalFilesToHash = sizeGroups.Sum(g => g.Count());
            int filesHashed = 0;

            using var sha256 = SHA256.Create();

            foreach (var sizeGroup in sizeGroups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var hashGroups = new Dictionary<string, List<ScannedFile>>();

                foreach (var file in sizeGroup)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        using var stream = File.OpenRead(file.FullName);
                        byte[] hashBytes = sha256.ComputeHash(stream);
                        string hashStr = BitConverter.ToString(hashBytes);

                        if (!hashGroups.ContainsKey(hashStr))
                            hashGroups[hashStr] = new List<ScannedFile>();

                        hashGroups[hashStr].Add(new ScannedFile
                        {
                            FilePath = file.FullName,
                            FileName = file.Name,
                            SizeBytes = file.Length,
                            LastModified = file.LastWriteTime,
                            Hash = hashStr
                        });
                    }
                    catch (Exception ex) { HardwareLogger.LogError($"ScanDuplicatesAsync hash failed: {file.FullName}", ex); }

                    filesHashed++;
                    if (progress != null && totalFilesToHash > 0)
                    {
                        progress.Report((int)((filesHashed / (double)totalFilesToHash) * 100));
                    }
                }

                foreach (var kvp in hashGroups)
                {
                    if (kvp.Value.Count > 1)
                    {
                        duplicateGroups.Add(kvp.Value.OrderByDescending(f => f.LastModified).ToList());
                    }
                }
            }

            return duplicateGroups.OrderByDescending(g => g.First().SizeBytes).ToList();
        }

        public static async Task DeleteFilesAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
        {
            var pathsList = filePaths.ToList();
            int total = pathsList.Count;
            if (total == 0) return;

            for (int i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (i % Math.Max(1, total / 20) == 0)
                {
                    ReportProgress((int)(((double)i / total) * 100), $"Deleting files...");
                }

                try
                {
                    if (File.Exists(pathsList[i])) File.Delete(pathsList[i]);
                }
                catch (Exception ex) { HardwareLogger.LogError($"DeleteFilesAsync failed for {pathsList[i]}", ex); }
            }
            ReportProgress(100, "Deletion Complete");
        }
    }
}
