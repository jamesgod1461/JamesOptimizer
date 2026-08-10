using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Diagnostics;
using LibreHardwareMonitor.Hardware;
using System.Net.NetworkInformation;
using System.IO;

namespace JamesOptimizer
{
    internal static class HardwareLogger
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JamesOptimizer", "hardware.log");

        static HardwareLogger()
        {
            try
            {
                var dir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
            catch { }
        }

        public static void Log(string message)
        {
            try
            {
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch { }
        }

        public static void LogError(string message, Exception ex)
        {
            Log($"ERROR: {message} - {ex.Message}");
        }
    }
    public class GpuInfo : System.ComponentModel.INotifyPropertyChanged
    {
        private string _name = "Unknown GPU";
        public string Name { get => _name; set { _name = value; OnPropertyChanged(nameof(Name)); } }
        
        private float _usage;
        public float Usage { get => _usage; set { _usage = value; OnPropertyChanged(nameof(Usage)); } }
        
        private float _vramUsed;
        public float VramUsed { get => _vramUsed; set { _vramUsed = value; OnPropertyChanged(nameof(VramUsed)); } }
        
        private float _wattage;
        public float Wattage { get => _wattage; set { _wattage = value; OnPropertyChanged(nameof(Wattage)); } }
        
        private float _temperature;
        public float Temperature { get => _temperature; set { _temperature = value; OnPropertyChanged(nameof(Temperature)); } }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }

    public class HardwareMonitorService : IDisposable
    {
        private Computer computer;
        public Computer Computer => computer;
        private PerformanceCounter? perfCounter;
        private float baseClockGhz = 0;

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }


        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

public HardwareMonitorService()
        {
            computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = false, // We use native memory calculation now
                IsMotherboardEnabled = true,
                IsStorageEnabled = true,
                IsNetworkEnabled = true
            };
            try { computer.Open(); }
            catch (Exception ex) { HardwareLogger.LogError("Computer.Open failed", ex); }

            try
            {
                perfCounter = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total");
                perfCounter.NextValue();
            }
            catch (Exception ex) { HardwareLogger.LogError("PerformanceCounter init failed", ex); }

            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("select MaxClockSpeed from Win32_Processor"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        baseClockGhz = Convert.ToSingle(obj["MaxClockSpeed"]) / 1000f;
                        break;
                    }
                }
            }
            catch (Exception ex) { HardwareLogger.LogError("MaxClockSpeed query failed", ex); baseClockGhz = 2.1f; }
        }

        private bool loggedOnce = false;

        public void Update()
        {
            try
            {
                computer.Accept(new UpdateVisitor());
                
                if (!loggedOnce)
                {
                    loggedOnce = true;
                    try
                    {
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine("CPU Sensors:");
                        var c = computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
                        if (c != null) {
                            foreach (var s in c.Sensors) sb.AppendLine($"[{s.SensorType}] {s.Name}: {s.Value}");
                        }
                        sb.AppendLine("\nMobo Sensors:");
                        var m = computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Motherboard);
                        if (m != null) {
                            foreach (var sub in m.SubHardware) {
                                sb.AppendLine($"Sub: {sub.Name}");
                                foreach (var s in sub.Sensors) sb.AppendLine($"[{s.SensorType}] {s.Name}: {s.Value}");
                            }
                        }
                        System.IO.File.WriteAllText(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "SensorsLog.txt"), sb.ToString());
                    }
                    catch (Exception ex) { HardwareLogger.LogError("Sensor logging failed", ex); }
                }
            }
            catch (Exception ex) { HardwareLogger.LogError("HardwareMonitorService.Update failed", ex); }
        }

        // --- HARDWARE NAMES ---
        public string GetCpuName()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("select Name from Win32_Processor");
                foreach (ManagementObject obj in searcher.Get())
                {
                    return obj["Name"]?.ToString()?.Trim() ?? "Unknown CPU";
                }
            }
            catch (Exception ex)
            {
                HardwareLogger.LogError("GetCpuName failed", ex);
            }
            return "Unknown CPU";
        }
        public string GetMotherboardName() => computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Motherboard)?.Name ?? "Unknown Motherboard";

        // --- NATIVE CPU CLOCK (USER-MODE) ---
        public float GetCpuClock()
        {
            try
            {
                if (perfCounter != null && baseClockGhz > 0)
                {
                    float performancePercent = perfCounter.NextValue();
                    if (performancePercent > 0)
                    {
                        return baseClockGhz * (performancePercent / 100f);
                    }
                }
            }
            catch (Exception ex) { HardwareLogger.LogError("GetCpuClock perfCounter failed", ex); }

            // Fallback to LibreHardwareMonitor if PerformanceCounters fail
            var cpu = computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
            if (cpu == null) return baseClockGhz;
            var clockSensors = cpu.Sensors.Where(s => s.SensorType == SensorType.Clock && s.Name.IndexOf("Bus", StringComparison.OrdinalIgnoreCase) < 0).ToList();
            if (clockSensors.Any())
            {
                return clockSensors.Max(s => s.Value ?? 0) / 1000f;
            }
            return baseClockGhz;
        }

        // --- NATIVE RAM (USER-MODE) ---
        public float GetRamUsed()
        {
            MEMORYSTATUSEX memStatus = new MEMORYSTATUSEX();
            memStatus.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (GlobalMemoryStatusEx(ref memStatus))
            {
                return (memStatus.ullTotalPhys - memStatus.ullAvailPhys) / (1024f * 1024f * 1024f);
            }
            return 0;
        }

        public float GetRamAvailable()
        {
            MEMORYSTATUSEX memStatus = new MEMORYSTATUSEX();
            memStatus.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (GlobalMemoryStatusEx(ref memStatus))
            {
                return memStatus.ullAvailPhys / (1024f * 1024f * 1024f);
            }
            return 0;
        }

        // --- CPU & GPU LOAD (LIBRE) ---
        public float GetCpuUsage()
        {
            var cpu = computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
            if (cpu == null) return 0;
            var sensor = cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name == "CPU Total");
            return sensor?.Value ?? 0;
        }

        public float GetCpuWattage()
        {
            var cpu = computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
            if (cpu == null) return EstimateCpuWattage();
            
            var powerSensors = cpu.Sensors.Where(s => s.SensorType == SensorType.Power).ToList();
            var packagePower = powerSensors.FirstOrDefault(s => s.Name.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0);
            
            if (packagePower != null && (packagePower.Value ?? 0) > 0) return packagePower.Value ?? 0;
            if (powerSensors.Any(s => (s.Value ?? 0) > 0)) return powerSensors.Where(s => (s.Value ?? 0) > 0).First().Value ?? 0;
            
            // Fallback: estimate when WinRing0 can't load (Core Isolation blocks MSR access)
            return EstimateCpuWattage();
        }

        private float EstimateCpuWattage()
        {
            float cpuLoad = GetCpuUsage();
            float clockGhz = GetCpuClock();
            
            int coreCount = 8;
            string cpuName = "";
            try
            {
                using (var searcher = new ManagementObjectSearcher("select Name, NumberOfCores from Win32_Processor"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        if (obj["Name"] != null) cpuName = obj["Name"]?.ToString() ?? "";
                        if (obj["NumberOfCores"] != null) coreCount = Convert.ToInt32(obj["NumberOfCores"]);
                        break;
                    }
                }
            }
            catch (Exception ex) { HardwareLogger.LogError("EstimateCpuWattage WMI query failed", ex); }

            bool isMobile = cpuName.Contains(" U", StringComparison.OrdinalIgnoreCase) || 
                            cpuName.Contains("-U", StringComparison.OrdinalIgnoreCase) ||
                            cpuName.Contains(" H", StringComparison.OrdinalIgnoreCase) ||
                            cpuName.Contains("-H", StringComparison.OrdinalIgnoreCase) ||
                            cpuName.Contains("Mobile", StringComparison.OrdinalIgnoreCase);

            float idlePower = isMobile ? 3f : 10f;
            float perCoreMaxPower = isMobile ? 4f : 12f;
            
            // Adjust for modern high-power architectures if it's a massive CPU
            if (coreCount > 16) perCoreMaxPower = 15f; 

            float clockFactor = baseClockGhz > 0 ? Math.Max(clockGhz / baseClockGhz, 1f) : 1f;
            float estimatedMax = idlePower + (perCoreMaxPower * coreCount * clockFactor);
            
            float tdpCap = isMobile ? 80f : 350f;
            if (estimatedMax > tdpCap) estimatedMax = tdpCap;

            float estimated = idlePower + ((estimatedMax - idlePower) * (cpuLoad / 100f));
            
            return Math.Max(estimated, idlePower);
        }

        public List<GpuInfo> GetGpus()
        {
            var list = new List<GpuInfo>();
            var wmiGpuNames = new List<string>();

            // 1. Get all GPUs from WMI to ensure we don't miss sleeping/integrated GPUs
            try
            {
                using (var searcher = new ManagementObjectSearcher("select Name from Win32_VideoController"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        if (obj["Name"] != null)
                        {
                            wmiGpuNames.Add(obj["Name"]?.ToString() ?? "");
                        }
                    }
                }
                HardwareLogger.Log($"WMI GPUS: {string.Join(", ", wmiGpuNames)}");
            }
            catch (Exception ex) { HardwareLogger.LogError("WMI GPU query failed", ex); }

            // 2. Get LHM GPUs
            var lhmGpus = computer.Hardware.Where(h => h.HardwareType == HardwareType.GpuNvidia || h.HardwareType == HardwareType.GpuAmd || h.HardwareType == HardwareType.GpuIntel).ToList();

            // 3. Merge them
            foreach (var wmiName in wmiGpuNames.Distinct())
            {
                var lhmGpu = lhmGpus.FirstOrDefault(g => g.Name.IndexOf(wmiName, StringComparison.OrdinalIgnoreCase) >= 0 || wmiName.IndexOf(g.Name, StringComparison.OrdinalIgnoreCase) >= 0);
                
                if (lhmGpu != null)
                {
                    var info = new GpuInfo { Name = lhmGpu.Name };
                    var loadSensor = lhmGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name == "GPU Core");
                    info.Usage = loadSensor?.Value ?? 0;
                    
                    var vramSensor = lhmGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && (s.Name == "GPU Memory Used" || s.Name == "Memory Used" || s.Name == "D3D Dedicated Memory Used"));
                    info.VramUsed = (vramSensor?.Value ?? 0) / 1024f;
                    
                    var powerSensors = lhmGpu.Sensors.Where(s => s.SensorType == SensorType.Power).ToList();
                    info.Wattage = powerSensors.FirstOrDefault()?.Value ?? 0;
                    
                    var tempSensor = lhmGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name.IndexOf("GPU Core", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (tempSensor == null) tempSensor = lhmGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
                    info.Temperature = tempSensor?.Value ?? 0;

                    list.Add(info);
                    lhmGpus.Remove(lhmGpu); // Remove from list so we don't duplicate
                }
                else
                {
                    // WMI found it but LHM didn't
                    list.Add(new GpuInfo { Name = wmiName, Usage = 0, Temperature = 0, VramUsed = 0, Wattage = 0 });
                }
            }

            // 4. Add any remaining LHM GPUs that WMI somehow missed
            foreach (var lhmGpu in lhmGpus)
            {
                var info = new GpuInfo { Name = lhmGpu.Name };
                var loadSensor = lhmGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name == "GPU Core");
                info.Usage = loadSensor?.Value ?? 0;
                
                var vramSensor = lhmGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && (s.Name == "GPU Memory Used" || s.Name == "Memory Used" || s.Name == "D3D Dedicated Memory Used"));
                info.VramUsed = (vramSensor?.Value ?? 0) / 1024f;
                
                var powerSensors = lhmGpu.Sensors.Where(s => s.SensorType == SensorType.Power).ToList();
                info.Wattage = powerSensors.FirstOrDefault()?.Value ?? 0;
                
                var tempSensor = lhmGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name.IndexOf("GPU Core", StringComparison.OrdinalIgnoreCase) >= 0);
                if (tempSensor == null) tempSensor = lhmGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
                info.Temperature = tempSensor?.Value ?? 0;

                list.Add(info);
            }

            if (list.Count == 0)
            {
                list.Add(new GpuInfo { Name = "No GPU Detected" });
            }
            HardwareLogger.Log($"FINAL GPUS: {string.Join(", ", list.Select(g => g.Name))}");
            return list;
        }

        // --- TEMPERATURES ---
        public float GetCpuTemp()
        {
            try
            {
                var cpu = computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
                if (cpu != null)
                {
                    // First try to average all Core temperatures
                    var coreTemps = cpu.Sensors.Where(s => s.SensorType == SensorType.Temperature && s.Name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0 && s.Value.HasValue && s.Value.Value > 0).ToList();
                    if (coreTemps.Any()) return coreTemps.Average(s => s.Value ?? 0f);

                    // If no "Core" sensors, try "Package" or any available CPU temperature
                    var tempSensor = cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Value.HasValue && s.Value.Value > 0);
                    if (tempSensor != null && tempSensor.Value.HasValue) return tempSensor.Value.Value;
                }

                // Fallback 1: Motherboard sensors (SuperIO)
                var mobo = computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Motherboard);
                if (mobo != null)
                {
                    foreach (var sub in mobo.SubHardware)
                    {
                        var moboSensor = sub.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) >= 0);
                        if (moboSensor != null && moboSensor.Value.HasValue && moboSensor.Value.Value > 0) return moboSensor.Value.Value;
                    }
                }
            }
            catch (Exception ex) { HardwareLogger.LogError("GetCpuTemp failed", ex); }

            // Fallback 2: Estimation based on load (Core Isolation workaround)
            float load = GetCpuUsage();
            float estimatedTemp = 38f + (load / 100f) * 45f;
            return estimatedTemp;
        }

        // --- NATIVE PROCESS COUNT ---
        public int GetProcessCount()
        {
            try
            {
                return Process.GetProcesses().Length;
            }
            catch { return 0; }
        }

        // --- STORAGE & NETWORK ---
        public float GetDiskLoad(int diskIndex)
        {
            var storages = computer.Hardware.Where(h => h.HardwareType == HardwareType.Storage).ToList();
            if (storages.Count > diskIndex)
            {
                var sensor = storages[diskIndex].Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Contains("Total"));
                if (sensor == null) sensor = storages[diskIndex].Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load);
                return sensor?.Value ?? 0;
            }
            return 0;
        }

        public float GetDiskReadRate(int diskIndex)
        {
            var storages = computer.Hardware.Where(h => h.HardwareType == HardwareType.Storage).ToList();
            if (storages.Count > diskIndex)
            {
                var sensor = storages[diskIndex].Sensors.FirstOrDefault(s => s.SensorType == SensorType.Throughput && s.Name.Contains("Read"));
                return sensor?.Value ?? 0;
            }
            return 0;
        }

        public float GetDiskWriteRate(int diskIndex)
        {
            var storages = computer.Hardware.Where(h => h.HardwareType == HardwareType.Storage).ToList();
            if (storages.Count > diskIndex)
            {
                var sensor = storages[diskIndex].Sensors.FirstOrDefault(s => s.SensorType == SensorType.Throughput && s.Name.Contains("Write"));
                return sensor?.Value ?? 0;
            }
            return 0;
        }

        public string GetDiskName(int diskIndex)
        {
            var storages = computer.Hardware.Where(h => h.HardwareType == HardwareType.Storage).ToList();
            if (storages.Count > diskIndex) return storages[diskIndex].Name;
            return "Disk " + diskIndex;
        }

        private System.Collections.Generic.Dictionary<string, long> lastBytesReceived = new System.Collections.Generic.Dictionary<string, long>();
        private System.Collections.Generic.Dictionary<string, long> lastBytesSent = new System.Collections.Generic.Dictionary<string, long>();
        private DateTime lastNetworkCheckTime = DateTime.MinValue;
        private float currentDownloadRate = 0;
        private float currentUploadRate = 0;

        private void UpdateNetworkStats()
        {
            var now = DateTime.UtcNow;
            var timeDiff = (now - lastNetworkCheckTime).TotalSeconds;

            if (timeDiff >= 0.5) // Update at most twice a second
            {
                float maxDown = 0;
                float maxUp = 0;

                try
                {
                    var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                    foreach (var ni in interfaces)
                    {
                        if (ni.OperationalStatus == OperationalStatus.Up &&
                            ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                            ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                        {
                            var stats = ni.GetIPStatistics();
                            string id = ni.Id;

                            if (lastBytesReceived.ContainsKey(id) && lastNetworkCheckTime != DateTime.MinValue && timeDiff > 0)
                            {
                                float down = (float)((stats.BytesReceived - lastBytesReceived[id]) / timeDiff);
                                float up = (float)((stats.BytesSent - lastBytesSent[id]) / timeDiff);
                                
                                if (down > maxDown) maxDown = down;
                                if (up > maxUp) maxUp = up;
                            }

                            lastBytesReceived[id] = stats.BytesReceived;
                            lastBytesSent[id] = stats.BytesSent;
                        }
                    }
                }
                catch (Exception ex) { HardwareLogger.LogError("UpdateNetworkStats failed", ex); }

                if (lastNetworkCheckTime != DateTime.MinValue && timeDiff > 0)
                {
                    currentDownloadRate = maxDown;
                    currentUploadRate = maxUp;
                }

                lastNetworkCheckTime = now;
            }
        }

        public float GetNetworkDownloadRate()
        {
            UpdateNetworkStats();
            return Math.Max(0, currentDownloadRate);
        }

        public float GetNetworkUploadRate()
        {
            UpdateNetworkStats();
            return Math.Max(0, currentUploadRate);
        }

        private long currentPing = -1;
        private DateTime lastPingTime = DateTime.MinValue;
        private Ping pingSender = new Ping();

        public long GetPing()
        {
            var now = DateTime.UtcNow;
            if ((now - lastPingTime).TotalSeconds >= 1.0)
            {
                lastPingTime = now;
                try
                {
                    pingSender.SendPingAsync("8.8.8.8", 900).ContinueWith(t =>
                    {
                        try
                        {
                            if (t.IsCompletedSuccessfully && t.Result.Status == IPStatus.Success)
                            {
                                currentPing = t.Result.RoundtripTime;
                            }
                            else
                            {
                                currentPing = -1;
                            }
                        }
                        catch (Exception ex) { HardwareLogger.LogError("Ping callback failed", ex); currentPing = -1; }
                    });
                }
                catch (Exception ex) { HardwareLogger.LogError("GetPing SendPingAsync failed", ex); currentPing = -1; }
            }
            return currentPing;
        }

        public void Dispose()
        {
            try { computer?.Close(); }
            catch (Exception ex) { HardwareLogger.LogError("Dispose computer.Close failed", ex); }
            try { perfCounter?.Dispose(); }
            catch (Exception ex) { HardwareLogger.LogError("Dispose perfCounter failed", ex); }
            try { pingSender?.Dispose(); }
            catch (Exception ex) { HardwareLogger.LogError("Dispose pingSender failed", ex); }
        }
    }

    public class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) { computer.Traverse(this); }
        public void VisitHardware(IHardware hardware) { hardware.Update(); foreach (var subHardware in hardware.SubHardware) subHardware.Accept(this); }
        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }
}
