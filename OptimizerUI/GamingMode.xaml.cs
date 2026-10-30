using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

namespace JamesOptimizer
{
    public partial class GamingMode : Page, INavigablePage
    {
        private bool _isBoosting = false;

        public GamingMode()
        {
            InitializeComponent();
        }

        private void GamingMode_Loaded(object sender, RoutedEventArgs e)
        {
            // Initial animation / state check
        }

        public void OnNavigatedTo()
        {
        }

        public void OnNavigatedFrom()
        {
        }

        private async void BtnActivateTurbo_Click(object sender, RoutedEventArgs e)
        {
            if (_isBoosting) return;
            _isBoosting = true;
            BtnActivateTurbo.IsEnabled = false;
            TxtTurboStatus.Text = "Engaging Game Turbo Engine...";

            try
            {
                await OptimizationEngine.RunGamingBoostAsync(CancellationToken.None);
                await OptimizationEngine.RunRamFlushAsync(CancellationToken.None);

                TxtStatus.Text = "TURBO ACTIVE ✓";
                if (TryFindResource("SuccessGreen") is Brush greenBrush)
                {
                    DotStatus.Fill = greenBrush;
                    TxtStatus.Foreground = greenBrush;
                }

                BtnActivateTurbo.Content = "GAME TURBO ACTIVE ✓";
                TxtTurboStatus.Text = "Ultra Low-Latency Engine Engaged";

                MainWindow.Instance?.ShowToastSuccess("Game Turbo Active", "Ultra low-latency gaming engine running.");
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToastError("Gaming Mode Error", ex.Message);
                BtnActivateTurbo.IsEnabled = true;
                BtnActivateTurbo.Content = "ACTIVATE GAME TURBO ⚡";
                TxtTurboStatus.Text = "Standby · Ready to Engage";
            }
            finally
            {
                _isBoosting = false;
            }
        }

        private async void BtnGpuOptimize_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Set GPU priority and hardware scheduling registry keys
                Microsoft.Win32.Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2, Microsoft.Win32.RegistryValueKind.DWord);
                MainWindow.Instance?.ShowToastSuccess("GPU Pipeline Optimized", "Hardware-accelerated GPU scheduling enabled.");
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToastError("GPU Optimization", ex.Message);
            }
        }

        private async void BtnNetworkLatency_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await OptimizationEngine.RunGamingBoostAsync(CancellationToken.None);
                MainWindow.Instance?.ShowToastSuccess("Network Latency Minimized", "Nagle's TCP algorithm disabled and packet coalescing optimized.");
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToastError("Network Optimization", ex.Message);
            }
        }

        private void BtnDisableDvr_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Microsoft.Win32.Registry.SetValue(@"HKEY_CURRENT_USER\System\GameConfigStore", "GameDVR_Enabled", 0, Microsoft.Win32.RegistryValueKind.DWord);
                Microsoft.Win32.Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0, Microsoft.Win32.RegistryValueKind.DWord);
                MainWindow.Instance?.ShowToastSuccess("Game DVR Disabled", "Background recording disabled to eliminate FPS dips.");
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToastError("Game DVR", ex.Message);
            }
        }

        private async void BtnFlushMemory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await OptimizationEngine.RunRamFlushAsync(CancellationToken.None);
                MainWindow.Instance?.ShowToastSuccess("RAM Flushed", "Cleaned standby list and compacted process working sets.");
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToastError("Memory Flush", ex.Message);
            }
        }
    }
}
