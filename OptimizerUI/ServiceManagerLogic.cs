using System;
using System.Collections.Generic;
using System.ServiceProcess;
using System.Linq;
using System.Management;

namespace JamesOptimizer
{
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
                if (PathName.StartsWith("\""))
                {
                    int endQuote = PathName.IndexOf("\"", 1);
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

    public static class ServiceManagerLogic
    {
        public static List<ServiceInfo> GetAllServices()
        {
            var result = new List<ServiceInfo>();
            try
            {
                var services = ServiceController.GetServices();
                
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
                catch (Exception ex) { HardwareLogger.LogError("WMI service query failed", ex); }

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
                    
                    // Dispose the ServiceController to prevent resource leaks
                    sc.Dispose();
                }
            }
            catch (Exception ex) { HardwareLogger.LogError("GetAllServices failed", ex); }

            return result.OrderBy(s => s.DisplayName).ToList();
        }

        public static void ToggleServiceState(string serviceName, bool start)
        {
            try
            {
                using (var sc = new ServiceController(serviceName))
                {
                    if (start && sc.Status == ServiceControllerStatus.Stopped)
                    {
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(5));
                    }
                    else if (!start && sc.CanStop && sc.Status == ServiceControllerStatus.Running)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(5));
                    }
                }
            }
            catch (Exception ex) { HardwareLogger.LogError($"ToggleServiceState failed for {serviceName}", ex); }
        }
    }
}


