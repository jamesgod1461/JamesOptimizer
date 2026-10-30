using System;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using JamesOptimizer;

namespace JamesOptimizer.WinUI.Views
{
    public sealed partial class NetworkPage : Page
    {
        public NetworkPage()
        {
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _ = RunPingTestAsync();
        }

        private async Task RunPingTestAsync()
        {
            TxtCloudflarePing.Text = "--";
            TxtGooglePing.Text = "--";

            await Task.Run(async () =>
            {
                using var ping = new Ping();

                // Test Cloudflare
                try
                {
                    var reply = await ping.SendPingAsync("1.1.1.1", 1500);
                    if (reply.Status == IPStatus.Success)
                    {
                        DispatcherQueue.TryEnqueue(() => TxtCloudflarePing.Text = reply.RoundtripTime.ToString());
                    }
                    else
                    {
                        DispatcherQueue.TryEnqueue(() => TxtCloudflarePing.Text = "ERR");
                    }
                }
                catch
                {
                    DispatcherQueue.TryEnqueue(() => TxtCloudflarePing.Text = "N/A");
                }

                // Test Google
                try
                {
                    var reply = await ping.SendPingAsync("8.8.8.8", 1500);
                    if (reply.Status == IPStatus.Success)
                    {
                        DispatcherQueue.TryEnqueue(() => TxtGooglePing.Text = reply.RoundtripTime.ToString());
                    }
                    else
                    {
                        DispatcherQueue.TryEnqueue(() => TxtGooglePing.Text = "ERR");
                    }
                }
                catch
                {
                    DispatcherQueue.TryEnqueue(() => TxtGooglePing.Text = "N/A");
                }
            });
        }

        private async void BtnTestLatency_Click(object sender, RoutedEventArgs e)
        {
            BtnTestLatency.IsEnabled = false;
            await RunPingTestAsync();
            BtnTestLatency.IsEnabled = true;
            App.ViewModel?.AddLog("NETWORK", "Ping latency benchmark refreshed.");
        }

        private async void BtnFlushDns_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn) btn.IsEnabled = false;

            await Task.Run(async () =>
            {
                await OptimizationEngine.FlushDnsAsync();
            });

            if (sender is Button btnDone) btnDone.IsEnabled = true;
            App.ViewModel?.AddLog("NETWORK", "DNS resolver cache successfully purged and refreshed.");
        }

        private async void BtnResetNetwork_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn) btn.IsEnabled = false;

            await Task.Run(async () =>
            {
                await OptimizationEngine.ResetTcpIpStackAsync();
            });

            if (sender is Button btnDone) btnDone.IsEnabled = true;
            App.ViewModel?.AddLog("NETWORK", "TCP/IP stack and Winsock catalog reset (restart recommended).");
        }

        private async void BtnSetDns_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string provider)
            {
                btn.IsEnabled = false;

                string primaryDns = "";
                string secondaryDns = "";

                switch (provider)
                {
                    case "Cloudflare":
                        primaryDns = "1.1.1.1";
                        secondaryDns = "1.0.0.1";
                        break;
                    case "Google":
                        primaryDns = "8.8.8.8";
                        secondaryDns = "8.8.4.4";
                        break;
                    case "Quad9":
                        primaryDns = "9.9.9.9";
                        secondaryDns = "149.112.112.112";
                        break;
                }

                await Task.Run(() =>
                {
                    try
                    {
                        if (provider == "DHCP")
                        {
                            RunCmd("netsh interface ip set dns \"Wi-Fi\" dhcp");
                            RunCmd("netsh interface ip set dns \"Ethernet\" dhcp");
                        }
                        else
                        {
                            RunCmd($"netsh interface ip set dns \"Wi-Fi\" static {primaryDns}");
                            RunCmd($"netsh interface ip add dns \"Wi-Fi\" {secondaryDns} index=2");
                            RunCmd($"netsh interface ip set dns \"Ethernet\" static {primaryDns}");
                            RunCmd($"netsh interface ip add dns \"Ethernet\" {secondaryDns} index=2");
                        }
                    }
                    catch { }
                });

                btn.IsEnabled = true;
                App.ViewModel?.AddLog("NETWORK", $"DNS Provider updated to: {provider}.");
                _ = RunPingTestAsync();
            }
        }

        private static void RunCmd(string cmd)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {cmd}",
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false
                });
                p?.WaitForExit(3000);
            }
            catch { }
        }
    }
}
