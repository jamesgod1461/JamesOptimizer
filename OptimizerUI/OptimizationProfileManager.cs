using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace JamesOptimizer
{
    public enum OptimizationProfileType
    {
        Gaming,
        Work,
        BatterySaver
    }

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
                        // Ultimate power, high priority, game mode tweaks
                        try
                        {
                            using var p = Process.Start(new ProcessStartInfo("powercfg", "-setactive e9a42b02-d5df-448d-aa00-03f14749eb61") { CreateNoWindow = true, UseShellExecute = false });
                            p?.WaitForExit();
                        }
                        catch { }

                        await OptimizationEngine.RunGamingBoostAsync(CancellationToken.None);
                        await OptimizationEngine.RunGpuTweaksAsync(CancellationToken.None);
                        await OptimizationEngine.RunRamFlushAsync();
                        break;

                    case OptimizationProfileType.Work:
                        // Balanced power, clean temp, standard responsiveness
                        try
                        {
                            using var p = Process.Start(new ProcessStartInfo("powercfg", "-setactive 381b4222-f694-41f0-9685-ff5bb260df2e") { CreateNoWindow = true, UseShellExecute = false });
                            p?.WaitForExit();
                        }
                        catch { }

                        await OptimizationEngine.RunVisualOptimizeAsync(CancellationToken.None);
                        await OptimizationEngine.RunRamFlushAsync();
                        break;

                    case OptimizationProfileType.BatterySaver:
                        // Power saver, low background load
                        try
                        {
                            using var p = Process.Start(new ProcessStartInfo("powercfg", "-setactive a1841308-3541-4fab-bc81-f71556f20b4a") { CreateNoWindow = true, UseShellExecute = false });
                            p?.WaitForExit();
                        }
                        catch { }

                        await OptimizationEngine.RunRamFlushAsync();
                        break;
                }
            });
        }
    }
}
