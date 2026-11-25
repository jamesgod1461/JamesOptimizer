using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;

namespace JamesOptimizer.Core;

/// <summary>
/// Reads cached GPU / ACPI thermal status with a short cooldown and sticky failure caching
/// when <c>nvidia-smi</c> is absent or ACPI thermal zones are unsupported.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GpuThermalProbe
{
    private readonly Func<string?> _nvidiaSmiReader;
    private readonly Func<double?> _acpiTempReader;
    private readonly Func<DateTime> _utcNow;

    private DateTime _lastTempCheck = DateTime.MinValue;
    private string _cachedGpuTemp = "";
    private bool _nvidiaSmiMissing;
    private bool _acpiThermalUnsupported;

    public bool IsNvidiaSmiMissing => _nvidiaSmiMissing;
    public bool IsAcpiThermalUnsupported => _acpiThermalUnsupported;

    public GpuThermalProbe(
        Func<string?>? nvidiaSmiReader = null,
        Func<double?>? acpiTempReader = null,
        Func<DateTime>? utcNow = null)
    {
        _nvidiaSmiReader = nvidiaSmiReader ?? RunDefaultNvidiaSmi;
        _acpiTempReader = acpiTempReader ?? ReadDefaultAcpiTempCelsius;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public string ReadLabel()
    {
        DateTime now = _utcNow();
        if (_lastTempCheck != DateTime.MinValue && (now - _lastTempCheck).TotalSeconds < 6)
            return _cachedGpuTemp;

        _lastTempCheck = now;
        if (!_nvidiaSmiMissing)
        {
            try
            {
                string? line = _nvidiaSmiReader()?.Trim();
                if (int.TryParse(line, out int temp) && temp > 0 && temp < 130)
                {
                    _cachedGpuTemp = $"{temp}°C · Temp";
                    return _cachedGpuTemp;
                }
            }
            catch (Win32Exception)
            {
                _nvidiaSmiMissing = true;
            }
            catch { }
        }

        if (!_acpiThermalUnsupported)
        {
            try
            {
                double? c = _acpiTempReader();
                if (c is > 10 and < 120)
                {
                    _cachedGpuTemp = $"{c.Value:0}°C · Thermal";
                    return _cachedGpuTemp;
                }
            }
            catch (ManagementException)
            {
                _acpiThermalUnsupported = true;
            }
            catch { }
        }

        _cachedGpuTemp = "3D Engine";
        return _cachedGpuTemp;
    }

    private static string? RunDefaultNvidiaSmi()
    {
        using var proc = Process.Start(new ProcessStartInfo
        {
            FileName = "nvidia-smi",
            Arguments = "--query-gpu=temperature.gpu --format=csv,noheader,nounits",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        if (proc == null) return null;
        if (!proc.WaitForExit(500))
        {
            try { proc.Kill(true); } catch { }
            return null;
        }
        return proc.StandardOutput.ReadLine();
    }

    private static double? ReadDefaultAcpiTempCelsius()
    {
        using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
        using var results = searcher.Get();
        foreach (ManagementObject obj in results)
        using (obj)
        {
            if (double.TryParse(obj["CurrentTemperature"]?.ToString(), out double raw))
                return (raw - 2732.0) / 10.0;
            break;
        }
        return null;
    }
}
