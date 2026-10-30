using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace JamesOptimizer
{
    /// <summary>
    /// Interaction logic for SystemCleanups.xaml
    /// James Optimizer V3 Architecture
    /// </summary>
    public partial class SystemCleanups : Page, INavigablePage
    {
        public SystemCleanups()
        {
            InitializeComponent();
        }

        private void SystemCleanups_Loaded(object sender, RoutedEventArgs e)
        {
            OnNavigatedTo();
        }

        // ═════════════════════════════════════════════════════════════════
        // INavigablePage & STAGGERED ENTRANCE ANIMATIONS
        // ═════════════════════════════════════════════════════════════════
        public void OnNavigatedTo()
        {
            PlayStaggeredEntrance();
        }

        public void OnNavigatedFrom()
        {
            // Clean up any pending state if needed
        }

        private void PlayStaggeredEntrance()
        {
            if (!ThemeManager.AnimationsEnabled)
            {
                // Show everything immediately, no motion
                foreach (var card in new FrameworkElement[] { Card1, Card2, Card3, CardProTip, BtnCleanRam, BtnFlushDns, BtnFastTempClean, BtnClearShaderCaches, BtnDeepCleanup, BtnUltimateDebloat, BtnServiceDebloater })
                {
                    card.Opacity = 1;
                    if (card.RenderTransform is TranslateTransform tt) tt.Y = 0;
                }
                return;
            }

            // Cards enter staggered: Card1 at 0ms, Card2 at 60ms, Card3 at 120ms
            // Each card: opacity 0→1 + translateY 16→0, 250ms EaseOut
            AnimateElementIn(Card1, TransCard1, delayMs: 0, durationMs: 250, fromY: 16);
            AnimateElementIn(Card2, TransCard2, delayMs: 60, durationMs: 250, fromY: 16);
            AnimateElementIn(Card3, TransCard3, delayMs: 120, durationMs: 250, fromY: 16);

            // Buttons in each card stagger 30ms × index after card appears
            // Card 1 buttons
            AnimateElementIn(BtnCleanRam, TransBtnCleanRam, delayMs: 30, durationMs: 200, fromY: 10);
            AnimateElementIn(BtnFlushDns, TransBtnFlushDns, delayMs: 60, durationMs: 200, fromY: 10);
            AnimateElementIn(BtnFastTempClean, TransBtnFastTempClean, delayMs: 90, durationMs: 200, fromY: 10);

            // Card 2 buttons
            AnimateElementIn(BtnClearShaderCaches, TransBtnClearShaderCaches, delayMs: 90, durationMs: 200, fromY: 10);
            AnimateElementIn(BtnDeepCleanup, TransBtnDeepCleanup, delayMs: 120, durationMs: 200, fromY: 10);

            // Card 3 buttons
            AnimateElementIn(BtnUltimateDebloat, TransBtnUltimateDebloat, delayMs: 150, durationMs: 200, fromY: 10);
            AnimateElementIn(BtnServiceDebloater, TransBtnServiceDebloater, delayMs: 180, durationMs: 200, fromY: 10);

            // Pro Tip card enters last at 200ms delay
            AnimateElementIn(CardProTip, TransCardProTip, delayMs: 200, durationMs: 250, fromY: 16);
        }

        private static void AnimateElementIn(UIElement element, TranslateTransform transform, int delayMs, int durationMs, double fromY)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            var opacityAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(durationMs))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = ease
            };

            var transAnim = new DoubleAnimation(fromY, 0, TimeSpan.FromMilliseconds(durationMs))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = ease
            };

            element.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
            transform.BeginAnimation(TranslateTransform.YProperty, transAnim);
        }

        // ═════════════════════════════════════════════════════════════════
        // COLUMN 1: SYSTEM CLEANUPS HANDLERS
        // ═════════════════════════════════════════════════════════════════
        private async void BtnCleanRam_Click(object sender, RoutedEventArgs e)
        {
            BtnCleanRam.IsEnabled = false;
            MainWindow.Instance?.ShowToast("Flushing RAM", "Freeing standby memory and trimming working sets...", ToastType.Info);

            try
            {
                await Task.Run(async () =>
                {
                    await OptimizationEngine.RunRamFlushAsync(CancellationToken.None);
                });

                MainWindow.Instance?.ShowToastSuccess("RAM Flushed", "System memory cleaned. Cached memory freed instantly.");
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToastError("RAM Clean Failed", ex.Message);
            }
            finally
            {
                BtnCleanRam.IsEnabled = true;
            }
        }

        private async void BtnFlushDns_Click(object sender, RoutedEventArgs e)
        {
            BtnFlushDns.IsEnabled = false;
            MainWindow.Instance?.ShowToast("Flushing DNS", "Clearing DNS resolver cache and tuning TCP sockets...", ToastType.Info);

            try
            {
                await Task.Run(async () =>
                {
                    await OptimizationEngine.RunNetworkFlushAsync(CancellationToken.None);
                });

                MainWindow.Instance?.ShowToastSuccess("Network DNS Flushed", "DNS resolver cache successfully cleared.");
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToastError("DNS Flush Failed", ex.Message);
            }
            finally
            {
                BtnFlushDns.IsEnabled = true;
            }
        }

        private async void BtnFastTempClean_Click(object sender, RoutedEventArgs e)
        {
            BtnFastTempClean.IsEnabled = false;
            MainWindow.Instance?.ShowToast("Cleaning Temp Files", "Scanning and removing temporary junk files...", ToastType.Info);

            try
            {
                await Task.Run(async () =>
                {
                    await OptimizationEngine.RunFastCleanAsync(CancellationToken.None);
                });

                MainWindow.Instance?.ShowToastSuccess("Fast Clean Complete", "Temporary files and system caches safely purged.");
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToastError("Fast Clean Failed", ex.Message);
            }
            finally
            {
                BtnFastTempClean.IsEnabled = true;
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // COLUMN 2: ADVANCED CLEANUPS HANDLERS
        // ═════════════════════════════════════════════════════════════════
        private async void BtnClearShaderCaches_Click(object sender, RoutedEventArgs e)
        {
            BtnClearShaderCaches.IsEnabled = false;
            MainWindow.Instance?.ShowToast("Clearing Shaders", "Purging DirectX, Vulkan, NVIDIA and AMD shader caches...", ToastType.Info);

            try
            {
                await Task.Run(async () =>
                {
                    await OptimizationEngine.RunShaderClearAsync(CancellationToken.None);
                });

                MainWindow.Instance?.ShowToastSuccess("Shader Caches Cleared", "GPU shader compiler caches wiped. Stutters cleared.");
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToastError("Shader Cache Clear Failed", ex.Message);
            }
            finally
            {
                BtnClearShaderCaches.IsEnabled = true;
            }
        }

        private async void BtnDeepCleanup_Click(object sender, RoutedEventArgs e)
        {
            BtnDeepCleanup.IsEnabled = false;
            MainWindow.Instance?.ShowToast("Scanning Deep Junk", "Scanning Windows Update logs, crash dumps, and installers...", ToastType.Info);

            try
            {
                await Task.Run(async () =>
                {
                    var categories = await OptimizationEngine.ScanDeepCleanupAsync();
                    if (categories != null && categories.Count > 0)
                    {
                        await OptimizationEngine.ExecuteDeepCleanupAsync(categories);
                    }
                });

                MainWindow.Instance?.ShowToastSuccess("Deep System Cleanup Complete", "All discovered deep junk categories and logs purged.");
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToastError("Deep Cleanup Failed", ex.Message);
            }
            finally
            {
                BtnDeepCleanup.IsEnabled = true;
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // COLUMN 3: WINDOWS DEBLOAT HANDLERS
        // ═════════════════════════════════════════════════════════════════
        private async void BtnUltimateDebloat_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Ultimate Win 11 Debloat will completely remove telemetry, pre-installed bloatware apps, and unnecessary background tasks.\n\nWould you like to create a System Restore Point first for maximum safety?",
                "Create Restore Point First?",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel)
            {
                return;
            }

            BtnUltimateDebloat.IsEnabled = false;

            try
            {
                if (result == MessageBoxResult.Yes)
                {
                    MainWindow.Instance?.ShowToast("Creating Restore Point", "Generating system restore snapshot...", ToastType.Info);
                    await Task.Run(async () =>
                    {
                        await OptimizationEngine.CreateRestorePointAsync(CancellationToken.None);
                    });
                    MainWindow.Instance?.ShowToastSuccess("Restore Point Created", "System checkpoint created successfully.");
                }

                MainWindow.Instance?.ShowToast("Running Debloat", "Debloating Windows 11 apps, telemetry, and background services...", ToastType.Info);

                await Task.Run(async () =>
                {
                    await OptimizationEngine.RunWindowsDebloatAsync(CancellationToken.None);
                });

                MainWindow.Instance?.ShowToastSuccess("Windows 11 Debloated", "Bloatware uninstalled, telemetry eradicated, and privacy protected.");
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToastError("Debloat Error", ex.Message);
            }
            finally
            {
                BtnUltimateDebloat.IsEnabled = true;
            }
        }

        private async void BtnServiceDebloater_Click(object sender, RoutedEventArgs e)
        {
            BtnServiceDebloater.IsEnabled = false;
            MainWindow.Instance?.ShowToast("Debloating Services", "Stopping unnecessary diagnostic and tracking background services...", ToastType.Info);

            try
            {
                await Task.Run(async () =>
                {
                    await OptimizationEngine.RunServiceDebloaterAsync(CancellationToken.None);
                });

                MainWindow.Instance?.ShowToastSuccess("Services Debloated", "Unnecessary background tracking services successfully disabled.");
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToastError("Service Debloat Failed", ex.Message);
            }
            finally
            {
                BtnServiceDebloater.IsEnabled = true;
            }
        }
    }
}
