using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Brush = System.Windows.Media.Brush;

namespace JamesOptimizer
{
    /// <summary>
    /// Interaction logic for PerformanceTweaks.xaml
    /// James Optimizer V3 Architecture
    /// </summary>
    public partial class PerformanceTweaks : Page, INavigablePage
    {
        private bool _isBusy = false;

        public PerformanceTweaks()
        {
            InitializeComponent();
        }

        private void PerformanceTweaks_Loaded(object sender, RoutedEventArgs e)
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
            // Reset any active animations if needed
        }

        private void PlayStaggeredEntrance()
        {
            if (!ThemeManager.AnimationsEnabled)
            {
                // Show everything immediately, no motion
                foreach (var card in new FrameworkElement[] { Card1, Card2, Card3, Card4, Card5, BtnRunAllTweaks, BtnReg, BtnElite, BtnVisual, BtnGaming, BtnMouseFix, BtnKeyboardFix, BtnGpuTweaks, BtnWindowsDebloat, BtnNetworkFlush, BtnJamesRegedit, BtnUltimatePower, BtnCpuUnpark })
                {
                    card.Opacity = 1;
                    if (card.RenderTransform is TranslateTransform tt) tt.Y = 0;
                }
                return;
            }

            // Top row cards stagger 0/60/120ms
            AnimateElementIn(Card1, TransCard1, delayMs: 0, durationMs: 250, fromY: 16);
            AnimateElementIn(Card2, TransCard2, delayMs: 60, durationMs: 250, fromY: 16);
            AnimateElementIn(Card3, TransCard3, delayMs: 120, durationMs: 250, fromY: 16);

            // Bottom row cards stagger 80/140ms after top row (120+80=200ms, 120+140=260ms)
            AnimateElementIn(Card4, TransCard4, delayMs: 200, durationMs: 250, fromY: 16);
            AnimateElementIn(Card5, TransCard5, delayMs: 260, durationMs: 250, fromY: 16);

            // Bottom CTA button
            AnimateElementIn(BtnRunAllTweaks, TransBtnRunAllTweaks, delayMs: 300, durationMs: 250, fromY: 16);

            // Each card's buttons stagger internally after card appears
            // Card 1 buttons
            AnimateElementIn(BtnReg, TransBtnReg, delayMs: 30, durationMs: 200, fromY: 10);
            AnimateElementIn(BtnElite, TransBtnElite, delayMs: 60, durationMs: 200, fromY: 10);
            AnimateElementIn(BtnVisual, TransBtnVisual, delayMs: 90, durationMs: 200, fromY: 10);
            AnimateElementIn(BtnGaming, TransBtnGaming, delayMs: 120, durationMs: 200, fromY: 10);

            // Card 2 buttons
            AnimateElementIn(BtnMouseFix, TransBtnMouseFix, delayMs: 90, durationMs: 200, fromY: 10);
            AnimateElementIn(BtnKeyboardFix, TransBtnKeyboardFix, delayMs: 120, durationMs: 200, fromY: 10);
            AnimateElementIn(BtnGpuTweaks, TransBtnGpuTweaks, delayMs: 150, durationMs: 200, fromY: 10);

            // Card 3 buttons
            AnimateElementIn(BtnWindowsDebloat, TransBtnWindowsDebloat, delayMs: 150, durationMs: 200, fromY: 10);
            AnimateElementIn(BtnNetworkFlush, TransBtnNetworkFlush, delayMs: 180, durationMs: 200, fromY: 10);
            AnimateElementIn(BtnJamesRegedit, TransBtnJamesRegedit, delayMs: 210, durationMs: 200, fromY: 10);

            // Card 4 buttons
            AnimateElementIn(BtnUltimatePower, TransBtnUltimatePower, delayMs: 230, durationMs: 200, fromY: 10);
            AnimateElementIn(BtnCpuUnpark, TransBtnCpuUnpark, delayMs: 260, durationMs: 200, fromY: 10);
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
        // CARD OVERLAY CONTROLLER
        // ═════════════════════════════════════════════════════════════════
        private async Task ExecuteWithOverlayAsync(int cardIndex, string tweakName, Func<Task> action)
        {
            if (_isBusy) return;
            _isBusy = true;

            var overlay = GetCardOverlay(cardIndex);
            var spinnerHost = GetSpinnerHost(cardIndex);
            var spinnerRotate = GetSpinnerRotate(cardIndex);
            var checkHost = GetCheckHost(cardIndex);
            var statusText = GetStatusText(cardIndex);

            if (overlay != null && spinnerHost != null && spinnerRotate != null && checkHost != null && statusText != null)
            {
                overlay.Visibility = Visibility.Visible;
                spinnerHost.Visibility = Visibility.Visible;
                checkHost.Visibility = Visibility.Collapsed;
                statusText.Text = "Applying...";
                statusText.Foreground = (Brush)FindResource("TextSecondary");

                var spinAnim = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(800))
                {
                    RepeatBehavior = RepeatBehavior.Forever
                };
                spinnerRotate.BeginAnimation(RotateTransform.AngleProperty, spinAnim);
            }

            MainWindow.Instance?.ShowToast(tweakName, "Applying optimized registry and kernel parameters...", ToastType.Info);

            try
            {
                await action();

                if (spinnerRotate != null && spinnerHost != null && checkHost != null && statusText != null)
                {
                    spinnerRotate.BeginAnimation(RotateTransform.AngleProperty, null);
                    spinnerHost.Visibility = Visibility.Collapsed;
                    checkHost.Visibility = Visibility.Visible;
                    statusText.Text = "Done!";
                    statusText.Foreground = (Brush)FindResource("SuccessGreen");
                }

                MainWindow.Instance?.ShowToastSuccess($"{tweakName} Applied", "System configurations successfully optimized.");

                await Task.Delay(1500);
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToastError("Optimization Error", ex.Message);
            }
            finally
            {
                if (overlay != null)
                {
                    overlay.Visibility = Visibility.Collapsed;
                }
                _isBusy = false;
            }
        }

        private Border? GetCardOverlay(int index) => index switch
        {
            1 => OverlayCard1,
            2 => OverlayCard2,
            3 => OverlayCard3,
            4 => OverlayCard4,
            _ => null
        };

        private Grid? GetSpinnerHost(int index) => index switch
        {
            1 => SpinnerHostCard1,
            2 => SpinnerHostCard2,
            3 => SpinnerHostCard3,
            4 => SpinnerHostCard4,
            _ => null
        };

        private RotateTransform? GetSpinnerRotate(int index) => index switch
        {
            1 => SpinnerRotateCard1,
            2 => SpinnerRotateCard2,
            3 => SpinnerRotateCard3,
            4 => SpinnerRotateCard4,
            _ => null
        };

        private Viewbox? GetCheckHost(int index) => index switch
        {
            1 => CheckHostCard1,
            2 => CheckHostCard2,
            3 => CheckHostCard3,
            4 => CheckHostCard4,
            _ => null
        };

        private TextBlock? GetStatusText(int index) => index switch
        {
            1 => TxtStatusCard1,
            2 => TxtStatusCard2,
            3 => TxtStatusCard3,
            4 => TxtStatusCard4,
            _ => null
        };

        // ═════════════════════════════════════════════════════════════════
        // CARD 1: AGGRESSIVE OPTIMIZATIONS
        // ═════════════════════════════════════════════════════════════════
        private async void BtnReg_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteWithOverlayAsync(1, "Registry Repair", async () =>
            {
                await Task.Run(async () =>
                {
                    await OptimizationEngine.RunRegTweaksAsync(CancellationToken.None);
                });
            });
        }

        private async void BtnElite_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteWithOverlayAsync(1, "Elite Hardware Mode", async () =>
            {
                await Task.Run(async () =>
                {
                    await OptimizationEngine.RunEliteTweaksAsync(CancellationToken.None);
                });
            });
        }

        private async void BtnVisual_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteWithOverlayAsync(1, "Visual FX Optimizer", async () =>
            {
                await Task.Run(async () =>
                {
                    await OptimizationEngine.RunVisualOptimizeAsync(CancellationToken.None);
                });
            });
        }

        private async void BtnGaming_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteWithOverlayAsync(1, "Gaming Priority Mode", async () =>
            {
                await Task.Run(async () =>
                {
                    await OptimizationEngine.RunGamingBoostAsync(CancellationToken.None);
                });
            });
        }

        // ═════════════════════════════════════════════════════════════════
        // CARD 2: INPUT & PERIPHERAL
        // ═════════════════════════════════════════════════════════════════
        private async void BtnMouseFix_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteWithOverlayAsync(2, "Mouse Latency Fix", async () =>
            {
                await Task.Run(async () =>
                {
                    await OptimizationEngine.RunMouseFixAsync(CancellationToken.None);
                });
            });
        }

        private async void BtnKeyboardFix_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteWithOverlayAsync(2, "Keyboard Latency Fix", async () =>
            {
                await Task.Run(async () =>
                {
                    await OptimizationEngine.RunKeyboardFixAsync(CancellationToken.None);
                });
            });
        }

        private async void BtnGpuTweaks_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteWithOverlayAsync(2, "GPU Max Performance", async () =>
            {
                await Task.Run(async () =>
                {
                    await OptimizationEngine.RunGpuTweaksAsync(CancellationToken.None);
                });
            });
        }

        // ═════════════════════════════════════════════════════════════════
        // CARD 3: SYSTEM & NETWORK
        // ═════════════════════════════════════════════════════════════════
        private async void BtnWindowsDebloat_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteWithOverlayAsync(3, "Windows OS Debloat", async () =>
            {
                await Task.Run(async () =>
                {
                    await OptimizationEngine.RunWindowsDebloatAsync(CancellationToken.None);
                });
            });
        }

        private async void BtnNetworkFlush_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteWithOverlayAsync(3, "Network DNS Flush", async () =>
            {
                await Task.Run(async () =>
                {
                    await OptimizationEngine.RunNetworkFlushAsync(CancellationToken.None);
                });
            });
        }

        private async void BtnJamesRegedit_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteWithOverlayAsync(3, "James Regedit V2", async () =>
            {
                await Task.Run(async () =>
                {
                    await OptimizationEngine.RunJamesRegeditAsync(CancellationToken.None);
                });
            });
        }

        // ═════════════════════════════════════════════════════════════════
        // CARD 4: POWER & ENERGY
        // ═════════════════════════════════════════════════════════════════
        private async void BtnUltimatePower_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteWithOverlayAsync(4, "Ultimate Performance Plan", async () =>
            {
                await Task.Run(() =>
                {
                    try
                    {
                        var psiDuplicate = new ProcessStartInfo("powercfg", "-duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61")
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using var p1 = Process.Start(psiDuplicate);
                        p1?.WaitForExit(3000);

                        var psiActive = new ProcessStartInfo("powercfg", "/setactive e9a42b02-d5df-448d-aa00-03f14749eb61")
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using var p2 = Process.Start(psiActive);
                        p2?.WaitForExit(3000);

                        var psiHib = new ProcessStartInfo("powercfg", "-h off")
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using var p3 = Process.Start(psiHib);
                        p3?.WaitForExit(3000);
                    }
                    catch (Exception ex)
                    {
                        HardwareLogger.LogError("Ultimate power plan failed", ex);
                    }
                });
            });
        }

        private async void BtnCpuUnpark_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteWithOverlayAsync(4, "Unpark CPU Cores", async () =>
            {
                await Task.Run(() =>
                {
                    try
                    {
                        // Unpark all CPU cores via powercfg attributes
                        var psi1 = new ProcessStartInfo("powercfg", "-setacvalueindex scheme_current sub_processor CPMINCORES 100")
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using var p1 = Process.Start(psi1);
                        p1?.WaitForExit(3000);

                        var psi2 = new ProcessStartInfo("powercfg", "/setactive scheme_current")
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using var p2 = Process.Start(psi2);
                        p2?.WaitForExit(3000);
                    }
                    catch (Exception ex)
                    {
                        HardwareLogger.LogError("CPU unpark failed", ex);
                    }
                });
            });
        }

        // ═════════════════════════════════════════════════════════════════
        // BOTTOM CTA: RUN ALL ADVANCED TWEAKS NOW
        // ═════════════════════════════════════════════════════════════════
        private async void BtnRunAllTweaks_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy) return;
            _isBusy = true;
            BtnRunAllTweaks.IsEnabled = false;

            MainWindow.Instance?.ShowToast("Applying All Advanced Tweaks", "Executing registry, hardware, input, and power optimizations...", ToastType.Info, TimeSpan.FromSeconds(5));

            try
            {
                await Task.Run(async () =>
                {
                    // 1. Registry & Elite Tweaks
                    await OptimizationEngine.RunRegTweaksAsync(CancellationToken.None);
                    await OptimizationEngine.RunEliteTweaksAsync(CancellationToken.None);
                    await OptimizationEngine.RunVisualOptimizeAsync(CancellationToken.None);
                    await OptimizationEngine.RunGamingBoostAsync(CancellationToken.None);

                    // 2. Input Latency
                    await OptimizationEngine.RunMouseFixAsync(CancellationToken.None);
                    await OptimizationEngine.RunKeyboardFixAsync(CancellationToken.None);
                    await OptimizationEngine.RunGpuTweaksAsync(CancellationToken.None);

                    // 3. Network & Power
                    await OptimizationEngine.RunNetworkFlushAsync(CancellationToken.None);

                    // Power Plan
                    try
                    {
                        using var p1 = Process.Start(new ProcessStartInfo("powercfg", "-duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61") { CreateNoWindow = true, UseShellExecute = false });
                        p1?.WaitForExit(3000);
                        using var p2 = Process.Start(new ProcessStartInfo("powercfg", "/setactive e9a42b02-d5df-448d-aa00-03f14749eb61") { CreateNoWindow = true, UseShellExecute = false });
                        p2?.WaitForExit(3000);
                    }
                    catch { }
                });

                MainWindow.Instance?.ShowToastSuccess("All Tweaks Applied!", "Peak registry, hardware, and input latency parameters engaged.");
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToastError("Tweaks Failed", ex.Message);
            }
            finally
            {
                BtnRunAllTweaks.IsEnabled = true;
                _isBusy = false;
            }
        }
    }
}
