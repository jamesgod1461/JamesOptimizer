using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace JamesOptimizer.Core;

public sealed record DiskHealthInfo(
    string Model,
    string StatusText,
    bool? PredictFailure,
    int? TemperatureC,
    int? WearPercent,
    int? AvailableSparePercent,
    bool IsSupported);

public sealed record BatteryHealthInfo(
    bool HasBattery,
    int DesignCapacityMwh,
    int FullChargedCapacityMwh,
    double HealthPercent,
    int? CycleCount,
    string Summary);

[SupportedOSPlatform("windows")]
public static class HardwareHealthService
{
    public static List<DiskHealthInfo> GetDiskHealth()
    {
        var list = new List<DiskHealthInfo>();
        var predictMap = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var wmiSmart = new ManagementObjectSearcher(@"root\WMI", "SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus");
            using var smartResults = wmiSmart.Get();
            foreach (ManagementBaseObject obj in smartResults)
            using (obj)
            {
                string inst = obj["InstanceName"]?.ToString() ?? "";
                bool pf = obj["PredictFailure"] is bool b && b;
                if (!string.IsNullOrEmpty(inst)) predictMap[inst] = pf;
            }
        }
        catch { }

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Index, Model, Status, PNPDeviceID, MediaType FROM Win32_DiskDrive");
            using var diskResults = searcher.Get();
            foreach (ManagementBaseObject obj in diskResults)
            using (obj)
            {
                int index = Convert.ToInt32(obj["Index"] ?? 0);
                string model = obj["Model"]?.ToString()?.Trim() ?? $"PhysicalDrive{index}";
                string status = obj["Status"]?.ToString() ?? "Unknown";
                string pnp = obj["PNPDeviceID"]?.ToString() ?? "";

                bool? predictFail = null;
                foreach (var kv in predictMap)
                {
                    if (!string.IsNullOrEmpty(pnp) && kv.Key.Contains(pnp, StringComparison.OrdinalIgnoreCase))
                    {
                        predictFail = kv.Value;
                        break;
                    }
                }

                var (tempC, wearPct, sparePct, nvmeOk) = TryQueryNvmeHealth(index);
                string display = predictFail == true
                    ? "WARNING · S.M.A.R.T. PredictFailure"
                    : nvmeOk
                        ? $"Healthy · {tempC}°C · {100 - (wearPct ?? 0)}% Life"
                        : status.Equals("OK", StringComparison.OrdinalIgnoreCase)
                            ? (predictFail.HasValue ? "Healthy (S.M.A.R.T. OK)" : "OK (Controller S.M.A.R.T. unavailable)")
                            : status;

                list.Add(new DiskHealthInfo(model, display, predictFail, tempC, wearPct, sparePct, predictFail.HasValue || nvmeOk));
            }
        }
        catch { }

        if (list.Count == 0)
            list.Add(new DiskHealthInfo("Primary Storage", "Unavailable (controller did not expose health)", null, null, null, null, false));

        return list;
    }

    private static (int? TempC, int? WearPct, int? SparePct, bool Ok) TryQueryNvmeHealth(int driveIndex)
    {
        string path = $@"\\.\PhysicalDrive{driveIndex}";
        IntPtr handle = CreateFileW(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            return (null, null, null, false);

        try
        {
            var query = new STORAGE_PROPERTY_QUERY
            {
                PropertyId = 50, // StorageDeviceProtocolSpecificProperty
                QueryType = 0    // PropertyStandardQuery
            };
            var proto = new STORAGE_PROTOCOL_SPECIFIC_DATA
            {
                ProtocolType = 3, // ProtocolTypeNvme
                DataType = 2,     // NVMeDataTypeLogPage
                ProtocolDataRequestValue = 2, // SMART / Health Info Log
                ProtocolDataRequestSubValue = 0,
                ProtocolDataOffset = (uint)Marshal.SizeOf<STORAGE_PROTOCOL_SPECIFIC_DATA>(),
                ProtocolDataLength = 512
            };

            int headerSize = Marshal.SizeOf<STORAGE_PROPERTY_QUERY>() - 4 + Marshal.SizeOf<STORAGE_PROTOCOL_SPECIFIC_DATA>();
            int totalSize = headerSize + 512;
            IntPtr buffer = Marshal.AllocHGlobal(totalSize);
            try
            {
                for (int i = 0; i < totalSize; i++) Marshal.WriteByte(buffer, i, 0);
                Marshal.WriteInt32(buffer, 0, query.PropertyId);
                Marshal.WriteInt32(buffer, 4, query.QueryType);
                Marshal.StructureToPtr(proto, IntPtr.Add(buffer, 8), false);

                if (DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY, buffer, (uint)totalSize, buffer, (uint)totalSize, out uint returned, IntPtr.Zero) &&
                    returned >= (uint)(Marshal.SizeOf<STORAGE_PROTOCOL_DATA_DESCRIPTOR>() + 16))
                {
                    // BUG FIX: STORAGE_PROTOCOL_DATA_DESCRIPTOR starts at offset 0 of the output buffer.
                    // The NVMe log data follows immediately after the descriptor (at offset 48, not 56).
                    int logOffset = Marshal.SizeOf<STORAGE_PROTOCOL_DATA_DESCRIPTOR>();
                    // NVMe SMART Log: bytes 1..2 = Composite Temp (Kelvin), byte 3 = Available Spare %, byte 5 = Percentage Used
                    ushort tempK = (ushort)(Marshal.ReadByte(buffer, logOffset + 1) | (Marshal.ReadByte(buffer, logOffset + 2) << 8));
                    int tempC = tempK > 200 && tempK < 420 ? tempK - 273 : 0;
                    int spare = Marshal.ReadByte(buffer, logOffset + 3);
                    int wear = Marshal.ReadByte(buffer, logOffset + 5);
                    if (tempC > 0)
                        return (tempC, wear, spare, true);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch { }
        finally
        {
            CloseHandle(handle);
        }

        return (null, null, null, false);
    }

    public static BatteryHealthInfo GetBatteryHealth()
    {
        if (!OsEnvironment.Detect().HasBattery)
            return new BatteryHealthInfo(false, 0, 0, 100, null, "Desktop / AC-Only (No Battery)");

        int design = 0, full = 0;
        int? cycles = null;

        try
        {
            using var s1 = new ManagementObjectSearcher(@"root\WMI", "SELECT DesignedCapacity FROM BatteryStaticData");
            using var c1 = s1.Get();
            foreach (ManagementBaseObject o in c1)
            using (o)
            {
                design = Convert.ToInt32(o["DesignedCapacity"] ?? 0);
                break;
            }
        }
        catch { }

        try
        {
            using var s2 = new ManagementObjectSearcher(@"root\WMI", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity");
            using var c2 = s2.Get();
            foreach (ManagementBaseObject o in c2)
            using (o)
            {
                full = Convert.ToInt32(o["FullChargedCapacity"] ?? 0);
                break;
            }
        }
        catch { }

        try
        {
            using var s3 = new ManagementObjectSearcher(@"root\WMI", "SELECT CycleCount FROM BatteryCycleCount");
            using var c3 = s3.Get();
            foreach (ManagementBaseObject o in c3)
            using (o)
            {
                int c = Convert.ToInt32(o["CycleCount"] ?? 0);
                if (c > 0) cycles = c;
                break;
            }
        }
        catch { }

        if (design <= 0 || full <= 0)
            return new BatteryHealthInfo(true, design, full, 0, cycles, "Unavailable (firmware did not expose battery capacity)");

        double pct = Math.Clamp((full / (double)design) * 100.0, 0, 110);
        string cycleText = cycles.HasValue ? $" · {cycles.Value} cycles" : "";
        return new BatteryHealthInfo(true, design, full, pct, cycles, $"{pct:F1}% ({full:N0} / {design:N0} mWh{cycleText})");
    }

    public static bool IsRebootPending()
    {
        try
        {
            using var cbs = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending", false);
            if (cbs != null) return true;
            using var wu = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired", false);
            if (wu != null) return true;
            object? pfro = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager", "PendingFileRenameOperations", null);
            if (pfro is string[] arr && arr.Length > 0) return true;
        }
        catch { }
        return false;
    }

    #region Native Storage IOCTL

    private const uint FILE_SHARE_READ = 1;
    private const uint FILE_SHARE_WRITE = 2;
    private const uint OPEN_EXISTING = 3;
    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;

    [StructLayout(LayoutKind.Sequential)]
    private struct STORAGE_PROPERTY_QUERY
    {
        public int PropertyId;
        public int QueryType;
        public int AdditionalParameters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STORAGE_PROTOCOL_SPECIFIC_DATA
    {
        public int ProtocolType;
        public int DataType;
        public uint ProtocolDataRequestValue;
        public uint ProtocolDataRequestSubValue;
        public uint ProtocolDataOffset;
        public uint ProtocolDataLength;
        public uint FixedProtocolReturnData;
        public uint ProtocolDataRequestSubValue2;
        public uint ProtocolDataRequestSubValue3;
        public uint ProtocolDataRequestSubValue4;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STORAGE_PROTOCOL_DATA_DESCRIPTOR
    {
        public uint Version;
        public uint Size;
        public STORAGE_PROTOCOL_SPECIFIC_DATA ProtocolSpecificData;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, IntPtr lpInBuffer, uint nInBufferSize, IntPtr lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    #endregion
}
