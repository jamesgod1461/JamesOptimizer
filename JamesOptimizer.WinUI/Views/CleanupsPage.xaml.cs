using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using JamesOptimizer.WinUI.ViewModels;

namespace JamesOptimizer.WinUI.Views
{
    public class CleanupCategoryItem : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private string _displaySize = "Scanning...";
        private long _sizeBytes;

        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string[] TargetPaths { get; set; } = Array.Empty<string>();

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public string DisplaySize
        {
            get => _displaySize;
            set { _displaySize = value; OnPropertyChanged(); }
        }

        public long SizeBytes
        {
            get => _sizeBytes;
            set { _sizeBytes = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? prop = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    public sealed partial class CleanupsPage : Page
    {
        public ObservableCollection<CleanupCategoryItem> Categories { get; } = new();

        public CleanupsPage()
        {
            InitializeComponent();
            CleanupCategoriesList.ItemsSource = Categories;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (Categories.Count == 0)
            {
                InitCategories();
                _ = ScanAllAsync();
            }
        }

        private void InitCategories()
        {
            Categories.Clear();

            Categories.Add(new CleanupCategoryItem
            {
                Id = "temp",
                Title = "Windows Temporary Files",
                Description = "User and system temp files left behind by running applications.",
                TargetPaths = new[] { Path.GetTempPath(), @"C:\Windows\Temp" }
            });

            Categories.Add(new CleanupCategoryItem
            {
                Id = "prefetch",
                Title = "Windows Prefetch Cache",
                Description = "Stale application prefetch launch data that can accumulate over time.",
                TargetPaths = new[] { @"C:\Windows\Prefetch" }
            });

            Categories.Add(new CleanupCategoryItem
            {
                Id = "softwaredist",
                Title = "Windows Update Download Cache",
                Description = "Downloaded installation packages from completed or superseded updates.",
                TargetPaths = new[] { @"C:\Windows\SoftwareDistribution\Download" }
            });

            Categories.Add(new CleanupCategoryItem
            {
                Id = "delivery",
                Title = "Delivery Optimization Files",
                Description = "Cached peer-to-peer Windows update parts on local storage.",
                TargetPaths = new[] { @"C:\Windows\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache" }
            });

            Categories.Add(new CleanupCategoryItem
            {
                Id = "crashdumps",
                Title = "System Error Memory Dumps",
                Description = "Crash dump files (*.dmp) and WER reports.",
                TargetPaths = new[] { @"C:\Windows\Minidump", @"C:\Windows\MEMORY.DMP", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps") }
            });

            Categories.Add(new CleanupCategoryItem
            {
                Id = "thumbnails",
                Title = "Thumbnail & Icon Cache",
                Description = "Explorer thumbnail cache databases (thumbcache_*.db).",
                TargetPaths = new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Explorer") }
            });

            Categories.Add(new CleanupCategoryItem
            {
                Id = "browser",
                Title = "Web Browser Caches",
                Description = "Temporary web assets from Microsoft Edge, Chrome, and Brave.",
                TargetPaths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Edge\User Data\Default\Cache"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\User Data\Default\Cache")
                }
            });
        }

        private async Task ScanAllAsync()
        {
            ScanProgressBar.Visibility = Visibility.Visible;
            TxtScanStatus.Text = "Scanning system directories for junk...";

            long totalBytes = 0;

            await Task.Run(() =>
            {
                foreach (var cat in Categories)
                {
                    long catSize = 0;
                    foreach (var path in cat.TargetPaths)
                    {
                        catSize += MeasureDirectorySize(path);
                    }
                    cat.SizeBytes = catSize;
                    cat.DisplaySize = FormatBytes(catSize);
                    totalBytes += catSize;
                }
            });

            TxtTotalReclaimable.Text = FormatBytes(totalBytes);
            TxtScanStatus.Text = $"Scan complete. {Categories.Count} categories inspected.";
            ScanProgressBar.Visibility = Visibility.Collapsed;
        }

        private static long MeasureDirectorySize(string path)
        {
            if (File.Exists(path))
            {
                try { return new FileInfo(path).Length; } catch { return 0; }
            }
            if (!Directory.Exists(path)) return 0;

            long size = 0;
            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { size += new FileInfo(file).Length; } catch { }
                }
            }
            catch { }
            return size;
        }

        private async void BtnScanAll_Click(object sender, RoutedEventArgs e)
        {
            await ScanAllAsync();
        }

        private async void BtnCleanAll_Click(object sender, RoutedEventArgs e)
        {
            var selected = Categories.Where(c => c.IsSelected).ToList();
            if (selected.Count == 0) return;

            BtnCleanAll.IsEnabled = false;
            ScanProgressBar.Visibility = Visibility.Visible;
            TxtScanStatus.Text = "Cleaning selected junk categories...";

            long cleanedBytes = 0;

            await Task.Run(() =>
            {
                foreach (var cat in selected)
                {
                    foreach (var path in cat.TargetPaths)
                    {
                        cleanedBytes += CleanTarget(path);
                    }
                    cat.SizeBytes = 0;
                    cat.DisplaySize = "0.0 B";
                }
            });

            ScanProgressBar.Visibility = Visibility.Collapsed;
            BtnCleanAll.IsEnabled = true;
            TxtScanStatus.Text = $"Cleanup completed! Reclaimed {FormatBytes(cleanedBytes)}.";
            TxtTotalReclaimable.Text = "0.0 B";

            App.ViewModel?.AddLog("CLEANUP", $"Reclaimed {FormatBytes(cleanedBytes)} from {selected.Count} categories.");
        }

        private async void BtnCleanSingle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string id)
            {
                var cat = Categories.FirstOrDefault(c => c.Id == id);
                if (cat == null) return;

                btn.IsEnabled = false;
                long cleaned = 0;

                await Task.Run(() =>
                {
                    foreach (var path in cat.TargetPaths)
                    {
                        cleaned += CleanTarget(path);
                    }
                    cat.SizeBytes = 0;
                    cat.DisplaySize = "0.0 B";
                });

                btn.IsEnabled = true;
                App.ViewModel?.AddLog("CLEANUP", $"Cleaned '{cat.Title}', freed {FormatBytes(cleaned)}.");
            }
        }

        private static long CleanTarget(string path)
        {
            long freed = 0;
            if (File.Exists(path))
            {
                try
                {
                    var len = new FileInfo(path).Length;
                    File.Delete(path);
                    return len;
                }
                catch { return 0; }
            }

            if (!Directory.Exists(path)) return 0;

            try
            {
                var dir = new DirectoryInfo(path);
                foreach (var f in dir.GetFiles())
                {
                    try
                    {
                        long len = f.Length;
                        f.Delete();
                        freed += len;
                    }
                    catch { }
                }

                foreach (var d in dir.GetDirectories())
                {
                    try { d.Delete(true); } catch { }
                }
            }
            catch { }
            return freed;
        }

        private void ChkSelectAll_Checked(object sender, RoutedEventArgs e)
        {
            foreach (var c in Categories) c.IsSelected = true;
        }

        private void ChkSelectAll_Unchecked(object sender, RoutedEventArgs e)
        {
            foreach (var c in Categories) c.IsSelected = false;
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
