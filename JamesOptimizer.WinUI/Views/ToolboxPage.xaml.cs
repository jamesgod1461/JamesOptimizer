using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using JamesOptimizer;

namespace JamesOptimizer.WinUI.Views
{
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

        public ToolboxPage()
        {
            InitializeComponent();
            ContextMenuList.ItemsSource = ContextMenuEntries;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (ContextMenuEntries.Count == 0)
            {
                _ = LoadContextMenuEntriesAsync();
            }
        }

        #region Hardware Benchmark (0 - 100 Index)

        private async void BtnRunBenchmark_Click(object sender, RoutedEventArgs e)
        {
            BtnRunBenchmark.IsEnabled = false;
            TxtBenchmarkScore.Text = "..";
            TxtTier.Text = "BENCHMARKING...";
            App.ViewModel?.AddLog("BENCHMARK", "Starting multi-core CPU stress, RAM memory copy, and unbuffered disk benchmark...");

            try
            {
                var res = await Task.Run(async () => await OptimizationEngine.BenchmarkSystemAsync());

                TxtBenchmarkScore.Text = res.total.ToString();
                TxtCpuScore.Text = $"{res.cpu} / 100";
                TxtRamScore.Text = $"{res.ram} / 100";
                TxtDiskScore.Text = $"{res.disk} / 100";

                string tier;
                if (res.total >= 90) tier = "TIER: S+ ULTRA";
                else if (res.total >= 80) tier = "TIER: A+ HIGH PERF";
                else if (res.total >= 65) tier = "TIER: B BALANCED";
                else tier = "TIER: C ENTRY";

                TxtTier.Text = tier;

                App.ViewModel?.AddLog("BENCHMARK", $"Benchmark completed! Score: {res.total}/100 ({tier}) · CPU: {res.cpu} | RAM: {res.ram} | Disk: {res.disk}");
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

        #endregion

        #region Context Menu Cleaner

        private async Task LoadContextMenuEntriesAsync()
        {
            ContextMenuEntries.Clear();

            await Task.Run(() =>
            {
                string[] rootPaths = new[]
                {
                    @"*\shellex\ContextMenuHandlers",
                    @"Directory\shellex\ContextMenuHandlers",
                    @"Folder\shellex\ContextMenuHandlers"
                };

                foreach (var rp in rootPaths)
                {
                    try
                    {
                        using var key = Registry.ClassesRoot.OpenSubKey(rp);
                        if (key == null) continue;

                        foreach (var sub in key.GetSubKeyNames())
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
                            });
                        }
                    }
                    catch { }
                }
            });

            TxtContextMenuCount.Text = $"{ContextMenuEntries.Count} Extensions";
        }

        private async void BtnScanContextMenu_Click(object sender, RoutedEventArgs e)
        {
            await LoadContextMenuEntriesAsync();
            App.ViewModel?.AddLog("TOOLBOX", $"Context menu scan found {ContextMenuEntries.Count} shell extensions.");
        }

        private void ContextMenuToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleSwitch toggle && toggle.Tag is ContextMenuItemDisplay item)
            {
                try
                {
                    string targetKey = item.KeyPath.Replace(@"HKCR\", "");
                    using var key = Registry.ClassesRoot.OpenSubKey(targetKey, true);
                    if (key != null)
                    {
                        string clsid = item.Clsid;
                        if (toggle.IsOn)
                        {
                            if (clsid.StartsWith("-")) clsid = clsid.Substring(1);
                        }
                        else
                        {
                            if (!clsid.StartsWith("-")) clsid = "-" + clsid;
                        }

                        key.SetValue("", clsid);
                        item.Clsid = clsid;
                        item.IsActive = toggle.IsOn;
                        App.ViewModel?.AddLog("TOOLBOX", $"Shell extension '{item.Name}' set to {(toggle.IsOn ? "ACTIVE" : "DISABLED")}.");
                    }
                }
                catch (Exception ex)
                {
                    App.ViewModel?.AddLog("ERROR", $"Failed to toggle shell extension: {ex.Message}", true);
                }
            }
        }

        private void BtnRestartExplorer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("explorer"))
                {
                    try { p.Kill(); } catch { }
                }
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
                App.ViewModel?.AddLog("TOOLBOX", "Windows Explorer restarted successfully.");
            }
            catch (Exception ex)
            {
                App.ViewModel?.AddLog("ERROR", $"Restart explorer error: {ex.Message}", true);
            }
        }

        #endregion

        #region Utilities

        private void BtnGodMode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string godModePath = Path.Combine(desktop, "GodMode.{ED7BA470-8E54-465E-825C-99712043E01C}");
                if (!Directory.Exists(godModePath))
                {
                    Directory.CreateDirectory(godModePath);
                    App.ViewModel?.AddLog("TOOLBOX", "God Mode control panel folder created on your Desktop!");
                }
                else
                {
                    App.ViewModel?.AddLog("TOOLBOX", "God Mode already exists on your Desktop.");
                }
            }
            catch (Exception ex)
            {
                App.ViewModel?.AddLog("ERROR", $"Failed to create God Mode: {ex.Message}", true);
            }
        }

        private void BtnDismRepair_Click(object sender, RoutedEventArgs e)
        {
            App.ViewModel?.AddLog("TOOLBOX", "Launching DISM & SFC System Health Scan in background...");
            Task.Run(() =>
            {
                try
                {
                    var p = Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c sfc /scannow & DISM /Online /Cleanup-Image /RestoreHealth",
                        UseShellExecute = true,
                        Verb = "runas"
                    });
                }
                catch (Exception ex)
                {
                    DispatcherQueue.TryEnqueue(() => App.ViewModel?.AddLog("ERROR", $"DISM launch error: {ex.Message}", true));
                }
            });
        }

        private void BtnDebloater_Click(object sender, RoutedEventArgs e)
        {
            App.ViewModel?.AddLog("TOOLBOX", "Executing Windows Debloater PowerShell pipeline...");
            Task.Run(async () =>
            {
                await OptimizationEngine.RunWin11DebloaterAsync();
            });
        }

        #endregion
    }
}
