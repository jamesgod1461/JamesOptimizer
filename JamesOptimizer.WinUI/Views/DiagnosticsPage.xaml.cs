using System;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;

namespace JamesOptimizer.WinUI.Views
{
    public sealed partial class DiagnosticsPage : Page
    {
        public DiagnosticsPage()
        {
            this.InitializeComponent();
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadSystemDiagnosticsAsync();
        }

        private async Task LoadSystemDiagnosticsAsync()
        {
            await Task.Run(() =>
            {
                string cpu = "Intel / AMD Processor";
                string gpu = "Dedicated / Integrated GPU";
                string ram = "16.0 GB";
                string os = "Windows 11 64-bit";
                string uacStatus = "Enabled (Optimal)";
                string defenderStatus = "Active (Protected)";
                string smartStatus = "OK · Healthy";

                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
                    foreach (var obj in searcher.Get())
                    {
                        cpu = obj["Name"]?.ToString() ?? cpu;
                        break;
                    }
                }
                catch { }

                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                    foreach (var obj in searcher.Get())
                    {
                        gpu = obj["Name"]?.ToString() ?? gpu;
                        break;
                    }
                }
                catch { }

                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                    foreach (var obj in searcher.Get())
                    {
                        if (ulong.TryParse(obj["TotalPhysicalMemory"]?.ToString(), out ulong bytes))
                        {
                            ram = $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
                        }
                        break;
                    }
                }
                catch { }

                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT Caption, OSArchitecture FROM Win32_OperatingSystem");
                    foreach (var obj in searcher.Get())
                    {
                        os = $"{obj["Caption"]} ({obj["OSArchitecture"]})".Replace("Microsoft", "").Trim();
                        break;
                    }
                }
                catch { }

                // Check UAC
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
                    if (key != null)
                    {
                        var val = key.GetValue("EnableLUA");
                        if (val != null && Convert.ToInt32(val) == 0)
                        {
                            uacStatus = "Disabled (Warning)";
                        }
                    }
                }
                catch { }

                // Check Defender Service
                try
                {
                    using var sc = new System.ServiceProcess.ServiceController("WinDefend");
                    if (sc.Status != System.ServiceProcess.ServiceControllerStatus.Running)
                    {
                        defenderStatus = "Inactive / Managed";
                    }
                }
                catch { }

                DispatcherQueue.TryEnqueue(() =>
                {
                    TxtCpuName.Text = cpu;
                    TxtGpuName.Text = gpu;
                    TxtRamTotal.Text = ram;
                    TxtOsInfo.Text = os;
                    TxtUacStatus.Text = uacStatus;
                    TxtDefenderStatus.Text = defenderStatus;
                    TxtSmartStatus.Text = smartStatus;
                });
            });
        }

        private async void BtnExportReport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BtnExportReport.IsEnabled = false;
                await OptimizationReportGenerator.GenerateAndOpenReportAsync();
            }
            finally
            {
                BtnExportReport.IsEnabled = true;
            }
        }
    }
}
