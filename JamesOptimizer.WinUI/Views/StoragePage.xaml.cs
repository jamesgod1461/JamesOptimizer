using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace JamesOptimizer.WinUI.Views
{
    public class DriveDisplayModel
    {
        public string Name { get; set; } = "";
        public string Format { get; set; } = "";
        public double UsedPercent { get; set; }
        public string Summary { get; set; } = "";
        public string FreeText { get; set; } = "";
    }

    public class LargeFileItem
    {
        public string FileName { get; set; } = "";
        public string FullPath { get; set; } = "";
        public string DisplaySize { get; set; } = "";
        public long SizeBytes { get; set; }
    }

    public sealed partial class StoragePage : Page
    {
        public ObservableCollection<DriveDisplayModel> Drives { get; } = new();
        public ObservableCollection<LargeFileItem> LargeFiles { get; } = new();

        public StoragePage()
        {
            InitializeComponent();
            DrivesList.ItemsSource = Drives;
            LargeFilesList.ItemsSource = LargeFiles;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (Drives.Count == 0)
            {
                LoadDrives();
            }
        }

        private void LoadDrives()
        {
            Drives.Clear();
            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                double totalGb = d.TotalSize / (1024.0 * 1024.0 * 1024.0);
                double freeGb = d.TotalFreeSpace / (1024.0 * 1024.0 * 1024.0);
                double usedGb = totalGb - freeGb;
                double percent = totalGb > 0 ? (usedGb / totalGb) * 100.0 : 0;

                string label = string.IsNullOrEmpty(d.VolumeLabel) ? "Local Disk" : d.VolumeLabel;

                Drives.Add(new DriveDisplayModel
                {
                    Name = $"{label} ({d.Name.TrimEnd('\\')})",
                    Format = $"{d.DriveFormat} Filesystem · Total {totalGb:0.0} GB",
                    UsedPercent = percent,
                    Summary = $"{usedGb:0.0} GB used of {totalGb:0.0} GB ({percent:0}%)",
                    FreeText = $"{freeGb:0.0} GB Free"
                });
            }
        }

        private async void BtnScanLargeFiles_Click(object sender, RoutedEventArgs e)
        {
            BtnScanLargeFiles.IsEnabled = false;
            LargeFilesProgress.Visibility = Visibility.Visible;
            LargeFiles.Clear();
            App.ViewModel?.AddLog("STORAGE", "Scanning user profile for files larger than 500 MB...");

            await Task.Run(() =>
            {
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var found = new System.Collections.Generic.List<LargeFileItem>();

                try
                {
                    var opt = new EnumerationOptions
                    {
                        IgnoreInaccessible = true,
                        RecurseSubdirectories = true,
                        MaxRecursionDepth = 5
                    };

                    foreach (var file in Directory.EnumerateFiles(userProfile, "*", opt))
                    {
                        try
                        {
                            var fi = new FileInfo(file);
                            if (fi.Length > 500L * 1024 * 1024) // > 500 MB
                            {
                                found.Add(new LargeFileItem
                                {
                                    FileName = fi.Name,
                                    FullPath = fi.FullName,
                                    SizeBytes = fi.Length,
                                    DisplaySize = FormatBytes(fi.Length)
                                });
                            }
                        }
                        catch { }
                    }
                }
                catch { }

                DispatcherQueue.TryEnqueue(() =>
                {
                    foreach (var f in found.OrderByDescending(f => f.SizeBytes).Take(20))
                    {
                        LargeFiles.Add(f);
                    }
                    LargeFilesProgress.Visibility = Visibility.Collapsed;
                    BtnScanLargeFiles.IsEnabled = true;
                    App.ViewModel?.AddLog("STORAGE", $"Scan completed. Found {LargeFiles.Count} large files.");
                });
            });
        }

        private void BtnLaunchCleanmgr_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("cleanmgr.exe") { UseShellExecute = true });
                App.ViewModel?.AddLog("STORAGE", "Launched Windows Disk Cleanup tool.");
            }
            catch (Exception ex)
            {
                App.ViewModel?.AddLog("ERROR", $"Failed to launch cleanmgr: {ex.Message}", true);
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1 && counter < suffixes.Length - 1)
            {
                number /= 1024;
                counter++;
            }
            return $"{number:n1} {suffixes[counter]}";
        }
    }
}
