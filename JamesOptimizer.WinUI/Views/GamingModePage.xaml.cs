using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using JamesOptimizer;

namespace JamesOptimizer.WinUI.Views
{
    public sealed partial class GamingModePage : Page
    {
        private bool _isBoosting = false;

        public GamingModePage()
        {
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
        }

        private async void BtnActivateTurbo_Click(object sender, RoutedEventArgs e)
        {
            if (_isBoosting) return;
            _isBoosting = true;
            BtnActivateTurbo.IsEnabled = false;
            TxtTurboStatus.Text = "Engaging Extreme Game Turbo Engine...";

            try
            {
                await OptimizationEngine.RunGamingBoostAsync(CancellationToken.None);
                await OptimizationEngine.RunRamFlushAsync(CancellationToken.None);

                TxtStatus.Text = "TURBO ACTIVE ✓";
                StatusDot.Fill = new SolidColorBrush(ColorHelper.FromArgb(255, 0, 245, 255));
                TxtStatus.Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 0, 245, 255));

                TxtTurboBtn.Text = "GAME TURBO ENGAGED ✓";
                TxtTurboStatus.Text = "Ultra low-latency gaming engine running. Standby RAM cleared.";

                App.ViewModel?.AddLog("GAMING", "Game Turbo Engine engaged! High resolution timer & gaming QoS active.");
            }
            catch (Exception ex)
            {
                BtnActivateTurbo.IsEnabled = true;
                TxtTurboBtn.Text = "ACTIVATE GAME TURBO ⚡";
                TxtTurboStatus.Text = $"Error: {ex.Message}";
                App.ViewModel?.AddLog("ERROR", $"Failed to engage Game Turbo: {ex.Message}", true);
            }
            finally
            {
                _isBoosting = false;
            }
        }

        private void BtnGpuOptimize_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2, RegistryValueKind.DWord);
                App.ViewModel?.AddLog("GAMING", "Hardware-accelerated GPU Scheduling (HAGS) set to Enabled (restart may be required).");
            }
            catch (Exception ex)
            {
                App.ViewModel?.AddLog("ERROR", $"GPU optimization error: {ex.Message}", true);
            }
        }

        private async void BtnNetworkLatency_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await OptimizationEngine.DisableNetworkThrottlingAsync();
                App.ViewModel?.AddLog("GAMING", "Zero Packet Delay applied. Nagle's TCP latency algorithm disabled.");
            }
            catch (Exception ex)
            {
                App.ViewModel?.AddLog("ERROR", $"Network optimization error: {ex.Message}", true);
            }
        }

        private void BtnDisableDvr_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Registry.SetValue(@"HKEY_CURRENT_USER\System\GameConfigStore", "GameDVR_Enabled", 0, RegistryValueKind.DWord);
                Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0, RegistryValueKind.DWord);
                App.ViewModel?.AddLog("GAMING", "Game DVR and background recording telemetry completely disabled.");
            }
            catch (Exception ex)
            {
                App.ViewModel?.AddLog("ERROR", $"Game DVR error: {ex.Message}", true);
            }
        }

        private void BtnMousePrecision_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Registry.SetValue(@"HKEY_CURRENT_USER\Control Panel\Mouse", "MouseSpeed", "0", RegistryValueKind.String);
                Registry.SetValue(@"HKEY_CURRENT_USER\Control Panel\Mouse", "MouseThreshold1", "0", RegistryValueKind.String);
                Registry.SetValue(@"HKEY_CURRENT_USER\Control Panel\Mouse", "MouseThreshold2", "0", RegistryValueKind.String);
                App.ViewModel?.AddLog("GAMING", "Windows Mouse Acceleration disabled. 1:1 raw linear mouse input applied.");
            }
            catch (Exception ex)
            {
                App.ViewModel?.AddLog("ERROR", $"Mouse precision error: {ex.Message}", true);
            }
        }
    }
}
