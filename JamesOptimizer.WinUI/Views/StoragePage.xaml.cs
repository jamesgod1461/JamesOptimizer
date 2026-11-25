using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using JamesOptimizer.Core;
using JamesOptimizer.WinUI.Core;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace JamesOptimizer.WinUI.Views;

public class DriveDisplayModel : INotifyPropertyChanged
{
    private double _animatedPercent;
    public string Name { get; set; } = "";
    public string Format { get; set; } = "";
    public double UsedPercent { get; set; }
    public double AnimatedPercent
    {
        get => _animatedPercent;
        set { _animatedPercent = value; OnPropertyChanged(); }
    }
    public string Summary { get; set; } = "";
    public string FreeText { get; set; } = "";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? prop = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
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
    public ObservableCollection<OptimizationEngine.ScannedFile> DuplicateItems { get; } = new();
    public ObservableCollection<OptimizationEngine.ScannedFile> DownloadItems { get; } = new();

    private List<List<OptimizationEngine.ScannedFile>> _duplicateGroups = new();
    private StorageNodeItem? _treemapRoot;

    public StoragePage()
    {
        InitializeComponent();
        DrivesList.ItemsSource = Drives;
        LargeFilesList.ItemsSource = LargeFiles;
        DuplicatesList.ItemsSource = DuplicateItems;
        DownloadsList.ItemsSource = DownloadItems;
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        // BUG FIX: Release Win2D DXGI device reference to prevent graphics resource leak on navigation.
        TreemapCanvas?.RemoveFromVisualTree();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        MotionHelper.AnimateStaggeredEntrance(new UIElement[] { DrivesList, CardLargeFiles }, staggerMs: 50, durationMs: 260);
        if (Drives.Count == 0)
        {
            LoadDrives();
            PopulateScanRoots();
        }
        _ = AnimateDriveGaugesAsync();
    }

    private void StorageTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;
        SegmentedPillHelper.SelectTab(tag,
            (TabVisualizer, PanelVisualizer, "VIS"),
            (TabDuplicates, PanelDuplicates, "DUP"),
            (TabDownloads, PanelDownloads, "DL"));
    }

    private void PopulateScanRoots()
    {
        ComboScanRoot.Items.Clear();
        ComboScanRoot.Items.Add(new ComboBoxItem { Content = "User Profile", Tag = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) });
        foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            ComboScanRoot.Items.Add(new ComboBoxItem { Content = $"Drive {d.Name.TrimEnd('\\')}", Tag = d.Name });
        }
        ComboScanRoot.SelectedIndex = 0;
    }

    private async Task AnimateDriveGaugesAsync()
    {
        if (!MotionHelper.ShouldAnimate)
        {
            foreach (var d in Drives) d.AnimatedPercent = d.UsedPercent;
            return;
        }
        foreach (var d in Drives) d.AnimatedPercent = 0;
        const int steps = 20;
        for (int i = 1; i <= steps; i++)
        {
            double t = (double)i / steps;
            double eased = 1.0 - Math.Pow(1.0 - t, 3);
            foreach (var d in Drives) d.AnimatedPercent = d.UsedPercent * eased;
            await Task.Delay(16);
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
                Format = $"{d.DriveFormat} · Total {totalGb:0.0} GB",
                UsedPercent = percent,
                Summary = $"{usedGb:0.0} GB used of {totalGb:0.0} GB ({percent:0}%)",
                FreeText = $"{freeGb:0.0} GB Free"
            });
        }
    }

    private async void BtnScanTreemap_Click(object sender, RoutedEventArgs e)
    {
        string root = (ComboScanRoot.SelectedItem as ComboBoxItem)?.Tag?.ToString()
                      ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        BtnScanTreemap.IsEnabled = false;
        LargeFilesProgress.Visibility = Visibility.Visible;
        LargeFiles.Clear();
        TxtVisualizerStatus.Text = $"Scanning '{root}'...";

        try
        {
            var (rootNode, largest, _) = await StorageVisualizerService.ScanPathAsync(root, maxDepth: 3);
            _treemapRoot = rootNode;
            foreach (var f in largest.Take(25))
            {
                LargeFiles.Add(new LargeFileItem
                {
                    FileName = f.Name,
                    FullPath = f.FullPath,
                    SizeBytes = f.SizeBytes,
                    DisplaySize = OptimizationEngine.FormatBytes(f.SizeBytes)
                });
            }
            TxtVisualizerStatus.Text = $"Scanned {OptimizationEngine.FormatBytes(rootNode.SizeBytes)} under '{root}'. Showing top {LargeFiles.Count} heavy files.";
            TreemapCanvas.Invalidate();
        }
        finally
        {
            LargeFilesProgress.Visibility = Visibility.Collapsed;
            BtnScanTreemap.IsEnabled = true;
        }
    }

    private static readonly CanvasTextFormat _treemapTextFormat = new()
    {
        FontFamily = "Cascadia Mono, Consolas",
        FontSize = 11,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
    };

    private void TreemapCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        float w = (float)sender.ActualWidth;
        float h = (float)sender.ActualHeight;
        if (w <= 0 || h <= 0 || _treemapRoot == null || _treemapRoot.Children.Count == 0)
            return;

        var palette = new[]
        {
            Theme.ThemeEffects.GetColor("Accent.Cyan.Color", ColorHelper.FromArgb(255, 34, 211, 238)),
            Theme.ThemeEffects.GetColor("Accent.Purple.Color", ColorHelper.FromArgb(255, 167, 139, 250)),
            Theme.ThemeEffects.GetColor("Accent.Green.Color", ColorHelper.FromArgb(255, 52, 211, 153)),
            Theme.ThemeEffects.GetColor("Accent.Yellow.Color", ColorHelper.FromArgb(255, 251, 191, 36)),
            Theme.ThemeEffects.GetColor("Accent.Blue.Color", ColorHelper.FromArgb(255, 96, 165, 250)),
            Theme.ThemeEffects.GetColor("Accent.Pink.Color", ColorHelper.FromArgb(255, 244, 114, 182))
        };
        var textColor = Theme.ThemeEffects.GetColor("Bg.App.Color", ColorHelper.FromArgb(255, 10, 10, 10));

        var items = _treemapRoot.Children.Where(c => c.SizeBytes > 0).Take(12).ToList();
        double total = Math.Max(1.0, items.Sum(c => (double)c.SizeBytes));
        float x = 4f, y = 4f, remW = w - 8f, remH = h - 8f;

        for (int i = 0; i < items.Count && remW > 12 && remH > 12; i++)
        {
            double frac = items[i].SizeBytes / total;
            bool horizontal = remW >= remH;
            float rw = horizontal ? (float)(remW * Math.Clamp(frac * 1.4, 0.08, 0.85)) : remW;
            float rh = horizontal ? remH : (float)(remH * Math.Clamp(frac * 1.4, 0.08, 0.85));
            if (i == items.Count - 1) { rw = remW; rh = remH; }

            var rect = new Windows.Foundation.Rect(x, y, Math.Max(4, rw - 3), Math.Max(4, rh - 3));
            var c = palette[i % palette.Length];
            ds.FillRoundedRectangle(rect, 8, 8, ColorHelper.FromArgb(215, c.R, c.G, c.B));
            if (rect.Width > 70 && rect.Height > 28)
            {
                string label = $"{items[i].Name} ({OptimizationEngine.FormatBytes(items[i].SizeBytes)})";
                ds.DrawText(label, (float)rect.X + 8, (float)rect.Y + 6, textColor, _treemapTextFormat);
            }

            if (horizontal) { x += rw; remW -= rw; }
            else { y += rh; remH -= rh; }
            total = Math.Max(1.0, items.Skip(i + 1).Sum(c => (double)c.SizeBytes));
        }
    }

    private void BtnRecycleLargeFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string path) return;
        if (NativeSystemHelper.MoveToRecycleBin(path))
        {
            var item = LargeFiles.FirstOrDefault(f => f.FullPath == path);
            if (item != null) LargeFiles.Remove(item);
            MainWindow.Current?.ShowToast("Moved to Recycle Bin", Path.GetFileName(path), "State.Good");
        }
    }

    private async void BtnScanDuplicates_Click(object sender, RoutedEventArgs e)
    {
        BtnScanDuplicates.IsEnabled = false;
        DupProgressBar.Visibility = Visibility.Visible;
        DupProgressBar.Value = 0;
        DuplicateItems.Clear();

        var exclusions = (TxtDupExclusions.Text ?? "")
            .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var prog = new Progress<int>(p => DispatcherQueue.TryEnqueue(() => DupProgressBar.Value = p));
        _duplicateGroups = await OptimizationEngine.ScanDuplicatesAsync(exclusions, prog);

        foreach (var grp in _duplicateGroups)
        {
            // Keep first (newest) unselected by default so all copies are never auto-deleted together
            for (int i = 0; i < grp.Count; i++)
            {
                grp[i].IsSelected = i > 0;
                DuplicateItems.Add(grp[i]);
            }
        }

        DupProgressBar.Visibility = Visibility.Collapsed;
        BtnScanDuplicates.IsEnabled = true;
        TxtDupStatus.Text = $"Found {_duplicateGroups.Count} duplicate group(s) ({DuplicateItems.Count} files).";
    }

    private void BtnKeepNewest_Click(object sender, RoutedEventArgs e)
    {
        DuplicateItems.Clear();
        foreach (var grp in _duplicateGroups)
        {
            var ordered = grp.OrderByDescending(f => f.LastModified).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                ordered[i].IsSelected = i > 0;
                DuplicateItems.Add(ordered[i]);
            }
        }
    }

    private void BtnKeepOldest_Click(object sender, RoutedEventArgs e)
    {
        DuplicateItems.Clear();
        foreach (var grp in _duplicateGroups)
        {
            var ordered = grp.OrderBy(f => f.LastModified).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                ordered[i].IsSelected = i > 0;
                DuplicateItems.Add(ordered[i]);
            }
        }
    }

    private async void BtnDeleteDuplicates_Click(object sender, RoutedEventArgs e)
    {
        // Safety: refuse if every file in a group was manually ticked
        var safeToRemove = new List<OptimizationEngine.ScannedFile>();
        foreach (var grp in _duplicateGroups)
        {
            var ticked = grp.Where(f => f.IsSelected).ToList();
            if (ticked.Count >= grp.Count && grp.Count > 0)
                ticked.RemoveAt(0); // preserve at least 1 copy
            safeToRemove.AddRange(ticked);
        }

        if (safeToRemove.Count == 0) return;
        int deleted = await OptimizationEngine.DeleteFilesSafeAsync(
            safeToRemove.Select(f => f.FilePath),
            permanentDelete: ChkPermanentDeleteDup.IsChecked == true);

        foreach (var f in safeToRemove) DuplicateItems.Remove(f);
        MainWindow.Current?.ShowToast("Duplicates Cleaned", $"Removed {deleted} duplicate file(s).", "State.Good");
    }

    private async void BtnScanDownloads_Click(object sender, RoutedEventArgs e)
    {
        BtnScanDownloads.IsEnabled = false;
        DownloadItems.Clear();
        int days = int.TryParse((ComboDownloadAge.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out int d) ? d : 0;
        var items = await OptimizationEngine.ScanDownloadsAsync(minAgeDays: days);
        foreach (var it in items) DownloadItems.Add(it);
        TxtDownloadsStatus.Text = $"Found {DownloadItems.Count} file(s) matching age filter.";
        BtnScanDownloads.IsEnabled = true;
    }

    private async void BtnRecycleDownloads_Click(object sender, RoutedEventArgs e)
    {
        var selected = DownloadItems.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0) return;
        int deleted = await OptimizationEngine.DeleteFilesSafeAsync(selected.Select(f => f.FilePath), permanentDelete: false);
        foreach (var f in selected) DownloadItems.Remove(f);
        MainWindow.Current?.ShowToast("Downloads Swept", $"Moved {deleted} file(s) to Recycle Bin.", "State.Good");
    }

    private void BtnLaunchCleanmgr_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(NativeSystemHelper.GetSystem32Exe("cleanmgr.exe")) { UseShellExecute = true });
        }
        catch { }
    }
}
