using System;
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

        public static void LogError(string message, Exception? ex = null)
        {
            Log($"ERROR: {message} {(ex != null ? "- " + ex.Message : "")}");
        }
    }
}
