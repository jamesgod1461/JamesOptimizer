using System;
using System.IO;
using System.Linq;
using System.Management;
using System.ServiceProcess;
using System.Threading.Tasks;
using JamesOptimizer.Core;
using JamesOptimizer.WinUI.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;

namespace JamesOptimizer.WinUI.Views;

public sealed partial class DiagnosticsPage : Page
{
    public DiagnosticsPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        MotionHelper.AnimateStaggeredEntrance(new UIElement[] { GridSpecCards, CardBaselineChecklist }, staggerMs: 50, durationMs: 260);
        await LoadSystemDiagnosticsAsync();
    }

    private async Task LoadSystemDiagnosticsAsync()
    {
        await Task.Run(() =>
        {
            string cpu = "Unavailable";
            string gpu = "Unavailable";
            string ram = "Unavailable";
            var env = OsEnvironment.Detect(forceRefresh: true);
            string os = $"{env.ProductName} {env.DisplayVersion} ({env.Architecture}) · Build {env.BuildNumber}".Trim();
            string uacStatus = "Enabled (Optimal)";
            string defenderStatus = "Active (Protected)";

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
                using var results = searcher.Get();
                foreach (var obj in results)
                using (obj)
                {
                    cpu = obj["Name"]?.ToString()?.Trim() ?? cpu;
                    break;
                }
            }
            catch { }

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                using var results = searcher.Get();
                foreach (var obj in results)
                using (obj)
                {
                    gpu = obj["Name"]?.ToString()?.Trim() ?? gpu;
                    break;
                }
            }
            catch { }

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                using var results = searcher.Get();
                foreach (var obj in results)
                using (obj)
                {
                    if (ulong.TryParse(obj["TotalPhysicalMemory"]?.ToString(), out ulong bytes))
                        ram = $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
                    break;
                }
            }
            catch { }

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
                if (key?.GetValue("EnableLUA") is int lua && lua == 0)
                    uacStatus = "Disabled (Warning)";
            }
            catch { }

            try
            {
                using var sc = new ServiceController("WinDefend");
                if (sc.Status != ServiceControllerStatus.Running)
                    defenderStatus = $"Status: {sc.Status}";
            }
            catch { defenderStatus = "Unavailable"; }

            var disks = HardwareHealthService.GetDiskHealth();
            string smartSummary = string.Join(" | ", disks.Select(d => $"{d.Model}: {d.StatusText}"));
            bool smartWarn = disks.Any(d => d.PredictFailure == true);

            var battery = HardwareHealthService.GetBatteryHealth();
            bool rebootPending = HardwareHealthService.IsRebootPending();
            string sleepText = env.SupportsS3Sleep ? "S3 Supported" : env.SupportsModernStandby ? "Modern Standby (S0ix)" : "Standard";
            string firmwareText = $"{(rebootPending ? "Reboot Pending" : "No Reboot Pending")} · {sleepText}";

            DispatcherQueue.TryEnqueue(() =>
            {
                TxtCpuName.Text = cpu;
                TxtGpuName.Text = gpu;
                TxtRamTotal.Text = ram;
                TxtOsInfo.Text = os;
                TxtUacStatus.Text = uacStatus;
                TxtDefenderStatus.Text = defenderStatus;
                TxtSmartStatus.Text = smartSummary;
                if (smartWarn)
                    TxtSmartStatus.Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 255, 61, 87));
                TxtBatteryStatus.Text = battery.Summary;
                TxtFirmwareStatus.Text = firmwareText;
            });
        });
    }

    private void BtnOpenAudit_Click(object sender, RoutedEventArgs e) =>
        MainWindow.Current?.OpenAuditDrawer(showHistory: true);

    private async void BtnExportBundle_Click(object sender, RoutedEventArgs e)
    {
        BtnExportBundle.IsEnabled = false;
        try
        {
            string zipPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                $"JamesOptimizer_DiagBundle_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
            await SharedLogger.ExportDiagnosticBundleAsync(zipPath);
            MainWindow.Current?.ShowToast("Diagnostic Bundle Exported", Path.GetFileName(zipPath) + " saved to Desktop.", "State.Good");
        }
        catch (Exception ex)
        {
            MainWindow.Current?.ShowToast("Bundle Export Failed", ex.Message, "State.Bad");
        }
        finally
        {
            BtnExportBundle.IsEnabled = true;
        }
    }

    private async void BtnExportReport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BtnExportReport.IsEnabled = false;
            await OptimizationReportGenerator.GenerateAndOpenReportAsync();
            MainWindow.Current?.ShowToast("Report Exported", "Hardware & diagnostics summary generated cleanly.", "Accent.Cyan");
        }
        finally
        {
            BtnExportReport.IsEnabled = true;
        }
    }
}
