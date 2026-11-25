using System;
using JamesOptimizer.Core;

namespace JamesOptimizer
{
    internal static class HardwareLogger
    {
        public static void Log(string message) => SharedLogger.Info("HARDWARE", message);
        public static void LogError(string message, Exception? ex = null) => SharedLogger.Error("HARDWARE", message, ex);
    }
}
