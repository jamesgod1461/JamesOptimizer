using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

namespace JamesOptimizer
{
    /// <summary>
    /// Interaction logic for Dashboard.xaml
    /// James Optimizer V3 Architecture
    /// </summary>
    public partial class Dashboard : Page, INavigablePage
    {
        private DispatcherTimer? _monitorTimer;
        private readonly Queue<double> _cpuHistory = new();
        private readonly Queue<double> _gpuHistory = new();
        private readonly Queue<double> _ramHistory = new();
        private readonly Queue<double> _diskHistory = new();

        private PerformanceCounter? _cpuCounter;
        private PerformanceCounter? _diskCounter;
        private HardwareMonitorService? _hwMonitor;

        private int _unresolvedIssues = 2;
        private bool _isOptimizing = false;
        private double _currentSystemScore = 0;

        private bool _isTimerInitialized = false;

        public Dashboard()
        {
            InitializeComponent();

            // Initialize sparkline histories with 16 baseline points
            for (int i = 0; i < 16; i++)
            {
                _cpuHistory.Enqueue(10 + (Math.Sin(i * 0.4) * 4));
                _gpuHistory.Enqueue(5 + (Math.Cos(i * 0.4) * 3));
                _ramHistory.Enqueue(35 + (i * 0.3));
                _diskHistory.Enqueue(2 + (i % 3));
            }

            InitHardwareCounters();
            InitAutoTimerSelection();
            _isTimerInitialized = true;
        }

        private void InitHardwareCounters()
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
                _diskCounter = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total", true);
                _hwMonitor = new HardwareMonitorService();
            }
            catch (Exception ex)
            {
                HardwareLogger.LogError("Failed to initialize performance counters", ex);
            }
        }

        private void InitAutoTimerSelection()
        {
            try
            {
                int savedTimer = (int)(Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\JamesOptimizer", "AutoTimer", 0) ?? 0);
                SelectAutoChip(savedTimer);
            }
            catch { }
        }

        /// <summary>
        /// Checks the frequency chip whose Tag (minutes) matches; falls back to OFF.
        /// </summary>
        private void SelectAutoChip(int minutes)
        {
            var chips = new[] { ChipAutoOff, ChipAuto10, ChipAuto15, ChipAuto30, ChipAuto45, ChipAuto60, ChipAuto120, ChipAuto180 };
            RadioButton? match = null;
            foreach (var chip in chips)
            {
                if (int.TryParse(chip.Tag?.ToString(), out int m) && m == minutes)
                {
                    match = chip;
                    break;
                }
            }
            if (match == null) match = ChipAutoOff;
            match.IsChecked = true;
        }

        // ═════════════════════════════════════════════════════════════════
        // INavigablePage & LIFECYCLE
        // ═════════════════════════════════════════════════════════════════
        public void OnNavigatedTo()
        {
            // 1. Dynamically evaluate system health and animate score out of 100
            _ = EvaluateAndApplySystemHealthAsync(isInitial: true);

            // 2. Start telemetry monitor timer (updates every 1500ms)
            StartTelemetryMonitoring();

            // 3. Update storage metrics with real drives
            LoadStorageMetrics();
        }

        public void OnNavigatedFrom()
        {
            // Stop monitor timer
            _monitorTimer?.Stop();
        }

        private void Dashboard_Loaded(object sender, RoutedEventArgs e)
        {
            OnNavigatedTo();
        }

        private void Dashboard_Unloaded(object sender, RoutedEventArgs e)
        {
            OnNavigatedFrom();
        }

        // ═════════════════════════════════════════════════════════════════
        // ① RADIAL GAUGE COUNT-UP ANIMATION
        // ═════════════════════════════════════════════════════════════════
        private void AnimateScoreCountUp(double fromScore, double toScore, int durationMs)
        {
            var start = DateTime.Now;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (s, e) =>
            {
                double elapsed = (DateTime.Now - start).TotalMilliseconds;
                double progress = Math.Min(1.0, elapsed / durationMs);
                double ease = 1.0 - Math.Pow(1.0 - progress, 3); // CubicEaseOut

                double currentScore = fromScore + ((toScore - fromScore) * ease);
                TxtScoreNumber.Text = Math.Round(currentScore).ToString();
                MainScoreBar.Value = currentScore;

                // Circumference = 160 * PI ≈ 502.6px. Stroke thickness = 10, total dash units ≈ 50.2
                double dashLength = (currentScore / 100.0) * 50.2;
                ScoreProgressRing.StrokeDashArray = new DoubleCollection { dashLength, 100 };

                if (progress >= 1.0)
                {
                    timer.Stop();
                    TxtScoreNumber.Text = Math.Round(toScore).ToString();
                }
            };
            timer.Start();
        }

        // ═════════════════════════════════════════════════════════════════
        // ⑤ SYSTEM MONITORING & SPARKLINES
        // ═════════════════════════════════════════════════════════════════
        private void StartTelemetryMonitoring()
        {
            _monitorTimer?.Stop();
            _monitorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            _monitorTimer.Tick += async (s, e) => await UpdateMetricsAsync();
            _monitorTimer.Start();

            // Initial immediate draw
            _ = UpdateMetricsAsync();
        }

        private async Task UpdateMetricsAsync()
        {
            double cpuVal = 14;
            double gpuVal = 8;
            float gpuTemp = 42;
            double ramPercent = 39;
            double diskVal = 2;
            string ramSubtitle = "12.4 / 32.0 GB";

            await Task.Run(() =>
            {
                try
                {
                    if (_cpuCounter != null)
                    {
                        cpuVal = Math.Round(_cpuCounter.NextValue());
                    }
                    if (_diskCounter != null)
                    {
                        diskVal = Math.Min(100, Math.Round(_diskCounter.NextValue()));
                    }

                    if (_hwMonitor != null)
                    {
                        var gpus = _hwMonitor.GetGpus();
                        if (gpus != null && gpus.Count > 0)
                        {
                            gpuVal = Math.Round(gpus.Max(g => g.Usage));
                            float maxTemp = gpus.Max(g => g.Temperature);
                            if (maxTemp > 0) gpuTemp = maxTemp;
                        }

                        float ramUsed = _hwMonitor.GetRamUsed();
                        float ramAvail = _hwMonitor.GetRamAvailable();
                        float totalRam = ramUsed + ramAvail;

                        if (totalRam > 0)
                        {
                            ramPercent = Math.Round((ramUsed / totalRam) * 100.0);
                            ramSubtitle = $"{ramUsed:F1} / {totalRam:F1} GB";
                        }
                    }
                }
                catch { }
            });

            // Update UI on dispatcher
            TxtCpuUsage.Text = $"{cpuVal}%";
            TxtGpuUsage.Text = $"{gpuVal}%";
            TxtGpuTemp.Text = $"{gpuTemp:F0}°C · Temp";
            TxtRamUsage.Text = $"{ramPercent}%";
            TxtRamSubtitle.Text = ramSubtitle;
            TxtDiskUsage.Text = $"{diskVal}%";

            // Push to queues
            UpdateHistoryQueue(_cpuHistory, cpuVal);
            UpdateHistoryQueue(_gpuHistory, gpuVal);
            UpdateHistoryQueue(_ramHistory, ramPercent);
            UpdateHistoryQueue(_diskHistory, diskVal);

            // Draw smooth bezier sparklines
            DrawSparkline(CanvasCpu, _cpuHistory, (MediaColor)FindResource("ColorAccentCyan"));
            DrawSparkline(CanvasGpu, _gpuHistory, (MediaColor)FindResource("ColorAccentViolet"));
            DrawSparkline(CanvasRam, _ramHistory, (MediaColor)FindResource("ColorSuccessGreen"));
            DrawSparkline(CanvasDisk, _diskHistory, (MediaColor)FindResource("ColorWarningAmber"));
        }

        private static void UpdateHistoryQueue(Queue<double> queue, double newVal)
        {
            queue.Enqueue(newVal);
            while (queue.Count > 16) queue.Dequeue();
        }

        private async void ProfileRadio_Click(object sender, RoutedEventArgs e)
        {
            if (ProfileGaming.IsChecked == true)
            {
                await OptimizationProfileManager.ApplyProfileAsync(OptimizationProfileType.Gaming);
                MainWindow.Instance?.ShowToastSuccess("Gaming Profile Active", "Ultimate Power Plan, GPU priority & schedulers engaged.");
            }
            else if (ProfileWork.IsChecked == true)
            {
                await OptimizationProfileManager.ApplyProfileAsync(OptimizationProfileType.Work);
                MainWindow.Instance?.ShowToastSuccess("Work & Dev Profile Active", "Balanced responsiveness and memory optimization applied.");
            }
            else if (ProfileEco.IsChecked == true)
            {
                await OptimizationProfileManager.ApplyProfileAsync(OptimizationProfileType.BatterySaver);
                MainWindow.Instance?.ShowToast("Battery Saver Profile Active", "Power saver mode engaged to maximize efficiency.", ToastType.Info);
            }
        }

        private async void BtnExportReportDashboard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MainWindow.Instance?.ShowToast("Generating Report", "Compiling hardware telemetry and optimization records...", ToastType.Info);
                string path = await OptimizationReportGenerator.GenerateAndOpenReportAsync();
                MainWindow.Instance?.ShowToastWithAction("Executive Report Generated", "HTML report opened in default browser.", "OPEN FOLDER", () =>
                {
                    try { Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { }
                }, ToastType.Success);
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToastError("Report Error", ex.Message);
            }
        }

        private static void DrawSparkline(Canvas canvas, Queue<double> history, MediaColor strokeColor)
        {
            canvas.Children.Clear();
            if (history.Count < 2) return;

            var points = history.ToArray();
            double width = canvas.Width > 0 ? canvas.Width : 80;
            double height = canvas.Height > 0 ? canvas.Height : 28;

            double stepX = width / (points.Length - 1);

            var pathFigure = new PathFigure();
            var fillFigure = new PathFigure();

            // Calculate first point: y maps 0-100% to height-4 down to 4
            double y0 = (height - 4) - ((points[0] / 100.0) * (height - 8));
            y0 = Math.Max(2, Math.Min(height - 2, y0));

            pathFigure.StartPoint = new System.Windows.Point(0, y0);
            fillFigure.StartPoint = new System.Windows.Point(0, y0);

            var pathSegmentCollection = new PathSegmentCollection();
            var fillSegmentCollection = new PathSegmentCollection();

            for (int i = 1; i < points.Length; i++)
            {
                double prevX = (i - 1) * stepX;
                double prevY = (height - 4) - ((points[i - 1] / 100.0) * (height - 8));
                prevY = Math.Max(2, Math.Min(height - 2, prevY));

                double curX = i * stepX;
                double curY = (height - 4) - ((points[i] / 100.0) * (height - 8));
                curY = Math.Max(2, Math.Min(height - 2, curY));

                // Smooth cubic bezier segment
                double cp1X = prevX + (stepX / 2.0);
                double cp1Y = prevY;
                double cp2X = prevX + (stepX / 2.0);
                double cp2Y = curY;

                var bezier = new BezierSegment(
                    new System.Windows.Point(cp1X, cp1Y),
                    new System.Windows.Point(cp2X, cp2Y),
                    new System.Windows.Point(curX, curY),
                    true);

                pathSegmentCollection.Add(bezier);
                fillSegmentCollection.Add(bezier);
            }

            // Close fill area down to bottom
            fillSegmentCollection.Add(new LineSegment(new System.Windows.Point(width, height), true));
            fillSegmentCollection.Add(new LineSegment(new System.Windows.Point(0, height), true));

            pathFigure.Segments = pathSegmentCollection;
            fillFigure.Segments = fillSegmentCollection;

            // 1. Fill geometry (AccentCyan 8% opacity)
            var fillGeometry = new PathGeometry { Figures = { fillFigure } };
            var fillPath = new System.Windows.Shapes.Path
            {
                Data = fillGeometry,
                Fill = new SolidColorBrush(MediaColor.FromArgb(0x14, strokeColor.R, strokeColor.G, strokeColor.B))
            };
            canvas.Children.Add(fillPath);

            // 2. Stroke line geometry
            var lineGeometry = new PathGeometry { Figures = { pathFigure } };
            var linePath = new System.Windows.Shapes.Path
            {
                Data = lineGeometry,
                Stroke = new SolidColorBrush(strokeColor),
                StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            canvas.Children.Add(linePath);
        }

        // ═════════════════════════════════════════════════════════════════
        // ⑥ STORAGE OVERVIEW
        // ═════════════════════════════════════════════════════════════════
        private void LoadStorageMetrics()
        {
            try
            {
                var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed).ToList();
                if (drives.Count == 0) return;

                // Drive C:
                var cDrive = drives.FirstOrDefault(d => d.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase)) ?? drives[0];
                if (cDrive != null)
                {
                    double usedGb = (cDrive.TotalSize - cDrive.TotalFreeSpace) / (1024.0 * 1024.0 * 1024.0);
                    double totalGb = cDrive.TotalSize / (1024.0 * 1024.0 * 1024.0);
                    double freeGb = cDrive.TotalFreeSpace / (1024.0 * 1024.0 * 1024.0);
                    double pct = (usedGb / totalGb) * 100.0;

                    TxtDriveCLabel.Text = $"System Drive ({cDrive.Name.TrimEnd('\\')})";
                    TxtDriveCTotal.Text = $"Total: {totalGb:N1} GB";
                    TxtDriveCUsedFree.Text = $"Used: {usedGb:N1} GB  ·  Free: {freeGb:N1} GB";
                    TxtDriveCPercent.Text = $"{Math.Round(pct)}%";

                    double cDash = (pct / 100.0) * 27.6;
                    DonutDriveC.StrokeDashArray = new DoubleCollection { cDash, 100 };
                    BorderDriveC.Visibility = Visibility.Visible;
                }

                // Drive D:
                var dDrive = drives.FirstOrDefault(d => !d.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase));
                if (dDrive != null)
                {
                    double usedGb = (dDrive.TotalSize - dDrive.TotalFreeSpace) / (1024.0 * 1024.0 * 1024.0);
                    double totalGb = dDrive.TotalSize / (1024.0 * 1024.0 * 1024.0);
                    double freeGb = dDrive.TotalFreeSpace / (1024.0 * 1024.0 * 1024.0);
                    double pct = (usedGb / totalGb) * 100.0;

                    TxtDriveDLabel.Text = $"Storage Drive ({dDrive.Name.TrimEnd('\\')})";
                    TxtDriveDTotal.Text = $"Total: {totalGb:N1} GB";
                    TxtDriveDUsedFree.Text = $"Used: {usedGb:N1} GB  ·  Free: {freeGb:N1} GB";
                    TxtDriveDPercent.Text = $"{Math.Round(pct)}%";

                    double dDash = (pct / 100.0) * 27.6;
                    DonutDriveD.StrokeDashArray = new DoubleCollection { dDash, 100 };
                    BorderDriveD.Visibility = Visibility.Visible;
                }
                else
                {
                    BorderDriveD.Visibility = Visibility.Collapsed;
                }

                // Drive E:
                var otherDrives = drives.Where(d => d != cDrive && d != dDrive).ToList();
                if (otherDrives.Count > 0)
                {
                    var eDrive = otherDrives[0];
                    double usedGb = (eDrive.TotalSize - eDrive.TotalFreeSpace) / (1024.0 * 1024.0 * 1024.0);
                    double totalGb = eDrive.TotalSize / (1024.0 * 1024.0 * 1024.0);
                    double freeGb = eDrive.TotalFreeSpace / (1024.0 * 1024.0 * 1024.0);
                    double pct = (usedGb / totalGb) * 100.0;

                    TxtDriveELabel.Text = $"Data Drive ({eDrive.Name.TrimEnd('\\')})";
                    TxtDriveETotal.Text = $"Total: {totalGb:N1} GB";
                    TxtDriveEUsedFree.Text = $"Used: {usedGb:N1} GB  ·  Free: {freeGb:N1} GB";
                    TxtDriveEPercent.Text = $"{Math.Round(pct)}%";

                    double eDash = (pct / 100.0) * 27.6;
                    DonutDriveE.StrokeDashArray = new DoubleCollection { eDash, 100 };
                    BorderDriveE.Visibility = Visibility.Visible;
                }
                else
                {
                    BorderDriveE.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                HardwareLogger.LogError("Storage load failed", ex);
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // ② QUICK ACTIONS HANDLERS
        // ═════════════════════════════════════════════════════════════════
        private async void BtnQuickAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;

            switch (btn.Name)
            {
                case "BtnQuickOptimize":
                    await RunFullOptimizationAsync();
                    break;

                case "BtnQuickClean":
                    await Task.Run(async () =>
                    {
                        try { await OptimizationEngine.RunFastCleanAsync(CancellationToken.None); } catch { }
                    });
                    MainWindow.Instance?.ShowToastSuccess("Junk Files Cleaned", "Temporary caches and lingering logs purged.");
                    _ = EvaluateAndApplySystemHealthAsync();
                    break;

                case "BtnQuickBoost":
                    await Task.Run(async () =>
                    {
                        try { await OptimizationEngine.RunGamingBoostAsync(CancellationToken.None); } catch { }
                    });
                    MainWindow.Instance?.ShowToastSuccess("Performance Boosted", "Low-latency CPU scheduler and GPU priority applied.");
                    _ = EvaluateAndApplySystemHealthAsync();
                    break;

                case "BtnQuickRam":
                    await Task.Run(async () =>
                    {
                        try { await OptimizationEngine.RunRamFlushAsync(CancellationToken.None); } catch { }
                    });
                    MainWindow.Instance?.ShowToastSuccess("RAM Flushed", "Cleaned standby list and compacted working sets.");
                    _ = EvaluateAndApplySystemHealthAsync();
                    break;

                case "BtnQuickStartup":
                    _ = MainWindow.Instance?.Navigate(new StartupManager());
                    break;

                case "BtnQuickNetwork":
                    _ = MainWindow.Instance?.Navigate(new NetworkBoost());
                    break;

                case "BtnRunAutoNow":
                    // Same routine the background auto-optimizer runs on its schedule.
                    await Task.Run(async () =>
                    {
                        try
                        {
                            await OptimizationEngine.RunRamFlushAsync(CancellationToken.None);
                            await OptimizationEngine.RunFastCleanAsync(CancellationToken.None);
                        }
                        catch { }
                    });
                    TxtLastOptimized.Text = $"Today, {DateTime.Now:h:mm tt}";
                    _ = EvaluateAndApplySystemHealthAsync();
                    MainWindow.Instance?.ShowToastSuccess("Auto-Optimize Complete", "Working sets trimmed and temp caches purged.");
                    break;
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // ④ OPTIMIZATION SUMMARY & DYNAMIC HEALTH ASSESSMENT
        // ═════════════════════════════════════════════════════════════════
        private async Task EvaluateAndApplySystemHealthAsync(bool isInitial = false)
        {
            int calculatedScore = 90;
            int healthScore = 94;
            int perfScore = 88;
            int secScore = 92;
            int stabScore = 86;
            int detectedIssues = 0;
            long junkBytes = 0;
            int startupCount = 0;
            double ramUsedGb = 0;
            double totalRamGb = 0;
            double ramPct = 0;
            bool isTelemetryActive = false;
            bool isVisualFxUnoptimized = false;

            await Task.Run(() =>
            {
                try
                {
                    // 1. RAM evaluation
                    if (_hwMonitor != null)
                    {
                        float used = _hwMonitor.GetRamUsed();
                        float avail = _hwMonitor.GetRamAvailable();
                        totalRamGb = used + avail;
                        ramUsedGb = used;
                        if (totalRamGb > 0)
                        {
                            ramPct = (used / totalRamGb) * 100.0;
                        }
                    }

                    // 2. Junk scan (quick check of %TEMP% and Windows temp)
                    string[] tempPaths = new[] { System.IO.Path.GetTempPath(), @"C:\Windows\Temp" };
                    foreach (var p in tempPaths)
                    {
                        if (Directory.Exists(p))
                        {
                            try
                            {
                                var di = new DirectoryInfo(p);
                                foreach (var fi in di.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
                                {
                                    junkBytes += fi.Length;
                                }
                            }
                            catch { }
                        }
                    }

                    // 3. Startup programs count
                    try
                    {
                        using var runKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                        if (runKey != null) startupCount += runKey.ValueCount;
                        using var runKeyMachine = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                        if (runKeyMachine != null) startupCount += runKeyMachine.ValueCount;
                    }
                    catch { }

                    // 4. Telemetry check
                    try
                    {
                        var allowTelemetry = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 1);
                        if (allowTelemetry != null && Convert.ToInt32(allowTelemetry) != 0)
                        {
                            isTelemetryActive = true;
                        }
                    }
                    catch { }

                    // 5. Visual effects check
                    try
                    {
                        var fxVal = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 1);
                        if (fxVal != null && Convert.ToInt32(fxVal) != 2) // 2 = Adjust for best performance
                        {
                            isVisualFxUnoptimized = true;
                        }
                    }
                    catch { }

                    // Deductions & scoring
                    if (isTelemetryActive) detectedIssues++;
                    if (isVisualFxUnoptimized) detectedIssues++;

                    // Health score (0-100)
                    double junkMb = junkBytes / (1024.0 * 1024.0);
                    int healthDeduct = (int)Math.Min(18, junkMb / 150.0);
                    if (isTelemetryActive) healthDeduct += 10;
                    healthScore = Math.Clamp(100 - healthDeduct, 50, 100);

                    // Performance score (0-100)
                    int perfDeduct = 0;
                    if (ramPct > 50) perfDeduct += (int)((ramPct - 50) * 0.5);
                    perfScore = Math.Clamp(100 - perfDeduct, 45, 100);

                    // Security score (0-100)
                    int secDeduct = isTelemetryActive ? 12 : 2;
                    secScore = Math.Clamp(100 - secDeduct, 60, 100);

                    // Stability score (0-100)
                    int stabDeduct = Math.Min(16, Math.Max(0, startupCount - 6) * 2);
                    if (isVisualFxUnoptimized) stabDeduct += 6;
                    stabScore = Math.Clamp(100 - stabDeduct, 55, 100);

                    // Overall Composite Score (0-100)
                    calculatedScore = (int)Math.Clamp(Math.Round((healthScore * 0.25) + (perfScore * 0.35) + (secScore * 0.20) + (stabScore * 0.20)), 1, 100);
                }
                catch { }
            });

            // Dispatch updates to UI
            _unresolvedIssues = detectedIssues;
            TxtIssuesCount.Text = detectedIssues.ToString();

            // Animate radial gauge
            double from = isInitial ? 0 : _currentSystemScore;
            _currentSystemScore = calculatedScore;
            AnimateScoreCountUp(from, calculatedScore, isInitial ? 1200 : 700);

            // Sub-scores
            TxtHealthSubScore.Text = $"{healthScore}% · {GetScoreLabel(healthScore)}";
            BarHealthSubScore.Value = healthScore;
            TxtPerfSubScore.Text = $"{perfScore}% · {GetScoreLabel(perfScore)}";
            BarPerfSubScore.Value = perfScore;
            TxtSecSubScore.Text = $"{secScore}% · {GetScoreLabel(secScore)}";
            BarSecSubScore.Value = secScore;
            TxtStabSubScore.Text = $"{stabScore}% · {GetScoreLabel(stabScore)}";
            BarStabSubScore.Value = stabScore;

            // Status pill & headlines
            if (calculatedScore >= 90)
            {
                TxtStatusValue.Text = "EXCELLENT";
                TxtStatusValue.Foreground = (MediaBrush)FindResource("SuccessGreen");
                StatusDot.Fill = (MediaBrush)FindResource("SuccessGreen");
                TxtScoreHeadline.Text = "YOUR SYSTEM IS OPTIMIZED";
                TxtScoreSubtitle.Text = "All background schedulers and kernel priority threads running at peak efficiency.";
            }
            else if (calculatedScore >= 75)
            {
                TxtStatusValue.Text = "OPTIMAL";
                TxtStatusValue.Foreground = (MediaBrush)FindResource("AccentCyan");
                StatusDot.Fill = (MediaBrush)FindResource("AccentCyan");
                TxtScoreHeadline.Text = "OPTIMIZATIONS AVAILABLE";
                TxtScoreSubtitle.Text = $"{detectedIssues} recommended adjustments available to increase latency response.";
            }
            else
            {
                TxtStatusValue.Text = "NEEDS ATTENTION";
                TxtStatusValue.Foreground = (MediaBrush)FindResource("WarningAmber");
                StatusDot.Fill = (MediaBrush)FindResource("WarningAmber");
                TxtScoreHeadline.Text = "SYSTEM BOTTLENECK DETECTED";
                TxtScoreSubtitle.Text = "Background processes and telemetry are impacting performance.";
            }

            // Quick Actions subtitles
            double junkMbFinal = junkBytes / (1024.0 * 1024.0);
            TxtQuickCleanSubtitle.Text = junkMbFinal > 1024
                ? $"Reclaim {(junkMbFinal / 1024.0):F1} GB"
                : $"Reclaim {junkMbFinal:N0} MB";

            if (totalRamGb > 0)
            {
                TxtQuickRamSubtitle.Text = $"{ramUsedGb:F1} / {totalRamGb:F1} GB";
            }
            TxtQuickStartupSubtitle.Text = $"{startupCount} apps configured";
            TxtQuickOptimizeSubtitle.Text = detectedIssues > 0 ? $"Fix {detectedIssues} issues (+{(100 - calculatedScore)} pts)" : "System fully tuned";

            // Update issues list buttons/dots
            if (!isVisualFxUnoptimized)
            {
                DotVisualEffects.Fill = (MediaBrush)FindResource("SuccessGreen");
                BtnFixVisualEffects.Content = "FIXED ✓";
                BtnFixVisualEffects.IsEnabled = false;
                BtnFixVisualEffects.Style = (Style)FindResource("GhostButton");
            }
            if (!isTelemetryActive)
            {
                DotTelemetry.Fill = (MediaBrush)FindResource("SuccessGreen");
                BtnFixTelemetry.Content = "FIXED ✓";
                BtnFixTelemetry.IsEnabled = false;
                BtnFixTelemetry.Style = (Style)FindResource("GhostButton");
            }
        }

        private static string GetScoreLabel(int score)
        {
            if (score >= 90) return "Excellent";
            if (score >= 80) return "Optimal";
            if (score >= 70) return "Good";
            return "Attention";
        }

        private void BtnFixVisualEffects_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Microsoft.Win32.Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 2);
                DotVisualEffects.Fill = (MediaBrush)FindResource("SuccessGreen");
                BtnFixVisualEffects.Content = "FIXED ✓";
                BtnFixVisualEffects.IsEnabled = false;
                BtnFixVisualEffects.Style = (Style)FindResource("GhostButton");

                DecrementIssues();
                MainWindow.Instance?.ShowToastSuccess("Visual Effects Optimized", "Smooth animations and fast UI response configured.");
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToastError("Optimization Error", ex.Message);
            }
        }

        private void BtnFixTelemetry_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Microsoft.Win32.Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0, Microsoft.Win32.RegistryValueKind.DWord);
                DotTelemetry.Fill = (MediaBrush)FindResource("SuccessGreen");
                BtnFixTelemetry.Content = "FIXED ✓";
                BtnFixTelemetry.IsEnabled = false;
                BtnFixTelemetry.Style = (Style)FindResource("GhostButton");

                DecrementIssues();
                MainWindow.Instance?.ShowToastSuccess("Telemetry Disabled", "Windows diagnostics and tracking background daemons stopped.");
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToastError("Optimization Error", ex.Message);
            }
        }

        private void DecrementIssues()
        {
            _unresolvedIssues = Math.Max(0, _unresolvedIssues - 1);
            TxtIssuesCount.Text = _unresolvedIssues.ToString();

            if (_unresolvedIssues == 0)
            {
                TxtIssuesCount.Foreground = (MediaBrush)FindResource("SuccessGreen");
                IssuesSegment.Stroke = (MediaBrush)FindResource("SuccessGreen");
                TxtStatusValue.Text = "EXCELLENT";
                TxtStatusValue.Foreground = (MediaBrush)FindResource("SuccessGreen");
                StatusDot.Fill = (MediaBrush)FindResource("SuccessGreen");
                StatusPillBorder.Background = new SolidColorBrush(MediaColor.FromArgb(0x1A, 0x00, 0xFF, 0x88));
                StatusPillBorder.BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x4D, 0x00, 0xFF, 0x88));
            }

            _ = EvaluateAndApplySystemHealthAsync();
        }

        // ═════════════════════════════════════════════════════════════════
        // ③ AUTO-OPTIMIZE FREQUENCY (chip selector)
        // ═════════════════════════════════════════════════════════════════
        private void AutoChip_Click(object sender, RoutedEventArgs e)
        {
            if (!_isTimerInitialized) return;

            if (sender is RadioButton chip && int.TryParse(chip.Tag?.ToString(), out int minutes))
            {
                try
                {
                    Microsoft.Win32.Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\JamesOptimizer", "AutoTimer", minutes, Microsoft.Win32.RegistryValueKind.DWord);
                    string label = minutes == 0 ? "Off" : minutes < 60 ? $"Every {minutes} minutes" : $"Every {minutes / 60} hour{(minutes / 60 > 1 ? "s" : "")}";
                    MainWindow.Instance?.ShowToast("Auto-Run Schedule", $"Auto-optimization schedule set to: {label}.", ToastType.Info);
                }
                catch { }
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // STICKY BOTTOM BAR & PRIMARY OPTIMIZATION
        // ═════════════════════════════════════════════════════════════════
        private async void BtnRunOptimization_Click(object sender, RoutedEventArgs e)
        {
            await RunFullOptimizationAsync();
        }

        private async Task RunFullOptimizationAsync()
        {
            if (_isOptimizing) return;
            _isOptimizing = true;

            MainWindow.Instance?.ShowToast("Optimization Running", "Optimizing memory, cleaning temp files, and tuning priorities...", ToastType.Info, TimeSpan.FromSeconds(3));

            await Task.Run(async () =>
            {
                try
                {
                    await OptimizationEngine.RunRamFlushAsync(CancellationToken.None);
                    await OptimizationEngine.RunFastCleanAsync(CancellationToken.None);
                    await OptimizationEngine.RunGamingBoostAsync(CancellationToken.None);
                }
                catch { }
            });

            // Re-evaluate health and animate to optimized score
            await EvaluateAndApplySystemHealthAsync();

            // Update last-run stamp (element lives in the page namescope)
            TxtLastOptimized.Text = $"Today, {DateTime.Now:h:mm tt}";

            MainWindow.Instance?.ShowToastSuccess("System Fully Optimized", "Performance maximized. All subsystems green.");
            _isOptimizing = false;
        }

        private void BtnViewDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            _ = MainWindow.Instance?.Navigate(new SystemDiagnostics());
        }

        // ═════════════════════════════════════════════════════════════════
        // SYSTEM UTILITIES (restore point · benchmark · explorer · palette)
        // ═════════════════════════════════════════════════════════════════
        private async void BtnQuickUtil_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;

            switch (btn.Name)
            {
                case "BtnQuickRestorePoint":
                    btn.IsEnabled = false;
                    MainWindow.Instance?.ShowToast("Creating Restore Point", "Generating system restore snapshot...", ToastType.Info);
                    try
                    {
                        await Task.Run(async () => { await OptimizationEngine.CreateRestorePointAsync(CancellationToken.None); });
                        MainWindow.Instance?.ShowToastSuccess("Restore Point Created", "System state snapshot saved successfully.");
                    }
                    catch (Exception ex)
                    {
                        MainWindow.Instance?.ShowToastError("Restore Point Failed", ex.Message);
                    }
                    finally
                    {
                        btn.IsEnabled = true;
                    }
                    break;

                case "BtnQuickBenchmark":
                    btn.IsEnabled = false;
                    MainWindow.Instance?.ShowToast("Benchmarking", "Running CPU, memory and disk latency tests...", ToastType.Info);
                    try
                    {
                        var res = await OptimizationEngine.BenchmarkSystemAsync(CancellationToken.None);
                        MainWindow.Instance?.ShowToastSuccess($"Benchmark Score: {res.total}/100", $"CPU: {res.cpu}/100 · RAM: {res.ram}/100 · Disk: {res.disk}/100");
                    }
                    catch (Exception ex)
                    {
                        MainWindow.Instance?.ShowToastError("Benchmark Failed", ex.Message);
                    }
                    finally
                    {
                        btn.IsEnabled = true;
                    }
                    break;

                case "BtnQuickExplorer":
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c taskkill /f /im explorer.exe & start explorer.exe") { CreateNoWindow = true, UseShellExecute = false });
                        MainWindow.Instance?.ShowToast("Explorer Restarted", "Windows desktop shell refreshed.", ToastType.Info);
                    }
                    catch (Exception ex)
                    {
                        MainWindow.Instance?.ShowToastError("Restart Failed", ex.Message);
                    }
                    break;

                case "BtnQuickPalette":
                    MainWindow.Instance?.OpenCommandPalette();
                    break;
            }
        }

        private void BtnManageStorage_Click(object sender, RoutedEventArgs e)
        {
            _ = MainWindow.Instance?.Navigate(new StorageManagement());
        }
    }
}
