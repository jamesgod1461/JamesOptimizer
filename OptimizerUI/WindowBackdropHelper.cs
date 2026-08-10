using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace JamesOptimizer
{
    /// <summary>
    /// Helper class to enable Mica/Acrylic backdrop on Windows 11
    /// </summary>
    public static class WindowBackdropHelper
    {
        // DWM Window Attributes
        private enum DWMWINDOWATTRIBUTE
        {
            DWMWA_USE_IMMERSIVE_DARK_MODE = 20,
            DWMWA_MICA_EFFECT = 1029,
            DWMWA_SYSTEMBACKDROP_TYPE = 38
        }

        // System Backdrop Types (Windows 11 22H2+)
        private enum DWM_SYSTEMBACKDROP_TYPE
        {
            DWMSBT_AUTO = 0,
            DWMSBT_NONE = 1,
            DWMSBT_MAINWINDOW = 2, // Mica
            DWMSBT_TRANSIENTWINDOW = 3, // Mica Alt
            DWMSBT_TABBEDWINDOW = 4, // Acrylic
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, DWMWINDOWATTRIBUTE attr, ref int attrValue, int attrSize);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS
        {
            public int cxLeftWidth;
            public int cxRightWidth;
            public int cyTopHeight;
            public int cyBottomHeight;
        }

        /// <summary>
        /// Enable Mica backdrop for the window (Windows 11 22H2+)
        /// </summary>
        public static void EnableMica(Window window)
        {
            if (!IsWindows11_22H2OrGreater())
                return;

            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            
            // Extend frame into client area for custom titlebar
            var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);

            // Set system backdrop type to Mica (Main Window)
            int backdropType = (int)DWM_SYSTEMBACKDROP_TYPE.DWMSBT_MAINWINDOW;
            DwmSetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));

            // Enable dark mode for titlebar
            int darkMode = 1;
            DwmSetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
        }

        /// <summary>
        /// Enable Acrylic backdrop for the window (Windows 11 22H2+)
        /// </summary>
        public static void EnableAcrylic(Window window)
        {
            if (!IsWindows11_22H2OrGreater())
                return;

            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            
            var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);

            // Set system backdrop type to Acrylic (Tabbed Window)
            int backdropType = (int)DWM_SYSTEMBACKDROP_TYPE.DWMSBT_TABBEDWINDOW;
            DwmSetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));

            int darkMode = 1;
            DwmSetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
        }

        /// <summary>
        /// Enable Mica Alt backdrop for transient windows
        /// </summary>
        public static void EnableMicaAlt(Window window)
        {
            if (!IsWindows11_22H2OrGreater())
                return;

            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            
            var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);

            int backdropType = (int)DWM_SYSTEMBACKDROP_TYPE.DWMSBT_TRANSIENTWINDOW;
            DwmSetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));

            int darkMode = 1;
            DwmSetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
        }

        /// <summary>
        /// Disable system backdrop (fallback to solid color)
        /// </summary>
        public static void DisableBackdrop(Window window)
        {
            if (!IsWindows11_22H2OrGreater())
                return;

            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            int backdropType = (int)DWM_SYSTEMBACKDROP_TYPE.DWMSBT_NONE;
            DwmSetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
        }

        /// <summary>
        /// Apply the backdrop based on user settings
        /// </summary>
        public static void ApplyBackdrop(Window window, BackdropType type)
        {
            switch (type)
            {
                case BackdropType.Mica:
                    EnableMica(window);
                    break;
                case BackdropType.Acrylic:
                    EnableAcrylic(window);
                    break;
                case BackdropType.MicaAlt:
                    EnableMicaAlt(window);
                    break;
                case BackdropType.None:
                default:
                    DisableBackdrop(window);
                    break;
            }
        }

        private static bool IsWindows11_22H2OrGreater()
        {
            // Windows 11 22H2 = Build 22621+
            return Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22621;
        }
    }

    public enum BackdropType
    {
        None,
        Mica,
        Acrylic,
        MicaAlt
    }
}