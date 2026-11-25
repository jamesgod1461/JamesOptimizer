using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using WinRT.Interop;

namespace JamesOptimizer.WinUI.Core;

/// <summary>
/// Lightweight Win32 Shell_NotifyIcon tray host with quick optimization actions.
/// </summary>
public sealed class TrayIconHelper : IDisposable
{
    private const string RegistryPath = @"Software\JamesOptimizer";
    private const string MinimizeToTrayValue = "MinimizeToTray";

    private const int WM_USER = 0x0400;
    private const int WM_TRAYICON = WM_USER + 42;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_DESTROY = 0x0002;
    private const int GWLP_WNDPROC = -4;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;
    private const uint LR_DEFAULTSIZE = 0x00000040;
    private const uint MF_STRING = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const int SW_RESTORE = 9;

    private const int CMD_OPEN = 1001;
    private const int CMD_FLUSH_RAM = 1002;
    private const int CMD_MODE_GAMING = 1003;
    private const int CMD_MODE_WORK = 1004;
    private const int CMD_MODE_ECO = 1005;
    private const int CMD_EXIT = 1006;

    private readonly IntPtr _hwnd;
    private readonly IntPtr _hIcon;
    private readonly WndProcDelegate _newWndProc;
    private readonly IntPtr _oldWndProc;
    private bool _disposed;

    public event Action? OnOpenRequested;
    public event Action? OnFlushRamRequested;
    public event Action<string>? OnSwitchProfileRequested;
    public event Action? OnExitRequested;

    public static bool MinimizeToTray
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, false);
                if (key?.GetValue(MinimizeToTrayValue) is int val) return val != 0;
            }
            catch { }
            return false;
        }
        set
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
                key?.SetValue(MinimizeToTrayValue, value ? 1 : 0, RegistryValueKind.DWord);
            }
            catch { }
        }
    }

    public TrayIconHelper(Window window)
    {
        _hwnd = WindowNative.GetWindowHandle(window);

        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
            _hIcon = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE | LR_DEFAULTSIZE);

        var nid = new NOTIFYICONDATAW
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hIcon,
            szTip = "JamesOptimizer V3"
        };
        Shell_NotifyIconW(NIM_ADD, ref nid);

        _newWndProc = CustomWndProc;
        _oldWndProc = SetWindowLongPtrW(_hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_newWndProc));
    }

    public void ShowWindowToFront()
    {
        ShowWindow(_hwnd, SW_SHOW);
        ShowWindow(_hwnd, SW_RESTORE);
        SetForegroundWindow(_hwnd);
    }

    public void HideWindowToTray() => ShowWindow(_hwnd, SW_HIDE);

    private IntPtr CustomWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAYICON)
        {
            int mouseMsg = (int)(lParam.ToInt64() & 0xFFFF);
            if (mouseMsg == WM_LBUTTONDBLCLK)
            {
                OnOpenRequested?.Invoke();
                return IntPtr.Zero;
            }
            if (mouseMsg == WM_RBUTTONUP)
            {
                ShowContextMenu();
                return IntPtr.Zero;
            }
        }
        else if (msg == WM_DESTROY)
        {
            Dispose();
        }

        return CallWindowProcW(_oldWndProc, hwnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;

        try
        {
            AppendMenuW(menu, MF_STRING, (UIntPtr)CMD_OPEN, "Open JamesOptimizer V3");
            AppendMenuW(menu, MF_SEPARATOR, UIntPtr.Zero, null);
            AppendMenuW(menu, MF_STRING, (UIntPtr)CMD_FLUSH_RAM, "Flush Standby RAM");
            AppendMenuW(menu, MF_STRING, (UIntPtr)CMD_MODE_GAMING, "Power: Ultimate / Gaming");
            AppendMenuW(menu, MF_STRING, (UIntPtr)CMD_MODE_WORK, "Power: Work & Dev");
            AppendMenuW(menu, MF_STRING, (UIntPtr)CMD_MODE_ECO, "Power: Silent / Battery Saver");
            AppendMenuW(menu, MF_SEPARATOR, UIntPtr.Zero, null);
            AppendMenuW(menu, MF_STRING, (UIntPtr)CMD_EXIT, "Exit");

            GetCursorPos(out var pt);
            SetForegroundWindow(_hwnd);
            int cmd = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.X, pt.Y, _hwnd, IntPtr.Zero);

            switch (cmd)
            {
                case CMD_OPEN: OnOpenRequested?.Invoke(); break;
                case CMD_FLUSH_RAM: OnFlushRamRequested?.Invoke(); break;
                case CMD_MODE_GAMING: OnSwitchProfileRequested?.Invoke("Gaming"); break;
                case CMD_MODE_WORK: OnSwitchProfileRequested?.Invoke("Work"); break;
                case CMD_MODE_ECO: OnSwitchProfileRequested?.Invoke("Eco"); break;
                case CMD_EXIT: OnExitRequested?.Invoke(); break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_oldWndProc != IntPtr.Zero)
            SetWindowLongPtrW(_hwnd, GWLP_WNDPROC, _oldWndProc);

        var nid = new NOTIFYICONDATAW { cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(), hWnd = _hwnd, uID = 1 };
        Shell_NotifyIconW(NIM_DELETE, ref nid);

        if (_hIcon != IntPtr.Zero) DestroyIcon(_hIcon);
    }

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public uint uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProcW(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr hMenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);
}
