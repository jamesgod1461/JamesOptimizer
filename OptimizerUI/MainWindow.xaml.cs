using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace JamesOptimizer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// James Optimizer V3 Architecture
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool _isSidebarExpanded = true;
        private ToastManager? _toastManager;
        private System.Windows.Forms.NotifyIcon? _trayIcon;
        private bool _isRealExit = false;
        private WalkthroughController? _walkthrough;
        private readonly List<TextBlock> _navLabels = new();

        public static MainWindow? Instance { get; private set; }
        public ToastManager? Toasts => _toastManager;
        public NavigationFrame ContentHost => MainContentFrame;

        public MainWindow()
        {
            Instance = this;
            InitializeComponent();

            // Collect all nav labels for synchronized collapse/expand animations
            _navLabels.AddRange(new[]
            {
                LblDashboard,
                LblCleanups,
                LblTweaks,
                LblGaming,
                LblStartup,
                LblServices,
                LblNetwork,
                LblStorage,
                LblToolbox,
                LblDiagnostics,
                LblSettings,
                LblAbout
            });

            // Initialize Toast Manager
            _toastManager = new ToastManager(this, ToastStack);

            // Setup tray icon
            InitializeSystemTray();

            // Track WindowState changes to update restore / maximize icon
            StateChanged += MainWindow_StateChanged;

            // Global Command Palette Shortcut (Ctrl+K, Esc)
            KeyDown += MainWindow_KeyDown;
            InitCommandPalette();

            // Tips walkthrough (first-run + replay from Settings)
            _walkthrough = new WalkthroughController(TipsOverlay, this);
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1. Apply the DWM backdrop + per-theme window tint (ThemeManager
                //    was already initialized in App.OnStartup, before this window).
                ThemeManager.ApplyWindowBackdrop(RootBorder);

                // 2. Start rotating cyan circle logo animation
                StartLogoRotation();

                // 3. Detect and display OS version
                await DetectOsVersionAsync();

                // 4. Welcome toast notification
                _toastManager?.ShowSuccess("James Optimizer v3.2.0", "System engine loaded. Ready to optimize.");

                // 5. Navigate to Dashboard by default
                _ = MainContentFrame.NavigateWithAnimationAsync(new Dashboard());

                // 6. First run: introduce the app with the tips walkthrough after
                //    the dashboard has loaded and settled.
                if (_walkthrough != null && !_walkthrough.HasSeenWalkthrough())
                {
                    _ = Task.Delay(1200).ContinueWith(_ =>
                    {
                        try
                        {
                            Dispatcher.Invoke(() =>
                            {
                                if (IsLoaded && !_walkthrough.HasSeenWalkthrough())
                                {
                                    StartTips();
                                }
                            });
                        }
                        catch { /* window closed before the timer fired */ }
                    });
                }
            }
            catch (Exception ex)
            {
                HardwareLogger.LogError("MainWindow_Loaded error", ex);
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // TIPS WALKTHROUGH
        // ═════════════════════════════════════════════════════════════════

        /// <summary>
        /// Replays the guided tips walkthrough (also runs automatically on first launch).
        /// All steps target window chrome (sidebar, titlebar, footer), so it works
        /// from any page.
        /// </summary>
        public void StartTips()
        {
            try
            {
                if (CommandPaletteOverlay.Visibility == Visibility.Visible)
                {
                    CloseCommandPalette();
                }

                _walkthrough?.Start(BuildTipsSteps());
            }
            catch (Exception ex)
            {
                HardwareLogger.LogError("StartTips error", ex);
            }
        }

        private List<WalkthroughStep> BuildTipsSteps() => new()
        {
            new WalkthroughStep
            {
                TargetElement = SidebarNavStack,
                Title = "Navigate Anywhere",
                Description = "Every tool lives in the sidebar — cleanups, gaming mode, network boosts, storage and more. The active page glows with your accent color.",
                CalloutPosition = CalloutPosition.Right
            },
            new WalkthroughStep
            {
                TargetElement = BtnToggleSidebar,
                Title = "Collapse the Rail",
                Description = "Want more room? Collapse the sidebar to a slim icon rail. It expands back the moment you hover it.",
                CalloutPosition = CalloutPosition.Bottom
            },
            new WalkthroughStep
            {
                TargetElement = StatusSystemPill,
                Title = "Live System Status",
                Description = "This pill shows your OS and engine state at a glance. Green means every optimization subsystem is ready.",
                CalloutPosition = CalloutPosition.Bottom
            },
            new WalkthroughStep
            {
                TargetElement = BtnOpenCommandPalette,
                Title = "Command Everything",
                Description = "Press Ctrl+K (or click here) to open the command runner — jump to any page or run one-click optimizations without leaving the keyboard.",
                CalloutPosition = CalloutPosition.Bottom
            },
            new WalkthroughStep
            {
                TargetElement = FooterExpanded,
                Title = "You're All Set",
                Description = "Make it yours in Settings: dark & light themes, five accent colors, backdrop effects and motion preferences. Replay these tips anytime from Settings.",
                CalloutPosition = CalloutPosition.Top
            },
        };

        // ═════════════════════════════════════════════════════════════════
        // LOGO ROTATION & DYNAMIC BREATHING ANIMATION
        // ═════════════════════════════════════════════════════════════════
        private void StartLogoRotation()
        {
            try
            {
                // 1. Outer Gyro Orbital Continuous Rotation
                var outerRotation = new DoubleAnimation
                {
                    From = 0,
                    To = 360,
                    Duration = TimeSpan.FromSeconds(8),
                    RepeatBehavior = RepeatBehavior.Forever
                };
                LogoRotationTransform?.BeginAnimation(RotateTransform.AngleProperty, outerRotation);

                // 2. Inner Hex Counter-Rotation
                var innerRotation = new DoubleAnimation
                {
                    From = 360,
                    To = 0,
                    Duration = TimeSpan.FromSeconds(14),
                    RepeatBehavior = RepeatBehavior.Forever
                };
                LogoCounterRotationTransform?.BeginAnimation(RotateTransform.AngleProperty, innerRotation);

                // 3. Central Speed Core Breathing Scale Pulse
                var sineEase = new SineEase { EasingMode = EasingMode.EaseInOut };
                var corePulse = new DoubleAnimation
                {
                    From = 0.94,
                    To = 1.06,
                    Duration = TimeSpan.FromSeconds(1.8),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = sineEase
                };
                LogoCoreScale?.BeginAnimation(ScaleTransform.ScaleXProperty, corePulse);
                LogoCoreScale?.BeginAnimation(ScaleTransform.ScaleYProperty, corePulse);

                // 4. Central Core Glow Pulse
                var glowPulse = new DoubleAnimation
                {
                    From = 8,
                    To = 18,
                    Duration = TimeSpan.FromSeconds(1.8),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = sineEase
                };
                LogoCoreGlow?.BeginAnimation(DropShadowEffect.BlurRadiusProperty, glowPulse);
            }
            catch (Exception ex)
            {
                HardwareLogger.LogError("StartLogoRotation error", ex);
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // OS VERSION DETECTION
        // ═════════════════════════════════════════════════════════════════
        private async Task DetectOsVersionAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    string osName = "Windows 11 Pro";
                    string osArch = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";

                    using (var searcher = new ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem"))
                    {
                        foreach (var mObj in searcher.Get())
                        {
                            osName = mObj["Caption"]?.ToString() ?? "Windows";
                            break;
                        }
                    }

                    osName = osName.Replace("Microsoft", "").Trim();
                    bool isWin11 = Environment.OSVersion.Version.Build >= 22000;
                    if (isWin11 && osName.Contains("Windows 10"))
                    {
                        osName = osName.Replace("Windows 10", "Windows 11");
                    }

                    Dispatcher.Invoke(() =>
                    {
                        TxtOsVersion.Text = $"{osName} · {osArch}";
                    });
                }
                catch
                {
                    Dispatcher.Invoke(() =>
                    {
                        TxtOsVersion.Text = "Windows 11 · 64-bit";
                    });
                }
            });
        }

        // ═════════════════════════════════════════════════════════════════
        // CAPTION BUTTONS (Minimize / Maximize / Close)
        // ═════════════════════════════════════════════════════════════════
        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            // Respect the "close button minimizes to tray" preference.
            if (ThemeManager.Settings.CloseToTray)
            {
                Hide();
                return;
            }

            _isRealExit = true;
            if (_trayIcon != null) _trayIcon.Visible = false;
            Close();
            Application.Current.Shutdown();
        }

        /// <summary>
        /// Re-applies the DWM backdrop + immersive dark mode and the per-theme
        /// window tint. Called by ThemeManager after a theme or backdrop change.
        /// A brief opacity dip masks the hard resource-dictionary swap so the
        /// theme change reads as a crossfade rather than a flash.
        /// </summary>
        public void ApplyWindowChromeTheme()
        {
            WindowBackdropHelper.SetImmersiveDarkMode(this, ThemeManager.IsDarkTheme);
            ThemeManager.ApplyWindowBackdrop(RootBorder);

            if (ThemeManager.AnimationsEnabled)
            {
                // Gentle fade: opacity only (no movement), 180ms ease-out.
                var fade = new DoubleAnimation(0.72, 1, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                RootBorder.BeginAnimation(UIElement.OpacityProperty, fade);
            }
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                IconMaximize.Visibility = Visibility.Collapsed;
                IconRestore.Visibility = Visibility.Visible;
                RootBorder.Margin = new Thickness(7); // Account for window chrome maximize overflow
            }
            else
            {
                IconMaximize.Visibility = Visibility.Visible;
                IconRestore.Visibility = Visibility.Collapsed;
                RootBorder.Margin = new Thickness(0);
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // SIDEBAR TOGGLE & ANIMATION
        // Width: 64px collapsed, 220px expanded
        // Toggle animation: Width 64↔220, 280ms cubic-bezier(0.4, 0, 0.2, 1)
        // Labels fade in with 80ms delay after expand starts (opacity 0→1, 150ms)
        // Labels fade out immediately before collapse (opacity 1→0, 100ms)
        // ═════════════════════════════════════════════════════════════════
        private void BtnToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            SetSidebarExpanded(!_isSidebarExpanded);
        }

        public void SetSidebarExpanded(bool expand)
        {
            if (_isSidebarExpanded == expand) return;
            _isSidebarExpanded = expand;

            double fromWidth = SidebarContainer.ActualWidth > 0 ? SidebarContainer.ActualWidth : (_isSidebarExpanded ? 64 : 220);
            double toWidth = expand ? 220 : 64;

            var cubicEase = new CubicEase { EasingMode = EasingMode.EaseInOut };

            if (expand)
            {
                // 1. Animate Sidebar width 64 → 220 (280ms)
                var widthAnim = new DoubleAnimation(fromWidth, toWidth, TimeSpan.FromMilliseconds(280))
                {
                    EasingFunction = cubicEase
                };
                SidebarContainer.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);

                // 2. Labels fade in with 80ms delay after expand starts (opacity 0→1, 150ms)
                foreach (var lbl in _navLabels)
                {
                    lbl.Visibility = Visibility.Visible;
                    var labelAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
                    {
                        BeginTime = TimeSpan.FromMilliseconds(80),
                        EasingFunction = cubicEase
                    };
                    lbl.BeginAnimation(UIElement.OpacityProperty, labelAnim);
                }

                // 3. Footer bottom section: Show full "James God" name + version text
                FooterCollapsed.Visibility = Visibility.Collapsed;
                FooterExpanded.Visibility = Visibility.Visible;
                var footerAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
                {
                    BeginTime = TimeSpan.FromMilliseconds(80),
                    EasingFunction = cubicEase
                };
                FooterExpanded.BeginAnimation(UIElement.OpacityProperty, footerAnim);
            }
            else
            {
                // 1. Labels fade out immediately before collapse (opacity 1→0, 100ms)
                foreach (var lbl in _navLabels)
                {
                    var labelAnim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(100))
                    {
                        EasingFunction = cubicEase
                    };
                    labelAnim.Completed += (s, e) =>
                    {
                        if (!_isSidebarExpanded)
                        {
                            lbl.Visibility = Visibility.Collapsed;
                        }
                    };
                    lbl.BeginAnimation(UIElement.OpacityProperty, labelAnim);
                }

                // 2. Footer bottom section: Only show "JE" badge when collapsed
                FooterExpanded.Visibility = Visibility.Collapsed;
                FooterCollapsed.Visibility = Visibility.Visible;
                FooterCollapsed.Opacity = 1;

                // 3. Animate Sidebar width 220 → 64 (280ms)
                var widthAnim = new DoubleAnimation(fromWidth, toWidth, TimeSpan.FromMilliseconds(280))
                {
                    EasingFunction = cubicEase
                };
                SidebarContainer.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // NAVIGATION HANDLER
        // ═════════════════════════════════════════════════════════════════
        private void NavButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb)
            {
                string targetName = rb.Name;
                OnNavSelected(targetName);
            }
        }

        private void OnNavSelected(string navButtonName)
        {
            switch (navButtonName)
            {
                case "NavDashboard": NavigateTo<Dashboard>(); break;
                case "NavCleanups": NavigateTo<SystemCleanups>(); break;
                case "NavTweaks": NavigateTo<PerformanceTweaks>(); break;
                case "NavGaming": NavigateTo<GamingMode>(); break;
                case "NavStartup": NavigateTo<StartupManager>(); break;
                case "NavServices": NavigateTo<ServiceManager>(); break;
                case "NavNetwork": NavigateTo<NetworkBoost>(); break;
                case "NavStorage": NavigateTo<StorageManagement>(); break;
                case "NavToolbox": NavigateTo<Toolbox>(); break;
                case "NavDiagnostics": NavigateTo<SystemDiagnostics>(); break;
                case "NavSettings": NavigateTo<Settings>(); break;
                case "NavAbout": NavigateTo<About>(); break;
            }
        }

        /// <summary>
        /// Sidebar order of each page, used to derive slide direction:
        /// navigating "down" the list slides forward, "up" slides backward.
        /// </summary>
        private static readonly Dictionary<Type, int> NavOrder = new()
        {
            { typeof(Dashboard), 0 },
            { typeof(SystemCleanups), 1 },
            { typeof(PerformanceTweaks), 2 },
            { typeof(GamingMode), 3 },
            { typeof(StartupManager), 4 },
            { typeof(ServiceManager), 5 },
            { typeof(NetworkBoost), 6 },
            { typeof(StorageManagement), 7 },
            { typeof(Toolbox), 8 },
            { typeof(SystemDiagnostics), 9 },
            { typeof(Settings), 10 },
            { typeof(About), 11 },
        };

        private void NavigateTo<TPage>() where TPage : System.Windows.Controls.Page, new()
        {
            int current = MainContentFrame.Content != null && NavOrder.TryGetValue(MainContentFrame.Content.GetType(), out int idx)
                ? idx : 0;
            NavOrder.TryGetValue(typeof(TPage), out int target);
            var direction = target >= current ? NavDirection.Forward : NavDirection.Backward;
            _ = MainContentFrame.NavigateWithAnimationAsync(new TPage(), direction);
        }

        /// <summary>
        /// Custom navigation: plays PageExitAnimation on current page, then PageEnterAnimation on new page.
        /// </summary>
        public async Task<bool> Navigate(object page)
        {
            return await MainContentFrame.NavigateWithAnimationAsync(page);
        }

        // ═════════════════════════════════════════════════════════════════
        // TOAST CONVENIENCE METHODS
        // ═════════════════════════════════════════════════════════════════
        public void ShowToast(string title, string message, ToastType type = ToastType.Info, TimeSpan? duration = null, Action? onClick = null)
        {
            _toastManager?.Show(title, message, type, duration, onClick);
        }

        public void ShowToastSuccess(string title, string message, TimeSpan? duration = null, Action? onClick = null)
        {
            _toastManager?.ShowSuccess(title, message, duration, onClick);
        }

        public void ShowToastWarning(string title, string message, TimeSpan? duration = null, Action? onClick = null)
        {
            _toastManager?.ShowWarning(title, message, duration, onClick);
        }

        public void ShowToastError(string title, string message, TimeSpan? duration = null, Action? onClick = null)
        {
            _toastManager?.ShowError(title, message, duration, onClick);
        }

        public void ShowToastWithAction(string title, string message, string actionText, Action onAction, ToastType type = ToastType.Info, TimeSpan? duration = null)
        {
            _toastManager?.ShowWithAction(title, message, actionText, onAction, type, duration);
        }

        // ═════════════════════════════════════════════════════════════════
        // SYSTEM TRAY NOTIFICATION ICON
        // ═════════════════════════════════════════════════════════════════
        private void InitializeSystemTray()
        {
            try
            {
                _trayIcon = new System.Windows.Forms.NotifyIcon();
                try
                {
                    var iconUri = new Uri("pack://application:,,,/app_icon.ico");
                    var streamInfo = Application.GetResourceStream(iconUri);
                    if (streamInfo != null)
                    {
                        _trayIcon.Icon = new System.Drawing.Icon(streamInfo.Stream);
                    }
                }
                catch
                {
                    try
                    {
                        _trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule?.FileName ?? "");
                    }
                    catch { }
                }

                _trayIcon.Text = "James Optimizer v3.0.0";
                _trayIcon.Visible = true;
                _trayIcon.DoubleClick += (s, e) =>
                {
                    Show();
                    WindowState = WindowState.Normal;
                    Activate();
                };

                var contextMenu = new System.Windows.Forms.ContextMenuStrip();
                contextMenu.Items.Add("Open James Optimizer", null, (s, e) =>
                {
                    Show();
                    WindowState = WindowState.Normal;
                    Activate();
                });
                contextMenu.Items.Add("Exit", null, (s, e) =>
                {
                    _isRealExit = true;
                    if (_trayIcon != null) _trayIcon.Visible = false;
                    Application.Current.Shutdown();
                });
                _trayIcon.ContextMenuStrip = contextMenu;
            }
            catch (Exception ex)
            {
                HardwareLogger.LogError("Tray initialization failed", ex);
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_isRealExit)
            {
                // Can minimize to tray or close directly
            }
            base.OnClosing(e);
        }

        /// <summary>
        /// Global helper to format raw byte counts into human-readable strings (KB, MB, GB, TB).
        /// </summary>
        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] suffixes = { "B", "KB", "MB", "GB", "TB", "PB" };
            int counter = 0;
            decimal number = (decimal)bytes;
            while (Math.Round(number / 1024m) >= 1 && counter < suffixes.Length - 1)
            {
                number /= 1024m;
                counter++;
            }
            return string.Format("{0:n1} {1}", number, suffixes[counter]);
        }

        // ═════════════════════════════════════════════════════════════════
        // COMMAND PALETTE / QUICK ACTIONS CONTROLLER (Ctrl+K)
        // ═════════════════════════════════════════════════════════════════
        public class CommandPaletteItem
        {
            public string Title { get; set; } = "";
            public string Subtitle { get; set; } = "";
            public string Category { get; set; } = "NAVIGATION";
            public string IconGlyph { get; set; } = "⚡";
            public Action Action { get; set; } = () => { };
        }

        private readonly List<CommandPaletteItem> _allCommands = new();

        private void InitCommandPalette()
        {
            _allCommands.Clear();

            // Navigation Commands
            _allCommands.Add(new CommandPaletteItem { Title = "Dashboard", Subtitle = "System overview, health score & live telemetry", Category = "NAVIGATION", IconGlyph = "📊", Action = () => SelectNav("NavDashboard") });
            _allCommands.Add(new CommandPaletteItem { Title = "System Cleanups", Subtitle = "Clean cache, logs, temp files & telemetry", Category = "NAVIGATION", IconGlyph = "🧹", Action = () => SelectNav("NavCleanups") });
            _allCommands.Add(new CommandPaletteItem { Title = "Performance Tweaks", Subtitle = "Registry optimizations, visual FX & input latency", Category = "NAVIGATION", IconGlyph = "⚡", Action = () => SelectNav("NavTweaks") });
            _allCommands.Add(new CommandPaletteItem { Title = "Gaming Mode", Subtitle = "Ultimate gaming priority & FPS optimization", Category = "NAVIGATION", IconGlyph = "🎮", Action = () => SelectNav("NavGaming") });
            _allCommands.Add(new CommandPaletteItem { Title = "Startup Manager", Subtitle = "Inspect and disable slow auto-launch apps", Category = "NAVIGATION", IconGlyph = "🚀", Action = () => SelectNav("NavStartup") });
            _allCommands.Add(new CommandPaletteItem { Title = "Service Manager", Subtitle = "Disable background Windows bloatware services", Category = "NAVIGATION", IconGlyph = "⚙️", Action = () => SelectNav("NavServices") });
            _allCommands.Add(new CommandPaletteItem { Title = "Network Boost", Subtitle = "TCP auto-tuning, DNS switcher & latency monitor", Category = "NAVIGATION", IconGlyph = "🌐", Action = () => SelectNav("NavNetwork") });
            _allCommands.Add(new CommandPaletteItem { Title = "Storage Management", Subtitle = "Drive storage breakdown & duplicate file finder", Category = "NAVIGATION", IconGlyph = "💾", Action = () => SelectNav("NavStorage") });
            _allCommands.Add(new CommandPaletteItem { Title = "Toolbox", Subtitle = "Hardware tools, restore points & context menu cleaner", Category = "NAVIGATION", IconGlyph = "🧰", Action = () => SelectNav("NavToolbox") });
            _allCommands.Add(new CommandPaletteItem { Title = "System Diagnostics", Subtitle = "Detailed hardware component specs & sensors", Category = "NAVIGATION", IconGlyph = "🔍", Action = () => SelectNav("NavDiagnostics") });
            _allCommands.Add(new CommandPaletteItem { Title = "Settings", Subtitle = "Theme accents, DWM backdrop & app options", Category = "NAVIGATION", IconGlyph = "🛠️", Action = () => SelectNav("NavSettings") });
            _allCommands.Add(new CommandPaletteItem { Title = "About", Subtitle = "Version information, architecture & author notes", Category = "NAVIGATION", IconGlyph = "ℹ️", Action = () => SelectNav("NavAbout") });

            // Quick Actions
            _allCommands.Add(new CommandPaletteItem
            {
                Title = "Flush RAM Working Sets",
                Subtitle = "Instantly release unused standby and working set memory",
                Category = "QUICK ACTION",
                IconGlyph = "💧",
                Action = async () =>
                {
                    await OptimizationEngine.RunRamFlushAsync();
                    ShowToastSuccess("Memory Flushed", "Standby and working set memory released.");
                }
            });

            _allCommands.Add(new CommandPaletteItem
            {
                Title = "Purge Temp & Junk Files",
                Subtitle = "Clean user temporary folders, Windows logs and caches",
                Category = "QUICK ACTION",
                IconGlyph = "🗑️",
                Action = async () =>
                {
                    await OptimizationEngine.RunFastCleanAsync(CancellationToken.None);
                    ShowToastSuccess("Cleanup Complete", "Temporary files and junk purged.");
                }
            });

            _allCommands.Add(new CommandPaletteItem
            {
                Title = "Run System Benchmark",
                Subtitle = "Benchmark CPU, RAM and disk performance latency",
                Category = "PERFORMANCE",
                IconGlyph = "⚡",
                Action = async () =>
                {
                    ShowToast("Benchmarking", "Running CPU, memory and disk latency tests...", ToastType.Info);
                    try
                    {
                        var res = await OptimizationEngine.BenchmarkSystemAsync(CancellationToken.None);
                        ShowToastSuccess($"Benchmark Score: {res.total}/100", $"CPU: {res.cpu}/100 · RAM: {res.ram}/100 · Disk: {res.disk}/100");
                    }
                    catch (Exception ex)
                    {
                        ShowToastError("Benchmark Failed", ex.Message);
                    }
                }
            });

            _allCommands.Add(new CommandPaletteItem
            {
                Title = "Flush DNS Resolver Cache",
                Subtitle = "Reset DNS resolver cache to improve online routing",
                Category = "QUICK ACTION",
                IconGlyph = "📡",
                Action = async () =>
                {
                    await OptimizationEngine.RunNetworkFlushAsync(CancellationToken.None);
                    ShowToastSuccess("DNS Reset", "DNS resolver cache successfully flushed.");
                }
            });

            _allCommands.Add(new CommandPaletteItem
            {
                Title = "Restart Windows Explorer",
                Subtitle = "Cleanly restarts explorer.exe shell to refresh UI",
                Category = "QUICK ACTION",
                IconGlyph = "🔄",
                Action = () =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo("cmd.exe", "/c taskkill /f /im explorer.exe & start explorer.exe") { CreateNoWindow = true, UseShellExecute = false });
                        ShowToast("Explorer Restarted", "Windows desktop shell refreshed.", ToastType.Info);
                    }
                    catch (Exception ex)
                    {
                        ShowToastError("Restart Failed", ex.Message);
                    }
                }
            });

            _allCommands.Add(new CommandPaletteItem
            {
                Title = "Create System Restore Point",
                Subtitle = "Generate an immediate Windows snapshot before changes",
                Category = "SECURITY",
                IconGlyph = "🛡️",
                Action = async () =>
                {
                    ShowToast("Creating Snapshot", "Generating Windows restore point...", ToastType.Info);
                    try
                    {
                        await OptimizationEngine.CreateRestorePointAsync(CancellationToken.None);
                        ShowToastSuccess("Restore Point Created", "System snapshot saved successfully.");
                    }
                    catch (Exception ex)
                    {
                        ShowToastError("Snapshot Error", ex.Message);
                    }
                }
            });

            _allCommands.Add(new CommandPaletteItem
            {
                Title = "Export Optimization Report",
                Subtitle = "Generate and open a rich HTML executive report",
                Category = "REPORT",
                IconGlyph = "📄",
                Action = async () =>
                {
                    try
                    {
                        string path = await OptimizationReportGenerator.GenerateAndOpenReportAsync();
                        ShowToastWithAction("Report Generated", "Executive report opened in browser.", "OPEN FOLDER", () =>
                        {
                            try { Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { }
                        }, ToastType.Success);
                    }
                    catch (Exception ex)
                    {
                        ShowToastError("Report Error", ex.Message);
                    }
                }
            });

            // Profiles
            _allCommands.Add(new CommandPaletteItem
            {
                Title = "Profile: Gaming Session",
                Subtitle = "Ultimate Power Plan, GPU priority & minimal latency",
                Category = "PROFILE",
                IconGlyph = "🔥",
                Action = async () =>
                {
                    await OptimizationProfileManager.ApplyProfileAsync(OptimizationProfileType.Gaming);
                    ShowToastSuccess("Gaming Profile Active", "Ultimate Power and high-priority schedulers engaged.");
                }
            });

            _allCommands.Add(new CommandPaletteItem
            {
                Title = "Profile: Work & Dev Mode",
                Subtitle = "Balanced power, cleared temp & stable background services",
                Category = "PROFILE",
                IconGlyph = "💻",
                Action = async () =>
                {
                    await OptimizationProfileManager.ApplyProfileAsync(OptimizationProfileType.Work);
                    ShowToastSuccess("Work Profile Active", "Balanced performance mode applied.");
                }
            });

            _allCommands.Add(new CommandPaletteItem
            {
                Title = "Profile: Battery / Eco Saver",
                Subtitle = "Power saver scheme, minimal background load",
                Category = "PROFILE",
                IconGlyph = "🔋",
                Action = async () =>
                {
                    await OptimizationProfileManager.ApplyProfileAsync(OptimizationProfileType.BatterySaver);
                    ShowToast("Eco Profile Active", "Power saver profile active.", ToastType.Info);
                }
            });

            // Help
            _allCommands.Add(new CommandPaletteItem
            {
                Title = "Show Tips & Walkthrough",
                Subtitle = "Replay the guided tour of the sidebar, command palette and settings",
                Category = "HELP",
                IconGlyph = "💡",
                Action = () => StartTips()
            });
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if ((e.Key == Key.K && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) ||
                (e.Key == Key.P && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control))
            {
                ToggleCommandPalette();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && CommandPaletteOverlay.Visibility == Visibility.Visible)
            {
                CloseCommandPalette();
                e.Handled = true;
            }
        }

        public void OpenCommandPalette()
        {
            CommandPaletteOverlay.Visibility = Visibility.Visible;
            TxtCommandSearch.Text = "";
            FilterCommandPalette("");
            TxtCommandSearch.Focus();
        }

        public void CloseCommandPalette()
        {
            CommandPaletteOverlay.Visibility = Visibility.Collapsed;
        }

        public void ToggleCommandPalette()
        {
            if (CommandPaletteOverlay.Visibility == Visibility.Visible)
                CloseCommandPalette();
            else
                OpenCommandPalette();
        }

        private void BtnOpenCommandPalette_Click(object sender, MouseButtonEventArgs e)
        {
            OpenCommandPalette();
        }

        private void BtnCloseCommandPalette_Click(object sender, RoutedEventArgs e)
        {
            CloseCommandPalette();
        }

        private void CommandPaletteScrim_MouseDown(object sender, MouseButtonEventArgs e)
        {
            CloseCommandPalette();
        }

        private void TxtCommandSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = TxtCommandSearch.Text.Trim();
            TxtSearchPlaceholder.Visibility = string.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;
            FilterCommandPalette(query);
        }

        private void FilterCommandPalette(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                ListCommandResults.ItemsSource = _allCommands;
            }
            else
            {
                string lower = query.ToLowerInvariant();
                var filtered = _allCommands.Where(c =>
                    c.Title.ToLowerInvariant().Contains(lower) ||
                    c.Subtitle.ToLowerInvariant().Contains(lower) ||
                    c.Category.ToLowerInvariant().Contains(lower)).ToList();

                ListCommandResults.ItemsSource = filtered;
            }

            if (ListCommandResults.Items.Count > 0)
            {
                ListCommandResults.SelectedIndex = 0;
            }
        }

        private void TxtCommandSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down)
            {
                if (ListCommandResults.Items.Count > 0)
                {
                    int next = (ListCommandResults.SelectedIndex + 1) % ListCommandResults.Items.Count;
                    ListCommandResults.SelectedIndex = next;
                    ListCommandResults.ScrollIntoView(ListCommandResults.SelectedItem);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                if (ListCommandResults.Items.Count > 0)
                {
                    int prev = (ListCommandResults.SelectedIndex - 1 + ListCommandResults.Items.Count) % ListCommandResults.Items.Count;
                    ListCommandResults.SelectedIndex = prev;
                    ListCommandResults.ScrollIntoView(ListCommandResults.SelectedItem);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                ExecuteSelectedCommand();
                e.Handled = true;
            }
        }

        private void ListCommandResults_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ExecuteSelectedCommand();
                e.Handled = true;
            }
        }

        private void ListCommandResults_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ExecuteSelectedCommand();
        }

        private void ListCommandResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Optional preview handler
        }

        private void ExecuteSelectedCommand()
        {
            if (ListCommandResults.SelectedItem is CommandPaletteItem item)
            {
                CloseCommandPalette();
                item.Action?.Invoke();
            }
        }

        public void SelectNav(string navButtonName)
        {
            OnNavSelected(navButtonName);

            switch (navButtonName)
            {
                case "NavDashboard": NavDashboard.IsChecked = true; break;
                case "NavCleanups": NavCleanups.IsChecked = true; break;
                case "NavTweaks": NavTweaks.IsChecked = true; break;
                case "NavGaming": NavGaming.IsChecked = true; break;
                case "NavStartup": NavStartup.IsChecked = true; break;
                case "NavServices": NavServices.IsChecked = true; break;
                case "NavNetwork": NavNetwork.IsChecked = true; break;
                case "NavStorage": NavStorage.IsChecked = true; break;
                case "NavToolbox": NavToolbox.IsChecked = true; break;
                case "NavDiagnostics": NavDiagnostics.IsChecked = true; break;
                case "NavSettings": NavSettings.IsChecked = true; break;
                case "NavAbout": NavAbout.IsChecked = true; break;
            }
        }
    }
}
