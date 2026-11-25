using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using JamesOptimizer.Core;
using JamesOptimizer.WinUI.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace JamesOptimizer.WinUI.Views;

public class CleanupCategoryItem : INotifyPropertyChanged
{
    private bool _isSelected = true;
    private string _displaySize = "Scanning...";
    private long _sizeBytes;

    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string[] TargetPaths { get; set; } = Array.Empty<string>();
    public bool DefaultSelected { get; set; } = true;

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
    private List<AppxPackageEntry> _allAppx = new();
    public ObservableCollection<AppxPackageEntry> DisplayedAppx { get; } = new();
    public ObservableCollection<StaleDriverPackage> StaleDrivers { get; } = new();

    public CleanupsPage()
    {
        InitializeComponent();
        CleanupCategoriesList.ItemsSource = Categories;
        AppxList.ItemsSource = DisplayedAppx;
        StaleDriversList.ItemsSource = StaleDrivers;
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        MotionHelper.AnimateStaggeredEntrance(new UIElement[] { CardCleanupHero, CleanupCategoriesList }, staggerMs: 50, durationMs: 260);
        if (Categories.Count == 0)
        {
            InitCategories();
            _ = ScanAllAsync();
        }
        RefreshWindowsOldNotice();
    }

    private void ModeTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string mode) return;
        var active = (Style)Application.Current.Resources["SegmentedPillActiveButtonStyle"];
        var normal = (Style)Application.Current.Resources["SegmentedPillButtonStyle"];

        TabJunk.Style = mode == "JUNK" ? active : normal;
        TabAppx.Style = mode == "APPX" ? active : normal;
        TabDeep.Style = mode == "DEEP" ? active : normal;

        PanelJunk.Visibility = mode == "JUNK" ? Visibility.Visible : Visibility.Collapsed;
        PanelAppx.Visibility = mode == "APPX" ? Visibility.Visible : Visibility.Collapsed;
        PanelDeep.Visibility = mode == "DEEP" ? Visibility.Visible : Visibility.Collapsed;

        if (mode == "APPX" && _allAppx.Count == 0)
            _ = LoadAppxAsync();
    }

    private void InitCategories()
    {
        Categories.Clear();
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        Categories.Add(new CleanupCategoryItem
        {
            Id = "temp",
            Title = "Windows Temporary Files",
            Description = "User and system temp files left behind by applications.",
            TargetPaths = new[] { Path.GetTempPath(), @"C:\Windows\Temp" }
        });

        Categories.Add(new CleanupCategoryItem
        {
            Id = "delivery",
            Title = "Delivery Optimization Cache",
            Description = "Cached peer-to-peer Windows update fragments on local storage.",
            TargetPaths = new[] { @"C:\Windows\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache" }
        });

        Categories.Add(new CleanupCategoryItem
        {
            Id = "crashdumps",
            Title = "System Error Memory Dumps & WER",
            Description = "Crash dump files (*.dmp) and Windows Error Reporting archives.",
            TargetPaths = new[] { @"C:\Windows\Minidump", @"C:\Windows\MEMORY.DMP", Path.Combine(localAppData, "CrashDumps"), @"C:\ProgramData\Microsoft\Windows\WER" }
        });

        Categories.Add(new CleanupCategoryItem
        {
            Id = "thumbnails",
            Title = "Thumbnail & Icon Cache",
            Description = "Explorer thumbnail cache databases (recreated automatically).",
            TargetPaths = new[] { Path.Combine(localAppData, @"Microsoft\Windows\Explorer") }
        });

        var browserPaths = DiscoverBrowserCachePaths(localAppData, appData, includeCookies: ChkIncludeCookies?.IsChecked == true);
        Categories.Add(new CleanupCategoryItem
        {
            Id = "browser",
            Title = "Multi-Browser & Multi-Profile Caches",
            Description = "Scans Default + Profile 1..N across Edge, Chrome, Brave, Opera, Vivaldi & Firefox (passwords & bookmarks untouched).",
            TargetPaths = browserPaths
        });

        Categories.Add(new CleanupCategoryItem
        {
            Id = "shadercache",
            Title = "DirectX & GPU Shader Caches",
            Description = "Warning: clearing D3DSCache / NVIDIA / AMD shader caches can cause brief stutter on first game launch while shaders rebuild.",
            DefaultSelected = false,
            IsSelected = false,
            TargetPaths = new[]
            {
                Path.Combine(localAppData, "D3DSCache"),
                Path.Combine(localAppData, @"NVIDIA\GLCache"),
                Path.Combine(localAppData, @"NVIDIA\DXCache"),
                Path.Combine(localAppData, @"AMD\DxCache")
            }
        });
    }

    private static string[] DiscoverBrowserCachePaths(string localAppData, string appData, bool includeCookies)
    {
        var paths = new List<string>();
        string[] chromiumRoots =
        {
            Path.Combine(localAppData, @"Microsoft\Edge\User Data"),
            Path.Combine(localAppData, @"Google\Chrome\User Data"),
            Path.Combine(localAppData, @"BraveSoftware\Brave-Browser\User Data"),
            Path.Combine(localAppData, @"Vivaldi\User Data"),
            Path.Combine(appData, @"Opera Software\Opera Stable"),
            Path.Combine(appData, @"Opera Software\Opera GX Stable")
        };

        string[] cacheSubDirs = { "Cache", "Code Cache", "GPUCache", @"Service Worker\CacheStorage", @"Crashpad\reports" };

        foreach (string root in chromiumRoots)
        {
            if (!Directory.Exists(root)) continue;
            var profileDirs = new List<string>();
            if (Directory.Exists(Path.Combine(root, "Default")))
                profileDirs.Add(Path.Combine(root, "Default"));
            try
            {
                profileDirs.AddRange(Directory.EnumerateDirectories(root, "Profile *"));
            }
            catch { }

            if (profileDirs.Count == 0)
                profileDirs.Add(root); // Opera single-profile layout

            foreach (string prof in profileDirs)
            {
                foreach (string sub in cacheSubDirs)
                {
                    string candidate = Path.Combine(prof, sub);
                    if (Directory.Exists(candidate)) paths.Add(candidate);
                }
                if (includeCookies)
                {
                    string cookies = Path.Combine(prof, @"Network\Cookies");
                    if (File.Exists(cookies)) paths.Add(cookies);
                }
            }
        }

        string ffProfiles = Path.Combine(localAppData, @"Mozilla\Firefox\Profiles");
        if (Directory.Exists(ffProfiles))
        {
            try
            {
                foreach (string p in Directory.EnumerateDirectories(ffProfiles))
                {
                    string c2 = Path.Combine(p, "cache2");
                    if (Directory.Exists(c2)) paths.Add(c2);
                }
            }
            catch { }
        }

        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private void ChkIncludeCookies_Changed(object sender, RoutedEventArgs e)
    {
        var browserCat = Categories.FirstOrDefault(c => c.Id == "browser");
        if (browserCat == null) return;
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        browserCat.TargetPaths = DiscoverBrowserCachePaths(localAppData, appData, ChkIncludeCookies.IsChecked == true);
        _ = ScanAllAsync();
    }

    private async Task ScanAllAsync()
    {
        ScanProgressBar.Visibility = Visibility.Visible;
        TxtScanStatus.Text = "Scanning system directories & browser profiles...";

        long totalBytes = 0;
        await Task.Run(() =>
        {
            foreach (var cat in Categories)
            {
                long catSize = cat.TargetPaths.Sum(p => DeepCleanupService.MeasurePathBytesSafe(p));
                string display = OptimizationEngine.FormatBytes(catSize);
                totalBytes += catSize;
                var captured = cat;
                DispatcherQueue.TryEnqueue(() =>
                {
                    captured.SizeBytes = catSize;
                    captured.DisplaySize = display;
                });
            }
        });

        TxtTotalReclaimable.Text = OptimizationEngine.FormatBytes(totalBytes);
        TxtScanStatus.Text = $"Scan complete. {Categories.Count} categories inspected.";
        ScanProgressBar.Visibility = Visibility.Collapsed;
    }

    private async void BtnScanAll_Click(object sender, RoutedEventArgs e) => await ScanAllAsync();

    private async void BtnCleanAll_Click(object sender, RoutedEventArgs e)
    {
        var selected = Categories.Where(c => c.IsSelected).ToList();
        if (selected.Count == 0) return;

        bool browserSelected = selected.Any(c => c.Id == "browser");
        if (browserSelected && IsAnyBrowserRunning(out string runningNames))
        {
            TxtScanStatus.Text = $"Note: running browser(s) detected ({runningNames}). Locked cache files will be skipped.";
        }

        BtnCleanAll.IsEnabled = false;
        ScanProgressBar.Visibility = Visibility.Visible;

        long cleanedBytes = 0;
        var remainingByCat = new Dictionary<CleanupCategoryItem, long>();
        await Task.Run(() =>
        {
            foreach (var cat in selected)
            {
                foreach (string path in cat.TargetPaths)
                    cleanedBytes += DeepCleanupService.CleanPathSafe(path);

                remainingByCat[cat] = cat.TargetPaths.Sum(p => DeepCleanupService.MeasurePathBytesSafe(p));
            }
        });

        foreach (var kv in remainingByCat)
        {
            kv.Key.SizeBytes = kv.Value;
            kv.Key.DisplaySize = OptimizationEngine.FormatBytes(kv.Value);
        }

        ScanProgressBar.Visibility = Visibility.Collapsed;
        BtnCleanAll.IsEnabled = true;
        TxtTotalReclaimable.Text = OptimizationEngine.FormatBytes(Categories.Sum(c => c.SizeBytes));
        TxtScanStatus.Text = $"Cleanup completed! Reclaimed {OptimizationEngine.FormatBytes(cleanedBytes)}.";
        App.ViewModel?.AddLog("CLEANUP", $"Reclaimed {OptimizationEngine.FormatBytes(cleanedBytes)} from {selected.Count} categories.");
        MainWindow.Current?.ShowToast($"Cleaned {OptimizationEngine.FormatBytes(cleanedBytes)}", $"Swept {selected.Count} categories cleanly.", "State.Good");
    }

    private static bool IsAnyBrowserRunning(out string names)
    {
        string[] procs = { "msedge", "chrome", "brave", "firefox", "opera", "vivaldi" };
        var running = new List<string>();
        foreach (string p in procs)
        {
            var found = Process.GetProcessesByName(p);
            if (found.Length > 0) running.Add(p);
            foreach (var fp in found) fp.Dispose();
        }
        names = string.Join(", ", running);
        return running.Count > 0;
    }

    private async void BtnCleanSingle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string id) return;
        var cat = Categories.FirstOrDefault(c => c.Id == id);
        if (cat == null) return;

        btn.IsEnabled = false;
        var (cleaned, remaining) = await Task.Run(() =>
        {
            long freed = cat.TargetPaths.Sum(p => DeepCleanupService.CleanPathSafe(p));
            long rem = cat.TargetPaths.Sum(p => DeepCleanupService.MeasurePathBytesSafe(p));
            return (freed, rem);
        });
        cat.SizeBytes = remaining;
        cat.DisplaySize = OptimizationEngine.FormatBytes(remaining);
        TxtTotalReclaimable.Text = OptimizationEngine.FormatBytes(Categories.Sum(c => c.SizeBytes));
        btn.IsEnabled = true;

        App.ViewModel?.AddLog("CLEANUP", $"Cleaned '{cat.Title}', freed {OptimizationEngine.FormatBytes(cleaned)}.");
        MainWindow.Current?.ShowToast($"Cleaned {OptimizationEngine.FormatBytes(cleaned)}", $"{cat.Title} purged.", "State.Good");
    }

    private void ChkSelectAll_Checked(object sender, RoutedEventArgs e)
    {
        foreach (var c in Categories) c.IsSelected = true;
    }

    private void ChkSelectAll_Unchecked(object sender, RoutedEventArgs e)
    {
        foreach (var c in Categories) c.IsSelected = false;
    }

    #region UWP / Appx Manager

    private async Task LoadAppxAsync()
    {
        BtnScanAppx.IsEnabled = false;
        TxtAppxStatus.Text = "Enumerating installed Appx packages via native PackageManager...";
        _allAppx = await AppxManagerService.GetInstalledPackagesAsync();
        ApplyAppxFilter();
        TxtAppxStatus.Text = $"Found {_allAppx.Count} installed packages (protected system/dependency packages locked).";
        BtnScanAppx.IsEnabled = true;
    }

    private void ApplyAppxFilter()
    {
        string q = (TxtAppxSearch?.Text ?? "").Trim();
        DisplayedAppx.Clear();
        foreach (var pkg in _allAppx)
        {
            if (string.IsNullOrEmpty(q) ||
                pkg.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                pkg.PackageFullName.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                DisplayedAppx.Add(pkg);
            }
        }
    }

    private void TxtAppxSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyAppxFilter();

    private async void BtnScanAppx_Click(object sender, RoutedEventArgs e) => await LoadAppxAsync();

    private async void BtnRemoveSelectedAppx_Click(object sender, RoutedEventArgs e)
    {
        var selected = _allAppx.Where(p => p.IsSelected && p.CanRemove).ToList();
        if (selected.Count == 0)
        {
            MainWindow.Current?.ShowToast("No Packages Selected", "Check removable Appx packages first.", "State.Warn");
            return;
        }

        BtnRemoveSelectedAppx.IsEnabled = false;
        int removed = 0, failed = 0;
        foreach (var pkg in selected)
        {
            var (ok, _) = await AppxManagerService.RemovePackageAsync(pkg);
            if (ok) removed++; else failed++;
        }

        await LoadAppxAsync();
        BtnRemoveSelectedAppx.IsEnabled = true;
        MainWindow.Current?.ShowToast("UWP Debloat Complete", $"Removed {removed} package(s) ({failed} failed). Store reinstall log saved.", removed > 0 ? "State.Good" : "State.Warn");
    }

    #endregion

    #region Deep System & Driver Store

    private void RefreshWindowsOldNotice()
    {
        if (DeepCleanupService.HasWindowsOld(out long bytes))
            TxtWindowsOldStatus.Text = $"Detected C:\\Windows.old (~{OptimizationEngine.FormatBytes(bytes)} sampled). Warning: removing it disables rolling back to your prior Windows version.";
        else
            TxtWindowsOldStatus.Text = "C:\\Windows.old is not present on this system.";
    }

    private async void BtnCleanWinSxS_Click(object sender, RoutedEventArgs e)
    {
        BtnCleanWinSxS.IsEnabled = false;
        TxtDeepStatus.Text = "Running DISM /StartComponentCleanup on WinSxS component store (may take a few minutes)...";
        try
        {
            var res = await DeepCleanupService.CleanWinSxSComponentStoreAsync();
            TxtDeepStatus.Text = res.ExitCode == 0
                ? "WinSxS Component Store cleanup completed cleanly."
                : $"DISM returned exit code {res.ExitCode} (ensure app is running as Administrator).";
        }
        finally
        {
            BtnCleanWinSxS.IsEnabled = true;
        }
    }

    private async void BtnCleanWuCache_Click(object sender, RoutedEventArgs e)
    {
        BtnCleanWuCache.IsEnabled = false;
        var (_, msg) = await DeepCleanupService.CleanWindowsUpdateCacheSafelyAsync();
        TxtDeepStatus.Text = msg;
        BtnCleanWuCache.IsEnabled = true;
    }

    private async void BtnScanDrivers_Click(object sender, RoutedEventArgs e)
    {
        BtnScanDrivers.IsEnabled = false;
        StaleDrivers.Clear();
        var list = await DeepCleanupService.FindStaleOemDriversAsync();
        foreach (var d in list) StaleDrivers.Add(d);
        TxtDeepStatus.Text = list.Count > 0
            ? $"Found {list.Count} superseded OEM driver package(s) in Driver Store."
            : "No superseded OEM driver packages found.";
        BtnScanDrivers.IsEnabled = true;
    }

    private async void BtnRemoveSelectedDrivers_Click(object sender, RoutedEventArgs e)
    {
        var selected = StaleDrivers.Where(d => d.IsSelected).ToList();
        if (selected.Count == 0) return;
        BtnRemoveSelectedDrivers.IsEnabled = false;
        int okCount = 0;
        foreach (var drv in selected)
        {
            var (ok, _) = await DeepCleanupService.RemoveOemDriverPackageAsync(drv.PublishedName);
            if (ok)
            {
                okCount++;
                StaleDrivers.Remove(drv);
            }
        }
        BtnRemoveSelectedDrivers.IsEnabled = true;
        MainWindow.Current?.ShowToast("Driver Store Cleanup", $"Removed {okCount} stale driver package(s).", "State.Good");
    }

    #endregion
}
