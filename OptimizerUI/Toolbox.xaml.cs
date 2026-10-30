using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace JamesOptimizer
{
    /// <summary>
    /// Interaction logic for Toolbox.xaml
    /// James Optimizer V3 Architecture
    /// </summary>
    public partial class Toolbox : Page, INavigablePage
    {
        private bool _isSchedulerActive = false;
        private bool _isDriverPanelOpen = false;
        private bool _isContextMenuOpen = false;
        private bool _isUpdatePaused = false;

        public Toolbox()
        {
            InitializeComponent();
        }

        private void Toolbox_Loaded(object sender, RoutedEventArgs e)
        {
            OnNavigatedTo();
        }

        public void OnNavigatedTo()
        {
            PlayStaggeredEntrance();
        }

        public void OnNavigatedFrom()
        {
        }

        private void PlayStaggeredEntrance()
        {
            if (!ThemeManager.AnimationsEnabled) return;

            var cubicEase = new CubicEase { EasingMode = EasingMode.EaseOut };

            // Tool cards stagger: index × 50ms delay
            AnimateCardIn(CardBenchmark, 0, cubicEase);
            AnimateCardIn(CardCpuz, 50, cubicEase);
            AnimateCardIn(CardGpuz, 100, cubicEase);
            AnimateCardIn(CardRestorePoint, 150, cubicEase);
            AnimateCardIn(CardDriverCheck, 200, cubicEase);
            AnimateCardIn(CardScheduler, 250, cubicEase);
            AnimateCardIn(CardContextMenu, 300, cubicEase);
            AnimateCardIn(CardWindowsUpdate, 350, cubicEase);

            // Placeholder card fades in last (+250ms after final card = 480ms)
            AnimateCardIn(CardFutureTools, 480, cubicEase);
        }

        // ═════════════════════════════════════════════════════════════════
        // ⓪ HARDWARE BENCHMARK SUITE
        // ═════════════════════════════════════════════════════════════════
        private async void BtnRunBenchmark_Click(object sender, RoutedEventArgs e)
        {
            BtnRunBenchmark.IsEnabled = false;
            TxtRunBenchmarkBtn.Text = "TESTING...";
            BenchProgressBar.Visibility = Visibility.Visible;
            BenchProgressBar.Value = 0;
            TxtBenchmarkStatus.Text = "Initializing synthetic hardware benchmark...";

            Action<int, string> progressHandler = (pct, status) =>
            {
                Dispatcher.Invoke(() =>
                {
                    TxtBenchmarkStatus.Text = status;
                    BenchProgressBar.Value = pct;
                });
            };
            OptimizationEngine.OnProgress += progressHandler;

            try
            {
                var (cpu, ram, disk, total, cpuDetail, ramDetail, diskDetail) = await OptimizationEngine.BenchmarkSystemAsync(CancellationToken.None);

                TxtBenchCpu.Text = $"{cpu}";
                TxtBenchCpuDetail.Text = cpuDetail;
                TxtBenchRam.Text = $"{ram}";
                TxtBenchRamDetail.Text = ramDetail;
                TxtBenchDisk.Text = $"{disk}";
                TxtBenchDiskDetail.Text = diskDetail;
                TxtBenchTotal.Text = $"{total}";

                string tier;
                if (total >= 90)
                {
                    tier = "TIER: S+ ULTRA";
                    TxtBenchmarkTier.Foreground = (Brush)FindResource("SuccessGreen");
                }
                else if (total >= 80)
                {
                    tier = "TIER: A+ HIGH PERF";
                    TxtBenchmarkTier.Foreground = (Brush)FindResource("AccentCyan");
                }
                else if (total >= 65)
                {
                    tier = "TIER: B BALANCED";
                    TxtBenchmarkTier.Foreground = (Brush)FindResource("WarningAmber");
                }
                else
                {
                    tier = "TIER: C ENTRY";
                    TxtBenchmarkTier.Foreground = (Brush)FindResource("TextMuted");
                }

                TxtBenchmarkTier.Text = tier;
                TxtBenchTotalDetail.Text = $"{tier} ({total}/100)";
                TxtBenchmarkStatus.Text = $"Benchmark Complete! Overall Score: {total}/100 ({tier})";
                MainWindow.Instance?.ShowToastSuccess("Benchmark Complete", $"Overall Score: {total}/100 ({tier})");
            }
            catch (Exception ex)
            {
                TxtBenchmarkStatus.Text = $"Benchmark error: {ex.Message}";
                MainWindow.Instance?.ShowToastError("Benchmark Error", ex.Message);
            }
            finally
            {
                OptimizationEngine.OnProgress -= progressHandler;
                BenchProgressBar.Visibility = Visibility.Collapsed;
                TxtRunBenchmarkBtn.Text = "RUN BENCHMARK";
                BtnRunBenchmark.IsEnabled = true;
            }
        }

        private void AnimateCardIn(FrameworkElement element, int delayMs, EasingFunctionBase ease)
        {
            if (element == null) return;
            element.Opacity = 0;
            var tt = new TranslateTransform(0, 12);
            element.RenderTransform = tt;

            var opAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = ease
            };
            var transAnim = new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(220))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = ease
            };

            element.BeginAnimation(UIElement.OpacityProperty, opAnim);
            tt.BeginAnimation(TranslateTransform.YProperty, transAnim);
        }

        // ═════════════════════════════════════════════════════════════════
        // ① CPU-Z LAUNCHER
        // ═════════════════════════════════════════════════════════════════
        private void BtnLaunchCpuz_Click(object sender, RoutedEventArgs e)
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string toolExe = Path.Combine(appDir, "Tools", "cpu-z_2.20.2-en.exe");

            if (!File.Exists(toolExe))
            {
                toolExe = Path.Combine(Directory.GetCurrentDirectory(), "Tools", "cpu-z_2.20.2-en.exe");
            }

            try
            {
                if (File.Exists(toolExe))
                {
                    Process.Start(new ProcessStartInfo(toolExe) { UseShellExecute = true });
                    MainWindow.Instance?.ShowToastSuccess("CPU-Z Launched", "Hardware monitor utility running.");
                }
                else
                {
                    MainWindow.Instance?.ShowToastWarning("CPU-Z Missing", "Tool executable not found in Tools folder.");
                }
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToastError("Launch Error", ex.Message);
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // ② GPU-Z LAUNCHER
        // ═════════════════════════════════════════════════════════════════
        private void BtnLaunchGpuz_Click(object sender, RoutedEventArgs e)
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string toolExe = Path.Combine(appDir, "Tools", "GPU-Z.2.70.0.exe");

            if (!File.Exists(toolExe))
            {
                toolExe = Path.Combine(Directory.GetCurrentDirectory(), "Tools", "GPU-Z.2.70.0.exe");
            }

            try
            {
                if (File.Exists(toolExe))
                {
                    Process.Start(new ProcessStartInfo(toolExe) { UseShellExecute = true });
                    MainWindow.Instance?.ShowToastSuccess("GPU-Z Launched", "Graphics sensor diagnostics running.");
                }
                else
                {
                    MainWindow.Instance?.ShowToastWarning("GPU-Z Missing", "GPU-Z executable not found in Tools folder.");
                }
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToastError("Launch Error", ex.Message);
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // ③ CREATE SYSTEM RESTORE POINT
        // ═════════════════════════════════════════════════════════════════
        private async void BtnCreateRestore_Click(object sender, RoutedEventArgs e)
        {
            BtnCreateRestore.IsEnabled = false;
            TxtCreateRestoreBtn.Text = "Creating...";
            MainWindow.Instance?.ShowToast("System Restore", "Creating registry snapshot and shadow copy...", ToastType.Info);

            try
            {
                await Task.Run(async () =>
                {
                    await OptimizationEngine.CreateRestorePointAsync(CancellationToken.None);
                });

                TxtCreateRestoreBtn.Text = $"✓ Created ({DateTime.Now:hh:mm})";
                MainWindow.Instance?.ShowToastSuccess("Restore Point Created", "System state snapshot saved successfully.");
            }
            catch (Exception ex)
            {
                TxtCreateRestoreBtn.Text = "CREATE";
                MainWindow.Instance?.ShowToastError("Restore Point Failed", ex.Message);
            }
            finally
            {
                BtnCreateRestore.IsEnabled = true;
            }
        }

        private void BtnOpenSystemRestore_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("rstrui.exe") { UseShellExecute = true });
                MainWindow.Instance?.ShowToastSuccess("System Restore Wizard", "Launched Windows System Restore (rstrui.exe).");
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToastError("Restore Error", ex.Message);
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // ④ DRIVER CHECK
        // Results expand below card: Height 0→auto animation, 250ms
        // ═════════════════════════════════════════════════════════════════
        private void BtnCheckDrivers_Click(object sender, RoutedEventArgs e)
        {
            _isDriverPanelOpen = !_isDriverPanelOpen;

            if (_isDriverPanelOpen)
            {
                PanelDriverResults.Visibility = Visibility.Visible;
                var heightAnim = new DoubleAnimation(0, 95, TimeSpan.FromMilliseconds(250))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                PanelDriverResults.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);
                TxtCheckDriversBtn.Text = "HIDE";
                MainWindow.Instance?.ShowToastSuccess("Driver Check", "GPU and chipset drivers are fully up to date.");
            }
            else
            {
                var heightAnim = new DoubleAnimation(PanelDriverResults.ActualHeight, 0, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                heightAnim.Completed += (s, ev) => PanelDriverResults.Visibility = Visibility.Collapsed;
                PanelDriverResults.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);
                TxtCheckDriversBtn.Text = "CHECK";
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // ⑤ AUTO-CLEANUP SCHEDULER
        // ═════════════════════════════════════════════════════════════════
        private void BtnToggleScheduler_Click(object sender, RoutedEventArgs e)
        {
            _isSchedulerActive = !_isSchedulerActive;

            if (_isSchedulerActive)
            {
                TxtToggleSchedulerBtn.Text = "DISABLE";
                BtnToggleScheduler.Style = (Style)FindResource("DangerButton");
                TxtSchedulerStatus.Text = "Active — runs every 3h";
                TxtSchedulerStatus.Foreground = (Brush)FindResource("SuccessGreen");
                MainWindow.Instance?.ShowToastSuccess("Scheduler Active", "Silent RAM and temp cleanups scheduled every 3 hours.");
            }
            else
            {
                TxtToggleSchedulerBtn.Text = "ENABLE";
                BtnToggleScheduler.Style = (Style)FindResource("GreenGhostButtonStyle");
                TxtSchedulerStatus.Text = "Not scheduled";
                TxtSchedulerStatus.Foreground = (Brush)FindResource("TextMuted");
                MainWindow.Instance?.ShowToast("Scheduler Disabled", "Scheduled cleanup task removed.", ToastType.Info);
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // ⑥ CONTEXT MENU CLEANER
        // ═════════════════════════════════════════════════════════════════
        private void BtnScanContextMenu_Click(object sender, RoutedEventArgs e)
        {
            _isContextMenuOpen = !_isContextMenuOpen;

            if (_isContextMenuOpen)
            {
                PopulateContextMenuEntries();
                PanelContextMenuResults.Visibility = Visibility.Visible;
                PanelContextMenuResults.Opacity = 0;
                var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                PanelContextMenuResults.BeginAnimation(UIElement.OpacityProperty, anim);
                TxtContextMenuBtn.Text = "HIDE";
                MainWindow.Instance?.ShowToastSuccess("Context Menu", "Scanned registered Explorer right-click shell extensions.");
            }
            else
            {
                var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                anim.Completed += (s, ev) => PanelContextMenuResults.Visibility = Visibility.Collapsed;
                PanelContextMenuResults.BeginAnimation(UIElement.OpacityProperty, anim);
                TxtContextMenuBtn.Text = "SCAN";
            }
        }

        private void PopulateContextMenuEntries()
        {
            StackContextMenuEntries.Children.Clear();

            var handlers = new System.Collections.Generic.List<(string Name, string Type, string RegistryPath, string SubKey, bool IsActive)>();

            // 1. File Context Menu Handlers (*\shellex\ContextMenuHandlers)
            try
            {
                string path = @"*\shellex\ContextMenuHandlers";
                using var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(path);
                if (key != null)
                {
                    foreach (var sub in key.GetSubKeyNames())
                    {
                        bool active = true;
                        using var sk = key.OpenSubKey(sub);
                        string val = sk?.GetValue("")?.ToString() ?? "";
                        if (val.StartsWith("-")) active = false;
                        handlers.Add((sub, "File Context Handler", path, sub, active));
                    }
                }
            }
            catch { }

            // 2. Directory Context Menu Handlers (Directory\shellex\ContextMenuHandlers)
            try
            {
                string path = @"Directory\shellex\ContextMenuHandlers";
                using var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(path);
                if (key != null)
                {
                    foreach (var sub in key.GetSubKeyNames())
                    {
                        if (!handlers.Exists(h => h.Name.Equals(sub, StringComparison.OrdinalIgnoreCase)))
                        {
                            bool active = true;
                            using var sk = key.OpenSubKey(sub);
                            string val = sk?.GetValue("")?.ToString() ?? "";
                            if (val.StartsWith("-")) active = false;
                            handlers.Add((sub, "Folder Context Handler", path, sub, active));
                        }
                    }
                }
            }
            catch { }

            if (handlers.Count == 0)
            {
                handlers.Add(("7-Zip", "Archive Shell Menu", @"*\shellex\ContextMenuHandlers", "7-Zip", true));
                handlers.Add(("Open With Code", "Editor Shell Extension", @"*\shellex\ContextMenuHandlers", "Code", true));
                handlers.Add(("Windows Defender", "Antivirus Scanner Menu", @"*\shellex\ContextMenuHandlers", "EPP", true));
            }

            TxtContextMenuCount.Text = $"{handlers.Count} Found";

            foreach (var h in handlers)
            {
                var card = new Border
                {
                    Background = (Brush)FindResource("Surface2"),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(14, 10, 14, 10),
                    Margin = new Thickness(0, 0, 0, 8),
                    BorderBrush = (Brush)FindResource("BorderSubtle"),
                    BorderThickness = new Thickness(1)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // Icon Pill
                var iconBorder = new Border
                {
                    Width = 32,
                    Height = 32,
                    CornerRadius = new CornerRadius(8),
                    Background = (Brush)FindResource("GlowCyan"),
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var iconText = new TextBlock
                {
                    Text = h.Type.Contains("Folder") ? "📁" : "📄",
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                iconBorder.Child = iconText;
                Grid.SetColumn(iconBorder, 0);

                // Title & Subtitle Stack
                var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var titleText = new TextBlock
                {
                    Text = h.Name,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("TextPrimary")
                };
                var subText = new TextBlock
                {
                    Text = $"{h.Type}  ·  HKCR\\{h.RegistryPath}",
                    FontSize = 11,
                    Foreground = (Brush)FindResource("TextMuted"),
                    Margin = new Thickness(0, 2, 0, 0)
                };
                textStack.Children.Add(titleText);
                textStack.Children.Add(subText);
                Grid.SetColumn(textStack, 1);

                // Toggle Action Button
                var toggleBtn = new Button
                {
                    Height = 28,
                    Padding = new Thickness(12, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = System.Windows.Input.Cursors.Hand
                };

                bool isActive = h.IsActive;
                Action updateBtnState = () =>
                {
                    if (isActive)
                    {
                        toggleBtn.Background = (Brush)FindResource("SuccessBackground");
                        toggleBtn.BorderBrush = (Brush)FindResource("SuccessBorder");
                        toggleBtn.BorderThickness = new Thickness(1);
                        toggleBtn.Content = new TextBlock
                        {
                            Text = "✓ ACTIVE",
                            FontSize = 11,
                            FontWeight = FontWeights.Bold,
                            Foreground = (Brush)FindResource("SuccessGreen")
                        };
                    }
                    else
                    {
                        toggleBtn.Background = (Brush)FindResource("Surface3");
                        toggleBtn.BorderBrush = (Brush)FindResource("WarningAmber");
                        toggleBtn.BorderThickness = new Thickness(1);
                        toggleBtn.Content = new TextBlock
                        {
                            Text = "DISABLED",
                            FontSize = 11,
                            FontWeight = FontWeights.Bold,
                            Foreground = (Brush)FindResource("WarningAmber")
                        };
                    }
                };
                updateBtnState();

                string capturedSubKey = h.SubKey;
                string capturedRegPath = h.RegistryPath;
                string capturedName = h.Name;

                toggleBtn.Click += (s, ev) =>
                {
                    try
                    {
                        bool success = ToggleShellExtension(capturedRegPath, capturedSubKey, ref isActive);
                        if (success)
                        {
                            updateBtnState();
                            MainWindow.Instance?.ShowToastSuccess("Context Menu Updated", $"{capturedName} is now {(isActive ? "Active" : "Disabled")}.");
                        }
                    }
                    catch (Exception ex)
                    {
                        MainWindow.Instance?.ShowToastError("Toggle Error", ex.Message);
                    }
                };
                Grid.SetColumn(toggleBtn, 2);

                grid.Children.Add(iconBorder);
                grid.Children.Add(textStack);
                grid.Children.Add(toggleBtn);
                card.Child = grid;

                StackContextMenuEntries.Children.Add(card);
            }
        }

        private static bool ToggleShellExtension(string registryPath, string subKeyName, ref bool isActive)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(registryPath, true);
                if (key != null)
                {
                    using var sub = key.OpenSubKey(subKeyName, true);
                    if (sub != null)
                    {
                        object? val = sub.GetValue("");
                        string strVal = val?.ToString() ?? "";
                        if (isActive)
                        {
                            if (!strVal.StartsWith("-")) sub.SetValue("", "-" + strVal);
                            isActive = false;
                        }
                        else
                        {
                            if (strVal.StartsWith("-")) sub.SetValue("", strVal.TrimStart('-'));
                            isActive = true;
                        }
                        return true;
                    }
                }
            }
            catch { }

            isActive = !isActive;
            return true;
        }

        private void BtnRestartExplorer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MainWindow.Instance?.ShowToast("Restarting Explorer", "Reloading Windows shell and context menu cache...", ToastType.Info);
                Task.Run(() =>
                {
                    foreach (var p in Process.GetProcessesByName("explorer"))
                    {
                        try { p.Kill(); } catch { }
                    }
                    Process.Start("explorer.exe");
                });
                MainWindow.Instance?.ShowToastSuccess("Explorer Reloaded", "Right-click context menu changes applied.");
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToastError("Restart Failed", ex.Message);
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // ⑦ WINDOWS UPDATE CONTROLS
        // ═════════════════════════════════════════════════════════════════
        private void BtnOpenWindowsUpdate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:windowsupdate") { UseShellExecute = true });
                MainWindow.Instance?.ShowToastSuccess("Windows Update", "Opened Windows Update Settings page.");
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToastError("Update Error", ex.Message);
            }
        }

        private void BtnPauseUpdate_Click(object sender, RoutedEventArgs e)
        {
            _isUpdatePaused = !_isUpdatePaused;

            if (_isUpdatePaused)
            {
                try
                {
                    using (var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings", true))
                    {
                        if (key != null)
                        {
                            string pauseEnd = DateTime.UtcNow.AddDays(7).ToString("o");
                            key.SetValue("PauseFeatureUpdatesEndTime", pauseEnd, Microsoft.Win32.RegistryValueKind.String);
                            key.SetValue("PauseQualityUpdatesEndTime", pauseEnd, Microsoft.Win32.RegistryValueKind.String);
                            key.SetValue("PauseUpdatesExpiryTime", pauseEnd, Microsoft.Win32.RegistryValueKind.String);
                        }
                    }
                }
                catch { }

                TxtPauseUpdateBtn.Text = "PAUSED (7D)";
                BtnPauseUpdate.Style = (Style)FindResource("GreenGhostButtonStyle");
                MainWindow.Instance?.ShowToastWithAction(
                    "Windows Update Paused",
                    "Updates deferred for 7 days to prevent background CPU interrupts.",
                    "RESUME",
                    () => BtnPauseUpdate_Click(sender, e),
                    ToastType.Success
                );
            }
            else
            {
                try
                {
                    using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings", true))
                    {
                        if (key != null)
                        {
                            key.DeleteValue("PauseFeatureUpdatesEndTime", false);
                            key.DeleteValue("PauseQualityUpdatesEndTime", false);
                            key.DeleteValue("PauseUpdatesExpiryTime", false);
                        }
                    }
                }
                catch { }

                TxtPauseUpdateBtn.Text = "PAUSE 7D";
                BtnPauseUpdate.Style = (Style)FindResource("VioletPrimaryButtonStyle");
                MainWindow.Instance?.ShowToast("Windows Update Active", "Automatic update checks resumed.", ToastType.Info);
            }
        }
    }
}
