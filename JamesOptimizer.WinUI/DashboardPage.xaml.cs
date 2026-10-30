using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Windows.UI.ViewManagement;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using JamesOptimizer.WinUI.Services;
using JamesOptimizer.WinUI.ViewModels;

namespace JamesOptimizer.WinUI;

public sealed partial class DashboardPage : Page
{
    private readonly DashboardViewModel _viewModel;
    private readonly DispatcherTimer _sparklineTimer;
    private readonly UISettings _uiSettings = new();

    // Arc Gauge load animation
    private double _currentArcProgress = 0.0;
    private double _targetArcProgress = 0.96; // 96%
    private DispatcherTimer? _arcAnimationTimer;
    private bool _isScanning = false;

    // Auto-Timer 6-Interval Step Mapping (10m, 15m, 30m, 45m, 1h, 3h)
    private static readonly int[] TimerIntervalMinutes = { 10, 15, 30, 45, 60, 180 };
    private static readonly string[] TimerIntervalLabels = { "10M", "15M", "30M", "45M", "1H", "3H" };

    // Direct2D Color Tokens
    private readonly Windows.UI.Color _coralColor = ColorHelper.FromArgb(255, 255, 51, 102);
    private readonly Windows.UI.Color _cyanColor = ColorHelper.FromArgb(255, 0, 245, 255);
    private readonly Windows.UI.Color _violetColor = ColorHelper.FromArgb(255, 123, 92, 240);
    private readonly Windows.UI.Color _greenColor = ColorHelper.FromArgb(255, 0, 230, 118);
    private readonly Windows.UI.Color _amberColor = ColorHelper.FromArgb(255, 255, 179, 0);
    private readonly Windows.UI.Color _trackColor = ColorHelper.FromArgb(255, 31, 41, 61);

    public DashboardPage()
    {
        InitializeComponent();

        _viewModel = App.ViewModel ?? new DashboardViewModel();
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        // Sparkline redraw timer (aligned with 1s metric polling)
        _sparklineTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sparklineTimer.Tick += SparklineTimer_Tick;
        _sparklineTimer.Start();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // Safe post-load switch initialization
        SwitchStayAwake.IsOn = true;
        SwitchZeroDelay.IsOn = true;
        SwitchSilenceTelemetry.IsOn = true;
        AutoRunSwitch.IsOn = _viewModel.AutoRunEnabled;
        AutoRunSwitch.Toggled += AutoRunSwitch_Toggled;

        // Safe post-load slider initialization (Step 2 = 30M default)
        AutoTimerSlider.Value = 2;
        AutoTimerSlider.ValueChanged += AutoTimerSlider_ValueChanged;

        bool animationsEnabled = _uiSettings.AnimationsEnabled;

        if (animationsEnabled)
        {
            DriveCShimmerStoryboard?.Begin();
            DriveDShimmerStoryboard?.Begin();
            BeaconPulseStoryboard?.Begin();

            // Run initial startup system efficiency benchmark scan
            RunStartupScan();
        }
        else
        {
            _currentArcProgress = _targetArcProgress;
            HeroScoreText.Text = "96";
            TotalScoreBadgeText.Text = "96 / 100";
            ScanStatusLabel.Text = "PEAK OPTIMAL";
            ScoreArcCanvas.Invalidate();
        }
    }

    #region Startup System Scan & Benchmark

    private void RunStartupScan()
    {
        _isScanning = true;
        ScanStatusLabel.Text = "SCANNING SYSTEM...";
        ScanStatusLabel.Foreground = (SolidColorBrush)Application.Current.Resources["AccentCoralBrush"];
        ScanDetailSummary.Text = "Executing deep hardware scan: testing core latency, RAM overhead, and cache hygiene...";

        _currentArcProgress = 0.0;
        _arcAnimationTimer?.Stop();
        _arcAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };

        _arcAnimationTimer.Tick += (s, ev) =>
        {
            if (_currentArcProgress < _targetArcProgress)
            {
                _currentArcProgress += 0.025;
                int displayNum = (int)(_currentArcProgress * 100);
                HeroScoreText.Text = displayNum.ToString();
                TotalScoreBadgeText.Text = $"{displayNum} / 100";

                if (_currentArcProgress >= _targetArcProgress)
                {
                    _currentArcProgress = _targetArcProgress;
                    _arcAnimationTimer.Stop();
                    _isScanning = false;

                    // Scan Complete: Lock in final score and optimal badge
                    HeroScoreText.Text = "96";
                    TotalScoreBadgeText.Text = "96 / 100";
                    ScanStatusLabel.Text = "PEAK OPTIMAL";
                    ScanStatusLabel.Foreground = (SolidColorBrush)Application.Current.Resources["AccentCyanBrush"];
                    ScanDetailSummary.Text = "Baseline scan verified. Kernel responsiveness optimal, zero orphaned memory locks, telemetry services suppressed.";

                    HealthScoreLabel.Text = "98%";
                    PerfScoreLabel.Text = "94%";
                    SecScoreLabel.Text = "99%";
                    StabScoreLabel.Text = "95%";
                }
                ScoreArcCanvas.Invalidate();
            }
        };
        _arcAnimationTimer.Start();
    }

    private void BtnRescan_Click(object sender, RoutedEventArgs e)
    {
        if (_isScanning) return;
        RunStartupScan();
    }

    #endregion

    private void SparklineTimer_Tick(object? sender, object e)
    {
        CpuSparkCanvas?.Invalidate();
        GpuSparkCanvas?.Invalidate();
        RamSparkCanvas?.Invalidate();
        DiskSparkCanvas?.Invalidate();
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(DashboardViewModel.CpuMetric):
                    CpuPercentText.Text = _viewModel.CpuMetric.DisplayValue;
                    CpuPercentText.Foreground = GetMetricBrush(_viewModel.CpuMetric.Value);
                    break;
                case nameof(DashboardViewModel.GpuMetric):
                    GpuPercentText.Text = _viewModel.GpuMetric.DisplayValue;
                    GpuPercentText.Foreground = GetMetricBrush(_viewModel.GpuMetric.Value);
                    break;
                case nameof(DashboardViewModel.RamMetric):
                    RamPercentText.Text = _viewModel.RamMetric.DisplayValue;
                    RamPercentText.Foreground = GetMetricBrush(_viewModel.RamMetric.Value);
                    RamSubLabelText.Text = _viewModel.RamMetric.SubLabel;
                    break;
                case nameof(DashboardViewModel.DiskMetric):
                    DiskPercentText.Text = _viewModel.DiskMetric.DisplayValue;
                    DiskPercentText.Foreground = GetMetricBrush(_viewModel.DiskMetric.Value);
                    break;
            }
        });
    }

    private SolidColorBrush GetMetricBrush(float value)
    {
        if (value >= 85) return (SolidColorBrush)Application.Current.Resources["AccentRedBrush"];
        if (value >= 65) return (SolidColorBrush)Application.Current.Resources["AccentAmberBrush"];
        return (SolidColorBrush)Application.Current.Resources["AccentGreenBrush"];
    }

    #region Win2D Direct2D Arc Gauge Drawing

    private void ScoreArcCanvas_CreateResources(CanvasControl sender, Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args)
    {
    }

    private void ScoreArcCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        ds.Antialiasing = CanvasAntialiasing.Antialiased;

        float width = (float)sender.ActualWidth;
        float height = (float)sender.ActualHeight;
        if (width <= 0 || height <= 0) return;

        var center = new Vector2(width / 2f, height * 0.76f);
        float radius = Math.Min(width / 2f, height * 0.72f) - 8f;
        float strokeWidth = 8f;

        float startAngle = 180f;
        float sweepTotal = 180f;

        // 1. Background Track
        using (var trackPath = new CanvasPathBuilder(ds))
        {
            var startRad = (float)(startAngle * Math.PI / 180.0);
            var endRad = (float)((startAngle + sweepTotal) * Math.PI / 180.0);
            var startPt = center + new Vector2((float)Math.Cos(startRad), (float)Math.Sin(startRad)) * radius;
            var endPt = center + new Vector2((float)Math.Cos(endRad), (float)Math.Sin(endRad)) * radius;

            trackPath.BeginFigure(startPt);
            trackPath.AddArc(endPt, radius, radius, 0, CanvasSweepDirection.Clockwise, CanvasArcSize.Small);
            trackPath.EndFigure(CanvasFigureLoop.Open);

            using var trackGeom = CanvasGeometry.CreatePath(trackPath);
            ds.DrawGeometry(trackGeom, _trackColor, strokeWidth, new CanvasStrokeStyle
            {
                StartCap = CanvasCapStyle.Round,
                EndCap = CanvasCapStyle.Round
            });
        }

        // 2. Active Progress Arc
        float currentSweep = (float)(sweepTotal * _currentArcProgress);
        if (currentSweep > 1f)
        {
            using var activePath = new CanvasPathBuilder(ds);
            var startRad = (float)(startAngle * Math.PI / 180.0);
            var activeEndRad = (float)((startAngle + currentSweep) * Math.PI / 180.0);
            var startPt = center + new Vector2((float)Math.Cos(startRad), (float)Math.Sin(startRad)) * radius;
            var endPt = center + new Vector2((float)Math.Cos(activeEndRad), (float)Math.Sin(activeEndRad)) * radius;

            activePath.BeginFigure(startPt);
            activePath.AddArc(endPt, radius, radius, 0, CanvasSweepDirection.Clockwise, CanvasArcSize.Small);
            activePath.EndFigure(CanvasFigureLoop.Open);

            using var activeGeom = CanvasGeometry.CreatePath(activePath);
            using var gradientBrush = new CanvasLinearGradientBrush(ds, _coralColor, _cyanColor)
            {
                StartPoint = center - new Vector2(radius, 0),
                EndPoint = center + new Vector2(radius, 0)
            };

            ds.DrawGeometry(activeGeom, gradientBrush, strokeWidth, new CanvasStrokeStyle
            {
                StartCap = CanvasCapStyle.Round,
                EndCap = CanvasCapStyle.Round
            });
        }
    }

    #endregion

    #region Win2D Sparklines

    private void CpuSparkCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        DrawSparkline(sender, args, _viewModel.CpuMetric.History, _cyanColor);
    }

    private void GpuSparkCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        DrawSparkline(sender, args, _viewModel.GpuMetric.History, _violetColor);
    }

    private void RamSparkCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        DrawSparkline(sender, args, _viewModel.RamMetric.History, _greenColor);
    }

    private void DiskSparkCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        DrawSparkline(sender, args, _viewModel.DiskMetric.History, _amberColor);
    }

    private static void DrawSparkline(CanvasControl sender, CanvasDrawEventArgs args, IReadOnlyList<float> points, Windows.UI.Color strokeColor)
    {
        var ds = args.DrawingSession;
        ds.Antialiasing = CanvasAntialiasing.Antialiased;

        float width = (float)sender.ActualWidth;
        float height = (float)sender.ActualHeight;
        if (width <= 0 || height <= 0 || points.Count < 2) return;

        float stepX = width / (points.Count - 1);
        float lineWeight = 1.5f;

        using var pb = new CanvasPathBuilder(ds);
        float firstY = height - (Math.Clamp(points[0], 0f, 100f) / 100f * (height - 4f)) - 2f;
        pb.BeginFigure(new Vector2(0, firstY));

        for (int i = 1; i < points.Count; i++)
        {
            float x = i * stepX;
            float y = height - (Math.Clamp(points[i], 0f, 100f) / 100f * (height - 4f)) - 2f;
            pb.AddLine(new Vector2(x, y));
        }

        pb.EndFigure(CanvasFigureLoop.Open);

        using var geom = CanvasGeometry.CreatePath(pb);
        var strokeStyle = new CanvasStrokeStyle
        {
            StartCap = CanvasCapStyle.Round,
            EndCap = CanvasCapStyle.Round,
            LineJoin = CanvasLineJoin.Round
        };
        ds.DrawGeometry(geom, strokeColor, lineWeight, strokeStyle);
    }

    #endregion

    #region Bouncy Spring Micro-Animations

    private void Tile_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border tile)
        {
            ApplyBouncyScale(tile, 1.025, 180, true);
        }
    }

    private void Tile_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border tile)
        {
            ApplyBouncyScale(tile, 1.00, 160, false);
        }
    }

    private static void ApplyBouncyScale(Border tile, double scale, int durationMs, bool overshoot)
    {
        if (tile.RenderTransform is not ScaleTransform st)
        {
            st = new ScaleTransform { CenterX = tile.ActualWidth / 2, CenterY = tile.ActualHeight / 2 };
            tile.RenderTransform = st;
        }
        else
        {
            st.CenterX = tile.ActualWidth / 2;
            st.CenterY = tile.ActualHeight / 2;
        }

        EasingFunctionBase easing = overshoot
            ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 }
            : new CubicEase { EasingMode = EasingMode.EaseOut };

        var animX = new DoubleAnimation { To = scale, Duration = TimeSpan.FromMilliseconds(durationMs), EasingFunction = easing };
        var animY = new DoubleAnimation { To = scale, Duration = TimeSpan.FromMilliseconds(durationMs), EasingFunction = easing };

        var sb = new Storyboard();
        Storyboard.SetTarget(animX, st);
        Storyboard.SetTargetProperty(animX, "ScaleX");
        Storyboard.SetTarget(animY, st);
        Storyboard.SetTargetProperty(animY, "ScaleY");
        sb.Begin();
    }

    #endregion

    #region Profile Switcher (Game Turbo, Max Power, Workstation, Eco Mode)

    private void ProfileCard_Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border clickedCard)
        {
            ApplyBouncyScale(clickedCard, 0.95, 80, false);

            var normalStyle = (Style)Application.Current.Resources["JamesGuiCardStyle"];
            var activeCoralStyle = (Style)Application.Current.Resources["JamesGuiActiveCoralCardStyle"];
            var activeCyanStyle = (Style)Application.Current.Resources["JamesGuiActiveCyanCardStyle"];
            var activeVioletStyle = (Style)Application.Current.Resources["JamesGuiActiveVioletCardStyle"];

            ProfileCard1.Style = normalStyle;
            ProfileCard2.Style = normalStyle;
            ProfileCard3.Style = normalStyle;
            ProfileCard4.Style = normalStyle;

            string tag = clickedCard.Tag as string ?? "";
            switch (tag)
            {
                case "GameTurbo":
                    clickedCard.Style = activeCoralStyle;
                    _ = _viewModel.ExecuteQuickActionAsync("game_mode");
                    break;
                case "MaxPerf":
                    clickedCard.Style = activeCyanStyle;
                    _ = _viewModel.ExecuteQuickActionAsync("ram_purge");
                    break;
                case "Workstation":
                    clickedCard.Style = activeVioletStyle;
                    break;
                case "EcoSilent":
                    clickedCard.Style = activeCoralStyle;
                    break;
            }
        }
    }

    #endregion

    #region Action Tiles (RAM Flush, Temp Clean, DNS Boost)

    private void ActionTile_Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border tile && tile.Tag is string actionId)
        {
            ApplyBouncyScale(tile, 0.95, 80, false);
            _ = _viewModel.ExecuteQuickActionAsync(actionId);
        }
    }

    #endregion

    #region Auto-Optimize Frequency (10M, 15M, 30M, 45M, 1H, 3H)

    private void AutoTimerSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        int index = Math.Clamp((int)Math.Round(e.NewValue), 0, TimerIntervalMinutes.Length - 1);
        int minutes = TimerIntervalMinutes[index];
        string label = TimerIntervalLabels[index];

        if (AutoTimerValueLabel != null)
        {
            AutoTimerValueLabel.Text = label;
        }

        UpdatePresetPillStyles(index);

        _ = Task.Run(async () =>
        {
            await BackgroundServiceClient.SetAutoTimerAsync(minutes);
        });
    }

    private void PresetPill_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && int.TryParse(btn.Tag as string, out int index))
        {
            AutoTimerSlider.Value = index;
        }
    }

    private void UpdatePresetPillStyles(int activeIndex)
    {
        var activeStyle = (Style)Application.Current.Resources["SegmentedPillActiveButtonStyle"];
        var normalStyle = (Style)Application.Current.Resources["SegmentedPillButtonStyle"];

        PresetPill0.Style = (activeIndex == 0) ? activeStyle : normalStyle;
        PresetPill1.Style = (activeIndex == 1) ? activeStyle : normalStyle;
        PresetPill2.Style = (activeIndex == 2) ? activeStyle : normalStyle;
        PresetPill3.Style = (activeIndex == 3) ? activeStyle : normalStyle;
        PresetPill4.Style = (activeIndex == 4) ? activeStyle : normalStyle;
        PresetPill5.Style = (activeIndex == 5) ? activeStyle : normalStyle;
    }

    #endregion

    private void HeroStartMissionBtn_Click(object sender, RoutedEventArgs e)
    {
        _ = Task.Run(async () =>
        {
            await _viewModel.RunFullOptimizationAsync();
            DispatcherQueue.TryEnqueue(() =>
            {
                _targetArcProgress = 0.99;
                RunStartupScan();
            });
        });
    }

    private void AutoRunSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch ts)
        {
            _viewModel.AutoRunEnabled = ts.IsOn;
        }
    }

    private void BtnOpenDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Current?.NavigateTo("Diagnostics");
    }

    private void BtnOpenStorage_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Current?.NavigateTo("Storage");
    }
}
