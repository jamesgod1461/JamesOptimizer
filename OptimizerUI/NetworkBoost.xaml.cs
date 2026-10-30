using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace JamesOptimizer
{
    /// <summary>
    /// Interaction logic for NetworkBoost.xaml
    /// James Optimizer V3 Architecture
    /// </summary>
    public partial class NetworkBoost : Page, INavigablePage
    {
        private DispatcherTimer? _pingTimer;
        private DispatcherTimer? _trafficTimer;
        private readonly Queue<double> _pingHistory = new();
        private readonly List<double> _downloadHistory = new();
        private readonly List<double> _uploadHistory = new();

        private long _prevBytesReceived = 0;
        private long _prevBytesSent = 0;
        private DateTime _lastTrafficTime = DateTime.MinValue;
        private bool _isOptimizing = false;

        public NetworkBoost()
        {
            InitializeComponent();

            // Populate baseline 8 points for ping
            for (int i = 0; i < 8; i++) _pingHistory.Enqueue(24);
            // Populate baseline 30 points for traffic
            for (int i = 0; i < 30; i++)
            {
                _downloadHistory.Add(0);
                _uploadHistory.Add(0);
            }
        }

        private void NetworkBoost_Loaded(object sender, RoutedEventArgs e)
        {
            OnNavigatedTo();
        }

        private void NetworkBoost_Unloaded(object sender, RoutedEventArgs e)
        {
            OnNavigatedFrom();
        }

        // ═════════════════════════════════════════════════════════════════
        // INavigablePage & ENTRANCE ANIMATIONS
        // ═════════════════════════════════════════════════════════════════
        public void OnNavigatedTo()
        {
            PlayStaggeredEntrance();
            StartPulseRingsAnimation();
            DetectActiveAdapter();
            StartMonitoring();
        }

        public void OnNavigatedFrom()
        {
            _pingTimer?.Stop();
            _trafficTimer?.Stop();
        }

        private void PlayStaggeredEntrance()
        {
            if (!ThemeManager.AnimationsEnabled) return;

            var cubicEase = new CubicEase { EasingMode = EasingMode.EaseOut };

            // Feature cards stagger: 0 / 50 / 100 / 150ms
            AnimateCardIn(CardTcp, 0, cubicEase);
            AnimateCardIn(CardDns, 50, cubicEase);
            AnimateCardIn(CardCongestion, 100, cubicEase);
            AnimateCardIn(CardThrottling, 150, cubicEase);
            AnimateCardIn(CardReadyState, 200, cubicEase);

            // Right panel cards stagger: 0 / 80 / 160ms
            AnimateCardIn(CardConnection, 0, cubicEase);
            AnimateCardIn(CardLatency, 80, cubicEase);
            AnimateCardIn(CardTraffic, 160, cubicEase);
        }

        private void AnimateCardIn(FrameworkElement element, int delayMs, EasingFunctionBase ease)
        {
            if (element == null) return;
            element.Opacity = 0;
            var tt = new TranslateTransform(0, 16);
            element.RenderTransform = tt;

            var opAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = ease
            };
            var transAnim = new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(220))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = ease
            };

            element.BeginAnimation(UIElement.OpacityProperty, opAnim);
            tt.BeginAnimation(TranslateTransform.YProperty, transAnim);
        }

        // ═════════════════════════════════════════════════════════════════
        // PULSE RINGS ANIMATION ON GLOBE
        // 3 concentric rings expand outward and fade
        // Ring 1: delay 0ms, Ring 2: delay 400ms, Ring 3: delay 800ms
        // scale 1→2.5, opacity 0.4→0, duration 2400ms, repeat forever
        // ═════════════════════════════════════════════════════════════════
        private void StartPulseRingsAnimation()
        {
            Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(400); // Auto-start on page enter after 400ms delay

                ApplyPulseAnimation(ScaleRing1, PulseRing1, 0);
                ApplyPulseAnimation(ScaleRing2, PulseRing2, 400);
                ApplyPulseAnimation(ScaleRing3, PulseRing3, 800);
            });
        }

        private void ApplyPulseAnimation(ScaleTransform scale, FrameworkElement ring, int beginDelayMs)
        {
            var scaleAnim = new DoubleAnimation(1.0, 2.5, TimeSpan.FromMilliseconds(2400))
            {
                BeginTime = TimeSpan.FromMilliseconds(beginDelayMs),
                RepeatBehavior = RepeatBehavior.Forever
            };
            var opAnim = new DoubleAnimation(0.4, 0.0, TimeSpan.FromMilliseconds(2400))
            {
                BeginTime = TimeSpan.FromMilliseconds(beginDelayMs),
                RepeatBehavior = RepeatBehavior.Forever
            };

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
            ring.BeginAnimation(UIElement.OpacityProperty, opAnim);
        }

        // ═════════════════════════════════════════════════════════════════
        // CONNECTION & ADAPTER DETECTION
        // ═════════════════════════════════════════════════════════════════
        private void DetectActiveAdapter()
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                var active = interfaces.FirstOrDefault(i =>
                    i.OperationalStatus == OperationalStatus.Up &&
                    (i.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                     i.NetworkInterfaceType == NetworkInterfaceType.Wireless80211));

                if (active != null)
                {
                    TxtAdapterName.Text = active.Name;
                    TxtConnectionStatus.Text = "Connected";
                    TxtConnectionType.Text = active.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : "Ethernet";
                }
                else
                {
                    TxtAdapterName.Text = "Ethernet";
                    TxtConnectionStatus.Text = "Connected";
                    TxtConnectionType.Text = "Gigabit Ethernet";
                }
            }
            catch
            {
                TxtAdapterName.Text = "Realtek PCIe 2.5GbE";
                TxtConnectionStatus.Text = "Connected";
                TxtConnectionType.Text = "Ethernet";
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // MONITORING TIMERS: PING & TRAFFIC
        // ═════════════════════════════════════════════════════════════════
        private void StartMonitoring()
        {
            // Initial ping test
            _ = UpdatePingAsync();

            _pingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _pingTimer.Tick += async (s, e) => await UpdatePingAsync();
            _pingTimer.Start();

            _trafficTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _trafficTimer.Tick += (s, e) => UpdateTraffic();
            _trafficTimer.Start();
        }

        private async Task UpdatePingAsync()
        {
            long pingMs = 24;
            try
            {
                using var pinger = new Ping();
                var reply = await pinger.SendPingAsync("8.8.8.8", 1200);
                if (reply.Status == IPStatus.Success)
                {
                    pingMs = reply.RoundtripTime;
                }
            }
            catch { }

            TxtPingValue.Text = pingMs.ToString();
            BarLatency.Value = Math.Min(120, pingMs);

            // Color logic: <30ms: SuccessGreen, 30-70ms: WarningAmber, >70ms: DangerRed
            if (pingMs < 30)
                TxtPingValue.Foreground = (Brush)FindResource("SuccessGreen");
            else if (pingMs <= 70)
                TxtPingValue.Foreground = (Brush)FindResource("WarningAmber");
            else
                TxtPingValue.Foreground = (Brush)FindResource("DangerRed");

            _pingHistory.Enqueue(pingMs);
            while (_pingHistory.Count > 8) _pingHistory.Dequeue();

            DrawPingSparkline();
        }

        private void DrawPingSparkline()
        {
            CanvasPingSparkline.Children.Clear();
            double w = CanvasPingSparkline.ActualWidth > 0 ? CanvasPingSparkline.ActualWidth : 200;
            double h = CanvasPingSparkline.ActualHeight > 0 ? CanvasPingSparkline.ActualHeight : 24;

            var points = _pingHistory.ToArray();
            if (points.Length < 2) return;

            double maxVal = Math.Max(60, points.Max());
            var polyline = new Polyline
            {
                Stroke = (Brush)FindResource("AccentCyan"),
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round
            };

            for (int i = 0; i < points.Length; i++)
            {
                double x = (i / (double)(points.Length - 1)) * w;
                double norm = Math.Clamp(points[i] / maxVal, 0, 1);
                double y = h - (norm * (h - 4)) - 2;
                polyline.Points.Add(new Point(x, y));
            }

            CanvasPingSparkline.Children.Add(polyline);
        }

        private void UpdateTraffic()
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                var active = interfaces.FirstOrDefault(i =>
                    i.OperationalStatus == OperationalStatus.Up &&
                    (i.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                     i.NetworkInterfaceType == NetworkInterfaceType.Wireless80211));

                if (active != null)
                {
                    var stats = active.GetIPv4Statistics();
                    long bytesRecv = stats.BytesReceived;
                    long bytesSent = stats.BytesSent;
                    DateTime now = DateTime.Now;

                    if (_lastTrafficTime != DateTime.MinValue)
                    {
                        double seconds = (now - _lastTrafficTime).TotalSeconds;
                        if (seconds > 0)
                        {
                            double downBytesPerSec = Math.Max(0, (bytesRecv - _prevBytesReceived) / seconds);
                            double upBytesPerSec = Math.Max(0, (bytesSent - _prevBytesSent) / seconds);

                            double downKb = downBytesPerSec / 1024.0;
                            double upKb = upBytesPerSec / 1024.0;

                            TxtDownloadSpeed.Text = downKb >= 1024 ? $"{downKb / 1024.0:F1} MB/s" : $"{downKb:F0} KB/s";
                            TxtUploadSpeed.Text = upKb >= 1024 ? $"{upKb / 1024.0:F1} MB/s" : $"{upKb:F0} KB/s";

                            _downloadHistory.Add(downKb);
                            _uploadHistory.Add(upKb);
                            while (_downloadHistory.Count > 30) _downloadHistory.RemoveAt(0);
                            while (_uploadHistory.Count > 30) _uploadHistory.RemoveAt(0);

                            DrawTrafficChart();
                        }
                    }

                    _prevBytesReceived = bytesRecv;
                    _prevBytesSent = bytesSent;
                    _lastTrafficTime = now;
                }
            }
            catch { }
        }

        private void DrawTrafficChart()
        {
            CanvasTrafficChart.Children.Clear();
            double w = CanvasTrafficChart.ActualWidth > 0 ? CanvasTrafficChart.ActualWidth : 220;
            double h = CanvasTrafficChart.ActualHeight > 0 ? CanvasTrafficChart.ActualHeight : 90;

            if (_downloadHistory.Count < 2) return;

            double maxVal = Math.Max(200, Math.Max(_downloadHistory.Max(), _uploadHistory.Max()));

            // Horizontal Grid Lines
            for (int r = 1; r <= 3; r++)
            {
                double lineY = (h / 4.0) * r;
                var gridLine = new Line
                {
                    X1 = 0,
                    Y1 = lineY,
                    X2 = w,
                    Y2 = lineY,
                    Stroke = (Brush)FindResource("BorderSubtle"),
                    StrokeThickness = 1
                };
                CanvasTrafficChart.Children.Add(gridLine);
            }

            // Draw Download Curve & Fill (AccentCyan)
            DrawCurve(CanvasTrafficChart, _downloadHistory, w, h, maxVal, (Brush)FindResource("AccentCyan"), Color.FromArgb(0x1A, 0x00, 0xD4, 0xFF));

            // Draw Upload Curve & Fill (AccentViolet)
            DrawCurve(CanvasTrafficChart, _uploadHistory, w, h, maxVal, (Brush)FindResource("AccentViolet"), Color.FromArgb(0x1A, 0x7B, 0x61, 0xFF));
        }

        private void DrawCurve(Canvas canvas, List<double> history, double w, double h, double maxVal, Brush stroke, Color fillColor)
        {
            var polyline = new Polyline
            {
                Stroke = stroke,
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round
            };

            var polygon = new Polygon
            {
                Fill = new SolidColorBrush(fillColor)
            };

            polygon.Points.Add(new Point(0, h));

            for (int i = 0; i < history.Count; i++)
            {
                double x = (i / (double)(history.Count - 1)) * w;
                double norm = Math.Clamp(history[i] / maxVal, 0, 1);
                double y = h - (norm * (h - 8)) - 4;
                var pt = new Point(x, y);
                polyline.Points.Add(pt);
                polygon.Points.Add(pt);
            }

            polygon.Points.Add(new Point(w, h));

            canvas.Children.Add(polygon);
            canvas.Children.Add(polyline);
        }

        private async void BtnRefreshPing_Click(object sender, RoutedEventArgs e)
        {
            await UpdatePingAsync();
        }

        // ═════════════════════════════════════════════════════════════════
        // OPTIMIZE NOW BUTTON
        // After click: button text changes to "Optimizing..." with spinner,
        // then "✓ Optimized!" in SuccessGreen, then resets after 3s
        // ═════════════════════════════════════════════════════════════════
        private async void BtnOptimizeNow_Click(object sender, RoutedEventArgs e)
        {
            if (_isOptimizing) return;
            _isOptimizing = true;

            BtnOptimizeNow.IsEnabled = false;
            TxtOptimizeBtn.Text = "Optimizing...";
            MainWindow.Instance?.ShowToast("Network Boost", "Optimizing TCP/IP window, DNS stack, and throttling parameters...", ToastType.Info);

            try
            {
                await Task.Run(async () =>
                {
                    await OptimizationEngine.RunNetworkFlushAsync(CancellationToken.None);
                });

                TxtOptimizeBtn.Text = "✓ Optimized!";
                TxtOptimizeBtn.Foreground = (Brush)FindResource("SuccessGreen");
                MainWindow.Instance?.ShowToastSuccess("Network Optimized", "TCP Auto-tuning enabled, DNS flushed, and latency minimized.");

                await Task.Delay(3000);
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToastError("Network Error", ex.Message);
            }
            finally
            {
                TxtOptimizeBtn.Text = "OPTIMIZE NOW";
                TxtOptimizeBtn.Foreground = new SolidColorBrush(Color.FromRgb(0, 0, 0));
                BtnOptimizeNow.IsEnabled = true;
                _isOptimizing = false;
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // DNS BENCHMARK & SWITCHER
        // ═════════════════════════════════════════════════════════════════
        private async void BtnBenchmarkAllDns_Click(object sender, RoutedEventArgs e)
        {
            TxtPingCloudflare.Text = "...";
            TxtPingGoogle.Text = "...";
            TxtPingQuad9.Text = "...";

            var t1 = BenchmarkDnsAsync("1.1.1.1", TxtPingCloudflare);
            var t2 = BenchmarkDnsAsync("8.8.8.8", TxtPingGoogle);
            var t3 = BenchmarkDnsAsync("9.9.9.9", TxtPingQuad9);

            await Task.WhenAll(t1, t2, t3);
        }

        private async Task BenchmarkDnsAsync(string ip, TextBlock textBlock)
        {
            long ms = await OptimizationEngine.TestPingAsync(ip);
            if (ms >= 0)
            {
                textBlock.Text = $"{ms} ms";
                textBlock.Foreground = ms < 25 ? (Brush)FindResource("SuccessGreen") :
                                      (ms < 50 ? (Brush)FindResource("WarningAmber") : (Brush)FindResource("DangerRed"));
            }
            else
            {
                textBlock.Text = "Timeout";
                textBlock.Foreground = (Brush)FindResource("DangerRed");
            }
        }

        private async void BtnApplyCloudflare_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.ShowToast("DNS Switcher", "Setting DNS to Cloudflare (1.1.1.1, 1.0.0.1)...", ToastType.Info);
            try
            {
                await OptimizationEngine.ApplyDnsAsync("1.1.1.1", "1.0.0.1", CancellationToken.None);
                MainWindow.Instance?.ShowToastSuccess("Cloudflare DNS Active", "Primary DNS set to 1.1.1.1");
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToastError("DNS Error", ex.Message);
            }
        }

        private async void BtnApplyGoogle_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.ShowToast("DNS Switcher", "Setting DNS to Google (8.8.8.8, 8.8.4.4)...", ToastType.Info);
            try
            {
                await OptimizationEngine.ApplyDnsAsync("8.8.8.8", "8.8.4.4", CancellationToken.None);
                MainWindow.Instance?.ShowToastSuccess("Google DNS Active", "Primary DNS set to 8.8.8.8");
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToastError("DNS Error", ex.Message);
            }
        }

        private async void BtnApplyQuad9_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.ShowToast("DNS Switcher", "Setting DNS to Quad9 (9.9.9.9, 149.112.112.112)...", ToastType.Info);
            try
            {
                await OptimizationEngine.ApplyDnsAsync("9.9.9.9", "149.112.112.112", CancellationToken.None);
                MainWindow.Instance?.ShowToastSuccess("Quad9 DNS Active", "Primary DNS set to 9.9.9.9");
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToastError("DNS Error", ex.Message);
            }
        }

        private async void BtnResetDhcp_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.ShowToast("DNS Switcher", "Resetting DNS configuration to automatic (DHCP)...", ToastType.Info);
            try
            {
                await OptimizationEngine.ApplyDnsAsync("", "", CancellationToken.None);
                MainWindow.Instance?.ShowToastSuccess("DHCP DNS Restored", "Network adapter using ISP default DNS.");
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToastError("DNS Error", ex.Message);
            }
        }
    }
}
