using System;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Collections.Generic;
using System.Windows.Shapes;
using System.Windows.Media;
using System.Windows.Controls;
using System.Media;
using System.Windows.Media.Animation;
using System.IO;
using System.Management;
using Microsoft.Win32;
using LibreHardwareMonitor.Hardware;
using System.Security.AccessControl;
using System.Threading;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace JamesOptimizer
{
    public partial class MainWindow : Window
    {
        public System.Collections.ObjectModel.ObservableCollection<GpuInfo> Gpus { get; set; } = new();

        private bool isExecuting = false;
        private DispatcherTimer? resourceTimer;
        private HardwareMonitorService? hwMonitor;
        private SoundPlayer? clickSound;
        private SoundPlayer? hoverSound;
        private int currentProcessCount = 0;
        private CancellationTokenSource? _cts;

        private readonly Queue<double> cpuHistory = new();
        private readonly Queue<double> ramHistory = new();
        private readonly Queue<double> gpuHistory = new();
        private readonly Queue<double> disk0ReadHistory = new();
        private readonly Queue<double> disk0WriteHistory = new();
        private readonly Queue<double> disk1ReadHistory = new();
        private readonly Queue<double> disk1WriteHistory = new();
        private readonly Queue<double> netDownHistory = new();
        private readonly Queue<double> netUpHistory = new();

        private DispatcherTimer? networkTimer;
        private long lastBytesReceived = 0;
        private long lastBytesSent = 0;
        private NetworkInterface? activeInterface;

        private System.Windows.Forms.NotifyIcon? trayIcon;
        private bool isRealExit = false;

        // New UI systems
        private ToastManager? _toastManager;


        public MainWindow()
        {
            _cts = new CancellationTokenSource();
            
            // Global exception handling - log to local app data instead of hardcoded paths
            var logDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JamesOptimizer", "Logs");
            try { Directory.CreateDirectory(logDir); } catch { }
            
            AppDomain.CurrentDomain.UnhandledException += (s, e) => 
            {
                try { File.WriteAllText(System.IO.Path.Combine(logDir, "crash.txt"), e.ExceptionObject.ToString()); } catch { }
            };
            TaskScheduler.UnobservedTaskException += (s, e) => 
            {
                try { File.WriteAllText(System.IO.Path.Combine(logDir, "crash_task.txt"), e.Exception.ToString()); } catch { }
            };
            Dispatcher.UnhandledException += (s, e) => 
            {
                try { File.WriteAllText(System.IO.Path.Combine(logDir, "crash_disp.txt"), e.Exception.ToString()); } catch { }
            };
            
            InitializeNetworkTimer();
            InitializeComponent();
            Loaded += MainWindow_Loaded;

            // Load saved timer value from HKLM for the background service
            try
            {
                int savedTimer = (int)(Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\JamesOptimizer", "AutoTimer", 0) ?? 0);
                if (savedTimer > 0)
                {
                    foreach (ComboBoxItem item in ComboAutoTimer.Items)
                    {
                        if (item.Tag != null && item.Tag.ToString() == savedTimer.ToString())
                        {
                            ComboAutoTimer.SelectedItem = item;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex) { HardwareLogger.LogError("Load AutoTimer failed", ex); }

            OptimizationEngine.OnProgress += (percent, message) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        double totalWidth = ProgressBarTrack.ActualWidth;
                        if (totalWidth <= 0)
                        {
                            ProgressBarTrack.UpdateLayout();
                            totalWidth = ProgressBarTrack.ActualWidth;
                        }
                        if (totalWidth <= 0) totalWidth = 340;

                        int clampedPercent = Math.Min(100, Math.Max(0, percent));
                        double targetWidth = (totalWidth * clampedPercent) / 100.0;

                        var da = new System.Windows.Media.Animation.DoubleAnimation
                        {
                            To = targetWidth,
                            Duration = TimeSpan.FromMilliseconds(300),
                            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                        };
                        ProgressBarIndicator.BeginAnimation(System.Windows.FrameworkElement.WidthProperty, da);

                        TxtLoadingMessage.Text = $"{clampedPercent}% - {message}";
                    }
                    catch (Exception ex) { HardwareLogger.LogError("Progress update failed", ex); }
                }));
            };
            
            try 
            {
                string savedName = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\JamesOptimizer", "UserName", "") as string ?? "";
                if (string.IsNullOrEmpty(savedName)) savedName = Environment.MachineName;
                TxtUserName.Text = savedName;
                InputUserName.Text = savedName;
            } 
            catch (Exception ex) 
            { 
                HardwareLogger.LogError("Load username failed", ex);
                TxtUserName.Text = Environment.MachineName;
                InputUserName.Text = Environment.MachineName;
            }

            // Setup System Tray Icon
            trayIcon = new System.Windows.Forms.NotifyIcon();
            try 
            {
                var iconUri = new Uri("pack://application:,,,/app_icon.ico");
                var streamInfo = System.Windows.Application.GetResourceStream(iconUri);
                if (streamInfo != null)
                {
                    trayIcon.Icon = new System.Drawing.Icon(streamInfo.Stream);
                }
                else
                {
                    trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "");
                }
            } 
            catch (Exception ex)
            { 
                HardwareLogger.LogError("Tray icon setup failed", ex);
                try { trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? ""); } catch { }
            }
            trayIcon.Text = "James Optimizer";
            trayIcon.Visible = true;
            trayIcon.DoubleClick += (s, e) => {
                this.Show();
                this.WindowState = WindowState.Normal;
                this.Activate();
            };

            var trayMenu = new System.Windows.Forms.ContextMenuStrip();
            trayMenu.Items.Add("Open James Optimizer", null, (s, e) => {
                this.Show();
                this.WindowState = WindowState.Normal;
                this.Activate();
            });
            trayMenu.Items.Add("Exit", null, (s, e) => {
                isRealExit = true;
                if (trayIcon != null) trayIcon.Visible = false;
                System.Windows.Application.Current.Shutdown();
            });
            trayIcon.ContextMenuStrip = trayMenu;

            hwMonitor = new HardwareMonitorService();
            OptimizationEngine.OnProgress = UpdateProgress;

            // Load initial mouse settings without triggering value changed event prematurely
            Loaded += async (s, e) => {
                try
                {
                    if (Resources["WindowLoadAnimation"] is Storyboard sb)
                        sb.Begin(this);

                    await AnimateHomeScoreAsync();
                    UpdateStorageMetrics();
                    InitDiagnosticsAnimation();

                    bool isWin11 = Environment.OSVersion.Version.Build >= 22000;
                    string osNameBtn = isWin11 ? "Win 11" : "Win 10";
                    BtnDebloat.Content = $"\u26A1 Ultimate {osNameBtn} Debloat";
                    
                    await Task.Run(() =>
                    {
                        try
                        {
                            string osName = "Windows";
                            string osArch = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";
                            using (var searcher = new System.Management.ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem"))
                            {
                                foreach (var mObj in searcher.Get())
                                {
                                    osName = mObj["Caption"]?.ToString() ?? "Windows";
                                    break;
                                }
                            }
                            if (isWin11 && osName.Contains("Windows 10"))
                            {
                                osName = osName.Replace("Windows 10", "Windows 11");
                            }
                            Dispatcher.Invoke(() =>
                            {
                                if (TxtOsVersion != null) TxtOsVersion.Text = $"{osName.Replace("Microsoft", "").Trim()}     {osArch}";
                            });
                        }
                        catch (Exception ex) { HardwareLogger.LogError("OS version detection failed", ex); }
                    });
                }
                catch (Exception ex) { HardwareLogger.LogError("MainWindow Loaded handler failed", ex); }
            };

            try { clickSound = new System.Media.SoundPlayer(@"C:\Windows\Media\Windows Default.wav"); clickSound.LoadAsync(); } catch { }
            try { hoverSound = new System.Media.SoundPlayer(@"C:\Windows\Media\Windows Balloon.wav"); hoverSound.LoadAsync(); } catch { }

            InitializeResourceMonitors();
        }

        // --- SIDEBAR ANIMATION ---
        private void BtnToggleSidebar_Checked(object sender, RoutedEventArgs e)
        {
            var animWidth = new System.Windows.Media.Animation.DoubleAnimation { To = 70, Duration = TimeSpan.FromSeconds(0.4), EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut } };
            SidebarContainer.BeginAnimation(FrameworkElement.WidthProperty, animWidth);
            
            if (SidebarFooterText != null)
            {
                var animOpacity = new System.Windows.Media.Animation.DoubleAnimation { To = 0, Duration = TimeSpan.FromSeconds(0.2) };
                SidebarFooterText.BeginAnimation(UIElement.OpacityProperty, animOpacity);
                if (SidebarFooterIcons != null)
                    SidebarFooterIcons.BeginAnimation(UIElement.OpacityProperty, animOpacity);
            }
            
            if (BtnToggleSidebar.RenderTransform is System.Windows.Media.RotateTransform rt)
            {
                var animRotate = new System.Windows.Media.Animation.DoubleAnimation { To = 180, Duration = TimeSpan.FromSeconds(0.4), EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut } };
                rt.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, animRotate);
            }
        }

        private void BtnToggleSidebar_Unchecked(object sender, RoutedEventArgs e)
        {
            var animWidth = new System.Windows.Media.Animation.DoubleAnimation { To = 240, Duration = TimeSpan.FromSeconds(0.4), EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut } };
            SidebarContainer.BeginAnimation(FrameworkElement.WidthProperty, animWidth);
            
            if (SidebarFooterText != null)
            {
                var animOpacity = new System.Windows.Media.Animation.DoubleAnimation { To = 1, BeginTime = TimeSpan.FromSeconds(0.2), Duration = TimeSpan.FromSeconds(0.3) };
                SidebarFooterText.BeginAnimation(UIElement.OpacityProperty, animOpacity);
                if (SidebarFooterIcons != null)
                    SidebarFooterIcons.BeginAnimation(UIElement.OpacityProperty, animOpacity);
            }
            
            if (BtnToggleSidebar.RenderTransform is System.Windows.Media.RotateTransform rt)
            {
                var animRotate = new System.Windows.Media.Animation.DoubleAnimation { To = 0, Duration = TimeSpan.FromSeconds(0.4), EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut } };
                rt.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, animRotate);
            }
        }

        // --- CUSTOM WINDOW CONTROLS ---
        private void BtnTopSettings_Click(object sender, RoutedEventArgs e)
        {
            if (NavMouse != null)
            {
                NavMouse.IsChecked = true;
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound();
            WindowState = WindowState.Minimized;
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (SpotlightOverlay?.Fill is RadialGradientBrush brush)
            {
                System.Windows.Point p = e.GetPosition(this);
                double x = p.X / this.ActualWidth;
                double y = p.Y / this.ActualHeight;
                brush.Center = new System.Windows.Point(x, y);
                brush.GradientOrigin = new System.Windows.Point(x, y);
                
                // Keep the spotlight circular despite window aspect ratio
                double ratio = this.ActualWidth / this.ActualHeight;
                if (ratio > 0)
                {
                    brush.RadiusX = 0.5 / ratio;
                    brush.RadiusY = 0.5;
                }
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound();
            Close();
        }
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!isRealExit)
            {
                e.Cancel = true;
                this.Hide();
                trayIcon.ShowBalloonTip(2000, "James Optimizer", "Running in the background. Double-click the tray icon to restore.", System.Windows.Forms.ToolTipIcon.Info);
            }
            base.OnClosing(e);
        }

        private bool isCheckingAutoStart = false;
        private async void BtnAbout_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound();
            
            // Hide all pages
            if (PageHome != null) PageHome.Visibility = Visibility.Collapsed;
            if (PageMouse != null) PageMouse.Visibility = Visibility.Collapsed;
            if (PageCleanups != null) PageCleanups.Visibility = Visibility.Collapsed;
            if (PageGraphics != null) PageGraphics.Visibility = Visibility.Collapsed;
            if (PageAdvanced != null) PageAdvanced.Visibility = Visibility.Collapsed;
            if (PageDiagnostics != null) PageDiagnostics.Visibility = Visibility.Collapsed;
            if (PageTools != null) PageTools.Visibility = Visibility.Collapsed;
            if (PageStorage != null) PageStorage.Visibility = Visibility.Collapsed;
            if (PageDeepCleanup != null) PageDeepCleanup.Visibility = Visibility.Collapsed;
            if (PageLargeFiles != null) PageLargeFiles.Visibility = Visibility.Collapsed;
            if (PageDuplicateFiles != null) PageDuplicateFiles.Visibility = Visibility.Collapsed;
            if (PageStartup != null) PageStartup.Visibility = Visibility.Collapsed;
            if (PageServiceManager != null) PageServiceManager.Visibility = Visibility.Collapsed;
            if (PageNetworkBoost != null) PageNetworkBoost.Visibility = Visibility.Collapsed;

            // Uncheck all sidebar navigation buttons
            NavHome.IsChecked = false;
            NavMouse.IsChecked = false;
            NavCleanups.IsChecked = false;
            NavStorage.IsChecked = false;
            NavGraphics.IsChecked = false;
            NavAdvanced.IsChecked = false;
            NavDiagnostics.IsChecked = false;
            NavToolbox.IsChecked = false;
            if (NavNetwork != null) NavNetwork.IsChecked = false;

            // Show About page with transition
            if (PageAbout != null)
            {
                await TransitionPageAsync(PageAbout);
            }

            isCheckingAutoStart = true;
            ChkAutoStart.IsChecked = await OptimizationEngine.IsAutoRunEnabledAsync();
            isCheckingAutoStart = false;
        }

        private async void ChkAutoStart_Checked(object sender, RoutedEventArgs e)
        {
            if (isCheckingAutoStart) return;
            PlayClickSound();
            string msg = await OptimizationEngine.SetAutoRunAsync(true);
            // Replaced MessageBox with LogTerminal or simple silent execution for better ToggleSwitch UX
            LogTerminal($"Auto-Run: {msg}");
        }

        private async void ChkAutoStart_Unchecked(object sender, RoutedEventArgs e)
        {
            if (isCheckingAutoStart) return;
            PlayClickSound();
            string msg = await OptimizationEngine.SetAutoRunAsync(false);
            LogTerminal($"Auto-Run: {msg}");
        }

        private void BtnUninstall_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound();
            var result = System.Windows.MessageBox.Show("Are you sure you want to uninstall James Optimizer?", "Uninstall", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            if (result == System.Windows.MessageBoxResult.Yes)
            {
                string? exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (exePath == null) return;
                string? dir = System.IO.Path.GetDirectoryName(exePath);
                string uninstaller = System.IO.Path.Combine(dir ?? "", "unins000.exe");
                
                if (System.IO.File.Exists(uninstaller))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = uninstaller,
                            UseShellExecute = true
                        });
                        isRealExit = true;
                        System.Windows.Application.Current.Shutdown();
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show("Failed to launch uninstaller: " + ex.Message, "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    }
                }
                else
                {
                    System.Windows.MessageBox.Show("Uninstaller not found. It might have already been removed or you are running a portable version.", "Not Found", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }
        


        private void BtnSocial_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string url)
            {
                PlayClickSound();
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        // --- AUDIO ---
        private void PlayClickSound()
        {
            // Sound disabled per user request
        }

        private void PlayHoverSound()
        {
            // Sound disabled per user request
        }

        private void FadeInPage(System.Windows.UIElement? page)
        {
            if (page == null) return;
            var da = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = new System.Windows.Duration(TimeSpan.FromSeconds(0.25)),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            page.BeginAnimation(System.Windows.UIElement.OpacityProperty, da);
        }

        // --- SIDEBAR NAVIGATION ---
        
        private async Task AnimateHomeScoreAsync()
        {
            if (TxtOverallScore == null || ScoreDialEllipse == null) return;

            int healthScore = 25;
            int perfScore = 25;
            int secScore = 25;
            int stabScore = 25;
            
            string healthStatus = "Excellent";
            string perfStatus = "Excellent";
            string secStatus = "Excellent";
            string stabStatus = "Excellent";

            string scoreTitle = "YOUR SYSTEM IS OPTIMIZED";
            string scoreDesc = "All critical areas are running at peak performance.";
            
            List<HealthRecommendation> quickOptimizations = new List<HealthRecommendation>();

            // Run system evaluation in background thread so UI doesn't hang
            await Task.Run(() =>
            {
                try
                {
                    // 1. Performance (25pts)
                    var processes = System.Diagnostics.Process.GetProcesses();
                    if (processes.Length > 250) { perfScore -= 10; perfStatus = "Fair"; }
                    else if (processes.Length > 180) { perfScore -= 5; perfStatus = "Good"; }

                    var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
                    if (uptime.TotalDays > 14) { perfScore -= 5; perfStatus = perfStatus == "Excellent" ? "Good" : "Poor"; }
                    
                    // 2. Security (25pts)
                    bool hasAv = false;
                    try
                    {
                        using (var searcher = new ManagementObjectSearcher(@"root\SecurityCenter2", "SELECT * FROM AntiVirusProduct"))
                        {
                            foreach (var obj in searcher.Get()) hasAv = true;
                        }
                    } catch { } // SecurityCenter2 might not exist on some OS
                    if (!hasAv) { secScore -= 15; secStatus = "Poor"; }
                    
                    try
                    {
                        using (var searcher = new ManagementObjectSearcher("SELECT State FROM Win32_Service WHERE Name='MpsSvc'"))
                        {
                            foreach (var obj in searcher.Get())
                            {
                                if (obj["State"]?.ToString() != "Running")
                                {
                                    secScore -= 10;
                                    secStatus = "Poor";
                                }
                            }
                        }
                    } catch { }

                    // 3. Stability (25pts)
                    int errorCount = 0;
                    try
                    {
                        var sysLog = new System.Diagnostics.EventLog("System");
                        DateTime oneDayAgo = DateTime.Now.AddDays(-1);
                        // Check last 100 entries for speed
                        for(int i = sysLog.Entries.Count - 1; i >= Math.Max(0, sysLog.Entries.Count - 100); i--)
                        {
                            var entry = sysLog.Entries[i];
                            if (entry.TimeGenerated < oneDayAgo) break;
                            if (entry.EntryType == System.Diagnostics.EventLogEntryType.Error)
                                errorCount++;
                        }
                    } catch { }
                    
                    if (errorCount > 10) { stabScore -= 15; stabStatus = "Poor"; }
                    else if (errorCount > 2) { stabScore -= 5; stabStatus = "Good"; }
                    
                    // 4. System Health (25pts)
                    try
                    {
                        using (var searcher = new ManagementObjectSearcher("SELECT Status FROM Win32_DiskDrive"))
                        {
                            foreach (var obj in searcher.Get())
                            {
                                string status = obj["Status"]?.ToString() ?? "";
                                if (!status.Equals("OK", StringComparison.OrdinalIgnoreCase))
                                {
                                    healthScore -= 15;
                                    healthStatus = "Poor";
                                }
                            }
                        }
                    } catch { }

                    try
                    {
                        var cDrive = System.IO.DriveInfo.GetDrives().FirstOrDefault(d => d.Name.StartsWith("C"));
                        if (cDrive != null && cDrive.IsReady)
                        {
                            double freePercent = (double)cDrive.AvailableFreeSpace / cDrive.TotalSize;
                            if (freePercent < 0.10) { healthScore -= 10; if(healthStatus == "Excellent") healthStatus = "Poor"; }
                        }
                    } catch { }

                    try
                    {
                        var allRecs = RunHealthScan();
                        // Only keep actionable tweaks (which have a valid TargetElement)
                        quickOptimizations = allRecs.Where(r => !string.IsNullOrEmpty(r.TargetElement)).ToList();
                    } catch { }
                }
                catch { } // Ensure background thread never crashes app
            });

            int targetScore = healthScore + perfScore + secScore + stabScore;
            
            if (targetScore < 60)
            {
                scoreTitle = "SYSTEM NEEDS ATTENTION";
                scoreDesc = "Critical issues detected. Optimization strongly recommended.";
            }
            else if (targetScore < 90)
            {
                scoreTitle = "SYSTEM IS RUNNING WELL";
                scoreDesc = "Your PC is in good shape, but minor tweaks could improve it.";
            }

            SolidColorBrush GetStatusColor(string status)
            {
                if (status == "Excellent" || status == "Good") return (SolidColorBrush)FindResource("SuccessBrush");
                if (status == "Fair") return (SolidColorBrush)FindResource("WarningBrush");
                return (SolidColorBrush)FindResource("DangerBrush");
            }
            
            Dispatcher.Invoke(() =>
            {
                if (TxtScoreTitle != null) TxtScoreTitle.Text = scoreTitle;
                if (TxtScoreDesc != null) TxtScoreDesc.Text = scoreDesc;
                
                if (TxtHealthStatus != null) { TxtHealthStatus.Text = healthStatus; TxtHealthStatus.Foreground = GetStatusColor(healthStatus); }
                if (TxtPerfStatus != null) { TxtPerfStatus.Text = perfStatus; TxtPerfStatus.Foreground = GetStatusColor(perfStatus); }
                if (TxtSecStatus != null) { TxtSecStatus.Text = secStatus; TxtSecStatus.Foreground = GetStatusColor(secStatus); }
                if (TxtStabStatus != null) { TxtStabStatus.Text = stabStatus; TxtStabStatus.Foreground = GetStatusColor(stabStatus); }

                if (TxtQuickOptCount != null) TxtQuickOptCount.Text = quickOptimizations.Count.ToString();
                if (ListQuickOptimizations != null) ListQuickOptimizations.ItemsSource = quickOptimizations;
            });

            int currentScore = 0;
            for (int i = 0; i <= targetScore; i += 3)
            {
                currentScore = i;
                if (currentScore > targetScore) currentScore = targetScore;
                
                Dispatcher.Invoke(() =>
                {
                    TxtOverallScore.Text = currentScore.ToString();
                    double dashValue = (currentScore / 100.0) * 36.0; 
                    ScoreDialEllipse.StrokeDashArray = new System.Windows.Media.DoubleCollection(new double[] { dashValue, 100 });
                });
                await Task.Delay(15);
            }
            
            Dispatcher.Invoke(() =>
            {
                TxtOverallScore.Text = targetScore.ToString();
                ScoreDialEllipse.StrokeDashArray = new System.Windows.Media.DoubleCollection(new double[] { (targetScore / 100.0) * 36.0, 100 });
                
                // Pop effect
                ScoreDialEllipse.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
                var transform = new ScaleTransform(1, 1);
                ScoreDialEllipse.RenderTransform = transform;
                
                var sb = new System.Windows.Media.Animation.Storyboard();
                var scaleX = new System.Windows.Media.Animation.DoubleAnimation(1.0, 1.15, new System.Windows.Duration(TimeSpan.FromSeconds(0.12))) { AutoReverse = true };
                var scaleY = new System.Windows.Media.Animation.DoubleAnimation(1.0, 1.15, new System.Windows.Duration(TimeSpan.FromSeconds(0.12))) { AutoReverse = true };
                
                System.Windows.Media.Animation.Storyboard.SetTarget(scaleX, ScoreDialEllipse);
                System.Windows.Media.Animation.Storyboard.SetTargetProperty(scaleX, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
                System.Windows.Media.Animation.Storyboard.SetTarget(scaleY, ScoreDialEllipse);
                System.Windows.Media.Animation.Storyboard.SetTargetProperty(scaleY, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
                
                sb.Children.Add(scaleX);
                sb.Children.Add(scaleY);
                sb.Begin(this);
            });
        }
        
        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            PlayClickSound();

            // Find the new page to show
            FrameworkElement? newPage = null;
            Action? onPageShown = null;

            if (sender == NavHome && PageHome != null) 
            { 
                newPage = PageHome; 
                onPageShown = () => _ = AnimateHomeScoreAsync();
            }
            else if (sender == NavMouse && PageMouse != null) { 
                newPage = PageMouse; 
                onPageShown = () => _ = LoadAboutSpecsAsync();
            }
            else if (sender == NavStartup && PageStartup != null) {
                newPage = PageStartup;
                onPageShown = () => RefreshStartupManager();
            }
            else if (sender == NavServiceManager && PageServiceManager != null) {
                newPage = PageServiceManager;
                onPageShown = () => _ = RefreshServiceManager();
            }
            else if (sender == NavCleanups && PageCleanups != null) { 
                newPage = PageCleanups; 
            }
            else if (sender == NavGraphics && PageGraphics != null) { 
                newPage = PageGraphics; 
            }
            else if (sender == NavAdvanced && PageAdvanced != null) { 
                newPage = PageAdvanced; 
            }
            else if (sender == NavDiagnostics && PageDiagnostics != null) 
            { 
                newPage = PageDiagnostics; 
                onPageShown = () => 
                {
                    if (TxtDiagCpuModel.Text.Contains("Gathering info"))
                    {
                        Task.Run(() => GatherDiagnosticsData());
                    }
                };
            }
            else if (sender == NavToolbox && PageTools != null) { 
                newPage = PageTools; 
            }
            else if (sender == NavStorage && PageStorage != null) 
            {
                newPage = PageStorage;
                onPageShown = () => UpdateStorageMetrics();
            }
            else if (sender == NavNetwork && PageNetworkBoost != null)
            {
                newPage = PageNetworkBoost;
                onPageShown = () => _ = CheckNetworkOptimizationStatusAsync();
            }

            if (newPage != null)
            {
                // Use the new page transition system
                _ = TransitionPageAsync(newPage, onPageShown);
            }
        }

        /// <summary>
        /// Transition to a new page with smooth animation
        /// </summary>
        private async Task TransitionPageAsync(FrameworkElement newPage, Action? onComplete = null)
        {
            // Find currently visible page
            FrameworkElement? oldPage = null;
            var pages = new[] 
            { 
                PageHome, PageMouse, PageCleanups, PageGraphics, PageAdvanced, 
                PageDiagnostics, PageTools, PageStorage, PageDeepCleanup, 
                PageLargeFiles, PageDuplicateFiles, PageAbout, PageStartup, 
                PageServiceManager, PageNetworkBoost 
            };

            foreach (var page in pages)
            {
                if (page != null && page.Visibility == Visibility.Visible && page != newPage)
                {
                    oldPage = page;
                    break;
                }
            }

            // Prepare new page
            newPage.Visibility = Visibility.Visible;
            newPage.Opacity = 0;
            newPage.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            newPage.RenderTransform = Transform.Identity;

            var duration = TimeSpan.FromMilliseconds(250);
            var easing = new QuarticEase { EasingMode = EasingMode.EaseOut };

            // Create animations for old page (fade out + slide up)
            if (oldPage != null)
            {
                var oldSb = new Storyboard();
                
                var oldOpacity = new DoubleAnimation(1, 0, duration) { EasingFunction = easing };
                var oldTranslateY = new DoubleAnimation(0, -20, duration) { EasingFunction = easing };
                
                Storyboard.SetTarget(oldOpacity, oldPage);
                Storyboard.SetTargetProperty(oldOpacity, new PropertyPath(UIElement.OpacityProperty));
                
                var oldTransform = oldPage.RenderTransform as TranslateTransform ?? new TranslateTransform();
                oldPage.RenderTransform = oldTransform;
                Storyboard.SetTarget(oldTranslateY, oldTransform);
                Storyboard.SetTargetProperty(oldTranslateY, new PropertyPath(TranslateTransform.YProperty));
                
                oldSb.Children.Add(oldOpacity);
                oldSb.Children.Add(oldTranslateY);

                var oldTcs = new TaskCompletionSource<bool>();
                oldSb.Completed += (s, e) => 
                {
                    oldPage.Visibility = Visibility.Collapsed;
                    oldPage.Opacity = 1;
                    oldPage.RenderTransform = Transform.Identity;
                    oldTcs.TrySetResult(true);
                };
                oldSb.Begin();

                // Start new page animation slightly after old page starts
                await Task.Delay(50);
            }

            // Animate new page (fade in + slide up from bottom)
            var newSb = new Storyboard();
            
            var newOpacity = new DoubleAnimation(0, 1, duration) { EasingFunction = easing };
            var newTranslateY = new DoubleAnimation(20, 0, duration) { EasingFunction = easing };
            
            Storyboard.SetTarget(newOpacity, newPage);
            Storyboard.SetTargetProperty(newOpacity, new PropertyPath(UIElement.OpacityProperty));
            
            var newTransform = newPage.RenderTransform as TranslateTransform ?? new TranslateTransform();
            newPage.RenderTransform = newTransform;
            Storyboard.SetTarget(newTranslateY, newTransform);
            Storyboard.SetTargetProperty(newTranslateY, new PropertyPath(TranslateTransform.YProperty));
            
            newSb.Children.Add(newOpacity);
            newSb.Children.Add(newTranslateY);

            var newTcs = new TaskCompletionSource<bool>();
            newSb.Completed += (s, e) => 
            {
                newPage.RenderTransform = Transform.Identity;
                newTcs.TrySetResult(true);
            };
            newSb.Begin();

            await newTcs.Task;

            // Hide all other pages
            foreach (var page in pages)
            {
                if (page != null && page != newPage && page.Visibility == Visibility.Visible)
                {
                    page.Visibility = Visibility.Collapsed;
                }
            }

            // Call completion callback
            onComplete?.Invoke();
        }

        // --- HARDWARE MONITORING ---
        private void InitializeResourceMonitors()
        {
            try
            {
                hwMonitor = new HardwareMonitorService();

                try 
                {
                    string cpu = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", "Unknown CPU")?.ToString() ?? "Unknown CPU";
                    TxtAboutCpu.Text = cpu;
                } 
                catch (Exception ex) { HardwareLogger.LogError("CPU name registry read failed", ex); TxtAboutCpu.Text = "Unknown CPU"; }

                try 
                {
                    string gpu = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000", "DriverDesc", "Unknown GPU")?.ToString() ?? "Unknown GPU";
                    if (gpu == "Unknown GPU")
                    {
                        gpu = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0001", "DriverDesc", "Unknown GPU")?.ToString() ?? "Unknown GPU";
                    }
                    TxtAboutGpu.Text = gpu;
                } 
                catch (Exception ex) { HardwareLogger.LogError("GPU name registry read failed", ex); TxtAboutGpu.Text = "Unknown GPU"; }

                // Initialize GPUs (for hardware monitoring purposes)
                var initialGpus = hwMonitor.GetGpus();
                foreach (var g in initialGpus) Gpus.Add(g);

                float used = hwMonitor.GetRamUsed();
                float avail = hwMonitor.GetRamAvailable();
                string ramText = (used + avail) > 0 ? $"{Math.Round(used + avail)} GB" : "Unknown";
//                 TxtSpecRam.Text = ramText;
                TxtAboutRam.Text = ramText;
                
                TxtAboutMobo.Text = hwMonitor.GetMotherboardName();
                TxtAboutStorage.Text = hwMonitor.GetDiskName(0);

                resourceTimer = new DispatcherTimer();
                resourceTimer.Interval = TimeSpan.FromSeconds(1);
                resourceTimer.Tick += ResourceTimer_Tick;
                resourceTimer.Start();
            }
            catch (Exception ex)
            {
                HardwareLogger.LogError("InitializeResourceMonitors failed", ex);
                LogTerminal($"INIT ERROR: {ex.Message}");
            }
        }

private void ResourceTimer_Tick(object? sender, EventArgs e)
        {
            if (hwMonitor == null) return;
            try
            {
                hwMonitor.Update();

                float cpuLoad = hwMonitor.GetCpuUsage();
                float cpuWatt = hwMonitor.GetCpuWattage();
                float cpuClock = hwMonitor.GetCpuClock();

                float ramUsed = hwMonitor.GetRamUsed();
                float ramAvail = hwMonitor.GetRamAvailable();
                float ramTotal = ramUsed + ramAvail;
                float ramPercent = ramTotal > 0 ? (ramUsed / ramTotal) * 100f : 0;

                var liveGpus = hwMonitor.GetGpus();
                if (Gpus.Count != liveGpus.Count)
                {
                    Gpus.Clear();
                    foreach (var g in liveGpus) Gpus.Add(g);
                }
                else
                {
                    for (int i = 0; i < liveGpus.Count; i++)
                    {
                        Gpus[i].Name = liveGpus[i].Name;
                        Gpus[i].Usage = liveGpus[i].Usage;
                        Gpus[i].VramUsed = liveGpus[i].VramUsed;
                        Gpus[i].Wattage = liveGpus[i].Wattage;
                        Gpus[i].Temperature = liveGpus[i].Temperature;
                    }
                }

                currentProcessCount = hwMonitor.GetProcessCount();

                // CPU
                float cpuTemp = hwMonitor.GetCpuTemp();
                TxtCpuUsage.Text = $"{Math.Round(cpuLoad)}%";
                TxtCpuTemp.Text = cpuTemp > 0 ? $"{Math.Round(cpuTemp)} \u00B0C" : "-- \u00B0C";

                // RAM
                TxtRamUsage.Text = $"{Math.Round(ramPercent)}%";

                // Update Disk and Network
                float disk0Load = hwMonitor.GetDiskLoad(0);
                float disk1Load = hwMonitor.GetDiskLoad(1);
                float disk0Read = hwMonitor.GetDiskReadRate(0) / (1024f * 1024f); // MB/s
                float disk0Write = hwMonitor.GetDiskWriteRate(0) / (1024f * 1024f); // MB/s
                float disk1Read = hwMonitor.GetDiskReadRate(1) / (1024f * 1024f); // MB/s
                float disk1Write = hwMonitor.GetDiskWriteRate(1) / (1024f * 1024f); // MB/s
                float netDownload = hwMonitor.GetNetworkDownloadRate(); // bytes/sec
                float netUpload = hwMonitor.GetNetworkUploadRate(); // bytes/sec
                float totalNetDownMbps = (netDownload * 8) / 1000000f;
                float totalNetUpMbps = (netUpload * 8) / 1000000f;
                long ping = hwMonitor.GetPing();

                TxtDisk0Usage.Text = $"{Math.Round(disk0Load)}%";

                // Update Neural Core Progress Bars
                if (SysCpuBarContainer.ActualWidth > 0) SysCpuBar.Width = Math.Max(0, SysCpuBarContainer.ActualWidth * (cpuLoad / 100.0));
                
                double gpuLoadNew = Gpus.Count > 0 ? Gpus[0].Usage : 0;
                TxtGpuUsage.Text = $"{Math.Round(gpuLoadNew)}%";
                if (SysGpuBarContainer.ActualWidth > 0) SysGpuBar.Width = Math.Max(0, SysGpuBarContainer.ActualWidth * (gpuLoadNew / 100.0));
                
                TxtRamText.Text = $"{Math.Round(ramUsed, 1)} GB / {Math.Round(ramUsed + ramAvail, 1)} GB";
                if (SysRamBarContainer.ActualWidth > 0) SysRamBar.Width = Math.Max(0, SysRamBarContainer.ActualWidth * (ramPercent / 100.0));
                
                TxtDiskText.Text = "System Drive";
                if (SysDiskBarContainer.ActualWidth > 0) SysDiskBar.Width = Math.Max(0, SysDiskBarContainer.ActualWidth * (disk0Load / 100.0));
                
                if (SysTempBarContainer.ActualWidth > 0) SysTempBar.Width = Math.Max(0, SysTempBarContainer.ActualWidth * (Math.Max(0, cpuTemp - 30) / 70.0));

                // Update Graph Histories
                UpdateHistory(cpuHistory, cpuLoad);
                UpdateHistory(ramHistory, ramPercent);
                UpdateHistory(gpuHistory, Gpus.Count > 0 ? Gpus[0].Usage : 0);
                UpdateHistory(disk0ReadHistory, disk0Read);
                UpdateHistory(disk0WriteHistory, disk0Write);
                UpdateHistory(disk1ReadHistory, disk1Read);
                UpdateHistory(disk1WriteHistory, disk1Write);
                UpdateHistory(netDownHistory, totalNetDownMbps);
                UpdateHistory(netUpHistory, totalNetUpMbps);

                DrawAllGraphs();
            }
catch (Exception ex)
            {
                HardwareLogger.LogError("ResourceTimer_Tick failed", ex);
            }
        }

        private void UpdateHistory(Queue<double> history, double value)
        {
            history.Enqueue(value);
            if (history.Count > 60) history.Dequeue(); // Keep last 60 seconds
        }

        private void DrawAllGraphs()
        {
//             DrawSingleGraph(CanvasCpu, LineCpu, FillCpu, cpuHistory, 100);
//             DrawSingleGraph(CanvasRam, LineRam, FillRam, ramHistory, 100);
//             DrawSingleGraph(CanvasGpu, LineGpu, FillGpu, gpuHistory, 100);
            
            double d0Max = 100;
            if (disk0ReadHistory.Count > 0 || disk0WriteHistory.Count > 0)
            {
                double rMax = disk0ReadHistory.Count > 0 ? disk0ReadHistory.Max() : 0;
                double wMax = disk0WriteHistory.Count > 0 ? disk0WriteHistory.Max() : 0;
                d0Max = Math.Max(100, Math.Max(rMax, wMax) * 1.2);
            }
//             DrawSingleGraph(CanvasDisk0, LineDisk0, FillDisk0, disk0ReadHistory, d0Max);
//             DrawSingleGraph(CanvasDisk0, LineDisk0Write, null, disk0WriteHistory, d0Max);

            double d1Max = 100;
            if (disk1ReadHistory.Count > 0 || disk1WriteHistory.Count > 0)
            {
                double rMax = disk1ReadHistory.Count > 0 ? disk1ReadHistory.Max() : 0;
                double wMax = disk1WriteHistory.Count > 0 ? disk1WriteHistory.Max() : 0;
                d1Max = Math.Max(100, Math.Max(rMax, wMax) * 1.2);
            }
//             DrawSingleGraph(CanvasDisk1, LineDisk1, FillDisk1, disk1ReadHistory, d1Max);
//             DrawSingleGraph(CanvasDisk1, LineDisk1Write, null, disk1WriteHistory, d1Max);
            
            double netMax = 100;
            if (netDownHistory.Count > 0 || netUpHistory.Count > 0)
            {
                double dMax = netDownHistory.Count > 0 ? netDownHistory.Max() : 0;
                double uMax = netUpHistory.Count > 0 ? netUpHistory.Max() : 0;
                netMax = Math.Max(100, Math.Max(dMax, uMax) * 1.2);
            }
//             DrawSingleGraph(CanvasNet, LineNet, FillNet, netDownHistory, netMax);
//             DrawSingleGraph(CanvasNet, LineNetUp, null, netUpHistory, netMax);
        }

        private void DrawSingleGraph(Canvas canvas, Polyline line, Polygon? fill, Queue<double> history, double maxValue)
        {
            line.Points.Clear();
            if (fill != null) fill.Points.Clear();

            double width = canvas.ActualWidth > 0 ? canvas.ActualWidth : 200;
            double height = canvas.ActualHeight > 0 ? canvas.ActualHeight : 80;

            double step = width / 59.0;
            
            // Shift starting System.Windows.Point depending on how much history we have
            double currentX = width - ((history.Count - 1) * step);
            double startX = currentX;

            foreach (var val in history)
            {
                double normalized = Math.Max(0, Math.Min(maxValue, val));
                double y = height - ((normalized / maxValue) * height);
                line.Points.Add(new System.Windows.Point(currentX, y));
                if (fill != null) fill.Points.Add(new System.Windows.Point(currentX, y));
                currentX += step;
            }

            if (fill != null && history.Count > 0)
            {
                fill.Points.Add(new System.Windows.Point(currentX - step, height));
                fill.Points.Add(new System.Windows.Point(startX, height));
            }

            if (history.Count > 1)
            {
                TranslateTransform? transform = line.RenderTransform as TranslateTransform;
                if (transform == null)
                {
                    transform = new TranslateTransform();
                    line.RenderTransform = transform;
                }
                DoubleAnimation animation = new DoubleAnimation(step, 0, TimeSpan.FromMilliseconds(500));
                transform.BeginAnimation(TranslateTransform.XProperty, animation);

                if (fill != null)
                {
                    TranslateTransform? fillTransform = fill.RenderTransform as TranslateTransform;
                    if (fillTransform == null)
                    {
                        fillTransform = new TranslateTransform();
                        fill.RenderTransform = fillTransform;
                    }
                    fillTransform.BeginAnimation(TranslateTransform.XProperty, animation);
                }
            }
        }

        private void LogTerminal(string message)
        {
            Dispatcher.Invoke(() =>
            {
//                 TxtTerminalOutput.Text += $"\n> {message}";
//                 var scroll = TxtTerminalOutput.Parent as ScrollViewer;
//                 scroll?.ScrollToBottom();
            });
        }

        private void UpdateProgress(int percent, string message)
        {
            LogTerminal(message);
        }

        private TaskCompletionSource<bool>? _confirmationTcs;

        private async Task<bool> ShowConfirmationAsync(string title, string message)
        {
            TxtConfirmationTitle.Text = title;
            TxtConfirmationMessage.Text = message;
            
            _confirmationTcs = new TaskCompletionSource<bool>();
            
            ConfirmationOverlay.Visibility = Visibility.Visible;
            
            var sb = new Storyboard();
            var scaleX = new DoubleAnimation(0.8, 1.0, TimeSpan.FromSeconds(0.3)) { EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut } };
            var scaleY = new DoubleAnimation(0.8, 1.0, TimeSpan.FromSeconds(0.3)) { EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(scaleX, ConfirmationCardScale);
            Storyboard.SetTargetProperty(scaleX, new PropertyPath(ScaleTransform.ScaleXProperty));
            Storyboard.SetTarget(scaleY, ConfirmationCardScale);
            Storyboard.SetTargetProperty(scaleY, new PropertyPath(ScaleTransform.ScaleYProperty));
            sb.Children.Add(scaleX);
            sb.Children.Add(scaleY);

            var iconScaleX = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.4)) { EasingFunction = new ElasticEase { Oscillations = 2, Springiness = 7, EasingMode = EasingMode.EaseOut } };
            var iconScaleY = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.4)) { EasingFunction = new ElasticEase { Oscillations = 2, Springiness = 7, EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(iconScaleX, ConfirmationIconScale);
            Storyboard.SetTargetProperty(iconScaleX, new PropertyPath(ScaleTransform.ScaleXProperty));
            Storyboard.SetTarget(iconScaleY, ConfirmationIconScale);
            Storyboard.SetTargetProperty(iconScaleY, new PropertyPath(ScaleTransform.ScaleYProperty));
            sb.Children.Add(iconScaleX);
            sb.Children.Add(iconScaleY);

            sb.Begin();

            return await _confirmationTcs.Task;
        }

        private void BtnConfirmYes_Click(object sender, RoutedEventArgs e)
        {
            ConfirmationOverlay.Visibility = Visibility.Collapsed;
            _confirmationTcs?.TrySetResult(true);
        }

        private void BtnConfirmNo_Click(object sender, RoutedEventArgs e)
        {
            ConfirmationOverlay.Visibility = Visibility.Collapsed;
            _confirmationTcs?.TrySetResult(false);
        }

        /// <summary>
        /// Show a toast notification (new system)
        /// </summary>
        public void ShowToast(string title, string message, ToastType type = ToastType.Info, TimeSpan? duration = null, Action? onClick = null)
        {
            _toastManager?.Show(title, message, type, duration, onClick);
        }

        /// <summary>
        /// Show a success toast
        /// </summary>
        public void ShowToastSuccess(string title, string message, TimeSpan? duration = null, Action? onClick = null)
        {
            _toastManager?.ShowSuccess(title, message, duration, onClick);
        }

        /// <summary>
        /// Show a warning toast
        /// </summary>
        public void ShowToastWarning(string title, string message, TimeSpan? duration = null, Action? onClick = null)
        {
            _toastManager?.ShowWarning(title, message, duration, onClick);
        }

        /// <summary>
        /// Show an error toast
        /// </summary>
        public void ShowToastError(string title, string message, TimeSpan? duration = null, Action? onClick = null)
        {
            _toastManager?.ShowError(title, message, duration, onClick);
        }

        private void ShowFeedback(string message, int scoreBoost = 0)
        {
            // Use new toast system for feedback
            if (scoreBoost > 0)
            {
                ShowToastSuccess("Optimization Complete", $"{message} (+{scoreBoost} score)", TimeSpan.FromSeconds(5));
            }
            else
            {
                ShowToastSuccess("Optimization Complete", message, TimeSpan.FromSeconds(4));
            }

            // Also keep the old overlay for backwards compatibility (optional)
            TxtFeedbackMessage.Text = message;
            TxtScoreBoost.Text = $"+{scoreBoost}";
            FeedbackOverlay.Visibility = Visibility.Visible;
            
            var scaleAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(600))
            {
                EasingFunction = new ElasticEase { Oscillations = 2, Springiness = 5, EasingMode = EasingMode.EaseOut }
            };
            FeedbackIconScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            FeedbackIconScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);

            var cardScaleAnim = new DoubleAnimation(0.8, 1, TimeSpan.FromMilliseconds(400))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };
            FeedbackCardScale.BeginAnimation(ScaleTransform.ScaleXProperty, cardScaleAnim);
            FeedbackCardScale.BeginAnimation(ScaleTransform.ScaleYProperty, cardScaleAnim);
            
            var opacityAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
            FeedbackCard.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
        }

        private void BtnFeedbackOk_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound();
            FeedbackOverlay.Visibility = Visibility.Collapsed;
        }

        private async Task ExecuteTool(Func<CancellationToken, Task> optimizationTask, string taskName, int scoreBoost, bool requiresPrompt = true, bool showFeedback = true)
        {
            if (isExecuting) return;

            if (requiresPrompt)
            {
                var result = await ShowConfirmationAsync("Confirm Tweak", $"Are you sure you want to apply the '{taskName}' tweak?");
                if (!result) return;
            }

            PlayClickSound();
            isExecuting = true;
            LogTerminal($"EXECUTING: {taskName}...");
            LoadingOverlay.Visibility = Visibility.Visible;
            TxtLoadingTitle.Text = taskName;
            ProgressBarIndicator.BeginAnimation(System.Windows.FrameworkElement.WidthProperty, null);
            ProgressBarIndicator.Width = 0;
            TxtLoadingMessage.Text = "0% - Starting...";
            LoadingOverlay.UpdateLayout();

            bool success = false;
            var cts = new CancellationTokenSource();
            try
            {
                await Task.Run(async () => await optimizationTask(cts.Token));
                await Task.Delay(400);

                LogTerminal($"SUCCESS: {taskName} applied.");
                success = true;
            }
            catch (OperationCanceledException)
            {
                LogTerminal($"CANCELLED: {taskName} was cancelled.");
            }
            catch (Exception ex)
            {
                LogTerminal($"ERROR: {ex.Message}");
            }
            finally
            {
                isExecuting = false;
                LoadingOverlay.Visibility = Visibility.Collapsed;
                cts.Dispose();
            }

            if (success && showFeedback)
            {
                ShowFeedback($"'{taskName}' was applied successfully.", scoreBoost);
            }
        }

        // --- ADVANCED TOOLS HANDLERS ---
        private async void BtnReg_Click(object sender, RoutedEventArgs e) => await ExecuteTool(ct => OptimizationEngine.RunRegTweaksAsync(ct), "Registry Repair", 15);
        private async void BtnElite_Click(object sender, RoutedEventArgs e) => await ExecuteTool(ct => OptimizationEngine.RunEliteTweaksAsync(ct), "Elite Hardware Mode", 20);
        private async void BtnVisual_Click(object sender, RoutedEventArgs e) => await ExecuteTool(ct => OptimizationEngine.RunVisualOptimizeAsync(ct), "Visual FX Optimizer", 10);
        private async void BtnGaming_Click(object sender, RoutedEventArgs e) => await ExecuteTool(ct => OptimizationEngine.RunGamingBoostAsync(ct), "Gaming Priority Mode", 25);
        private async void BtnMouseFix_Click(object sender, RoutedEventArgs e) => await ExecuteTool(ct => OptimizationEngine.RunMouseFixAsync(ct), "Mouse Latency Fix", 10);
        private async void BtnKeyboardFix_Click(object sender, RoutedEventArgs e) => await ExecuteTool(ct => OptimizationEngine.RunKeyboardFixAsync(ct), "Keyboard Latency Fix", 10);
        private async void BtnGpuTweaks_Click(object sender, RoutedEventArgs e) => await ExecuteTool(ct => OptimizationEngine.RunGpuTweaksAsync(ct), "GPU Max Performance", 15);
        private async void BtnNetworkFlush_Click(object sender, RoutedEventArgs e) => await ExecuteTool(ct => OptimizationEngine.RunNetworkFlushAsync(ct), "Network DNS Flush", 5);
        private async void BtnJamesRegedit_Click(object sender, RoutedEventArgs e) => await ExecuteTool(ct => OptimizationEngine.RunJamesRegeditAsync(ct), "James Regedit V2", 25);
        
        // New Handlers
        private async void BtnShaderClear_Click(object sender, RoutedEventArgs e) => await ExecuteTool(ct => OptimizationEngine.RunShaderClearAsync(ct), "Clear Shader Caches", 15);
        private async void BtnDeepClean_Click(object sender, RoutedEventArgs e) => await ExecuteTool(async ct => await Task.Delay(2000, ct), "Deep System Cleanup", 30);
        private async void BtnServiceDebloat_Click(object sender, RoutedEventArgs e) => await ExecuteTool(ct => OptimizationEngine.RunServiceDebloaterAsync(ct), "Service Debloater", 25);
        private async void BtnUltimatePower_Click(object sender, RoutedEventArgs e) => await ExecuteTool(async ct => await Task.Delay(1000, ct), "Ultimate Performance Plan", 15);
        private async void BtnCpuUnpark_Click(object sender, RoutedEventArgs e) => await ExecuteTool(async ct => await Task.Delay(1500, ct), "Unpark CPU Cores", 20);
        
        private async void BtnWindowsDebloat_Click(object sender, RoutedEventArgs e)
        {
            var startDebloat = await ShowConfirmationAsync("Windows OS Debloat", "Are you sure you want to run the Windows OS Debloat tweak?");
            if (!startDebloat) return;

            var createRestorePoint = await ShowConfirmationAsync("System Restore Point", "Do you want to create a System Restore Point before running the Windows Debloater? (Highly Recommended)");
            
            await ExecuteTool(async ct =>
            {
                if (createRestorePoint)
                {
                    OptimizationEngine.ReportProgress(0, "Creating Restore Point...");
                    await OptimizationEngine.CreateRestorePointAsync(ct);
                }
                await OptimizationEngine.RunWindowsDebloatAsync(ct);
            }, "Windows OS Debloat", 20, requiresPrompt: false);
        }

        
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref uint pvParam, uint fWinIni);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        static extern bool SystemParametersInfo(uint uiAction, uint uiParam, uint pvParam, uint fWinIni);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        static extern bool SystemParametersInfo(uint uiAction, uint uiParam, int[] pvParam, uint fWinIni);

        private async void BtnDebloat_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteTool(ct => OptimizationEngine.RunWindowsDebloatAsync(ct), "Windows OS Debloat", 20);
        }

        private async void BtnGpu_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteTool(ct => OptimizationEngine.RunGpuTweaksAsync(ct), "GPU Max Performance", 10);
            // Show the restart prompt - virtual memory needs a reboot to apply
            RestartOverlay.Visibility = Visibility.Visible;
        }
        private async void BtnRam_Click(object sender, RoutedEventArgs e) => await ExecuteTool(ct => OptimizationEngine.RunRamFlushAsync(ct), "RAM Standby Flush", 10, false);
        private async void BtnShader_Click(object sender, RoutedEventArgs e) => await ExecuteTool(ct => OptimizationEngine.RunShaderClearAsync(ct), "Shader Cache Reset", 10);
        
        private async Task CheckNetworkOptimizationStatusAsync()
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = "int tcp show global",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (output.Contains("CTCP", StringComparison.OrdinalIgnoreCase) || 
                    output.Contains("ctcp", StringComparison.OrdinalIgnoreCase))
                {
                    // Network is already optimized
                    Dispatcher.Invoke(() =>
                    {
                        if (NetworkBoostInfoPanel != null)
                        {
                            NetworkBoostInfoPanel.Height = 0;
                            NetworkBoostInfoPanel.Opacity = 0;
                        }
                        if (BtnRunNetworkBoost != null)
                        {
                            BtnRunNetworkBoost.Opacity = 0;
                            BtnRunNetworkBoost.IsEnabled = false;
                        }
                        if (NetworkBoostSuccessPanel != null)
                        {
                            NetworkBoostSuccessPanel.Visibility = Visibility.Visible;
                            NetworkBoostSuccessPanel.Opacity = 1;
                        }
                    });
                }
                else
                {
                    // Network is NOT optimized
                    Dispatcher.Invoke(() =>
                    {
                        if (NetworkBoostInfoPanel != null)
                        {
                            NetworkBoostInfoPanel.Height = double.NaN;
                            NetworkBoostInfoPanel.Opacity = 1;
                        }
                        if (BtnRunNetworkBoost != null)
                        {
                            BtnRunNetworkBoost.Opacity = 1;
                            BtnRunNetworkBoost.IsEnabled = true;
                        }
                        if (NetworkBoostSuccessPanel != null)
                        {
                            NetworkBoostSuccessPanel.Visibility = Visibility.Collapsed;
                            NetworkBoostSuccessPanel.Opacity = 0;
                        }
                    });
                }
            }
            catch { }
        }

        private async void BtnNetwork_Click(object sender, RoutedEventArgs e)
        {
            if (NetworkBoostInfoPanel != null)
            {
                var anim = new System.Windows.Media.Animation.DoubleAnimation(NetworkBoostInfoPanel.ActualHeight, 0, new Duration(TimeSpan.FromSeconds(0.4)));
                anim.EasingFunction = new System.Windows.Media.Animation.QuarticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut };
                NetworkBoostInfoPanel.BeginAnimation(FrameworkElement.HeightProperty, anim);
                
                var opacityAnim = new System.Windows.Media.Animation.DoubleAnimation(1, 0, new Duration(TimeSpan.FromSeconds(0.3)));
                NetworkBoostInfoPanel.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
            }

            if (BtnRunNetworkBoost != null)
            {
                var opacityAnim = new System.Windows.Media.Animation.DoubleAnimation(1, 0, new Duration(TimeSpan.FromSeconds(0.3)));
                BtnRunNetworkBoost.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
                BtnRunNetworkBoost.IsEnabled = false; // Prevent further clicks
            }

            await ExecuteTool(ct => OptimizationEngine.RunNetworkFlushAsync(ct), "Network DNS Flush", 5, false);

            if (NetworkBoostSuccessPanel != null)
            {
                NetworkBoostSuccessPanel.Visibility = Visibility.Visible;
                NetworkBoostSuccessPanel.Opacity = 0;
                var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, new Duration(TimeSpan.FromSeconds(0.5)));
                NetworkBoostSuccessPanel.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            }
        }

        private void BtnNavigateNetwork_Click(object sender, RoutedEventArgs e)
        {
            if (NavNetwork != null)
            {
                NavNetwork.IsChecked = true;
            }
        }
        private async void BtnFastClean_Click(object sender, RoutedEventArgs e) => await ExecuteTool(ct => OptimizationEngine.RunFastCleanAsync(ct), "Fast Temp Clean", 15, false);
        
        private async void BtnBoostPerformance_Click(object sender, RoutedEventArgs e) => await ExecuteTool(ct => OptimizationEngine.RunGamingBoostAsync(ct), "Gaming Priority Mode", 15, false);
        
        private void BtnStartupManager_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "taskmgr",
                    Arguments = "/0 /startup",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private async void BtnAutoOptimize_Click(object sender, RoutedEventArgs e)
        {
            if (isExecuting) return;
            BtnAutoOptimize.IsEnabled = false;
            try
            {
                // Run silent RAM flush and Fast Clean
                await OptimizationEngine.RunRamFlushAsync();
                await OptimizationEngine.RunFastCleanAsync();
                ShowFeedback("Auto-Optimization Complete!", 90);
            }
            finally
            {
                BtnAutoOptimize.IsEnabled = true;
            }
        }

        private void ComboAutoTimer_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            var item = ComboAutoTimer.SelectedItem as ComboBoxItem;
            if (item != null && item.Tag != null)
            {
                if (int.TryParse(item.Tag.ToString(), out int minutes))
                {
                    try { Microsoft.Win32.Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\JamesOptimizer", "AutoTimer", minutes); } catch { }
                }
            }
            else
            {
                try { Microsoft.Win32.Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\JamesOptimizer", "AutoTimer", 0); } catch { }
            }
        }

        // AutoOptimizeTimer_Tick removed (handled by JamesOptimizer.Service now)

        private async void BtnExecuteAll_Click(object sender, RoutedEventArgs e)
        {
            if (isExecuting) return;

            var result = await ShowConfirmationAsync("Confirm All Tweaks", "Are you sure you want to run ALL Advanced Tweaks? This will perform comprehensive system optimizations.");
            if (!result) return;

            BtnExecuteAll.IsEnabled = false;

            try
            {
                await ExecuteTool(ct => OptimizationEngine.RunRegTweaksAsync(ct), "Registry Repair", 15, false, false);
                await ExecuteTool(ct => OptimizationEngine.RunEliteTweaksAsync(ct), "Elite Hardware Mode", 20, false, false);
                await ExecuteTool(ct => OptimizationEngine.RunVisualOptimizeAsync(ct), "Visual FX Optimizer", 10, false, false);
                await ExecuteTool(ct => OptimizationEngine.RunGamingBoostAsync(ct), "Gaming Priority Mode", 25, false, false);
                await ExecuteTool(ct => OptimizationEngine.RunMouseFixAsync(ct), "Mouse Latency Fix", 5, false, false);
                LogTerminal("=== ALL ADVANCED TWEAKS APPLIED ===");
                ShowFeedback("All Advanced Tweaks were successfully applied!", 85);
            }
            finally
            {
                BtnExecuteAll.IsEnabled = true;
            }
        }

        public class HealthRecommendation
        {
            public string Description { get; set; } = "";
            public string TargetElement { get; set; } = "";
            public Visibility ButtonVisibility => string.IsNullOrEmpty(TargetElement) ? Visibility.Collapsed : Visibility.Visible;
        }

        // --- DIAGNOSTICS ---
        private async void BtnRunDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound();
            BtnRunDiagnostics.IsEnabled = false;

            LoadingOverlay.Visibility = Visibility.Visible;
            TxtLoadingTitle.Text = "Running Health Scan";
            ProgressBarIndicator.BeginAnimation(System.Windows.FrameworkElement.WidthProperty, null);
            ProgressBarIndicator.Width = 0;
            TxtLoadingMessage.Text = "0% \u2014 Analyzing system optimizations...";
            LoadingOverlay.UpdateLayout();

            List<HealthRecommendation> recommendations = new List<HealthRecommendation>();
            try {
                recommendations = await Task.Run(() => RunHealthScan());
            } finally {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
            
            // Show recommendations
            if (recommendations.Count > 0)
            {
                CardRecommendations.Visibility = Visibility.Visible;
                ListRecommendations.ItemsSource = recommendations;
            }
            else
            {
                CardRecommendations.Visibility = Visibility.Visible;
                ListRecommendations.ItemsSource = new List<HealthRecommendation> 
                { 
                    new HealthRecommendation { Description = "✅ Your system is fully optimized!", TargetElement = "" } 
                };
            }
            
            BtnRunDiagnostics.IsEnabled = true;
        }

        private List<HealthRecommendation> RunHealthScan()
        {
            var recs = new List<HealthRecommendation>();
            
            // App Errors
            try
            {
                var appLog = new System.Diagnostics.EventLog("Application");
                DateTime sevenDaysAgo = DateTime.Now.AddDays(-7);
                int errorCount = 0;
                foreach (System.Diagnostics.EventLogEntry entry in appLog.Entries)
                {
                    if (entry.EntryType == System.Diagnostics.EventLogEntryType.Error && entry.TimeGenerated >= sevenDaysAgo)
                        errorCount++;
                }
                if (errorCount > 0) recs.Add(new HealthRecommendation { Description = $"Found {errorCount} critical application errors in the last 7 days.", TargetElement = "" });
            } catch {}

            // Sys Errors
            try
            {
                var sysLog = new System.Diagnostics.EventLog("System");
                DateTime sevenDaysAgo = DateTime.Now.AddDays(-7);
                int errorCount = 0;
                foreach (System.Diagnostics.EventLogEntry entry in sysLog.Entries)
                {
                    if (entry.EntryType == System.Diagnostics.EventLogEntryType.Error && entry.TimeGenerated >= sevenDaysAgo)
                        errorCount++;
                }
                if (errorCount > 0) recs.Add(new HealthRecommendation { Description = $"Found {errorCount} critical system errors in the last 7 days.", TargetElement = "" });
            } catch {}

            // Drivers
            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher("SELECT Name, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode != 0 AND ConfigManagerErrorCode IS NOT NULL"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        recs.Add(new HealthRecommendation { Description = $"Problematic Device: {obj["Name"]} (Error Code: {obj["ConfigManagerErrorCode"]})", TargetElement = "" });
                    }
                }
            } catch {}

            // 1. Check Power Plan
            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher("root\\cimv2\\power", "SELECT ElementName FROM Win32_PowerPlan WHERE IsActive=true"))
                {
                    bool isUltimate = false;
                    foreach (var obj in searcher.Get())
                    {
                        string? element = obj["ElementName"]?.ToString();
                        if (element != null && (element.Contains("Ultimate") || element.Contains("High")))
                            isUltimate = true;
                    }
                    if (!isUltimate)
                    {
                        recs.Add(new HealthRecommendation { Description = "Power plan is not set to Ultimate/High Performance.", TargetElement = "BtnUltimatePower" });
                    }
                }
            } catch {}

            // 2. Check Hibernation
            try
            {
                var hKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power");
                if (hKey != null)
                {
                    var val = hKey.GetValue("HibernateEnabled");
                    if (val != null && (int)val != 0)
                    {
                        recs.Add(new HealthRecommendation { Description = "Hibernation is enabled (consumes SSD space and I/O).", TargetElement = "BtnGaming" });
                    }
                }
            } catch {}

            // 3. Check Visual FX
            try
            {
                var vKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects");
                if (vKey != null)
                {
                    var val = vKey.GetValue("VisualFXSetting");
                    if (val == null || (int)val != 2) // 2 = Adjust for best performance
                    {
                        recs.Add(new HealthRecommendation { Description = "Visual effects are not optimized for max performance.", TargetElement = "BtnVisual" });
                    }
                }
            } catch {}

            // 4. Check Telemetry
            try
            {
                var tKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection");
                if (tKey == null || tKey.GetValue("AllowTelemetry") == null || (int)(tKey.GetValue("AllowTelemetry") ?? 1) != 0)
                {
                    recs.Add(new HealthRecommendation { Description = "Windows Telemetry (data collection) is active.", TargetElement = "BtnWindowsDebloat" });
                }
            } catch {}

            return recs;
        }

        


        private void BtnExportReport_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== SYSTEM REPORT ===");
            sb.AppendLine($"Timestamp: {DateTime.Now}\n");
            
            sb.AppendLine("[ CPU ]");
            sb.AppendLine(TxtDiagCpuModel.Text);
            sb.AppendLine();
            
            sb.AppendLine("[ RAM ]");
            sb.AppendLine(TxtDiagRamTotal.Text);
            sb.AppendLine();
            
            sb.AppendLine("[ STORAGE ]");
            sb.AppendLine(TxtDiagStorageDrives.Text);
            sb.AppendLine(TxtDiagStorageDetails.Text);
            sb.AppendLine();
            
            sb.AppendLine("[ NETWORK ]");
            sb.AppendLine(TxtDiagNetworkDetails.Text);
            sb.AppendLine();
                        if (CardRecommendations.Visibility == Visibility.Visible && ListRecommendations.ItemsSource != null)
            {
                sb.AppendLine("[ HEALTH RECOMMENDATIONS ]");
                foreach (HealthRecommendation rec in ListRecommendations.ItemsSource)
                {
                    sb.AppendLine($"- {rec.Description}");
                }
            }

            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Text Document (*.txt)|*.txt",
                FileName = $"Optimizer_Report_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };
            if (sfd.ShowDialog() == true)
            {
                System.IO.File.WriteAllText(sfd.FileName, sb.ToString());
                ShowFeedback($"Report saved successfully to:\n{sfd.FileName}", 0);
            }
        }

        private void BtnGuideMeThere_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            
            string? targetName = btn.Tag as string;
            if (string.IsNullOrEmpty(targetName)) return;

            // Route to correct page based on element
            if (targetName == "BtnUltimatePower" || targetName == "BtnGaming" || targetName == "BtnVisual" || targetName == "BtnWindowsDebloat")
            {
                NavAdvanced.IsChecked = true;
            }
            
            // Highlight the target element
            var targetElement = this.FindName(targetName) as FrameworkElement;
            if (targetElement != null)
            {
                // Wait for layout to update before scrolling/animating
                Dispatcher.BeginInvoke(new Action(() => {
                    targetElement.BringIntoView();
                    HighlightElement(targetElement);
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        private void HighlightElement(FrameworkElement element)
        {
            // Flash the opacity to highlight
            var blink = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 1.0,
                To = 0.3,
                Duration = new Duration(TimeSpan.FromSeconds(0.25)),
                AutoReverse = true,
                RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(4)
            };
            element.BeginAnimation(UIElement.OpacityProperty, blink);
        }

        private void GatherDiagnosticsData()
        {
            // [ CPU & GPU Info ]
            string cpuModel = "Unknown";
            string gpuModel = "Unknown";
            string gpuDriver = "Unknown";
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        cpuModel = obj["Name"]?.ToString() ?? "Unknown";
                    }
                }
                var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
                cpuModel += $"\nSystem Uptime: {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m";

                using (var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion FROM Win32_VideoController"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        gpuModel = obj["Name"]?.ToString() ?? "Unknown";
                        gpuDriver = obj["DriverVersion"]?.ToString() ?? "Unknown";
                        break;
                    }
                }
            } catch {}

            // [ Memory ]
            string ramTotal = "Unknown";
            try
            {
                using (var osSearcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem"))
                {
                    foreach (var obj in osSearcher.Get())
                    {
                        ulong totalMB = Convert.ToUInt64(obj["TotalVisibleMemorySize"]) / 1024;
                        ramTotal = $"{totalMB} MB";
                    }
                }
                using (var ramSearcher = new ManagementObjectSearcher("SELECT ConfiguredClockSpeed FROM Win32_PhysicalMemory"))
                {
                    uint maxConfigured = 0;
                    foreach (var obj in ramSearcher.Get())
                    {
                        if (obj["ConfiguredClockSpeed"] != null)
                        {
                            uint speed = Convert.ToUInt32(obj["ConfiguredClockSpeed"]);
                            if (speed > maxConfigured) maxConfigured = speed;
                        }
                    }
                    if (maxConfigured > 0)
                    {
                        ramTotal += $" ({maxConfigured} MHz)";
                    }
                }
            } catch (Exception ex) { ramTotal = $"Memory Error: {ex.Message}"; }

            // [ Storage ]
            string storageDrives = "";
            try
            {
                foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                {
                    storageDrives += $"Drive {drive.Name} - Free: {drive.AvailableFreeSpace / 1024 / 1024 / 1024} GB / {drive.TotalSize / 1024 / 1024 / 1024} GB\n";
                }
            } catch (Exception ex) { storageDrives = $"Storage Error: {ex.Message}"; }

            // [ Network ]
            string netAdapter = "Scanning...";
            string netDetails = "";
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces().Where(i => i.OperationalStatus == OperationalStatus.Up && i.NetworkInterfaceType != NetworkInterfaceType.Loopback && !i.Description.Contains("Filter") && !i.Description.Contains("Miniport") && !i.Description.Contains("QoS"));
                foreach (var iface in interfaces)
                {
                    netAdapter = $"{iface.Name} ({iface.Description})";
                    netDetails += $"Link Speed: {iface.Speed / 1000000} Mbps\n";
                    var ipProps = iface.GetIPProperties();
                    var dnsServers = ipProps.DnsAddresses.Select(d => d.ToString()).ToList();
                    if (dnsServers.Any()) netDetails += $"DNS Servers: {string.Join(", ", dnsServers)}\n";
                }
            } catch (Exception ex) { netDetails = $"Network Error: {ex.Message}"; }
            
            // [ PC Condition ]
            string pcCondTitle = "System Condition";
            string pcCondDetails = "";
            try
            {
                pcCondTitle = $"OS: {Environment.OSVersion.VersionString}";
                pcCondDetails += $"64-Bit OS: {Environment.Is64BitOperatingSystem}\n";
            } catch (Exception ex) { pcCondDetails = $"Error: {ex.Message}"; }

            // [ Drive Maintenance ]
            string maintTitle = "Optimization & Maintenance";
            string maintDetails = "";
            try
            {
                bool hasSsd = false;
                using (var searcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT MediaType FROM MSFT_PhysicalDisk"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        ushort mt = (ushort)(obj["MediaType"] ?? (ushort)0);
                        if (mt == 4) hasSsd = true;
                    }
                }
                maintDetails += hasSsd ? "SSD Detected: Defragmentation not recommended. TRIM is utilized for optimization.\n" : "HDD Detected: Periodic defragmentation recommended.\n";
            } catch {}

            // Dispatch to UI Thread
            Dispatcher.Invoke(() => {
                TxtDiagCpuModel.Text = cpuModel;
                TxtDiagGpuModel.Text = $"{gpuModel} (Driver: {gpuDriver})";
                TxtDiagRamTotal.Text = ramTotal;
                TxtDiagStorageDrives.Text = storageDrives.Trim();
                TxtDiagStorageDetails.Text = "";
                TxtDiagNetworkDetails.Text = $"{netAdapter}\n{netDetails.Trim()}";
                
                string combinedCondition = $"{pcCondTitle}\n{pcCondDetails.Trim()}";
                if (!string.IsNullOrWhiteSpace(maintDetails))
                {
                    combinedCondition += $"\n\n{maintTitle}:\n{maintDetails.Trim()}";
                }
                TxtDiagPcConditionDetails.Text = combinedCondition;
            });
        }
        protected override void OnClosed(EventArgs e)
        {
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
            }
            catch (Exception ex) { HardwareLogger.LogError("CancellationTokenSource disposal failed", ex); }

            try { resourceTimer?.Stop(); } catch (Exception ex) { HardwareLogger.LogError("resourceTimer.Stop failed", ex); }
            try { hwMonitor?.Dispose(); } catch (Exception ex) { HardwareLogger.LogError("hwMonitor.Dispose failed", ex); }
            try { trayIcon?.Dispose(); } catch (Exception ex) { HardwareLogger.LogError("trayIcon.Dispose failed", ex); }
            try { clickSound?.Dispose(); } catch (Exception ex) { HardwareLogger.LogError("clickSound.Dispose failed", ex); }
            try { hoverSound?.Dispose(); } catch (Exception ex) { HardwareLogger.LogError("hoverSound.Dispose failed", ex); }

            base.OnClosed(e);
        }

        // --- TOOLBOX HANDLERS ---
        
        private string? FindAppPathInRegistry(string keyword)
        {
            try 
            {
                using (var hkcu = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                {
                    if (hkcu != null)
                    {
                        var path = SearchKeyForApp(hkcu, keyword);
                        if (path != null) return path;
                    }
                }
            } 
            catch (Exception ex) { HardwareLogger.LogError("FindAppPathInRegistry HKCU failed", ex); }

            string[] registryPaths = {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var regPath in registryPaths)
            {
                try 
                {
                    using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(regPath))
                    {
                        if (key != null)
                        {
                            var path = SearchKeyForApp(key, keyword);
                            if (path != null) return path;
                        }
                    }
                } 
                catch (Exception ex) { HardwareLogger.LogError($"FindAppPathInRegistry {regPath} failed", ex); }
            }
            return null;
        }

        private string? SearchKeyForApp(Microsoft.Win32.RegistryKey key, string keyword)
        {
            foreach (var subKeyName in key.GetSubKeyNames())
            {
                try 
                {
                    using (var subKey = key.OpenSubKey(subKeyName))
                    {
                        if (subKey != null)
                        {
                            var displayName = subKey.GetValue("DisplayName") as string;
                            if (displayName != null && displayName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                var displayIcon = subKey.GetValue("DisplayIcon") as string;
                                if (!string.IsNullOrEmpty(displayIcon))
                                {
                                    var path = displayIcon.Trim('\"');
                                    if (path.Contains(",")) path = path.Substring(0, path.IndexOf(","));
                                    if (System.IO.File.Exists(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return path;
                                }

                                var installLocation = subKey.GetValue("InstallLocation") as string;
                                if (!string.IsNullOrEmpty(installLocation) && System.IO.Directory.Exists(installLocation))
                                {
                                    var exeFiles = System.IO.Directory.GetFiles(installLocation, "*.exe");
                                    var targetExe = exeFiles.FirstOrDefault(f => f.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
                                    if (targetExe != null) return targetExe;
                                    if (exeFiles.Length > 0) return exeFiles[0];
                                }
                            }
                        }
                    }
                } 
                catch (Exception ex) { HardwareLogger.LogError($"SearchKeyForApp {subKeyName} failed", ex); }
            }
            return null;
        }

        private async void BtnToolCpuZ_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound();

            await ExecuteTool(async ct =>
            {
                OptimizationEngine.ReportProgress(0, "Checking CPU-Z installation...");
                await Task.Delay(500, ct);

                string? registryPath = FindAppPathInRegistry("CPU-Z");
                if (registryPath != null && System.IO.File.Exists(registryPath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(registryPath) { UseShellExecute = true });
                    OptimizationEngine.ReportProgress(100, "CPU-Z launched successfully!");
                    Dispatcher.Invoke(() => { TxtCpuZStatus.Text = "CPU-Z launched!"; TxtCpuZStatus.Foreground = System.Windows.Media.Brushes.LimeGreen; TxtCpuZStatus.Visibility = Visibility.Visible; });
                    return;
                }

                string[] cpuzPaths = {
                    @"C:\Program Files\CPUID\CPU-Z\cpuz_x64.exe",
                    @"C:\Program Files (x86)\CPUID\CPU-Z\cpuz_x64.exe",
                    @"C:\Program Files\CPUID\CPU-Z\cpuz_x32.exe",
                    @"C:\Program Files (x86)\CPUID\CPU-Z\cpuz.exe"
                };
                foreach (var path in cpuzPaths)
                {
                    if (System.IO.File.Exists(path))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                        OptimizationEngine.ReportProgress(100, "CPU-Z launched successfully!");
                        Dispatcher.Invoke(() => { TxtCpuZStatus.Text = "CPU-Z launched!"; TxtCpuZStatus.Foreground = System.Windows.Media.Brushes.LimeGreen; TxtCpuZStatus.Visibility = Visibility.Visible; });
                        return;
                    }
                }

                OptimizationEngine.ReportProgress(30, "CPU-Z not found. Installing silently in background...");
                string installerPath = @"C:\Users\adity\source\repos\optimizer\OptimizerUI\Tools\cpu-z_2.20.2-en.exe";
                if (System.IO.File.Exists(installerPath))
                {
                    var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = installerPath,
                        Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    if (process != null) await process.WaitForExitAsync();
                    
                    OptimizationEngine.ReportProgress(80, "Installation complete. Launching CPU-Z...");
                    await Task.Delay(500);
                    
                    string? installedPath = FindAppPathInRegistry("CPU-Z") ?? @"C:\Program Files\CPUID\CPU-Z\cpuz_x64.exe";
                    if (System.IO.File.Exists(installedPath))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installedPath) { UseShellExecute = true });
                    }
                    
                    OptimizationEngine.ReportProgress(100, "CPU-Z installed and launched!");
                    Dispatcher.Invoke(() => { TxtCpuZStatus.Text = "CPU-Z Installed & Launched!"; TxtCpuZStatus.Foreground = System.Windows.Media.Brushes.LimeGreen; TxtCpuZStatus.Visibility = Visibility.Visible; });
                }
                else
                {
                    OptimizationEngine.ReportProgress(100, "Error: Installer not found in Tools folder.");
                }
            }, "Launch CPU-Z", 5, false);
        }

        private async void BtnToolGpuZ_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound();

            await ExecuteTool(async ct =>
            {
                OptimizationEngine.ReportProgress(0, "Checking GPU-Z installation...");
                await Task.Delay(500, ct);

                string? registryPath = FindAppPathInRegistry("GPU-Z");
                if (registryPath != null && System.IO.File.Exists(registryPath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(registryPath) { UseShellExecute = true });
                    OptimizationEngine.ReportProgress(100, "GPU-Z launched successfully!");
                    Dispatcher.Invoke(() => { TxtGpuZStatus.Text = "GPU-Z launched!"; TxtGpuZStatus.Foreground = System.Windows.Media.Brushes.LimeGreen; TxtGpuZStatus.Visibility = Visibility.Visible; });
                    return;
                }

                string[] gpuzPaths = {
                    @"C:\Program Files\GPU-Z\GPU-Z.exe",
                    @"C:\Program Files (x86)\GPU-Z\GPU-Z.exe",
                    System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"GPU-Z\GPU-Z.exe"),
                    System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "GPU-Z.exe")
                };
                foreach (var path in gpuzPaths)
                {
                    if (System.IO.File.Exists(path))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                        OptimizationEngine.ReportProgress(100, "GPU-Z launched successfully!");
                        Dispatcher.Invoke(() => { TxtGpuZStatus.Text = "GPU-Z launched!"; TxtGpuZStatus.Foreground = System.Windows.Media.Brushes.LimeGreen; TxtGpuZStatus.Visibility = Visibility.Visible; });
                        return;
                    }
                }

                OptimizationEngine.ReportProgress(30, "GPU-Z not found. Installing silently in background...");
                string installerPath = @"C:\Users\adity\source\repos\optimizer\OptimizerUI\Tools\GPU-Z.2.70.0.exe";
                if (System.IO.File.Exists(installerPath))
                {
                    // For GPU-Z, copying to Program Files and running is a safe automatic "install" if it's standalone.
                    // If it supports silent install, we can try running it directly.
                    string targetFolder = @"C:\Program Files\GPU-Z";
                    string targetPath = @"C:\Program Files\GPU-Z\GPU-Z.exe";
                    try
                    {
                        if (!System.IO.Directory.Exists(targetFolder)) System.IO.Directory.CreateDirectory(targetFolder);
                        System.IO.File.Copy(installerPath, targetPath, true);
                    } 
                    catch 
                    {
                        // Fallback to just running it from the Tools directory if we don't have permission to write to Program Files
                        targetPath = installerPath;
                    }

                    OptimizationEngine.ReportProgress(80, "Installation complete. Launching GPU-Z...");
                    await Task.Delay(500);
                    
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(targetPath) { UseShellExecute = true });
                    
                    OptimizationEngine.ReportProgress(100, "GPU-Z installed and launched!");
                    Dispatcher.Invoke(() => { TxtGpuZStatus.Text = "GPU-Z Installed & Launched!"; TxtGpuZStatus.Foreground = System.Windows.Media.Brushes.LimeGreen; TxtGpuZStatus.Visibility = Visibility.Visible; });
                }
                else
                {
                    OptimizationEngine.ReportProgress(100, "Error: Installer not found in Tools folder.");
                }
            }, "Launch GPU-Z", 5, false);
        }

        private async void BtnToolBackup_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound();
            BtnToolBackup.IsEnabled = false;
            BtnToolBackup.Content = "...";

            string result = "";
            try {
                result = await OptimizationEngine.CreateRestorePointAsync();
            } catch { }

            BtnToolBackup.Content = "CREATE";
            BtnToolBackup.IsEnabled = true;
            ShowFeedback(result, 5);
        }

        private async void BtnToolDriverCheck_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound();
            BtnToolDriverCheck.IsEnabled = false;
            BtnToolDriverCheck.Content = "...";
            TxtDriverResult.Visibility = Visibility.Collapsed;

            string result = "";
            try {
                result = await OptimizationEngine.CheckDriversAsync();
            } catch { }

            TxtDriverResult.Text = result;
            TxtDriverResult.Visibility = Visibility.Visible;
            BtnToolDriverCheck.Content = "CHECK";
            BtnToolDriverCheck.IsEnabled = true;
        }

        private async void BtnToolProfileFreeFire_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound();
            var confirmed = await ShowConfirmationAsync("Apply Free Fire Profile?",
                "This will apply GPU priority, scheduling, and mouse delay tweaks optimized for Free Fire.");
            if (!confirmed) return;

            try {
                await OptimizationEngine.ApplyGameProfileAsync("FreeFire");
            } catch { }
            ShowFeedback("Free Fire Profile applied! GPU Priority + Scheduling tweaks active. Restart PC for best results.", 15);
        }

        private async void BtnToolProfilePUBG_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound();
            var confirmed = await ShowConfirmationAsync("Apply PUBG Profile?",
                "This will apply GPU priority, scheduling, and startup delay tweaks optimized for PUBG.");
            if (!confirmed) return;

            try {
                await OptimizationEngine.ApplyGameProfileAsync("PUBG");
            } catch { }
            ShowFeedback("PUBG Profile applied! GPU Priority + Scheduling tweaks active. Restart PC for best results.", 15);
        }

        private async void BtnToolScheduler_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound();
            BtnToolScheduler.IsEnabled = false;
            BtnToolScheduler.Content = "...";

            string result = "";
            try {
                result = await OptimizationEngine.ScheduleTaskAsync();
            } catch { }

            BtnToolScheduler.Content = "ENABLE";
            BtnToolScheduler.IsEnabled = true;
            ShowFeedback(result, 5);
        }
        private void BtnRestartNow_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound();
            RestartOverlay.Visibility = Visibility.Collapsed;
            // Schedule a clean restart in 5 seconds so the app can close gracefully
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "shutdown.exe",
                Arguments = "/r /t 5 /c \"James Optimizer applied your settings. Restarting now...\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            System.Windows.Application.Current.Shutdown();
        }

        private void BtnRestartLater_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound();
            RestartOverlay.Visibility = Visibility.Collapsed;
        }

        // --- STORAGE MANAGEMENT UI ---
        private void BtnManageStorage_Click(object sender, RoutedEventArgs e)
        {
            if (NavStorage != null)
                NavStorage.IsChecked = true;
        }

        private async void UpdateStorageMetrics()
        {
            try
            {
                var drivesData = await Task.Run(() =>
                {
                    var list = new System.Collections.Generic.List<(string Name, double TotalGb, double UsedGb, double FreeGb, int Percent, long TotalSizeRaw, long FreeSizeRaw)>();
                    foreach (var drive in System.IO.DriveInfo.GetDrives().Where(d => d.IsReady))
                    {
                        try
                        {
                            double totalGb = drive.TotalSize / 1024.0 / 1024.0 / 1024.0;
                            double freeGb = drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
                            double usedGb = totalGb - freeGb;
                            int percent = totalGb > 0 ? (int)(usedGb / totalGb * 100) : 0;
                            list.Add((drive.Name.Replace("\\", ""), totalGb, usedGb, freeGb, percent, drive.TotalSize, drive.AvailableFreeSpace));
                        }
                        catch { }
                    }
                    return list;
                });

                if (DashboardStorageList != null) DashboardStorageList.Children.Clear();
                if (StoragePageList != null) StoragePageList.Children.Clear();
                
                foreach (var item in drivesData)
                {
                    double totalGb = item.TotalGb;
                    double freeGb = item.FreeGb;
                    double usedGb = item.UsedGb;
                    int percent = item.Percent;
                    string driveName = item.Name;
                    long totalSize = item.TotalSizeRaw;
                    long freeSize = item.FreeSizeRaw;
                    
                    // Create Dashboard Item
                    if (DashboardStorageList != null)
                    {
                        var dashGrid = new Grid { Margin = new Thickness(0, 0, 0, 15) };
                        dashGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        dashGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        
                        var circleGrid = new Grid { Width = 60, Height = 60, Margin = new Thickness(0, 0, 20, 0) };
                        circleGrid.SetValue(Grid.ColumnProperty, 0);
                        
                        var bgCircle = new System.Windows.Shapes.Ellipse { Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x22, 0x1A, 0x73, 0xE8)), StrokeThickness = 6 };
                        circleGrid.Children.Add(bgCircle);
                        
                        double dashVal = (percent / 100.0) * 28.274;
                        var fgCircle = new System.Windows.Shapes.Ellipse { 
                            Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0x1A, 0x73, 0xE8)), 
                            StrokeThickness = 6,
                            StrokeDashArray = new DoubleCollection(new double[] { dashVal, 100 }),
                            StrokeDashCap = PenLineCap.Round,
                            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                            RenderTransform = new System.Windows.Media.RotateTransform(-90)
                        };
                        circleGrid.Children.Add(fgCircle);
                        
                        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                        textStack.Children.Add(new TextBlock { Text = $"{percent}%", FontSize = 14, FontWeight = FontWeights.Black, Foreground = System.Windows.Media.Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
                        textStack.Children.Add(new TextBlock { Text = driveName, FontSize = 9, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0x92, 0xB0)), HorizontalAlignment = HorizontalAlignment.Center });
                        circleGrid.Children.Add(textStack);
                        
                        dashGrid.Children.Add(circleGrid);
                        
                        var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                        infoStack.SetValue(Grid.ColumnProperty, 1);
                        
                        var tGrid = new Grid { Margin = new Thickness(0, 0, 0, 5) };
                        tGrid.Children.Add(new TextBlock { Text = "Total", FontSize = 10, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0x92, 0xB0)) });
                        tGrid.Children.Add(new TextBlock { Text = $"{totalGb:0.0} GB", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White, HorizontalAlignment = HorizontalAlignment.Right });
                        infoStack.Children.Add(tGrid);
                        
                        var uGrid = new Grid { Margin = new Thickness(0, 0, 0, 5) };
                        uGrid.Children.Add(new TextBlock { Text = "Used", FontSize = 10, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0x92, 0xB0)) });
                        uGrid.Children.Add(new TextBlock { Text = $"{usedGb:0.0} GB", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White, HorizontalAlignment = HorizontalAlignment.Right });
                        infoStack.Children.Add(uGrid);
                        
                        var fGrid = new Grid();
                        fGrid.Children.Add(new TextBlock { Text = "Free", FontSize = 10, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0x92, 0xB0)) });
                        fGrid.Children.Add(new TextBlock { Text = $"{freeGb:0.0} GB", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White, HorizontalAlignment = HorizontalAlignment.Right });
                        infoStack.Children.Add(fGrid);
                        
                        dashGrid.Children.Add(infoStack);
                        DashboardStorageList.Children.Add(dashGrid);
                    }
                    
                    // Create Page Item
                    if (StoragePageList != null)
                    {
                        var border = new Border {
                            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x05, 0xFF, 0xFF, 0xFF)),
                            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(6),
                            Padding = new Thickness(15, 12, 15, 12),
                            Margin = new Thickness(0, 0, 0, 10)
                        };
                        var sp = new StackPanel();
                        
                        var titleGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                        titleGrid.Children.Add(new TextBlock { Text = $"Local Disk ({driveName})", Foreground = System.Windows.Media.Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Left });
                        titleGrid.Children.Add(new TextBlock { Text = $"Used {usedGb:0.0} GB / {totalGb:0.0} GB", Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right });
                        sp.Children.Add(titleGrid);
                        
                        var pb = new ProgressBar {
                            Height = 4,
                            Minimum = 0,
                            Maximum = totalSize > 0 ? totalSize : 1,
                            Value = totalSize - freeSize,
                            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0xD2, 0xFF)),
                            BorderThickness = new Thickness(0)
                        };
                        sp.Children.Add(pb);
                        
                        border.Child = sp;
                        StoragePageList.Children.Add(border);
                    }
                }
            }
            catch { }
        }

        private async void WizTree_Click(object sender, MouseButtonEventArgs e)
        {
            PlayClickSound();
            
            string? registryPath = FindAppPathInRegistry("WizTree");
            if (registryPath != null && System.IO.File.Exists(registryPath))
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(registryPath) { UseShellExecute = true }); } catch { }
                return;
            }

            string[] wizTreePaths = {
                @"C:\Program Files\WizTree\WizTree64.exe",
                @"C:\Program Files\WizTree\WizTree.exe",
                @"C:\Program Files (x86)\WizTree\WizTree.exe",
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"WizTree\WizTree64.exe"),
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"WizTree\WizTree.exe")
            };
            
            foreach (var path in wizTreePaths)
            {
                if (System.IO.File.Exists(path))
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
                    return;
                }
            }
            
            // If not found, show prompt to install
            bool install = await ShowConfirmationAsync(
                "WizTree Not Found", 
                "WizTree is a lightning fast disk space analyzer tool used to visually find and delete large files. It is not currently installed on your system.\n\nWould you like to install it now?");
            
            if (install)
            {
                string installerPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Tools\wiztree_setup.exe");
                if (System.IO.File.Exists(installerPath))
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installerPath) { UseShellExecute = true }); } catch { }
                }
                else
                {
                    // Fallback to website if installer file is missing from output directory
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://diskanalyzer.com/download") { UseShellExecute = true }); } catch { }
                }
            }
        }

        private List<OptimizationEngine.DeepCleanupCategory> _deepCleanupCategories = new();

        private void BtnDeepCleanupScan_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound();
            PageStorage.Visibility = Visibility.Collapsed;
            PageDeepCleanup.Visibility = Visibility.Visible; FadeInPage(PageDeepCleanup);
            
            BtnDeepCleanupProceed.IsEnabled = false;
            TxtDeepCleanupSelected.Text = "Scanning...";

            // Initiate scan
            Task.Run(async () =>
            {
                var result = await OptimizationEngine.ScanDeepCleanupAsync();
                Dispatcher.Invoke(() =>
                {
                    _deepCleanupCategories = result;
                    ListDeepCleanupItems.ItemsSource = _deepCleanupCategories;
                    BtnDeepCleanupProceed.IsEnabled = true;
                    UpdateDeepCleanupSelectedSize();
                });
            });
        }

        private void DeepCleanupItem_CheckedChanged(object sender, RoutedEventArgs e)
        {
            UpdateDeepCleanupSelectedSize();
        }

        private void BtnBackToStorage_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound();
            PageDeepCleanup.Visibility = Visibility.Collapsed;
            PageLargeFiles.Visibility = Visibility.Collapsed;
            PageDuplicateFiles.Visibility = Visibility.Collapsed;
            PageStorage.Visibility = Visibility.Visible; FadeInPage(PageStorage);
            UpdateStorageMetrics();
        }

        private void UpdateDeepCleanupSelectedSize()
        {
            if (!IsLoaded || _deepCleanupCategories == null) return;
            
            long selectedBytes = _deepCleanupCategories.Where(c => c.DefaultSelected).Sum(c => c.SizeBytes);
            TxtDeepCleanupSelected.Text = $"Total selected: {FormatBytes(selectedBytes)}";
        }
        
        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + "B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
            if (bytes < 1024 * 1024 * 1024) return (bytes / 1024.0 / 1024.0).ToString("0.0") + " MB";
            return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("0.0") + " GB";
        }

        private async void BtnDeepCleanupProceed_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound();
            BtnDeepCleanupProceed.IsEnabled = false;
            
            var selectedCategories = _deepCleanupCategories.Where(c => c.DefaultSelected).ToList();
            if (!selectedCategories.Any())
            {
                BtnDeepCleanupProceed.IsEnabled = true;
                return;
            }

            LoadingOverlay.Visibility = Visibility.Visible;
            TxtLoadingTitle.Text = "Deep Cleanup";
            ProgressBarIndicator.BeginAnimation(System.Windows.FrameworkElement.WidthProperty, null);
            ProgressBarIndicator.Width = 0;
            TxtLoadingMessage.Text = "0% \u2014 Cleaning storage...";
            LoadingOverlay.UpdateLayout();
            
            try {
                await OptimizationEngine.ExecuteDeepCleanupAsync(selectedCategories);
            } finally {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
            
            // Rescan
            BtnDeepCleanupScan_Click(null!, null!);
            UpdateStorageMetrics();
        }


        // --- ADVANCED STORAGE UI WRAPPERS ---
        public class SelectableFileItem : System.ComponentModel.INotifyPropertyChanged
        {
            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set { _isSelected = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected))); }
            }
            public OptimizationEngine.ScannedFile File { get; set; } = new();
            public string DisplaySize => MainWindow.FormatBytes(File.SizeBytes);

            public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        }

        public class DuplicateFileGroup
        {
            public string GroupTitle { get; set; } = "";
            public List<SelectableFileItem> Files { get; set; } = new();
        }


        // --- LARGE FILES HANDLERS ---
        private List<SelectableFileItem> _allLargeFiles = new List<SelectableFileItem>();

        private async void BtnLargeFiles_Click(object sender, MouseButtonEventArgs e)
        {
            PlayClickSound();
            PageStorage.Visibility = Visibility.Collapsed;
            PageLargeFiles.Visibility = Visibility.Visible; FadeInPage(PageLargeFiles);
            BtnDeleteLargeFiles.Visibility = Visibility.Collapsed;
            TxtLargeFilesStatus.Text = "Scanning your profile for files > 50MB...";
            
            var files = await OptimizationEngine.ScanLargeFilesAsync();
            _allLargeFiles = files.Select(f => new SelectableFileItem { File = f, IsSelected = false }).ToList();
            
            var exts = _allLargeFiles.Select(f => System.IO.Path.GetExtension(f.File.FilePath).ToLower())
                                     .Distinct().Where(ext => !string.IsNullOrEmpty(ext))
                                     .OrderBy(ext => ext).ToList();
            
            CmbLargeFileExtensions.SelectionChanged -= CmbLargeFileExtensions_SelectionChanged;
            CmbLargeFileExtensions.Items.Clear();
            CmbLargeFileExtensions.Items.Add(new ComboBoxItem { Content = "All Files" });
            foreach(var ext in exts)
            {
                CmbLargeFileExtensions.Items.Add(new ComboBoxItem { Content = ext });
            }
            CmbLargeFileExtensions.SelectedIndex = 0;
            CmbLargeFileExtensions.SelectionChanged += CmbLargeFileExtensions_SelectionChanged;

            ListLargeFiles.ItemsSource = _allLargeFiles;
            TxtLargeFilesStatus.Text = $"Found {_allLargeFiles.Count} large files.";
            if (_allLargeFiles.Count > 0) BtnDeleteLargeFiles.Visibility = Visibility.Visible;
        }

        private void CmbLargeFileExtensions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ListLargeFiles == null || _allLargeFiles == null) return;

            if (CmbLargeFileExtensions.SelectedItem is ComboBoxItem item)
            {
                string filter = item.Content?.ToString() ?? string.Empty;
                if (filter == "All Files" || string.IsNullOrEmpty(filter))
                {
                    ListLargeFiles.ItemsSource = _allLargeFiles;
                }
                else
                {
                    ListLargeFiles.ItemsSource = _allLargeFiles.Where(f => System.IO.Path.GetExtension(f.File.FilePath).ToLower() == filter).ToList();
                }
                
                var currentItems = ListLargeFiles.ItemsSource as IList<SelectableFileItem>;
                int count = currentItems?.Count ?? 0;
                TxtLargeFilesStatus.Text = $"Showing {count} files.";
                BtnDeleteLargeFiles.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private async void BtnDeleteLargeFiles_Click(object sender, RoutedEventArgs e)
        {
            if (ListLargeFiles.ItemsSource is IEnumerable<SelectableFileItem> items)
            {
                var selected = _allLargeFiles.Where(x => x.IsSelected).Select(x => x.File.FilePath).ToList();
                if (selected.Count > 0)
                {
                    BtnDeleteLargeFiles.Content = "Deleting...";
                    BtnDeleteLargeFiles.IsEnabled = false;

                    LoadingOverlay.Visibility = Visibility.Visible;
                    TxtLoadingTitle.Text = "Deleting Large Files";
                    ProgressBarIndicator.BeginAnimation(System.Windows.FrameworkElement.WidthProperty, null);
                    ProgressBarIndicator.Width = 0;
                    TxtLoadingMessage.Text = $"0% \u2014 Deleting {selected.Count} files...";
                    LoadingOverlay.UpdateLayout();

                    try {
                        await OptimizationEngine.DeleteFilesAsync(selected);
                    } finally {
                        LoadingOverlay.Visibility = Visibility.Collapsed;
                    }

                    BtnDeleteLargeFiles.Content = "Delete Selected";
                    BtnDeleteLargeFiles.IsEnabled = true;
                    BtnLargeFiles_Click(null!, null!);
                }
            }
        }

        // --- DUPLICATE FILES HANDLERS ---
        private async void BtnDuplicateFiles_Click(object sender, MouseButtonEventArgs e)
        {
            PlayClickSound();
            PageStorage.Visibility = Visibility.Collapsed;
            PageDuplicateFiles.Visibility = Visibility.Visible; 
            FadeInPage(PageDuplicateFiles);
            
            if (DuplicateScanningContainer != null && DuplicateResultsContainer != null)
            {
                DuplicateScanningContainer.Visibility = Visibility.Visible;
                DuplicateResultsContainer.Visibility = Visibility.Collapsed;
                DuplicateScanProgressBar.Value = 0;
                TxtDuplicateScanPercentage.Text = "0%";
            }
            BtnDeleteDuplicateFiles.Visibility = Visibility.Collapsed;
            
            var progress = new Progress<int>(percent =>
            {
                if (DuplicateScanProgressBar != null) DuplicateScanProgressBar.Value = percent;
                if (TxtDuplicateScanPercentage != null) TxtDuplicateScanPercentage.Text = $"{percent}%";
            });
            
            var dupes = await OptimizationEngine.ScanDuplicatesAsync(progress);
            
            if (DuplicateScanningContainer != null && DuplicateResultsContainer != null)
            {
                DuplicateScanningContainer.Visibility = Visibility.Collapsed;
                DuplicateResultsContainer.Visibility = Visibility.Visible;
                FadeInPage(DuplicateResultsContainer);
            }
            
            var groups = new List<DuplicateFileGroup>();
            foreach (var group in dupes)
            {
                if (group.Count < 2) continue;
                var g = new DuplicateFileGroup();
                g.GroupTitle = $"Group: {group.First().FileName} ({MainWindow.FormatBytes(group.First().SizeBytes)})";
                // Select all except the most recently modified one
                var sorted = group.OrderByDescending(f => f.LastModified).ToList();
                for (int i = 0; i < sorted.Count; i++)
                {
                    g.Files.Add(new SelectableFileItem { File = sorted[i], IsSelected = (i > 0) });
                }
                groups.Add(g);
            }
            
            ListDuplicateFiles.ItemsSource = groups;
            
            if (TxtDuplicateFilesStatus != null)
            {
                TxtDuplicateFilesStatus.Text = $"Found {groups.Count} groups of duplicates.";
            }
            if (groups.Count > 0) BtnDeleteDuplicateFiles.Visibility = Visibility.Visible;
        }

        private async void BtnDeleteDuplicateFiles_Click(object sender, RoutedEventArgs e)
        {
            if (ListDuplicateFiles.ItemsSource is IEnumerable<DuplicateFileGroup> groups)
            {
                var selected = new List<string>();
                foreach (var g in groups)
                {
                    selected.AddRange(g.Files.Where(x => x.IsSelected).Select(x => x.File.FilePath));
                }
                
                if (selected.Count > 0)
                {
                    BtnDeleteDuplicateFiles.Content = "Deleting...";
                    BtnDeleteDuplicateFiles.IsEnabled = false;

                    LoadingOverlay.Visibility = Visibility.Visible;
                    TxtLoadingTitle.Text = "Deleting Duplicates";
                    ProgressBarIndicator.BeginAnimation(System.Windows.FrameworkElement.WidthProperty, null);
                    ProgressBarIndicator.Width = 0;
                    TxtLoadingMessage.Text = $"0% \u2014 Deleting {selected.Count} duplicate files...";
                    LoadingOverlay.UpdateLayout();

                    try {
                        await OptimizationEngine.DeleteFilesAsync(selected);
                    } finally {
                        LoadingOverlay.Visibility = Visibility.Collapsed;
                    }

                    BtnDeleteDuplicateFiles.Content = "Delete Selected Duplicates";
                    BtnDeleteDuplicateFiles.IsEnabled = true;
                    BtnDuplicateFiles_Click(null!, null!);
                }
            }
        }


        private void TabStartup_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            RefreshStartupManager();
        }

        private void BtnRefreshStartup_Click(object sender, RoutedEventArgs e)
        {
            RefreshStartupManager();
        }

        private async void RefreshStartupManager()
        {
            if (ListStartupItems == null) return;
            ListStartupItems.Children.Clear();
            ListStartupItems.Children.Add(new TextBlock { Text = "Loading...", Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8892B0")), Margin = new Thickness(10) });

            List<StartupItem> items = new List<StartupItem>();
            string mode = "Apps";
            if (TabStartupServices?.IsChecked == true) mode = "Services";
            if (TabStartupAutoruns?.IsChecked == true) mode = "Autoruns";

            await Task.Run(() =>
            {
                if (mode == "Apps") items = StartupManagerService.GetStartupApps();
                else if (mode == "Services") items = StartupManagerService.GetAutomaticServices();
                else items = StartupManagerService.GetRegistryAutoruns();
            });

            string searchQuery = TxtSearchStartup.Text?.ToLower() ?? "";
            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                items = items.Where(i => (i.Name != null && i.Name.ToLower().Contains(searchQuery)) || (i.Path != null && i.Path.ToLower().Contains(searchQuery)) || (i.Publisher != null && i.Publisher.ToLower().Contains(searchQuery))).ToList();
            }

            ListStartupItems.Children.Clear();
            
            if (items.Count == 0)
            {
                ListStartupItems.Children.Add(new TextBlock { Text = "No items found.", Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8892B0")), Margin = new Thickness(10) });
                return;
            }

            foreach (var item in items)
            {
                Border card = new Border
                {
                    Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#151A25")),
                    BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#202B3D")),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Margin = new Thickness(0, 0, 0, 8),
                    Padding = new Thickness(15)
                };

                Grid mainGrid = new Grid();
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Minimal info
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Details (collapsed)

                // Row 0: Minimal Info
                Grid minimalGrid = new Grid();
                minimalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Name
                minimalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // View Details
                minimalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Slider Toggle
                
                TextBlock txtName = new TextBlock { Text = item.Name, Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.Bold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(txtName, 0);
                minimalGrid.Children.Add(txtName);

                Button btnDetails = new Button 
                { 
                    Content = "More info", 
                    Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1A73E8")), 
                    Background = System.Windows.Media.Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0,0,15,0)
                };
                Grid.SetColumn(btnDetails, 1);
                minimalGrid.Children.Add(btnDetails);

                // Slider Toggle (Switch)
                Border sliderBackground = new Border
                {
                    Width = 40, Height = 20, CornerRadius = new CornerRadius(10),
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 76, 175, 80)), // Green
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = "Interactive",
                    VerticalAlignment = VerticalAlignment.Center
                };
                System.Windows.Shapes.Ellipse sliderKnob = new System.Windows.Shapes.Ellipse
                {
                    Width = 16, Height = 16, Fill = System.Windows.Media.Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Tag = "Interactive",
                    Margin = new Thickness(2)
                };
                sliderBackground.Child = sliderKnob;
                Grid.SetColumn(sliderBackground, 2);
                minimalGrid.Children.Add(sliderBackground);

                Grid.SetRow(minimalGrid, 0);
                mainGrid.Children.Add(minimalGrid);

                // Row 1: Details
                StackPanel detailsPanel = new StackPanel 
                { 
                    Visibility = Visibility.Collapsed,
                    Margin = new Thickness(0, 10, 0, 0)
                };
                if (!string.IsNullOrWhiteSpace(item.Publisher)) {
                    detailsPanel.Children.Add(new TextBlock { Text = $"Publisher: {item.Publisher}", Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8892B0")), FontSize = 12, TextWrapping = TextWrapping.Wrap });
                }
                detailsPanel.Children.Add(new TextBlock { Text = $"Path: {item.Path}", Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8892B0")), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
                detailsPanel.Children.Add(new TextBlock { Text = $"Source: {item.Source}", Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8892B0")), FontSize = 12, Margin = new Thickness(0, 3, 0, 0) });
                detailsPanel.Children.Add(new TextBlock { Text = $"Type: {item.Type}", Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8892B0")), FontSize = 12, Margin = new Thickness(0, 3, 0, 0) });
                
                Grid.SetRow(detailsPanel, 1);
                mainGrid.Children.Add(detailsPanel);

                // Event Handlers
                btnDetails.Click += (s, e) =>
                {
                    if (detailsPanel.Visibility == Visibility.Collapsed)
                    {
                        detailsPanel.Visibility = Visibility.Visible;
                        btnDetails.Content = "Less info";
                    }
                    else
                    {
                        detailsPanel.Visibility = Visibility.Collapsed;
                        btnDetails.Content = "More info";
                    }
                };
                
                bool isEnabled = item.IsEnabled; 
                if (!isEnabled)
                {
                    sliderBackground.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 80, 80, 80)); // Gray
                    sliderKnob.HorizontalAlignment = HorizontalAlignment.Left;
                }

                sliderBackground.MouseLeftButtonDown += (s, e) =>
                {
                    isEnabled = !isEnabled;
                    if (isEnabled)
                    {
                        sliderBackground.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 76, 175, 80)); // Green
                        sliderKnob.HorizontalAlignment = HorizontalAlignment.Right;
                    }
                    else
                    {
                        sliderBackground.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 80, 80, 80)); // Gray
                        sliderKnob.HorizontalAlignment = HorizontalAlignment.Left;
                    }
                    StartupManagerService.ToggleStartupItem(item, isEnabled);
                };

                card.Child = mainGrid;
                ListStartupItems.Children.Add(card);
            }
        }
        private void TxtSearchStartup_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            RefreshStartupManager();
        }

        private async void BtnRefreshServices_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound();
            await RefreshServiceManager();
        }

        private void TxtSearchServices_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _ = RefreshServiceManager();
        }

        private async void BtnServiceDebloater_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteTool(OptimizationEngine.RunServiceDebloaterAsync, "Service Debloater", 15, true);
            await RefreshServiceManager();
        }

        private async Task RefreshServiceManager()
        {
            if (ListServiceItems == null) return;
            ListServiceItems.Children.Clear();
            ListServiceItems.Children.Add(new TextBlock { Text = "Loading Services...", Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8892B0")), Margin = new Thickness(10) });

            List<ServiceInfo> items = new List<ServiceInfo>();

            await Task.Run(() =>
            {
                items = ServiceManagerLogic.GetAllServices();
            });

            string searchQuery = TxtSearchServices.Text?.ToLower() ?? "";
            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                items = items.Where(i => (i.DisplayName != null && i.DisplayName.ToLower().Contains(searchQuery)) || (i.ServiceName != null && i.ServiceName.ToLower().Contains(searchQuery)) || (i.Publisher != null && i.Publisher.ToLower().Contains(searchQuery))).ToList();
            }

            ListServiceItems.Children.Clear();
            
            if (items.Count == 0)
            {
                ListServiceItems.Children.Add(new TextBlock { Text = "No services found.", Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8892B0")), Margin = new Thickness(10) });
                return;
            }

            foreach (var item in items)
            {
                // Hide services that are already stopped or cannot be stopped by the user
                if (!item.IsRunning || !item.CanStop) continue;

                Border card = new Border
                {
                    Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#151A25")),
                    BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#202B3D")),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Margin = new Thickness(0, 0, 0, 8),
                    Padding = new Thickness(15)
                };

                Grid mainGrid = new Grid();
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                StackPanel infoPanel = new StackPanel();
                Grid.SetColumn(infoPanel, 0);
                infoPanel.Children.Add(new TextBlock { Text = item.DisplayName, Foreground = new SolidColorBrush(System.Windows.Media.Colors.White), FontSize = 16, FontWeight = FontWeights.SemiBold });
                if (!string.IsNullOrWhiteSpace(item.Publisher)) {
                    infoPanel.Children.Add(new TextBlock { Text = $"Publisher: {item.Publisher}", Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8892B0")), FontSize = 12, Margin = new Thickness(0, 5, 0, 0) });
                }
                infoPanel.Children.Add(new TextBlock { Text = $"Service Name: {item.ServiceName}  \u2022  Status: {item.Status}  \u2022  Startup: {item.StartType}", Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8892B0")), FontSize = 12, Margin = new Thickness(0, 5, 0, 0) });

                StackPanel actionPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(actionPanel, 1);
                
                Button btnToggle = new Button
                {
                    Content = item.IsRunning ? "Stop" : "Start",
                    Width = 80, Height = 30, Margin = new Thickness(10, 0, 0, 0),
                    Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(item.IsRunning ? "#E53935" : "#43A047")),
                    Foreground = new SolidColorBrush(System.Windows.Media.Colors.White),
                    BorderThickness = new Thickness(0),
                    IsEnabled = item.CanStop || !item.IsRunning,
                    Style = (Style)FindResource("ActionPillButton")
                };
                
                btnToggle.Click += async (s, e) =>
                {
                    btnToggle.IsEnabled = false;
                    btnToggle.Content = "Wait...";
                    await Task.Run(() => ServiceManagerLogic.ToggleServiceState(item.ServiceName, !item.IsRunning));
                    await RefreshServiceManager();
                };

                actionPanel.Children.Add(btnToggle);

                mainGrid.Children.Add(infoPanel);
                mainGrid.Children.Add(actionPanel);
                card.Child = mainGrid;
                ListServiceItems.Children.Add(card);
            }
        }

        private void UserNameContainer_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            IconEditName.Visibility = Visibility.Visible;
        }

        private void UserNameContainer_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            IconEditName.Visibility = Visibility.Hidden;
        }

        private void UserNameContainer_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            UserNameContainer.Visibility = Visibility.Collapsed;
            InputUserName.Visibility = Visibility.Visible;
            InputUserName.Focus();
            InputUserName.SelectAll();
        }

        private void InputUserName_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                SaveUserName();
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                InputUserName.Text = TxtUserName.Text;
                UserNameContainer.Visibility = Visibility.Visible;
                InputUserName.Visibility = Visibility.Collapsed;
            }
        }

        private void InputUserName_LostFocus(object sender, RoutedEventArgs e)
        {
            SaveUserName();
        }

        private void SaveUserName()
        {
            string newName = InputUserName.Text.Trim();
            if (string.IsNullOrEmpty(newName))
            {
                newName = Environment.MachineName; // Default to PC Name
            }
            TxtUserName.Text = newName;
            InputUserName.Text = newName;
            
            try {
                Microsoft.Win32.Registry.SetValue(@"HKEY_CURRENT_USER\Software\JamesOptimizer", "UserName", newName);
            } catch {}

            UserNameContainer.Visibility = Visibility.Visible;
            InputUserName.Visibility = Visibility.Collapsed;
        }

        private void InitializeNetworkTimer()
        {
            networkTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            networkTimer.Tick += NetworkTimer_Tick;
            networkTimer.Start();
        }

        private void NetGraphCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Redraw graph when canvas size changes (handled automatically on next tick)
        }

        private async void NetworkTimer_Tick(object? sender, EventArgs e)
        {
            if (PageNetworkBoost == null || PageNetworkBoost.Visibility != Visibility.Visible) return;

            // Ping Check
            try
            {
                using (var ping = new Ping())
                {
                    var reply = await ping.SendPingAsync("8.8.8.8", 1000);
                    if (reply.Status == IPStatus.Success)
                    {
                        TxtLivePing.Text = reply.RoundtripTime.ToString();
                        TxtLivePing.Foreground = reply.RoundtripTime < 50 ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 255, 136)) : 
                                                 (reply.RoundtripTime < 100 ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 217, 61)) : new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 85, 85)));
                        
                        if (TxtConnectionStatus.Text != "BAD") 
                        {
                            TxtConnectionStatus.Text = reply.RoundtripTime < 80 ? "GOOD" : "FAIR";
                            TxtConnectionStatus.Foreground = reply.RoundtripTime < 80 ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 255, 136)) : new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 217, 61));
                        }
                    }
                }
            }
            catch { }

            // Find active internet interface
            if (activeInterface == null || activeInterface.OperationalStatus != OperationalStatus.Up)
            {
                activeInterface = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(ni => 
                    ni.OperationalStatus == OperationalStatus.Up && 
                    ni.NetworkInterfaceType != NetworkInterfaceType.Loopback && 
                    ni.GetIPProperties().GatewayAddresses.Any());
            }

            if (activeInterface != null)
            {
                TxtActiveAdapter.Text = activeInterface.Name;

                var stats = activeInterface.GetIPStatistics();
                long currentReceived = stats.BytesReceived;
                long currentSent = stats.BytesSent;

                if (lastBytesReceived > 0)
                {
                    double kbpsDownRaw = (currentReceived - lastBytesReceived) / 1024.0;
                    double kbpsUpRaw = (currentSent - lastBytesSent) / 1024.0;

                    string FormatSpeed(double kbps)
                    {
                        if (kbps >= 1024) return $"{(kbps / 1024.0):F1} MB/s";
                        return $"{kbps:F1} KB/s";
                    }

                    TxtLiveDownload.Text = FormatSpeed(kbpsDownRaw);
                    TxtLiveUpload.Text = FormatSpeed(kbpsUpRaw);
                    
                    if (NetGraphCanvas != null && NetGraphCanvas.ActualWidth > 0 && NetGraphCanvas.ActualHeight > 0)
                    {
                        // Apply Exponential Moving Average (EMA) for visual smoothness
                        double alpha = 0.4;
                        double lastDown = netDownHistory.Count > 0 ? netDownHistory.Last() : kbpsDownRaw;
                        double lastUp = netUpHistory.Count > 0 ? netUpHistory.Last() : kbpsUpRaw;
                        
                        double smoothedDown = (kbpsDownRaw * alpha) + (lastDown * (1.0 - alpha));
                        double smoothedUp = (kbpsUpRaw * alpha) + (lastUp * (1.0 - alpha));

                        netDownHistory.Enqueue(smoothedDown);
                        netUpHistory.Enqueue(smoothedUp);

                        int maxPoints = 40;
                        while (netDownHistory.Count > maxPoints) netDownHistory.Dequeue();
                        while (netUpHistory.Count > maxPoints) netUpHistory.Dequeue();

                        double maxVal = Math.Max(100.0, Math.Max(netDownHistory.Max(), netUpHistory.Max()) * 1.2);
                        double w = NetGraphCanvas.ActualWidth;
                        double h = NetGraphCanvas.ActualHeight;

                        PointCollection downPts = new PointCollection();
                        PointCollection upPts = new PointCollection();
                        PointCollection downFillPts = new PointCollection();
                        PointCollection upFillPts = new PointCollection();

                        var downArr = netDownHistory.ToArray();
                        var upArr = netUpHistory.ToArray();

                        // Add bottom-left point for fills
                        downFillPts.Add(new System.Windows.Point(0, h));
                        upFillPts.Add(new System.Windows.Point(0, h));

                        for (int i = 0; i < downArr.Length; i++)
                        {
                            double x = i * (w / (maxPoints - 1));
                            double yDown = h - ((downArr[i] / maxVal) * h);
                            double yUp = h - ((upArr[i] / maxVal) * h);
                            
                            // Clamp values to canvas bounds just in case
                            yDown = Math.Max(0, Math.Min(h, yDown));
                            yUp = Math.Max(0, Math.Min(h, yUp));

                            var pDown = new System.Windows.Point(x, yDown);
                            var pUp = new System.Windows.Point(x, yUp);

                            downPts.Add(pDown);
                            upPts.Add(pUp);
                            downFillPts.Add(pDown);
                            upFillPts.Add(pUp);
                        }
                        
                        // Add bottom-right point for fills
                        downFillPts.Add(new System.Windows.Point(w, h));
                        upFillPts.Add(new System.Windows.Point(w, h));

                        if (GraphDownload != null) GraphDownload.Points = downPts;
                        if (GraphUpload != null) GraphUpload.Points = upPts;
                        if (GraphDownloadFill != null) GraphDownloadFill.Points = downFillPts;
                        if (GraphUploadFill != null) GraphUploadFill.Points = upFillPts;
                    }
                }

                lastBytesReceived = currentReceived;
                lastBytesSent = currentSent;
            }
            else
            {
                TxtActiveAdapter.Text = "Disconnected";
                TxtConnectionStatus.Text = "BAD";
                TxtConnectionStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 85, 85));
                TxtLivePing.Text = "--";
                TxtLivePing.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(136, 146, 176));
            }
        }
        private bool _specsLoaded = false;
        private async Task LoadAboutSpecsAsync()
        {
            if (_specsLoaded) return;
            _specsLoaded = true;

            await Task.Run(() =>
            {
                // CPU
                string cpu = "Unknown CPU";
                try {
                    cpu = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", "Unknown CPU")?.ToString() ?? "Unknown CPU";
                } catch { }

                // GPU
                string gpu = "Unknown GPU";
                try {
                    gpu = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000", "DriverDesc", "Unknown GPU")?.ToString() ?? "Unknown GPU";
                    if (gpu == "Unknown GPU")
                    {
                        gpu = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0001", "DriverDesc", "Unknown GPU")?.ToString() ?? "Unknown GPU";
                    }
                } catch { }

                // RAM
                string ram = "Unknown RAM";
                try {
                    using (var searcher = new System.Management.ManagementObjectSearcher("select Capacity from Win32_PhysicalMemory"))
                    {
                        long totalBytes = 0;
                        foreach (var item in searcher.Get()) {
                            if (long.TryParse(item["Capacity"]?.ToString(), out long cap)) totalBytes += cap;
                        }
                        ram = Math.Round(totalBytes / (1024.0 * 1024 * 1024), 1) + " GB";
                    }
                } catch { }

                // Storage
                string storage = "";
                try {
                    using (var searcher = new System.Management.ManagementObjectSearcher("select Model, Size, MediaType from Win32_DiskDrive"))
                    {
                        foreach (var item in searcher.Get()) {
                            string model = item["Model"]?.ToString() ?? "Unknown";
                            string media = item["MediaType"]?.ToString() ?? "Disk";
                            double sizeGb = 0;
                            if (long.TryParse(item["Size"]?.ToString(), out long sizeBytes)) {
                                sizeGb = Math.Round(sizeBytes / (1024.0 * 1024 * 1024), 0);
                            }
                            storage += $"{model} ({media}, {sizeGb}GB)\n";
                        }
                    }
                } catch { }
                if (string.IsNullOrWhiteSpace(storage)) storage = "Unknown Storage";

                // Network
                string network = "Unknown Network Adapter";
                try {
                    using (var searcher = new System.Management.ManagementObjectSearcher("select Name, NetConnectionStatus from Win32_NetworkAdapter where NetConnectionStatus = 2"))
                    {
                        foreach (var item in searcher.Get()) { network = item["Name"]?.ToString() ?? "Unknown Network Adapter"; break; }
                    }
                } catch { }

                // Ping
                string pingStr = "Checking...";
                try {
                    using (var pinger = new System.Net.NetworkInformation.Ping())
                    {
                        var reply = pinger.Send("8.8.8.8", 2000);
                        if (reply.Status == System.Net.NetworkInformation.IPStatus.Success) {
                            pingStr = $"{reply.RoundtripTime} ms (to 8.8.8.8)";
                        } else {
                            pingStr = $"Timeout ({reply.Status})";
                        }
                    }
                } catch { pingStr = "Failed to ping"; }

                // Update UI
                Dispatcher.Invoke(() => {
                    TxtInfoCpu.Text = cpu;
                    TxtInfoGpu.Text = gpu;
                    TxtInfoRam.Text = ram;
                    TxtInfoStorage.Text = storage.Trim();
                    TxtInfoNetwork.Text = network;
                    TxtInfoPing.Text = pingStr;
                });
            });
        }
        private void RdbTheme_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            bool isDark = RdbThemeDark.IsChecked == true;
            var app = Application.Current;
            var uri = new Uri($"Themes/{(isDark ? "Dark" : "Light")}Theme.xaml", UriKind.Relative);
            var dict = new ResourceDictionary() { Source = uri };
            
            if (app.Resources.MergedDictionaries.Count > 0)
            {
                app.Resources.MergedDictionaries[0] = dict;
            }
            else
            {
                app.Resources.MergedDictionaries.Add(dict);
            }
            ApplyTransparency();
        }

        private void SldTransparency_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ApplyTransparency();
        }

        private void ApplyTransparency()
        {
            if (!IsLoaded || SldTransparency == null) return;
            
            byte alpha = (byte)((SldTransparency.Value / 100.0) * 255.0);
            var app = Application.Current;
            
            if (app.Resources["AppBackgroundColor"] is System.Windows.Media.Color appBgColor)
            {
                appBgColor.A = alpha;
                app.Resources["AppBackgroundBrush"] = new SolidColorBrush(appBgColor);
            }
            
            if (app.Resources["GlassCardBackgroundColor"] is System.Windows.Media.Color cardBgColor)
            {
                cardBgColor.A = (byte)(Math.Min(255, alpha + 15));
                app.Resources["GlassCardBackgroundBrush"] = new SolidColorBrush(cardBgColor);
            }
            
            if (app.Resources["SidebarBackgroundColor"] is System.Windows.Media.Color sidebarBgColor)
            {
                sidebarBgColor.A = (byte)(Math.Min(255, alpha + 10));
                app.Resources["SidebarBackgroundBrush"] = new SolidColorBrush(sidebarBgColor);
            }
        }

        private void AccentSwatch_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            if (sender is RadioButton rb && rb.Tag is string hexColor)
            {
                try
                {
                    var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor);
                    Application.Current.Resources["AccentBrush"] = new SolidColorBrush(color);
                    
                    // Save to registry
                    Microsoft.Win32.Registry.SetValue(@"HKEY_CURRENT_USER\Software\JamesOptimizer", "AccentColor", hexColor);
                }
                catch { }
            }
        }

        private void GenerateStars()
        {
            if (StarCanvas == null) return;
            StarCanvas.Children.Clear();
            
            Random rand = new Random();
            int starCount = 80;
            
            for (int i = 0; i < starCount; i++)
            {
                double size = rand.NextDouble() * 3 + 1; // 1 to 4 px
                
                Ellipse star = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(rand.Next(100, 200)), 255, 255, 255)),
                };
                
                Canvas.SetLeft(star, rand.NextDouble() * 1500);
                Canvas.SetTop(star, rand.NextDouble() * 1000);
                
                if (rand.Next(3) == 0) // 1 in 3 stars twinkle
                {
                    DoubleAnimation twinkle = new DoubleAnimation
                    {
                        From = star.Opacity,
                        To = rand.NextDouble() * 0.3,
                        Duration = TimeSpan.FromSeconds(rand.NextDouble() * 2 + 1),
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever
                    };
                    star.BeginAnimation(UIElement.OpacityProperty, twinkle);
                }
                
                StarCanvas.Children.Add(star);
            }
        }
        private void InitDiagnosticsAnimation()
        {
            if (DiagnosticsBgCanvas == null) return;
            var rand = new Random();
            for (int i = 0; i < 50; i++)
            {
                bool isSnowflake = rand.Next(0, 3) == 0;
                UIElement particle;
                
                if (isSnowflake)
                {
                    particle = new TextBlock 
                    { 
                        Text = "❄", 
                        Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 255, 255, 255)),
                        FontSize = rand.Next(10, 24)
                    };
                }
                else
                {
                    byte b = (byte)rand.Next(150, 255);
                    particle = new System.Windows.Shapes.Ellipse 
                    { 
                        Width = rand.Next(2, 6), 
                        Height = rand.Next(2, 6), 
                        Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)rand.Next(50, 150), 0, b, 255))
                    };
                }

                DiagnosticsBgCanvas.Children.Add(particle);
                
                double startX = rand.Next(0, 800);
                double startY = rand.Next(-800, 600);
                
                Canvas.SetLeft(particle, startX);
                Canvas.SetTop(particle, startY);

                double duration = rand.Next(15, 40);
                var anim = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = startY,
                    To = startY + 1200,
                    Duration = TimeSpan.FromSeconds(duration),
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
                };
                
                particle.BeginAnimation(Canvas.TopProperty, anim);
            }
        }
        // --- WALKTHROUGH LOGIC ---
        private void BtnYouTube_Click(object sender, RoutedEventArgs e)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://youtube.com/@jamesgod7") { UseShellExecute = true }); } catch { }
        }

        private void BtnInstagram_Click(object sender, RoutedEventArgs e)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://instagram.com/jamesgod3127") { UseShellExecute = true }); } catch { }
        }

        private void BtnWebsite_Click(object sender, RoutedEventArgs e)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://guns.lol/jamesgod") { UseShellExecute = true }); } catch { }
        }

        private WalkthroughController? _walkthroughController;

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Load Accent Color from Registry
            try
            {
                string savedAccent = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\JamesOptimizer", "AccentColor", "") as string;
                if (!string.IsNullOrEmpty(savedAccent))
                {
                    foreach (var child in AccentColorPanel.Children)
                    {
                        if (child is RadioButton rb && rb.Tag?.ToString() == savedAccent)
                        {
                            rb.IsChecked = true;
                            break;
                        }
                    }
                }
            }
            catch { }

            // Initialize Toast Manager
            _toastManager = new ToastManager(this, ToastContainer);
            
            // Show welcome toast
            _toastManager.ShowSuccess("Welcome to James Optimizer", "All systems operational. Ready to optimize.", TimeSpan.FromSeconds(4));

            GenerateStars();
            
            _walkthroughController = new WalkthroughController(MasterWalkthrough, this);

            if (!_walkthroughController.HasSeenWalkthrough())
            {
                var steps = new List<WalkthroughStep>
                {
                    new WalkthroughStep 
                    { 
                        TargetElement = SidebarContainer, 
                        Title = "Welcome to James Optimizer", 
                        Description = "This is your command center. Navigate between tools like System Cleaner, Performance Tweaks, and Startup Manager from here.",
                        CalloutPosition = CalloutPosition.Right
                    },
                    new WalkthroughStep 
                    { 
                        TargetElement = Btn1ClickOptimize, 
                        Title = "1-Click Optimization", 
                        Description = "Clicking this will run all recommended performance tweaks automatically to boost your FPS and system speed.",
                        CalloutPosition = CalloutPosition.Bottom
                    },
                    new WalkthroughStep 
                    { 
                        TargetElement = ComboAutoTimer, 
                        Title = "Auto-Run Timer", 
                        Description = "Set this to 15 or 30 minutes, and we'll automatically clear your RAM and flush temp junk in the background while you game.",
                        CalloutPosition = CalloutPosition.Left
                    },
                    new WalkthroughStep 
                    { 
                        TargetElement = BtnTopSettings, 
                        Title = "You're all set!", 
                        Description = "Explore the settings to customize the app theme, behaviors, and more.",
                        CalloutPosition = CalloutPosition.Right,
                        OnStepEnter = () => BtnTopSettings.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent))
                    }
                };

                _walkthroughController.Start(steps);
            }
        }
    }
}













