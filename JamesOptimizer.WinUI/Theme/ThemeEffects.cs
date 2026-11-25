using System;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.System.Power;
using Windows.UI;

namespace JamesOptimizer.WinUI.Theme;

/// <summary>
/// Central runtime theme helpers, token lookup, and performance guardrails for visual effects.
/// Controls the global Theme.EffectsEnabled flag, battery saver / load throttling, and window pause state.
/// </summary>
public static class ThemeEffects
{
    private static bool _userEffectsEnabled = true;
    private static bool _isWindowVisibleAndActive = true;
    private static long _lastLoadProbeTick;
    private static bool _cachedUnderLoad;
    private static ulong _prevIdleTime;
    private static ulong _prevKernelTime;
    private static ulong _prevUserTime;

    static ThemeEffects()
    {
        try
        {
            PowerManager.EnergySaverStatusChanged += (_, _) => NotifyEffectsChanged();
        }
        catch { }
    }

    /// <summary>
    /// User/configuration override mapped to XAML resource key "Theme.EffectsEnabled". Default true.
    /// </summary>
    public static bool UserEffectsEnabled
    {
        get => _userEffectsEnabled;
        set
        {
            if (_userEffectsEnabled != value)
            {
                _userEffectsEnabled = value;
                SyncResourceFlag();
                NotifyEffectsChanged();
            }
        }
    }

    public static bool IsWindowVisibleAndActive => _isWindowVisibleAndActive;

    public static void SetWindowActiveState(bool visibleAndActive)
    {
        if (_isWindowVisibleAndActive == visibleAndActive) return;
        _isWindowVisibleAndActive = visibleAndActive;
        WindowVisibilityChanged?.Invoke(null, visibleAndActive);
        NotifyEffectsChanged();
    }

    /// <summary>
    /// True when visual glow/motion effects should render. Auto-disables under battery saver,
    /// heavy CPU/RAM load, reduced-motion preference, or when the window is minimized/hidden.
    /// </summary>
    public static bool EffectsEnabled
    {
        get
        {
            if (!_userEffectsEnabled || !_isWindowVisibleAndActive || Core.MotionHelper.ReduceAnimations)
                return false;

            if (IsBatterySaverActive() || IsSystemUnderHeavyLoad())
                return false;

            return true;
        }
    }

    public static event EventHandler<bool>? EffectsEnabledChanged;
    public static event EventHandler<bool>? WindowVisibilityChanged;

    public static bool IsBatterySaverActive()
    {
        try
        {
            return PowerManager.EnergySaverStatus == EnergySaverStatus.On;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsSystemUnderHeavyLoad()
    {
        long now = Environment.TickCount64;
        if (now - _lastLoadProbeTick < 3000)
            return _cachedUnderLoad;

        _lastLoadProbeTick = now;
        bool underLoad = false;

        try
        {
            var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref mem) && mem.dwMemoryLoad >= 90)
                underLoad = true;

            if (!underLoad && GetSystemTimes(out var idleFt, out var kernelFt, out var userFt))
            {
                ulong idle = FileTimeToUInt64(idleFt);
                ulong kernel = FileTimeToUInt64(kernelFt);
                ulong user = FileTimeToUInt64(userFt);

                if (_prevKernelTime != 0)
                {
                    ulong totalSys = (kernel - _prevKernelTime) + (user - _prevUserTime);
                    ulong idleDelta = idle - _prevIdleTime;
                    if (totalSys > 0)
                    {
                        double cpuUsage = (1.0 - (double)idleDelta / totalSys) * 100.0;
                        if (cpuUsage >= 88.0)
                            underLoad = true;
                    }
                }

                _prevIdleTime = idle;
                _prevKernelTime = kernel;
                _prevUserTime = user;
            }
        }
        catch { }

        if (_cachedUnderLoad != underLoad)
        {
            _cachedUnderLoad = underLoad;
            SyncResourceFlag();
        }
        return _cachedUnderLoad;
    }

    private static void SyncResourceFlag()
    {
        try
        {
            if (Application.Current?.Resources is { } res)
                res["Theme.EffectsEnabled"] = EffectsEnabled;
        }
        catch { }
    }

    public static void NotifyEffectsChanged()
    {
        SyncResourceFlag();
        EffectsEnabledChanged?.Invoke(null, EffectsEnabled);
    }

    public static Brush GetBrush(string tokenKey)
    {
        if (Application.Current?.Resources is { } res && res.TryGetValue(tokenKey, out var obj) && obj is Brush b)
            return b;
        return new SolidColorBrush(Colors.Transparent);
    }

    public static SolidColorBrush GetSolidBrush(string tokenKey) =>
        GetBrush(tokenKey) as SolidColorBrush ?? new SolidColorBrush(Colors.Transparent);

    public static Color GetColor(string colorTokenKey, Color fallback)
    {
        if (Application.Current?.Resources is { } res && res.TryGetValue(colorTokenKey, out var obj) && obj is Color c)
            return c;
        return fallback;
    }

    private static ulong FileTimeToUInt64(System.Runtime.InteropServices.ComTypes.FILETIME ft) =>
        ((ulong)(uint)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
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

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out System.Runtime.InteropServices.ComTypes.FILETIME lpIdleTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime);
}
