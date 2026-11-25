using System;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;

namespace JamesOptimizer.Core;

public enum GpuVendor
{
    Unknown,
    Nvidia,
    Amd,
    Intel
}

public sealed class OsEnvironmentInfo
{
    public int BuildNumber { get; init; }
    public string DisplayVersion { get; init; } = "";
    public string EditionId { get; init; } = "";
    public string ProductName { get; init; } = "Windows";
    public Architecture Architecture { get; init; }
    public bool IsWindows11 => BuildNumber >= 22000;
    public bool IsHomeEdition => EditionId.Contains("Core", StringComparison.OrdinalIgnoreCase) || ProductName.Contains("Home", StringComparison.OrdinalIgnoreCase);
    public bool IsElevated { get; init; }
    public bool HasBattery { get; init; }
    public bool SupportsS3Sleep { get; init; }
    public bool SupportsModernStandby { get; init; }
    public GpuVendor PrimaryGpuVendor { get; init; }
}

[SupportedOSPlatform("windows")]
public static class OsEnvironment
{
    private static OsEnvironmentInfo? _cached;
    private static readonly object _sync = new();

    public static OsEnvironmentInfo Detect(bool forceRefresh = false)
    {
        lock (_sync)
        {
            if (_cached != null && !forceRefresh)
                return _cached;

            int build = Environment.OSVersion.Version.Build;
            string displayVer = "";
            string edition = "";
            string product = IsWin11FromBuild(build) ? "Windows 11" : "Windows 10";

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false);
                if (key != null)
                {
                    if (int.TryParse(key.GetValue("CurrentBuildNumber")?.ToString(), out int regBuild))
                        build = regBuild;
                    displayVer = key.GetValue("DisplayVersion")?.ToString() ?? "";
                    edition = key.GetValue("EditionID")?.ToString() ?? "";
                    product = key.GetValue("ProductName")?.ToString() ?? product;
                    if (build >= 22000 && product.Contains("Windows 10", StringComparison.OrdinalIgnoreCase))
                        product = product.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { }

            bool elevated = false;
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                elevated = principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { }

            bool hasBattery = CheckHasBattery();
            var (s3, s0ix) = ProbeSleepStates();
            GpuVendor gpu = DetectPrimaryGpuVendor();

            _cached = new OsEnvironmentInfo
            {
                BuildNumber = build,
                DisplayVersion = displayVer,
                EditionId = edition,
                ProductName = product,
                Architecture = RuntimeInformation.OSArchitecture,
                IsElevated = elevated,
                HasBattery = hasBattery,
                SupportsS3Sleep = s3,
                SupportsModernStandby = s0ix,
                PrimaryGpuVendor = gpu
            };
            return _cached;
        }
    }

    private static bool IsWin11FromBuild(int build) => build >= 22000;

    private static bool CheckHasBattery()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT BatteryStatus FROM Win32_Battery");
            using var results = searcher.Get();
            return results.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static (bool supportsS3, bool supportsModernStandby) ProbeSleepStates()
    {
        try
        {
            var res = NativeSystemHelper.RunSystem32ToolSync("powercfg.exe", new[] { "/a" }, 3000);
            string output = res.StdOut ?? "";
            bool hasAvailableSection = true;
            int unavailIdx = output.IndexOf("not available", StringComparison.OrdinalIgnoreCase);
            string availPart = unavailIdx > 0 ? output[..unavailIdx] : output;
            bool s3 = hasAvailableSection && availPart.Contains("Standby (S3)", StringComparison.OrdinalIgnoreCase);
            bool s0 = hasAvailableSection && availPart.Contains("S0 Low Power Idle", StringComparison.OrdinalIgnoreCase);
            return (s3, s0);
        }
        catch
        {
            return (false, false);
        }
    }

    public static GpuVendor DetectPrimaryGpuVendor()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            using var col = searcher.Get();
            foreach (ManagementBaseObject obj in col)
            using (obj)
            {
                string name = obj["Name"]?.ToString() ?? "";
                if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || name.Contains("GeForce", StringComparison.OrdinalIgnoreCase))
                    return GpuVendor.Nvidia;
                if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
                    return GpuVendor.Amd;
                if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                    return GpuVendor.Intel;
            }
        }
        catch { }
        return GpuVendor.Unknown;
    }
}
