using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using JamesOptimizer.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;

namespace JamesOptimizer.WinUI.Views;

public class ContextMenuItemDisplay : INotifyPropertyChanged
{
    private bool _isActive;
    public string Name { get; set; } = "";
    public string KeyPath { get; set; } = "";
    public string Clsid { get; set; } = "";

    public bool IsActive
    {
        get => _isActive;
        set { _isActive = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? prop = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}

public sealed partial class ToolboxPage : Page
{
    public ObservableCollection<ContextMenuItemDisplay> ContextMenuEntries { get; } = new();
    public ObservableCollection<LockingProcessInfo> LockProcesses { get; } = new();
    public ObservableCollection<RunAliasEntry> RunAliases { get; } = new();
    public ObservableCollection<PathEntryItem> PathEntries { get; } = new();
    public ObservableCollection<WingetPackageItem> WingetPackages { get; } = new();

    public ToolboxPage()
    {
        InitializeComponent();
        ContextMenuList.ItemsSource = ContextMenuEntries;
        LockProcessesList.ItemsSource = LockProcesses;
        AliasesList.ItemsSource = RunAliases;
        PathEntriesList.ItemsSource = PathEntries;
        WingetList.ItemsSource = WingetPackages;
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (ContextMenuEntries.Count == 0)
            _ = LoadContextMenuEntriesAsync();
        LoadLatestBenchmarkSummary();

        if (!string.IsNullOrWhiteSpace(App.StartupFileLockTarget))
        {
            TxtLockTargetPath.Text = App.StartupFileLockTarget;
            App.StartupFileLockTarget = null;
            SwitchToolboxTab("LOCK");
            RefreshLocks();
        }
    }

    private void ToolboxTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
            SwitchToolboxTab(tag);
    }

    private void SwitchToolboxTab(string tag)
    {
        var active = (Style)Application.Current.Resources["SegmentedPillActiveButtonStyle"];
        var normal = (Style)Application.Current.Resources["SegmentedPillButtonStyle"];

        TabBenchmark.Style = tag == "BENCH" ? active : normal;
        TabFileLock.Style = tag == "LOCK" ? active : normal;
        TabIntegrator.Style = tag == "INTEG" ? active : normal;
        TabWinget.Style = tag == "WINGET" ? active : normal;

        PanelBench.Visibility = tag == "BENCH" ? Visibility.Visible : Visibility.Collapsed;
        PanelFileLock.Visibility = tag == "LOCK" ? Visibility.Visible : Visibility.Collapsed;
        PanelIntegrator.Visibility = tag == "INTEG" ? Visibility.Visible : Visibility.Collapsed;
        PanelWinget.Visibility = tag == "WINGET" ? Visibility.Visible : Visibility.Collapsed;

        if (tag == "INTEG")
        {
            RefreshAliases();
            RefreshPaths();
        }
        else if (tag == "WINGET" && WingetPackages.Count == 0)
        {
            LoadWingetCatalog();
        }
    }

    #region Hardware Benchmark & History Delta

    private void LoadLatestBenchmarkSummary()
    {
        var history = OptimizationEngine.GetBenchmarkHistory();
        if (history.Count == 0) return;

        var baseline = history[0];
        var latest = history[^1];
        TxtBenchmarkScore.Text = latest.TotalScore.ToString();
        TxtCpuScore.Text = $"{latest.CpuScore} / 100";
        TxtRamScore.Text = $"{latest.RamScore} / 100";
        TxtDiskScore.Text = $"{latest.DiskScore} / 100";
        TxtTier.Text = GetTierLabel(latest.TotalScore);

        if (history.Count >= 2)
        {
            int delta = latest.TotalScore - baseline.TotalScore;
            DeltaBadge.Visibility = Visibility.Visible;
            bool samePlan = baseline.PowerPlanGuid.Equals(latest.PowerPlanGuid, StringComparison.OrdinalIgnoreCase);
            TxtBenchDelta.Text = $"{(delta >= 0 ? "+" : "")}{delta} vs Baseline{(samePlan ? "" : " (Different Power Plan!)")}";
        }
        TxtBenchContext.Text = $"Last run: {latest.Timestamp:g} · Plan: {latest.PowerPlanName} · Build {latest.OsBuild}";
    }

    private static string GetTierLabel(int total) =>
        total >= 90 ? "TIER: S+ ULTRA"
        : total >= 80 ? "TIER: A+ HIGH PERF"
        : total >= 65 ? "TIER: B BALANCED"
        : "TIER: C ENTRY";

    private async void BtnRunBenchmark_Click(object sender, RoutedEventArgs e)
    {
        BtnRunBenchmark.IsEnabled = false;
        TxtBenchmarkScore.Text = "..";
        TxtTier.Text = "BENCHMARKING...";
        App.ViewModel?.AddLog("BENCHMARK", "Starting 3-run median CPU, RAM, and Direct I/O benchmark...");

        try
        {
            var res = await Task.Run(() => OptimizationEngine.BenchmarkSystemAsync());
            TxtBenchmarkScore.Text = res.total.ToString();
            TxtCpuScore.Text = $"{res.cpu} / 100 · {res.cpuDetail}";
            TxtRamScore.Text = $"{res.ram} / 100 · {res.ramDetail}";
            TxtDiskScore.Text = $"{res.disk} / 100 · {res.diskDetail}";
            TxtTier.Text = GetTierLabel(res.total);
            LoadLatestBenchmarkSummary();
            App.ViewModel?.AddLog("BENCHMARK", $"Benchmark complete: {res.total}/100 ({res.cpuDetail} | {res.ramDetail} | {res.diskDetail})");
        }
        catch (Exception ex)
        {
            TxtBenchmarkScore.Text = "ERR";
            TxtTier.Text = "ERROR";
            App.ViewModel?.AddLog("ERROR", $"Benchmark failed: {ex.Message}", true);
        }
        finally
        {
            BtnRunBenchmark.IsEnabled = true;
        }
    }

    private void BtnExportBenchCsv_Click(object sender, RoutedEventArgs e)
    {
        var history = OptimizationEngine.GetBenchmarkHistory();
        if (history.Count == 0)
        {
            MainWindow.Current?.ShowToast("No Benchmark History", "Run a benchmark first.", "State.Warn");
            return;
        }
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"JamesOptimizer_Benchmarks_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        var sb = new StringBuilder();
        sb.AppendLine("Timestamp,TotalScore,CpuScore,RamScore,DiskScore,CpuDetail,RamDetail,DiskDetail,PowerPlan,OsBuild");
        foreach (var h in history)
            sb.AppendLine($"{h.Timestamp:O},{h.TotalScore},{h.CpuScore},{h.RamScore},{h.DiskScore},\"{h.CpuDetail}\",\"{h.RamDetail}\",\"{h.DiskDetail}\",\"{h.PowerPlanName}\",{h.OsBuild}");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        MainWindow.Current?.ShowToast("CSV Exported", Path.GetFileName(path) + " saved to Desktop.", "State.Good");
    }

    #endregion

    #region Context Menu Cleaner & Quick Utilities

    private async Task LoadContextMenuEntriesAsync()
    {
        ContextMenuEntries.Clear();
        await Task.Run(() =>
        {
            string[] rootPaths =
            {
                @"*\shellex\ContextMenuHandlers",
                @"Directory\shellex\ContextMenuHandlers",
                @"Folder\shellex\ContextMenuHandlers"
            };
            foreach (string rp in rootPaths)
            {
                try
                {
                    using var key = Registry.ClassesRoot.OpenSubKey(rp);
                    if (key == null) continue;
                    foreach (string sub in key.GetSubKeyNames())
                    {
                        using var subKey = key.OpenSubKey(sub);
                        string? clsid = subKey?.GetValue("")?.ToString();
                        bool isActive = !string.IsNullOrEmpty(clsid) && !clsid.StartsWith("-");
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            ContextMenuEntries.Add(new ContextMenuItemDisplay
                            {
                                Name = sub,
                                KeyPath = $@"HKCR\{rp}\{sub}",
                                Clsid = clsid ?? "",
                                IsActive = isActive
                            });
                            TxtContextMenuCount.Text = $"{ContextMenuEntries.Count} Extensions";
                        });
                    }
                }
                catch { }
            }
        });
    }

    private async void BtnScanContextMenu_Click(object sender, RoutedEventArgs e) => await LoadContextMenuEntriesAsync();

    private void ContextMenuToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle || toggle.Tag is not ContextMenuItemDisplay item || !toggle.IsLoaded || item.IsActive == toggle.IsOn)
            return;

        try
        {
            string targetKey = item.KeyPath.Replace(@"HKCR\", "");
            using var key = Registry.ClassesRoot.OpenSubKey(targetKey, true);
            if (key == null) { toggle.IsOn = item.IsActive; return; }

            string clsid = item.Clsid;
            if (toggle.IsOn && clsid.StartsWith("-")) clsid = clsid[1..];
            else if (!toggle.IsOn && !clsid.StartsWith("-")) clsid = "-" + clsid;

            TweakBackupService.ApplyRegistryWithBackup("TOOLBOX", $"Context Menu: {item.Name}", $@"HKEY_CLASSES_ROOT\{targetKey}", "", clsid, RegistryValueKind.String, $"ctx_ext_{item.Name}");
            item.Clsid = clsid;
            item.IsActive = toggle.IsOn;
        }
        catch (Exception ex)
        {
            toggle.IsOn = item.IsActive;
            App.ViewModel?.AddLog("ERROR", $"Context menu toggle failed: {ex.Message}", true);
        }
    }

    private void BtnRestartExplorer_Click(object sender, RoutedEventArgs e)
    {
        foreach (var p in Process.GetProcessesByName("explorer"))
        using (p)
        {
            try { p.Kill(); } catch { }
        }
        Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = true });
    }

    private void BtnGodMode_Click(object sender, RoutedEventArgs e)
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "GodMode.{ED7BA470-8E54-465E-825C-99712043E01C}");
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        MainWindow.Current?.ShowToast("God Mode Ready", "Desktop shortcut created.", "State.Good");
    }

    private async void BtnDismRepair_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Current?.ShowToast("DISM & SFC Started", "Running /RestoreHealth in background...", "Accent.Cyan");
        await NativeSystemHelper.RunSystem32ToolAsync("Dism.exe", new[] { "/Online", "/Cleanup-Image", "/RestoreHealth" });
        await NativeSystemHelper.RunSystem32ToolAsync("sfc.exe", new[] { "/scannow" });
        MainWindow.Current?.ShowToast("System Repair Finished", "DISM & SFC completed.", "State.Good");
    }

    private void BtnDebloater_Click(object sender, RoutedEventArgs e) =>
        MainWindow.Current?.NavigateTo("Cleanups");

    #endregion

    #region File Lock Unlocker

    private void RefreshLocks()
    {
        LockProcesses.Clear();
        string target = (TxtLockTargetPath.Text ?? "").Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(target)) return;

        var locks = FileLockService.FindLockingProcesses(target);
        foreach (var l in locks) LockProcesses.Add(l);
        TxtLockStatus.Text = locks.Count > 0
            ? $"Found {locks.Count} process(es) locking '{Path.GetFileName(target)}'."
            : $"No Restart Manager locks detected on '{target}'.";
    }

    private void BtnFindLocks_Click(object sender, RoutedEventArgs e) => RefreshLocks();

    private void BtnGracefulShutdownLocks_Click(object sender, RoutedEventArgs e)
    {
        var (ok, msg) = FileLockService.ShutdownGracefully((TxtLockTargetPath.Text ?? "").Trim().Trim('"'));
        TxtLockStatus.Text = msg;
        RefreshLocks();
        MainWindow.Current?.ShowToast(ok ? "Graceful Close Sent" : "Close Failed", msg, ok ? "State.Good" : "State.Warn");
    }

    private void BtnKillLockingProc_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int pid) return;
        var (ok, msg) = FileLockService.TerminateLockingProcess(pid);
        TxtLockStatus.Text = msg;
        RefreshLocks();
        MainWindow.Current?.ShowToast(ok ? "Process Terminated" : "Protected Process", msg, ok ? "State.Good" : "State.Bad");
    }

    private void BtnScheduleDeleteReboot_Click(object sender, RoutedEventArgs e)
    {
        string target = (TxtLockTargetPath.Text ?? "").Trim().Trim('"');
        bool ok = NativeSystemHelper.ScheduleDeleteOnReboot(target);
        TxtLockStatus.Text = ok
            ? $"Scheduled '{target}' for deletion on next reboot (MoveFileEx)."
            : "Could not schedule reboot deletion (run as Administrator).";
    }

    private void BtnAddExplorerLockMenu_Click(object sender, RoutedEventArgs e)
    {
        string exe = Environment.ProcessPath ?? "";
        var (ok, msg) = IntegratorService.AddFileLockContextMenuShortcut(exe);
        TxtLockStatus.Text = msg;
        MainWindow.Current?.ShowToast(ok ? "Context Menu Added" : "Error", msg, ok ? "State.Good" : "State.Bad");
    }

    #endregion

    #region Environment Integrator

    private void RefreshAliases()
    {
        RunAliases.Clear();
        foreach (var a in IntegratorService.GetCustomRunAliases()) RunAliases.Add(a);
    }

    private void RefreshPaths()
    {
        PathEntries.Clear();
        foreach (var p in IntegratorService.InspectEnvironmentPaths()) PathEntries.Add(p);
    }

    private void BtnAddAlias_Click(object sender, RoutedEventArgs e)
    {
        var (ok, msg) = IntegratorService.AddRunAlias(TxtAliasName.Text, (TxtAliasExePath.Text ?? "").Trim().Trim('"'));
        TxtAliasStatus.Text = msg;
        if (ok) RefreshAliases();
        MainWindow.Current?.ShowToast(ok ? "Alias Registered" : "Alias Rejected", msg, ok ? "State.Good" : "State.Bad");
    }

    private void BtnRemoveAlias_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string alias) return;
        var (ok, msg) = IntegratorService.RemoveRunAlias(alias);
        TxtAliasStatus.Text = msg;
        if (ok) RefreshAliases();
    }

    private void BtnRefreshPath_Click(object sender, RoutedEventArgs e) => RefreshPaths();

    private void BtnCleanUserPath_Click(object sender, RoutedEventArgs e)
    {
        var (ok, msg) = IntegratorService.CleanDeadAndDuplicatePaths("User");
        TxtPathStatus.Text = msg;
        RefreshPaths();
    }

    private void BtnCleanSystemPath_Click(object sender, RoutedEventArgs e)
    {
        var (ok, msg) = IntegratorService.CleanDeadAndDuplicatePaths("System");
        TxtPathStatus.Text = msg;
        RefreshPaths();
    }

    #endregion

    #region winget Essentials

    private void LoadWingetCatalog()
    {
        WingetPackages.Clear();
        foreach (var p in WingetInstallerService.LoadCatalog()) WingetPackages.Add(p);
    }

    private async void BtnCheckWinget_Click(object sender, RoutedEventArgs e)
    {
        if (!await WingetInstallerService.IsWingetAvailableAsync())
        {
            TxtWingetStatus.Text = "winget not detected. Install 'App Installer' from Microsoft Store.";
            return;
        }
        var installed = await WingetInstallerService.GetInstalledPackageIdsAsync(WingetPackages.Select(p => p.Id));
        var updated = WingetPackages.ToList();
        WingetPackages.Clear();
        foreach (var pkg in updated)
        {
            pkg.IsInstalled = installed.Contains(pkg.Id);
            WingetPackages.Add(pkg);
        }
        TxtWingetStatus.Text = $"Checked {WingetPackages.Count} packages ({installed.Count} currently installed).";
    }

    private async void BtnInstallSelectedWinget_Click(object sender, RoutedEventArgs e)
    {
        var selected = WingetPackages.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0) return;
        BtnInstallSelectedWinget.IsEnabled = false;

        int okCount = 0;
        foreach (var pkg in selected)
        {
            TxtWingetStatus.Text = $"Installing {pkg.Name} ({pkg.Id})...";
            var (ok, msg) = await WingetInstallerService.InstallPackageAsync(pkg.Id);
            if (ok) okCount++;
            App.ViewModel?.AddLog("WINGET", msg, !ok);
        }

        TxtWingetStatus.Text = $"Installed/verified {okCount} of {selected.Count} selected packages.";
        BtnInstallSelectedWinget.IsEnabled = true;
    }

    private async void BtnUpgradeAllWinget_Click(object sender, RoutedEventArgs e)
    {
        BtnUpgradeAllWinget.IsEnabled = false;
        TxtWingetStatus.Text = "Running winget upgrade --all --source winget...";
        var (ok, msg) = await WingetInstallerService.UpgradeAllAsync();
        TxtWingetStatus.Text = msg;
        BtnUpgradeAllWinget.IsEnabled = true;
    }

    #endregion
}
