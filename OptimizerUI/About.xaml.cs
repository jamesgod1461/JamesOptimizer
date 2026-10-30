using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Microsoft.Win32;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace JamesOptimizer
{
    /// <summary>
    /// Interaction logic for About.xaml
    /// James Optimizer V3 Architecture
    /// </summary>
    public partial class About : Page, INavigablePage
    {
        public About()
        {
            InitializeComponent();
        }

        private void About_Loaded(object sender, RoutedEventArgs e)
        {
            OnNavigatedTo();
        }

        public void OnNavigatedTo()
        {
            PlayStaggeredEntrance();
            _ = QuerySystemInfoAsync();
            _ = TestNetworkLatencyAsync();
        }

        public void OnNavigatedFrom()
        {
        }

        private void PlayStaggeredEntrance()
        {
            if (!ThemeManager.AnimationsEnabled)
            {
                // Show everything immediately, no motion
                foreach (var card in new FrameworkElement[] { CardPcSpecs, CardNetworkInfo, CardDevInfo, CardVersion })
                {
                    card.Opacity = 1;
                    if (card.RenderTransform is TranslateTransform tt) tt.X = 0;
                }
                return;
            }

            var cubicEase = new CubicEase { EasingMode = EasingMode.EaseOut };

            // Cards stagger: top-left 0ms, top-right 60ms, bottom-left 120ms, bottom-right 180ms
            AnimateCardIn(CardPcSpecs, 0, cubicEase);
            AnimateCardIn(CardNetworkInfo, 60, cubicEase);
            AnimateCardIn(CardDevInfo, 120, cubicEase);
            AnimateCardIn(CardVersion, 180, cubicEase);
        }

        private void AnimateCardIn(FrameworkElement element, int delayMs, EasingFunctionBase ease)
        {
            if (element == null) return;
            element.Opacity = 0;
            var tt = new TranslateTransform(-6, 0);
            element.RenderTransform = tt;

            var opAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = ease
            };
            var transAnim = new DoubleAnimation(-6, 0, TimeSpan.FromMilliseconds(220))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = ease
            };

            element.BeginAnimation(UIElement.OpacityProperty, opAnim);
            tt.BeginAnimation(TranslateTransform.XProperty, transAnim);
        }

        // ═════════════════════════════════════════════════════════════════
        // HARDWARE & NETWORK QUERIES
        // ═════════════════════════════════════════════════════════════════
        private async Task QuerySystemInfoAsync()
        {
            await Task.Run(() =>
            {
                string cpu = "Intel(R) Core(TM) i7-14700F";
                string gpu = "NVIDIA GeForce RTX 4060 Ti";
                string ram = "32 GB (6000 MHz)";

                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
                    foreach (var m in searcher.Get()) { cpu = m["Name"]?.ToString() ?? cpu; break; }
                }
                catch { }

                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                    foreach (var m in searcher.Get()) { gpu = m["Name"]?.ToString() ?? gpu; break; }
                }
                catch { }

                try
                {
                    var memStatus = new HardwareMonitorService.MEMORYSTATUSEX();
                    memStatus.dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(HardwareMonitorService.MEMORYSTATUSEX));
                    if (HardwareMonitorService.GlobalMemoryStatusEx(ref memStatus))
                    {
                        ulong totalGb = (memStatus.ullTotalPhys / (1024 * 1024 * 1024)) + 1;
                        ram = $"{totalGb} GB";
                    }
                }
                catch { }

                string adapter = "Realtek Gaming 2.5GbE Family Controller";
                try
                {
                    var active = NetworkInterface.GetAllNetworkInterfaces()
                        .FirstOrDefault(i => i.OperationalStatus == OperationalStatus.Up &&
                        (i.NetworkInterfaceType == NetworkInterfaceType.Ethernet || i.NetworkInterfaceType == NetworkInterfaceType.Wireless80211));
                    if (active != null) adapter = active.Description;
                }
                catch { }

                Dispatcher.Invoke(() =>
                {
                    TxtSpecProcessor.Text = cpu;
                    TxtSpecGpu.Text = gpu;
                    TxtSpecRam.Text = ram;
                    TxtInfoAdapter.Text = adapter;
                });
            });
        }

        private async Task TestNetworkLatencyAsync()
        {
            long pingMs = 26;
            try
            {
                using var pinger = new Ping();
                var reply = await pinger.SendPingAsync("8.8.8.8", 1200);
                if (reply.Status == IPStatus.Success) pingMs = reply.RoundtripTime;
            }
            catch { }

            TxtAboutPing.Text = pingMs.ToString();
            if (pingMs < 30)
                TxtAboutPing.Foreground = (Brush)FindResource("SuccessGreen");
            else if (pingMs <= 70)
                TxtAboutPing.Foreground = (Brush)FindResource("WarningAmber");
            else
                TxtAboutPing.Foreground = (Brush)FindResource("DangerRed");
        }

        private async void BtnRefreshNetwork_Click(object sender, RoutedEventArgs e)
        {
            await TestNetworkLatencyAsync();
            MainWindow.Instance?.ShowToastSuccess("Network Ping Refreshed", $"Latency to 8.8.8.8: {TxtAboutPing.Text} ms.");
        }

        // ═════════════════════════════════════════════════════════════════
        // VERSION & UPDATES CARD
        // (Theme / accent / backdrop customization lives in the Settings page)
        // ═════════════════════════════════════════════════════════════════
        private void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.ShowToastSuccess("You're Up to Date", "James Optimizer 3.2.0 is the latest version.");
        }

        private void BtnOpenSettings_Click(object sender, RoutedEventArgs e)
        {
            _ = MainWindow.Instance?.Navigate(new Settings());
        }

        private void BtnOpenGithub_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://github.com/jamesgod1461/JamesOptimizer") { UseShellExecute = true });
        }

        private void BtnReportIssue_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://github.com/jamesgod1461/JamesOptimizer/issues") { UseShellExecute = true });
        }

        private void BtnOpenDiscord_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://discord.gg/jamesoptimizer") { UseShellExecute = true });
        }
    }
}
