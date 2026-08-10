using System;
using System.Collections.Generic;
using System.Management;
using System.IO;
using Microsoft.Win32;

namespace JamesOptimizer
{
    public class StartupItem
    {
        public string Name { get; set; } = "";
        
        private string _path = "";
        public string Path 
        { 
            get => _path; 
            set 
            { 
                _path = value; 
                ResolvePublisher();
            } 
        }

        public string Source { get; set; } = "";
        public string Publisher { get; set; } = "";
        public bool IsEnabled { get; set; } = true;
        public string Type { get; set; } = "App"; // App, Service, Autorun

        private void ResolvePublisher()
        {
            if (string.IsNullOrWhiteSpace(_path)) return;
            try
            {
                string exePath = "";
                if (_path.StartsWith("\""))
                {
                    int endQuote = _path.IndexOf("\"", 1);
                    if (endQuote > 0)
                        exePath = _path.Substring(1, endQuote - 1);
                }
                else
                {
                    int exeIndex = _path.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                    if (exeIndex > 0)
                        exePath = _path.Substring(0, exeIndex + 4);
                    else
                        exePath = _path;
                }
                
                exePath = exePath.Trim();
                
                // Handle native paths and environment variables
                if (exePath.StartsWith(@"\??\")) exePath = exePath.Substring(4);
                if (exePath.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase)) 
                    exePath = exePath.Replace(@"\SystemRoot\", @"%SystemRoot%\");
                
                exePath = Environment.ExpandEnvironmentVariables(exePath);

                if (System.IO.File.Exists(exePath))
                {
                    try
                    {
                        #pragma warning disable SYSLIB0057
                        var cert = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(exePath);
                        #pragma warning restore SYSLIB0057
                        string subject = cert.Subject;
                        var parts = subject.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
                        string? cn = null, o = null;
                        foreach (var part in parts)
                        {
                            if (part.StartsWith("O=")) o = part.Substring(2);
                            else if (part.StartsWith("CN=")) cn = part.Substring(3);
                        }
                        Publisher = (o ?? cn ?? subject).Replace("\"", "");
                        Publisher = "[Verified] " + Publisher;
                    }
                    catch
                    {
                        var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(exePath);
                        Publisher = versionInfo.CompanyName ?? "";
                    }
                }
            }
            catch (Exception ex) { HardwareLogger.LogError("ResolvePublisher failed", ex); }
        }
    }

    public static class StartupManagerService
    {
        public static List<StartupItem> GetStartupApps()
        {
            var items = new List<StartupItem>();
            try
            {
                // HKCU Run
                ReadRegistryKey(items, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");

                // HKLM Run
                ReadRegistryKey(items, Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", @"HKLM\Software\Microsoft\Windows\CurrentVersion\Run", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");

                // Startup Folder
                string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (Directory.Exists(startupFolder))
                {
                    foreach (string file in Directory.GetFiles(startupFolder))
                    {
                        string name = Path.GetFileName(file);
                        bool isEnabled = CheckStartupApproved(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder", name);

                        items.Add(new StartupItem { Name = name, Path = file, Source = startupFolder, Type = "App", IsEnabled = isEnabled });
                    }
                }
            }
            catch (Exception ex) { HardwareLogger.LogError("GetStartupApps failed", ex); }
            return items;
        }

        private static void ReadRegistryKey(List<StartupItem> items, RegistryKey root, string subKey, string sourceName, string approvedKeyPath)
        {
            using (RegistryKey? key = root.OpenSubKey(subKey, false))
            {
                if (key != null)
                {
                    foreach (string valName in key.GetValueNames())
                    {
                        bool isEnabled = CheckStartupApproved(root, approvedKeyPath, valName);
                        items.Add(new StartupItem { Name = valName, Path = key.GetValue(valName)?.ToString() ?? "", Source = sourceName, Type = "App", IsEnabled = isEnabled });
                    }
                }
            }
        }

        private static bool CheckStartupApproved(RegistryKey root, string approvedKeyPath, string valueName)
        {
            try
            {
                using (RegistryKey? key = root.OpenSubKey(approvedKeyPath, false))
                {
                    if (key != null)
                    {
                        object? val = key.GetValue(valueName);
                        if (val is byte[] bytes && bytes.Length > 0)
                        {
                            return bytes[0] == 0x02; // 0x02 is enabled, 0x03/0x0B is disabled
                        }
                    }
                }
            }
            catch (Exception ex) { HardwareLogger.LogError("CheckStartupApproved failed", ex); }
            return true; // Default to enabled if no StartupApproved entry exists
        }

        public static void ToggleStartupItem(StartupItem item, bool enable)
        {
            try
            {
                if (item.Type == "App")
                {
                    if (item.Source.StartsWith("HKCU"))
                    {
                        ToggleRegistryStartupApproved(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", item.Name, enable);
                    }
                    else if (item.Source.StartsWith("HKLM"))
                    {
                        ToggleRegistryStartupApproved(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", item.Name, enable);
                    }
                    else // Startup Folder
                    {
                        ToggleRegistryStartupApproved(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder", item.Name, enable);
                    }
                }
            }
            catch (Exception ex) { HardwareLogger.LogError("ToggleStartupItem failed", ex); }
        }

        private static void ToggleRegistryStartupApproved(RegistryKey root, string approvedKeyPath, string valueName, bool enable)
        {
            try
            {
                using (RegistryKey? key = root.CreateSubKey(approvedKeyPath))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            // Writing 0x02 for enabled (12 bytes total)
                            byte[] data = new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                            key.SetValue(valueName, data, RegistryValueKind.Binary);
                        }
                        else
                        {
                            // Writing 0x03 for disabled (12 bytes total)
                            byte[] data = new byte[] { 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                            key.SetValue(valueName, data, RegistryValueKind.Binary);
                        }
                    }
                }
            }
            catch (Exception ex) { HardwareLogger.LogError("ToggleRegistryStartupApproved failed", ex); }
        }

        public static List<StartupItem> GetAutomaticServices()
        {
            var items = new List<StartupItem>();
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name, DisplayName, PathName FROM Win32_Service WHERE StartMode = 'Auto'"))
                {
                    foreach (ManagementObject queryObj in searcher.Get())
                    {
                        string name = queryObj["DisplayName"]?.ToString() ?? queryObj["Name"]?.ToString() ?? "Unknown";
                        string path = queryObj["PathName"]?.ToString() ?? "";
                        items.Add(new StartupItem { Name = name, Path = path, Source = "Windows Service (Auto)", Type = "Service", IsEnabled = true });
                    }
                }
            }
            catch (Exception ex) { HardwareLogger.LogError("GetAutomaticServices failed", ex); }
            return items;
        }

        public static List<StartupItem> GetRegistryAutoruns()
        {
            var items = new List<StartupItem>();
            string[] autorunKeys = new string[]
            {
                @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
                @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run",
                @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon\Userinit",
                @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon\Shell"
            };

            foreach (string path in autorunKeys)
            {
                try
                {
                    using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(path, false))
                    {
                        if (key != null)
                        {
                            foreach (string valName in key.GetValueNames())
                            {
                                items.Add(new StartupItem { Name = valName, Path = key.GetValue(valName)?.ToString() ?? "", Source = @"HKLM\" + path, Type = "Autorun", IsEnabled = true });
                            }
                        }
                    }
                }
                catch (Exception ex) { HardwareLogger.LogError($"GetRegistryAutoruns HKLM failed for {path}", ex); }
                try
                {
                    using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(path, false))
                    {
                        if (key != null)
                        {
                            foreach (string valName in key.GetValueNames())
                            {
                                items.Add(new StartupItem { Name = valName, Path = key.GetValue(valName)?.ToString() ?? "", Source = @"HKCU\" + path, Type = "Autorun", IsEnabled = true });
                            }
                        }
                    }
                }
                catch (Exception ex) { HardwareLogger.LogError($"GetRegistryAutoruns HKCU failed for {path}", ex); }
            }
            return items;
        }
    }
}


