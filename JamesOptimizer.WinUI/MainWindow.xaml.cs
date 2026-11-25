using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using JamesOptimizer.Core;
using JamesOptimizer.WinUI.Core;
using JamesOptimizer.WinUI.Theme;
using JamesOptimizer.WinUI.ViewModels;

namespace JamesOptimizer.WinUI;

public sealed class CommandPaletteItem
{
    public string Glyph { get; set; } = "\uE80F";
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Badge { get; set; } = "PAGE";
    public string SearchKeywords { get; set; } = "";
    public Action Execute { get; set; } = () => { };
}

public sealed partial class MainWindow : Window
{
    private AppWindow? _appWindow;
    private bool _isSidebarCollapsed = true;
    private bool _isRealExit = false;
    private bool _isPaletteOpen = false;
    private int _paletteAnimGen = 0;
    private TrayIconHelper? _trayIcon;
    private readonly DashboardViewModel _viewModel;
    private readonly List<CommandPaletteItem> _paletteCatalog = new();

    public static new MainWindow? Current { get; private set; }

    public MainWindow()
    {
        Current = this;
        InitializeComponent();

        // 1. Full Bleed Custom Title Bar
        try
        {
            ExtendsContentIntoTitleBar = true;
            _appWindow = AppWindow;
            _appWindow.Title = "JamesOptimizer V3";
            _appWindow.Resize(new SizeInt32(1380, 880));

            if (AppWindowTitleBar.IsCustomizationSupported() && _appWindow.TitleBar != null)
            {
                var titleBar = _appWindow.TitleBar;
                titleBar.ButtonBackgroundColor = Colors.Transparent;
                titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
                titleBar.ButtonForegroundColor = ThemeEffects.GetColor("Text.Primary.Color", Colors.White);
                titleBar.ButtonHoverBackgroundColor = ThemeEffects.GetColor("Bg.Surface2.Color", Colors.DimGray);
                titleBar.ButtonHoverForegroundColor = ThemeEffects.GetColor("Text.Primary.Color", Colors.White);
                titleBar.ButtonPressedBackgroundColor = ThemeEffects.GetColor("Border.Strong.Color", Colors.Gray);
            }

            SetTitleBar(TitleBarDragArea);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TitleBar customization failed: {ex.Message}");
        }

        // Apply Mica tint when supported, otherwise solid Bg.App fallback
        try
        {
            RootShellGrid.Background = MicaController.IsSupported()
                ? ThemeEffects.GetBrush("Bg.App.MicaTint")
                : ThemeEffects.GetBrush("Bg.App");
        }
        catch
        {
            RootShellGrid.Background = ThemeEffects.GetBrush("Bg.App");
        }

        // 2. Detect Windows version label & wire pause guardrails on minimize/hide
        TxtOsVersion.Text = Environment.OSVersion.Version.Build >= 22000
            ? "Windows 11 · 64-bit"
            : "Windows 10 · 64-bit";

        UpdateAmbientPulseState(ThemeEffects.EffectsEnabled);
        ThemeEffects.EffectsEnabledChanged += (_, enabled) =>
            DispatcherQueue.TryEnqueue(() => UpdateAmbientPulseState(enabled));

        this.VisibilityChanged += (_, args) => UpdateWindowActiveState(args.Visible);
        if (_appWindow != null)
        {
            _appWindow.Changed += (_, args) =>
            {
                if (args.DidPresenterChange || args.DidVisibilityChange)
                    UpdateWindowActiveState(this.Visible);
            };
        }

        // 3. Connect Shared ViewModel, Tray Host & Command Palette
        _viewModel = App.ViewModel ?? new DashboardViewModel();
        AuditLogsList.ItemsSource = _viewModel.AllLogs;
        BuildCommandPaletteCatalog();
        InitializeSystemTray();

        // 4. Navigate Frame to DashboardPage (SidebarNavButtonStyle drives ElasticBehavior)
        ContentFrame.Navigate(typeof(DashboardPage));

        if (!string.IsNullOrWhiteSpace(App.StartupFileLockTarget))
            NavigateTo("Toolbox");

        CheckCrashRecoveryJournal();
    }

    private void CheckCrashRecoveryJournal()
    {
        try
        {
            var pending = WriteAheadJournal.GetIncompleteEntries();
            if (pending.Count == 0) return;

            int rolled = 0;
            foreach (var entry in pending)
            {
                if (entry.Snapshot != null)
                {
                    TweakBackupService.RevertStepsLifo(entry.Snapshot.Steps);
                    rolled++;
                }
                WriteAheadJournal.MarkRolledBack(entry.Id);
            }
            if (rolled > 0)
            {
                _viewModel.AddLog("RECOVERY", $"Write-ahead journal rolled back {rolled} interrupted tweak(s) from previous crash.");
                ShowToast("Crash Recovery", $"Safely rolled back {rolled} interrupted tweak(s).", "State.Warn");
            }
        }
        catch { }
    }

    private void InitializeSystemTray()
    {
        try
        {
            _trayIcon = new TrayIconHelper(this);
            _trayIcon.OnOpenRequested += () => DispatcherQueue.TryEnqueue(() => _trayIcon.ShowWindowToFront());
            _trayIcon.OnFlushRamRequested += () => DispatcherQueue.TryEnqueue(async () =>
            {
                await OptimizationEngine.RunRamFlushAsync();
                ShowToast("Tray: RAM Flushed", "Working sets trimmed from tray.", "State.Good");
            });
            _trayIcon.OnSwitchProfileRequested += mode => DispatcherQueue.TryEnqueue(async () =>
            {
                var p = mode switch
                {
                    "Work" => OptimizationProfileType.Work,
                    "Eco" => OptimizationProfileType.BatterySaver,
                    _ => OptimizationProfileType.Gaming
                };
                await OptimizationProfileManager.ApplyProfileAsync(p);
                ShowToast($"Tray: {mode} Mode", "Power profile updated from tray.", "Accent.Cyan");
            });
            _trayIcon.OnExitRequested += () => DispatcherQueue.TryEnqueue(() =>
            {
                _isRealExit = true;
                _trayIcon?.Dispose();
                Close();
            });

            if (_appWindow != null)
            {
                _appWindow.Closing += (s, args) =>
                {
                    if (!_isRealExit && TrayIconHelper.MinimizeToTray)
                    {
                        args.Cancel = true;
                        _trayIcon?.HideWindowToTray();
                    }
                    else
                    {
                        _trayIcon?.Dispose();
                    }
                };
            }
        }
        catch { }
    }

    #region Sidebar Navigation & Collapse

    private void ToggleSidebarBtn_Click(object sender, RoutedEventArgs e)
    {
        _isSidebarCollapsed = !_isSidebarCollapsed;
        AnimateSidebar(_isSidebarCollapsed);
    }

    private Button[] GetNavButtons() => new[]
    {
        NavDashboard, NavCleanups, NavPerformance, NavGamingMode,
        NavStartup, NavServices, NavNetwork, NavStorage,
        NavToolbox, NavDiagnostics, NavSettings
    };

    private FrameworkElement[] GetSidebarLabels() => new FrameworkElement[]
    {
        NavHeaderLabel, NavLabelDashboard, NavLabelClean, NavLabelPerf, NavLabelGame,
        NavLabelStartup, NavLabelServices, NavLabelNet, NavLabelStorage,
        NavLabelToolbox, NavLabelDiag, NavLabelSettings, UserInfoPanel
    };

    private void AnimateSidebar(bool collapse)
    {
        double targetWidth = collapse ? 58 : 218;
        var labels = GetSidebarLabels();

        if (!MotionHelper.ShouldAnimate)
        {
            MotionHelper.StopFrameLoop(SidebarColDef);
            SidebarColDef.Width = new GridLength(targetWidth);
            var vis = collapse ? Visibility.Collapsed : Visibility.Visible;
            foreach (var lbl in labels)
            {
                lbl.Visibility = vis;
                ElementCompositionPreview.GetElementVisual(lbl).Opacity = 1f;
            }
            return;
        }

        double startWidth = SidebarColDef.Width.Value;
        const double durationMs = 280.0;
        const double labelThreshold = 120.0;
        bool labelsHandled = false;

        if (collapse)
        {
            foreach (var lbl in labels)
                MotionHelper.SpringOpacity(lbl, 0f, 100);
        }

        MotionHelper.StartFrameLoop(SidebarColDef, elapsedMs =>
        {
            double t = Math.Clamp(elapsedMs / durationMs, 0.0, 1.0);
            double eased = MotionHelper.DampedSpringCurve(t);
            double width = Math.Max(40.0, startWidth + (targetWidth - startWidth) * eased);
            SidebarColDef.Width = new GridLength(width);

            if (!labelsHandled)
            {
                if (collapse && (width <= labelThreshold || t >= 1.0))
                {
                    labelsHandled = true;
                    foreach (var lbl in labels)
                        lbl.Visibility = Visibility.Collapsed;
                }
                else if (!collapse && (width >= labelThreshold || t >= 1.0))
                {
                    labelsHandled = true;
                    foreach (var lbl in labels)
                    {
                        if (lbl.Visibility != Visibility.Visible)
                        {
                            lbl.Visibility = Visibility.Visible;
                            ElementCompositionPreview.GetElementVisual(lbl).Opacity = 0f;
                        }
                        MotionHelper.SpringOpacity(lbl, 1f, 160);
                    }
                }
            }

            if (t >= 1.0)
            {
                SidebarColDef.Width = new GridLength(targetWidth);
                return false;
            }
            return true;
        });
    }

    private static readonly Dictionary<string, (Type PageType, int Index)> NavRoutes = new(StringComparer.Ordinal)
    {
        ["Dashboard"] = (typeof(DashboardPage), 0),
        ["Cleanups"] = (typeof(Views.CleanupsPage), 1),
        ["Performance"] = (typeof(Views.PerformancePage), 2),
        ["GamingMode"] = (typeof(Views.GamingModePage), 3),
        ["Startup"] = (typeof(Views.StartupPage), 4),
        ["Services"] = (typeof(Views.ServicesPage), 5),
        ["Network"] = (typeof(Views.NetworkPage), 6),
        ["Storage"] = (typeof(Views.StoragePage), 7),
        ["Toolbox"] = (typeof(Views.ToolboxPage), 8),
        ["Diagnostics"] = (typeof(Views.DiagnosticsPage), 9),
        ["Settings"] = (typeof(Views.SettingsPage), 10)
    };

    public void NavigateTo(string tag)
    {
        var allButtons = GetNavButtons();

        var allLabels = new[]
        {
            NavLabelDashboard, NavLabelClean, NavLabelPerf, NavLabelGame,
            NavLabelStartup, NavLabelServices, NavLabelNet, NavLabelStorage,
            NavLabelToolbox, NavLabelDiag, NavLabelSettings
        };

        var transparentBrush = new SolidColorBrush(Colors.Transparent);
        var activeSurface = ThemeEffects.GetBrush("Brush.Raised.Pressed");
        var activeBorder = ThemeEffects.GetBrush("Border.InnerPressedShadow");
        var accentCyan = ThemeEffects.GetBrush("Accent.Cyan");
        var primaryTextBrush = ThemeEffects.GetBrush("Text.Primary");
        var secondaryTextBrush = ThemeEffects.GetBrush("Text.Secondary");

        for (int i = 0; i < allButtons.Length; i++)
        {
            allButtons[i].Background = transparentBrush;
            allButtons[i].BorderBrush = transparentBrush;
            allLabels[i].Foreground = secondaryTextBrush;
            allLabels[i].FontWeight = Microsoft.UI.Text.FontWeights.Normal;
            if (allButtons[i].Content is StackPanel sp && sp.Children.FirstOrDefault() is TextBlock iconTb)
            {
                iconTb.Foreground = secondaryTextBrush;
            }
        }

        (Type targetPageType, int selectedIndex) = NavRoutes.TryGetValue(tag, out var route)
            ? route
            : (typeof(DashboardPage), 0);

        var selectedBtn = allButtons[selectedIndex];
        selectedBtn.Background = activeSurface;
        selectedBtn.BorderBrush = activeBorder;
        allLabels[selectedIndex].Foreground = primaryTextBrush;
        allLabels[selectedIndex].FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        if (selectedBtn.Content is StackPanel selSp && selSp.Children.FirstOrDefault() is TextBlock selIcon)
        {
            selIcon.Foreground = accentCyan;
        }

        try
        {
            var pt = selectedBtn.TransformToVisual(NavItemsPanel).TransformPoint(new Point(0, 0));
            MotionHelper.AnimateSlideY(SidebarActiveIndicator, (float)pt.Y);
        }
        catch { }

        NavigationTransitionInfo transition = MotionHelper.ShouldAnimate
            ? new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight }
            : new SuppressNavigationTransitionInfo();

        ContentFrame.Navigate(targetPageType, null, transition);
        _viewModel.AddLog("SYSTEM", $"Active view shifted to: {tag.ToUpper()}");
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
            NavigateTo(tag);
    }

    private void UserCard_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        NavigateTo("Settings");
    }

    #endregion

    #region Ambient Pulse, Toast Notifications & Ctrl+K Command Palette

    private void UpdateWindowActiveState(bool isVisible)
    {
        bool isMinimized = _appWindow?.Presenter is OverlappedPresenter op && op.State == OverlappedPresenterState.Minimized;
        bool activeAndVisible = isVisible && !isMinimized;
        ThemeEffects.SetWindowActiveState(activeAndVisible);
        if (!activeAndVisible)
            MotionHelper.StopAllFrameLoops();
    }

    private void UpdateAmbientPulseState(bool animate)
    {
        try
        {
            StatusPulseDot.Opacity = 1.0;
        }
        catch { }
    }

    public async void ShowToast(string title, string message, string accentTokenOrHex = "Accent.Cyan")
    {
        try
        {
            Windows.UI.Color accentColor;
            if (!string.IsNullOrWhiteSpace(accentTokenOrHex) && accentTokenOrHex.StartsWith("#") && accentTokenOrHex.Length >= 7)
            {
                byte r = Convert.ToByte(accentTokenOrHex.Substring(1, 2), 16);
                byte g = Convert.ToByte(accentTokenOrHex.Substring(3, 2), 16);
                byte b = Convert.ToByte(accentTokenOrHex.Substring(5, 2), 16);
                accentColor = Windows.UI.Color.FromArgb(255, r, g, b);
            }
            else
            {
                accentColor = ThemeEffects.GetColor($"{accentTokenOrHex}.Color", ThemeEffects.GetColor("Accent.Cyan.Color", Colors.Cyan));
            }

            var toast = new Border
            {
                Background = ThemeEffects.GetBrush("Bg.Surface2"),
                BorderBrush = ThemeEffects.GetBrush("Border.Strong"),
                BorderThickness = new Thickness(1),
                CornerRadius = (CornerRadius)Application.Current.Resources["Radius.Md"],
                Padding = new Thickness(14, 10, 14, 10)
            };

            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var strip = new Border
            {
                Background = new SolidColorBrush(accentColor),
                CornerRadius = (CornerRadius)Application.Current.Resources["Radius.Bar"],
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Grid.SetColumn(strip, 0);

            var textStack = new StackPanel { Spacing = 2 };
            textStack.Children.Add(new TextBlock
            {
                Text = title,
                FontFamily = (FontFamily)Application.Current.Resources["Font.Sans"],
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = ThemeEffects.GetBrush("Text.Primary")
            });
            textStack.Children.Add(new TextBlock
            {
                Text = message,
                FontFamily = (FontFamily)Application.Current.Resources["Font.Sans"],
                FontSize = 12,
                Foreground = ThemeEffects.GetBrush("Text.Secondary"),
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 2
            });
            Grid.SetColumn(textStack, 1);

            row.Children.Add(strip);
            row.Children.Add(textStack);
            toast.Child = row;

            ToastHost.Children.Insert(0, toast);
            ElementCompositionPreview.SetIsTranslationEnabled(toast, true);
            var tv = ElementCompositionPreview.GetElementVisual(toast);

            if (!MotionHelper.ShouldAnimate)
            {
                tv.Opacity = 1f;
                tv.Properties.InsertVector3("Translation", Vector3.Zero);
            }
            else
            {
                tv.Opacity = 0f;
                tv.Properties.InsertVector3("Translation", new Vector3(0f, 16f, 0f));
                MotionHelper.SpringTranslate(toast, 0f, 0f, MotionHelper.Soft);
                MotionHelper.SpringOpacity(toast, 1f);
            }

            await Task.Delay(3400);
            if (!ToastHost.Children.Contains(toast)) return;

            if (!MotionHelper.ShouldAnimate)
            {
                ToastHost.Children.Remove(toast);
            }
            else
            {
                MotionHelper.SpringTranslate(toast, 0f, 12f, MotionHelper.Soft);
                var batch = tv.Compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
                MotionHelper.SpringOpacity(toast, 0f, 150);
                batch.Completed += (_, _) =>
                {
                    batch.Dispose();
                    if (ToastHost.Children.Contains(toast))
                        ToastHost.Children.Remove(toast);
                };
                batch.End();
            }
        }
        catch { }
    }

    private void BuildCommandPaletteCatalog()
    {
        _paletteCatalog.Clear();
        _paletteCatalog.AddRange(new[]
        {
            new CommandPaletteItem { Glyph = "\uE80F", Title = "Dashboard", Subtitle = "System health ring, quick actions & monitors", Badge = "PAGE", SearchKeywords = "home health score cpu gpu ram", Execute = () => NavigateTo("Dashboard") },
            new CommandPaletteItem { Glyph = "\uE74D", Title = "System Cleanups", Subtitle = "Clean temporary files, shader & browser caches", Badge = "PAGE", SearchKeywords = "clean temp junk disk space reclaim", Execute = () => NavigateTo("Cleanups") },
            new CommandPaletteItem { Glyph = "\uE950", Title = "Performance Tweaks", Subtitle = "CPU scheduling, power plans & kernel latency", Badge = "PAGE", SearchKeywords = "fps boost cpu power latency tweaks", Execute = () => NavigateTo("Performance") },
            new CommandPaletteItem { Glyph = "\uE7FC", Title = "Gaming Mode", Subtitle = "Ultimate Power priorities & frame pacing", Badge = "PAGE", SearchKeywords = "game fps turbo boost mode", Execute = () => NavigateTo("GamingMode") },
            new CommandPaletteItem { Glyph = "\uE7E8", Title = "Startup Apps", Subtitle = "Inspect & disable boot autorun entries", Badge = "PAGE", SearchKeywords = "boot startup autorun registry", Execute = () => NavigateTo("Startup") },
            new CommandPaletteItem { Glyph = "\uE9D2", Title = "Services", Subtitle = "Manage Windows background services", Badge = "PAGE", SearchKeywords = "services telemetry diagtrack background", Execute = () => NavigateTo("Services") },
            new CommandPaletteItem { Glyph = "\uE774", Title = "Network Boost", Subtitle = "DNS cache flush & TCP latency optimization", Badge = "PAGE", SearchKeywords = "dns ping network tcp internet", Execute = () => NavigateTo("Network") },
            new CommandPaletteItem { Glyph = "\uE7B8", Title = "Storage", Subtitle = "Drive space breakdown & heavy file finder", Badge = "PAGE", SearchKeywords = "storage drive disk ssd space", Execute = () => NavigateTo("Storage") },
            new CommandPaletteItem { Glyph = "\uE825", Title = "Toolbox", Subtitle = "Stress benchmark & context menu cleaner", Badge = "PAGE", SearchKeywords = "tools benchmark context menu", Execute = () => NavigateTo("Toolbox") },
            new CommandPaletteItem { Glyph = "\uE9D9", Title = "Diagnostics", Subtitle = "Hardware specs & security baseline", Badge = "PAGE", SearchKeywords = "diagnostics health scan hardware check", Execute = () => NavigateTo("Diagnostics") },
            new CommandPaletteItem { Glyph = "\uE713", Title = "Settings", Subtitle = "Background service, theme & reduced motion", Badge = "PAGE", SearchKeywords = "settings theme dark light animation motion", Execute = () => NavigateTo("Settings") },
            new CommandPaletteItem
            {
                Glyph = "\uE777", Title = "Create Windows Restore Point", Subtitle = "Snapshot current registry & system state", Badge = "ACTION",
                SearchKeywords = "backup snapshot restore point safety",
                Execute = async () =>
                {
                    var res = await OptimizationEngine.CreateRestorePointAsync(System.Threading.CancellationToken.None);
                    ShowToast("Restore Point Ready", res, "State.Good");
                }
            },
            new CommandPaletteItem
            {
                Glyph = "\uE945", Title = "Flush Standby RAM Now", Subtitle = "Trim background working sets & free memory", Badge = "ACTION",
                SearchKeywords = "ram memory flush clean standby free",
                Execute = async () =>
                {
                    await OptimizationEngine.RunRamFlushAsync();
                    ShowToast("RAM Standby Flushed", "Working sets trimmed cleanly.", "State.Good");
                }
            },
            new CommandPaletteItem
            {
                Glyph = "\uE7B3", Title = "Toggle Reduce Animations", Subtitle = "Switch between 30fps data motion and reduced motion", Badge = "PREF",
                SearchKeywords = "reduce animations motion accessibility performance",
                Execute = () =>
                {
                    MotionHelper.ReduceAnimations = !MotionHelper.ReduceAnimations;
                    ShowToast("Motion Preference", MotionHelper.ReduceAnimations ? "Reduced animations enabled." : "Effects enabled.", "Accent.Purple");
                }
            },
            new CommandPaletteItem
            {
                Glyph = "\uE756", Title = "Open Audit Log & Revert History", Subtitle = "Inspect live kernel logs & 1-click revert snapshots (Ctrl+L)", Badge = "TOOL",
                SearchKeywords = "audit log console history revert undo backup",
                Execute = () => OpenAuditDrawer(showHistory: false)
            },
            new CommandPaletteItem
            {
                Glyph = "\uE7A7", Title = "View Revertable Tweak History", Subtitle = "Roll back registry tweaks to their original values", Badge = "REVERT",
                SearchKeywords = "undo rollback revert restore registry tweak backup",
                Execute = () => OpenAuditDrawer(showHistory: true)
            },
            new CommandPaletteItem
            {
                Glyph = "\uE774", Title = "Flush Windows DNS Cache", Subtitle = "Clear local DNS resolver cache", Badge = "TWEAK",
                SearchKeywords = "dns cache flush internet network lag",
                Execute = async () =>
                {
                    await OptimizationEngine.FlushDnsAsync();
                    ShowToast("DNS Cache Flushed", "Windows DNS resolver cache cleared.", "State.Good");
                }
            },
            new CommandPaletteItem
            {
                Glyph = "\uE7FC", Title = "Engage Ultimate Performance Power Plan", Subtitle = "Lock high-performance scheduler & CPU boost", Badge = "TWEAK",
                SearchKeywords = "ultimate power plan cpu turbo gaming boost",
                Execute = async () =>
                {
                    await OptimizationEngine.EnableUltimatePowerPlanAsync();
                    ShowToast("Ultimate Power Plan", "Maximum performance plan active.", "Accent.Cyan");
                }
            },
            new CommandPaletteItem
            {
                Glyph = "\uE72E", Title = "Silence Windows Telemetry (with Backup)", Subtitle = "Minimize telemetry & DiagTrack via TweakCatalog", Badge = "TWEAK",
                SearchKeywords = "telemetry privacy diagtrack disable block",
                Execute = async () =>
                {
                    var res = await Task.Run(() => TweakCatalog.ApplyTweak("telemetry"));
                    ShowToast("Telemetry Shield", res.Message, res.Status is TweakStatus.Success or TweakStatus.AlreadyApplied ? "State.Good" : "State.Bad");
                }
            },
            new CommandPaletteItem
            {
                Glyph = "\uE7A7", Title = "Panic Revert All Tweaks", Subtitle = "Undo every registry, service, and power change made by JamesOptimizer", Badge = "PANIC",
                SearchKeywords = "panic undo revert all restore stock",
                Execute = async () =>
                {
                    var (ok, fail, _) = await Task.Run(() => TweakBackupService.RevertAllSnapshots());
                    ShowToast("Panic Revert Complete", $"Reverted {ok} snapshot(s) ({fail} failed).", fail == 0 ? "State.Good" : "State.Warn");
                }
            },
            new CommandPaletteItem
            {
                Glyph = "\uE7B8", Title = "Export Diagnostic Bundle (.zip)", Subtitle = "Collect logs & snapshot manifest (no personal data) to Desktop", Badge = "SUPPORT",
                SearchKeywords = "diagnostic bundle logs zip support",
                Execute = async () =>
                {
                    string zipPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"JamesOptimizer_DiagBundle_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
                    await SharedLogger.ExportDiagnosticBundleAsync(zipPath);
                    ShowToast("Bundle Exported", System.IO.Path.GetFileName(zipPath) + " saved to Desktop.", "State.Good");
                }
            },
            new CommandPaletteItem
            {
                Glyph = "\uE8A5", Title = "Export Optimization Report", Subtitle = "Generate and open HTML diagnostics summary", Badge = "REPORT",
                SearchKeywords = "export report html summary hardware",
                Execute = async () =>
                {
                    await OptimizationReportGenerator.GenerateAndOpenReportAsync();
                    ShowToast("Report Exported", "System report generated cleanly.", "Accent.Cyan");
                }
            }
        });
    }

    public void OpenAuditDrawer(bool showHistory = false)
    {
        AuditDrawerOverlay.Visibility = Visibility.Visible;
        SwitchAuditTab(showHistory ? "HISTORY" : "LOGS");
    }

    private void OpenAuditDrawer_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        OpenAuditDrawer(false);
    }

    private void CloseAuditDrawer_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (AuditDrawerOverlay.Visibility == Visibility.Visible)
        {
            args.Handled = true;
            AuditDrawerOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void AuditBackdrop_PointerPressed(object sender, PointerRoutedEventArgs e) =>
        AuditDrawerOverlay.Visibility = Visibility.Collapsed;

    private void BtnCloseAuditDrawer_Click(object sender, RoutedEventArgs e) =>
        AuditDrawerOverlay.Visibility = Visibility.Collapsed;

    private void BtnAuditTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string tab) SwitchAuditTab(tab);
    }

    private void SwitchAuditTab(string tab)
    {
        var activeStyle = (Style)Application.Current.Resources["SegmentedPillActiveButtonStyle"];
        var normalStyle = (Style)Application.Current.Resources["SegmentedPillButtonStyle"];

        bool history = tab == "HISTORY";
        BtnTabLogs.Style = history ? normalStyle : activeStyle;
        BtnTabHistory.Style = history ? activeStyle : normalStyle;
        AuditLogsList.Visibility = history ? Visibility.Collapsed : Visibility.Visible;
        AuditHistoryList.Visibility = history ? Visibility.Visible : Visibility.Collapsed;

        if (history)
            AuditHistoryList.ItemsSource = TweakBackupService.GetHistory();
        else
            AuditLogsList.ItemsSource = _viewModel.AllLogs;
    }

    private void BtnRevertSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            bool ok = TweakBackupService.RevertSnapshot(id, out string msg);
            AuditHistoryList.ItemsSource = TweakBackupService.GetHistory();
            _viewModel.AddLog("REVERT", msg, !ok);
            ShowToast(ok ? "Tweak Reverted" : "Revert Failed", msg, ok ? "State.Good" : "State.Bad");
        }
    }

    private void BtnCopyAuditLogs_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.CopyConsoleLogsCommand.Execute(null);
        ShowToast("Logs Copied", "Audit logs copied to clipboard.", "Accent.Cyan");
    }

    private void BtnClearAuditLogs_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ClearLogs();
        ShowToast("Logs Cleared", "Audit log buffer reset.", "Accent.Cyan");
    }

    private void OpenCommandPalette_Click(object sender, RoutedEventArgs e) => OpenCommandPalette();

    private void OpenCommandPalette_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        OpenCommandPalette();
    }

    private void CloseCommandPalette_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (CommandPaletteOverlay.Visibility == Visibility.Visible)
        {
            args.Handled = true;
            CloseCommandPalette();
        }
    }

    public void OpenCommandPalette()
    {
        _isPaletteOpen = true;
        _paletteAnimGen++;
        CommandPaletteOverlay.Visibility = Visibility.Visible;
        PaletteSearchBox.Text = string.Empty;
        FilterCommandPalette(string.Empty);
        CommandPaletteCard.UpdateLayout();
        PaletteSearchBox.Focus(FocusState.Programmatic);

        var overlayVisual = ElementCompositionPreview.GetElementVisual(CommandPaletteOverlay);
        var cardVisual = ElementCompositionPreview.GetElementVisual(CommandPaletteCard);
        overlayVisual.StopAnimation("Opacity");
        cardVisual.StopAnimation("Scale");

        if (!MotionHelper.ShouldAnimate)
        {
            overlayVisual.Opacity = 1f;
            cardVisual.Scale = Vector3.One;
            return;
        }

        MotionHelper.PrepareVisual(CommandPaletteCard);
        overlayVisual.Opacity = 0f;
        cardVisual.Scale = new Vector3(0.95f, 0.95f, 1f);
        MotionHelper.SpringScale(CommandPaletteCard, 1f, MotionHelper.Soft);
        MotionHelper.SpringOpacity(CommandPaletteOverlay, 1f);
    }

    private void CloseCommandPalette()
    {
        if (!_isPaletteOpen && CommandPaletteOverlay.Visibility != Visibility.Visible) return;
        _isPaletteOpen = false;
        int gen = ++_paletteAnimGen;

        if (PaletteSearchBox.FocusState != FocusState.Unfocused)
        {
            PaletteSearchBox.IsEnabled = false;
            PaletteSearchBox.IsEnabled = true;
        }

        var overlayVisual = ElementCompositionPreview.GetElementVisual(CommandPaletteOverlay);
        var cardVisual = ElementCompositionPreview.GetElementVisual(CommandPaletteCard);
        overlayVisual.StopAnimation("Opacity");
        cardVisual.StopAnimation("Scale");

        if (!MotionHelper.ShouldAnimate)
        {
            CommandPaletteOverlay.Visibility = Visibility.Collapsed;
            overlayVisual.Opacity = 1f;
            cardVisual.Scale = Vector3.One;
            return;
        }

        MotionHelper.SpringScale(CommandPaletteCard, 0.97f, MotionHelper.Soft);
        var batch = overlayVisual.Compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        MotionHelper.SpringOpacity(CommandPaletteOverlay, 0f, 120);
        batch.Completed += (_, _) =>
        {
            batch.Dispose();
            if (!_isPaletteOpen && gen == _paletteAnimGen)
            {
                cardVisual.StopAnimation("Scale");
                overlayVisual.StopAnimation("Opacity");
                CommandPaletteOverlay.Visibility = Visibility.Collapsed;
                overlayVisual.Opacity = 1f;
                cardVisual.Scale = Vector3.One;
            }
        };
        batch.End();
    }

    private void CommandPaletteBackdrop_PointerPressed(object sender, PointerRoutedEventArgs e) => CloseCommandPalette();

    private void CommandPaletteCard_PointerPressed(object sender, PointerRoutedEventArgs e) => e.Handled = true;

    private void PaletteSearchBox_TextChanged(object sender, TextChangedEventArgs e) => FilterCommandPalette(PaletteSearchBox.Text);

    private void FilterCommandPalette(string query)
    {
        string q = (query ?? string.Empty).Trim();
        IEnumerable<CommandPaletteItem> matches = _paletteCatalog;

        if (!string.IsNullOrEmpty(q))
        {
            matches = _paletteCatalog
                .Select(item => (item, score: FuzzyScore(q, $"{item.Title} {item.Subtitle} {item.SearchKeywords}")))
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score)
                .Select(x => x.item);
        }

        var list = matches.ToList();
        PaletteResultsList.ItemsSource = list;
        if (list.Count > 0) PaletteResultsList.SelectedIndex = 0;
    }

    private static int FuzzyScore(string query, string target)
    {
        if (target.Contains(query, StringComparison.OrdinalIgnoreCase)) return 100;
        int qi = 0;
        foreach (char c in target)
        {
            if (qi < query.Length && char.ToLowerInvariant(c) == char.ToLowerInvariant(query[qi]))
                qi++;
        }
        return qi == query.Length ? 40 : 0;
    }

    private void PaletteSearchBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            CloseCommandPalette();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Down && PaletteResultsList.Items.Count > 0)
        {
            PaletteResultsList.SelectedIndex = Math.Min(PaletteResultsList.SelectedIndex + 1, PaletteResultsList.Items.Count - 1);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Up && PaletteResultsList.Items.Count > 0)
        {
            PaletteResultsList.SelectedIndex = Math.Max(PaletteResultsList.SelectedIndex - 1, 0);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Enter && PaletteResultsList.SelectedItem is CommandPaletteItem item)
        {
            CloseCommandPalette();
            item.Execute();
            e.Handled = true;
        }
    }

    private void PaletteResultsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CommandPaletteItem item)
        {
            CloseCommandPalette();
            item.Execute();
        }
    }

    #endregion
}
