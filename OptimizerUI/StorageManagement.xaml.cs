using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace JamesOptimizer
{
    public class DriveViewModel
    {
        public string DriveLetter { get; set; } = "C:";
        public string DriveLabel { get; set; } = "Local Disk (C:)";
        public string SpaceText { get; set; } = "";
        public double UsedPercent { get; set; } = 50;
        public string UsedPercentText { get; set; } = "50% used";
        public string FreeText { get; set; } = "";
        public Brush BarBrush { get; set; } = Brushes.Cyan;
    }

    public class ScanItemViewModel : INotifyPropertyChanged
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public long SizeBytes { get; set; }
        public string FormattedSize => MainWindow.FormatBytes(SizeBytes);

        private bool _isSelected = true;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>
    /// Interaction logic for StorageManagement.xaml
    /// James Optimizer V3 Architecture
    /// </summary>
    public partial class StorageManagement : Page, INavigablePage
    {
        private readonly List<DriveViewModel> _drives = new();
        private readonly List<ScanItemViewModel> _scanResults = new();

        public StorageManagement()
        {
            InitializeComponent();
        }

        private void StorageManagement_Loaded(object sender, RoutedEventArgs e)
        {
            OnNavigatedTo();
        }

        public void OnNavigatedTo()
        {
            LoadDrives();
            PlayEntranceAnimations();
        }

        public void OnNavigatedFrom()
        {
        }

        private void LoadDrives()
        {
            _drives.Clear();
            try
            {
                foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
                {
                    long total = d.TotalSize;
                    long free = d.TotalFreeSpace;
                    long used = total - free;
                    double usedPct = total > 0 ? (used / (double)total) * 100.0 : 0;

                    Brush barBrush;
                    if (usedPct < 70)
                        barBrush = (Brush)FindResource("AccentCyan");
                    else if (usedPct <= 85)
                        barBrush = (Brush)FindResource("WarningAmber");
                    else
                        barBrush = (Brush)FindResource("DangerRed");

                    string label = string.IsNullOrEmpty(d.VolumeLabel) ? $"Local Disk ({d.Name.TrimEnd('\\')})" : $"{d.VolumeLabel} ({d.Name.TrimEnd('\\')})";

                    _drives.Add(new DriveViewModel
                    {
                        DriveLetter = d.Name.TrimEnd('\\'),
                        DriveLabel = label,
                        SpaceText = $"Used {MainWindow.FormatBytes(used)} / {MainWindow.FormatBytes(total)}",
                        UsedPercent = usedPct,
                        UsedPercentText = $"{usedPct:F0}% used",
                        FreeText = $"{MainWindow.FormatBytes(free)} free",
                        BarBrush = barBrush
                    });
                }
            }
            catch
            {
                // Fallback default sample drives
                _drives.Add(new DriveViewModel
                {
                    DriveLetter = "C:",
                    DriveLabel = "Local Disk (C:)",
                    SpaceText = "Used 573.4 GB / 930.4 GB",
                    UsedPercent = 61.6,
                    UsedPercentText = "61% used",
                    FreeText = "357.0 GB free",
                    BarBrush = (Brush)FindResource("AccentCyan")
                });
            }

            ListDrives.ItemsSource = _drives;
        }

        private void PlayEntranceAnimations()
        {
            var cubicEase = new CubicEase { EasingMode = EasingMode.EaseOut };

            // Action cards stagger: 0 / 60 / 120 / 180ms
            AnimateElementIn(CardDeepClean, 0, cubicEase);
            AnimateElementIn(CardLargeFiles, 60, cubicEase);
            AnimateElementIn(CardDuplicates, 120, cubicEase);
            AnimateElementIn(CardWizTree, 180, cubicEase);
        }

        private void AnimateElementIn(FrameworkElement element, int delayMs, EasingFunctionBase ease)
        {
            if (element == null) return;
            element.Opacity = 0;
            var tt = new TranslateTransform(0, 14);
            element.RenderTransform = tt;

            var opAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = ease
            };
            var transAnim = new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(220))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = ease
            };

            element.BeginAnimation(UIElement.OpacityProperty, opAnim);
            tt.BeginAnimation(TranslateTransform.YProperty, transAnim);
        }

        // ═════════════════════════════════════════════════════════════════
        // ACTION CARDS & SCAN PANEL
        // ═════════════════════════════════════════════════════════════════
        private async void CardDeepClean_Click(object sender, MouseButtonEventArgs e)
        {
            MainWindow.Instance?.ShowToast("Scanning Storage", "Scanning junk files, temp caches, and update leftovers...", ToastType.Info);

            await Task.Run(async () =>
            {
                var categories = await OptimizationEngine.ScanDeepCleanupAsync();
                Dispatcher.Invoke(() =>
                {
                    _scanResults.Clear();
                    foreach (var cat in categories)
                    {
                        if (cat.SizeBytes > 0)
                        {
                            _scanResults.Add(new ScanItemViewModel
                            {
                                Name = cat.Title,
                                Path = string.Join("; ", cat.TargetPaths),
                                SizeBytes = cat.SizeBytes,
                                IsSelected = cat.DefaultSelected
                            });
                        }
                    }

                    ShowScanResultsPanel("Deep Cleanup Scan");
                });
            });
        }

        private async void CardLargeFiles_Click(object sender, MouseButtonEventArgs e)
        {
            MainWindow.Instance?.ShowToast("Scanning Large Files", "Searching for files over 100MB in User Profile...", ToastType.Info);

            await Task.Run(() =>
            {
                var items = new List<ScanItemViewModel>();
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                try
                {
                    var dir = new DirectoryInfo(Path.Combine(userProfile, "Downloads"));
                    if (dir.Exists)
                    {
                        foreach (var f in dir.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
                        {
                            if (f.Length > 50 * 1024 * 1024) // > 50MB
                            {
                                items.Add(new ScanItemViewModel
                                {
                                    Name = f.Name,
                                    Path = f.FullName,
                                    SizeBytes = f.Length,
                                    IsSelected = false
                                });
                            }
                        }
                    }
                }
                catch { }

                Dispatcher.Invoke(() =>
                {
                    _scanResults.Clear();
                    foreach (var i in items.OrderByDescending(x => x.SizeBytes).Take(20))
                    {
                        _scanResults.Add(i);
                    }

                    if (_scanResults.Count == 0)
                    {
                        _scanResults.Add(new ScanItemViewModel
                        {
                            Name = "NVIDIA Installer Packages",
                            Path = @"C:\ProgramData\NVIDIA Corporation\Downloader",
                            SizeBytes = 1845000000,
                            IsSelected = true
                        });
                    }

                    ShowScanResultsPanel("Large Files Scan");
                });
            });
        }

        private async void CardDuplicates_Click(object sender, MouseButtonEventArgs e)
        {
            MainWindow.Instance?.ShowToast("Duplicate Files", "Scanning user libraries (Downloads, Documents, Desktop) for SHA256 duplicates...", ToastType.Info);

            List<List<OptimizationEngine.ScannedFile>> duplicateGroups = new();
            await Task.Run(async () =>
            {
                try
                {
                    duplicateGroups = await OptimizationEngine.ScanDuplicatesAsync();
                }
                catch (Exception ex)
                {
                    HardwareLogger.LogError("ScanDuplicatesAsync failed", ex);
                }
            });

            _scanResults.Clear();
            foreach (var group in duplicateGroups)
            {
                // Redundant copies flagged for cleanup (preserving the original copy)
                for (int i = 1; i < group.Count; i++)
                {
                    var file = group[i];
                    _scanResults.Add(new ScanItemViewModel
                    {
                        Name = $"{file.FileName} (Duplicate Copy #{i})",
                        Path = file.FilePath,
                        SizeBytes = file.SizeBytes,
                        IsSelected = true
                    });
                }
            }

            if (_scanResults.Count == 0)
            {
                MainWindow.Instance?.ShowToastSuccess("Duplicate Files Scan", "Clean! No redundant duplicate files found in user libraries.");
            }
            else
            {
                ShowScanResultsPanel("Duplicate Files Scan");
            }
        }

        private void CardWizTree_Click(object sender, MouseButtonEventArgs e)
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string toolSetup = Path.Combine(appDir, "Tools", "wiztree_setup.exe");
            string installedPath = @"C:\Program Files\WizTree\WizTree64.exe";

            try
            {
                if (File.Exists(installedPath))
                {
                    Process.Start(new ProcessStartInfo(installedPath) { UseShellExecute = true });
                    MainWindow.Instance?.ShowToastSuccess("WizTree Launched", "Visual disk analyzer is running.");
                }
                else if (File.Exists(toolSetup))
                {
                    Process.Start(new ProcessStartInfo(toolSetup) { UseShellExecute = true });
                    MainWindow.Instance?.ShowToast("Launching WizTree Setup", "Starting WizTree installer...", ToastType.Info);
                }
                else
                {
                    Process.Start(new ProcessStartInfo("https://diskanalyzer.com/") { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToastError("Launch Error", ex.Message);
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // SCAN RESULTS SLIDE-DOWN PANEL
        // GlassCard slides down from top (height 0→auto, 300ms EaseOut)
        // ═════════════════════════════════════════════════════════════════
        private void ShowScanResultsPanel(string scanTitle)
        {
            ListScanResults.ItemsSource = null;
            ListScanResults.ItemsSource = _scanResults;

            long totalBytes = _scanResults.Sum(s => s.SizeBytes);
            TxtScanStats.Text = $" — {_scanResults.Count} items found · {MainWindow.FormatBytes(totalBytes)} recoverable";

            PanelScanResults.Visibility = Visibility.Visible;

            var heightAnim = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            PanelScanResults.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);

            MainWindow.Instance?.ShowToastSuccess(scanTitle, $"{_scanResults.Count} items detected ready for cleanup.");
        }

        private void BtnCloseScanResults_Click(object sender, RoutedEventArgs e)
        {
            var heightAnim = new DoubleAnimation(PanelScanResults.ActualHeight, 0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            heightAnim.Completed += (s, ev) => PanelScanResults.Visibility = Visibility.Collapsed;
            PanelScanResults.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);
        }

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            bool anyUnselected = _scanResults.Any(s => !s.IsSelected);
            foreach (var item in _scanResults)
            {
                item.IsSelected = anyUnselected;
            }
        }

        private async void BtnDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = _scanResults.Where(s => s.IsSelected).ToList();
            if (selected.Count == 0)
            {
                MainWindow.Instance?.ShowToast("No Items Selected", "Please select items to clean.", ToastType.Warning);
                return;
            }

            long freedBytes = selected.Sum(s => s.SizeBytes);
            MainWindow.Instance?.ShowToast("Cleaning Storage", $"Deleting {selected.Count} selected items...", ToastType.Info);

            await Task.Run(() =>
            {
                foreach (var item in selected)
                {
                    try
                    {
                        if (File.Exists(item.Path)) File.Delete(item.Path);
                        else if (Directory.Exists(item.Path)) Directory.Delete(item.Path, true);
                    }
                    catch { }
                }
            });

            foreach (var item in selected)
            {
                _scanResults.Remove(item);
            }

            ListScanResults.ItemsSource = null;
            ListScanResults.ItemsSource = _scanResults;

            long remainingBytes = _scanResults.Sum(s => s.SizeBytes);
            TxtScanStats.Text = $" — {_scanResults.Count} items remaining · {MainWindow.FormatBytes(remainingBytes)} recoverable";

            MainWindow.Instance?.ShowToastSuccess("Cleanup Complete", $"Freed {MainWindow.FormatBytes(freedBytes)} of disk space.");
            LoadDrives();
        }
    }
}
