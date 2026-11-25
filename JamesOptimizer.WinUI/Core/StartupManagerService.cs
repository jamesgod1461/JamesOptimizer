using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Management;
using System.Runtime.Versioning;
using System.ServiceProcess;
using JamesOptimizer.Core;
using Microsoft.Win32;

namespace JamesOptimizer
{
    [SupportedOSPlatform("windows")]
    public class StartupItem
    {
        public string Name { get; set; } = "";
        public string ServiceName { get; set; } = "";

        private string _path = "";
        public string Path
        {
            get => _path;
            set
            {
                _path = value;
                ResolveMetadata();
            }
        }

        public string Source { get; set; } = "";
        public string Publisher { get; set; } = "";
        public bool IsEnabled { get; set; } = true;
        public string Type { get; set; } = "App"; // App, ScheduledTask, Autorun, Service
        public string ImpactLabel { get; set; } = "Low Impact";
        public double EstimatedBootSeconds { get; set; } = 0.4;
        public string TaskFullPath { get; set; } = "";

        private void ResolveMetadata()
        {
            if (string.IsNullOrWhiteSpace(_path)) return;
            try
            {
                string exePath = ServiceInfo.ExtractExecutablePath(_path);
                if (!File.Exists(exePath)) return;

                long fileBytes = 0;
                try { fileBytes = new FileInfo(exePath).Length; } catch { }

                if (fileBytes > 30L * 1024 * 1024 ||
                    _path.Contains("Steam", StringComparison.OrdinalIgnoreCase) ||
                    _path.Contains("Discord", StringComparison.OrdinalIgnoreCase) ||
                    _path.Contains("Teams", StringComparison.OrdinalIgnoreCase))
                {
                    ImpactLabel = "High Impact";
                    EstimatedBootSeconds = 2.4;
                }
                else if (fileBytes > 5L * 1024 * 1024)
                {
                    ImpactLabel = "Medium Impact";
                    EstimatedBootSeconds = 1.1;
                }
                else
                {
                    ImpactLabel = "Low Impact";
                    EstimatedBootSeconds = 0.4;
                }

                Publisher = NativeSystemHelper.ResolvePublisherLabel(exePath);
            }
            catch (Exception ex)
            {
                HardwareLogger.LogError("ResolveMetadata failed", ex);
            }
        }
    }

    [SupportedOSPlatform("windows")]
    public static class StartupManagerService
    {
        public static List<StartupItem> GetStartupApps()
        {
            var items = new List<StartupItem>();
            try
            {
                ReadRegistryKey(items, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
                ReadRegistryKey(items, Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", @"HKLM\Software\Microsoft\Windows\CurrentVersion\Run", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");

                string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (Directory.Exists(startupFolder))
                {
                    foreach (string file in Directory.GetFiles(startupFolder))
                    {
                        string name = System.IO.Path.GetFileName(file);
                        bool isEnabled = CheckStartupApproved(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder", name);
                        items.Add(new StartupItem { Name = name, Path = file, Source = startupFolder, Type = "App", IsEnabled = isEnabled });
                    }
                }

                ApplyBootDiagnosticsImpact(items);
            }
            catch (Exception ex) { HardwareLogger.LogError("GetStartupApps failed", ex); }
            return items;
        }

        private static void ApplyBootDiagnosticsImpact(List<StartupItem> items)
        {
            if (items.Count == 0) return;
            var itemExes = items
                .Select(i => (Item: i, ExeName: System.IO.Path.GetFileNameWithoutExtension(ServiceInfo.ExtractExecutablePath(i.Path))))
                .Where(x => !string.IsNullOrEmpty(x.ExeName))
                .ToList();
            if (itemExes.Count == 0) return;

            try
            {
                var query = new EventLogQuery("Microsoft-Windows-Diagnostics-Performance/Operational", PathType.LogName, "*[System[(EventID>=100 and EventID<=110)]]");
                query.ReverseDirection = true;
                using var reader = new EventLogReader(query);
                int inspected = 0;
                while (inspected < 40)
                {
                    using var evt = reader.ReadEvent();
                    if (evt == null) break;
                    inspected++;
                    string xml = evt.ToXml();
                    foreach (var (item, exeName) in itemExes)
                    {
                        if (xml.Contains(exeName, StringComparison.OrdinalIgnoreCase))
                        {
                            item.ImpactLabel = "Measured Boot Impact";
                            break;
                        }
                    }
                }
            }
            catch
            {
                // Event log may require elevated/Diagnostics-Performance access on some SKUs
            }
        }

        public static List<StartupItem> GetScheduledTasks()
        {
            var items = new List<StartupItem>();
            try
            {
                var comType = Type.GetTypeFromProgID("Schedule.Service");
                if (comType == null) return items;
                dynamic? scheduler = Activator.CreateInstance(comType);
                if (scheduler == null) return items;
                scheduler.Connect();
                dynamic rootFolder = scheduler.GetFolder("\\");
                CollectTasksRecursive(rootFolder, items);
            }
            catch (Exception ex)
            {
                HardwareLogger.LogError("GetScheduledTasks failed", ex);
            }
            return items;
        }

        private static void CollectTasksRecursive(dynamic folder, List<StartupItem> items)
        {
            try
            {
                string folderPath = folder.Path ?? "\\";
                if (folderPath.StartsWith("\\Microsoft\\Windows", StringComparison.OrdinalIgnoreCase))
                    return;

                foreach (dynamic task in folder.GetTasks(0))
                {
                    try
                    {
                        string name = task.Name ?? "";
                        string path = task.Path ?? "";
                        bool enabled = task.Enabled;
                        string xml = task.Xml ?? "";
                        bool isBootOrLogon = xml.Contains("<LogonTrigger", StringComparison.OrdinalIgnoreCase) ||
                                             xml.Contains("<BootTrigger", StringComparison.OrdinalIgnoreCase);
                        if (!isBootOrLogon) continue;

                        string exec = ExtractTag(xml, "Command");
                        string args = ExtractTag(xml, "Arguments");
                        string fullCmd = string.IsNullOrWhiteSpace(args) ? exec : $"{exec} {args}";
                        if (string.IsNullOrWhiteSpace(fullCmd)) continue;

                        items.Add(new StartupItem
                        {
                            Name = name,
                            TaskFullPath = path,
                            Path = fullCmd,
                            Source = $"Task: {folderPath}",
                            Type = "ScheduledTask",
                            IsEnabled = enabled
                        });
                    }
                    catch { }
                }

                foreach (dynamic sub in folder.GetFolders(0))
                {
                    CollectTasksRecursive(sub, items);
                }
            }
            catch { }
        }

        private static string ExtractTag(string xml, string tag)
        {
            string open = $"<{tag}>";
            string close = $"</{tag}>";
            int s = xml.IndexOf(open, StringComparison.OrdinalIgnoreCase);
            if (s < 0) return "";
            s += open.Length;
            int e = xml.IndexOf(close, s, StringComparison.OrdinalIgnoreCase);
            return e > s ? xml[s..e].Trim() : "";
        }

        public static List<StartupItem> GetThirdPartyServices()
        {
            var items = new List<StartupItem>();
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, DisplayName, PathName, StartMode FROM Win32_Service WHERE StartMode = 'Auto'");
                using var results = searcher.Get();
                foreach (ManagementObject queryObj in results)
                using (queryObj)
                {
                    string svcName = queryObj["Name"]?.ToString() ?? "";
                    if (CoreConstants.IsProtectedService(svcName)) continue;

                    string path = queryObj["PathName"]?.ToString() ?? "";
                    string exe = ServiceInfo.ExtractExecutablePath(path);
                    string pub = NativeSystemHelper.ResolvePublisherLabel(exe);
                    if (pub.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)) continue;

                    items.Add(new StartupItem
                    {
                        Name = queryObj["DisplayName"]?.ToString() ?? svcName,
                        ServiceName = svcName,
                        Path = path,
                        Source = "3rd-Party Auto Service",
                        Type = "Service",
                        IsEnabled = true
                    });
                }
            }
            catch (Exception ex) { HardwareLogger.LogError("GetThirdPartyServices failed", ex); }
            return items;
        }

        public static List<StartupItem> GetRegistryAutoruns()
        {
            var items = new List<StartupItem>();
            string[] autorunKeys =
            {
                @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
                @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run",
                @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon\Userinit",
                @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon\Shell"
            };

            foreach (string path in autorunKeys)
            {
                ReadAutorunHive(items, Registry.LocalMachine, @"HKLM\", path);
                ReadAutorunHive(items, Registry.CurrentUser, @"HKCU\", path);
            }
            return items;
        }

        private static void ReadAutorunHive(List<StartupItem> items, RegistryKey root, string prefix, string path)
        {
            try
            {
                using var key = root.OpenSubKey(path, false);
                if (key == null) return;
                foreach (string valName in key.GetValueNames())
                {
                    items.Add(new StartupItem
                    {
                        Name = valName,
                        Path = key.GetValue(valName)?.ToString() ?? "",
                        Source = prefix + path,
                        Type = "Autorun",
                        IsEnabled = true
                    });
                }
            }
            catch (Exception ex) { HardwareLogger.LogError($"GetRegistryAutoruns failed for {prefix}{path}", ex); }
        }

        private static void ReadRegistryKey(List<StartupItem> items, RegistryKey root, string subKey, string sourceName, string approvedKeyPath)
        {
            using RegistryKey? key = root.OpenSubKey(subKey, false);
            if (key == null) return;
            foreach (string valName in key.GetValueNames())
            {
                bool isEnabled = CheckStartupApproved(root, approvedKeyPath, valName);
                items.Add(new StartupItem
                {
                    Name = valName,
                    Path = key.GetValue(valName)?.ToString() ?? "",
                    Source = sourceName,
                    Type = "App",
                    IsEnabled = isEnabled
                });
            }
        }

        private static bool CheckStartupApproved(RegistryKey root, string approvedKeyPath, string valueName)
        {
            try
            {
                using RegistryKey? key = root.OpenSubKey(approvedKeyPath, false);
                if (key?.GetValue(valueName) is byte[] bytes && bytes.Length > 0)
                    return bytes[0] == 0x02;
            }
            catch (Exception ex) { HardwareLogger.LogError("CheckStartupApproved failed", ex); }
            return true;
        }

        public static bool SetStartupState(StartupItem item, bool enable)
        {
            try
            {
                if (item.Type == "App")
                {
                    if (item.Source.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase))
                        ToggleRegistryStartupApproved(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", item.Name, enable);
                    else if (item.Source.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase))
                        ToggleRegistryStartupApproved(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", item.Name, enable);
                    else
                        ToggleRegistryStartupApproved(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder", item.Name, enable);
                }
                else if (item.Type == "ScheduledTask" && !string.IsNullOrEmpty(item.TaskFullPath))
                {
                    string flag = enable ? "/ENABLE" : "/DISABLE";
                    var res = NativeSystemHelper.RunSystem32ToolSync("schtasks.exe", new[] { "/Change", "/TN", item.TaskFullPath, flag });
                    if (res.ExitCode != 0) return false;
                    TweakBackupService.RecordSnapshot(new TweakSnapshot
                    {
                        TweakId = $"task_{item.Name}",
                        Category = "STARTUP",
                        Title = $"Task '{item.Name}' -> {(enable ? "Enabled" : "Disabled")}",
                        KeyPath = item.TaskFullPath,
                        Steps = new List<SnapshotStep>
                        {
                            new() { Kind = SnapshotStepKind.ScheduledTask, ExtraTarget = item.TaskFullPath, OriginalValueText = (!enable).ToString() }
                        }
                    });
                }
                else if (item.Type == "Service" && !string.IsNullOrEmpty(item.ServiceName))
                {
                    if (CoreConstants.IsProtectedService(item.ServiceName)) return false;
                    TweakBackupService.SetServiceStartMode(item.ServiceName, enable ? "Automatic" : "Manual");
                    if (!enable) ServiceManagerLogic.StopService(item.ServiceName);
                }

                item.IsEnabled = enable;
                return true;
            }
            catch (Exception ex)
            {
                HardwareLogger.LogError("SetStartupState failed", ex);
                return false;
            }
        }

        private static void ToggleRegistryStartupApproved(RegistryKey root, string approvedKeyPath, string valueName, bool enable)
        {
            using RegistryKey? key = root.CreateSubKey(approvedKeyPath);
            if (key == null) return;
            byte[] data = new byte[12];
            data[0] = enable ? (byte)0x02 : (byte)0x03;
            key.SetValue(valueName, data, RegistryValueKind.Binary);
        }

        public static void DeleteStartupItem(StartupItem item)
        {
            try
            {
                if (item.Source.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase))
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                    key?.DeleteValue(item.Name, false);
                }
                else if (item.Source.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase))
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                    key?.DeleteValue(item.Name, false);
                }
                else if (item.Type == "App" && File.Exists(item.Path))
                {
                    NativeSystemHelper.MoveToRecycleBin(item.Path);
                }
            }
            catch (Exception ex)
            {
                HardwareLogger.LogError($"DeleteStartupItem failed for {item.Name}", ex);
            }
        }
    }
}
