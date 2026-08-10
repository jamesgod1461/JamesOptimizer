$file = "c:\Users\adity\source\repos\optimizer\OptimizerUI\ServiceManagerLogic.cs"
$content = Get-Content $file -Raw

$search1 = @"
    public class ServiceInfo
    {
        public string ServiceName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Status { get; set; } = "";
        public string StartType { get; set; } = "";
        public bool IsRunning => Status == "Running";
        public bool CanStop { get; set; }
    }
"@

$replace1 = @"
    public class ServiceInfo
    {
        public string ServiceName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Status { get; set; } = "";
        public string StartType { get; set; } = "";
        public string PathName { get; set; } = "";
        public string Publisher { get; set; } = "";
        public bool IsRunning => Status == "Running";
        public bool CanStop { get; set; }

        public void ResolvePublisher()
        {
            if (string.IsNullOrWhiteSpace(PathName)) return;
            try
            {
                string exePath = "";
                if (PathName.StartsWith("`""))
                {
                    int endQuote = PathName.IndexOf("`"", 1);
                    if (endQuote > 0)
                        exePath = PathName.Substring(1, endQuote - 1);
                }
                else
                {
                    int exeIndex = PathName.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                    if (exeIndex > 0)
                        exePath = PathName.Substring(0, exeIndex + 4);
                    else
                        exePath = PathName;
                }
                
                exePath = exePath.Trim();
                
                if (exePath.StartsWith(@"\??\")) exePath = exePath.Substring(4);
                if (exePath.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase)) 
                    exePath = exePath.Replace(@"\SystemRoot\", @"%SystemRoot%\");
                
                exePath = Environment.ExpandEnvironmentVariables(exePath);

                if (System.IO.File.Exists(exePath))
                {
                    var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(exePath);
                    Publisher = versionInfo.CompanyName ?? "";
                }
            }
            catch { }
        }
    }
"@

$content = $content.Replace($search1, $replace1)

$search2 = @"
                // We can use WMI to get Startup Type quickly
                Dictionary<string, string> startTypes = new Dictionary<string, string>();
                try
                {
                    using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name, StartMode FROM Win32_Service"))
                    {
                        foreach (ManagementObject queryObj in searcher.Get())
                        {
                            string name = queryObj["Name"]?.ToString() ?? "";
                            string startMode = queryObj["StartMode"]?.ToString() ?? "Unknown";
                            if (!string.IsNullOrEmpty(name))
                            {
                                startTypes[name] = startMode;
                            }
                        }
                    }
                }
                catch { }

                foreach (var sc in services)
                {
                    string startType = "Unknown";
                    if (startTypes.ContainsKey(sc.ServiceName))
                    {
                        startType = startTypes[sc.ServiceName];
                    }

                    result.Add(new ServiceInfo
                    {
                        ServiceName = sc.ServiceName,
                        DisplayName = sc.DisplayName,
                        Status = sc.Status.ToString(),
                        StartType = startType,
                        CanStop = sc.CanStop
                    });
                }
"@

$replace2 = @"
                // We can use WMI to get Startup Type quickly
                Dictionary<string, string> startTypes = new Dictionary<string, string>();
                Dictionary<string, string> pathNames = new Dictionary<string, string>();
                try
                {
                    using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name, StartMode, PathName FROM Win32_Service"))
                    {
                        foreach (ManagementObject queryObj in searcher.Get())
                        {
                            string name = queryObj["Name"]?.ToString() ?? "";
                            string startMode = queryObj["StartMode"]?.ToString() ?? "Unknown";
                            string pathName = queryObj["PathName"]?.ToString() ?? "";
                            if (!string.IsNullOrEmpty(name))
                            {
                                startTypes[name] = startMode;
                                pathNames[name] = pathName;
                            }
                        }
                    }
                }
                catch { }

                foreach (var sc in services)
                {
                    string startType = "Unknown";
                    string pathName = "";
                    if (startTypes.ContainsKey(sc.ServiceName))
                    {
                        startType = startTypes[sc.ServiceName];
                        pathName = pathNames[sc.ServiceName];
                    }

                    var info = new ServiceInfo
                    {
                        ServiceName = sc.ServiceName,
                        DisplayName = sc.DisplayName,
                        Status = sc.Status.ToString(),
                        StartType = startType,
                        PathName = pathName,
                        CanStop = sc.CanStop
                    };
                    info.ResolvePublisher();
                    result.Add(info);
                }
"@

$content = $content.Replace($search2, $replace2)
Set-Content $file $content
