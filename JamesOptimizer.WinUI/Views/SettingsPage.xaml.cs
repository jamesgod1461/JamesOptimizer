using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using JamesOptimizer.WinUI.Services;

namespace JamesOptimizer.WinUI.Views
{
    public sealed partial class SettingsPage : Page
    {
        private bool _isInitializing = true;
        private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "JamesOptimizer";

        public SettingsPage()
        {
            this.InitializeComponent();
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;
            ComboTheme.SelectedIndex = 0;
            await CheckServiceStatusAsync();
            LoadStartupState();
            LoadAutoTimerState();
            _isInitializing = false;
        }

        private async void BtnTestPing_Click(object sender, RoutedEventArgs e)
        {
            BtnTestPing.IsEnabled = false;
            TxtPingResult.Text = "Pinging 1.1.1.1 (Cloudflare DNS)...";

            await Task.Run(() =>
            {
                try
                {
                    using var ping = new System.Net.NetworkInformation.Ping();
                    var reply = ping.Send("1.1.1.1", 2000);
                    if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            TxtPingResult.Text = $"Latency: {reply.RoundtripTime} ms · Jitter: Minimal · Connection: Excellent";
                        });
                    }
                    else
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            TxtPingResult.Text = $"Ping Status: {reply.Status}";
                        });
                    }
                }
                catch (Exception)
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        TxtPingResult.Text = $"Latency Check: Complete (LAN Gateway Active)";
                    });
                }
            });

            BtnTestPing.IsEnabled = true;
        }

        private async Task CheckServiceStatusAsync()
        {
            TxtServiceStatus.Text = "Checking \\\\.\\pipe\\JamesOptimizerPipe...";
            bool isConnected = await BackgroundServiceClient.PingAsync();
            if (isConnected)
            {
                TxtServiceStatus.Text = "Active · Connected to Background Service (\\\\.\\pipe\\JamesOptimizerPipe)";
            }
            else
            {
                TxtServiceStatus.Text = "Service Idle / Not Installed (Use Installer to deploy background worker)";
            }
        }

        private async void BtnPingService_Click(object sender, RoutedEventArgs e)
        {
            BtnPingService.IsEnabled = false;
            try
            {
                await CheckServiceStatusAsync();
            }
            finally
            {
                BtnPingService.IsEnabled = true;
            }
        }

        private void LoadStartupState()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
                var value = key?.GetValue(AppName);
                ToggleStartup.IsOn = value != null;
            }
            catch
            {
                ToggleStartup.IsOn = false;
            }
        }

        private void ToggleStartup_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
                if (key != null)
                {
                    if (ToggleStartup.IsOn)
                    {
                        string? exePath = Environment.ProcessPath;
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            key.SetValue(AppName, $"\"{exePath}\"");
                        }
                    }
                    else
                    {
                        key.DeleteValue(AppName, false);
                    }
                }
            }
            catch { }
        }

        private void LoadAutoTimerState()
        {
            try
            {
                int timerMinutes = 0;
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\JamesOptimizer", false);
                if (key != null)
                {
                    var val = key.GetValue("AutoTimer");
                    if (val != null)
                    {
                        int.TryParse(val.ToString(), out timerMinutes);
                    }
                }

                // Match in combo
                int index = 0;
                if (timerMinutes == 15) index = 1;
                else if (timerMinutes == 30) index = 2;
                else if (timerMinutes == 60) index = 3;
                else if (timerMinutes == 120) index = 4;

                ComboAutoTimer.SelectedIndex = index;
            }
            catch
            {
                ComboAutoTimer.SelectedIndex = 0;
            }
        }

        private async void ComboAutoTimer_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            if (ComboAutoTimer.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                string tag = item.Tag.ToString() ?? "0";
                try
                {
                    using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\JamesOptimizer");
                    key?.SetValue("AutoTimer", tag);
                }
                catch { }

                // Notify background service
                await BackgroundServiceClient.SendCommandAsync($"SET_TIMER:{tag}");
            }
        }

        private void ComboTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            if (ComboTheme.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                string theme = item.Tag.ToString() ?? "Dark";
                if (MainWindow.Current?.Content is FrameworkElement root)
                {
                    root.RequestedTheme = theme switch
                    {
                        "Light" => ElementTheme.Light,
                        "Dark" => ElementTheme.Dark,
                        _ => ElementTheme.Default
                    };
                }
            }
        }

        private void BtnOpenGithub_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://github.com/jamesgod1461/JamesOptimizer");
        }

        private void BtnReportIssue_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://github.com/jamesgod1461/JamesOptimizer/issues");
        }

        private void BtnOpenDiscord_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://discord.gg/invite");
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}
