using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JamesOptimizer.Core;

namespace JamesOptimizer
{
    public enum OptimizationProfileType
    {
        Gaming,
        Work,
        BatterySaver
    }

    [SupportedOSPlatform("windows")]
    public static class OptimizationProfileManager
    {
        public static OptimizationProfileType CurrentProfile { get; private set; } = OptimizationProfileType.Gaming;

        public static async Task ApplyProfileAsync(OptimizationProfileType profile)
        {
            CurrentProfile = profile;

            await Task.Run(async () =>
            {
                switch (profile)
                {
                    case OptimizationProfileType.Gaming:
                        await ActivateUltimatePerformanceAsync();
                        await OptimizationEngine.RunGamingBoostAsync(CancellationToken.None);
                        await OptimizationEngine.RunGpuTweaksAsync(CancellationToken.None);
                        await OptimizationEngine.RunRamFlushAsync();
                        break;

                    case OptimizationProfileType.Work:
                        await ActivateBalancedAsync();
                        await OptimizationEngine.RunVisualOptimizeAsync(CancellationToken.None);
                        await OptimizationEngine.RunRamFlushAsync();
                        break;

                    case OptimizationProfileType.BatterySaver:
                        await ActivatePowerSaverAsync();
                        await OptimizationEngine.RunRamFlushAsync();
                        break;
                }
            });
        }

        public static async Task ActivateUltimatePerformanceAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var listRes = await NativeSystemHelper.RunSystem32ToolAsync("powercfg.exe", new[] { "/list" }, cancellationToken);
                string output = listRes.StdOut;
                string targetGuid = CoreConstants.UltimatePerformanceGuid;
                bool alreadyExists = output.Contains(targetGuid, StringComparison.OrdinalIgnoreCase);

                if (!alreadyExists)
                {
                    foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (line.Contains("Ultimate Performance", StringComparison.OrdinalIgnoreCase))
                        {
                            var match = Regex.Match(line, @"[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}");
                            if (match.Success)
                            {
                                targetGuid = match.Value;
                                alreadyExists = true;
                                break;
                            }
                        }
                    }
                }

                if (!alreadyExists)
                {
                    var dupRes = await NativeSystemHelper.RunSystem32ToolAsync("powercfg.exe", new[] { "-duplicatescheme", CoreConstants.UltimatePerformanceGuid }, cancellationToken);
                    var match = Regex.Match(dupRes.StdOut, @"[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}");
                    if (match.Success) targetGuid = match.Value;
                }

                await NativeSystemHelper.RunSystem32ToolAsync("powercfg.exe", new[] { "/setactive", targetGuid }, cancellationToken);
            }
            catch (Exception ex)
            {
                HardwareLogger.LogError("ActivateUltimatePerformanceAsync error", ex);
            }
        }

        public static Task ActivateBalancedAsync(CancellationToken cancellationToken = default) =>
            SetPowerSchemeAsync(CoreConstants.BalancedGuid, cancellationToken);

        public static Task ActivatePowerSaverAsync(CancellationToken cancellationToken = default) =>
            SetPowerSchemeAsync(CoreConstants.PowerSaverGuid, cancellationToken);

        private static async Task SetPowerSchemeAsync(string guid, CancellationToken cancellationToken)
        {
            try
            {
                await NativeSystemHelper.RunSystem32ToolAsync("powercfg.exe", new[] { "/setactive", guid }, cancellationToken);
            }
            catch (Exception ex)
            {
                HardwareLogger.LogError($"SetPowerSchemeAsync({guid}) error", ex);
            }
        }
    }
}
