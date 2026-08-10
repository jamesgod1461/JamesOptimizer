$file = "c:\Users\adity\source\repos\optimizer\OptimizerUI\StartupManagerService.cs"
$content = Get-Content $file -Raw

$search1 = @"
        public static List<StartupItem> GetStartupApps()
        {
            var items = new List<StartupItem>();
            try
            {
                // HKCU Run
                ReadRegistryKey(items, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", true);
                ReadRegistryKey(items, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run-Disabled", @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", false);

                // HKLM Run
                ReadRegistryKey(items, Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", @"HKLM\Software\Microsoft\Windows\CurrentVersion\Run", true);
                ReadRegistryKey(items, Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run-Disabled", @"HKLM\Software\Microsoft\Windows\CurrentVersion\Run", false);

                // Startup Folder
                string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (Directory.Exists(startupFolder))
                {
                    foreach (string file in Directory.GetFiles(startupFolder))
                    {
                        bool isEnabled = !file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                        string realFile = file;
                        string name = Path.GetFileNameWithoutExtension(file);
                        if (!isEnabled) name = Path.GetFileNameWithoutExtension(name); // remove inner extension if double

                        items.Add(new StartupItem { Name = name, Path = realFile, Source = startupFolder, Type = "App", IsEnabled = isEnabled });
                    }
                }
            }
            catch { }
            return items;
        }

        private static void ReadRegistryKey(List<StartupItem> items, RegistryKey root, string subKey, string sourceName, bool isEnabled)
        {
            using (RegistryKey? key = root.OpenSubKey(subKey, false))
            {
                if (key != null)
                {
                    foreach (string valName in key.GetValueNames())
                    {
                        items.Add(new StartupItem { Name = valName, Path = key.GetValue(valName)?.ToString() ?? "", Source = sourceName, Type = "App", IsEnabled = isEnabled });
                    }
                }
            }
        }

        public static void ToggleStartupItem(StartupItem item, bool enable)
        {
            try
            {
                if (item.Type == "App")
                {
                    if (item.Source.StartsWith("HKCU"))
                    {
                        MoveRegistryValue(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", item.Name, item.Path, enable);
                    }
                    else if (item.Source.StartsWith("HKLM"))
                    {
                        MoveRegistryValue(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", item.Name, item.Path, enable);
                    }
                    else // Startup Folder
                    {
                        string currentPath = item.Path;
                        if (enable && currentPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                        {
                            string newPath = currentPath.Substring(0, currentPath.Length - 9);
                            File.Move(currentPath, newPath);
                            item.Path = newPath;
                        }
                        else if (!enable && !currentPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                        {
                            string newPath = currentPath + ".disabled";
                            File.Move(currentPath, newPath);
                            item.Path = newPath;
                        }
                    }
                }
            }
            catch { }
        }

        private static void MoveRegistryValue(RegistryKey root, string baseKeyPath, string valueName, string valueData, bool enable)
        {
            string sourceKeyPath = enable ? baseKeyPath + "-Disabled" : baseKeyPath;
            string destKeyPath = enable ? baseKeyPath : baseKeyPath + "-Disabled";

            using (RegistryKey? sourceKey = root.OpenSubKey(sourceKeyPath, true))
            {
                if (sourceKey != null && sourceKey.GetValue(valueName) != null)
                {
                    sourceKey.DeleteValue(valueName);
                }
            }

            using (RegistryKey? destKey = root.CreateSubKey(destKeyPath))
            {
                if (destKey != null)
                {
                    destKey.SetValue(valueName, valueData);
                }
            }
        }
"@

$replace1 = @"
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
            catch { }
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
            catch { }
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
            catch { }
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
            catch { }
        }
"@

$content = $content.Replace($search1, $replace1)
Set-Content $file $content
