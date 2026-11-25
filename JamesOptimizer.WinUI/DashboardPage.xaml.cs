using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Win32;
using JamesOptimizer.Core;
using JamesOptimizer.WinUI.Core;
using JamesOptimizer.WinUI.Services;
using JamesOptimizer.WinUI.Theme;
using JamesOptimizer.WinUI.ViewModels;

namespace JamesOptimizer.WinUI;

public sealed partial class DashboardPage : Page
{
    private readonly DashboardViewModel _viewModel;
    private readonly DispatcherTimer _sparklineTimer;

    // Animated ring gauges & sparkline interpolation
    private double _currentArcProgress = 0.0;
    private double _targetArcProgress = 0.91;
    private double _driveRingScaleC = 0.0, _driveRingScaleD = 0.0, _driveRingScaleE = 0.0;
    private double _drivePctC = 0.0, _drivePctD = 0.0, _drivePctE = 0.0;
    private int _unresolvedIssues = 0;

    private readonly float[] _sparkStart = new float[4];
    private readonly float[] _sparkCurrent = new float[4];
    private readonly float[] _sparkTarget = new float[4];
    private readonly Vector2[] _sparkCoordsBuffer = new Vector2[32];
    private bool _sparkInitialized = false;
    private bool _heroPulseActive = false;

    private bool _isScanning = false;
    private bool _isOptimizing = false;
    private bool _isInitializingControls = false;
    private bool _isHooked = false;

    // Cached Win2D stroke styles (zero per-frame allocations)
    private static readonly CanvasStrokeStyle _roundStrokeStyle = new()
    {
        StartCap = CanvasCapStyle.Round,
        EndCap = CanvasCapStyle.Round,
        LineJoin = CanvasLineJoin.Round
    };

    // Win2D Colors (synced from Theme/Tokens.xaml)
    private Windows.UI.Color _cyanColor = ColorHelper.FromArgb(255, 34, 211, 238);
    private Windows.UI.Color _cyanGlowColor = ColorHelper.FromArgb(77, 34, 211, 238);
    private Windows.UI.Color _purpleColor = ColorHelper.FromArgb(255, 167, 139, 250);
    private Windows.UI.Color _purpleGlowColor = ColorHelper.FromArgb(77, 167, 139, 250);
    private Windows.UI.Color _greenColor = ColorHelper.FromArgb(255, 52, 211, 153);
    private Windows.UI.Color _greenGlowColor = ColorHelper.FromArgb(77, 52, 211, 153);
    private Windows.UI.Color _yellowColor = ColorHelper.FromArgb(255, 251, 191, 36);
    private Windows.UI.Color _yellowGlowColor = ColorHelper.FromArgb(77, 251, 191, 36);
    private Windows.UI.Color _trackColor = ColorHelper.FromArgb(20, 255, 255, 255);

    private void RefreshThemeColors()
    {
        _cyanColor = ThemeEffects.GetColor("Accent.Cyan.Color", _cyanColor);
        _cyanGlowColor = ThemeEffects.GetColor("Accent.Cyan.GlowColor", _cyanGlowColor);
        _purpleColor = ThemeEffects.GetColor("Accent.Purple.Color", _purpleColor);
        _purpleGlowColor = ThemeEffects.GetColor("Accent.Purple.GlowColor", _purpleGlowColor);
        _greenColor = ThemeEffects.GetColor("Accent.Green.Color", _greenColor);
        _greenGlowColor = ThemeEffects.GetColor("Accent.Green.GlowColor", _greenGlowColor);
        _yellowColor = ThemeEffects.GetColor("Accent.Yellow.Color", _yellowColor);
        _yellowGlowColor = ThemeEffects.GetColor("Accent.Yellow.GlowColor", _yellowGlowColor);
        _trackColor = ThemeEffects.GetColor("Border.Subtle.Color", _trackColor);
    }

    public DashboardPage()
    {
        InitializeComponent();
        _viewModel = App.ViewModel ?? new DashboardViewModel();
        _sparklineTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sparklineTimer.Tick += SparklineTimer_Tick;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_isHooked)
        {
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            _viewModel.CpuMetric.PropertyChanged += Metric_PropertyChanged;
            _viewModel.GpuMetric.PropertyChanged += Metric_PropertyChanged;
            _viewModel.RamMetric.PropertyChanged += Metric_PropertyChanged;
            _viewModel.DiskMetric.PropertyChanged += Metric_PropertyChanged;
            _isHooked = true;
        }
        RefreshThemeColors();
        _sparklineTimer.Start();
        InitSparklineSamples();
        UpdateAllMetricDisplays();

        _isInitializingControls = true;
        try
        {
            SyncProfileButtons();
            InitAutoTimerSelection();
        }
        finally
        {
            _isInitializingControls = false;
        }

        MotionHelper.AnimateStaggeredEntrance(new UIElement[]
        {
            CardTopCommandDeck,
            CardEfficiencyHero,
            CardQuickActionsMatrix,
            CardAutoPurge,
            CardSystemMonitor,
            CardStorageOverview,
            CardOptimizationSummary,
            HeroStartMissionBar
        }, staggerMs: 35, durationMs: 220);

        MotionHelper.ReduceAnimationsChanged -= OnReduceAnimationsChanged;
        MotionHelper.ReduceAnimationsChanged += OnReduceAnimationsChanged;
        ThemeEffects.WindowVisibilityChanged -= OnWindowVisibilityChanged;
        ThemeEffects.WindowVisibilityChanged += OnWindowVisibilityChanged;
        ActualThemeChanged -= OnActualThemeChanged;
        ActualThemeChanged += OnActualThemeChanged;

        StartHeroGlowPulse();
        LoadStorageMetrics();
        await EvaluateAndApplySystemHealthAsync(isInitial: true);
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        RefreshThemeColors();
        ScoreArcCanvas?.Invalidate();
        InvalidateDriveRings();
        InvalidateSparkCanvases();
        IssuesDonutCanvas?.Invalidate();
    }

    private void OnWindowVisibilityChanged(object? sender, bool visibleAndActive)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (visibleAndActive)
            {
                _sparklineTimer.Start();
                ScoreArcCanvas?.Invalidate();
                InvalidateDriveRings();
                InvalidateSparkCanvases();
                StartHeroGlowPulse();
            }
            else
            {
                _sparklineTimer.Stop();
                StopHeroGlowPulse();
                MotionHelper.StopFrameLoop("ScoreChoreography");
                MotionHelper.StopFrameLoop("Sparklines");
                MotionHelper.StopFrameLoop(ModeSlidingPill);
            }
        });
    }

    private void OnReduceAnimationsChanged(object? sender, bool reduced)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ScoreArcCanvas?.Invalidate();
            InvalidateSparkCanvases();
            if (MotionHelper.ShouldAnimate)
                StartHeroGlowPulse();
            else
                StopHeroGlowPulse();
        });
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        // BUG FIX: Release Win2D DXGI device references to prevent graphics resource leak on navigation.
        ScoreArcCanvas?.RemoveFromVisualTree();
        CpuSparkCanvas?.RemoveFromVisualTree();
        GpuSparkCanvas?.RemoveFromVisualTree();
        RamSparkCanvas?.RemoveFromVisualTree();
        DiskSparkCanvas?.RemoveFromVisualTree();
        DriveRingCanvasC?.RemoveFromVisualTree();
        DriveRingCanvasD?.RemoveFromVisualTree();
        DriveRingCanvasE?.RemoveFromVisualTree();
        IssuesDonutCanvas?.RemoveFromVisualTree();

        _sparklineTimer.Stop();
        StopHeroGlowPulse();
        MotionHelper.StopFrameLoop("ScoreChoreography");
        MotionHelper.StopFrameLoop("Sparklines");
        MotionHelper.StopFrameLoop(ModeSlidingPill);
        MotionHelper.ReduceAnimationsChanged -= OnReduceAnimationsChanged;
        ThemeEffects.WindowVisibilityChanged -= OnWindowVisibilityChanged;
        ActualThemeChanged -= OnActualThemeChanged;

        if (_isHooked)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _viewModel.CpuMetric.PropertyChanged -= Metric_PropertyChanged;
            _viewModel.GpuMetric.PropertyChanged -= Metric_PropertyChanged;
            _viewModel.RamMetric.PropertyChanged -= Metric_PropertyChanged;
            _viewModel.DiskMetric.PropertyChanged -= Metric_PropertyChanged;
            _isHooked = false;
        }
    }

    #region Profile Mode Switcher (Ultimate Power / Work & Dev / Silent)

    private void ModeButtonsPanel_SizeChanged(object sender, SizeChangedEventArgs e) =>
        SyncProfileButtons();

    private void SyncProfileButtons()
    {
        var profile = OptimizationProfileManager.CurrentProfile;
        string tag = profile switch
        {
            OptimizationProfileType.Work => "Work",
            OptimizationProfileType.BatterySaver => "Eco",
            _ => "Gaming"
        };
        UpdateModeUi(tag, animateSlide: false);
    }

    private void ProfileModeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;
        UpdateModeUi(tag, animateSlide: true);

        switch (tag)
        {
            case "Gaming":
                _ = OptimizationProfileManager.ApplyProfileAsync(OptimizationProfileType.Gaming);
                MainWindow.Current?.ShowToast("Power Mode: Ultimate / Gaming", "High-performance scheduler & unparked cores active.", "Accent.Cyan");
                break;
            case "Work":
                _ = OptimizationProfileManager.ApplyProfileAsync(OptimizationProfileType.Work);
                MainWindow.Current?.ShowToast("Power Mode: Work & Dev", "Balanced power plan with foreground boost active.", "Accent.Purple");
                break;
            case "Eco":
                _ = OptimizationProfileManager.ApplyProfileAsync(OptimizationProfileType.BatterySaver);
                MainWindow.Current?.ShowToast("Power Mode: Silent / Battery Saver", "Power-saving clocks and quiet thermal curve active.", "State.Good");
                break;
        }
    }

    private void UpdateModeUi(string tag, bool animateSlide)
    {
        var cyan = ThemeEffects.GetBrush("Accent.Cyan");
        var purple = ThemeEffects.GetBrush("Accent.Purple");
        var good = ThemeEffects.GetBrush("State.Good");
        var primary = ThemeEffects.GetBrush("Text.Primary");
        var secondary = ThemeEffects.GetBrush("Text.Secondary");

        LblModeGaming.Foreground = tag == "Gaming" ? primary : secondary;
        LblModeWork.Foreground = tag == "Work" ? primary : secondary;
        LblModeEco.Foreground = tag == "Eco" ? primary : secondary;

        Button targetBtn = tag switch
        {
            "Work" => BtnModeWork,
            "Eco" => BtnModeEco,
            _ => BtnModeGaming
        };

        if (tag == "Gaming")
        {
            TxtActivePowerPlan.Text = "POWER: ULTIMATE PERFORMANCE";
            TxtActivePowerPlan.Foreground = cyan;
            PowerPulseDot.Fill = cyan;
        }
        else if (tag == "Work")
        {
            TxtActivePowerPlan.Text = "POWER: WORK & DEV BALANCED";
            TxtActivePowerPlan.Foreground = purple;
            PowerPulseDot.Fill = purple;
        }
        else
        {
            TxtActivePowerPlan.Text = "POWER: SILENT / POWER SAVER";
            TxtActivePowerPlan.Foreground = good;
            PowerPulseDot.Fill = good;
        }

        try
        {
            targetBtn.UpdateLayout();
            var pt = targetBtn.TransformToVisual(ModeButtonsPanel).TransformPoint(new Point(0, 0));
            double targetWidth = targetBtn.ActualWidth;

            if (!animateSlide || !MotionHelper.ShouldAnimate)
            {
                MotionHelper.StopFrameLoop(ModeSlidingPill);
                if (targetWidth > 0) ModeSlidingPill.Width = targetWidth;
                MotionHelper.AnimateSlideX(ModeSlidingPill, (float)pt.X, 0);
            }
            else
            {
                MotionHelper.AnimateSlideX(ModeSlidingPill, (float)pt.X, 210);
                if (targetWidth > 0)
                {
                    double startWidth = ModeSlidingPill.ActualWidth > 0
                        ? ModeSlidingPill.ActualWidth
                        : (double.IsNaN(ModeSlidingPill.Width) ? targetWidth : ModeSlidingPill.Width);

                    const double durationMs = 240.0;
                    MotionHelper.StartFrameLoop(ModeSlidingPill, elapsedMs =>
                    {
                        double t = Math.Clamp(elapsedMs / durationMs, 0.0, 1.0);
                        double eased = MotionHelper.DampedSpringCurve(t);
                        ModeSlidingPill.Width = Math.Max(24.0, startWidth + (targetWidth - startWidth) * eased);
                        if (t >= 1.0)
                        {
                            ModeSlidingPill.Width = targetWidth;
                            return false;
                        }
                        return true;
                    });
                }
            }
        }
        catch { }
    }

    private async void BtnExportReportDashboard_Click(object sender, RoutedEventArgs e)
    {
        BtnExportReportDashboard.IsEnabled = false;
        try
        {
            await OptimizationReportGenerator.GenerateAndOpenReportAsync();
            MainWindow.Current?.ShowToast("Report Exported", "Optimization summary report generated cleanly.", "Accent.Cyan");
        }
        finally
        {
            BtnExportReportDashboard.IsEnabled = true;
        }
    }

    #endregion

    #region System Health Assessment & Animated Circular Score Ring

    private async Task EvaluateAndApplySystemHealthAsync(bool isInitial = false)
    {
        int calculatedScore = 91;
        int healthScore = 99;
        int perfScore = 90;
        int secScore = 98;
        int stabScore = 78;
        int detectedIssues = 0;
        long junkBytes = 0;
        int startupCount = 0;
        bool isTelemetryActive = false;
        bool isVisualFxUnoptimized = false;

        await Task.Run(() =>
        {
            try
            {
                string[] tempPaths = { Path.GetTempPath(), @"C:\Windows\Temp" };
                foreach (var p in tempPaths)
                {
                    if (!Directory.Exists(p)) continue;
                    try
                    {
                        foreach (var fi in new DirectoryInfo(p).EnumerateFiles("*", SearchOption.TopDirectoryOnly))
                            junkBytes += fi.Length;
                    }
                    catch { }
                }

                try
                {
                    using var rk1 = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                    if (rk1 != null) startupCount += rk1.ValueCount;
                    using var rk2 = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                    if (rk2 != null) startupCount += rk2.ValueCount;
                }
                catch { }

                try
                {
                    var allowTel = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 1);
                    if (allowTel != null && Convert.ToInt32(allowTel) != 0) isTelemetryActive = true;
                }
                catch { }

                try
                {
                    var fxVal = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 1);
                    if (fxVal != null && Convert.ToInt32(fxVal) != 2) isVisualFxUnoptimized = true;
                }
                catch { }

                bool smartHealthy = true;
                bool rebootPending = false;
                try
                {
                    var disks = HardwareHealthService.GetDiskHealth();
                    smartHealthy = !disks.Any(d => d.PredictFailure == true);
                    rebootPending = HardwareHealthService.IsRebootPending();
                }
                catch { }

                if (isTelemetryActive) detectedIssues++;
                if (isVisualFxUnoptimized) detectedIssues++;
                if (!smartHealthy) detectedIssues++;
                if (rebootPending) detectedIssues++;

                double junkMb = junkBytes / (1024.0 * 1024.0);
                int healthDeduct = (int)Math.Min(18, junkMb / 150.0) + (isTelemetryActive ? 10 : 0) + (!smartHealthy ? 25 : 0);
                healthScore = Math.Clamp(100 - healthDeduct, 40, 100);

                double ramPct = _viewModel.RamMetric.Value;
                int perfDeduct = ramPct > 50 ? (int)((ramPct - 50) * 0.5) : 0;
                perfScore = Math.Clamp(100 - perfDeduct, 45, 100);

                secScore = Math.Clamp(100 - (isTelemetryActive ? 12 : 2), 60, 100);

                int stabDeduct = Math.Min(16, Math.Max(0, startupCount - 6) * 2) + (isVisualFxUnoptimized ? 6 : 0) + (rebootPending ? 5 : 0);
                stabScore = Math.Clamp(100 - stabDeduct, 55, 100);

                calculatedScore = (int)Math.Clamp(Math.Round((healthScore * 0.25) + (perfScore * 0.35) + (secScore * 0.20) + (stabScore * 0.20)), 1, 100);
            }
            catch { }
        });

        _unresolvedIssues = detectedIssues;
        TxtIssuesCount.Text = detectedIssues.ToString();
        IssuesDonutCanvas.Invalidate();

        TxtHealthSubScore.Text = $"{healthScore}% · {GetScoreLabel(healthScore)}";
        TxtPerfSubScore.Text = $"{perfScore}% · {GetScoreLabel(perfScore)}";
        TxtSecSubScore.Text = $"{secScore}% · {GetScoreLabel(secScore)}";
        TxtStabSubScore.Text = $"{stabScore}% · {GetScoreLabel(stabScore)}";

        AnimateScoreRing(calculatedScore, healthScore, perfScore, secScore, stabScore, isInitial);

        if (calculatedScore >= 90 && detectedIssues == 0)
        {
            TxtStatusValue.Text = "EXCELLENT";
            TxtStatusValue.Foreground = ThemeEffects.GetBrush("State.Good");
            StatusDot.Fill = ThemeEffects.GetBrush("State.Good");
            TxtScoreHeadline.Text = "NOTHING TO FIX · SYSTEM HEALTHY";
            TxtScoreSubtitle.Text = "Disk SMART, startup load, visual settings, and telemetry baseline all look clean.";
        }
        else if (calculatedScore >= 75)
        {
            TxtStatusValue.Text = "OPTIMAL";
            TxtStatusValue.Foreground = ThemeEffects.GetBrush("Accent.Cyan");
            StatusDot.Fill = ThemeEffects.GetBrush("Accent.Cyan");
            TxtScoreHeadline.Text = "OPTIMIZATIONS AVAILABLE";
            TxtScoreSubtitle.Text = $"{detectedIssues} recommended adjustments available to improve latency.";
        }
        else
        {
            TxtStatusValue.Text = "NEEDS ATTENTION";
            TxtStatusValue.Foreground = ThemeEffects.GetBrush("State.Warn");
            StatusDot.Fill = ThemeEffects.GetBrush("State.Warn");
            TxtScoreHeadline.Text = "SYSTEM BOTTLENECK DETECTED";
            TxtScoreSubtitle.Text = "Background processes and telemetry are impacting responsiveness.";
        }

        double junkMbFinal = junkBytes / (1024.0 * 1024.0);
        TxtQuickCleanSubtitle.Text = junkMbFinal > 1024 ? $"Reclaim {(junkMbFinal / 1024.0):F1} GB" : $"Reclaim {junkMbFinal:N0} MB";
        TxtQuickRamSubtitle.Text = _viewModel.RamMetric.SubLabel;
        TxtQuickStartupSubtitle.Text = $"{startupCount} apps configured";
        TxtQuickOptimizeSubtitle.Text = detectedIssues > 0 ? $"Fix {detectedIssues} issues (+{(100 - calculatedScore)} pts)" : "System fully tuned";

        ApplyIssueChipState(BtnFixVisualEffects, DotVisualEffects, isVisualFxUnoptimized);
        ApplyIssueChipState(BtnFixTelemetry, DotTelemetry, isTelemetryActive);
    }

    private void ApplyIssueChipState(Button fixBtn, Microsoft.UI.Xaml.Shapes.Ellipse dot, bool hasIssue)
    {
        if (!hasIssue)
        {
            dot.Fill = ThemeEffects.GetBrush("State.Good");
            fixBtn.Content = "FIXED ✓";
            fixBtn.IsEnabled = false;
            fixBtn.Opacity = 0.65;
        }
        else
        {
            dot.Fill = ThemeEffects.GetBrush("State.Warn");
            fixBtn.Content = "FIX";
            fixBtn.IsEnabled = true;
            fixBtn.Opacity = 1.0;
        }
    }

    private static string GetScoreLabel(int score) =>
        score >= 90 ? "Excellent" : score >= 80 ? "Optimal" : score >= 70 ? "Good" : "Attention";

    private void AnimateScoreRing(int finalScore, int healthScore, int perfScore, int secScore, int stabScore, bool fromZero)
    {
        finalScore = Math.Clamp(finalScore, 0, 100);
        healthScore = Math.Clamp(healthScore, 0, 100);
        perfScore = Math.Clamp(perfScore, 0, 100);
        secScore = Math.Clamp(secScore, 0, 100);
        stabScore = Math.Clamp(stabScore, 0, 100);
        _targetArcProgress = finalScore / 100.0;

        if (!MotionHelper.ShouldAnimate)
        {
            MotionHelper.StopFrameLoop("ScoreChoreography");
            _currentArcProgress = _targetArcProgress;
            _driveRingScaleC = _driveRingScaleD = _driveRingScaleE = 1.0;
            HeroScoreText.Text = finalScore.ToString();
            MainScoreBar.Value = finalScore;
            BarHealthSubScore.Value = healthScore;
            BarPerfSubScore.Value = perfScore;
            BarSecSubScore.Value = secScore;
            BarStabSubScore.Value = stabScore;
            ElementCompositionPreview.GetElementVisual(ScoreRingContainer).Scale = Vector3.One;
            ScoreArcCanvas.Invalidate();
            InvalidateDriveRings();
            _isScanning = false;
            return;
        }

        _isScanning = true;
        double startArc = fromZero ? 0.0 : _currentArcProgress;
        double startMainBar = fromZero ? 0.0 : MainScoreBar.Value;
        double startH = fromZero ? 0.0 : BarHealthSubScore.Value;
        double startP = fromZero ? 0.0 : BarPerfSubScore.Value;
        double startS = fromZero ? 0.0 : BarSecSubScore.Value;
        double startSt = fromZero ? 0.0 : BarStabSubScore.Value;
        double startDriveC = fromZero ? 0.0 : _driveRingScaleC;
        double startDriveD = fromZero ? 0.0 : _driveRingScaleD;
        double startDriveE = fromZero ? 0.0 : _driveRingScaleE;

        const double durationMs = 700.0;
        const double maxStaggerMs = 120.0;
        bool reboundTriggered = false;

        static double StaggerSpring(double elapsedMs, int index, double stepMs, double durMs)
        {
            double st = Math.Clamp((elapsedMs - index * stepMs) / durMs, 0.0, 1.0);
            return Math.Clamp(MotionHelper.DampedSpringCurve(st), 0.0, 1.0);
        }

        MotionHelper.StartFrameLoop("ScoreChoreography", elapsedMs =>
        {
            if (!MotionHelper.ShouldAnimate || elapsedMs >= durationMs + maxStaggerMs)
            {
                _currentArcProgress = _targetArcProgress;
                _driveRingScaleC = _driveRingScaleD = _driveRingScaleE = 1.0;
                HeroScoreText.Text = finalScore.ToString();
                MainScoreBar.Value = finalScore;
                BarHealthSubScore.Value = healthScore;
                BarPerfSubScore.Value = perfScore;
                BarSecSubScore.Value = secScore;
                BarStabSubScore.Value = stabScore;
                ElementCompositionPreview.GetElementVisual(ScoreRingContainer).Scale = Vector3.One;
                ScoreArcCanvas.Invalidate();
                InvalidateDriveRings();
                _isScanning = false;
                return false;
            }

            double t = Math.Clamp(elapsedMs / durationMs, 0.0, 1.0);
            double rawCurve = MotionHelper.DampedSpringCurve(t);
            double clampedCurve = Math.Clamp(rawCurve, 0.0, 1.0);

            if (!reboundTriggered && rawCurve >= 1.0)
            {
                reboundTriggered = true;
                MotionHelper.PrepareVisual(ScoreRingContainer);
                ElementCompositionPreview.GetElementVisual(ScoreRingContainer).Scale = new Vector3(1.045f, 1.045f, 1f);
                MotionHelper.SpringScale(ScoreRingContainer, 1f, MotionHelper.Snappy);
            }

            _currentArcProgress = Math.Clamp(startArc + ((_targetArcProgress - startArc) * clampedCurve), 0.0, 1.0);
            double barVal = Math.Clamp(startMainBar + ((finalScore - startMainBar) * clampedCurve), 0.0, finalScore);
            HeroScoreText.Text = Math.Clamp((int)Math.Round(barVal), 0, finalScore).ToString();
            MainScoreBar.Value = Math.Clamp(barVal, 0.0, 100.0);

            BarHealthSubScore.Value = Math.Clamp(startH + ((healthScore - startH) * StaggerSpring(elapsedMs, 0, 40.0, durationMs)), 0.0, 100.0);
            BarPerfSubScore.Value = Math.Clamp(startP + ((perfScore - startP) * StaggerSpring(elapsedMs, 1, 40.0, durationMs)), 0.0, 100.0);
            BarSecSubScore.Value = Math.Clamp(startS + ((secScore - startS) * StaggerSpring(elapsedMs, 2, 40.0, durationMs)), 0.0, 100.0);
            BarStabSubScore.Value = Math.Clamp(startSt + ((stabScore - startSt) * StaggerSpring(elapsedMs, 3, 40.0, durationMs)), 0.0, 100.0);

            _driveRingScaleC = Math.Clamp(startDriveC + ((1.0 - startDriveC) * StaggerSpring(elapsedMs, 0, 60.0, durationMs)), 0.0, 1.0);
            _driveRingScaleD = Math.Clamp(startDriveD + ((1.0 - startDriveD) * StaggerSpring(elapsedMs, 1, 60.0, durationMs)), 0.0, 1.0);
            _driveRingScaleE = Math.Clamp(startDriveE + ((1.0 - startDriveE) * StaggerSpring(elapsedMs, 2, 60.0, durationMs)), 0.0, 1.0);

            ScoreArcCanvas.Invalidate();
            InvalidateDriveRings();
            return true;
        });
    }

    private async void BtnRescan_Click(object sender, RoutedEventArgs e)
    {
        if (_isScanning) return;
        await EvaluateAndApplySystemHealthAsync(isInitial: true);
    }

    #endregion

    #region Win2D Rings & Cubic-Spline Sparklines

    private void ScoreArcCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        ds.Antialiasing = CanvasAntialiasing.Antialiased;

        float width = (float)sender.ActualWidth;
        float height = (float)sender.ActualHeight;
        if (width <= 0 || height <= 0) return;

        var center = new Vector2(width / 2f, height / 2f);
        float radius = (Math.Min(width, height) / 2f) - 10f;
        float strokeWidth = 8f;
        bool glowOn = ThemeEffects.EffectsEnabled;

        // Neutral Border.Subtle track
        ds.DrawCircle(center, radius, _trackColor, strokeWidth);

        // Full 360 gradient sweep starting at -90 (12 o'clock)
        float sweepAngle = (float)(359.5 * _currentArcProgress);
        if (sweepAngle > 1f)
        {
            float startRad = (float)(-Math.PI / 2.0);
            float endRad = startRad + (float)(sweepAngle * Math.PI / 180.0);
            var startPt = center + new Vector2((float)Math.Cos(startRad), (float)Math.Sin(startRad)) * radius;
            var endPt = center + new Vector2((float)Math.Cos(endRad), (float)Math.Sin(endRad)) * radius;

            using var pb = new CanvasPathBuilder(ds);
            pb.BeginFigure(startPt);
            pb.AddArc(endPt, radius, radius, 0, CanvasSweepDirection.Clockwise, sweepAngle >= 180f ? CanvasArcSize.Large : CanvasArcSize.Small);
            pb.EndFigure(CanvasFigureLoop.Open);

            using var geom = CanvasGeometry.CreatePath(pb);
            if (glowOn)
            {
                ds.DrawGeometry(geom, _cyanGlowColor, strokeWidth + 6f, _roundStrokeStyle);
            }

            using var grad = new CanvasLinearGradientBrush(ds, _cyanColor, _purpleColor)
            {
                StartPoint = new Vector2(0, 0),
                EndPoint = new Vector2(width, height)
            };
            ds.DrawGeometry(geom, grad, strokeWidth, _roundStrokeStyle);

            if (glowOn)
            {
                ds.FillCircle(endPt, 6.5f, _cyanGlowColor);
            }
            ds.FillCircle(endPt, 3f, _cyanColor);
        }
    }

    private void DriveRingCanvasC_Draw(CanvasControl sender, CanvasDrawEventArgs args) => DrawRingGauge(sender, args, _drivePctC * _driveRingScaleC, _cyanColor, _cyanGlowColor);
    private void DriveRingCanvasD_Draw(CanvasControl sender, CanvasDrawEventArgs args) => DrawRingGauge(sender, args, _drivePctD * _driveRingScaleD, _purpleColor, _purpleGlowColor);
    private void DriveRingCanvasE_Draw(CanvasControl sender, CanvasDrawEventArgs args) => DrawRingGauge(sender, args, _drivePctE * _driveRingScaleE, _greenColor, _greenGlowColor);

    private void IssuesDonutCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var color = _unresolvedIssues > 0 ? _yellowColor : _greenColor;
        var glow = _unresolvedIssues > 0 ? _yellowGlowColor : _greenGlowColor;
        double pct = _unresolvedIssues > 0 ? Math.Min(85.0, _unresolvedIssues * 25.0) : 100.0;
        DrawRingGauge(sender, args, pct, color, glow, strokeWidth: 5.5f, trackColorOverride: _trackColor);
    }

    private void DrawRingGauge(CanvasControl sender, CanvasDrawEventArgs args, double percent, Windows.UI.Color strokeColor, Windows.UI.Color glowColor, float strokeWidth = 3.5f, Windows.UI.Color? trackColorOverride = null)
    {
        var ds = args.DrawingSession;
        ds.Antialiasing = CanvasAntialiasing.Antialiased;

        float w = (float)sender.ActualWidth;
        float h = (float)sender.ActualHeight;
        if (w <= 0 || h <= 0) return;

        var center = new Vector2(w / 2f, h / 2f);
        float radius = (Math.Min(w, h) / 2f) - strokeWidth;

        ds.DrawCircle(center, radius, trackColorOverride ?? _trackColor, strokeWidth);

        float sweep = (float)(359.5 * Math.Clamp(percent / 100.0, 0.0, 1.0));
        if (sweep > 1f)
        {
            float startRad = (float)(-Math.PI / 2.0);
            float endRad = startRad + (float)(sweep * Math.PI / 180.0);
            var startPt = center + new Vector2((float)Math.Cos(startRad), (float)Math.Sin(startRad)) * radius;
            var endPt = center + new Vector2((float)Math.Cos(endRad), (float)Math.Sin(endRad)) * radius;

            using var pb = new CanvasPathBuilder(ds);
            pb.BeginFigure(startPt);
            pb.AddArc(endPt, radius, radius, 0, CanvasSweepDirection.Clockwise, sweep >= 180f ? CanvasArcSize.Large : CanvasArcSize.Small);
            pb.EndFigure(CanvasFigureLoop.Open);

            using var geom = CanvasGeometry.CreatePath(pb);
            if (ThemeEffects.EffectsEnabled)
                ds.DrawGeometry(geom, glowColor, strokeWidth + 3.5f, _roundStrokeStyle);
            ds.DrawGeometry(geom, strokeColor, strokeWidth, _roundStrokeStyle);
        }
    }

    private void InvalidateDriveRings()
    {
        DriveRingCanvasC?.Invalidate();
        DriveRingCanvasD?.Invalidate();
        DriveRingCanvasE?.Invalidate();
    }

    private void CpuSparkCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args) => DrawSparkline(sender, args, _viewModel.CpuMetric.History, _cyanColor, _cyanGlowColor, _trackColor, _sparkCoordsBuffer, _sparkInitialized ? _sparkCurrent[0] : null);
    private void GpuSparkCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args) => DrawSparkline(sender, args, _viewModel.GpuMetric.History, _purpleColor, _purpleGlowColor, _trackColor, _sparkCoordsBuffer, _sparkInitialized ? _sparkCurrent[1] : null);
    private void RamSparkCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args) => DrawSparkline(sender, args, _viewModel.RamMetric.History, _greenColor, _greenGlowColor, _trackColor, _sparkCoordsBuffer, _sparkInitialized ? _sparkCurrent[2] : null);
    private void DiskSparkCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args) => DrawSparkline(sender, args, _viewModel.DiskMetric.History, _yellowColor, _yellowGlowColor, _trackColor, _sparkCoordsBuffer, _sparkInitialized ? _sparkCurrent[3] : null);

    private static void DrawSparkline(
        CanvasControl sender,
        CanvasDrawEventArgs args,
        IReadOnlyList<float> points,
        Windows.UI.Color strokeColor,
        Windows.UI.Color glowColor,
        Windows.UI.Color gridColor,
        Vector2[] coords,
        float? overrideLastPoint = null)
    {
        var ds = args.DrawingSession;
        ds.Antialiasing = CanvasAntialiasing.Antialiased;

        float width = (float)sender.ActualWidth;
        float height = (float)sender.ActualHeight;
        int count = Math.Min(points.Count, coords.Length);
        if (width <= 0 || height <= 0 || count < 2) return;

        // Neutral mid gridline at Border.Subtle
        float midY = height * 0.5f;
        ds.DrawLine(0f, midY, width, midY, gridColor, 1f);

        float stepX = width / (count - 1);
        for (int i = 0; i < count; i++)
        {
            float val = (i == count - 1 && overrideLastPoint.HasValue) ? overrideLastPoint.Value : points[points.Count - count + i];
            float x = i * stepX;
            float y = height - (Math.Clamp(val, 0f, 100f) / 100f * (height - 6f)) - 3f;
            coords[i] = new Vector2(x, y);
        }

        using var strokePb = new CanvasPathBuilder(ds);
        using var fillPb = new CanvasPathBuilder(ds);

        strokePb.BeginFigure(coords[0]);
        fillPb.BeginFigure(new Vector2(coords[0].X, height));
        fillPb.AddLine(coords[0]);

        for (int i = 0; i < count - 1; i++)
        {
            Vector2 p0 = coords[Math.Max(0, i - 1)];
            Vector2 p1 = coords[i];
            Vector2 p2 = coords[i + 1];
            Vector2 p3 = coords[Math.Min(count - 1, i + 2)];

            Vector2 cp1 = new(p1.X + (p2.X - p0.X) / 6f, Math.Clamp(p1.Y + (p2.Y - p0.Y) / 6f, 2f, height - 2f));
            Vector2 cp2 = new(p2.X - (p3.X - p1.X) / 6f, Math.Clamp(p2.Y - (p3.Y - p1.Y) / 6f, 2f, height - 2f));

            strokePb.AddCubicBezier(cp1, cp2, p2);
            fillPb.AddCubicBezier(cp1, cp2, p2);
        }

        strokePb.EndFigure(CanvasFigureLoop.Open);
        fillPb.AddLine(new Vector2(coords[count - 1].X, height));
        fillPb.EndFigure(CanvasFigureLoop.Closed);

        using var fillGeom = CanvasGeometry.CreatePath(fillPb);
        using var fillBrush = new CanvasLinearGradientBrush(ds,
            Windows.UI.Color.FromArgb(46, strokeColor.R, strokeColor.G, strokeColor.B),
            Windows.UI.Color.FromArgb(0, strokeColor.R, strokeColor.G, strokeColor.B))
        {
            StartPoint = new Vector2(0, 0),
            EndPoint = new Vector2(0, height)
        };
        ds.FillGeometry(fillGeom, fillBrush);

        using var geom = CanvasGeometry.CreatePath(strokePb);
        if (ThemeEffects.EffectsEnabled)
            ds.DrawGeometry(geom, glowColor, 4.5f, _roundStrokeStyle);
        ds.DrawGeometry(geom, strokeColor, 1.75f, _roundStrokeStyle);
        ds.FillCircle(coords[count - 1], 2.5f, strokeColor);
    }

    private float GetLatestMetricPoint(int idx)
    {
        var history = idx switch
        {
            0 => _viewModel.CpuMetric.History,
            1 => _viewModel.GpuMetric.History,
            2 => _viewModel.RamMetric.History,
            _ => _viewModel.DiskMetric.History
        };
        return history.Count > 0 ? history[^1] : 0f;
    }

    private void InitSparklineSamples()
    {
        for (int i = 0; i < 4; i++)
        {
            float v = GetLatestMetricPoint(i);
            _sparkStart[i] = _sparkCurrent[i] = _sparkTarget[i] = v;
        }
        _sparkInitialized = true;
        InvalidateSparkCanvases();
    }

    private void InvalidateSparkCanvases()
    {
        CpuSparkCanvas?.Invalidate();
        GpuSparkCanvas?.Invalidate();
        RamSparkCanvas?.Invalidate();
        DiskSparkCanvas?.Invalidate();
    }

    private void SparklineTimer_Tick(object? sender, object e)
    {
        bool anyChanged = false;
        for (int i = 0; i < 4; i++)
        {
            float next = GetLatestMetricPoint(i);
            if (Math.Abs(next - _sparkTarget[i]) > 0.01f) anyChanged = true;
            _sparkStart[i] = _sparkCurrent[i];
            _sparkTarget[i] = next;
        }
        _sparkInitialized = true;

        if (!MotionHelper.ShouldAnimate)
        {
            MotionHelper.StopFrameLoop("Sparklines");
            for (int i = 0; i < 4; i++) _sparkCurrent[i] = _sparkTarget[i];
            InvalidateSparkCanvases();
            return;
        }

        if (!anyChanged)
        {
            InvalidateSparkCanvases();
            return;
        }

        const double durationMs = 320.0;
        MotionHelper.StartFrameLoop("Sparklines", elapsedMs =>
        {
            double t = Math.Clamp(elapsedMs / durationMs, 0.0, 1.0);
            float eased = (float)(1.0 - Math.Pow(1.0 - t, 3.0));
            for (int i = 0; i < 4; i++)
                _sparkCurrent[i] = _sparkStart[i] + (_sparkTarget[i] - _sparkStart[i]) * eased;

            InvalidateSparkCanvases();

            if (t >= 1.0)
            {
                for (int i = 0; i < 4; i++) _sparkCurrent[i] = _sparkTarget[i];
                return false;
            }
            return true;
        });
    }

    private void Metric_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Models.MetricItem.DisplayValue) or nameof(Models.MetricItem.SubLabel))
            DispatcherQueue.TryEnqueue(UpdateAllMetricDisplays);
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DashboardViewModel.OverallScore) && !_isScanning)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _targetArcProgress = Math.Clamp(_viewModel.OverallScore, 0, 100) / 100.0;
                _currentArcProgress = _targetArcProgress;
                ScoreArcCanvas?.Invalidate();
            });
        }
    }

    private void UpdateAllMetricDisplays()
    {
        CpuPercentText.Text = _viewModel.CpuMetric.DisplayValue;
        GpuPercentText.Text = _viewModel.GpuMetric.DisplayValue;
        if (!string.IsNullOrEmpty(_viewModel.GpuMetric.SubLabel))
            GpuSubLabelText.Text = _viewModel.GpuMetric.SubLabel;
        RamPercentText.Text = _viewModel.RamMetric.DisplayValue;
        RamSubLabelText.Text = _viewModel.RamMetric.SubLabel;
        DiskPercentText.Text = _viewModel.DiskMetric.DisplayValue;
        TxtQuickRamSubtitle.Text = _viewModel.RamMetric.SubLabel;
    }

    #endregion

    #region Storage Overview Loader

    private void LoadStorageMetrics()
    {
        try
        {
            var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed).ToList();
            if (drives.Count == 0) return;

            var cDrive = drives.FirstOrDefault(d => d.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase)) ?? drives[0];
            ApplyDriveInfo(cDrive, "System Drive", TxtDriveCLabel, TxtDriveCTotal, TxtDriveCUsedFree, TxtDriveCPercent, BorderDriveC, out _drivePctC);

            var dDrive = drives.FirstOrDefault(d => d != cDrive);
            if (dDrive != null)
                ApplyDriveInfo(dDrive, "Storage Drive", TxtDriveDLabel, TxtDriveDTotal, TxtDriveDUsedFree, TxtDriveDPercent, BorderDriveD, out _drivePctD);
            else
                BorderDriveD.Visibility = Visibility.Collapsed;

            var eDrive = drives.FirstOrDefault(d => d != cDrive && d != dDrive);
            if (eDrive != null)
                ApplyDriveInfo(eDrive, "Data Drive", TxtDriveELabel, TxtDriveETotal, TxtDriveEUsedFree, TxtDriveEPercent, BorderDriveE, out _drivePctE);
            else
                BorderDriveE.Visibility = Visibility.Collapsed;

            InvalidateDriveRings();
        }
        catch { }
    }

    private static void ApplyDriveInfo(DriveInfo drive, string prefix, TextBlock lbl, TextBlock tot, TextBlock usedFree, TextBlock pctTxt, UIElement container, out double pctOut)
    {
        double totalGb = drive.TotalSize / (1024.0 * 1024.0 * 1024.0);
        double freeGb = drive.TotalFreeSpace / (1024.0 * 1024.0 * 1024.0);
        double usedGb = totalGb - freeGb;
        pctOut = totalGb > 0 ? (usedGb / totalGb) * 100.0 : 0.0;

        lbl.Text = $"{prefix} ({drive.Name.TrimEnd('\\')})";
        tot.Text = $"Total: {totalGb:N1} GB";
        usedFree.Text = $"Used: {usedGb:N1} GB · Free: {freeGb:N1} GB";
        pctTxt.Text = $"{Math.Round(pctOut)}%";
        container.Visibility = Visibility.Visible;
    }

    #endregion

    #region Quick Actions, Utilities, Auto-Purge & Optimization Summary Chips

    private void StartHeroGlowPulse()
    {
        if (_heroPulseActive || !MotionHelper.ShouldAnimate) return;
        if (FindDescendantByName(HeroStartMissionBtn, "PART_AccentRim") is not Border rim) return;

        var visual = ElementCompositionPreview.GetElementVisual(rim);
        var anim = visual.Compositor.CreateScalarKeyFrameAnimation();
        anim.InsertKeyFrame(0f, 0.40f);
        anim.InsertKeyFrame(0.5f, 0.56f);
        anim.InsertKeyFrame(1f, 0.40f);
        anim.Duration = TimeSpan.FromSeconds(3);
        anim.IterationBehavior = Microsoft.UI.Composition.AnimationIterationBehavior.Forever;
        visual.StartAnimation("Opacity", anim);
        _heroPulseActive = true;
    }

    private void StopHeroGlowPulse()
    {
        if (!_heroPulseActive) return;
        _heroPulseActive = false;
        if (FindDescendantByName(HeroStartMissionBtn, "PART_AccentRim") is Border rim)
        {
            var visual = ElementCompositionPreview.GetElementVisual(rim);
            visual.StopAnimation("Opacity");
            visual.Opacity = 0.45f;
        }
    }

    private static FrameworkElement? FindDescendantByName(DependencyObject root, string name)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe && fe.Name == name)
                return fe;
            var found = FindDescendantByName(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string pageTag)
            MainWindow.Current?.NavigateTo(pageTag);
    }

    private async void ActionTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement tile || tile.Tag is not string actionId) return;

        switch (actionId)
        {
            case "optimize_all":
                await RunFullHeroOptimizationAsync();
                break;
            case "temp_clean":
                await _viewModel.ExecuteQuickActionAsync("temp_clean");
                MainWindow.Current?.ShowToast("Junk Cleaned", "Temporary files and system caches purged.", "State.Good");
                await EvaluateAndApplySystemHealthAsync();
                break;
            case "game_mode":
                await _viewModel.ExecuteQuickActionAsync("game_mode");
                MainWindow.Current?.ShowToast("Performance Boosted", "Low-latency CPU scheduler and GPU priority active.", "Accent.Cyan");
                await EvaluateAndApplySystemHealthAsync();
                break;
            case "ram_purge":
                await _viewModel.ExecuteQuickActionAsync("ram_purge");
                MainWindow.Current?.ShowToast("RAM Cleaned", "Standby list and working sets trimmed.", "State.Good");
                await EvaluateAndApplySystemHealthAsync();
                break;
            case "nav_startup":
                MainWindow.Current?.NavigateTo("Startup");
                break;
            case "nav_network":
                MainWindow.Current?.NavigateTo("Network");
                break;
        }
    }

    private async void UtilTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement tile || tile.Tag is not string utilId) return;

        switch (utilId)
        {
            case "restore_point":
                MainWindow.Current?.ShowToast("Creating Restore Point", "Generating Windows system restore snapshot...", "Accent.Cyan");
                try
                {
                    var res = await OptimizationEngine.CreateRestorePointAsync(CancellationToken.None);
                    MainWindow.Current?.ShowToast("Restore Point Created", res, "State.Good");
                }
                catch (Exception ex)
                {
                    MainWindow.Current?.ShowToast("Restore Point Failed", ex.Message, "State.Bad");
                }
                break;
            case "benchmark":
                MainWindow.Current?.NavigateTo("Toolbox");
                break;
            case "restart_explorer":
                try
                {
                    foreach (var ep in Process.GetProcessesByName("explorer"))
                    using (ep)
                    {
                        try { ep.Kill(); } catch { }
                    }
                    Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
                    {
                        UseShellExecute = true
                    });
                    MainWindow.Current?.ShowToast("Explorer Restarted", "Windows shell refreshed cleanly.", "Accent.Cyan");
                }
                catch (Exception ex)
                {
                    MainWindow.Current?.ShowToast("Restart Failed", ex.Message, "State.Bad");
                }
                break;
            case "command_palette":
                MainWindow.Current?.OpenCommandPalette();
                break;
        }
    }

    private void InitAutoTimerSelection()
    {
        try
        {
            int savedTimer = Convert.ToInt32(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\JamesOptimizer", "AutoTimer", 0) ?? 0);
            foreach (var item in ComboAutoFreq.Items)
            {
                if (item is ComboBoxItem cbi && int.TryParse(cbi.Tag?.ToString(), out int m) && m == savedTimer)
                {
                    ComboAutoFreq.SelectedItem = cbi;
                    UpdateAutoStatusPill(savedTimer);
                    return;
                }
            }
            ComboAutoFreq.SelectedIndex = 0;
            UpdateAutoStatusPill(0);
        }
        catch { ComboAutoFreq.SelectedIndex = 0; }
    }

    private async void ComboAutoFreq_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializingControls) return;
        if (ComboAutoFreq.SelectedItem is ComboBoxItem cbi && int.TryParse(cbi.Tag?.ToString(), out int minutes))
        {
            try { Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\JamesOptimizer", "AutoTimer", minutes, RegistryValueKind.DWord); } catch { }
            UpdateAutoStatusPill(minutes);
            await BackgroundServiceClient.SetAutoTimerAsync(minutes);
            MainWindow.Current?.ShowToast("Auto-Purge Schedule", minutes == 0 ? "Auto-optimization stopped." : $"Scheduled every {minutes} minutes.", "Accent.Cyan");
        }
    }

    private void UpdateAutoStatusPill(int minutes)
    {
        if (minutes <= 0)
        {
            TxtAutoStatusPill.Text = "STOPPED";
            TxtAutoStatusPill.Foreground = ThemeEffects.GetBrush("Text.Secondary");
            AutoStatusDot.Fill = ThemeEffects.GetBrush("Text.Tertiary");
        }
        else
        {
            TxtAutoStatusPill.Text = "ACTIVE";
            TxtAutoStatusPill.Foreground = ThemeEffects.GetBrush("State.Good");
            AutoStatusDot.Fill = ThemeEffects.GetBrush("State.Good");
        }
    }

    private async void BtnRunAutoNow_Click(object sender, RoutedEventArgs e)
    {
        BtnRunAutoNow.IsEnabled = false;
        try
        {
            await _viewModel.ExecuteQuickActionAsync("ram_purge");
            await _viewModel.ExecuteQuickActionAsync("temp_clean");
            TxtLastOptimized.Text = $"Today, {DateTime.Now:h:mm tt}";
            await EvaluateAndApplySystemHealthAsync();
            MainWindow.Current?.ShowToast("Auto-Purge Complete", "Working sets trimmed and temp caches purged.", "State.Good");
        }
        finally
        {
            BtnRunAutoNow.IsEnabled = true;
        }
    }

    private async void BtnFixVisualEffects_Click(object sender, RoutedEventArgs e)
    {
        var res = await Task.Run(() => TweakCatalog.ApplyTweak("visuals"));
        ApplyIssueChipState(BtnFixVisualEffects, DotVisualEffects, res.Status == TweakStatus.Failed);
        await EvaluateAndApplySystemHealthAsync();
        MainWindow.Current?.ShowToast("Visual Effects", res.Message, res.Status is TweakStatus.Success or TweakStatus.AlreadyApplied ? "State.Good" : "State.Bad");
    }

    private async void BtnFixTelemetry_Click(object sender, RoutedEventArgs e)
    {
        var res = await Task.Run(() => TweakCatalog.ApplyTweak("telemetry"));
        ApplyIssueChipState(BtnFixTelemetry, DotTelemetry, res.Status == TweakStatus.Failed);
        await EvaluateAndApplySystemHealthAsync();
        MainWindow.Current?.ShowToast("Telemetry Shield", res.Message, res.Status is TweakStatus.Success or TweakStatus.AlreadyApplied ? "State.Good" : "State.Bad");
    }

    private void BtnNavVisualEffects_Click(object sender, RoutedEventArgs e) => MainWindow.Current?.NavigateTo("Performance");
    private void BtnNavTelemetry_Click(object sender, RoutedEventArgs e) => MainWindow.Current?.NavigateTo("Cleanups");
    private void BtnOpenDiagnostics_Click(object sender, RoutedEventArgs e) => MainWindow.Current?.NavigateTo("Diagnostics");
    private void BtnOpenStorage_Click(object sender, RoutedEventArgs e) => MainWindow.Current?.NavigateTo("Storage");

    #endregion

    #region Hero CTA ("START SYSTEM OPTIMIZATION") & Responsive Layout

    private async void HeroStartMissionBtn_Click(object sender, RoutedEventArgs e) =>
        await RunFullHeroOptimizationAsync();

    private async Task RunFullHeroOptimizationAsync()
    {
        if (_isOptimizing) return;
        _isOptimizing = true;
        HeroStartMissionBtn.IsEnabled = false;

        HeroBtnPlayIcon.Visibility = Visibility.Collapsed;
        HeroBtnCheckIcon.Visibility = Visibility.Collapsed;
        HeroBtnProgress.Visibility = Visibility.Visible;
        HeroBtnProgress.IsActive = true;
        HeroBtnLabel.Text = "ANALYZING KERNEL & MEMORY STATE...";

        var optTask = _viewModel.RunFullOptimizationAsync();

        await Task.Delay(500);
        if (!optTask.IsCompleted) HeroBtnLabel.Text = "PURGING TEMP CACHES & TRIMMING RAM...";

        await Task.Delay(500);
        if (!optTask.IsCompleted) HeroBtnLabel.Text = "APPLYING LATENCY & POWER POLICIES...";

        await optTask;

        HeroBtnProgress.IsActive = false;
        HeroBtnProgress.Visibility = Visibility.Collapsed;
        HeroBtnCheckIcon.Visibility = Visibility.Visible;
        HeroBtnLabel.Text = "SYSTEM OPTIMIZED · PEAK PERFORMANCE";
        TxtLastOptimized.Text = $"Today, {DateTime.Now:h:mm tt}";

        await EvaluateAndApplySystemHealthAsync();
        MainWindow.Current?.ShowToast("Full Optimization Complete", "Caches purged, standby RAM trimmed, and latency policies applied.", "State.Good");

        await Task.Delay(2400);
        HeroBtnCheckIcon.Visibility = Visibility.Collapsed;
        HeroBtnPlayIcon.Visibility = Visibility.Visible;
        HeroBtnLabel.Text = "START SYSTEM OPTIMIZATION";
        HeroStartMissionBtn.IsEnabled = true;
        _isOptimizing = false;
    }

    private void DashboardRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = e.NewSize.Width < 920;
        if (compact)
        {
            RightRailColDef.Width = new GridLength(0);
            LeftColumnStack.Margin = new Thickness(0, 0, 0, 12);
            Grid.SetColumn(RightColumnStack, 0);
            Grid.SetRow(RightColumnStack, 1);
        }
        else
        {
            RightRailColDef.Width = new GridLength(2, GridUnitType.Star);
            LeftColumnStack.Margin = new Thickness(0, 0, 16, 0);
            Grid.SetColumn(RightColumnStack, 1);
            Grid.SetRow(RightColumnStack, 0);
        }
    }

    #endregion
}
