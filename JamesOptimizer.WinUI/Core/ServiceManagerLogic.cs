using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.Versioning;
using System.ServiceProcess;
using JamesOptimizer.Core;

namespace JamesOptimizer
{
    [SupportedOSPlatform("windows")]
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
        public bool IsProtected => CoreConstants.IsProtectedService(ServiceName);

        public void ResolvePublisher()
        {
            if (string.IsNullOrWhiteSpace(PathName)) return;
            try
            {
                string exePath = ExtractExecutablePath(PathName);
                if (!string.IsNullOrEmpty(exePath))
                    Publisher = NativeSystemHelper.ResolvePublisherLabel(exePath);
            }
            catch (Exception ex)
            {
                HardwareLogger.LogError("ResolvePublisher failed", ex);
            }
        }

        internal static string ExtractExecutablePath(string rawPath)
        {
            string exePath;
            if (rawPath.StartsWith('"'))
            {
                int endQuote = rawPath.IndexOf('"', 1);
                exePath = endQuote > 0 ? rawPath[1..endQuote] : rawPath;
            }
            else
            {
                int exeIndex = rawPath.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                exePath = exeIndex > 0 ? rawPath[..(exeIndex + 4)] : rawPath;
            }

            exePath = exePath.Trim();
            if (exePath.StartsWith(@"\??\", StringComparison.Ordinal)) exePath = exePath[4..];
            if (exePath.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
                exePath = exePath.Replace(@"\SystemRoot\", @"%SystemRoot%\", StringComparison.OrdinalIgnoreCase);
            return Environment.ExpandEnvironmentVariables(exePath);
        }
    }

    [SupportedOSPlatform("windows")]
    public static class ServiceManagerLogic
    {
        public static List<ServiceInfo> GetAllServices()
        {
            var result = new List<ServiceInfo>();
            try
            {
                var services = ServiceController.GetServices();
                var startTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var pathNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT Name, StartMode, PathName FROM Win32_Service");
                    using var results = searcher.Get();
                    foreach (ManagementObject queryObj in results)
                    using (queryObj)
                    {
                        string name = queryObj["Name"]?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(name))
                        {
                            startTypes[name] = queryObj["StartMode"]?.ToString() ?? "Unknown";
                            pathNames[name] = queryObj["PathName"]?.ToString() ?? "";
                        }
                    }
                }
                catch (Exception ex) { HardwareLogger.LogError("WMI service query failed", ex); }

                foreach (var sc in services)
                {
                    startTypes.TryGetValue(sc.ServiceName, out string? startType);
                    pathNames.TryGetValue(sc.ServiceName, out string? pathName);

                    var info = new ServiceInfo
                    {
                        ServiceName = sc.ServiceName,
                        DisplayName = sc.DisplayName,
                        Status = sc.Status.ToString(),
                        StartType = startType ?? "Unknown",
                        PathName = pathName ?? "",
                        CanStop = sc.CanStop && !CoreConstants.IsProtectedService(sc.ServiceName)
                    };
                    info.ResolvePublisher();
                    result.Add(info);
                    sc.Dispose();
                }
            }
            catch (Exception ex) { HardwareLogger.LogError("GetAllServices failed", ex); }

            return result.OrderBy(s => s.DisplayName).ToList();
        }

        public static bool ToggleServiceState(string serviceName, bool start)
        {
            if (!start && CoreConstants.IsProtectedService(serviceName))
            {
                HardwareLogger.Log($"Blocked attempt to stop protected service '{serviceName}'.");
                return false;
            }

            try
            {
                using var sc = new ServiceController(serviceName);
                if (start && sc.Status == ServiceControllerStatus.Stopped)
                {
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(5));
                    return sc.Status == ServiceControllerStatus.Running;
                }
                if (!start && sc.CanStop && sc.Status == ServiceControllerStatus.Running)
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(5));
                    return sc.Status == ServiceControllerStatus.Stopped;
                }
                return (start && sc.Status == ServiceControllerStatus.Running)
                    || (!start && sc.Status == ServiceControllerStatus.Stopped);
            }
            catch (Exception ex)
            {
                HardwareLogger.LogError($"ToggleServiceState failed for {serviceName}", ex);
                return false;
            }
        }

        public static bool StartService(string serviceName) => ToggleServiceState(serviceName, true);
        public static bool StopService(string serviceName) => ToggleServiceState(serviceName, false);

        public static void ChangeStartType(string serviceName, string startMode)
        {
            if (CoreConstants.IsProtectedService(serviceName))
            {
                HardwareLogger.Log($"Blocked attempt to reconfigure protected service '{serviceName}'.");
                return;
            }

            try
            {
                TweakBackupService.SetServiceStartMode(serviceName, startMode);
            }
            catch (Exception ex)
            {
                HardwareLogger.LogError($"ChangeStartType failed for {serviceName}", ex);
            }
        }
    }
}
